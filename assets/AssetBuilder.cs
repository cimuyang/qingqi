using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class AssetBuilder
{
    static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); p.AddArc(x, y, r * 2, r * 2, 180, 90); p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90); p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90); p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90); p.CloseFigure(); return p;
    }
    static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent); g.ScaleTransform(size / 64f, size / 64f);
            using (var box = Round(3, 3, 58, 58, 15))
            using (var fill = new LinearGradientBrush(new Point(5, 3), new Point(55, 62), Color.FromArgb(255, 255, 255), Color.FromArgb(232, 236, 243)))
            using (var outline = new Pen(Color.FromArgb(162, 169, 181), 1.5f)) { g.FillPath(fill, box); g.DrawPath(outline, box); }
            using (var p = new Pen(Color.FromArgb(129, 138, 155), 1.1f))
            {
                p.StartCap = p.EndCap = LineCap.Round; g.DrawEllipse(p, 10, 9, 44, 44);
                for (int n = 0; n < 24; n++)
                {
                    double a = n * Math.PI / 12; g.DrawLine(p, (float)(32 + Math.Cos(a) * 5), (float)(31 + Math.Sin(a) * 5), (float)(32 + Math.Cos(a + .25) * 17), (float)(31 + Math.Sin(a + .25) * 17));
                }
                g.DrawBezier(p, 31, 9, 2, 14, 14, 56, 40, 50); g.DrawBezier(p, 11, 35, 21, 64, 61, 44, 52, 20); g.DrawBezier(p, 49, 15, 21, 0, 5, 39, 19, 47);
                using (var dot = new SolidBrush(p.Color)) g.FillEllipse(dot, 30, 29, 4, 4);
                PointF[] play = { new PointF(46, 45), new PointF(46, 54), new PointF(54, 49.5f) };
                using (var white = new SolidBrush(Color.FromArgb(245, 247, 251))) g.FillPolygon(white, play); g.DrawPolygon(p, play);
            }
        }
        return bitmap;
    }
    public static int Main(string[] args)
    {
        string folder = args[0]; var images = new List<byte[]>(); int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        foreach (int size in sizes) using (var b = Draw(size)) using (var m = new MemoryStream()) { b.Save(m, ImageFormat.Png); images.Add(m.ToArray()); }
        using (var f = File.Create(Path.Combine(folder, "orbit.ico"))) using (var w = new BinaryWriter(f))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length); int offset = 6 + 16 * sizes.Length;
            for (int n = 0; n < sizes.Length; n++) { w.Write((byte)(sizes[n] == 256 ? 0 : sizes[n])); w.Write((byte)(sizes[n] == 256 ? 0 : sizes[n])); w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32); w.Write(images[n].Length); w.Write(offset); offset += images[n].Length; }
            foreach (var image in images) w.Write(image);
        }
        return 0;
    }
}
