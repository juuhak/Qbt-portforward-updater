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
        private static readonly string LOG_FILE_NAME = "client-logs.txt";

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

        private const int BufferSize = 8192;

        private static async Task<string?> FindLastPortInFileAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[BufferSize];
            byte[] lineBuffer = new byte[BufferSize * 2];
            int lineLength = 0;

            long position = stream.Length;

            while (position > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(BufferSize, position);
                position -= toRead;

                stream.Seek(position, SeekOrigin.Begin);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);

                for (int i = read - 1; i >= 0; i--)
                {
                    byte b = buffer[i];

                    if (b == (byte)'\n')
                    {
                        if (lineLength > 0 &&
                            TryProcessLine(lineBuffer.AsSpan(0, lineLength), out var result))
                            return result;

                        lineLength = 0;
                    }
                    else if (b != (byte)'\r')
                    {
                        lineBuffer[lineLength++] = b;
                    }
                }
            }

            if (lineLength > 0 &&
                TryProcessLine(lineBuffer.AsSpan(0, lineLength), out var finalResult))
                return finalResult;

            return null;
        }

        private static bool TryProcessLine(
            ReadOnlySpan<byte> reversedLine,
            out string? result)
        {
            result = null;

            Span<byte> temp = stackalloc byte[reversedLine.Length];

            for (int i = 0; i < reversedLine.Length; i++)
                temp[i] = reversedLine[reversedLine.Length - i - 1];

            string line = System.Text.Encoding.UTF8.GetString(temp);

            if (STATUS_STOPPED_REGEX.IsMatch(line))
                return true;

            var m = PORT_REGEX.Match(line);
            if (m.Success)
            {
                result = m.Groups[1].Value;
                return true;
            }

            return false;
        }
    }
}
