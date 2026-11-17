using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using G7MonitorDesktop.Interop;
using G7MonitorDesktop.Localization;
using G7MonitorDesktop.Models;
using G7MonitorDesktop.Services;

namespace G7MonitorDesktop.UI
{
    /// <summary>
    /// Quick-glance reading on the taskbar, styled like a Windows 11 taskbar button.
    /// A per-pixel-alpha layered window: transparent when idle, the taskbar shows through.
    /// Uses only its own window messages for the mouse (no global hooks), so it stays clean for antivirus heuristics.
    /// </summary>
    public class TaskbarWidget : Form
    {
        private const int LogicalHeight = 40;
        private const int HPad = 10;
        private const int ArrowSlot = 22;

        private GlucoseEntry current;
        private int? currentDelta;
        private SyncState state = SyncState.Connecting;
        private AppConfig config;
        private readonly float scale;

        private bool pressed;
        private float hover, hoverTarget;
        private DateTime lastClick = DateTime.MinValue;

        private readonly Timer hoverTimer;
        private readonly Timer positionTimer;
        private readonly Timer pointerTimer;
        private bool pointerInside;
        private IntPtr flyoutHandle = IntPtr.Zero;

        // Used only to measure text: the widget sizes itself to its content.
        private static readonly Graphics Measure = Graphics.FromImage(new Bitmap(1, 1));

        public event Action Clicked;
        public event Action RightClicked;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW |
                              NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public TaskbarWidget(AppConfig config)
        {
            this.config = config;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;

            scale = Theme.DpiScale;
            Size = new Size((int)(96 * scale), (int)(LogicalHeight * scale));

            hoverTimer = new Timer { Interval = 15 };
            hoverTimer.Tick += (s, e) =>
            {
                hover += (hoverTarget - hover) * 0.35f;
                if (Math.Abs(hoverTarget - hover) < 0.03f)
                {
                    hover = hoverTarget;
                    hoverTimer.Stop();
                }
                Render();
            };

            // Taskbar size and tray width change (icons appear, resolution changes): keep docked.
            positionTimer = new Timer { Interval = 500 };
            positionTimer.Tick += (s, e) => UpdateVisibilityAndPosition();
            positionTimer.Start();

            // Hover state and z-order follow the pointer position (read-only, like any tooltip logic):
            // when the pointer reaches the widget it is raised above the taskbar, so the click lands on it.
            pointerTimer = new Timer { Interval = 100 };
            pointerTimer.Tick += (s, e) => TrackPointer();
            pointerTimer.Start();
        }

        public void SetFlyoutHandle(IntPtr handle)
        {
            flyoutHandle = handle;
        }

        // ================================================================== public API (thread-safe)

        public void UpdateGlucose(GlucoseEntry entry, int? delta)
        {
            if (InvokeRequired) { BeginInvoke(new Action<GlucoseEntry, int?>(UpdateGlucose), entry, delta); return; }
            current = entry;
            currentDelta = delta;
            Render();
        }

        public void UpdateStatus(SyncState newState, int retrySeconds)
        {
            if (InvokeRequired) { BeginInvoke(new Action<SyncState, int>(UpdateStatus), newState, retrySeconds); return; }
            state = newState;
            if (current == null) Render();
        }

        public void UpdateConfig(AppConfig newConfig)
        {
            if (InvokeRequired) { BeginInvoke(new Action<AppConfig>(UpdateConfig), newConfig); return; }
            config = newConfig;
            Render();
        }

        // ================================================================== position

        private void UpdateVisibilityAndPosition()
        {
            bool hideForFullscreen = !config.ShowOverFullscreen && NativeMethods.IsFullscreenAppRunning(Handle, flyoutHandle);
            if (hideForFullscreen)
            {
                if (Visible) Hide();
                return;
            }
            if (!Visible)
            {
                Show();
                Render();
            }
            SnapToTaskbar();
        }

