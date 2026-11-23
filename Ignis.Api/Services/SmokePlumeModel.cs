namespace Ignis.Api.Services;

/// <summary>
/// Satu sel asap pada grid kasar. Posisi relatif terhadap titik asal simulasi:
/// lintang = lintang asal + <see cref="Row"/> × <see cref="SmokePlumeModel.CellStepDegrees"/>, bujur = bujur asal + <see cref="Col"/> × langkah yang sama.
/// <see cref="Level"/>: 1 = ringan, 2 = sedang, 3 = pekat.
/// </summary>
public readonly record struct SmokeCell(int Row, int Col, int Level);

/// <summary>
/// Model sebaran asap kebakaran: adveksi-difusi deterministik pada grid kasar (sel 0,025° ≈ 2,8 km, lima kali sel api).
///
/// Tiap jam, setiap sel api yang sedang terbakar memancarkan asap ke sel kasar yang memuatnya. Asap lalu
///  (1) terbawa angin ke arah rambatan (semi-Lagrangian, dibagi menjadi langkah kecil agar jejaknya menerus, bukan gumpalan terpisah),
///  (2) menyebar ke sekitarnya (difusi), dan
///  (3) meluruh (umur dasar 14 jam; hujan mempercepat luruhnya, "washout").
/// Angin tenang menghasilkan sebaran melingkar; angin kencang menghasilkan jejak memanjang searah angin.
///
/// Satuan kepekatan: "massa per km²" dengan massa 1 = satu sel api yang terbakar selama satu jam. Angkanya relatif (indeks), bukan µg/m³;
/// ambang level ditetapkan dari pengukuran pada skenario khas (lihat tes). Hasilnya ilustrasi, bukan prakiraan kualitas udara.
/// </summary>
public static class SmokePlumeModel
{
    public const double CellStepDegrees = 0.025;   // ±2,78 km; tepat lima sel api (0,005°) agar grid selaras
    public const int FireCellsPerSmokeCell = 5;
    public const int HalfSizeCells = 22;           // domain ±22 sel ≈ ±61 km; asap yang melewatinya dianggap keluar
    public const int MaxCellsPerFrame = 700;       // pembatas ukuran respons; yang terpekat dipertahankan

    public const double DiffusionKm2PerHour = 8.0;
    public const double BaseLifetimeHours = 14.0;
    public const double EmissionPerFireCellHour = 1.0;

    // Ambang indeks kepekatan (massa per km²) untuk level 1, 2, 3. Dikalibrasi pada skenario khas: lihat SmokeModelTests.
    public const double LightThreshold = 0.01;
    public const double ModerateThreshold = 0.12;
    public const double DenseThreshold = 0.60;

    private const double KmPerDegree = 111.32;
    private const double MaxCourant = 0.6;     // jarak adveksi maksimum per langkah, dalam sel
    private const double MaxDiffusionNumber = 0.2;
    private const int MaxSubsteps = 80;

    /// <summary>Luas satu sel asap (km²) pada lintang tertentu.</summary>
    public static double CellAreaKm2(double latitude)
    {
        var north = CellStepDegrees * KmPerDegree;
        return north * north * Math.Cos(latitude * Math.PI / 180.0);
    }

    public static int LevelOf(double density) =>
        density >= DenseThreshold ? 3 : density >= ModerateThreshold ? 2 : density >= LightThreshold ? 1 : 0;

