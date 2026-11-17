using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace G7MonitorDesktop.UI
{
    /// <summary>Windows 11 (WinUI 3 / Fluent 2) colour tokens for one theme, flattened on the opaque flyout surface.</summary>
    public sealed class Palette
    {
        public bool IsDark;
        public Color Surface;          // flyout background (acrylic fallback)
        public Color Layer;            // footer / secondary layer
        public Color Stroke;           // divider between surface and layer
        public Color CardFill;
        public Color CardStroke;
        public Color ControlFill;      // text boxes, number boxes, standard buttons
        public Color ControlFillHover;
        public Color ControlFillPressed;
        public Color ControlStroke;
        public Color ControlStrongStroke; // toggle "off" track, text box bottom line
        public Color SubtleHover;      // icon buttons
        public Color SubtlePressed;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color TextTertiary;
        public Color TextOnAccent;
        public Color Accent;
        public Color AccentHover;
        public Color AccentPressed;
        public Color Success;
        public Color Caution;
        public Color Critical;
        public Color GridLine;

        public static Palette Create(bool dark, Color accentDark, Color accentLight)
        {
            Palette p = new Palette { IsDark = dark };
            if (dark)
            {
                p.Surface = Color.FromArgb(44, 44, 44);
                p.Layer = Color.FromArgb(32, 32, 32);
                p.Stroke = Color.FromArgb(29, 29, 29);
                p.CardFill = Theme.Over(p.Surface, Color.FromArgb(8, 255, 255, 255));
                p.CardStroke = Theme.Over(p.Surface, Color.FromArgb(40, 0, 0, 0));
                p.ControlFill = Theme.Over(p.Surface, Color.FromArgb(22, 255, 255, 255));
                p.ControlFillHover = Theme.Over(p.Surface, Color.FromArgb(29, 255, 255, 255));
                p.ControlFillPressed = Theme.Over(p.Surface, Color.FromArgb(14, 255, 255, 255));
                p.ControlStroke = Theme.Over(p.Surface, Color.FromArgb(30, 255, 255, 255));
                p.ControlStrongStroke = Color.FromArgb(139, 255, 255, 255);
                p.SubtleHover = Color.FromArgb(15, 255, 255, 255);
                p.SubtlePressed = Color.FromArgb(10, 255, 255, 255);
                p.TextPrimary = Color.FromArgb(255, 255, 255);
                p.TextSecondary = Color.FromArgb(200, 255, 255, 255);
                p.TextTertiary = Color.FromArgb(139, 255, 255, 255);
                p.TextOnAccent = Color.Black;
                p.Accent = accentDark;
                p.Success = Color.FromArgb(108, 203, 95);   // SystemFillColorSuccess
                p.Caution = Color.FromArgb(252, 225, 0);    // SystemFillColorCaution
                p.Critical = Color.FromArgb(255, 153, 164); // SystemFillColorCritical
                p.GridLine = Color.FromArgb(14, 255, 255, 255);
            }
            else
            {
                p.Surface = Color.FromArgb(249, 249, 249);
                p.Layer = Color.FromArgb(238, 238, 238);
                p.Stroke = Color.FromArgb(229, 229, 229);
                p.CardFill = Theme.Over(p.Surface, Color.FromArgb(179, 255, 255, 255));
                p.CardStroke = Theme.Over(p.Surface, Color.FromArgb(15, 0, 0, 0));
                p.ControlFill = Theme.Over(p.Surface, Color.FromArgb(179, 255, 255, 255));
                p.ControlFillHover = Theme.Over(p.Surface, Color.FromArgb(10, 0, 0, 0));
                p.ControlFillPressed = Theme.Over(p.Surface, Color.FromArgb(6, 0, 0, 0));
                p.ControlStroke = Theme.Over(p.Surface, Color.FromArgb(24, 0, 0, 0));
                p.ControlStrongStroke = Color.FromArgb(114, 0, 0, 0);
                p.SubtleHover = Color.FromArgb(10, 0, 0, 0);
                p.SubtlePressed = Color.FromArgb(6, 0, 0, 0);
                p.TextPrimary = Color.FromArgb(228, 0, 0, 0);
                p.TextSecondary = Color.FromArgb(158, 0, 0, 0);
                p.TextTertiary = Color.FromArgb(114, 0, 0, 0);
                p.TextOnAccent = Color.White;
                p.Accent = accentLight;
                p.Success = Color.FromArgb(15, 123, 15);
                p.Caution = Color.FromArgb(157, 93, 0);
                p.Critical = Color.FromArgb(196, 43, 28);
                p.GridLine = Color.FromArgb(14, 0, 0, 0);
            }
            p.AccentHover = Color.FromArgb(230, p.Accent);
            p.AccentPressed = Color.FromArgb(204, p.Accent);
            return p;
        }

        /// <summary>Colour for a glucose category: "low", "high" or "normal".</summary>
        public Color Category(string category)
        {
            if (category == "low") return Critical;
            if (category == "high") return Caution;
            return Success;
        }
    }

    public static class Theme
    {
        public const float OverlayRadius = 8f;
        public const float ControlRadius = 4f;

        public const string GlyphSettings = "";
        public const string GlyphBack = "";
        public const string GlyphGlobe = "";
        public const string GlyphAdd = "";
        public const string GlyphRemove = "";

        /// <summary>System DPI / 96 (the process is system-DPI aware).</summary>
        public static float DpiScale
        {
            get
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
            }
        }

        private static Palette current;
        private static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
        private static readonly Dictionary<string, string> familyCache = new Dictionary<string, string>();

        /// <summary>The palette for the current Windows theme (cached; call Refresh on theme changes).</summary>
        public static Palette Current
        {
            get
            {
                if (current == null) Refresh();
                return current;
            }
        }

        public static void Refresh()
        {
            bool dark = IsDarkTheme();
            Color accentDark, accentLight;
            ReadAccentColors(out accentDark, out accentLight);
            current = Palette.Create(dark, accentDark, accentLight);
        }

        public static bool IsDarkTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object val = key != null ? key.GetValue("SystemUsesLightTheme") : null;
                    if (val != null) return Convert.ToInt32(val) == 0;
                }
            }
            catch { }
            return true;
        }

        /// <summary>AccentLight2 for dark mode, AccentDark1 for light mode, as WinUI uses them.</summary>
        private static void ReadAccentColors(out Color dark, out Color light)
        {
            dark = Color.FromArgb(76, 194, 255);
            light = Color.FromArgb(0, 95, 184);
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    byte[] p = key != null ? key.GetValue("AccentPalette") as byte[] : null;
                    if (p != null && p.Length >= 32)
                    {
                        dark = Color.FromArgb(p[4], p[5], p[6]);
                        light = Color.FromArgb(p[16], p[17], p[18]);
                    }
                }
            }
            catch { }
        }

        /// <summary>Alpha-blends a translucent token over an opaque surface.</summary>
        public static Color Over(Color under, Color over)
        {
            float a = over.A / 255f;
            return Color.FromArgb(255,
                (int)(under.R + (over.R - under.R) * a),
                (int)(under.G + (over.G - under.G) * a),
                (int)(under.B + (over.B - under.B) * a));
        }

        // ------------------------------------------------------------------ typography
        // Sizes are in pixels at 96 DPI (the WinUI type ramp); a Graphics scale transform handles DPI.

        /// <summary>Caption 12, Body 14, Subtitle 20, Title 28, Display 40 (pixels).</summary>
        public static Font Text(float px, bool semibold = false)
        {
            return GetFont(semibold ? "Segoe UI Variable Text Semibold|Segoe UI Semibold" : "Segoe UI Variable Text|Segoe UI", px, FontStyle.Regular);
        }

        public static Font Display(float px)
        {
            return GetFont("Segoe UI Variable Display Semib|Segoe UI Semibold", px, FontStyle.Regular);
        }

        public static Font Icons(float px)
        {
            return GetFont("Segoe Fluent Icons|Segoe MDL2 Assets", px, FontStyle.Regular);
        }

        /// <summary>Fonts are cached for the life of the app: creating them on every paint is slow.</summary>
        private static Font GetFont(string families, float px, FontStyle style)
        {
            string key = families + "|" + px + "|" + style;
            Font f;
            if (fontCache.TryGetValue(key, out f)) return f;

            string family;
            if (!familyCache.TryGetValue(families, out family))
            {
                family = "Segoe UI";
                foreach (string candidate in families.Split('|'))
                {
                    try
                    {
                        using (FontFamily ff = new FontFamily(candidate)) { family = ff.Name; break; }
                    }
                    catch (ArgumentException) { }
                }
                familyCache[families] = family;
            }

            f = new Font(family, px, style, GraphicsUnit.Pixel);
            fontCache[key] = f;
            return f;
        }

        // ------------------------------------------------------------------ geometry

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPath(b, path);
            }
        }

        /// <summary>Fill plus a 1px inner stroke, like WinUI card and control borders.</summary>
        public static void FillRounded(Graphics g, Color fill, Color stroke, RectangleF rect, float radius)
        {
            FillRounded(g, fill, rect, radius);
            RectangleF inner = new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);
            using (GraphicsPath path = RoundedRect(inner, radius - 0.5f))
            using (Pen p = new Pen(stroke, 1f))
            {
                g.DrawPath(p, path);
            }
        }

        /// <summary>
        /// Trend arrow drawn as vectors (crisp at any DPI, no font fallback issues).
        /// Same thresholds as the G7 Monitor web page: 1, 2, 3 mg/dL per minute.
        /// </summary>
        public static void DrawTrendArrow(Graphics g, float cx, float cy, float size, double rate, Color color)
        {
            double abs = Math.Abs(rate);
            float angle = abs < 1 ? 0f : abs < 2 ? 45f : 90f;
            if (rate > 0) angle = -angle;
            bool doubled = abs >= 3;

            GraphicsState state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            using (Pen pen = new Pen(color, Math.Max(1.5f, size * 0.13f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float half = size / 2f, head = size * 0.36f;
                foreach (float offset in doubled ? new[] { -size * 0.2f, size * 0.2f } : new[] { 0f })
                {
                    g.DrawLine(pen, -half, offset, half, offset);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(half - head, offset - head),
                        new PointF(half, offset),
                        new PointF(half - head, offset + head)
                    });
                }
            }
            g.Restore(state);
        }

        // ------------------------------------------------------------------ text

        private static readonly StringFormat Typographic = CreateTypographic();

        private static StringFormat CreateTypographic()
        {
            StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return sf;
        }

        public static float MeasureWidth(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return g.MeasureString(text, font, PointF.Empty, Typographic).Width;
        }

        /// <summary>Distance from the top of the text cell to the baseline, in pixels.</summary>
        public static float Ascent(Font font)
        {
            FontFamily ff = font.FontFamily;
            return font.Size * ff.GetCellAscent(font.Style) / ff.GetEmHeight(font.Style);
        }

        /// <summary>Draws text with its baseline at y (lets different sizes line up like real typography).</summary>
        public static float DrawOnBaseline(Graphics g, string text, Font font, Color color, float x, float baseline)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            using (SolidBrush b = new SolidBrush(color))
            {
                g.DrawString(text, font, b, x, baseline - Ascent(font), Typographic);
            }
            return MeasureWidth(g, text, font);
        }

        /// <summary>Text vertically centred in a rectangle; alignment Near/Center/Far horizontally, ellipsis when too long.</summary>
        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush b = new SolidBrush(color))
            {
                sf.Alignment = align;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, font, b, rect, sf);
            }
        }
    }
}
