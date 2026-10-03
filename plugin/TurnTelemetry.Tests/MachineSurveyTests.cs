using System;
using System.IO;
using System.Linq;
using TurnTelemetry.Core.Turns;
using Xunit;
using Xunit.Abstractions;

namespace TurnTelemetry.Tests
{
    /// <summary>
    /// Resolves turns for every SimHub map on this machine through the real catalogue (skipped where SimHub is absent).
    /// Run with: dotnet test --filter MachineSurvey --logger "console;verbosity=detailed"
    /// </summary>
    public class MachineSurveyTests
    {
        private const string SimHub = @"C:\Program Files (x86)\SimHub";
        private readonly ITestOutputHelper _out;

        public MachineSurveyTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Every_local_map_resolves_without_errors()
        {
            var data = Path.Combine(SimHub, "PluginsData");
            if (!Directory.Exists(data)) return;
            var catalog = new TurnCatalog(Path.Combine(Path.GetTempPath(), "tt-survey-" + Guid.NewGuid().ToString("N")),
                new EmbeddedBundledData(typeof(TurnFile).Assembly));

            foreach (var gameDir in Directory.GetDirectories(data))
            {
                var game = Path.GetFileName(gameDir);
                var tracks = new[] { "MapRecords", "MapRecordsCloud" }
                    .Select(d => Path.Combine(gameDir, d)).Where(Directory.Exists)
                    .SelectMany(d => Directory.GetFiles(d, "*.shtl"))
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Select(n => TrimLength(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var track in tracks)
                {
                    var result = catalog.Resolve(game, track, simHubRoot: SimHub);
                    Assert.DoesNotContain("unreadable", result.Detail ?? "");
                    _out.WriteLine($"{game,-26} {track,-42} {CatalogResult.SourceId(result.Source),-13} {result.Turns.Count,3}  {result.Detail}");
                }
            }
        }

        // Cloud map names end with "-{length}"; strip it to get the track id.
        private static string TrimLength(string name)
        {
            var dash = name.LastIndexOf('-');
            return dash > 0 && double.TryParse(name.Substring(dash + 1), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _) ? name.Substring(0, dash) : name;
        }
    }
}
