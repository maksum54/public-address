using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PublicAddress.UI
{
    /// <summary>Ikon speaker digambar lewat kode (tanpa file gambar).</summary>
    public static class IconFactory
    {
        public static BitmapSource Speaker(int size)
        {
            var dv = new DrawingVisual();
            double s = size / 32.0;
            using (var dc = dv.RenderOpen())
            {
                var bg = new LinearGradientBrush(Color.FromRgb(0x1E, 0x88, 0xE5), Color.FromRgb(0x0D, 0x47, 0xA1), 90);
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, size, size), 6 * s, 6 * s);

                // badan speaker
                var body = new StreamGeometry();
                using (var g = body.Open())
                {
                    g.BeginFigure(new Point(6 * s, 12 * s), true, true);
                    g.LineTo(new Point(11 * s, 12 * s), true, false);
                    g.LineTo(new Point(17 * s, 7 * s), true, false);
                    g.LineTo(new Point(17 * s, 25 * s), true, false);
                    g.LineTo(new Point(11 * s, 20 * s), true, false);
                    g.LineTo(new Point(6 * s, 20 * s), true, false);
                }
                dc.DrawGeometry(Brushes.White, null, body);

                // gelombang suara
                var pen = new Pen(Brushes.White, 2.2 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                foreach (var r in new[] { 5.0, 9.0 })
                {
                    var arc = new StreamGeometry();
                    using (var g = arc.Open())
                    {
                        g.BeginFigure(new Point((17 + r * 0.55) * s, (16 - r * 0.83) * s), false, false);
                        g.ArcTo(new Point((17 + r * 0.55) * s, (16 + r * 0.83) * s), new Size(r * s, r * s), 0, false,
                            SweepDirection.Clockwise, true, false);
                    }
                    dc.DrawGeometry(null, pen, arc);
                }
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}
