using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PublicAddress.UI;

namespace PublicAddress
{
    public class App : IExternalApplication
    {
        const string TabName = "Public Address";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); } catch { /* tab sudah ada */ }
            var panel = app.CreateRibbonPanel(TabName, "PA Design");

            var btn = new PushButtonData("PA_Calc", "PA\nCalculation",
                typeof(App).Assembly.Location, typeof(PaCommand).FullName)
            {
                ToolTip = "Hitung & tempatkan speaker Public Address",
                LongDescription =
                    "Ceiling speaker: jumlah & jarak per Space dari coverage angle dan tinggi plafon.\n" +
                    "Horn speaker: tempel di face dinding, radius SPL (dB) digambar di plan.\n" +
                    "Rekap: total watt per level dan ukuran amplifier 100 V.",
                LargeImage = IconFactory.Speaker(32),
                Image = IconFactory.Speaker(16),
            };
            panel.AddItem(btn);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    public class PaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var win = new MainWindow(data.Application);
                new System.Windows.Interop.WindowInteropHelper(win).Owner = data.Application.MainWindowHandle;
                win.Show();   // modeless: aksi Revit dijalankan lewat ExternalEvent (RevitRunner)
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
