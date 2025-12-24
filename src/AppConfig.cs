namespace QbPortUpdater
{
    public class AppConfig
    {
        public string? QbUrl { get; set; }
        public string? QbUsername { get; set; }
        public string? QbPassword { get; set; }
        public string? LogDirectory { get; set; }
        public int? IntervalSeconds { get; set; }
        public string? LogLevel { get; set; }
        public string? ServiceAccount { get; set; }
        public string? ServicePassword { get; set; }
        // Optional single detector to use (built-in list). If omitted, all registered detectors are tried in registration order.
        public DetectorType? Detector { get; set; }
    }
}
