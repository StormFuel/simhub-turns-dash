using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace TurnTelemetry.Core.Turns
{
    /// <summary>JSON schema for curated and user turn files (docs/architecture.md §6).</summary>
    public sealed class TurnFile
    {
        public const int CurrentSchema = 1;

        [JsonProperty("schema")] public int Schema = CurrentSchema;
        [JsonProperty("game")] public string Game;
        [JsonProperty("track")] public string Track;
        [JsonProperty("trackName")] public string TrackName;
        [JsonProperty("lengthMeters", NullValueHandling = NullValueHandling.Ignore)] public double? LengthMeters;
        [JsonProperty("source")] public string Source;
        [JsonProperty("turns")] public List<TurnEntry> Turns = new List<TurnEntry>();

        public sealed class TurnEntry
        {
            [JsonProperty("label")] public string Label;
            [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name;
            [JsonProperty("start")] public double Start;
            [JsonProperty("end")] public double End;
        }

        public static TurnFile Parse(string json) => JsonConvert.DeserializeObject<TurnFile>(json);

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public List<TurnDefinition> ToDefinitions() => Turns
            .Where(t => !string.IsNullOrWhiteSpace(t.Label) && InRange(t.Start) && InRange(t.End))
            .Select(t => new TurnDefinition { Label = t.Label.Trim(), Name = t.Name, Start = t.Start, End = t.End })
            .OrderBy(t => t.Start)
            .ToList();

        private static bool InRange(double v) => v >= 0 && v <= 1;
    }

    /// <summary>Reads Assetto Corsa's data/sections.ini (the Track Description app's section ranges).</summary>
    public static class AcSectionsReader
    {
        public sealed class Result
        {
            public int SectionCount;
            public List<TurnDefinition> Turns = new List<TurnDefinition>();
        }

        public sealed class Section
        {
            public string Text;
            public double Start;
            public double End;
        }

        /// <summary>Only explicitly numbered sections become turns (see TurnLabel.Parse).</summary>
        public static Result Parse(string ini)
        {
            var result = new Result();
            var sections = ParseAll(ini, out var count);
            foreach (var section in sections)
            {
                var label = TurnLabel.Parse(section.Text);
                if (label != null)
                    result.Turns.Add(new TurnDefinition { Label = label, Name = section.Text, Start = section.Start, End = section.End });
            }
            result.SectionCount = count;
            result.Turns = result.Turns.OrderBy(t => t.Start).ToList();
            return result;
        }

        public static List<Section> ParseAll(string ini) => ParseAll(ini, out _);

        /// <summary>Every section with a valid IN/OUT range, numbered or not.</summary>
        public static List<Section> ParseAll(string ini, out int sectionCount)
        {
            var sections = new List<Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            foreach (var rawLine in (ini ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';' || line.StartsWith("//")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections.Add(current);
                    continue;
                }
                var eq = line.IndexOf('=');
                if (current == null || eq <= 0) continue;
                current[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            var result = new List<Section>();
            sectionCount = 0;
            foreach (var section in sections)
            {
                if (!section.ContainsKey("IN") && !section.ContainsKey("OUT")) continue;
                sectionCount++;
                section.TryGetValue("TEXT", out var text);
                if (!TryDouble(section, "IN", out var start) || !TryDouble(section, "OUT", out var end)) continue;
                if (start < 0 || end < 0 || start > 1 || end > 1) continue;
                result.Add(new Section { Text = text ?? "", Start = start, End = end });
            }
            return result;
        }

        /// <summary>content/tracks/{track}/{layout}/data/sections.ini, falling back to the track root.</summary>
        public static string FindFile(string acRoot, string track, string layout)
        {
            if (string.IsNullOrEmpty(acRoot) || string.IsNullOrEmpty(track)) return null;
            var trackDir = Path.Combine(acRoot, "content", "tracks", track);
            if (!string.IsNullOrEmpty(layout))
            {
                var layoutFile = Path.Combine(trackDir, layout, "data", "sections.ini");
                if (File.Exists(layoutFile)) return layoutFile;
            }
            var rootFile = Path.Combine(trackDir, "data", "sections.ini");
            return File.Exists(rootFile) ? rootFile : null;
        }

        private static bool TryDouble(Dictionary<string, string> section, string key, out double value)
        {
            value = 0;
            return section.TryGetValue(key, out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>Turn files shipped inside the Core assembly (data/turns/{game}/{track}.json).</summary>
    public interface IBundledData
    {
        /// <returns>File content, or null when absent.</returns>
        string Read(string relativePath);

        /// <summary>Paths of bundled files starting with <paramref name="prefix"/> (e.g. "data/circuits/").</summary>
        IEnumerable<string> List(string prefix);
    }

    public sealed class EmbeddedBundledData : IBundledData
    {
        private readonly Assembly _assembly;
        private readonly Dictionary<string, string> _names;

        public EmbeddedBundledData(Assembly assembly = null)
        {
            _assembly = assembly ?? typeof(EmbeddedBundledData).Assembly;
            // LogicalName uses %(RecursiveDir), which carries backslashes on Windows.
            _names = _assembly.GetManifestResourceNames()
                .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> List(string prefix) =>
            _names.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        public string Read(string relativePath)
        {
            if (!_names.TryGetValue(relativePath.Replace('\\', '/'), out var name)) return null;
            using (var stream = _assembly.GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }

    public static class TrackKey
    {
        /// <summary>File-name-safe form of a SimHub game name or track id.</summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        public static string RelativePath(string game, string track) =>
            $"turns/{Sanitize(game)}/{Sanitize(track)}.json";
    }
}
