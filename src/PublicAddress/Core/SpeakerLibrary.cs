using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PublicAddress.Core
{
    public enum SpeakerKind { Ceiling, Horn }

    public class SpeakerSpec
    {
        public string Model { get; set; }
        public SpeakerKind Kind { get; set; }
        public double CoverageHDeg { get; set; }
        public double CoverageVDeg { get; set; }
        /// <summary>Sudut yang dipakai untuk tabel coverage/spacing pabrikan.</summary>
        public double SpacingAngleDeg { get; set; }
        public double SensitivityDb { get; set; }
        public double MaxPowerW { get; set; }
        public List<double> TapsW { get; set; } = new();

        public override string ToString() => Model;
    }

    public class SpeakerLibrary
    {
        public List<SpeakerSpec> Speakers { get; set; } = new();

        public IEnumerable<SpeakerSpec> OfKind(SpeakerKind kind) => Speakers.Where(s => s.Kind == kind);

        public static string DefaultPath =>
            Path.Combine(Path.GetDirectoryName(typeof(SpeakerLibrary).Assembly.Location) ?? "", "Resources", "speakers.json");

        public static SpeakerLibrary Load(string path = null)
        {
            path ??= DefaultPath;
            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            opts.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var lib = JsonSerializer.Deserialize<SpeakerLibrary>(json, opts) ?? new SpeakerLibrary();
            foreach (var s in lib.Speakers) s.TapsW.Sort();
            return lib;
        }
    }
}