        /// <summary>Docks just left of the notification area, vertically centred in the taskbar.</summary>
        private void SnapToTaskbar()
        {
            Rectangle bar = NativeMethods.GetTaskbarRect();
            int trayLeft = NativeMethods.GetNotificationAreaLeft();
            int x = trayLeft > 0 ? trayLeft - Width - (int)(4 * scale) : bar.Right - Width - (int)(260 * scale);
            int y = bar.Top + (bar.Height - Height) / 2;

            if (Left != x || Top != y)
            {
                Location = new Point(x, y);
                Render();
            }
            // The taskbar re-raises itself: stay above it without stealing focus.
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SnapToTaskbar();
            Render();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_WINDOWPOSCHANGING:
                    var pos = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                    pos.hwndInsertAfter = NativeMethods.HWND_TOPMOST;
                    Marshal.StructureToPtr(pos, m.LParam, false);
                    break;
                case NativeMethods.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                    return;
                case NativeMethods.WM_SETTINGCHANGE:
                case NativeMethods.WM_THEMECHANGED:
                case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                    Theme.Refresh();
                    Render();
                    break;
            }
            base.WndProc(ref m);
        }

        // ================================================================== mouse

        private void TrackPointer()
        {
            bool inside = Visible && Bounds.Contains(Cursor.Position);
            if (inside)
            {
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            if (inside == pointerInside) return;
            pointerInside = inside;
            if (!inside) pressed = false;
            hoverTarget = inside ? 1f : 0f;
            hoverTimer.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pointerInside) TrackPointer(); // immediate hover feedback
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            pressed = true;
            Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasPressed = pressed;
            pressed = false;
            Render();
            if (!wasPressed || (DateTime.UtcNow - lastClick).TotalMilliseconds < 200) return;
            lastClick = DateTime.UtcNow;
            Action handler = e.Button == MouseButtons.Left ? Clicked : e.Button == MouseButtons.Right ? RightClicked : null;
            if (handler != null) handler();
        }

        // ================================================================== rendering

        public void Render()
        {
            if (!IsHandleCreated) return;
            Palette P = Theme.Current;

            // Content and its width (96-DPI units)
            bool stale = current != null && current.IsStale;
            // Sized for reading at a glance: value larger than the taskbar clock, delta at body size.
            Font valueFont = Theme.Display(20);
            Font deltaFont = Theme.Text(14, true);
            Font smallFont = Theme.Text(13);
            string valueText = current != null ? current.Sgv.ToString() : null;
            string deltaText = null;
            bool showArrow = current != null && !stale && current.TrendRate.HasValue;
            if (current != null)
            {
                deltaText = stale ? FormatAge(current.Age)
                    : currentDelta.HasValue ? (currentDelta.Value > 0 ? "+" : currentDelta.Value < 0 ? "−" : "±") + Math.Abs(currentDelta.Value) : "";
            }
            string statusText = ShortStatus();

            float contentWidth;
            if (current != null)
            {
                contentWidth = 8 + 8 + Theme.MeasureWidth(Measure, valueText, valueFont)
                    + (showArrow ? ArrowSlot : 0)
                    + (string.IsNullOrEmpty(deltaText) ? 0 : 6 + Theme.MeasureWidth(Measure, deltaText, deltaFont));
            }
            else
            {
                contentWidth = Theme.MeasureWidth(Measure, statusText, smallFont);
            }
            int logicalWidth = (int)Math.Ceiling(Math.Max(48, contentWidth + 2 * HPad));
            int width = (int)Math.Round(logicalWidth * scale);
            if (width != Width)
            {
                // Grow/shrink towards the left: the right edge stays next to the tray.
                Bounds = new Rectangle(Right - width, Top, width, Height);
            }

            using (Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // Transparent surface: grayscale anti-aliasing (ClearType here causes coloured fringes).
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.ScaleTransform(scale, scale);

                    float w = logicalWidth, h = LogicalHeight;

                    // Almost invisible fill so the whole rectangle receives the mouse.
                    using (SolidBrush hitBrush = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                        g.FillRectangle(hitBrush, 0, 0, w, h);

                    DrawBackground(g, P, new RectangleF(0, 4, w, h - 8));

                    // Baseline so the 20px figures sit optically centred (cap height ≈ 0.7 em).
                    float baseline = h / 2f + 7f;
                    if (current == null)
                    {
                        if (P.IsDark)
                            Theme.DrawText(g, statusText, smallFont, Color.FromArgb(120, 0, 0, 0), new RectangleF(0, 1, w, h), StringAlignment.Center);
                        Theme.DrawText(g, statusText, smallFont, P.TextSecondary, new RectangleF(0, 0, w, h), StringAlignment.Center);
                    }
                    else
                    {
                        Color cat = stale ? P.TextTertiary : P.Category(config.CategoryOf(current.Sgv));
                        Color valueColor = stale ? P.TextSecondary : P.TextPrimary;
                        Color deltaColor = stale ? P.TextSecondary : Color.FromArgb(P.IsDark ? 225 : 200, P.TextPrimary);

                        // A transparent taskbar shows the wallpaper: on dark taskbars a soft shadow keeps
                        // light text readable over bright or busy backgrounds (pass 0 = shadow, 1 = content).
                        for (int pass = P.IsDark ? 0 : 1; pass < 2; pass++)
                        {
                            bool shadow = pass == 0;
                            float dy = shadow ? 1f : 0f;
                            Color sh = Color.FromArgb(120, 0, 0, 0);
                            float x = HPad;
                            using (SolidBrush dot = new SolidBrush(shadow ? sh : cat)) g.FillEllipse(dot, x, h / 2f - 4 + dy, 8, 8);
                            x += 16;
                            x += Theme.DrawOnBaseline(g, valueText, valueFont, shadow ? sh : valueColor, x, baseline + dy);
                            if (showArrow)
                            {
                                Theme.DrawTrendArrow(g, x + ArrowSlot / 2f + 1, h / 2f + dy, 15, current.TrendRate.Value, shadow ? sh : cat);
                                x += ArrowSlot;
                            }
                            if (!string.IsNullOrEmpty(deltaText))
                                Theme.DrawOnBaseline(g, deltaText, deltaFont, shadow ? sh : deltaColor, x + 6, baseline + dy);
                        }
                    }
                }
                SetLayeredBitmap(bmp);
            }
        }

