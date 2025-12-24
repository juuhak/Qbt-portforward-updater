using System.Reflection;

namespace QbPortUpdater
{
    /// <summary>
    /// Provides static access to the application's version information.
    /// The version is typically set via the project file (.csproj) and
    /// can be influenced by CI/CD processes (e.g., Git tags).
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// Gets the informational version of the application assembly.
        /// This usually corresponds to the full SemVer string, including any prerelease or build metadata.
        /// </summary>
        public static string GetInformationalVersion()
        {
            return typeof(VersionInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "Unknown Version";
        }

        /// <summary>
        /// Gets the assembly version of the application assembly.
        /// This is typically Major.Minor.0.0 or Major.Minor.Build.Revision.
        /// </summary>
        public static string GetAssemblyVersion()
        {
            return typeof(VersionInfo).Assembly.GetName().Version?.ToString() ?? "Unknown Version";
        }
    }
}