    /// <summary>
    /// Hitung sebaran asap untuk tiap jam. <paramref name="burningByHour"/>[h] = sel api (baris, kolom pada grid api) yang terbakar pada jam h;
    /// hasil[h] = sel asap pada jam h. Jam 0 selalu kosong (api baru menyala, belum ada asap yang terbawa).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<SmokeCell>> Compute(
        double originLatitude,
        IReadOnlyList<IReadOnlyList<(int Row, int Col)>> burningByHour,
        double windSpeedMs,
        double spreadDirectionDegrees,
        double precipitationMm)
    {
        int size = 2 * HalfSizeCells + 1;
        var mass = new double[size, size];
        var next = new double[size, size];

        double cosLat = Math.Max(0.1, Math.Cos(originLatitude * Math.PI / 180.0));
        double cellNorthKm = CellStepDegrees * KmPerDegree;
        double cellEastKm = cellNorthKm * cosLat;
        double cellAreaKm2 = cellNorthKm * cellEastKm;

        double speedKmH = Math.Max(0, windSpeedMs) * 3.6;
        double rad = spreadDirectionDegrees * Math.PI / 180.0;
        double velRow = Math.Cos(rad) * speedKmH / cellNorthKm;   // sel per jam ke utara
        double velCol = Math.Sin(rad) * speedKmH / cellEastKm;    // sel per jam ke timur

        double diffRow = DiffusionKm2PerHour / (cellNorthKm * cellNorthKm);   // bilangan difusi per jam
        double diffCol = DiffusionKm2PerHour / (cellEastKm * cellEastKm);

        int substeps = (int)Math.Clamp(Math.Ceiling(Math.Max(
            Math.Max(Math.Abs(velRow), Math.Abs(velCol)) / MaxCourant,
            Math.Max(diffRow, diffCol) / MaxDiffusionNumber)), 1, MaxSubsteps);
        double dt = 1.0 / substeps;

        double lifetime = BaseLifetimeHours / (1.0 + 0.8 * Math.Max(0, precipitationMm));   // hujan mencuci asap
        double decay = Math.Exp(-dt / lifetime);

        var frames = new List<IReadOnlyList<SmokeCell>>(burningByHour.Count) { Array.Empty<SmokeCell>() };

        for (int hour = 1; hour < burningByHour.Count; hour++)
        {
            // Sumber selama jam ini: sel api yang sudah terbakar pada awal jam, dikelompokkan ke sel asap kasar.
            var sources = new Dictionary<(int Row, int Col), int>();
            foreach (var (r, c) in burningByHour[hour - 1])
            {
                var key = (Coarse(r), Coarse(c));
                sources[key] = sources.GetValueOrDefault(key) + 1;
            }

            for (int step = 0; step < substeps; step++)
            {
                foreach (var ((row, col), count) in sources)
                    if (InDomain(row, col)) mass[row + HalfSizeCells, col + HalfSizeCells] += count * EmissionPerFireCellHour * dt;

                Advect(mass, next, velRow * dt, velCol * dt, size);
                Diffuse(next, mass, diffRow * dt, diffCol * dt, size, decay);   // hasil akhir kembali ke 'mass'
            }

            frames.Add(Snapshot(mass, size, cellAreaKm2));
        }

        return frames;
    }

    private static int Coarse(int fineIndex) => (int)Math.Round(fineIndex / (double)FireCellsPerSmokeCell, MidpointRounding.AwayFromZero);

    private static bool InDomain(int row, int col) => Math.Abs(row) <= HalfSizeCells && Math.Abs(col) <= HalfSizeCells;

    /// <summary>Semi-Lagrangian: nilai baru di sebuah sel = nilai lama di posisi asalnya (dihitung mundur sepanjang angin), interpolasi bilinear.</summary>
    private static void Advect(double[,] src, double[,] dst, double shiftRows, double shiftCols, int size)
    {
        if (Math.Abs(shiftRows) < 1e-12 && Math.Abs(shiftCols) < 1e-12)
        {
            Array.Copy(src, dst, src.Length);
            return;
        }

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double fr = r - shiftRows, fc = c - shiftCols;
                int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
                double tr = fr - r0, tc = fc - c0;

                dst[r, c] =
                    At(src, r0, c0, size) * (1 - tr) * (1 - tc) +
                    At(src, r0 + 1, c0, size) * tr * (1 - tc) +
                    At(src, r0, c0 + 1, size) * (1 - tr) * tc +
                    At(src, r0 + 1, c0 + 1, size) * tr * tc;
            }
        }
    }

    private static double At(double[,] a, int r, int c, int size) =>
        r < 0 || c < 0 || r >= size || c >= size ? 0.0 : a[r, c];   // di luar domain = udara bersih (asap yang keluar hilang)

    /// <summary>Difusi eksplisit lima titik lalu luruh; membaca dari <paramref name="src"/>, menulis ke <paramref name="dst"/>.</summary>
    private static void Diffuse(double[,] src, double[,] dst, double kRow, double kCol, int size, double decay)
    {
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double centre = src[r, c];
                double value = centre
                    + kRow * (At(src, r + 1, c, size) - 2 * centre + At(src, r - 1, c, size))
                    + kCol * (At(src, r, c + 1, size) - 2 * centre + At(src, r, c - 1, size));
                dst[r, c] = Math.Max(0, value) * decay;
            }
        }
    }

    private static IReadOnlyList<SmokeCell> Snapshot(double[,] mass, int size, double cellAreaKm2)
    {
        var cells = new List<(SmokeCell Cell, double Density)>();
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double density = mass[r, c] / cellAreaKm2;
                int level = LevelOf(density);
                if (level > 0) cells.Add((new SmokeCell(r - HalfSizeCells, c - HalfSizeCells, level), density));
            }
        }

        // Batas ukuran respons: pertahankan yang terpekat; urutan akhir tetap (baris, kolom) agar hasil deterministik dan mudah dibandingkan.
        if (cells.Count > MaxCellsPerFrame)
            cells = cells.OrderByDescending(x => x.Density).ThenBy(x => x.Cell.Row).ThenBy(x => x.Cell.Col).Take(MaxCellsPerFrame).ToList();

        return cells.Select(x => x.Cell).OrderBy(x => x.Row).ThenBy(x => x.Col).ToList();
    }
}
