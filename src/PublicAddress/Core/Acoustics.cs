using System;
using System.Collections.Generic;
using System.Linq;

namespace PublicAddress.Core
{
    public enum SpacingMethod
    {
        CoverageArea,     // N = ceil(luas lantai / (pi r^2))
        EdgeToCenter,     // s = r        (grid kotak)
        MinOverlapSquare, // s = r * sqrt2 (grid kotak)
        MinOverlapHex,    // s = r * sqrt3 (grid hex)
        NoOverlap,        // s = 2r       (grid kotak)
        SplLimited,       // r = jangkauan SPL, dibatasi sudut cone x faktor; s = 2r
    }

    /// <summary>Rumus akustik. Semua jarak dalam meter.</summary>
    public static class Acoustics
    {
        static double Rad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Radius coverage di bidang telinga: r = h * tan(sudut/2).</summary>
        public static double CoverageRadius(double h, double angleDeg) => h * Math.Tan(Rad(angleDeg / 2));

        public static double Spacing(double r, SpacingMethod m) => m switch
        {
            SpacingMethod.EdgeToCenter => r,
            SpacingMethod.MinOverlapSquare => r * Math.Sqrt(2),
            SpacingMethod.MinOverlapHex => r * Math.Sqrt(3),
            SpacingMethod.NoOverlap => 2 * r,
            _ => r,
        };

        /// <summary>
        /// Metode SPL dibatasi: r_maks = h * tan(min(sudut * faktor, sudutMaks) / 2).
        /// Tap terkecil yang SPL-nya masih >= target sampai tepi r_maks dipakai; bila tap terbesar
        /// pun tidak cukup, r = jangkauan SPL tap terbesar (bisa 0 bila SPL di bawah speaker pun kurang).
        /// </summary>
        public static double SplLimitedRadius(SpeakerSpec spk, double h, double targetDb, double angleFactor,
            double maxAngleDeg, out double tap, out bool ok)
        {
            double angle = Math.Min(spk.SpacingAngleDeg * angleFactor, Math.Min(maxAngleDeg, 170));
            double rMax = CoverageRadius(h, angle);
            foreach (var t in spk.TapsW)
            {
                if (HorizontalReach(DistanceForSpl(spk.SensitivityDb, t, targetDb), h) >= rMax)
                {
                    tap = t; ok = true;
                    return rMax;
                }
            }
            tap = spk.TapsW.Count > 0 ? spk.TapsW.Last() : 1;
            ok = false;
            return HorizontalReach(DistanceForSpl(spk.SensitivityDb, tap, targetDb), h);
        }

        /// <summary>SPL = Sens + 10log(P) - 20log(d).</summary>
        public static double Spl(double sensitivityDb, double powerW, double distanceM) =>
            sensitivityDb + 10 * Math.Log10(powerW) - 20 * Math.Log10(Math.Max(distanceM, 0.01));

        /// <summary>Jarak (miring) saat SPL turun ke targetDb.</summary>
        public static double DistanceForSpl(double sensitivityDb, double powerW, double targetDb) =>
            Math.Pow(10, (sensitivityDb + 10 * Math.Log10(powerW) - targetDb) / 20.0);

        /// <summary>Jarak horizontal dari dinding, dikoreksi beda tinggi horn - telinga. 0 bila tak tercapai.</summary>
        public static double HorizontalReach(double slantDistance, double heightDiff)
        {
            var d2 = slantDistance * slantDistance - heightDiff * heightDiff;
            return d2 > 0 ? Math.Sqrt(d2) : 0;
        }

        /// <summary>Tap terkecil yang memenuhi target; null bila tap terbesar pun tidak cukup.</summary>
        public static double? PickTap(SpeakerSpec spk, double distanceM, double targetDb)
        {
            foreach (var t in spk.TapsW)
                if (Spl(spk.SensitivityDb, t, distanceM) >= targetDb) return t;
            return null;
        }

        public const double Ft = 0.3048, SqFt = 0.09290304;

        /// <summary>Baris tabel datasheet dengan h-l (ft) terdekat. Null bila speaker tanpa tabel.</summary>
        public static double[] TableRow(SpeakerSpec spk, double hM)
        {
            if (spk.Table == null || spk.Table.Count == 0) return null;
            double hFt = hM / Ft;
            return spk.Table.OrderBy(r => Math.Abs(r[0] - hFt)).First();
        }

        static readonly double[] AmpSizes = { 60, 120, 240, 360, 480, 600, 900, 1200, 2400 };

        public static double AmplifierSize(double totalW, double headroom = 1.25)
        {
            var need = totalW * headroom;
            foreach (var a in AmpSizes) if (a >= need) return a;
            return Math.Ceiling(need / 100) * 100;
        }
    }

    public struct P2
    {
        public double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
    }

