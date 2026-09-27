using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PublicAddress.Core;

namespace PublicAddress.Revit
{
    public class CeilingPlacement
    {
        public ElementId LevelId;
        public double CeilingHeightM;
        public List<P2> PointsM;
        public string Comment;
    }

    public class HornSettings
    {
        public SpeakerSpec Speaker;
        public double TapW;
        public double MountHeightM;
        public double EarHeightM;
        public List<double> DbLevels;   // mis. 99, 94, 90
        public int CountPerWall;        // 0 = otomatis
        public bool DrawRadius;
    }

    public class HornResult
    {
        public int Placed, Walls, Skipped;
        public string Message;
    }

    class WallFaceFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Wall;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    class SpaceFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Autodesk.Revit.DB.Mechanical.Space;
        public bool AllowReference(Reference r, XYZ p) => false;
    }

    public static class Placement
    {
        /// <summary>User mengklik satu Space di view. Null bila batal.</summary>
        public static ElementId PickSpace(UIDocument uidoc)
        {
            try
            {
                return uidoc.Selection.PickObject(ObjectType.Element, new SpaceFilter(),
                    "Klik Space yang akan diberi horn speaker").ElementId;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        static void Activate(FamilySymbol s)
        {
            if (!s.IsActive) { s.Activate(); s.Document.Regenerate(); }
        }

        public static int PlaceCeiling(Document doc, FamilySymbol symbol, IEnumerable<CeilingPlacement> items)
        {
            int n = 0;
            using var t = new Transaction(doc, "PA - Tempatkan Ceiling Speaker");
            t.Start();
            Activate(symbol);
            foreach (var it in items)
            {
                if (doc.GetElement(it.LevelId) is not Level lvl) continue;
                foreach (var p in it.PointsM)
                {
                    var loc = new XYZ(U.ToFt(p.X), U.ToFt(p.Y), lvl.Elevation);
                    var fi = doc.Create.NewFamilyInstance(loc, symbol, lvl, StructuralType.NonStructural);
                    var off = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)
                              ?? fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                    if (off != null && !off.IsReadOnly) off.Set(U.ToFt(it.CeilingHeightM));
                    RevitData.SetComments(fi, it.Comment);
                    n++;
                }
            }
            t.Commit();
            return n;
        }

        /// <summary>User memilih face dinding, horn ditempel di face tsb. Harus dipanggil saat window disembunyikan.</summary>
        public static HornResult PlaceHorns(UIDocument uidoc, FamilySymbol symbol, HornSettings hs)
        {
            var doc = uidoc.Document;
            var res = new HornResult();
            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(ObjectType.Face, new WallFaceFilter(),
                    "Klik face dinding tempat horn dipasang (sisi yang menghadap area), lalu tekan Finish");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                res.Message = "Dibatalkan.";
                return res;
            }

            var spk = hs.Speaker;
            double halfH = hs.Speaker.CoverageHDeg / 2 * Math.PI / 180;
            double dh = hs.MountHeightM - hs.EarHeightM;
            var reaches = hs.DbLevels
                .Select(db => (db, reach: Acoustics.HorizontalReach(Acoustics.DistanceForSpl(spk.SensitivityDb, hs.TapW, db), dh)))
                .Where(x => x.reach > 0)
                .ToList();
            double designReach = reaches.Count > 0 ? reaches[0].reach : 0;
            double coverWidth = 2 * designReach * Math.Tan(halfH);

            var view = doc.ActiveView;
            bool canDraw = hs.DrawRadius && view is ViewPlan;

            using var t = new Transaction(doc, "PA - Tempatkan Horn Speaker");
            t.Start();
            Activate(symbol);

            foreach (var r in refs)
            {
                var wall = doc.GetElement(r) as Wall;
                if (wall?.GetGeometryObjectFromReference(r) is not PlanarFace face ||
                    Math.Abs(face.FaceNormal.Z) > 0.1)
                {
                    res.Skipped++;
                    continue;
                }
                res.Walls++;

                var normal = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, 0).Normalize();
                var along = XYZ.BasisZ.CrossProduct(normal).Normalize();
                var origin = face.Origin;

                double tMin = double.MaxValue, tMax = double.MinValue, zMin = double.MaxValue, zMax = double.MinValue;
                foreach (EdgeArray loop in face.EdgeLoops)
                foreach (Edge e in loop)
                foreach (var p in e.Tessellate())
                {
                    var tt = (p - origin).DotProduct(along);
                    tMin = Math.Min(tMin, tt); tMax = Math.Max(tMax, tt);
                    zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z);
                }

                var lvl = doc.GetElement(wall.LevelId) as Level;
                double z = (lvl?.Elevation ?? zMin) + U.ToFt(hs.MountHeightM);
                z = Math.Max(zMin + 0.01, Math.Min(zMax - 0.01, z));

                double lenM = U.ToM(tMax - tMin);
                int n = hs.CountPerWall > 0
                    ? hs.CountPerWall
                    : Math.Max(1, coverWidth > 0 ? (int)Math.Ceiling(lenM / coverWidth) : 1);

                for (int i = 0; i < n; i++)
                {
                    double tt = tMin + (tMax - tMin) * (i + 0.5) / n;
                    var loc = origin + along * tt + XYZ.BasisZ * (z - origin.Z);
                    var fi = symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted
                        ? doc.Create.NewFamilyInstance(loc, symbol, along, wall, StructuralType.NonStructural)
                        : doc.Create.NewFamilyInstance(r, loc, along, symbol);
                    var info = reaches.Count > 0 ? $"{reaches[0].db:0}dB@{reaches[0].reach:0.0}m" : "";
                    RevitData.SetComments(fi, new PaTag { Model = spk.Model, TapW = hs.TapW, Info = info }.Encode());
                    res.Placed++;

                    if (canDraw) DrawRadius(doc, (ViewPlan)view, loc, normal, along, halfH, reaches);
                }
            }
            t.Commit();

            res.Message = $"{res.Placed} horn ditempatkan di {res.Walls} dinding." +
                          (res.Skipped > 0 ? $" {res.Skipped} face dilewati (bukan face vertikal datar)." : "") +
                          (hs.DrawRadius && !canDraw ? " Radius dB tidak digambar: buka Floor Plan terlebih dulu." : "");
            return res;
        }

        static readonly Color[] Palette =
        {
            new Color(0, 160, 70),    // hijau
            new Color(230, 160, 0),   // oranye
            new Color(210, 40, 40),   // merah
            new Color(40, 90, 210),   // biru
        };

        static void DrawRadius(Document doc, ViewPlan view, XYZ loc, XYZ normal, XYZ along, double halfH,
                               List<(double db, double reach)> reaches)
        {
            double zv = view.GenLevel?.Elevation ?? view.Origin.Z;
            var c = new XYZ(loc.X, loc.Y, zv);
            var textType = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            for (int i = 0; i < reaches.Count; i++)
            {
                var (db, reachM) = reaches[i];
                double rFt = U.ToFt(reachM);
                var ogs = new OverrideGraphicSettings()
                    .SetProjectionLineColor(Palette[i % Palette.Length])
                    .SetProjectionLineWeight(3);

                var arc = Arc.Create(c, rFt, -halfH, halfH, normal, along);
                var dc = doc.Create.NewDetailCurve(view, arc);
                view.SetElementOverrides(dc.Id, ogs);

                var tip = c + normal * rFt;
                var tn = TextNote.Create(doc, view.Id, tip, $"{db:0} dB = {reachM:0.0} m", textType);
                view.SetElementOverrides(tn.Id, ogs);
            }

            if (reaches.Count > 0)
            {
                double rMax = U.ToFt(reaches.Max(x => x.reach));
                foreach (var sign in new[] { -1.0, 1.0 })
                {
                    var dir = normal * Math.Cos(halfH) + along * (sign * Math.Sin(halfH));
                    var ln = doc.Create.NewDetailCurve(view, Line.CreateBound(c, c + dir * rMax));
                    view.SetElementOverrides(ln.Id, new OverrideGraphicSettings()
                        .SetProjectionLineColor(new Color(128, 128, 128)));
                }
            }
        }
    }
}
