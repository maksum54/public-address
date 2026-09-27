# Public Address Calculation – Add-in Revit 2025

Tab ribbon **Public Address → PA Calculation**. Satu jendela dengan 3 tab:

| Tab | Fungsi |
|---|---|
| Ceiling Speaker | Hitung jumlah, spacing, tap & SPL per MEP Space, lalu tempatkan family non-hosted di plafon |
| Horn Speaker | Pilih face dinding, horn face-based ditempel & dibagi rata, radius dB digambar di Floor Plan |
| Rekap & Amplifier | Total watt per level + ukuran amplifier 100 V, export CSV |

## Rumus
- Ceiling (default): `h = plafon − telinga`, `r = h·tan(sudut/2)`, coverage = π·r², jumlah = ceil(luas lantai ÷ coverage).
  Metode grid (opsional): spacing `r` / `r√2` / `r√3` (hex) / `2r`,
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

## Rekomendasi horn dari Space
Di tab Horn, pilih Space (daftar atau **Klik di Model**) lalu **Hitung Rekomendasi**:
- dimensi ruang L × W dari boundary Space (persegi panjang terkecil yang membungkus);
- 1 baris di dinding panjang bila SPL target (dB pertama, mis. 99) tercapai sampai dinding seberang,
  selain itu 2 baris berhadapan (throw = W/2);
- jumlah per dinding = ceil(L / (2·throw·tan(H/2))), tap = tap terkecil yang memenuhi target di jarak miring.
Tombol **Pakai** mengisi tap & jumlah per dinding, lalu tempatkan dengan memilih face dinding.

## Tinggi plafon
Tinggi plafon diambil dari input user (**Tinggi plafon (m)**, default 3.0), bukan dari tinggi Space.
Tombol **Terapkan** mengisi ke semua baris tercentang; tetap bisa diedit per baris.

## Build & install
DLL dibangun otomatis oleh GitHub Actions (`.github/workflows/build.yml`) setiap push.
Unduh artifact **PublicAddress-Revit2025** dari tab *Actions*, lalu:
1. Salin folder `PublicAddress` ke `%AppData%\Autodesk\Revit\Addins\2025\`.
2. Salin `PublicAddress.addin` ke `%AppData%\Autodesk\Revit\Addins\2025\`.
