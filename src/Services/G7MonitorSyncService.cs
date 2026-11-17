using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using G7MonitorDesktop.Models;

namespace G7MonitorDesktop.Services
{
    /// <summary>Connection state; the user interface turns it into text in the current language.</summary>
    public enum SyncState { NotConnected, Connecting, Live, Polling, Unlinked, Offline, ServerError }

    /// <summary>Why linking failed (localized by the user interface).</summary>
    public enum PairError
    {
        None, NotG7Server, Unreachable, UnexpectedResponse, Redirected, WrongCredentials, TotpRequired,
        WrongTotp, TooManyAttempts, PasswordChangeRequired, TooManyDevices, InvalidData, ServerMessage, ConnectionFailed
    }

    public class PairResult
    {
        public bool Ok;
        public string Token;
        public string AccountName;
        public PairError Error;
        /// <summary>Server's own text (for errors without a known code) or the HTTP status.</summary>
        public string Detail;
    }

    /// <summary>
    /// Receives readings from a G7 Monitor Server as a read-only device ("reader"):
    ///  - GET /api/v1/readings/stream?since=  (Server-Sent Events, live) with Bearer token
    ///  - GET /api/v1/readings?since=         (polling fallback for servers without /stream)
    /// The token is obtained once with POST /api/v1/devices (username + password [+ TOTP]).
    /// </summary>
    public class G7MonitorSyncService
    {
        /// <summary>How much history the graph shows (and is requested at startup).</summary>
        public const int HistorySeconds = 3 * 3600;
        /// <summary>The server pings an idle device stream every 270 s: a silent line beyond this is dead.</summary>
        private const int StreamReadTimeoutMs = 330000;
        private const int PollIntervalMs = 60000;
        private const string UserAgent = "G7MonitorDesktop/2.0";

        /// <summary>(history oldest-first, latest or null, delta from the previous reading or null).</summary>
        public event Action<GlucoseEntry[], GlucoseEntry, int?> OnReadingsUpdated;
        /// <summary>(state, seconds before the next attempt for Offline/ServerError).</summary>
        public event Action<SyncState, int> OnStatusChanged;

        private enum Outcome { Closed, Unauthorized, NotFound, Failed }

        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly List<GlucoseEntry> history = new List<GlucoseEntry>();
        private AppConfig config;
        private Thread worker;
        private volatile bool running;
        private int generation;
        private HttpWebRequest currentRequest;
        private bool streamUnsupported;

        static G7MonitorSyncService()
        {
            try
            {
                // .NET Framework 4.x defaults may not include TLS 1.2 on older setups.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch { }
        }

        public G7MonitorSyncService(AppConfig config)
        {
            this.config = config.Clone();
        }

        public void Start()
        {
            running = true;
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "G7MonitorSync" };
            worker.Start();
        }

        public void Stop()
        {
            running = false;
            AbortRequest();
            wake.Set();
        }

        public void UpdateConfig(AppConfig newConfig)
        {
            bool connectionChanged;
            lock (sync)
            {
                connectionChanged = newConfig.ServerUrl != config.ServerUrl || newConfig.DeviceToken != config.DeviceToken;
                config = newConfig.Clone();
                if (connectionChanged)
                {
                    generation++;
                    history.Clear();
                    streamUnsupported = false;
                }
            }

            if (connectionChanged)
            {
                AbortRequest();
                RaiseReadings();
            }
            wake.Set();
        }

        // ------------------------------------------------------------------ worker

        private void WorkerLoop()
        {
            int failures = 0;
            while (running)
            {
                AppConfig cfg;
                int gen;
                bool usePolling;
                lock (sync)
                {
                    cfg = config;
                    gen = generation;
                    usePolling = streamUnsupported;
                }

                if (!cfg.IsPaired)
                {
                    SetStatus(SyncState.NotConnected);
                    wake.WaitOne();
                    continue;
                }

                if (failures == 0 && !usePolling) SetStatus(SyncState.Connecting);

                Outcome outcome = usePolling ? PollOnce(cfg, gen) : RunStream(cfg, gen);
                if (!running) break;
                if (gen != CurrentGeneration())
                {
                    failures = 0;
                    continue;
                }

                switch (outcome)
                {
                    case Outcome.Closed:
                        // Stream ended (server restart, proxy timeout) or a successful poll.
                        failures = 0;
                        wake.WaitOne(usePolling ? PollIntervalMs : 2000);
                        break;

                    case Outcome.NotFound:
                        if (!usePolling)
                        {
                            // Older server without /stream: fall back to periodic requests.
                            lock (sync) { if (gen == generation) streamUnsupported = true; }
                            continue;
                        }
                        failures++;
                        WaitAfterFailure(failures, SyncState.ServerError);
                        break;

                    case Outcome.Unauthorized:
                        // Token revoked from the website or the account was deleted.
                        SetStatus(SyncState.Unlinked);
                        failures = 0;
                        wake.WaitOne(TimeSpan.FromMinutes(10));
                        break;

                    default:
                        failures++;
                        WaitAfterFailure(failures, SyncState.Offline);
                        break;
                }
            }
        }

