# Public Address Calculation – Add-in Revit 2025

Tab ribbon **Public Address → PA Calculation**. Satu jendela dengan 3 tab:

| Tab | Fungsi |
|---|---|
| Ceiling Speaker | Hitung jumlah, spacing, tap & SPL per MEP Space, lalu tempatkan family non-hosted di plafon |
| Horn Speaker | Pilih face dinding, horn face-based ditempel & dibagi rata, radius dB digambar di Floor Plan |
| Rekap & Amplifier | Total watt per level + ukuran amplifier 100 V, export CSV |

## Rumus
- Ceiling: `h = plafon − telinga`, `r = h·tan(sudut/2)`, spacing `r` / `r√2` / `r√3` (hex) / `2r`,
  `SPL = Sens + 10·log(Tap) − 20·log(h)`, tap = tap terkecil yang memenuhi `noise + margin`.
- Horn: `d = 10^((Sens + 10·log(Tap) − SPL)/20)`, jangkauan horizontal `√(d² − (tinggi pasang − telinga)²)`,
  lebar sebaran `2·jangkauan·tan(H/2)`. SC-615/T @15 W → 99 dB ≈ 17.3 m (17.1 m horizontal bila beda tinggi 2.5 m).
- Amplifier = total W × 1.25, dibulatkan ke ukuran standar.

Hasil sudah dicek terhadap tabel coverage PC-671R/RV dan F-101C/M.

## Database speaker
`Resources/speakers.json` – tambah speaker dengan menyalin satu blok.
`SpacingAngleDeg` = sudut yang dipakai tabel pabrikan (F-101C/M: 120° nominal, tabel memakai 90°).
SC-650 tidak dimasukkan karena tidak punya trafo 100 V.

## Family yang dibutuhkan (kategori Communication Devices)
- Ceiling speaker: family **non-hosted** (level based).
- Horn: family **face-based** (work plane based). Arah corong = arah keluar dari face.

Setiap speaker yang ditempatkan diberi Comments `PA|model|tapW|info`; tab Rekap membaca dari situ.

## Build & install
1. Build `src/PublicAddress/PublicAddress.csproj` (Visual Studio 2022 / .NET 8 SDK, Windows).
2. Salin isi `bin/Release/net8.0-windows/` ke `%AppData%\Autodesk\Revit\Addins\2025\PublicAddress\`
   (termasuk folder `Resources`).
3. Salin `PublicAddress.addin` ke `%AppData%\Autodesk\Revit\Addins\2025\`.
