 # qBittorrent Port Updater — .NET port

A .NET (C# .NET 8) reimplementation of the original Python `qb-port-updater.py` utility.

The service watches VPN/log sources for forwarded-port announcements and updates qBittorrent's `listen_port` via the qBittorrent Web API.

## Highlights

- Generic Host background service (Microsoft.Extensions.Hosting) and designed to run as a Windows Service.
- NLog for logging (configurable via `nlog.config`).
- Typed, extensible detector architecture — detectors implement a small async interface and are provided via DI.
- Configuration via `appsettings.json` (section `QbPortUpdater`) with support for environment-specific files like `appsettings.Development.json` during development.
- Installer/uninstaller PowerShell helpers to install the published app as a Windows Service.
- GitHub Actions workflow to produce a self-contained Windows single-file publish and package installer scripts and example configuration.

## How it works (high level)

- A `Worker` background service polls for the most recent forwarded port using a pluggable detector (e.g. ProtonVPN detector).
- When a new port is detected, the service logs the event and calls the qBittorrent Web API to update `listen_port` (with retries/backoff).
- Configuration changes to `appsettings.json` are observed at runtime (IOptionsMonitor.OnChange) and validated; valid changes are accepted without restarting the service.

## Configuration

Configuration is loaded from `appsettings.json` (or `appsettings.{Environment}.json` when `DOTNET_ENVIRONMENT` is set). The relevant section is `QbPortUpdater`.

Example: `appsettings.json.example` (copy to `appsettings.json` and edit locally):

```json
{
  "QbPortUpdater": {
    "qbUrl": "http://127.0.0.1:8080",
    "qbUsername": "admin",
    "qbPassword": "",
    "intervalSeconds": 60,
    "logDirectory": "C:\\path\\to\\logs",
    "logLevel": "INFO",
    "serviceAccount": "LocalSystem",
    "servicePassword": "",
    "detector": "ProtonVPN"
  }
}
```

Important notes:

- The configuration keys are under the `QbPortUpdater` object (this is bound to the `AppConfig` POCO).
- `detector` is a typed enum value (one of the built-in detector names). Example values: `ProtonVPN`, `WireGuard`, `WindowsApi`.
  - The app requires a valid built-in `detector` value at startup; it will log an error and exit if the configured value is missing or does not match any registered detector implementation.
- Do NOT commit files that contain real secrets (passwords). Use `appsettings.json.example` as the template and create a local `appsettings.json` with secrets.

Development convenience:

- `appsettings.Development.json` (not committed) can be used during local development. The project copies only example files into publish artifacts — runtime appsettings files are not included in published artifacts by CI.

## Detectors (extensible)

- The service discovers one or more `IPortDetector` implementations registered with DI. Each detector implements an async contract:
  - `DetectorType DetectorType { get; }` — built-in typed enum
  - `string Name { get; }` — human-friendly name for diagnostics
  - `Task<string?> GetLastPortAsync(string logDirectory, CancellationToken ct)` — return the latest forwarded port or `null`.

- Built-in detectors (enum `DetectorType`) include:
  - `ProtonVPN` — parses ProtonVPN/OpenVPN-style log files (the original behavior)
  - `WireGuard` (placeholder)
  - `WindowsApi` (placeholder)

- To add a new built-in detector:
 1. Add a new value to `DetectorType`.
 2. Implement `IPortDetector` and return that `DetectorType`.
 3. Register the detector in `Program.cs` (services.AddSingleton<IPortDetector, YourDetector>()).
 4. Update docs/examples and CI if needed.

## Logging

- Logging is configured using `nlog.config` placed next to the executable. The repository contains `src/nlog.config` used in development and packaged by CI when present.
- The service uses Info-level logs to report port-update attempts and their outcomes. Trace-level logs capture diagnostic details (detector selection, config reloads, and routine checks).

## Build & run (development)

Requirements: .NET 8 SDK

Run in development mode (loads `appsettings.Development.json` when DOTNET_ENVIRONMENT=Development):

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project src/qb-port-updater.csproj
```

You can also run the published exe (after publishing) directly.

## Publish & CI

A GitHub Actions workflow (.github/workflows/release.yml) builds a self-contained Windows single-file publish and packages the published files together with the installer scripts and example configuration. The workflow uploads a zip artifact and creates a GitHub Release when you push a `v*` tag.

Notes:
- The project intentionally only packages example configuration files. Do not include actual runtime `appsettings.json` or sensitive data in release artifacts.
- The publish command in CI produces a self-contained single-file Windows binary.

## Install as a Windows Service

Scripts:

- `install_service.ps1` — installs the published app as a Windows Service. It expects a prebuilt publish folder (the script will not build by itself). The script copies `appsettings.json` into the publish folder if present in the project root (so installer-time configuration injection is supported).
- `uninstall_service.ps1` — removes the service and optionally deletes the publish folder.

Examples (run PowerShell as Administrator):

```powershell
# Install using LocalSystem (self-contained publish assumed present in publish folder)
.\install_service.ps1 -ServiceName QbPortUpdater

# Uninstall and remove files
.\uninstall_service.ps1 -ServiceName QbPortUpdater -RemoveFiles
```

## Runtime behavior & safety

- On startup the app validates required settings (qbUrl, qbUsername, qbPassword, logDirectory, detector). If required fields are missing the service exits with a non-zero code.
- The configured `detector` must match a built-in `DetectorType` and have a registered implementation. The app will exit if the configured detector is not available.
- Config reloads are validated. Invalid reload attempts are ignored and logged; valid reloads are applied without restarting.

## Extensibility ideas

- Add `IHttpClientFactory` + Polly-based resilience for qBittorrent HTTP calls.
- Add unit tests for detectors and Worker behavior (mock detectors, mock HttpClient).
- Add plugin-based detectors loaded at runtime (requires moving from enum-typed detectors to a registry + extensible type system).

## Troubleshooting

- If the service fails to start, check the Windows Event Log and the NLog file next to the executable.
- Ensure `appsettings.json` is present next to the executable and contains the required `QbPortUpdater` section.

---

If you'd like, I can:

- Update the README with more examples for adding detectors.
- Add unit tests for the ProtonVPN detector.
- Add CI smoke-tests to validate the publish artifact contents.

*** End of README ***# qBittorrent Port Updater — .NET port

This repository contains a .NET C# port of the original Python `qb-port-updater.py` script.

What it does
- Watches Proton VPN log files (or other specified log folder) for lines like `Port pair 51870->...`.
- When a forwarded port is detected, it updates qBittorrent's `listen_port` via the qBittorrent Web API.

Files added
- `qb-port-updater.csproj` — .NET project file (net8.0).
- `Program.cs` — main program logic.
 - `nlog.config` — NLog configuration used by the service (rolling file + console targets).
 - Updated `start_with_env.ps1` to prefer running the .NET app when `dotnet` is available.

Configuration
The application now prefers a `config.json` file placed in the same folder as the executable. If present, the app reads configuration (qBittorrent URL, credentials, log directory, interval, log level) from that file. If `config.json` is not found, the app falls back to reading the environment variables used previously. This makes the PowerShell wrapper optional for the .NET service.

Create `config.json` with these keys. A safe template is included as `config.json.example` — copy it and fill the secret fields locally:

```powershell
copy config.json.example config.json
# then edit config.json to add passwords and correct paths
```

Example contents (see `config.json.example`):

```json
{
  "qbUrl": "http://127.0.0.1:8080",
  "qbUsername": "admin",
  "qbPassword": "",
  "intervalSeconds": 60,
  "logDirectory": "C:\\path\\to\\logs",
  "logLevel": "INFO",
  "serviceAccount": "LocalSystem",
  "servicePassword": ""
}
```

Configuration
The application now reads configuration exclusively from `config.json` placed in the same folder as the executable. Legacy environment-variable fallbacks have been removed.

Create `appsettings.json` from the example (`appsettings.json.example`) and ensure the required fields are present before running the service. The configuration is expected under the `QbPortUpdater` section.

Build & run
The C# project source is now in the `src/` folder. For development you can run the project directly from the repo (requires the .NET SDK):

```powershell
dotnet run --project src/qb-port-updater.csproj
```

Alternatively use `start_with_env.ps1` which runs the project in `src/` if `dotnet` is available, or falls back to running the legacy Python script when present.

CI / GitHub Releases
The repository contains a GitHub Actions workflow `.github/workflows/release.yml` that will build a self-contained Windows publish and package it together with the installer scripts (`install_service.ps1`, `uninstall_service.ps1`, `start_with_env.ps1`) plus `config.json.example` and `nlog.config` (if present). The workflow uploads `artifact.zip` as a workflow artifact and, when you push a tag `v*`, creates a GitHub Release and attaches the zip as a release asset.

Notes
- The C# program performs simple retry/backoff for transient HTTP errors.
Notes
- The C# program performs simple retry/backoff for transient HTTP errors.
- Logging is handled by NLog; configure logging via `nlog.config` next to the executable (the repository includes `src/nlog.config` which will be packaged by CI and copied by the installer if present).

Limitations & possible improvements
- Add unit tests and CI.
- Replace ad-hoc HTTP retry logic with `IHttpClientFactory` + Polly for advanced retry/circuit-breaker functionality.

Install as Windows Service (automated)
-------------------------------------

Two helper scripts were added:

- `install_service.ps1` — publishes the project and installs it as a Windows Service. Supports self-contained single-file publishing and allows specifying a service account and a service password. The script will also read `ServiceAccount` and `ServicePassword` from `config.json` if present.
- `uninstall_service.ps1` — stops and removes the service; optionally deletes the publish folder.

Examples:

Publish a self-contained exe and install as LocalSystem (run PowerShell as Administrator):

```powershell
.\install_service.ps1 -SelfContained -ServiceName QbPortUpdater
```

Publish framework-dependent and install using a domain account (provide password via `-ServicePassword` or `config.json`):

```powershell
.\install_service.ps1 -ServiceAccount 'DOMAIN\User' -ServicePassword 'P@ssw0rd' -ServiceName QbPortUpdater
```

Uninstall the service and remove the published files:

```powershell
.\uninstall_service.ps1 -ServiceName QbPortUpdater -RemoveFiles
```

Notes:
- The installer script copies `config.json` into the publish folder if present. Ensure it contains correct credentials and paths or set environment variables at system level.
- Running the installer requires Administrator privileges.
- If you choose a non-LocalSystem service account, the script will grant read/execute (RX) permissions for the publish folder to that account.
