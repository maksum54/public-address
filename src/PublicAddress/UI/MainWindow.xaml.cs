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

        bool _include = false;
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

        public double TableHFt { get; private set; }
        public int AreaCount { get; private set; }
        /// <summary>Jumlah manual dari user (0 = otomatis dari grid).</summary>
        public int ManualQty { get; set; }
        public string GridSize { get; private set; } = "";
        public double DxM { get; private set; }
        public double DyM { get; private set; }

        /// <summary>
        /// 1. h = plafon - telinga, dibulatkan ke baris tabel datasheet terdekat (ft).
        /// 2. spacing dari tabel: No Overlap atau Min. Overlap (square).
        /// 3. kolom = ceil(lebar / spacing), baris = ceil(panjang / spacing).
        /// Metode luas (luas / coverage) ditampilkan sebagai pembanding.
        /// </summary>
        public void Calc(SpeakerSpec spk, SpacingMethod m, double ear, double margin)
        {
            H = CeilingH - ear;
            if (H <= 0.1)
            {
                R = S = Spl = Tap = CoverageM2 = TableHFt = DxM = DyM = 0; Count = AreaCount = 0; Points.Clear(); GridSize = "";
                Status = "Plafon ≤ telinga"; Ok = false;
                On(null);
                return;
            }

            var row = Acoustics.TableRow(spk, H);
            if (row != null)
            {
                TableHFt = row[0];
                CoverageM2 = row[1] * Acoustics.SqFt;
                R = row[2] * Acoustics.Ft;
                S = (m == SpacingMethod.NoOverlap ? row[5] : row[3]) * Acoustics.Ft;
            }
            else
            {
                TableHFt = 0;
                R = Acoustics.CoverageRadius(H, spk.SpacingAngleDeg);
                CoverageM2 = Math.PI * R * R;
                S = Acoustics.Spacing(R, m);
            }

            Points = GridLayout.LayoutCells(Space.Boundary, S, out int nx, out int ny);
            var b = Space.Boundary;
            DxM = nx > 0 ? (b.Max(p => p.X) - b.Min(p => p.X)) / nx : 0;
            DyM = ny > 0 ? (b.Max(p => p.Y) - b.Min(p => p.Y)) / ny : 0;
            GridSize = $"{nx} × {ny}";
            if (ManualQty > 0)
            {
                Points = GridLayout.LayoutCount(Space.Boundary, ManualQty);
                GridSize = "manual";
                DxM = DyM = Math.Sqrt(Area / ManualQty);
            }
            Count = Points.Count;
            AreaCount = Math.Max(1, (int)Math.Ceiling(Area / CoverageM2 - 1e-9));

            double target = Noise + margin;
            var tap = Acoustics.PickTap(spk, H, target);
            Tap = tap ?? spk.TapsW.Last();
            Spl = Acoustics.Spl(spk.SensitivityDb, Tap, H);
            Ok = tap != null;
            Status = Ok == true ? "OK" : $"SPL kurang ({target:0} dB)";
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
            CeilMethod.SelectedIndex = 0; // No Overlap
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
            foreach (var r in VisibleRows) r.Calc(spk, Method, ear, margin);
            var sel = VisibleRows.Where(r => r.Include).ToList();
            double w = sel.Sum(r => r.TotalW);
            int bad = sel.Count(r => r.Ok == false);
            CeilSummary.Text = $"{sel.Count} Space · {sel.Sum(r => r.Count)} speaker · {F(w)} W · amplifier ≥ {F(Acoustics.AmplifierSize(w))} W"
                               + (bad > 0 ? $" · {bad} Space SPL kurang" : "");
            if (CeilGrid.SelectedItem is CeilingRow cur) DrawPreview(cur);
        }

        void CeilPreview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CeilGrid.SelectedItem is CeilingRow cur) DrawPreview(cur);
        }

        void HornPreview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_rec != null && HornSpace.SelectedItem is SpaceInfo sp) DrawHornPreview(sp, _rec);
        }

        void CeilInputChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            UpdateSpecText();
            CeilRecalc();
        }

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
            if (total == 0) { Status("Centang Space dulu, atau pakai 'Klik Space di Model'."); return; }
            if (MessageBox.Show($"Tempatkan {total} speaker {spk.Model} ({fam.Display}) di {rows.Count} Space?",
                    "Public Address", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            PlaceRows(fam, spk, rows);
        }

        bool PlaceRows(FamilyTypeItem fam, SpeakerSpec spk, List<CeilingRow> rows)
        {
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
                return true;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Gagal menempatkan speaker"); return false; }
        }

        /// <summary>Seperti tool fire alarm: klik satu Space, lihat preview, konfirmasi, lanjut ke Space berikutnya.</summary>
        void CeilPick_Click(object sender, RoutedEventArgs e)
        {
            if (CeilFamily.SelectedItem is not FamilyTypeItem fam)
            {
                MessageBox.Show("Belum ada family Communication Devices (non-hosted) di project. Load family ceiling speaker dulu.", "Public Address");
                return;
            }
            var spk = (SpeakerSpec)CeilSpeaker.SelectedItem;
            if (CeilLevel.SelectedIndex != 0) CeilLevel.SelectedIndex = 0;
            int placed = 0;

            while (true)
            {
                Hide();
                Autodesk.Revit.DB.ElementId id;
                try { id = Placement.PickSpace(_uiapp.ActiveUIDocument); }
                finally { Show(); Activate(); }
                if (id == null) break;

                var row = _allRows.FirstOrDefault(r => r.Space.Id.Equals(id));
                if (row == null) { Status("Space ini tidak punya boundary yang valid."); continue; }

                foreach (var r in _allRows) r.Include = false;
                row.Include = true;
                CeilRecalc();
                CeilGrid.SelectedItem = row;
                CeilGrid.ScrollIntoView(row);
                DrawPreview(row);

                var ans = MessageBox.Show(this,
                    $"Space {row.Number} {row.Name}  ({row.Area:0.0} m²)\n\n" +
                    $"Grid {row.GridSize} = {row.Count} × {spk.Model} @ {F(row.Tap)} W\nJarak {row.DxM:0.00} × {row.DyM:0.00} m  ·  SPL {row.Spl:0.0} dB  ({row.Status})\n(metode luas: {row.AreaCount} unit)\n\n" +
                    "Ya = tempatkan lalu klik Space berikutnya\nTidak = lewati Space ini\nCancel = selesai",
                    "Preview Space", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (ans == MessageBoxResult.Cancel) break;
                if (ans == MessageBoxResult.Yes && row.Count > 0 && PlaceRows(fam, spk, new List<CeilingRow> { row }))
                    placed += row.Count;
            }
            Status($"Selesai. {placed} speaker ditempatkan pada sesi klik ini.");
        }

        void CeilGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CeilGrid.SelectedItem is CeilingRow r) DrawPreview(r);
        }

        void DrawPreview(CeilingRow row)
        {
            var c = CeilPreview;
            c.Children.Clear();
            var poly = row.Space.Boundary;
            if (poly.Count < 3) return;
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double cw = Math.Max(c.ActualWidth, 300), ch = Math.Max(c.ActualHeight, 260), pad = 10;
            double k = Math.Min((cw - 2 * pad) / Math.Max(maxX - minX, 0.1), (ch - 2 * pad) / Math.Max(maxY - minY, 0.1));
            System.Windows.Point P(double x, double y) => new(pad + (x - minX) * k, ch - pad - (y - minY) * k);

            var room = new System.Windows.Shapes.Polygon
            {
                Stroke = System.Windows.Media.Brushes.SteelBlue, StrokeThickness = 2,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD)),
            };
            foreach (var p in poly) room.Points.Add(P(p.X, p.Y));
            c.Children.Add(room);

            foreach (var p in row.Points)
            {
                var q = P(p.X, p.Y);
                double rr = row.R * k;
                var circle = new System.Windows.Shapes.Ellipse
                {
                    Width = 2 * rr, Height = 2 * rr,
                    Stroke = System.Windows.Media.Brushes.Gray, StrokeDashArray = new System.Windows.Media.DoubleCollection { 3, 3 },
                    Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xEB, 0x3B)),
                };
                Canvas.SetLeft(circle, q.X - rr); Canvas.SetTop(circle, q.Y - rr);
                c.Children.Add(circle);
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = System.Windows.Media.Brushes.Black };
                Canvas.SetLeft(dot, q.X - 3.5); Canvas.SetTop(dot, q.Y - 3.5);
                c.Children.Add(dot);
            }
            CeilPreviewInfo.Text =
                $"{row.Number} {row.Name} · {maxX - minX:0.00} × {maxY - minY:0.00} m = {row.Area:0.0} m²\n" +
                $"h-l = {row.H:0.00} m" + (row.TableHFt > 0 ? $" ≈ {row.TableHFt:0} ft (tabel)" : "") + $" · coverage {row.CoverageM2:0.0} m²\n" +
                $"Grid {row.GridSize} = {row.Count} speaker · spacing tabel {row.S:0.00} m\n" +
                $"Jarak aktual {row.DxM:0.00} × {row.DyM:0.00} m · ke dinding {row.DxM / 2:0.00} / {row.DyM / 2:0.00} m\n" +
                $"Pembanding metode luas: {row.Area:0.0} ÷ {row.CoverageM2:0.0} = {row.AreaCount} unit\n" +
                $"Tap {F(row.Tap)} W · SPL {row.Spl:0.0} dB";
        }

        void CeilExport_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder("Level;No;Nama;Luas m2;Plafon m;Noise dBA;h m;h tabel ft;r m;Coverage m2;Spacing m;Grid;Jumlah;Metode luas;Tap W;SPL dB;Total W;Status\n");
            foreach (var r in VisibleRows.Where(r => r.Include))
                sb.AppendLine(string.Join(";", r.Level, r.Number, r.Name, F(r.Area), F(r.CeilingH), F(r.Noise), F(r.H), F(r.TableHFt),
                    F(r.R), F(r.CoverageM2), F(r.S), r.GridSize, r.Count, r.AreaCount, F(r.Tap), r.Spl.ToString("0.0", CultureInfo.InvariantCulture), F(r.TotalW), r.Status));
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
            if (_rec != null && HornSpace.SelectedItem is SpaceInfo) HornRecommend_Click(null, null);
        }

        void HornSpace_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_ready && HornSpace.SelectedItem is SpaceInfo) HornRecommend_Click(null, null);
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
            _lastHornPlaced = res.Placed;
        }

        int _lastHornPlaced;

        /// <summary>Seperti ceiling: klik Space → preview rekomendasi → pilih face dinding → Space berikutnya.</summary>
        void HornPickLoop_Click(object sender, RoutedEventArgs e)
        {
            if (HornFamily.SelectedItem is not FamilyTypeItem)
            {
                MessageBox.Show("Belum ada family Communication Devices (face-based) di project. Load family horn dulu.", "Public Address");
                return;
            }
            int total = 0;
            while (true)
            {
                Hide();
                Autodesk.Revit.DB.ElementId id;
                try { id = Placement.PickSpace(_uiapp.ActiveUIDocument); }
                finally { Show(); Activate(); }
                if (id == null) break;

                var sp = (HornSpace.ItemsSource as IEnumerable<SpaceInfo>)?.FirstOrDefault(x => x.Id.Equals(id));
                if (sp == null) { Status("Space ini tidak punya boundary yang valid."); continue; }
                HornSpace.SelectedItem = sp;
                HornRecommend_Click(sender, e);
                if (_rec == null) break;
                DrawHornPreview(sp, _rec);

                var ans = MessageBox.Show(this,
                    $"Space {sp.Number} {sp.Name}\n\n{_rec.Text}\n\n" +
                    $"Ya = pilih {_rec.Rows} face dinding panjang lalu tempatkan ({_rec.PerWall} horn/dinding)\nTidak = lewati Space ini\nCancel = selesai",
                    "Preview Horn", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (ans == MessageBoxResult.Cancel) break;
                if (ans != MessageBoxResult.Yes) continue;

                HornApply_Click(sender, e);
                _lastHornPlaced = 0;
                HornPlace_Click(sender, e);
                total += _lastHornPlaced;
            }
            Status($"Selesai. {total} horn ditempatkan pada sesi klik ini.");
        }

        void DrawHornPreview(SpaceInfo sp, HornRecommendation rec)
        {
            var c = HornPreview;
            c.Children.Clear();
            var poly = sp.Boundary;
            if (poly.Count < 3 || HornSpeaker.SelectedItem is not SpeakerSpec h) return;
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double cw = Math.Max(c.ActualWidth, 300), ch = Math.Max(c.ActualHeight, 180), pad = 10;
            double k = Math.Min((cw - 2 * pad) / Math.Max(maxX - minX, 0.1), (ch - 2 * pad) / Math.Max(maxY - minY, 0.1));
            System.Windows.Point P(double x, double y) => new(pad + (x - minX) * k, ch - pad - (y - minY) * k);

            var room = new System.Windows.Shapes.Polygon
            {
                Stroke = System.Windows.Media.Brushes.SteelBlue, StrokeThickness = 2,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD)),
            };
            foreach (var p in poly) room.Points.Add(P(p.X, p.Y));
            c.Children.Add(room);

            // horn di dinding sisi panjang (bounding box), menembak ke dalam ruang
            bool alongX = (maxX - minX) >= (maxY - minY);
            double len = alongX ? maxX - minX : maxY - minY;
            double half = h.CoverageHDeg / 2 * Math.PI / 180;
            var walls = new List<(double sign, double pos)> { (1, alongX ? minY : minX) };
            if (rec.Rows == 2) walls.Add((-1, alongX ? maxY : maxX));

            foreach (var (sign, pos) in walls)
            int perWall = (int)Num(HornCount.Text, 0);
            if (perWall <= 0) perWall = rec.PerWall;
            for (int i = 0; i < perWall; i++)
            {
                double t = (alongX ? minX : minY) + len * (i + 0.5) / perWall;
                double hx = alongX ? t : pos, hy = alongX ? pos : t;
                double ax = alongX ? 0 : sign, ay = alongX ? sign : 0;   // arah tembak
                var fan = new System.Windows.Shapes.Polygon
                {
                    Stroke = System.Windows.Media.Brushes.DarkOrange, StrokeThickness = 1,
                    Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0x98, 0x00)),
                };
                fan.Points.Add(P(hx, hy));
                for (int a = 0; a <= 20; a++)
                {
                    double ang = -half + 2 * half * a / 20;
                    double dx = ax * Math.Cos(ang) - ay * Math.Sin(ang), dy = ax * Math.Sin(ang) + ay * Math.Cos(ang);
                    fan.Points.Add(P(hx + dx * rec.ThrowM, hy + dy * rec.ThrowM));
                }
                c.Children.Add(fan);
                var q = P(hx, hy);
                var dot = new System.Windows.Shapes.Rectangle { Width = 9, Height = 9, Fill = System.Windows.Media.Brushes.Black };
                Canvas.SetLeft(dot, q.X - 4.5); Canvas.SetTop(dot, q.Y - 4.5);
                c.Children.Add(dot);
            }
            HornPreviewInfo.Text = perWall == rec.PerWall
                ? rec.Text
                : $"Manual: {perWall} horn/dinding × {walls.Count} dinding = {perWall * walls.Count} horn (rekomendasi {rec.Total}).";
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
            DrawHornPreview(sp, _rec);
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
