using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MonitorConfig
{
    // Flag of the main country of each interface language, built from the flags' official geometry and colours
    // (drawn 4x larger and reduced, so they stay crisp at icon size). All share a 3:2 box, as icon sets do.
    static class Flags
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string language, int height)
        {
            string key = language + "@" + height;
            Bitmap flag;
            if (cache.TryGetValue(key, out flag)) return flag;
            int width = (int)Math.Round(height * 1.5);
            const int k = 4;
            flag = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var big = new Bitmap(width * k, height * k, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(big))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Draw(g, language, width * k, height * k);
                }
                using (var g = Graphics.FromImage(flag))
                using (var wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);   // no faded edges when reducing
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, width, height), 0, 0, big.Width, big.Height, GraphicsUnit.Pixel, wrap);
                    using (var edge = new Pen(Color.FromArgb(70, 0, 0, 0)))   // thin outline: white areas stay visible
                        g.DrawRectangle(edge, 0, 0, width - 1, height - 1);
                }
            }
            cache[key] = flag;
            return flag;
        }

        static Color C(int rgb) { return Color.FromArgb(255, Color.FromArgb(rgb)); }

        static void Draw(Graphics g, string language, float w, float h)
        {
            switch (language)
            {
                case "en": UnitedKingdom(g, w, h); break;
                case "es": Bands(g, w, h, false, new[] { C(0xAA151B), C(0xF1BF00), C(0xAA151B) }, new[] { 1f, 2f, 1f }); break;
                case "fr": Bands(g, w, h, true, new[] { C(0x002395), C(0xFFFFFF), C(0xED2939) }, null); break;
                case "it": Bands(g, w, h, true, new[] { C(0x009246), C(0xFFFFFF), C(0xCE2B37) }, null); break;
                case "de": Bands(g, w, h, false, new[] { C(0x000000), C(0xDD0000), C(0xFFCE00) }, null); break;
                case "pt": Brazil(g, w, h); break;
                case "ru": Bands(g, w, h, false, new[] { C(0xFFFFFF), C(0x0039A6), C(0xD52B1E) }, null); break;
                case "ja": Japan(g, w, h); break;
                case "zh": China(g, w, h); break;
                default: g.Clear(C(0xC0C0C0)); break;
            }
        }

        // Stripes (vertical or horizontal) with relative widths (null = equal).
        static void Bands(Graphics g, float w, float h, bool vertical, Color[] colors, float[] weights)
        {
            float total = 0;
            for (int i = 0; i < colors.Length; i++) total += weights == null ? 1 : weights[i];
            float pos = 0, length = vertical ? w : h;
            for (int i = 0; i < colors.Length; i++)
            {
                float size = length * (weights == null ? 1 : weights[i]) / total;
                using (var b = new SolidBrush(colors[i]))
                {
                    if (vertical) g.FillRectangle(b, pos, 0, size + 1, h);
                    else g.FillRectangle(b, 0, pos, w, size + 1);
                }
                pos += size;
            }
        }

        static void UnitedKingdom(Graphics g, float w, float h)
        {
            g.Clear(C(0x012169));
            using (var white = new Pen(Color.White, h * 0.2f))
            using (var red = new Pen(C(0xC8102E), h * 0.067f))
            {
                g.DrawLine(white, 0, 0, w, h);   // St Andrew's saltire
                g.DrawLine(white, 0, h, w, 0);
                g.DrawLine(red, 0, 0, w, h);     // St Patrick's saltire (centred at this size)
                g.DrawLine(red, 0, h, w, 0);
            }
            using (var white = new SolidBrush(Color.White))
            using (var red = new SolidBrush(C(0xC8102E)))
            {
                g.FillRectangle(white, w / 2 - h / 6, 0, h / 3, h);   // St George's cross with its white border
                g.FillRectangle(white, 0, h / 3, w, h / 3);
                g.FillRectangle(red, w / 2 - h / 10, 0, h / 5, h);
                g.FillRectangle(red, 0, h * 0.4f, w, h / 5);
            }
        }

        static void Brazil(Graphics g, float w, float h)
        {
            g.Clear(C(0x009C3B));
            // Official grid: 20 x 14 units, the rhombus 1.7 units from the edges, the globe of radius 3.5.
            float ux = w / 20, uy = h / 14;
            var rhombus = new[] { new PointF(1.7f * ux, 7 * uy), new PointF(10 * ux, 1.7f * uy), new PointF(18.3f * ux, 7 * uy), new PointF(10 * ux, 12.3f * uy) };
            using (var yellow = new SolidBrush(C(0xFFDF00))) g.FillPolygon(yellow, rhombus);
            float r = 3.5f * uy;
            using (var blue = new SolidBrush(C(0x002776))) g.FillEllipse(blue, 10 * ux - r, 7 * uy - r, 2 * r, 2 * r);
        }

        static void Japan(Graphics g, float w, float h)
        {
            g.Clear(Color.White);
            float r = h * 0.3f;   // the disc is 3/5 of the height
            using (var red = new SolidBrush(C(0xBC002D))) g.FillEllipse(red, w / 2 - r, h / 2 - r, 2 * r, 2 * r);
        }

        static void China(Graphics g, float w, float h)
        {
            g.Clear(C(0xEE1C25));
            float u = h / 20;   // official grid: 30 x 20 units
            using (var yellow = new SolidBrush(C(0xFFFF00)))
            {
                Star(g, yellow, 5 * u, 5 * u, 3 * u, -Math.PI / 2);
                foreach (var c in new[] { new PointF(10, 2), new PointF(12, 4), new PointF(12, 7), new PointF(10, 9) })
                    Star(g, yellow, c.X * u, c.Y * u, u, Math.Atan2(5 - c.Y, 5 - c.X));   // each points at the big star
            }
        }

        static void Star(Graphics g, Brush brush, float cx, float cy, float radius, double angle)
        {
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = angle + i * Math.PI / 5;
                float r = i % 2 == 0 ? radius : radius * 0.382f;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));
            }
            g.FillPolygon(brush, pts);
        }
    }
}
