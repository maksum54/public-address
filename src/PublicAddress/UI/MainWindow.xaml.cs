using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using Microsoft.Win32;
using PublicAddress.Core;
using PublicAddress.Revit;

namespace PublicAddress.UI
{
    public class CeilingRow : INotifyPropertyChanged
    {
        public SpaceInfo Space;
        public List<P2> Points = new();

        bool _include = true;
        public bool Include { get => _include; set { _include = value; On(); } }
        public string Level => Space.LevelName;
        public string Number => Space.Number;
        public string Name => Space.Name;
        public double Area => Space.AreaM2;
        public double CeilingH { get; set; }
        public double Noise { get; set; }

        public double H { get; private set; }
        public double R { get; private set; }
        public double S { get; private set; }
        public double CoverageM2 { get; private set; }
        public int Count { get; private set; }
        public double Tap { get; private set; }
        public double Spl { get; private set; }
        public double TotalW => Count * Tap;
        public string Status { get; private set; } = "-";
        public bool? Ok { get; private set; }

        public void Calc(SpeakerSpec spk, SpacingMethod m, double ear, double margin, bool full)
        {
            H = CeilingH - ear;
            if (H <= 0.1)
            {
                R = S = Spl = Tap = CoverageM2 = 0; Count = 0; Points.Clear();
                Status = "Plafon ≤ telinga"; Ok = false;
            }
            else
            {
                R = Acoustics.CoverageRadius(H, spk.SpacingAngleDeg);
                if (m == SpacingMethod.CoverageArea)
                {
                    // Luas lantai / luas coverage satu speaker (pi r^2), dibulatkan ke atas
                    CoverageM2 = Math.PI * R * R;
                    int n = Math.Max(1, (int)Math.Ceiling(Area / CoverageM2 - 1e-9));
                    Points = GridLayout.LayoutCount(Space.Boundary, n);
                    S = Math.Sqrt(Area / Math.Max(1, Points.Count));
                }
                else
                {
                    CoverageM2 = Math.PI * R * R;
                    S = Acoustics.Spacing(R, m);
                    Points = GridLayout.Layout(Space.Boundary, S, m == SpacingMethod.MinOverlapHex, full);
                }
                Count = Points.Count;
                double target = Noise + margin;
                var tap = Acoustics.PickTap(spk, H, target);
                Tap = tap ?? spk.TapsW.Last();
                Spl = Acoustics.Spl(spk.SensitivityDb, Tap, H);
                Ok = tap != null;
                Status = Ok == true ? "OK" : $"SPL kurang ({target:0} dB)";
            }
            On(null);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void On([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class HornRow { public double Db { get; set; } public double Slant { get; set; } public double Reach { get; set; } public double Width { get; set; } }
    public class RecapRow { public string Level { get; set; } public string Model { get; set; } public double Tap { get; set; } public int Qty { get; set; } public double TotalW { get; set; } public double Amp { get; set; } }

    public partial class MainWindow : Window
    {
        readonly UIApplication _uiapp;
        readonly SpeakerLibrary _lib;
        readonly ObservableCollection<CeilingRow> _allRows = new();
        bool _ready;

        public MainWindow(UIApplication uiapp)
        {
            InitializeComponent();
            _uiapp = uiapp;
            Icon = IconFactory.Speaker(32);
            HeaderIcon.Source = IconFactory.Speaker(32);

            try { _lib = SpeakerLibrary.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal membaca Resources\\speakers.json:\n" + ex.Message, "Public Address");
                _lib = new SpeakerLibrary();
            }

            var doc = uiapp.ActiveUIDocument.Document;

            // Ceiling
            CeilSpeaker.ItemsSource = _lib.OfKind(SpeakerKind.Ceiling).ToList();
            CeilSpeaker.SelectedIndex = 0;
            CeilMethod.SelectedIndex = 4; // Edge to Edge, sama dengan default Biamp
            CeilFamily.ItemsSource = RevitData.GetCommTypes(doc, faceBased: false);
            CeilFamily.SelectedIndex = 0;

            double noise = Num(CeilNoise.Text, 50);
            foreach (var sp in RevitData.GetSpaces(doc))
                _allRows.Add(new CeilingRow { Space = sp, CeilingH = Num(CeilBulkH.Text, 3), Noise = noise });
            var levels = new List<string> { "(Semua level)" };
            levels.AddRange(_allRows.Select(r => r.Level).Distinct());
            CeilLevel.ItemsSource = levels;
            CeilLevel.SelectedIndex = 0;

            // Horn
            var horns = _lib.OfKind(SpeakerKind.Horn).ToList();
            HornSpeaker.ItemsSource = horns;
            HornSpeaker.SelectedItem = horns.FirstOrDefault(h => h.Model.Contains("615")) ?? horns.FirstOrDefault();
            HornFamily.ItemsSource = RevitData.GetCommTypes(doc, faceBased: true);
            HornFamily.SelectedIndex = 0;
            HornSpace.ItemsSource = _allRows.Select(r => r.Space).ToList();

            _ready = true;
            UpdateSpecText();
            CeilRecalc();
            UpdateHorn();
            RecapRefresh();
            Status(_allRows.Count == 0
                ? "Tidak ada MEP Space di model. Buat Space dulu untuk tab Ceiling Speaker."
                : $"{_allRows.Count} Space terbaca.");
        }

        // ---------------- helpers ----------------
        static double Num(string s, double def)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        void Status(string s) => StatusText.Text = s;

        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        void UpdateSpecText()
        {
            if (CeilSpeaker.SelectedItem is SpeakerSpec c)
                CeilSpec.Text = $"{c.CoverageHDeg}° · {c.SensitivityDb} dB · tap {string.Join("/", c.TapsW.Select(F))} W";
            if (HornSpeaker.SelectedItem is SpeakerSpec h)
                HornSpec.Text = $"{h.CoverageHDeg}° H × {h.CoverageVDeg}° V · {h.SensitivityDb} dB (1W/1m) · maks {h.MaxPowerW} W";
        }

        // ---------------- CEILING ----------------
        SpacingMethod Method => Enum.Parse<SpacingMethod>((string)((ComboBoxItem)CeilMethod.SelectedItem).Tag);

        IEnumerable<CeilingRow> VisibleRows => CeilGrid.ItemsSource as IEnumerable<CeilingRow> ?? Enumerable.Empty<CeilingRow>();

        void CeilRecalc()
        {
            if (!_ready || CeilSpeaker.SelectedItem is not SpeakerSpec spk) return;
            double ear = Num(CeilEar.Text, 1.2), margin = Num(CeilMargin.Text, 10);
            bool full = CeilFull.IsChecked == true;
            foreach (var r in VisibleRows) r.Calc(spk, Method, ear, margin, full);
            var sel = VisibleRows.Where(r => r.Include).ToList();
            double w = sel.Sum(r => r.TotalW);
            int bad = sel.Count(r => r.Ok == false);
            CeilSummary.Text = $"{sel.Count} Space · {sel.Sum(r => r.Count)} speaker · {F(w)} W · amplifier ≥ {F(Acoustics.AmplifierSize(w))} W"
                               + (bad > 0 ? $" · {bad} Space SPL kurang" : "");
        }

        void CeilInputChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            UpdateSpecText();
            CeilRecalc();
        }

        void CeilCoverage_Click(object sender, RoutedEventArgs e) => CeilRecalc();

        void CeilLevelChanged(object sender, SelectionChangedEventArgs e)
        {
            var lvl = CeilLevel.SelectedItem as string;
            CeilGrid.ItemsSource = CeilLevel.SelectedIndex <= 0
                ? _allRows.ToList()
                : _allRows.Where(r => r.Level == lvl).ToList();
            CeilRecalc();
        }

        void CeilGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(CeilRecalc));

        void CeilCalc_Click(object sender, RoutedEventArgs e)
        {
            double noise = Num(CeilNoise.Text, 50);
            CeilRecalc();
            Status($"Dihitung. Noise default {F(noise)} dBA bisa diubah per baris di kolom Noise.");
        }

        void CeilSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in VisibleRows) r.Include = CeilAll.IsChecked == true;
            CeilRecalc();
        }

