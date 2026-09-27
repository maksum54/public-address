using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using PublicAddress.Core;

namespace PublicAddress.Revit
{
    /// <summary>Satu Space beserta boundary-nya (meter).</summary>
    public class SpaceInfo
    {
        public ElementId Id;
        public string Number, Name, LevelName;
        public ElementId LevelId;
        public double AreaM2, HeightM;
        public List<P2> Boundary = new();
        public string Label => $"{LevelName} · {Number} {Name}";
    }

    public class FamilyTypeItem
    {
        public FamilySymbol Symbol;
        public string Display
        {
            get
            {
                var pt = Symbol.Family.FamilyPlacementType switch
                {
                    FamilyPlacementType.WorkPlaneBased => "face-based",
                    FamilyPlacementType.OneLevelBasedHosted => "hosted",
                    FamilyPlacementType.OneLevelBased => "non-hosted",
                    var x => x.ToString(),
                };
                return $"{Symbol.FamilyName} : {Symbol.Name}  [{pt}]";
            }
        }
        public override string ToString() => Display;
    }

    /// <summary>Data yang ditulis ke parameter Comments setiap speaker: "PA|model|tapW|info".</summary>
    public class PaTag
    {
        public const string Prefix = "PA|";
        public string Model; public double TapW; public string Info;

        public string Encode() => $"{Prefix}{Model}|{TapW.ToString(CultureInfo.InvariantCulture)}W|{Info}";

        public static PaTag Decode(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith(Prefix)) return null;
            var p = s.Split('|');
            if (p.Length < 3) return null;
            double.TryParse(p[2].TrimEnd('W'), NumberStyles.Float, CultureInfo.InvariantCulture, out var w);
            return new PaTag { Model = p[1], TapW = w, Info = p.Length > 3 ? p[3] : "" };
        }
    }

    public static class RevitData
    {
        public static List<SpaceInfo> GetSpaces(Document doc)
        {
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            var list = new List<SpaceInfo>();
            foreach (var sp in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_MEPSpaces)
                         .WhereElementIsNotElementType()
                         .OfType<Space>())
            {
                if (sp.Area <= 0 || sp.Location == null) continue;
                var loops = sp.GetBoundarySegments(opt);
                if (loops == null || loops.Count == 0) continue;

                // loop terluar = loop dengan luas terbesar
                List<P2> best = null; double bestA = 0;
                foreach (var loop in loops)
                {
                    var pts = new List<P2>();
                    foreach (var seg in loop)
                    {
                        var tess = seg.GetCurve().Tessellate();
                        for (int i = 0; i < tess.Count - 1; i++)
                            pts.Add(new P2(U.ToM(tess[i].X), U.ToM(tess[i].Y)));
                    }
                    var a = GridLayout.Area(pts);
                    if (a > bestA) { bestA = a; best = pts; }
                }
                if (best == null) continue;

                list.Add(new SpaceInfo
                {
                    Id = sp.Id,
                    Number = sp.Number,
                    Name = sp.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                    LevelName = sp.Level?.Name ?? "",
                    LevelId = sp.LevelId,
                    AreaM2 = U.ToM2(sp.Area),
                    HeightM = Math.Round(U.ToM(sp.UnboundedHeight), 2),
                    Boundary = best,
                });
            }
            return list.OrderBy(s => s.LevelName).ThenBy(s => s.Number).ToList();
        }

        /// <summary>Hanya kategori Communication Devices yang dibaca.</summary>
        public static List<BuiltInCategory> SpeakerCategories() =>
            new() { BuiltInCategory.OST_CommunicationDevices };

        /// <summary>
        /// Semua family type speaker. Horn: face-based / work-plane / wall-hosted diutamakan di atas.
        /// Ceiling: non-hosted (level based) diutamakan. Tidak ada yang disembunyikan.
        /// </summary>
        public static List<FamilyTypeItem> GetCommTypes(Document doc, bool faceBased)
        {
            var cats = new ElementMulticategoryFilter(SpeakerCategories());
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .WherePasses(cats)
                .Cast<FamilySymbol>()
                .Select(s => new FamilyTypeItem { Symbol = s })
                .ToList();

            bool Preferred(FamilyTypeItem t)
            {
                var pt = t.Symbol.Family.FamilyPlacementType;
                return faceBased
                    ? pt == FamilyPlacementType.WorkPlaneBased || pt == FamilyPlacementType.OneLevelBasedHosted
                    : pt == FamilyPlacementType.OneLevelBased;
            }
            return all.OrderByDescending(Preferred).ThenBy(t => t.Display).ToList();
        }

        public static void SetComments(Element e, string text)
        {
            var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (p != null && !p.IsReadOnly) p.Set(text);
        }

        /// <summary>Semua speaker hasil add-in ini (Comments diawali "PA|").</summary>
        public static List<(FamilyInstance inst, PaTag tag, string level)> GetPlacedSpeakers(Document doc)
        {
            var res = new List<(FamilyInstance, PaTag, string)>();
            foreach (var fi in new FilteredElementCollector(doc)
                         .WherePasses(new ElementMulticategoryFilter(SpeakerCategories()))
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>())
            {
                var tag = PaTag.Decode(fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString());
                if (tag == null) continue;
                var lvl = doc.GetElement(fi.LevelId) as Level;
                if (lvl == null && fi.Host is Wall w) lvl = doc.GetElement(w.LevelId) as Level;
                res.Add((fi, tag, lvl?.Name ?? "-"));
            }
            return res;
        }
    }
}