        /// <summary>Windows 11 taskbar button: transparent at rest, soft fill and top highlight on hover.</summary>
        private void DrawBackground(Graphics g, Palette P, RectangleF r)
        {
            int opacity = config != null ? config.WidgetBackgroundOpacity : 0;
            float fillAlpha = opacity * (P.IsDark ? 0.6f : 0.45f) + hover * (P.IsDark ? 15f : 10f) - (pressed ? 5f : 0f);
            float strokeAlpha = opacity * (P.IsDark ? 0.3f : 0.25f) + hover * (P.IsDark ? 14f : 12f);
            Color tint = P.IsDark ? Color.White : Color.Black;

            if (fillAlpha >= 1f)
                Theme.FillRounded(g, Color.FromArgb(Math.Min(255, (int)fillAlpha), tint), r, Theme.ControlRadius);
            if (strokeAlpha >= 1f)
            {
                RectangleF inner = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                using (GraphicsPath path = Theme.RoundedRect(inner, Theme.ControlRadius))
                using (Pen p = new Pen(Color.FromArgb(Math.Min(255, (int)strokeAlpha), tint), 1f))
                    g.DrawPath(p, path);
            }
        }

        private string ShortStatus()
        {
            if (config == null || !config.IsPaired) return Strings.Current.WidgetSetup;
            string s = Strings.Current.Status(state, 0, true);
            return s.Length > 16 ? s.Substring(0, 15) + "…" : s;
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalHours < 1) return (int)age.TotalMinutes + "m";
            if (age.TotalDays < 1) return (int)age.TotalHours + "h";
            return (int)age.TotalDays + "d";
        }

        private void SetLayeredBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);
                var size = new NativeMethods.SIZE { cx = bitmap.Width, cy = bitmap.Height };
                var src = new NativeMethods.POINT();
                var dst = new NativeMethods.POINT { X = Left, Y = Top };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };
                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                    NativeMethods.DeleteObject(hBitmap);
                }
                NativeMethods.DeleteDC(memDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pointerTimer.Dispose();
                positionTimer.Dispose();
                hoverTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
