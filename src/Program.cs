using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;

namespace QbPortUpdater
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            
            NLog.LogManager.Configuration = new NLogLoggingConfiguration(config.GetSection("NLog"));

            var logger = NLog.LogManager.GetCurrentClassLogger();
            try
            {
                var builder = Host.CreateDefaultBuilder(args)
                    .UseContentRoot(AppContext.BaseDirectory)
                    .UseWindowsService()
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    })
                    .UseNLog()
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.Configure<AppConfig>(hostContext.Configuration.GetSection("QbPortUpdater"));
                        services.AddHttpClient();
                        services.AddSingleton<IPortDetector, ProtonVpnPortDetector>();
                        services.AddHostedService<Worker>();
                    });

                var host = builder.Build();

                // Validate detector configuration: require a single valid detector name in config
                var services = host.Services;
                var cfg = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AppConfig>>().CurrentValue;
                var detectors = services.GetServices<IPortDetector>().ToList();
                if (!cfg.Detector.HasValue)
                {
                    logger.Error("No detector configured. Set 'QbPortUpdater:detector' in appsettings.json to one of: {detectors}", string.Join(", ", detectors.Select(d => d.DetectorType.ToString())));
                    return 1;
                }
                var selected = cfg.Detector.Value;
                var matches = detectors.Where(d => d.DetectorType == selected).ToList();
                if (matches.Count == 0)
                {
                    logger.Error("Configured detector '{detector}' not found (no registered implementation). Registered detectors: {detectors}", selected.ToString(), string.Join(", ", detectors.Select(d => d.DetectorType.ToString())));
                    return 1;
                }

                logger.Info("Using detector: {detector}", selected.ToString());

                await host.RunAsync();
                return 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Host terminated unexpectedly");
                throw;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }
    }
}
