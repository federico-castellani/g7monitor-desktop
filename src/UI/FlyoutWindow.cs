using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;
using G7MonitorDesktop.Interop;
using G7MonitorDesktop.Models;
using G7MonitorDesktop.Localization;
using G7MonitorDesktop.Services;

namespace G7MonitorDesktop.UI
{
    /// <summary>
    /// Windows 11 style flyout (like Quick Settings / Calendar): reading, 3-hour graph, statistics,
    /// and a drill-in settings page. Everything is drawn in 96-DPI units and scaled to the screen DPI.
    /// </summary>
    public class FlyoutWindow : Form
    {
        // ------------------------------------------------------------------ layout (96-DPI units)
        private const int W = 340;
        private const int H = 522;
        private const int FooterH = 52;
        private const int Pad = 16;
        private const int FooterY = H - FooterH;

        private static readonly RectangleF GraphCard = new RectangleF(Pad, 96, W - 2 * Pad, 290);
        private static readonly RectangleF BoxServer = new RectangleF(28, 94, 284, 32);
        private static readonly RectangleF BoxUser = new RectangleF(28, 132, 139, 32);
        private static readonly RectangleF BoxPassword = new RectangleF(173, 132, 139, 32);
        private static readonly RectangleF BoxTotp = new RectangleF(28, 170, 100, 32);

        // ------------------------------------------------------------------ state
        private readonly AppConfig config;
        private readonly float scale;
        private Palette P;

        private GlucoseEntry[] history = new GlucoseEntry[0];
        private GlucoseEntry current;
        private int? currentDelta;
        private SyncState state = SyncState.Connecting;
        private int retrySeconds;

        // settings being edited (applied on Save)
        private int editLow, editHigh, editOpacity;
        private bool editAutostart, editFullscreen;
        private string editLanguage = Strings.Auto;
        private float knobAutostart, knobFullscreen;

        // drill-in between the main page (0) and settings (1)
        private bool settingsView;
        private float viewT;
        private Bitmap bufferMain, bufferSettings;

        // window entrance / exit
        public bool IsClosing { get; private set; }
        private bool winAnimating, winEntering;
        private int winFrame, winX, winFromY, winToY;
        private DateTime lastHidden = DateTime.MinValue;
        private const int EnterFrames = 16, ExitFrames = 9;

        // interaction: every clickable element registers a hit region while it is drawn
        private sealed class Hit
        {
            public string Id;
            public RectangleF Rect;
            public Action Click;
            public Action<float> Drag; // receives the x coordinate (96-DPI units)
        }
        private readonly List<Hit> hits = new List<Hit>();
        private bool registerHits;
        private string hoverId, pressedId;
        private Hit dragging;

        // graph hover
        private RectangleF plotRect;
        private long plotStart, plotEnd;
        private int graphHover = -1;

        // account
        private TextBox txtServer, txtUser, txtPassword, txtTotp;
        private bool isPairing;
        private string pairMessage;
        private bool pairError;

        private readonly System.Windows.Forms.Timer anim;

        public event Action<AppConfig> ConfigSaved;
        public event Action ExitRequested;

