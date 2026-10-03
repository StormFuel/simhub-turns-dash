using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace TurnTelemetryHost
{
    /// <summary>
    /// Writes a problem report (a zip) that users can attach to a GitHub issue or a Discord post: versions, the live
    /// diagnostics, the recent event log, settings, this track's turn and sector files, and Turn Telemetry's lines from
    /// SimHub's log. User-profile paths are masked; no other drivers' names are included.
    /// </summary>
    internal static class ReportWriter
    {
        private const int MaxSimHubLogLines = 400;

        public static string ReportsFolder(TurnTelemetry plugin) => Path.Combine(plugin.UserRoot, "reports");

        public static string Write(TurnTelemetry plugin)
        {
            var e = plugin.Engine;
            var folder = ReportsFolder(plugin);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"TurnTelemetry-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            var report = new StringBuilder();
            report.AppendLine("Turn Telemetry problem report");
            report.AppendLine($"Created       {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Plugin        {typeof(TurnTelemetry).Assembly.GetName().Version.ToString(3)}");
            report.AppendLine($"SimHub        {SimHubVersion()}");
            report.AppendLine($"Windows       {Environment.OSVersion.VersionString}");
            report.AppendLine();
            report.AppendLine("What happened (please describe it in your issue or post, with the time it happened).");
            report.AppendLine();
            if (e.GameName == null)
            {
                report.AppendLine("NOTE: no game session was running. Reports are most useful saved while driving, or right");
                report.AppendLine("after the problem (before closing the game or SimHub).");
                report.AppendLine();
            }
            report.AppendLine("=== Live diagnostics ===");
            report.AppendLine(SettingsControl.Diagnostics(e));
            report.AppendLine("=== Recent events (oldest first) ===");
            foreach (var line in e.Log.Snapshot()) report.AppendLine(line);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "report.txt", report.ToString());
                Add(zip, "settings.json", JsonConvert.SerializeObject(plugin.Settings, Formatting.Indented));
                AddFile(zip, "turns-user-file.json", e.UserTurnFilePath);
                AddFile(zip, "sectors.txt", e.SectorFilePath);
                Add(zip, "simhub-log.txt", SimHubLogLines());
            }
            e.Log.Add("problem report saved: " + Path.GetFileName(path));
            return path;
        }

        /// <summary>Opens Explorer with the report selected.</summary>
        public static void Show(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

        private static void Add(ZipArchive zip, string name, string text)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)))
                writer.Write(Mask(text ?? ""));
        }

        private static void AddFile(ZipArchive zip, string name, string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) Add(zip, name, File.ReadAllText(path));
        }

        /// <summary>
        /// SimHub's files are all stamped 1.0.0.0, so the real version (e.g. 9.12.9) comes from its entry in Windows'
        /// installed programs; the SimHub.Plugins build stamp is the fallback.
        /// </summary>
        private static string SimHubVersion()
        {
            try
            {
                foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                foreach (var root in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                                             @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
                using (var uninstall = hive.OpenSubKey(root))
                {
                    if (uninstall == null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    using (var app = uninstall.OpenSubKey(name))
                    {
                        var display = app?.GetValue("DisplayName") as string;
                        if (display != null && display.StartsWith("SimHub version", StringComparison.OrdinalIgnoreCase))
                            return app.GetValue("DisplayVersion") as string ?? display;
                    }
                }
                return "build " + typeof(SimHub.Plugins.PluginManager).Assembly.GetName().Version;
            }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>
        /// Turn Telemetry's entries from SimHub's log (with any stack trace lines that follow them), most recent last.
        /// </summary>
        private static string SimHubLogLines()
        {
            var log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "SimHub.txt");
            if (!File.Exists(log)) return "SimHub.txt not found";
            var lines = new List<string>();
            try
            {
                using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    var inEntry = false;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Each log entry starts with "[yyyy-..."; following lines without it belong to the same entry.
                        if (line.StartsWith("[")) inEntry = line.IndexOf("Turn Telemetry", StringComparison.OrdinalIgnoreCase) >= 0
                                                            || line.IndexOf("TurnTelemetry", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (inEntry) lines.Add(line);
                    }
                }
            }
            catch (IOException ex) { return "SimHub.txt couldn't be read: " + ex.Message; }
            return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Count - MaxSimHubLogLines)));
        }

        /// <summary>Hides the Windows user name in paths.</summary>
        private static string Mask(string text)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "%USERPROFILE%");
            var user = Environment.UserName;
            if (!string.IsNullOrEmpty(user)) text = text.Replace(@"\Users\" + user + @"\", @"\Users\%USERNAME%\");
            return text;
        }
    }
}
