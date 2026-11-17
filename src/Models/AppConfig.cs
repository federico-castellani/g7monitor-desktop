using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace G7MonitorDesktop.Models
{
    public class AppConfig
    {
        /// <summary>Base address of the G7 Monitor Server, e.g. https://glicemie.example.com</summary>
        public string ServerUrl { get; set; }
        /// <summary>Read-only device token ("reader") returned by POST /api/v1/devices.</summary>
        public string DeviceToken { get; set; }
        /// <summary>Display name of the account the token belongs to.</summary>
        public string AccountName { get; set; }
        public int LowThreshold { get; set; }
        public int HighThreshold { get; set; }
        /// <summary>Stored only in the registry Run key (shared with the installer option), never in the file.</summary>
        public bool Autostart { get; set; }
        public int WidgetBackgroundOpacity { get; set; }
        public bool ShowOverFullscreen { get; set; }
        /// <summary>"auto" (Windows display language), "en" or "it".</summary>
        public string Language { get; set; }

        // Unchanged since the app was called "Windows Scout": tokens saved by 2.0 stay readable.
        private static readonly byte[] TokenEntropy = Encoding.UTF8.GetBytes("WindowsScout.G7Monitor.DeviceToken");

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "G7MonitorDesktop";
        private const string LegacyName = "WindowsScout"; // former app name (config folder and Run value)

        private static bool folderSecured;

        public static string ConfigPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "G7MonitorDesktop");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (!folderSecured)
                {
                    folderSecured = true;
                    RestrictToCurrentUser(dir);
                }
                return Path.Combine(dir, "config.json");
            }
        }

        /// <summary>
        /// The settings folder (it holds the encrypted device token) gets its own permissions:
        /// only this Windows account and SYSTEM, nothing inherited from the profile
        /// (other software sometimes adds broad entries there).
        /// </summary>
        private static void RestrictToCurrentUser(string dir)
        {
            try
            {
                const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                    FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(dir, security);
            }
            catch { } // e.g. file system without ACLs: the token is still DPAPI-encrypted
        }

        /// <summary>
        /// One-time move from the "Windows Scout" name: copies the old settings (server link included)
        /// and replaces the old autostart entry. The old folder is removed only after a successful copy.
        /// </summary>
        public static void MigrateFromLegacyName()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string legacyDir = Path.Combine(appData, LegacyName);
                string legacyFile = Path.Combine(legacyDir, "config.json");
                if (File.Exists(legacyFile) && !File.Exists(ConfigPath)) File.Copy(legacyFile, ConfigPath);

                // Removed only once the copy exists. Antivirus scanning can hold the fresh copy's source
                // for a moment, so retry briefly; anything left is cleaned at the next start.
                if (Directory.Exists(legacyDir) && File.Exists(ConfigPath))
                {
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            Directory.Delete(legacyDir, true);
                            break;
                        }
                        catch (IOException) { System.Threading.Thread.Sleep(200); }
                        catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(200); }
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null && key.GetValue(LegacyName) != null)
                    {
                        key.DeleteValue(LegacyName, false);
                        key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                    }
                }
            }
            catch { }
        }

        public AppConfig()
        {
            ServerUrl = "";
            DeviceToken = "";
            AccountName = "";
            LowThreshold = 70;
            HighThreshold = 180;
            Autostart = IsAutostartEnabled();
            WidgetBackgroundOpacity = 0;
            ShowOverFullscreen = false;
            Language = "auto";
        }

        public bool IsPaired
        {
            get { return !string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(DeviceToken); }
        }

        public AppConfig Clone()
        {
            return (AppConfig)MemberwiseClone();
        }

        /// <summary>"low", "normal" or "high" for the current targets.</summary>
        public string CategoryOf(int sgv)
        {
            if (sgv < LowThreshold) return "low";
            if (sgv > HighThreshold) return "high";
            return "normal";
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ConfigPath)) as Dictionary<string, object>;
                    if (data != null) return FromDictionary(data);
                }
            }
            catch { }
            return new AppConfig();
        }

        private static AppConfig FromDictionary(Dictionary<string, object> d)
        {
            AppConfig cfg = new AppConfig();
            cfg.ServerUrl = GetString(d, "server_url");
            cfg.DeviceToken = Unprotect(GetString(d, "device_token"));
            cfg.AccountName = GetString(d, "account_name");
            cfg.LowThreshold = GetInt(d, "low_threshold", 70);
            cfg.HighThreshold = GetInt(d, "high_threshold", 180);
            if (cfg.LowThreshold >= cfg.HighThreshold) { cfg.LowThreshold = 70; cfg.HighThreshold = 180; }
            bool legacyBackground = GetBool(d, "widget_background", false);
            cfg.WidgetBackgroundOpacity = Math.Max(0, Math.Min(100, GetInt(d, "widget_background_opacity", legacyBackground ? 25 : 0)));
            cfg.ShowOverFullscreen = GetBool(d, "show_over_fullscreen", false);
            string lang = GetString(d, "language");
            cfg.Language = lang == "en" || lang == "it" ? lang : "auto";
            return cfg;
        }

        public void Save()
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["server_url"] = ServerUrl ?? "";
                d["device_token"] = Protect(DeviceToken);
                d["account_name"] = AccountName ?? "";
                d["low_threshold"] = LowThreshold;
                d["high_threshold"] = HighThreshold;
                d["widget_background_opacity"] = WidgetBackgroundOpacity;
                d["show_over_fullscreen"] = ShowOverFullscreen;
                d["language"] = Language ?? "auto";

                // Write to a temp file first: a crash mid-write never leaves a truncated config.
                string path = ConfigPath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch { }
            SetAutostart(Autostart);
        }

        /// <summary>The token is stored encrypted with DPAPI (readable only by this Windows user).</summary>
        private static string Protect(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            try
            {
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), TokenEntropy, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(enc);
            }
            catch
            {
                return "";
            }
        }

        private static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("dpapi:")) return "";
            try
            {
                byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(6)), TokenEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch
            {
                return "";
            }
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is string ? (string)v : "";
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null || v is string || v is bool) return def;
            try { return Convert.ToInt32(v); } catch { return def; }
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool ? (bool)v : def;
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key != null) return key.GetValue(RunValue) != null;
                }
            }
            catch { }
            return false;
        }

        public static void SetAutostart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                            key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                        else
                            key.DeleteValue(RunValue, false);
                    }
                }
            }
            catch { }
        }
    }
}
