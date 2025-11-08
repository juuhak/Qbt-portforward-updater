using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QbPortUpdater
{
    /// <summary>
    /// Detector that inspects ProtonVPN log files for "Port pair X->Y" entries and
    /// returns the latest forwarded port.
    /// </summary>
    public class ProtonVpnPortDetector : IPortDetector
    {
        private static readonly Regex PORT_REGEX = new(@"Port pair (\d+)->\d+", RegexOptions.Compiled);
        private static readonly Regex STATUS_STOPPED_REGEX = new("Received PortForwarding Status 'Stopped'", RegexOptions.Compiled);

        public string Name => "ProtonVPN";
        public DetectorType DetectorType => DetectorType.ProtonVPN;

        public async System.Threading.Tasks.Task<string?> GetLastPortAsync(string logDirectory, System.Threading.CancellationToken cancellationToken)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory)) return null;

                    var candidates = Directory.GetFiles(logDirectory, "*.txt", SearchOption.TopDirectoryOnly)
                        .Where(File.Exists)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    foreach (var fp in candidates)
                    {
                        var p = FindLastPortInFile(fp);
                        if (p != null) return p;
                    }
                }
                catch { }
                return null;
            }, cancellationToken);
        }

        private static string? FindLastPortInFile(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var readSize = (int)Math.Min(51200, fs.Length);
                fs.Seek(-readSize, SeekOrigin.End);
                using var sr = new StreamReader(fs);
                sr.ReadLine();
                var content = sr.ReadToEnd();
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (STATUS_STOPPED_REGEX.IsMatch(line))
                    {
                        return null;
                    }
                    var m = PORT_REGEX.Match(line);
                    if (m.Success)
                    {
                        return m.Groups[1].Value;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
