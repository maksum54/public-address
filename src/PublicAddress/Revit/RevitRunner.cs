using System;
using Autodesk.Revit.UI;

namespace PublicAddress.Revit
{
    /// <summary>
    /// Menjalankan kode Revit API dari window modeless lewat ExternalEvent,
    /// sehingga transaksi & PickObject selalu berada di API context.
    /// Harus dibuat di dalam API context (mis. dari IExternalCommand.Execute).
    /// </summary>
    public class RevitRunner : IExternalEventHandler
    {
        readonly ExternalEvent _ev;
        Action<UIApplication> _action;

        public RevitRunner() { _ev = ExternalEvent.Create(this); }

        public void Run(Action<UIApplication> action)
        {
            _action = action;
            _ev.Raise();
        }

        public void Execute(UIApplication app)
        {
            var a = _action;
            _action = null;
            try { a?.Invoke(app); }
            catch (Exception ex) { TaskDialog.Show("Public Address", ex.Message); }
        }

        public string GetName() => "Public Address Runner";
    }
}
