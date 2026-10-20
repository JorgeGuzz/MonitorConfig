// Generates src\app.ico (monitor with a settings gear) at several sizes.
// Usage: MakeIcon.exe <out.ico>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static PointF Polar(PointF c, float r, double a)
    {
        return new PointF(c.X + (float)(Math.Cos(a) * r), c.Y + (float)(Math.Sin(a) * r));
    }

    static Bitmap Draw(int n)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float u = n / 32f;
            // stand
            using (var stand = new SolidBrush(Color.FromArgb(70, 76, 88)))
            {
                g.FillRectangle(stand, 14 * u, 22 * u, 4 * u, 5 * u);
                using (var p = Rounded(new RectangleF(9 * u, 26 * u, 14 * u, 3 * u), 1.4f * u)) g.FillPath(stand, p);
            }
            // bezel and screen
            var bezel = new RectangleF(1 * u, 3 * u, 30 * u, 20.5f * u);
            using (var p = Rounded(bezel, 2.5f * u))
            using (var b = new SolidBrush(Color.FromArgb(38, 42, 50)))
                g.FillPath(b, p);
            float inset = n <= 20 ? 2f * u : 1.6f * u;
            var screen = new RectangleF(bezel.X + inset, bezel.Y + inset, bezel.Width - 2 * inset, bezel.Height - 2 * inset);
            using (var p = Rounded(screen, 1.2f * u))
            using (var b = new LinearGradientBrush(screen, Color.FromArgb(25, 118, 210), Color.FromArgb(94, 53, 177), 35f))
                g.FillPath(b, p);
            // settings gear (white, with a hole showing the screen)
            var c = new PointF(16 * u, 13.25f * u);
            bool small = n <= 20;
            int teeth = small ? 6 : 8;
            float outer = (small ? 7.2f : 6.9f) * u, root = (small ? 5.3f : 5.1f) * u, hole = (small ? 2.3f : 2.2f) * u;
            double period = 2 * Math.PI / teeth, tip = period * 0.17, foot = period * 0.27;
            var pts = new List<PointF>();
            for (int i = 0; i < teeth; i++)
            {
                double a0 = i * period - Math.PI / 2;
                pts.Add(Polar(c, root, a0 - foot));
                pts.Add(Polar(c, outer, a0 - tip));
                pts.Add(Polar(c, outer, a0 + tip));
                pts.Add(Polar(c, root, a0 + foot));
                for (int k = 1; k < 4; k++) pts.Add(Polar(c, root, a0 + foot + (period - 2 * foot) * k / 4));
            }
            using (var gear = new GraphicsPath(FillMode.Alternate))
            using (var white = new SolidBrush(Color.FromArgb(240, 243, 248)))
            {
                gear.AddPolygon(pts.ToArray());
                gear.AddEllipse(c.X - hole, c.Y - hole, 2 * hole, 2 * hole);
                g.FillPath(white, gear);
            }
        }
        return bmp;
    }

    // Classic DIB entry: BITMAPINFOHEADER + bottom-up BGRA + empty AND mask.
    static byte[] Dib(Bitmap bmp)
    {
        int n = bmp.Width;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = n - 1; y >= 0; y--)
            for (int x = 0; x < n; x++)
            {
                var px = bmp.GetPixel(x, y);
                w.Write(px.B); w.Write(px.G); w.Write(px.R); w.Write(px.A);
            }
        int maskRow = ((n + 31) / 32) * 4;
        w.Write(new byte[maskRow * n]);
        return ms.ToArray();
    }

    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("MakeIcon.exe <out.ico>"); return 1; }
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (var n in sizes)
            using (var bmp = Draw(n))
            {
                if (n >= 32)   // PNG-compressed (Vista+); the small ones stay classic for the tray
                {
                    var png = new MemoryStream();
                    bmp.Save(png, ImageFormat.Png);
                    images.Add(png.ToArray());
                }
                else images.Add(Dib(bmp));
            }
        using (var f = new BinaryWriter(File.Create(args[0])))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                f.Write((byte)0); f.Write((byte)0);
                f.Write((short)1); f.Write((short)32);
                f.Write(images[i].Length); f.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) f.Write(img);
        }
        // Preview: 256 px plus 48/32/16 px enlarged (nearest neighbour) to judge the small sizes.
        using (var preview = new Bitmap(456, 256))
        using (var g = Graphics.FromImage(preview))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var big = Draw(256)) g.DrawImage(big, 0, 0, 256, 256);
            using (var s48 = Draw(48)) g.DrawImage(s48, 264, 0, 96, 96);
            using (var s32 = Draw(32)) g.DrawImage(s32, 264, 104, 128, 128);
            using (var s16 = Draw(16)) g.DrawImage(s16, 368, 0, 64, 64);
            preview.Save(Path.ChangeExtension(args[0], ".preview.png"), ImageFormat.Png);
        }
        return 0;
    }
}
