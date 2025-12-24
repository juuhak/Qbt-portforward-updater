namespace QbPortUpdater
{
    /// <summary>
    /// Contract for components that can detect the most-recent forwarded port from some source.
    /// Implementations should return the port number as a string (e.g. "51820") or null when none found.
    /// </summary>
    public interface IPortDetector
    {
        /// <summary>
        /// Human-friendly name of the detector (used for logs).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The typed detector value for built-in detectors.
        /// </summary>
        DetectorType DetectorType { get; }

        /// <summary>
        /// Inspect the provided log directory (or other inputs) and return the last forwarded port, or null if none.
        /// This method is asynchronous because detectors may perform I/O or network calls.
        /// </summary>
        /// <param name="logDirectory">Path to directory to inspect; detector implementations may ignore or use this.</param>
        /// <param name="cancellationToken">Cancellation token forwarded from the worker loop.</param>
        /// <returns>Port string or null.</returns>
        System.Threading.Tasks.Task<string?> GetLastPortAsync(string logDirectory, System.Threading.CancellationToken cancellationToken);
    }
}
