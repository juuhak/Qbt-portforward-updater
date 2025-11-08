using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace QbPortUpdater
{
    class Worker : BackgroundService
    {
    private readonly ILogger<Worker> _logger;

    private readonly Microsoft.Extensions.Options.IOptionsMonitor<AppConfig> _configMonitor;
    private readonly IEnumerable<IPortDetector> _detectors;
    private AppConfig _lastValidConfig = new AppConfig();

        public Worker(Microsoft.Extensions.Options.IOptionsMonitor<AppConfig> configMonitor, IEnumerable<IPortDetector> detectors, ILogger<Worker> logger)
        {
            _logger = logger;
            _configMonitor = configMonitor;
            _detectors = detectors;

            _configMonitor.OnChange((newCfg, name) =>
            {
                var missing = ValidateConfig(newCfg);
                if (missing.Count == 0)
                {
                    _lastValidConfig = newCfg;
                    _logger.LogTrace("Configuration reloaded and accepted (keys: {keys})", string.Join(", ", new[] { "qbUrl", "qbUsername", "logDirectory" }));
                }
                else
                {
                    _logger.LogError("Reloaded configuration is invalid; missing keys: {missing}. Ignoring change.", string.Join(", ", missing));
                }
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var scriptDir = AppContext.BaseDirectory;

            // Use monitored AppConfig; fall back to last valid snapshot if needed.
            var current = _configMonitor.CurrentValue ?? _lastValidConfig;
            var qbUrl = current.QbUrl;
            var qbUser = current.QbUsername;
            var qbPass = current.QbPassword;
            var logDir = current.LogDirectory;
            var intervalStr = (current.IntervalSeconds?.ToString()) ?? "60";
            var logLevelRaw = current.LogLevel ?? "ERROR";

            // Validate required settings at startup; exit on failure.
            var initialMissing = ValidateConfig(current);
            if (initialMissing.Count > 0)
            {
                _logger.LogError("Missing required appsettings.json fields at startup: {missing}", string.Join(", ", initialMissing));
                Environment.Exit(1);
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(qbUrl)) missing.Add("qbUrl");
            if (string.IsNullOrWhiteSpace(qbUser)) missing.Add("qbUsername");
            if (string.IsNullOrWhiteSpace(qbPass)) missing.Add("qbPassword");
            if (string.IsNullOrWhiteSpace(logDir)) missing.Add("logDirectory");
            if (missing.Any())
            {
                _logger.LogError("Missing required config.json fields: {missing}", string.Join(", ", missing));
                // Exit immediately with a non-zero exit code so tools and services notice the configuration error.
                Environment.Exit(1);
            }

            // Required values exist; assert non-null for nullability analysis.
            var logDirNonNull = logDir!;
            var qbUrlNonNull = qbUrl!;
            var qbUserNonNull = qbUser!;
            var qbPassNonNull = qbPass!;

            // Startup information is verbose for local debugging; move to Trace so Info is reserved for port-update events.
            _logger.LogTrace("Starting qb-port-updater. Checking {logDir} every {intervalSeconds} seconds. Using qBittorrent URL: {qbUrl}; username: {qbUser}; logLevel: {logLevel}", logDirNonNull, intervalStr, qbUrlNonNull, qbUserNonNull, logLevelRaw);

            if (!int.TryParse(intervalStr, out var interval) || interval <= 0)
            {
                _logger.LogError("Invalid intervalSeconds value in config.json: {intervalStr}. It must be a positive integer number of seconds.", intervalStr);
                Environment.Exit(1);
            }

            string? lastPort = null;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cfg = _configMonitor.CurrentValue ?? _lastValidConfig;
                    var port = await GetPortFromDetectorsAsync(cfg.LogDirectory!, cfg.Detector, stoppingToken);
                    if (port != null)
                    {
                        _logger.LogInformation("Found forwarded port: {port}", port);
                        if (port != lastPort)
                        {
                            var ok = await UpdateQbittorrentPort(cfg.QbUrl!, cfg.QbUsername!, cfg.QbPassword!, port);
                            if (ok) lastPort = port;
                        }
                        else
                        {
                            _logger.LogTrace("Port has not changed. Skipping update.");
                        }
                    }
                    else
                    {
                        _logger.LogTrace("No forwarded port found.");
                    }
                }
                catch (Exception ex)
                {
                        _logger.LogError(ex, "Error during iteration");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
                }
                catch (TaskCanceledException) { break; }
            }

            _logger.LogWarning("Worker stopping.");
        }

        private record Config
        (
            string? QbUrl,
            string? QbUsername,
            string? QbPassword,
            string? LogDirectory,
            int? IntervalSeconds,
            string? LogLevel,
            string? ServiceAccount,
            string? ServicePassword
        );

        // Note: log levels are controlled by NLog configuration (nlog.config). The legacy LogLevel parsing
        // is no longer used.

        private async Task<string?> GetPortFromDetectorsAsync(string logDirectory, DetectorType? allowedDetector, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<IPortDetector> toTry = _detectors;
                if (allowedDetector.HasValue)
                {
                    var dt = allowedDetector.Value;
                    toTry = _detectors.Where(d => d.DetectorType == dt);
                    _logger.LogTrace("Using configured detector: {detector}", dt.ToString());
                }

                foreach (var d in toTry)
                {
                    try
                    {
                        var p = await d.GetLastPortAsync(logDirectory, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(p))
                        {
                            _logger.LogTrace("Detector {detector} found port {port}", d.Name, p);
                            return p;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Detector {detector} failed to inspect {dir}", d.Name, logDirectory);
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task<bool> UpdateQbittorrentPort(string qbUrl, string username, string password, string newPort)
        {
            try
            {
                _logger.LogInformation("Attempting to update qBittorrent listen_port to {newPort} at {qbUrl} for user {username}", newPort, qbUrl, username);

                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);

                var loginContent = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string,string>("username", username),
                    new KeyValuePair<string,string>("password", password)
                });

                var loginResp = await SendWithRetriesAsync(() => http.PostAsync(new Uri(new Uri(qbUrl), "/api/v2/auth/login"), loginContent));
                if (loginResp == null)
                {
                    _logger.LogWarning("Login request to qBittorrent at {qbUrl} failed (no response)", qbUrl);
                    return false;
                }

                var loginText = await loginResp.Content.ReadAsStringAsync();
                if (loginResp.StatusCode != HttpStatusCode.OK || string.IsNullOrEmpty(loginText) || !loginText.Contains("Ok", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("qBittorrent login failed. Status: {status}, Response: {resp}", (int)loginResp.StatusCode, loginText);
                    return false;
                }

                _logger.LogInformation("Authenticated to qBittorrent at {qbUrl}", qbUrl);

                var payloadObj = new { listen_port = int.Parse(newPort) };
                var payloadJson = JsonSerializer.Serialize(payloadObj);
                var prefsContent = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string,string>("json", payloadJson)
                });

                var setResp = await SendWithRetriesAsync(() => http.PostAsync(new Uri(new Uri(qbUrl), "/api/v2/app/setPreferences"), prefsContent));
                if (setResp == null)
                {
                    _logger.LogWarning("Failed to set preferences on qBittorrent at {qbUrl} (no response)", qbUrl);
                    return false;
                }

                var setText = await setResp.Content.ReadAsStringAsync();
                if (setResp.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation("Successfully updated qBittorrent listen_port to {newPort}", newPort);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to update qBittorrent preferences. Status: {status}, Response: {resp}", (int)setResp.StatusCode, setText);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateQbittorrentPort failed");
                return false;
            }
        }

        private static async Task<HttpResponseMessage?> SendWithRetriesAsync(Func<Task<HttpResponseMessage>> action, int maxAttempts = 3)
        {
            var attempt = 0;
            var backoff = 500; // ms
            while (attempt < maxAttempts)
            {
                attempt++;
                try
                {
                    var resp = await action();
                    if ((int)resp.StatusCode >= 500 || resp.StatusCode == (HttpStatusCode)429)
                    {
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(backoff);
                            backoff *= 2;
                            continue;
                        }
                    }
                    return resp;
                }
                catch (HttpRequestException) when (attempt < maxAttempts)
                {
                    await Task.Delay(backoff);
                    backoff *= 2;
                    continue;
                }
                catch (TaskCanceledException) when (attempt < maxAttempts)
                {
                    await Task.Delay(backoff);
                    backoff *= 2;
                    continue;
                }
            }
            return null;
        }

        private static List<string> ValidateConfig(AppConfig? cfg)
        {
            var missing = new List<string>();
            if (cfg == null) { missing.Add("QbPortUpdater (section missing)"); return missing; }
            if (string.IsNullOrWhiteSpace(cfg.QbUrl)) missing.Add("qbUrl");
            if (string.IsNullOrWhiteSpace(cfg.QbUsername)) missing.Add("qbUsername");
            if (string.IsNullOrWhiteSpace(cfg.QbPassword)) missing.Add("qbPassword");
            if (string.IsNullOrWhiteSpace(cfg.LogDirectory)) missing.Add("logDirectory");
            return missing;
        }
    }
}