        public bool IsRecentlyClosed
        {
            get { return (DateTime.UtcNow - lastHidden).TotalMilliseconds < 150; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= NativeMethods.CS_DROPSHADOW;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public FlyoutWindow(AppConfig config)
        {
            this.config = config;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            scale = Theme.DpiScale;
            ClientSize = new Size(S(W), S(H));

            CreateInputs();
            ApplyTheme();

            anim = new System.Windows.Forms.Timer { Interval = 15 };
            anim.Tick += OnAnimationTick;

            Deactivate += (s, e) =>
            {
                lastHidden = DateTime.UtcNow;
                HideAnimated();
            };
        }

        private int S(float v) { return (int)Math.Round(v * scale); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anim.Dispose();
                if (bufferMain != null) bufferMain.Dispose();
                if (bufferSettings != null) bufferSettings.Dispose();
            }
            base.Dispose(disposing);
        }

        // ================================================================== public API

        public void UpdateReadings(GlucoseEntry[] readings, GlucoseEntry latest, int? delta)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<GlucoseEntry[], GlucoseEntry, int?>(UpdateReadings), readings, latest, delta);
                return;
            }
            history = readings ?? new GlucoseEntry[0];
            current = latest;
            currentDelta = delta;
            graphHover = -1;
            Invalidate();
        }

        public void UpdateStatus(SyncState newState, int retry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<SyncState, int>(UpdateStatus), newState, retry);
                return;
            }
            state = newState;
            retrySeconds = retry;
            Invalidate();
        }

        /// <summary>Opens above (or below) the taskbar widget, aligned like the system flyouts.</summary>
        public void ShowNear(Rectangle anchor)
        {
            Theme.Refresh();
            ApplyTheme();

            Rectangle wa = Screen.FromRectangle(anchor).WorkingArea;
            int margin = S(12);
            winX = Math.Max(wa.Left + margin, Math.Min(anchor.Left + anchor.Width / 2 - Width / 2, wa.Right - Width - margin));

            bool taskbarAtTop = anchor.Top < wa.Top + wa.Height / 2;
            winToY = taskbarAtTop ? wa.Top + margin : wa.Bottom - Height - margin;
            winFromY = winToY + (taskbarAtTop ? -S(24) : S(24));

            IsClosing = false;
            winEntering = true;
            winAnimating = true;
            winFrame = 0;
            Opacity = 0;
            Location = new Point(winX, winFromY);
            Show();
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, winX, winFromY, 0, 0, NativeMethods.SWP_NOSIZE);
            Activate();
            anim.Start();
        }

        public void HideAnimated()
        {
            if (IsClosing || !Visible) return;
            IsClosing = true;
            winEntering = false;
            winAnimating = true;
            winFrame = 0;
            winFromY = Top;
            winToY = Top + (winToY < winFromY ? -S(12) : S(12)); // slide back towards the taskbar
            anim.Start();
        }

        // ================================================================== theme & inputs

        private void ApplyTheme()
        {
            P = Theme.Current;
            BackColor = P.Surface;
            NativeMethods.ApplyWindowFrame(Handle, P.IsDark, P.IsDark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(222, 222, 222));
            foreach (TextBox t in Inputs())
            {
                t.BackColor = P.ControlFill;
                t.ForeColor = P.TextPrimary;
            }
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SETTINGCHANGE || m.Msg == NativeMethods.WM_THEMECHANGED ||
                m.Msg == NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                Theme.Refresh();
                ApplyTheme();
            }
            base.WndProc(ref m);
        }

        private void CreateInputs()
        {
            txtServer = CreateInput(BoxServer, 200);
            txtUser = CreateInput(BoxUser, 64);
            txtPassword = CreateInput(BoxPassword, 1024, true);
            txtTotp = CreateInput(BoxTotp, 6);
            ApplyLanguage();
        }

        private TextBox CreateInput(RectangleF box, int maxLength, bool password = false)
        {
            Font font = new Font(Theme.Text(14).FontFamily, 14 * scale, GraphicsUnit.Pixel);
            TextBox t = new TextBox
            {
                Font = font,
                BorderStyle = BorderStyle.None,
                MaxLength = maxLength,
                UseSystemPasswordChar = password, // before the cue banner: changing it recreates the handle
                Visible = false
            };
            t.Width = S(box.Width - 22);
            t.Location = new Point(S(box.X + 11), S(box.Y) + (S(box.Height) - t.Height) / 2);
            t.GotFocus += (s, e) => Invalidate();
            t.LostFocus += (s, e) => Invalidate();
            t.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                StartPairing();
            };
            Controls.Add(t);
            return t;
        }

        /// <summary>Texts that live in native controls (the rest is read at paint time).</summary>
        public void ApplyLanguage()
        {
            Strings L = Strings.Current;
            NativeMethods.SetCueBanner(txtServer, "glicemie.example.com");
            NativeMethods.SetCueBanner(txtUser, L.Username);
            NativeMethods.SetCueBanner(txtPassword, L.Password);
            NativeMethods.SetCueBanner(txtTotp, L.TotpCode);
            Invalidate();
        }

        /// <summary>The password (and 2FA code) never outlive the settings page.</summary>
        private void ClearSecrets()
        {
            txtPassword.Text = "";
            txtTotp.Text = "";
            txtPassword.ClearUndo();
        }

        private TextBox[] Inputs()
        {
            return txtServer == null ? new TextBox[0] : new[] { txtServer, txtUser, txtPassword, txtTotp };
        }

        /// <summary>Native text boxes exist only on the settings page, when it is fully in place.</summary>
        private void UpdateInputVisibility()
        {
            bool show = settingsView && viewT >= 1f && !config.IsPaired;
            foreach (TextBox t in Inputs())
            {
                t.Visible = show;
                t.ReadOnly = isPairing;
            }
            if (!show && ContainsFocus) Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (settingsView) SetSettingsView(false);
                else HideAnimated();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================================================================== animation (one timer for everything)

        private void OnAnimationTick(object sender, EventArgs e)
        {
            bool busy = false;

            if (winAnimating)
            {
                winFrame++;
                int total = winEntering ? EnterFrames : ExitFrames;
                float t = Math.Min(1f, (float)winFrame / total);
                // Windows 11 motion: decelerate on entrance, accelerate on exit
                float ease = winEntering ? 1f - (float)Math.Pow(1f - t, 3) : t * t;
                Location = new Point(winX, (int)Math.Round(winFromY + (winToY - winFromY) * ease));
                Opacity = winEntering ? ease : 1f - ease;

                if (t >= 1f)
                {
                    winAnimating = false;
                    if (!winEntering) FinishHide();
                }
                else busy = true;
            }

            float target = settingsView ? 1f : 0f;
            if (viewT != target)
            {
                viewT += (target - viewT) * 0.3f;
                if (Math.Abs(target - viewT) < 0.01f)
                {
                    viewT = target;
                    UpdateInputVisibility();
                    if (settingsView && !config.IsPaired) (txtServer.Text.Trim().Length == 0 ? txtServer : txtUser).Focus();
                }
                else busy = true;
                Invalidate();
            }

            busy |= Approach(ref knobAutostart, editAutostart ? 1f : 0f);
            busy |= Approach(ref knobFullscreen, editFullscreen ? 1f : 0f);

            if (!busy) anim.Stop();
        }

        private bool Approach(ref float value, float target)
        {
            if (value == target) return false;
            value += (target - value) * 0.35f;
            if (Math.Abs(target - value) < 0.02f) value = target;
            Invalidate();
            return value != target;
        }

        private void FinishHide()
        {
            Hide();
            Opacity = 1;
            IsClosing = false;
            lastHidden = DateTime.UtcNow;

            // Like the system flyouts, the next opening starts from the main page.
            settingsView = false;
            viewT = 0;
            hoverId = pressedId = null;
            graphHover = -1;
            if (!isPairing) ClearSecrets();
            UpdateInputVisibility();
        }

        private void SetSettingsView(bool show)
        {
            if (settingsView == show) return;
            settingsView = show;
            if (show)
            {
                editLow = config.LowThreshold;
                editHigh = config.HighThreshold;
                editOpacity = config.WidgetBackgroundOpacity;
                editAutostart = config.Autostart;
                editFullscreen = config.ShowOverFullscreen;
                knobAutostart = editAutostart ? 1 : 0;
                knobFullscreen = editFullscreen ? 1 : 0;
                editLanguage = config.Language;
                if (config.IsPaired || txtServer.Text.Trim().Length == 0) txtServer.Text = config.ServerUrl;
                if (!isPairing) pairMessage = null;
            }
            else if (!isPairing)
            {
                ClearSecrets();
            }
            hoverId = pressedId = null;
            graphHover = -1;
            UpdateInputVisibility();
            anim.Start();
        }

        // ================================================================== mouse

        private PointF Logical(Point p) { return new PointF(p.X / scale, p.Y / scale); }

        private Hit HitAt(PointF p)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Rect.Contains(p)) return hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PointF p = Logical(e.Location);

            if (dragging != null)
            {
                dragging.Drag(p.X);
                Invalidate();
                return;
            }

            Hit h = HitAt(p);
            string id = h != null ? h.Id : null;
            if (id != hoverId)
            {
                hoverId = id;
                Invalidate();
            }

            int idx = !settingsView && viewT == 0f && plotRect.Contains(p) ? NearestReading(p.X) : -1;
            if (idx != graphHover)
            {
                graphHover = idx;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverId != null || graphHover >= 0)
            {
                hoverId = null;
                graphHover = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Hit h = HitAt(Logical(e.Location));
            pressedId = h != null ? h.Id : null;
            if (h != null && h.Drag != null)
            {
                dragging = h;
                h.Drag(Logical(e.Location).X);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Hit h = HitAt(Logical(e.Location));
            bool click = dragging == null && h != null && h.Id == pressedId && h.Click != null;
            dragging = null;
            pressedId = null;
            Invalidate();
            if (click) h.Click();
        }

        private void AddHit(string id, RectangleF rect, Action click, Action<float> drag = null)
        {
            if (registerHits) hits.Add(new Hit { Id = id, Rect = rect, Click = click, Drag = drag });
        }

        private bool IsHover(string id) { return hoverId == id; }
        private bool IsPressed(string id) { return pressedId == id && hoverId == id; }

        // ================================================================== painting

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            hits.Clear();

            if (viewT <= 0f || viewT >= 1f)
            {
                registerHits = true;
                Setup(g);
                g.ScaleTransform(scale, scale);
                DrawPage(g, viewT >= 1f);
                registerHits = false;
                return;
            }

            // Drill-in transition: the old page fades out, the new one slides in from the right.
            RenderPage(ref bufferMain, false);
            RenderPage(ref bufferSettings, true);
            float t = viewT;
            g.Clear(P.Surface);
            DrawFaded(g, bufferMain, 1f - t, -S(24) * t);
            DrawFaded(g, bufferSettings, t, S(40) * (1f - t));
        }

        private void RenderPage(ref Bitmap buffer, bool settings)
        {
            if (buffer == null || buffer.Width != ClientSize.Width || buffer.Height != ClientSize.Height)
            {
                if (buffer != null) buffer.Dispose();
                buffer = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            }
            using (Graphics g = Graphics.FromImage(buffer))
            {
                Setup(g);
                g.ScaleTransform(scale, scale);
                DrawPage(g, settings);
            }
        }

        private static void DrawFaded(Graphics g, Bitmap bmp, float alpha, float offsetX)
        {
            if (alpha <= 0.01f) return;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Min(1f, alpha) });
                g.DrawImage(bmp, new Rectangle((int)offsetX, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            // Opaque surface: ClearType gives the sharpest text, exactly like native Windows text.
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        private void DrawPage(Graphics g, bool settings)
        {
            g.Clear(P.Surface);
            if (settings) DrawSettingsPage(g);
            else DrawMainPage(g);
        }

        // ------------------------------------------------------------------ main page

        private void DrawMainPage(Graphics g)
        {
            bool stale = current != null && current.IsStale;
            string category = current != null ? config.CategoryOf(current.Sgv) : "normal";
            Color catColor = current == null || stale ? P.TextTertiary : P.Category(category);

            // Header: value, unit, trend arrow
            float baseline = 58;
            float x = 20;
            x += Theme.DrawOnBaseline(g, current != null ? current.Sgv.ToString() : "--", Theme.Display(40), stale ? P.TextSecondary : P.TextPrimary, x, baseline);
            x += 6;
            x += Theme.DrawOnBaseline(g, "mg/dL", Theme.Text(14), P.TextSecondary, x, baseline);
            if (current != null && !stale && current.TrendRate.HasValue)
                Theme.DrawTrendArrow(g, x + 22, baseline - 13, 20, current.TrendRate.Value, catColor);

            // Status pill (like an InfoBadge)
            if (current != null)
            {
                Strings L = Strings.Current;
                string label = stale ? L.OldReading : category == "low" ? L.Low : category == "high" ? L.High : L.InRange;
                Font f = Theme.Text(12, true);
                float w = Theme.MeasureWidth(g, label, f) + 20;
                RectangleF pill = new RectangleF(W - Pad - w, 26, w, 24);
                Theme.FillRounded(g, Color.FromArgb(P.IsDark ? 38 : 26, catColor), pill, 12);
                Theme.DrawText(g, label, f, catColor, pill, StringAlignment.Center);
            }

            // Subtitle: delta and age, or connection status
            string sub;
            if (current != null)
            {
                string d = currentDelta.HasValue ? string.Format("{0}{1} mg/dL", currentDelta.Value > 0 ? "+" : currentDelta.Value < 0 ? "−" : "±", Math.Abs(currentDelta.Value)) : "Δ --";
                sub = d + "  ·  " + FormatAge(current);
            }
            else
            {
                sub = config.IsPaired ? Strings.Current.Status(state, retrySeconds) : Strings.Current.NotConnectedToServer;
            }
            Theme.DrawOnBaseline(g, sub, Theme.Text(12), P.TextSecondary, 20, 82);

            DrawGraph(g, GraphCard);
            DrawStats(g, new RectangleF(Pad, GraphCard.Bottom + 8, W - 2 * Pad, 64));
            DrawMainFooter(g);
        }

        private static string FormatAge(GlucoseEntry e)
        {
            Strings L = Strings.Current;
            TimeSpan age = e.Age;
            if (age.TotalMinutes < 1) return L.JustNow;
            if (age.TotalHours < 1) return L.Format(L.MinutesAgo, (int)age.TotalMinutes);
            if (age.TotalDays < 1) return L.Format(L.HoursMinutesAgo, (int)age.TotalHours, age.Minutes);
            return e.Timestamp.ToString(L.DateFormat, L.Culture);
        }

        private void DrawGraph(Graphics g, RectangleF card)
        {
            Theme.FillRounded(g, P.CardFill, P.CardStroke, card, Theme.OverlayRadius);
            Theme.DrawText(g, Strings.Current.Last3Hours, Theme.Text(12, true), P.TextSecondary, new RectangleF(card.X + 12, card.Y + 6, 200, 24));

            plotRect = new RectangleF(card.X + 12, card.Y + 38, card.Width - 46, card.Height - 64);
            if (history.Length == 0)
            {
                Strings L = Strings.Current;
                string msg = !config.IsPaired ? L.OpenSettingsToConnect
                    : state == SyncState.Live || state == SyncState.Polling ? L.NoReadings3h : L.WaitingForData;
                Theme.DrawText(g, msg, Theme.Text(12), P.TextTertiary, new RectangleF(card.X + 12, card.Y, card.Width - 24, card.Height), StringAlignment.Center);
                plotRect = RectangleF.Empty;
                return;
            }

            RectangleF pr = plotRect;
            plotEnd = Math.Max(GlucoseEntry.NowUnix(), history[history.Length - 1].UnixTime);
            plotStart = plotEnd - G7MonitorSyncService.HistorySeconds;

            int lo = config.LowThreshold, hi = config.HighThreshold;
            foreach (GlucoseEntry e in history)
            {
                if (e.UnixTime < plotStart) continue;
                lo = Math.Min(lo, e.Sgv);
                hi = Math.Max(hi, e.Sgv);
            }
            lo = Math.Max(40, (lo - 15) / 10 * 10);
            hi = (hi + 25) / 10 * 10;
            float range = hi - lo;

            Func<long, float> X = ts => pr.X + (float)(ts - plotStart) / (plotEnd - plotStart) * pr.Width;
            Func<int, float> Y = v => pr.Bottom - (v - lo) / range * pr.Height;

            // Target range band
            float yHigh = Y(config.HighThreshold), yLow = Y(config.LowThreshold);
            using (SolidBrush band = new SolidBrush(Color.FromArgb(P.IsDark ? 12 : 16, P.Success)))
                g.FillRectangle(band, pr.X, yHigh, pr.Width, yLow - yHigh);

            Font small = Theme.Text(11);
            using (Pen grid = new Pen(P.GridLine, 1f))
            using (Pen limit = new Pen(Color.FromArgb(90, P.Success), 1f) { DashStyle = DashStyle.Dash })
            {
                // Full hours as vertical grid lines with clock labels
                DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(plotStart).ToLocalTime();
                DateTime hour = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Local).AddHours(1);
                for (; ; hour = hour.AddHours(1))
                {
                    long ts = (long)(hour.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    if (ts > plotEnd) break;
                    float hx = X(ts);
                    g.DrawLine(grid, hx, pr.Y - 4, hx, pr.Bottom);
                    Theme.DrawText(g, hour.ToString("HH:mm"), small, P.TextTertiary, new RectangleF(hx - 30, pr.Bottom + 4, 60, 16), StringAlignment.Center);
                }

                g.DrawLine(limit, pr.X, yHigh, pr.Right, yHigh);
                g.DrawLine(limit, pr.X, yLow, pr.Right, yLow);
                Theme.DrawText(g, config.HighThreshold.ToString(), small, P.TextTertiary, new RectangleF(pr.Right + 4, yHigh - 8, 30, 16));
                Theme.DrawText(g, config.LowThreshold.ToString(), small, P.TextTertiary, new RectangleF(pr.Right + 4, yLow - 8, 30, 16));
            }

            // Thin connecting line (broken over gaps longer than 15 minutes), then one dot per reading.
            using (Pen line = new Pen(Color.FromArgb(P.IsDark ? 70 : 60, P.TextPrimary), 1.5f) { LineJoin = LineJoin.Round })
            {
                List<PointF> seg = new List<PointF>();
                long prev = long.MinValue;
                foreach (GlucoseEntry e in history)
                {
                    if (e.UnixTime < plotStart) continue;
                    if (e.UnixTime - prev > 15 * 60 && seg.Count > 0)
                    {
                        if (seg.Count > 1) g.DrawLines(line, seg.ToArray());
                        seg.Clear();
                    }
                    seg.Add(new PointF(X(e.UnixTime), Y(e.Sgv)));
                    prev = e.UnixTime;
                }
                if (seg.Count > 1) g.DrawLines(line, seg.ToArray());
            }

            for (int i = 0; i < history.Length; i++)
            {
                GlucoseEntry e = history[i];
                if (e.UnixTime < plotStart) continue;
                bool last = i == history.Length - 1;
                bool hovered = i == graphHover;
                Color c = P.Category(config.CategoryOf(e.Sgv));
                if (last && e.IsStale) c = P.TextTertiary;
                float r = last || hovered ? 4.5f : 2.75f;
                float cx = X(e.UnixTime), cy = Y(e.Sgv);
                if (last || hovered)
                {
                    using (SolidBrush halo = new SolidBrush(Color.FromArgb(60, c)))
                        g.FillEllipse(halo, cx - r - 3, cy - r - 3, (r + 3) * 2, (r + 3) * 2);
                }
                using (SolidBrush dot = new SolidBrush(c))
                    g.FillEllipse(dot, cx - r, cy - r, r * 2, r * 2);
            }

            if (graphHover >= 0 && graphHover < history.Length) DrawGraphTooltip(g, history[graphHover], X, Y, card);
        }

        private void DrawGraphTooltip(Graphics g, GlucoseEntry e, Func<long, float> X, Func<int, float> Y, RectangleF card)
        {
            float cx = X(e.UnixTime), cy = Y(e.Sgv);
            using (Pen guide = new Pen(Color.FromArgb(70, P.TextPrimary), 1f) { DashStyle = DashStyle.Dot })
                g.DrawLine(guide, cx, plotRect.Y - 4, cx, plotRect.Bottom);

            string value = e.Sgv + " mg/dL";
            string time = e.Timestamp.ToString("HH:mm");
            Font fv = Theme.Text(12, true), ft = Theme.Text(12);
            float w = Theme.MeasureWidth(g, value, fv) + Theme.MeasureWidth(g, time, ft) + 26;
            RectangleF tip = new RectangleF(cx - w / 2, cy - 40, w, 28);
            if (tip.Y < card.Y + 4) tip.Y = cy + 12;
            tip.X = Math.Max(card.X + 4, Math.Min(tip.X, card.Right - w - 4));

            Theme.FillRounded(g, P.IsDark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(252, 252, 252), P.ControlStroke, tip, Theme.ControlRadius);
            float tx = tip.X + 10, baseline = tip.Y + 18.5f;
            tx += Theme.DrawOnBaseline(g, value, fv, P.TextPrimary, tx, baseline) + 6;
            Theme.DrawOnBaseline(g, time, ft, P.TextSecondary, tx, baseline);
        }

        private int NearestReading(float x)
        {
            if (history.Length == 0 || plotRect.Width <= 0) return -1;
            int best = -1;
            float bestDist = 12f; // pointer must be within 12px of a reading
            for (int i = 0; i < history.Length; i++)
            {
                if (history[i].UnixTime < plotStart) continue;
                float px = plotRect.X + (float)(history[i].UnixTime - plotStart) / (plotEnd - plotStart) * plotRect.Width;
                float d = Math.Abs(px - x);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private void DrawStats(Graphics g, RectangleF area)
        {
            int n = 0, inRange = 0, sum = 0, min = int.MaxValue, max = int.MinValue;
            long cutoff = GlucoseEntry.NowUnix() - G7MonitorSyncService.HistorySeconds;
            foreach (GlucoseEntry e in history)
            {
                if (e.UnixTime < cutoff) continue;
                n++;
                sum += e.Sgv;
                if (e.Sgv >= config.LowThreshold && e.Sgv <= config.HighThreshold) inRange++;
                min = Math.Min(min, e.Sgv);
                max = Math.Max(max, e.Sgv);
            }

            string[] labels = { Strings.Current.StatInRange, Strings.Current.StatAverage, Strings.Current.StatMinMax };
            string[] values = n == 0 ? new[] { "--", "--", "--" } : new[]
            {
                (int)Math.Round(100.0 * inRange / n) + "%",
                ((int)Math.Round((double)sum / n)).ToString(),
                min + " – " + max
            };

            float gap = 8, w = (area.Width - 2 * gap) / 3;
            for (int i = 0; i < 3; i++)
            {
                RectangleF tile = new RectangleF(area.X + i * (w + gap), area.Y, w, area.Height);
                Theme.FillRounded(g, P.CardFill, P.CardStroke, tile, Theme.OverlayRadius);
                Theme.DrawText(g, labels[i], Theme.Text(12), P.TextSecondary, new RectangleF(tile.X + 12, tile.Y + 8, w - 16, 20));
                Theme.DrawText(g, values[i], Theme.Text(16, true), P.TextPrimary, new RectangleF(tile.X + 12, tile.Y + 30, w - 16, 26));
            }
        }

        private void DrawFooterLayer(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(P.Layer)) g.FillRectangle(b, 0, FooterY, W, FooterH);
            using (Pen p = new Pen(P.Stroke, 1f)) g.DrawLine(p, 0, FooterY + 0.5f, W, FooterY + 0.5f);
        }

        private void DrawMainFooter(Graphics g)
        {
            DrawFooterLayer(g);

            Strings L = Strings.Current;
            bool live = state == SyncState.Live || state == SyncState.Polling;
            bool bad = !config.IsPaired || state == SyncState.Unlinked || state == SyncState.Offline ||
                       state == SyncState.ServerError || state == SyncState.NotConnected;
            Color dot = bad ? P.Critical : live ? P.Success : P.Caution;
            string text = !config.IsPaired ? L.StatusNotConnected : L.Status(state, retrySeconds);
            if (config.IsPaired && live && !string.IsNullOrEmpty(config.AccountName)) text += " · " + config.AccountName;

            float cy = FooterY + FooterH / 2f;
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, Pad + 2, cy - 4, 8, 8);
            float buttonsLeft = W - 12 - 36 - (config.IsPaired ? 40 : 0);
            Theme.DrawText(g, text, Theme.Text(12), P.TextSecondary, new RectangleF(Pad + 18, FooterY, buttonsLeft - Pad - 22, FooterH));

            RectangleF gear = new RectangleF(W - 12 - 36, cy - 18, 36, 36);
            DrawIconButton(g, "settings", gear, Theme.GlyphSettings, () => SetSettingsView(true));
            if (config.IsPaired)
            {
                RectangleF web = new RectangleF(gear.X - 40, gear.Y, 36, 36);
                DrawIconButton(g, "web", web, Theme.GlyphGlobe, OpenWebsite);
            }
        }

        private void OpenWebsite()
        {
            if (string.IsNullOrEmpty(config.ServerUrl)) return;
            try { Process.Start(config.ServerUrl); } catch { }
            HideAnimated();
        }

        // ------------------------------------------------------------------ settings page

        private void DrawSettingsPage(Graphics g)
        {
            DrawIconButton(g, "back", new RectangleF(12, 12, 36, 36), Theme.GlyphBack, () => SetSettingsView(false));
            Theme.DrawText(g, Strings.Current.Settings, Theme.Text(20, true), P.TextPrimary, new RectangleF(56, 12, 200, 36));

            DrawAccountCard(g, new RectangleF(Pad, 56, W - 2 * Pad, 158));

            // Target range
            RectangleF targets = new RectangleF(Pad, 222, W - 2 * Pad, 80);
            Theme.FillRounded(g, P.CardFill, P.CardStroke, targets, Theme.OverlayRadius);
            Theme.DrawText(g, Strings.Current.LowTarget, Theme.Text(12), P.TextSecondary, new RectangleF(28, 230, 139, 20));
            Theme.DrawText(g, Strings.Current.HighTarget, Theme.Text(12), P.TextSecondary, new RectangleF(173, 230, 139, 20));
            DrawNumberBox(g, "low", new RectangleF(28, 256, 139, 32), editLow,
                () => editLow = Math.Max(40, editLow - 5), () => editLow = Math.Min(Math.Min(150, editHigh - 10), editLow + 5));
            DrawNumberBox(g, "high", new RectangleF(173, 256, 139, 32), editHigh,
                () => editHigh = Math.Max(Math.Max(120, editLow + 10), editHigh - 5), () => editHigh = Math.Min(400, editHigh + 5));

            // Options
            Strings S = Strings.Current;
            RectangleF options = new RectangleF(Pad, 310, W - 2 * Pad, 152);
            Theme.FillRounded(g, P.CardFill, P.CardStroke, options, Theme.OverlayRadius);
            using (Pen div = new Pen(P.CardStroke, 1f))
            {
                g.DrawLine(div, options.X + 12, 348.5f, options.Right - 12, 348.5f);
                g.DrawLine(div, options.X + 12, 386.5f, options.Right - 12, 386.5f);
                g.DrawLine(div, options.X + 12, 424.5f, options.Right - 12, 424.5f);
            }

            Font body = Theme.Text(14);
            Theme.DrawText(g, S.LaunchAtStartup, body, P.TextPrimary, new RectangleF(28, 310, 200, 38));
            DrawToggle(g, "autostart", new RectangleF(272, 319, 40, 20), knobAutostart, () => { editAutostart = !editAutostart; anim.Start(); });

            Theme.DrawText(g, S.WidgetBackground, body, P.TextPrimary, new RectangleF(28, 348, 150, 38));
            DrawSlider(g, "opacity", new RectangleF(176, 357, 96, 20), editOpacity, v => editOpacity = v);
            Theme.DrawText(g, editOpacity + "%", Theme.Text(12), P.TextSecondary, new RectangleF(272, 348, 40, 38), StringAlignment.Far);

            Theme.DrawText(g, S.ShowOverFullscreen, body, P.TextPrimary, new RectangleF(28, 386, 236, 38));
            DrawToggle(g, "fullscreen", new RectangleF(272, 395, 40, 20), knobFullscreen, () => { editFullscreen = !editFullscreen; anim.Start(); });

            Theme.DrawText(g, S.Language, body, P.TextPrimary, new RectangleF(28, 424, 90, 38));
            DrawLanguageSelector(g, new RectangleF(120, 429, 192, 28));

            // Footer: Exit (standard) and Save (accent), like a Windows 11 dialog
            DrawFooterLayer(g);
            float by = FooterY + (FooterH - 32) / 2f;
            DrawButton(g, "exit", new RectangleF(Pad, by, 100, 32), S.ExitApp, false, true, () => { if (ExitRequested != null) ExitRequested(); });
            DrawButton(g, "save", new RectangleF(W - Pad - 120, by, 120, 32), S.Save, true, true, SaveSettings);
        }

        private void DrawAccountCard(Graphics g, RectangleF card)
        {
            Theme.FillRounded(g, P.CardFill, P.CardStroke, card, Theme.OverlayRadius);
            Theme.DrawText(g, Strings.Current.ServerCardTitle, Theme.Text(14, true), P.TextPrimary, new RectangleF(28, 62, 140, 28));
            if (!string.IsNullOrEmpty(pairMessage))
            {
                Theme.DrawText(g, pairMessage, Theme.Text(12), pairError ? P.Critical : P.TextSecondary,
                    new RectangleF(168, 62, 144, 28), StringAlignment.Far);
            }

            if (config.IsPaired)
            {
                Theme.DrawText(g, config.ServerUrl, Theme.Text(14), P.TextSecondary, new RectangleF(28, 94, 284, 32));
                using (SolidBrush dot = new SolidBrush(P.Success)) g.FillEllipse(dot, 30, 144, 8, 8);
                string who = string.IsNullOrEmpty(config.AccountName) ? Strings.Current.Linked : Strings.Current.Format(Strings.Current.LinkedAs, config.AccountName);
                Theme.DrawText(g, who, Theme.Text(14), P.TextPrimary, new RectangleF(46, 132, 266, 32));
                Theme.DrawText(g, Strings.Current.ReadOnlyDevice, Theme.Text(12), P.TextTertiary, new RectangleF(28, 170, 190, 32));
                DrawButton(g, "unlink", new RectangleF(232, 170, 80, 32), Strings.Current.Unlink, false, true, Unlink);
            }
            else
            {
                DrawTextBoxFrame(g, BoxServer, txtServer);
                DrawTextBoxFrame(g, BoxUser, txtUser);
                DrawTextBoxFrame(g, BoxPassword, txtPassword);
                DrawTextBoxFrame(g, BoxTotp, txtTotp);
                DrawButton(g, "connect", new RectangleF(BoxTotp.Right + 6, BoxTotp.Y, 312 - BoxTotp.Right - 6, 32),
                    isPairing ? Strings.Current.Connecting : Strings.Current.Connect, true, !isPairing, StartPairing);
            }
        }

        // ------------------------------------------------------------------ Fluent controls

        private void DrawIconButton(Graphics g, string id, RectangleF r, string glyph, Action click)
        {
            AddHit(id, r, click);
            if (IsHover(id))
                Theme.FillRounded(g, IsPressed(id) ? P.SubtlePressed : P.SubtleHover, r, Theme.ControlRadius);
            Theme.DrawText(g, glyph, Theme.Icons(16), IsPressed(id) ? P.TextSecondary : P.TextPrimary, r, StringAlignment.Center);
        }

        private void DrawButton(Graphics g, string id, RectangleF r, string text, bool accent, bool enabled, Action click)
        {
            if (enabled) AddHit(id, r, click);
            bool hover = enabled && IsHover(id), pressed = enabled && IsPressed(id);
            Color fg;
            if (accent)
            {
                Color fill = !enabled ? Color.FromArgb(110, P.Accent) : pressed ? P.AccentPressed : hover ? P.AccentHover : P.Accent;
                Theme.FillRounded(g, Theme.Over(P.Surface, fill), r, Theme.ControlRadius);
                fg = pressed ? Color.FromArgb(200, P.TextOnAccent) : P.TextOnAccent;
            }
            else
            {
                Theme.FillRounded(g, pressed ? P.ControlFillPressed : hover ? P.ControlFillHover : P.ControlFill, P.ControlStroke, r, Theme.ControlRadius);
                fg = pressed ? P.TextSecondary : P.TextPrimary;
            }
            Theme.DrawText(g, text, Theme.Text(14), fg, r, StringAlignment.Center);
        }

        private void DrawTextBoxFrame(Graphics g, RectangleF r, TextBox box)
        {
            bool focused = box.Focused;
            Theme.FillRounded(g, P.ControlFill, P.ControlStroke, r, Theme.ControlRadius);
            // Bottom edge: thin neutral line, 2px accent when focused (WinUI TextBox)
            GraphicsState state = g.Save();
            using (GraphicsPath clip = Theme.RoundedRect(r, Theme.ControlRadius))
            {
                g.SetClip(clip);
                float h = focused ? 2f : 1f;
                using (SolidBrush b = new SolidBrush(focused ? P.Accent : P.ControlStrongStroke))
                    g.FillRectangle(b, r.X, r.Bottom - h, r.Width, h);
            }
            g.Restore(state);
        }

        private void DrawNumberBox(Graphics g, string id, RectangleF r, int value, Action minus, Action plus)
        {
            Theme.FillRounded(g, P.ControlFill, P.ControlStroke, r, Theme.ControlRadius);
            Theme.DrawText(g, value.ToString(), Theme.Text(14), P.TextPrimary, new RectangleF(r.X + 12, r.Y, 50, r.Height));

            RectangleF bMinus = new RectangleF(r.Right - 64, r.Y + 4, 28, r.Height - 8);
            RectangleF bPlus = new RectangleF(r.Right - 32, r.Y + 4, 28, r.Height - 8);
            foreach (var b in new[] { Tuple.Create(id + "-", bMinus, Theme.GlyphRemove, minus), Tuple.Create(id + "+", bPlus, Theme.GlyphAdd, plus) })
            {
                Action act = b.Item4;
                AddHit(b.Item1, b.Item2, () => { act(); Invalidate(); });
                if (IsHover(b.Item1))
                    Theme.FillRounded(g, IsPressed(b.Item1) ? P.SubtlePressed : P.SubtleHover, b.Item2, Theme.ControlRadius);
                Theme.DrawText(g, b.Item3, Theme.Icons(12), P.TextSecondary, b.Item2, StringAlignment.Center);
            }
        }

        private void DrawToggle(Graphics g, string id, RectangleF r, float knob, Action click)
        {
            RectangleF hit = RectangleF.Inflate(r, 6, 8);
            AddHit(id, hit, click);
            bool hover = IsHover(id);

            if (knob > 0f)
            {
                Color on = Color.FromArgb((int)(255 * knob), hover ? P.AccentHover : P.Accent);
                Theme.FillRounded(g, Theme.Over(P.CardFill, on), r, r.Height / 2);
            }
            if (knob < 1f)
            {
                RectangleF inner = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                using (GraphicsPath path = Theme.RoundedRect(inner, inner.Height / 2))
                using (Pen p = new Pen(Color.FromArgb((int)(P.ControlStrongStroke.A * (1 - knob)), P.ControlStrongStroke), 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            // Knob: 12px (14px on hover), slides and changes colour with the state
            float size = hover ? 14f : 12f;
            float cx = r.X + 10 + (r.Width - 20) * knob, cy = r.Y + r.Height / 2;
            Color offKnob = Theme.Over(P.CardFill, P.ControlStrongStroke);
            Color knobColor = Blend(offKnob, P.TextOnAccent, knob);
            using (SolidBrush b = new SolidBrush(knobColor))
                g.FillEllipse(b, cx - size / 2, cy - size / 2, size, size);
        }

        private void DrawSlider(Graphics g, string id, RectangleF r, int value, Action<int> set)
        {
            float thumb = 20f, left = r.X + thumb / 2, width = r.Width - thumb;
            AddHit(id, RectangleF.Inflate(r, 0, 6), null, x =>
            {
                set(Math.Max(0, Math.Min(100, (int)Math.Round((x - left) / width * 100f))));
            });

            float cy = r.Y + r.Height / 2, tx = left + width * value / 100f;
            Theme.FillRounded(g, Theme.Over(P.CardFill, P.ControlStrongStroke), new RectangleF(left, cy - 2, width, 4), 2);
            if (tx > left) Theme.FillRounded(g, P.Accent, new RectangleF(left, cy - 2, tx - left, 4), 2);

            // WinUI thumb: neutral disc with a stroke and an accent centre that grows on hover
            RectangleF outer = new RectangleF(tx - thumb / 2, cy - thumb / 2, thumb, thumb);
            using (SolidBrush b = new SolidBrush(P.IsDark ? Color.FromArgb(69, 69, 69) : Color.White)) g.FillEllipse(b, outer);
            using (Pen p = new Pen(P.ControlStroke, 1f)) g.DrawEllipse(p, outer);
            float inner = IsPressed(id) || dragging != null && dragging.Id == id ? 10f : IsHover(id) ? 14f : 12f;
            using (SolidBrush b = new SolidBrush(P.Accent)) g.FillEllipse(b, tx - inner / 2, cy - inner / 2, inner, inner);
        }

        /// <summary>Segmented control: System / English / Italiano (each language named in its own language).</summary>
        private void DrawLanguageSelector(Graphics g, RectangleF r)
        {
            Theme.FillRounded(g, P.ControlFill, P.ControlStroke, r, Theme.ControlRadius);
            int n = Strings.Choices.Length;
            float w = r.Width / n;
            for (int i = 0; i < n; i++)
            {
                string value = Strings.Choices[i][0];
                string label = Strings.Choices[i][1] ?? Strings.Current.LanguageSystem;
                string id = "lang-" + value;
                RectangleF seg = new RectangleF(r.X + i * w + 2, r.Y + 2, w - 4, r.Height - 4);
                AddHit(id, seg, () => editLanguage = value);
                bool selected = editLanguage == value;
                if (selected)
                    Theme.FillRounded(g, Theme.Over(P.Surface, IsHover(id) ? P.AccentHover : P.Accent), seg, Theme.ControlRadius - 1);
                else if (IsHover(id))
                    Theme.FillRounded(g, IsPressed(id) ? P.SubtlePressed : P.SubtleHover, seg, Theme.ControlRadius - 1);
                Theme.DrawText(g, label, Theme.Text(12, selected), selected ? P.TextOnAccent : P.TextPrimary, seg, StringAlignment.Center);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // ================================================================== actions

        private void SaveSettings()
        {
            // Credentials typed but "Connect" not pressed: link the PC as part of saving.
            if (!config.IsPaired && !isPairing && txtUser.Text.Trim().Length > 0 && txtPassword.Text.Length > 0)
            {
                StartPairing();
            }
            else if (!config.IsPaired)
            {
                string server = G7MonitorSyncService.NormalizeServerUrl(txtServer.Text);
                if (server != null) config.ServerUrl = server;
            }

            config.LowThreshold = editLow;
            config.HighThreshold = editHigh;
            config.Autostart = editAutostart;
            config.WidgetBackgroundOpacity = editOpacity;
            config.ShowOverFullscreen = editFullscreen;
            bool languageChanged = config.Language != editLanguage;
            config.Language = editLanguage;
            if (languageChanged)
            {
                Strings.Apply(config.Language);
                ApplyLanguage();
            }
            config.Save();
            if (ConfigSaved != null) ConfigSaved(config);

            // While linking, stay here so the result (or the error) is visible.
            if (!isPairing) SetSettingsView(false);
        }

        private void SetPairMessage(string message, bool isError)
        {
            pairMessage = message;
            pairError = isError;
            Invalidate();
        }

        /// <summary>Links this PC to the G7 Monitor Server as a read-only device.</summary>
        private void StartPairing()
        {
            if (isPairing || config.IsPaired) return;

            string server = G7MonitorSyncService.NormalizeServerUrl(txtServer.Text);
            string user = txtUser.Text.Trim();
            string password = txtPassword.Text;
            string totp = txtTotp.Text.Trim();

            Strings L = Strings.Current;
            if (server == null) { SetPairMessage(L.InvalidServer, true); txtServer.Focus(); return; }
            if (user.Length == 0 || password.Length == 0) { SetPairMessage(L.EnterUserPassword, true); return; }
            if (totp.Length > 0 && (totp.Length != 6 || !IsDigits(totp))) { SetPairMessage(L.TotpSixDigits, true); return; }

            isPairing = true;
            UpdateInputVisibility();
            SetPairMessage(L.Connecting, false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                PairResult result = G7MonitorSyncService.Pair(server, user, password, totp);
                try { BeginInvoke(new Action(() => FinishPairing(server, result))); }
                catch (InvalidOperationException) { } // window already closed
            });
        }

        private void FinishPairing(string server, PairResult result)
        {
            isPairing = false;
            txtTotp.Text = "";

            if (result.Ok)
            {
                txtPassword.Text = "";
                txtServer.Text = server;
                config.ServerUrl = server;
                config.DeviceToken = result.Token;
                config.AccountName = result.AccountName;
                config.Save();
                SetPairMessage(null, false);
                if (ConfigSaved != null) ConfigSaved(config);
            }
            else
            {
                SetPairMessage(Strings.Current.PairError(result.Error, result.Detail), true);
            }

            UpdateInputVisibility();
            if (!config.IsPaired && txtPassword.Visible)
                (result.Error == PairError.TotpRequired || result.Error == PairError.WrongTotp ? txtTotp : txtPassword).Focus();
        }

        private void Unlink()
        {
            G7MonitorSyncService.Unpair(config.ServerUrl, config.DeviceToken);
            config.DeviceToken = "";
            config.AccountName = "";
            config.Save();
            SetPairMessage(null, false);
            if (ConfigSaved != null) ConfigSaved(config);
            UpdateInputVisibility();
            if (txtUser.Visible) txtUser.Focus();
        }

        private static bool IsDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