        void CeilBulkHeight_Click(object sender, RoutedEventArgs e)
        {
            double h = Num(CeilBulkH.Text, 3);
            double noise = Num(CeilNoise.Text, 50);
            foreach (var r in VisibleRows.Where(r => r.Include)) { r.CeilingH = h; r.Noise = noise; }
            CeilRecalc();
            CeilGrid.Items.Refresh();
        }

        void CeilPlace_Click(object sender, RoutedEventArgs e)
        {
            CeilRecalc();
            if (CeilFamily.SelectedItem is not FamilyTypeItem fam)
            {
                MessageBox.Show("Belum ada family Communication Devices (non-hosted) di project. Load family ceiling speaker dulu.", "Public Address");
                return;
            }
            var spk = (SpeakerSpec)CeilSpeaker.SelectedItem;
            var rows = VisibleRows.Where(r => r.Include && r.Count > 0).ToList();
            int total = rows.Sum(r => r.Count);
            if (total == 0) { Status("Tidak ada speaker untuk ditempatkan."); return; }
            if (MessageBox.Show($"Tempatkan {total} speaker {spk.Model} ({fam.Display}) di {rows.Count} Space?",
                    "Public Address", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                int n = Placement.PlaceCeiling(_uiapp.ActiveUIDocument.Document, fam.Symbol, rows.Select(r => new CeilingPlacement
                {
                    LevelId = r.Space.LevelId,
                    CeilingHeightM = r.CeilingH,
                    PointsM = r.Points,
                    Comment = new PaTag { Model = spk.Model, TapW = r.Tap, Info = $"{r.Number} SPL {r.Spl:0}dB" }.Encode(),
                }));
                Status($"{n} ceiling speaker ditempatkan.");
                RecapRefresh();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Gagal menempatkan speaker"); }
        }

        void CeilExport_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder("Level;No;Nama;Luas m2;Plafon m;Noise dBA;h m;r m;Coverage m2;Spacing m;Jumlah;Tap W;SPL dB;Total W;Status\n");
            foreach (var r in VisibleRows.Where(r => r.Include))
                sb.AppendLine(string.Join(";", r.Level, r.Number, r.Name, F(r.Area), F(r.CeilingH), F(r.Noise), F(r.H),
                    F(r.R), F(r.CoverageM2), F(r.S), r.Count, F(r.Tap), r.Spl.ToString("0.0", CultureInfo.InvariantCulture), F(r.TotalW), r.Status));
            SaveCsv(sb.ToString(), "PA_Ceiling.csv");
        }

        void SaveCsv(string content, string name)
        {
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = name };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName, "sep=;\n" + content, new UTF8Encoding(true));
            Status("Tersimpan: " + dlg.FileName);
        }

