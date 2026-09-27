using System;
using System.Collections.Generic;
using System.Linq;

namespace PublicAddress.Core
{
    public class HornRecommendation
    {
        public double LengthM, WidthM;   // sisi panjang & pendek ruang
        public int Rows;                 // 1 = satu dinding panjang, 2 = dua dinding berhadapan
        public int PerWall;
        public int Total => Rows * PerWall;
        public double TapW;
        public double ThrowM;            // jarak horizontal yang harus dicapai tiap horn
        public double SplAtThrow;
        public bool Ok;
        public string Text;
    }

    public static class HornAdvisor
    {
        /// <summary>Dimensi persegi panjang terkecil yang membungkus polygon (sisi panjang, sisi pendek).</summary>
        public static (double L, double W) Dimensions(IList<P2> poly)
        {
            double bestA = double.MaxValue, bl = 0, bw = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) continue;
                double ux = dx / len, uy = dy / len;
                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                foreach (var p in poly)
                {
                    double u = p.X * ux + p.Y * uy, v = -p.X * uy + p.Y * ux;
                    minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
                }
                double w1 = maxU - minU, w2 = maxV - minV;
                if (w1 * w2 < bestA) { bestA = w1 * w2; bl = Math.Max(w1, w2); bw = Math.Min(w1, w2); }
            }
            return (bl, bw);
        }

        /// <summary>
        /// Horn dipasang di dinding panjang, menembak ke arah sisi pendek.
        /// 1 baris bila SPL target tercapai sampai dinding seberang, selain itu 2 baris berhadapan (throw = W/2).
        /// Jumlah per dinding = ceil(L / lebar sebaran pada jarak throw). Tap = tap terkecil yang memenuhi target.
        /// </summary>
        public static HornRecommendation Recommend(SpeakerSpec spk, IList<P2> poly, double mountM, double earM, double targetDb)
        {
            var (L, W) = Dimensions(poly);
            double dh = mountM - earM;
            double half = spk.CoverageHDeg / 2 * Math.PI / 180;

            HornRecommendation Try(int rows)
            {
                double thr = rows == 1 ? W : W / 2;
                double slant = Math.Sqrt(thr * thr + dh * dh);
                var tap = Acoustics.PickTap(spk, slant, targetDb);
                double t = tap ?? spk.TapsW.Last();
                double width = Math.Max(2 * thr * Math.Tan(half), 0.5);
                return new HornRecommendation
                {
                    LengthM = L, WidthM = W, Rows = rows, ThrowM = thr, TapW = t,
                    PerWall = Math.Max(1, (int)Math.Ceiling(L / width)),
                    SplAtThrow = Acoustics.Spl(spk.SensitivityDb, t, slant),
                    Ok = tap != null,
                };
            }

            var r = Try(1);
            if (!r.Ok) r = Try(2);
            r.Text =
                $"Ruang ±{L:0.0} × {W:0.0} m → {(r.Rows == 1 ? "1 dinding panjang" : "2 dinding panjang saling berhadapan")}, " +
                $"{r.PerWall} horn/dinding = {r.Total} horn {spk.Model} @ {r.TapW:0.#} W. " +
                $"SPL di jarak {r.ThrowM:0.0} m = {r.SplAtThrow:0.0} dB " +
                (r.Ok ? $"(≥ {targetDb:0} dB OK)." : $"(< {targetDb:0} dB, pilih horn lebih besar / tambah baris).");
            return r;
        }
    }
}
