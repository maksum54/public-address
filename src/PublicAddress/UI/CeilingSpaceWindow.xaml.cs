using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PublicAddress.Core;

namespace PublicAddress.UI
{
    /// <summary>Input per Space yang dibawa dari satu Space ke Space berikutnya.</summary>
    public class CeilSettings
    {
        public double CeilingH = 3, Ear = 1.2, Noise = 50, Margin = 10;
        public SpacingMethod Method = SpacingMethod.NoOverlap;
        public double AngleFactor = 1.5, MaxAngleDeg = 140;
    }

    /// <summary>
    /// Seperti add-in fire alarm: satu Space diklik, user isi input, hasil & preview langsung dihitung,
    /// lalu Tempatkan / Simpan &amp; pilih Space lain / Selesai.
    /// </summary>
    public partial class CeilingSpaceWindow : Window
    {
        public enum UserAction { Close, PickAnother, Place }

        public UserAction Action { get; private set; } = UserAction.Close;
        public CeilingRow Row { get; }
        public CeilSettings Settings { get; private set; }

        readonly SpeakerSpec _spk;
        readonly bool _loading;

        public CeilingSpaceWindow(CeilingRow row, SpeakerSpec spk, CeilSettings s)
        {
            InitializeComponent();
            Row = row;
            _spk = spk;
            Settings = s;
            _loading = true;
            Icon = IconFactory.Speaker(32);

            var b = row.Space.Boundary;
            double w = b.Count > 0 ? b.Max(p => p.X) - b.Min(p => p.X) : 0;
            double l = b.Count > 0 ? b.Max(p => p.Y) - b.Min(p => p.Y) : 0;
            TxtSpaceName.Text = $"{row.Number} - {row.Name}";
            TxtSpaceInfo.Text = $"Level: {row.Level} · Speaker: {spk.Model} ({spk.SpacingAngleDeg}°, {spk.SensitivityDb} dB)";
            TxtLW.Text = $"{F(w)} × {F(l)} m";
            TxtArea.Text = $"{F(row.Area)} m²";

            TbCeiling.Text = F(s.CeilingH);
            TbEar.Text = F(s.Ear);
            TbNoise.Text = F(s.Noise);
            TbMargin.Text = F(s.Margin);
            TbFactor.Text = F(s.AngleFactor);
            TbMaxAngle.Text = F(s.MaxAngleDeg);
            TbManual.Text = row.ManualQty.ToString();
            CbMethod.SelectedItem = CbMethod.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Method.ToString())
                                    ?? CbMethod.Items[0];
            _loading = false;
            Loaded += (_, _) => { TbCeiling.Focus(); TbCeiling.SelectAll(); Recalculate(); };
        }

        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static bool TryNum(TextBox tb, out double v)
        {
            bool ok = double.TryParse((tb.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            tb.BorderBrush = ok ? new SolidColorBrush(Color.FromRgb(0xCB, 0xD2, 0xDB)) : Brushes.Red;
            return ok;
        }

        SpacingMethod SelectedMethod =>
            Enum.Parse<SpacingMethod>((string)((CbMethod.SelectedItem as ComboBoxItem)?.Tag ?? "NoOverlap"));

        void Input_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading) Recalculate();
        }

        void Recalculate()
        {
            if (TxtQty == null) return;
            var m = SelectedMethod;
            SplOptions.Visibility = m == SpacingMethod.SplLimited ? Visibility.Visible : Visibility.Collapsed;

            bool ok = TryNum(TbCeiling, out var ceil) & TryNum(TbEar, out var ear) & TryNum(TbNoise, out var noise)
                      & TryNum(TbMargin, out var margin) & TryNum(TbManual, out var manual);
            double factor = 1.5, maxAng = 140;
            if (m == SpacingMethod.SplLimited) ok &= TryNum(TbFactor, out factor) & TryNum(TbMaxAngle, out maxAng);
            ok &= ceil > 0 && factor > 0 && maxAng > 0 && manual >= 0;
            BtnPlace.IsEnabled = ok;
            if (!ok)
            {
                TxtQty.Text = "-";
                TxtDetail.Text = "Masukkan angka yang valid (contoh 3.5).";
                TxtWarning.Text = "";
                Preview.Children.Clear();
                return;
            }

            Settings = new CeilSettings
            {
                CeilingH = ceil, Ear = ear, Noise = noise, Margin = margin, Method = m,
                AngleFactor = factor, MaxAngleDeg = maxAng,
            };
            Row.CeilingH = ceil;
            Row.Noise = noise;
            Row.ManualQty = (int)Math.Round(manual);
            Row.Opt = Settings;
            Row.Recalc(_spk);

            TxtQty.Text = Row.Count.ToString();
            string how = m switch
            {
                SpacingMethod.SplLimited =>
                    $"r maks = h × tan({F(Math.Min(_spk.SpacingAngleDeg * factor, Math.Min(maxAng, 170)))}° / 2), " +
                    $"tap terkecil yang SPL ≥ {F(noise + margin)} dB di tepi r\n",
                _ => Row.TableHFt > 0 ? $"h ≈ {F(Row.TableHFt)} ft → baris tabel datasheet\n" : "r = h × tan(sudut / 2)\n",
            };
            TxtDetail.Text =
                $"h = {F(ceil)} − {F(ear)} = {Row.H:0.00} m\n" + how +
                $"r = {Row.R:0.00} m · spacing = {Row.S:0.00} m\n" +
                $"Grid {Row.GridSize} = {Row.Count} speaker · jarak {Row.DxM:0.00} × {Row.DyM:0.00} m\n" +
                $"Tap {F(Row.Tap)} W · SPL {Row.Spl:0.0} dB · total {F(Row.TotalW)} W\n" +
                (Row.AreaCount > 0 ? $"Pembanding metode luas: {Row.AreaCount} unit" : "");
            TxtWarning.Text = Row.Ok == true ? "" : "⚠ " + Row.Status;
            DrawPreview();
        }

        void Preview_SizeChanged(object sender, SizeChangedEventArgs e) => DrawPreview();

        void DrawPreview()
        {
            if (Preview.ActualWidth < 10) return;
            CeilingPreview.Draw(Preview, Row);
        }

        void Place_Click(object sender, RoutedEventArgs e)
        {
            Recalculate();
            if (!BtnPlace.IsEnabled) return;
            if (Row.Count == 0)
            {
                MessageBox.Show(this, "Jumlah speaker 0 — cek input.", "Public Address", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Action = UserAction.Place; Close();
        }

        void Pick_Click(object sender, RoutedEventArgs e) { Recalculate(); Action = UserAction.PickAnother; Close(); }
        void Close_Click(object sender, RoutedEventArgs e) { Action = UserAction.Close; Close(); }
    }
}