        // ---------------- HORN ----------------
        void HornSpeaker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (HornSpeaker.SelectedItem is not SpeakerSpec h) return;
            HornTap.ItemsSource = h.TapsW;
            HornTap.SelectedItem = h.TapsW.Contains(15) ? 15.0 : h.TapsW.Last();
            UpdateSpecText();
            UpdateHorn();
        }

        void HornTapChanged(object sender, SelectionChangedEventArgs e) => UpdateHorn();
        void HornInputChanged(object sender, TextChangedEventArgs e) => UpdateHorn();

        List<double> DbLevels() => (HornDb.Text ?? "")
            .Split(new[] { ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(s => s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => Num(s, double.NaN)).Where(v => !double.IsNaN(v))
            .OrderByDescending(v => v).ToList();

        void UpdateHorn()
        {
            if (!_ready || HornSpeaker.SelectedItem is not SpeakerSpec h || HornTap.SelectedItem is not double tap) return;
            double dh = Num(HornMount.Text, 4) - Num(HornEar.Text, 1.5);
            double half = h.CoverageHDeg / 2 * Math.PI / 180;
            var rows = DbLevels().Select(db =>
            {
                var d = Acoustics.DistanceForSpl(h.SensitivityDb, tap, db);
                var reach = Acoustics.HorizontalReach(d, dh);
                return new HornRow { Db = db, Slant = d, Reach = reach, Width = 2 * reach * Math.Tan(half) };
            }).ToList();
            HornGrid.ItemsSource = rows;
            var first = rows.FirstOrDefault();
            HornHeadline.Text = first == null ? "" :
                first.Reach > 0
                    ? $"{h.Model} @ {F(tap)} W: {first.Db:0} dB tercapai sampai {first.Reach:0.0} m dari dinding (lebar ±{first.Width:0.0} m)"
                    : $"{first.Db:0} dB tidak tercapai di tinggi telinga (horn terlalu tinggi / tap terlalu kecil)";
        }

        void HornPlace_Click(object sender, RoutedEventArgs e)
        {
            if (HornFamily.SelectedItem is not FamilyTypeItem fam)
            {
                MessageBox.Show("Belum ada family Communication Devices (face-based) di project. Load family horn dulu.", "Public Address");
                return;
            }
            var hs = new HornSettings
            {
                Speaker = (SpeakerSpec)HornSpeaker.SelectedItem,
                TapW = (double)HornTap.SelectedItem,
                MountHeightM = Num(HornMount.Text, 4),
                EarHeightM = Num(HornEar.Text, 1.5),
                DbLevels = DbLevels(),
                CountPerWall = (int)Num(HornCount.Text, 0),
                DrawRadius = HornDraw.IsChecked == true,
            };

            Hide();
            HornResult res;
            try { res = Placement.PlaceHorns(_uiapp.ActiveUIDocument, fam.Symbol, hs); }
            catch (Exception ex) { res = new HornResult { Message = "Gagal: " + ex.Message }; }
            finally { Show(); Activate(); }

            Status(res.Message);
            RecapRefresh();
        }

        HornRecommendation _rec;

        void HornPickSpace_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            Autodesk.Revit.DB.ElementId id;
            try { id = Placement.PickSpace(_uiapp.ActiveUIDocument); }
            finally { Show(); Activate(); }
            if (id == null) return;
            var sp = (HornSpace.ItemsSource as IEnumerable<SpaceInfo>)?.FirstOrDefault(s => s.Id.Equals(id));
            if (sp == null) { Status("Space tidak punya boundary yang valid."); return; }
            HornSpace.SelectedItem = sp;
            HornRecommend_Click(sender, e);
        }

        void HornRecommend_Click(object sender, RoutedEventArgs e)
        {
            if (HornSpace.SelectedItem is not SpaceInfo sp) { Status("Pilih Space dulu (dari daftar atau Klik di Model)."); return; }
            if (HornSpeaker.SelectedItem is not SpeakerSpec h) return;
            double target = DbLevels().FirstOrDefault(99);
            _rec = HornAdvisor.Recommend(h, sp.Boundary, Num(HornMount.Text, 4), Num(HornEar.Text, 1.5), target);
            HornRecText.Text = _rec.Text;
            HornRecText.Foreground = _rec.Ok ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.Firebrick;
            HornApply.IsEnabled = true;
        }

        void HornApply_Click(object sender, RoutedEventArgs e)
        {
            if (_rec == null) return;
            HornTap.SelectedItem = _rec.TapW;
            HornCount.Text = _rec.PerWall.ToString();
            Status($"Tap {F(_rec.TapW)} W dan {_rec.PerWall} horn/dinding dipakai. Klik tombol biru, lalu pilih {_rec.Rows} face dinding panjang.");
        }

        // ---------------- REKAP ----------------
        List<RecapRow> _recapDetail = new(), _recapZone = new();

        void RecapRefresh()
        {
            double headroom = Num(RecapHeadroom.Text, 1.25);
            var placed = RevitData.GetPlacedSpeakers(_uiapp.ActiveUIDocument.Document);
            _recapDetail = placed
                .GroupBy(p => (p.level, p.tag.Model, p.tag.TapW))
                .Select(g => new RecapRow { Level = g.Key.level, Model = g.Key.Model, Tap = g.Key.TapW, Qty = g.Count(), TotalW = g.Count() * g.Key.TapW })
                .OrderBy(r => r.Level).ThenBy(r => r.Model).ToList();
            _recapZone = _recapDetail.GroupBy(r => r.Level)
                .Select(g => new RecapRow { Level = g.Key, Qty = g.Sum(x => x.Qty), TotalW = g.Sum(x => x.TotalW) })
                .ToList();
            if (_recapZone.Count > 0)
                _recapZone.Add(new RecapRow { Level = "TOTAL", Qty = _recapZone.Sum(x => x.Qty), TotalW = _recapZone.Sum(x => x.TotalW) });
            foreach (var z in _recapZone) z.Amp = Acoustics.AmplifierSize(z.TotalW, headroom);
            RecapDetail.ItemsSource = _recapDetail;
            RecapZone.ItemsSource = _recapZone;
        }

        void RecapRefresh_Click(object sender, RoutedEventArgs e) { RecapRefresh(); Status("Rekap diperbarui dari model."); }

        void RecapExport_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder("Level;Model;Tap W;Qty;Total W\n");
            foreach (var r in _recapDetail) sb.AppendLine($"{r.Level};{r.Model};{F(r.Tap)};{r.Qty};{F(r.TotalW)}");
            sb.AppendLine().AppendLine("Level;Qty;Total W;Amplifier W");
            foreach (var z in _recapZone) sb.AppendLine($"{z.Level};{z.Qty};{F(z.TotalW)};{F(z.Amp)}");
            SaveCsv(sb.ToString(), "PA_Rekap.csv");
        }
    }
}
