using Autodesk.Revit.DB;

namespace PublicAddress.Revit
{
    internal static class U
    {
        public static double ToM(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters);
        public static double ToFt(double m) => UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters);
        public static double ToM2(double ft2) => UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMeters);
    }
}
