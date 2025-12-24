using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
        private static readonly string LOG_FILE_NAME = "client-log.txt";

        public string Name => "ProtonVPN";
        public DetectorType DetectorType => DetectorType.ProtonVPN;

        public async Task<string?> GetLastPortAsync(string logDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
            {
                return null;
            }
            
            var logFilePath = Path.GetFullPath(logDirectory + LOG_FILE_NAME);

            var p = await FindLastPortInFileAsync(logFilePath, cancellationToken);
            if (p != null)
            {
                return p;
            }

            return null;
        }

        private static async Task<string?> FindLastPortInFileAsync(string filePath, CancellationToken cancellationToken)
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
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
            return null;
        }
    }
}
