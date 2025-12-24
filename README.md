# qBittorrent Port Updater

A Windows Service that automatically updates qBittorrent's listening port from VPN log files. It detects the latest forwarded port from your VPN and updates qBittorrent via its Web API.

This service is a .NET reimplementation of the original Python `qb-port-updater.py` utility.

## Installation

1.  Download the latest `.zip` package from the [GitHub Releases](https://github.com/jphastings/Qbt-portforward-updater/releases) page.
2.  Unzip the archive to your desired location (e.g., `C:\Program Files\QbPortUpdater`).
3.  Open `appsettings.json` in a text editor and fill in your qBittorrent and detector details (see Configuration below).
4.  Run PowerShell as an Administrator and execute the installation script:
    ```powershell
    # Navigate to the unzipped directory
    cd "C:\Program Files\QbPortUpdater"

    # Run the installer
    .\install_service.ps1 -ServiceName "QbPortUpdater"
    ```
5.  The service will be installed and started.

## Configuration

Configuration is managed in the `appsettings.json` file. The following settings are required:

```json
{
  "QbPortUpdater": {
    "qbUrl": "http://127.0.0.1:8080",
    "qbUsername": "your_username",
    "qbPassword": "your_password",
    "detector": "ProtonVPN"
  }
}
```
-   `qbUrl`: The URL for your qBittorrent Web UI.
-   `qbUsername`: Your qBittorrent username.
-   `qbPassword`: Your qBittorrent password.
-   `detector`: The method used to find the port. Currently, only `ProtonVPN` is supported.

Settings can also be overridden with [Environment Variables](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0#environment-variables) (e.g., `DOTNET_QbPortUpdater__qbPassword=mysecretpassword`).

Logging is also configured in `appsettings.json` under the `"NLog"` section.

## Uninstallation

To remove the service, run `uninstall_service.ps1` as an Administrator.

```powershell
# Navigate to the service directory
cd "C:\Program Files\QbPortUpdater"

# Uninstall the service and remove all files
.\uninstall_service.ps1 -ServiceName "QbPortUpdater" -RemoveFiles
```

## Building from Source

To build and run the project locally, you will need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Set environment to Development for local runs
$env:DOTNET_ENVIRONMENT = 'Development'

# Run the project
dotnet run --project src/qb-port-updater.csproj
```
When developing, you can place an `appsettings.Development.json` file in the `src` directory to override configuration for your local environment.