    public static class GridLayout
    {
        public static bool Inside(IList<P2> poly, P2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                    p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    c = !c;
            }
            return c;
        }

        /// <summary>
        /// Titik speaker di dalam polygon (satuan meter). Grid di-center-kan pada bounding box,
        /// titik di luar boundary dibuang. Minimal 1 speaker per ruang.
        /// </summary>
        public static List<P2> Layout(IList<P2> poly, double s, bool hex, bool full = true)
        {
            var res = new List<P2>();
            if (poly.Count < 3 || s <= 0) return res;
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
            double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double w = maxX - minX, l = maxY - minY;

            double dx = s, dy = hex ? s * Math.Sqrt(3) / 2 : s;
            // Penuh    : ceil  -> seluruh ruang ter-cover sampai dinding
            // Terpusat : floor -> lingkaran coverage muat di dalam ruang (seperti Biamp "Centered")
            int nx = Math.Max(1, full ? (int)Math.Ceiling(w / dx - 1e-6) : (int)Math.Floor(w / dx + 1e-6));
            int ny = Math.Max(1, full ? (int)Math.Ceiling(l / dy - 1e-6) : (int)Math.Floor(l / dy + 1e-6));
            double x0 = minX + (w - (nx - 1) * dx) / 2;
            double y0 = minY + (l - (ny - 1) * dy) / 2;

            for (int j = 0; j < ny; j++)
            {
                double off = hex && (j % 2 == 1) ? dx / 2 : 0;
                int cols = hex && (j % 2 == 1) ? nx - 1 : nx;
                if (cols < 1) cols = 1;
                for (int i = 0; i < cols; i++)
                {
                    var p = new P2(x0 + off + i * dx, y0 + j * dy);
                    if (Inside(poly, p)) res.Add(p);
                }
            }

            if (res.Count == 0)
            {
                var c = new P2((minX + maxX) / 2, (minY + maxY) / 2);
                if (!Inside(poly, c))
                {
                    // ruang bentuk L/U: cari titik tengah terdekat antar vertex yang ada di dalam
                    c = poly[0];
                    for (int i = 0; i < poly.Count; i++)
                    for (int k = i + 2; k < poly.Count; k++)
                    {
                        var m = new P2((poly[i].X + poly[k].X) / 2, (poly[i].Y + poly[k].Y) / 2);
                        if (Inside(poly, m)) { c = m; i = poly.Count; break; }
                    }
                }
                res.Add(c);
            }
            return res;
        }

        /// <summary>
        /// Metode luas: N speaker dibagi rata dalam grid kolom x baris mengikuti proporsi ruang,
        /// speaker di tengah tiap sel. Sel yang jatuh di luar boundary dibuang.
        /// </summary>
        public static List<P2> LayoutCount(IList<P2> poly, int n)
        {
            var res = new List<P2>();
            if (poly.Count < 3 || n < 1) return res;
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
            double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double w = maxX - minX, l = maxY - minY;
            int nx = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n * w / Math.Max(l, 0.01))));
            nx = Math.Min(nx, n);
            int ny = (int)Math.Ceiling((double)n / nx);
            for (int j = 0; j < ny && res.Count < n; j++)
            {
                int cols = Math.Min(nx, n - j * nx);   // baris terakhir boleh kurang
                for (int i = 0; i < cols; i++)
                {
                    var p = new P2(minX + w * (i + 0.5) / cols, minY + l * (j + 0.5) / ny);
                    if (Inside(poly, p)) res.Add(p);
                }
            }
            if (res.Count == 0) res.AddRange(Layout(poly, Math.Max(w, l) * 2, false));
            return res;
        }

        /// <summary>
        /// Grid sel: kolom = ceil(lebar / s), baris = ceil(panjang / s). Speaker di tengah tiap sel,
        /// jadi jarak aktual = lebar / kolom dan speaker terluar ke dinding = setengahnya.
        /// </summary>
        public static List<P2> LayoutCells(IList<P2> poly, double s, out int nx, out int ny)
        {
            var res = new List<P2>();
            nx = ny = 0;
            if (poly.Count < 3 || s <= 0) return res;
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
            double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double w = maxX - minX, l = maxY - minY;
            nx = Math.Max(1, (int)Math.Ceiling(w / s - 1e-6));
            ny = Math.Max(1, (int)Math.Ceiling(l / s - 1e-6));
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var p = new P2(minX + w * (i + 0.5) / nx, minY + l * (j + 0.5) / ny);
                if (Inside(poly, p)) res.Add(p);
            }
            if (res.Count == 0) res.AddRange(Layout(poly, Math.Max(w, l) * 2, false));
            return res;
        }

        public static double Area(IList<P2> poly)
        {
            double a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += (poly[j].X + poly[i].X) * (poly[j].Y - poly[i].Y);
            return Math.Abs(a / 2);
        }
    }
}