        private void WaitAfterFailure(int failures, SyncState state)
        {
            // 5 s, 10 s, 20 s ... up to 5 minutes, like the Android app.
            int seconds = (int)Math.Min(300, 5 * Math.Pow(2, Math.Min(failures - 1, 10)));
            SetStatus(state, seconds);
            wake.WaitOne(seconds * 1000);
        }

        private int CurrentGeneration()
        {
            lock (sync) return generation;
        }

        private long SinceParameter()
        {
            lock (sync)
            {
                if (history.Count > 0) return history[history.Count - 1].UnixTime;
            }
            return GlucoseEntry.NowUnix() - HistorySeconds;
        }

        private Outcome RunStream(AppConfig cfg, int gen)
        {
            string url = cfg.ServerUrl + "/api/v1/readings/stream?since=" + SinceParameter();
            HttpWebRequest req = CreateRequest(url, "GET", cfg.DeviceToken);
            req.Accept = "text/event-stream";
            req.ReadWriteTimeout = StreamReadTimeoutMs;
            if (!SetCurrentRequest(req, gen)) return Outcome.Closed;

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), new UTF8Encoding(false)))
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return Outcome.Failed; // e.g. a redirect (not followed)
                    SetStatus(SyncState.Live);
                    StringBuilder data = new StringBuilder();
                    string eventName = null;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!running || gen != CurrentGeneration()) break;

                        if (line.Length == 0)
                        {
                            // Unnamed events are sensor readings; "fingerstick" is never requested.
                            if (data.Length > 0 && (eventName == null || eventName == "message"))
                                HandleRows(data.ToString(), gen);
                            data.Length = 0;
                            eventName = null;
                            continue;
                        }
                        if (line[0] == ':') continue; // keep-alive ping

                        int colon = line.IndexOf(':');
                        string field = colon < 0 ? line : line.Substring(0, colon);
                        string value = colon < 0 ? "" : line.Substring(colon + 1);
                        if (value.StartsWith(" ")) value = value.Substring(1);

                        if (field == "data")
                        {
                            if (data.Length > 0) data.Append('\n');
                            data.Append(value);
                        }
                        else if (field == "event")
                        {
                            eventName = value;
                        }
                    }
                }
                return Outcome.Closed;
            }
            catch (WebException ex)
            {
                return Classify(ex);
            }
            catch
            {
                return Outcome.Failed;
            }
            finally
            {
                ClearCurrentRequest(req);
            }
        }

        private Outcome PollOnce(AppConfig cfg, int gen)
        {
            string url = cfg.ServerUrl + "/api/v1/readings?since=" + SinceParameter();
            HttpWebRequest req = CreateRequest(url, "GET", cfg.DeviceToken);
            req.Accept = "application/json";
            if (!SetCurrentRequest(req, gen)) return Outcome.Closed;

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return Outcome.Failed;
                    var body = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                    object rows;
                    if (body != null && body.TryGetValue("r", out rows)) HandleRows(rows as object[], gen);
                    SetStatus(SyncState.Polling);
                }
                return Outcome.Closed;
            }
            catch (WebException ex)
            {
                return Classify(ex);
            }
            catch
            {
                return Outcome.Failed;
            }
            finally
            {
                ClearCurrentRequest(req);
            }
        }

        private static Outcome Classify(WebException ex)
        {
            HttpWebResponse resp = ex.Response as HttpWebResponse;
            if (resp != null)
            {
                int code = (int)resp.StatusCode;
                resp.Close();
                if (code == 401 || code == 403) return Outcome.Unauthorized;
                if (code == 404) return Outcome.NotFound;
            }
            return Outcome.Failed;
        }

        // ------------------------------------------------------------------ readings

        private void HandleRows(string json, int gen)
        {
            try
            {
                HandleRows(new JavaScriptSerializer().DeserializeObject(json) as object[], gen);
            }
            catch { }
        }

        /// <summary>Rows are [unix seconds, mg/dL, trend x10 | null, source], oldest first.</summary>
        private void HandleRows(object[] rows, int gen)
        {
            if (rows == null || rows.Length == 0) return;
            bool changed = false;

            lock (sync)
            {
                if (gen != generation) return;
                foreach (object o in rows)
                {
                    object[] row = o as object[];
                    if (row == null || row.Length < 3 || row[0] == null || row[1] == null) continue;
                    long ts;
                    int mgdl;
                    try
                    {
                        ts = Convert.ToInt64(row[0]);
                        mgdl = Convert.ToInt32(row[1]);
                    }
                    catch { continue; }
                    double? rate = null;
                    if (row[2] != null)
                    {
                        try { rate = Convert.ToDouble(row[2]) / 10.0; } catch { }
                    }

                    GlucoseEntry entry = new GlucoseEntry(ts, mgdl, rate);
                    int idx = history.FindIndex(e => e.UnixTime >= ts);
                    if (idx < 0) history.Add(entry);
                    else if (history[idx].UnixTime == ts) history[idx] = entry;
                    else history.Insert(idx, entry);
                    changed = true;
                }

                // Keep only the graph window (but never drop the latest reading).
                long cutoff = GlucoseEntry.NowUnix() - HistorySeconds;
                while (history.Count > 1 && history[0].UnixTime < cutoff) history.RemoveAt(0);
            }

            if (changed) RaiseReadings();
        }

        private void RaiseReadings()
        {
            GlucoseEntry[] snapshot;
            lock (sync) snapshot = history.ToArray();

            GlucoseEntry latest = snapshot.Length > 0 ? snapshot[snapshot.Length - 1] : null;
            int? delta = null;
            if (snapshot.Length > 1)
            {
                GlucoseEntry prev = snapshot[snapshot.Length - 2];
                // A delta only makes sense between consecutive readings (G7: every 5 minutes).
                if (latest.UnixTime - prev.UnixTime <= 15 * 60) delta = latest.Sgv - prev.Sgv;
            }

            var handler = OnReadingsUpdated;
            if (handler != null) handler(snapshot, latest, delta);
        }

        private void SetStatus(SyncState state, int retrySeconds = 0)
        {
            var handler = OnStatusChanged;
            if (handler != null) handler(state, retrySeconds);
        }

        // ------------------------------------------------------------------ HTTP helpers

        private bool SetCurrentRequest(HttpWebRequest req, int gen)
        {
            lock (sync)
            {
                if (gen != generation || !running) return false;
                currentRequest = req;
                return true;
            }
        }

        private void ClearCurrentRequest(HttpWebRequest req)
        {
            lock (sync)
            {
                if (currentRequest == req) currentRequest = null;
            }
        }

        private void AbortRequest()
        {
            HttpWebRequest req;
            lock (sync) req = currentRequest;
            if (req != null)
            {
                try { req.Abort(); } catch { }
            }
        }

        private static HttpWebRequest CreateRequest(string url, string method, string token)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = UserAgent;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 30000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            // Never follow redirects: credentials and the token go only to the address the user typed
            // (a redirect could otherwise move them to another host or to plain http).
            req.AllowAutoRedirect = false;
            if (!string.IsNullOrEmpty(token)) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            return req;
        }

        /// <summary>
        /// Accepts "glicemie.example.com", "https://glicemie.example.com/" etc. Returns null when invalid.
        /// Plain http is allowed only for the local machine: the password would otherwise travel in clear.
        /// </summary>
        public static string NormalizeServerUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string s = input.Trim();
            if (s.IndexOf("://", StringComparison.Ordinal) < 0) s = "https://" + s;

            Uri uri;
            if (!Uri.TryCreate(s, UriKind.Absolute, out uri)) return null;
            bool local = uri.IsLoopback;
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local)) return null;
            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;

            return (uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath).TrimEnd('/');
        }

        /// <summary>
        /// Links this PC as a read-only device. Blocking: call it from a background thread.
        /// The password is used only for this request and never stored.
        /// </summary>
        public static PairResult Pair(string serverUrl, string username, string password, string totp)
        {
            // 1. Make sure the address is a G7 Monitor Server before sending any credentials.
            try
            {
                HttpWebRequest info = CreateRequest(serverUrl + "/api/v1/info", "GET", null);
                using (HttpWebResponse resp = (HttpWebResponse)info.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    if (IsRedirect(resp.StatusCode)) return Fail(PairError.Redirected);
                    var body = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                    object service;
                    if (resp.StatusCode != HttpStatusCode.OK || body == null || !body.TryGetValue("service", out service) || (service as string) != "g7monitor")
                        return Fail(PairError.NotG7Server);
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null) { ex.Response.Close(); return Fail(PairError.NotG7Server); }
                return Fail(PairError.Unreachable);
            }
            catch
            {
                return Fail(PairError.NotG7Server);
            }

            // 2. Pair as "reader": works for read-only accounts too.
            var payload = new Dictionary<string, object>();
            payload["username"] = username;
            payload["password"] = password;
            if (!string.IsNullOrEmpty(totp)) payload["totp"] = totp;
            string name = "G7 Monitor Desktop · " + Environment.MachineName;
            payload["name"] = name.Length > 60 ? name.Substring(0, 60) : name;
            payload["kind"] = "reader";

            try
            {
                HttpWebRequest req = CreateRequest(serverUrl + "/api/v1/devices", "POST", null);
                req.ContentType = "application/json";
                req.Accept = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(payload));
                payload.Clear();
                req.ContentLength = bytes.Length;
                try
                {
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length); // do not leave the password in this buffer
                }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    if (IsRedirect(resp.StatusCode)) return Fail(PairError.Redirected);
                    var body = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                    object token, display, user;
                    if (body == null || !body.TryGetValue("token", out token) || !(token is string))
                        return Fail(PairError.UnexpectedResponse);
                    string account = body.TryGetValue("displayName", out display) && display is string ? (string)display
                        : body.TryGetValue("username", out user) && user is string ? (string)user : username;
                    return new PairResult { Ok = true, Token = (string)token, AccountName = account };
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp == null) return Fail(PairError.Unreachable);
                try
                {
                    using (resp)
                    using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        var body = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                        object error, message;
                        string code = body != null && body.TryGetValue("error", out error) ? error as string : null;
                        string msg = body != null && body.TryGetValue("message", out message) ? message as string : null;
                        return Describe(code, msg, (int)resp.StatusCode);
                    }
                }
                catch
                {
                    return Fail(PairError.ServerMessage, ((int)resp.StatusCode).ToString());
                }
            }
            catch
            {
                return Fail(PairError.ConnectionFailed);
            }
        }

        private static bool IsRedirect(HttpStatusCode code)
        {
            return (int)code >= 300 && (int)code < 400;
        }

        /// <summary>Maps the server error codes (src/app.ts, src/auth.ts) to errors the UI can translate.</summary>
        private static PairResult Describe(string code, string message, int status)
        {
            switch (code)
            {
                case "credenziali_non_valide": return Fail(PairError.WrongCredentials);
                case "totp_richiesto": return Fail(PairError.TotpRequired);
                case "codice_non_valido": return Fail(PairError.WrongTotp);
                case "troppi_tentativi": return Fail(PairError.TooManyAttempts);
                case "cambio_password_richiesto": return Fail(PairError.PasswordChangeRequired);
                case "troppi_dispositivi": return Fail(PairError.TooManyDevices);
                case "richiesta_non_valida": return Fail(PairError.InvalidData);
            }
            return Fail(PairError.ServerMessage, !string.IsNullOrEmpty(message) ? message : status.ToString());
        }

        private static PairResult Fail(PairError error, string detail = null)
        {
            return new PairResult { Ok = false, Error = error, Detail = detail };
        }

        /// <summary>Best effort: revokes this PC's token on the server (DELETE /api/v1/devices/self).</summary>
        public static void Unpair(string serverUrl, string token)
        {
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(token)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    HttpWebRequest req = CreateRequest(serverUrl + "/api/v1/devices/self", "DELETE", token);
                    using (req.GetResponse()) { }
                }
                catch (WebException ex)
                {
                    if (ex.Response != null) ex.Response.Close();
                }
                catch { }
            });
        }
    }
}
