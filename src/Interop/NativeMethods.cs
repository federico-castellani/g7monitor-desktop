using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace G7MonitorDesktop.Interop
{
    internal static class NativeMethods
    {
        // ------------------------------------------------------------------ constants

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int CS_DROPSHADOW = 0x00020000;

        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_THEMECHANGED = 0x031A;
        public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;


        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUND = 2;
        private const int EM_SETCUEBANNER = 0x1501;

        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const uint ULW_ALPHA = 0x02;

        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        // ------------------------------------------------------------------ structs

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }


        // ------------------------------------------------------------------ imports

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, uint crKey, [In] ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int qState);

        // ------------------------------------------------------------------ helpers

        /// <summary>Grey placeholder text in an empty TextBox (kept visible while focused).</summary>
        public static void SetCueBanner(TextBox box, string text)
        {
            try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); } catch { }
        }

        /// <summary>Windows 11 window frame: rounded corners, themed 1px border, dark/light non-client parts.</summary>
        public static void ApplyWindowFrame(IntPtr hwnd, bool dark, Color border)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                value = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
                value = border.R | (border.G << 8) | (border.B << 16); // COLORREF
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref value, sizeof(int));
            }
            catch { } // older Windows: square corners, default border
        }

        /// <summary>Bounds of the taskbar on the primary screen (falls back to the area below the working area).</summary>
        public static Rectangle GetTaskbarRect()
        {
            RECT r;
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out r) && r.Right > r.Left && r.Bottom > r.Top)
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);

            Rectangle full = Screen.PrimaryScreen.Bounds, wa = Screen.PrimaryScreen.WorkingArea;
            return Rectangle.FromLTRB(full.Left, wa.Bottom, full.Right, full.Bottom);
        }

        /// <summary>Left edge of the notification area (tray icons, clock), or -1 when it cannot be found.</summary>
        public static int GetNotificationAreaLeft()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return -1;
            IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT r;
            if (tray != IntPtr.Zero && GetWindowRect(tray, out r) && r.Right - r.Left > 20 && r.Left > Screen.PrimaryScreen.Bounds.Width / 3)
                return r.Left;
            return -1;
        }

        public static bool IsFullscreenAppRunning(IntPtr ownWidget, IntPtr ownFlyout)
        {
            try
            {
                // 1. DirectX exclusive fullscreen / presentation mode
                int state;
                if (SHQueryUserNotificationState(out state) == 0 &&
                    (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
                    return true;

                // 2. Borderless fullscreen: the foreground window covers its whole monitor
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == ownWidget || fg == ownFlyout || IsIconic(fg) || !IsWindowVisible(fg))
                    return false;

                StringBuilder sb = new StringBuilder(256);
                GetClassName(fg, sb, 256);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                    cls == "NotifyIconOverflowWindow" || cls.StartsWith("Windows.UI.Core.CoreWindow"))
                    return false;

                RECT r;
                if (GetWindowRect(fg, out r))
                {
                    Rectangle s = Screen.FromHandle(fg).Bounds;
                    return r.Left <= s.Left && r.Top <= s.Top && r.Right >= s.Right && r.Bottom >= s.Bottom;
                }
            }
            catch { }
            return false;
        }
    }
}
