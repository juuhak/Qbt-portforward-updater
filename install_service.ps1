<#
.SYNOPSIS
    Publish the .NET app and install it as a Windows Service.

.DESCRIPTION
    This script installs the project as a Windows Service.

.PARAMETER ServiceAccount
    The account the service will run under. Use 'LocalSystem' for the built-in account or 'DOMAIN\\User'.

.PARAMETER ServicePassword
    Password for the service account, if required.

.PARAMETER NonInteractive
    Switch to run the installer without interactive prompts (useful for automation). When set, any
    provided -ServiceAccount and -ServicePassword parameters are used and no prompts are shown.
#>

param(
    [string]$ServiceAccount = 'LocalSystem',
    [string]$ServicePassword = '',
    [switch]$NonInteractive
)

function Fail([string]$msg) {
    Write-Error $msg
    exit 1
}

# Interactive configuration for the service account (unless parameters were supplied)
# If the user provided -ServiceAccount or -ServicePassword on the command line or in config.json,
# we still offer a chance to accept or override them interactively.
try {
    # Skip interactive prompts if explicitly requested or if account parameters were provided on the command line
    if ($NonInteractive -or $PSBoundParameters.ContainsKey('ServiceAccount') -or $PSBoundParameters.ContainsKey('ServicePassword')) {
        Write-Host "Interactive prompts skipped because -NonInteractive was specified or account parameters were provided. Using account: $ServiceAccount"
        if ($ServiceAccount -eq 'LocalSystem' -or $ServiceAccount -eq 'NT AUTHORITY\\LocalSystem') { $ServicePassword = '' }
    }
    else {
        # Only prompt if running in an interactive host (console) and input is available
        if ($Host.Name -eq 'ConsoleHost' -and $PSHost.PrivateData -ne $null) {
            Write-Host "\nService account configuration (interactive)."

            $current = if ([string]::IsNullOrWhiteSpace($ServiceAccount)) { 'LocalSystem' } else { $ServiceAccount }
            Write-Host "Current account: $current"

            $inputAccount = Read-Host "Press Enter to accept current account, or type a different account (e.g. 'LocalSystem' or 'DOMAIN\\User' or '.\\User')"
            if (-not [string]::IsNullOrWhiteSpace($inputAccount)) {
                $ServiceAccount = $inputAccount
            }

            if ($ServiceAccount -ne 'LocalSystem' -and $ServiceAccount -ne 'NT AUTHORITY\\LocalSystem') {
                # If no password provided yet, ask for one. Allow empty to skip (installer will warn).
                if ([string]::IsNullOrWhiteSpace($ServicePassword)) {
                    $pw = Read-Host "Enter password for account '$ServiceAccount' (leave blank to skip)" -AsSecureString
                    if ($pw.Length -gt 0) {
                        # Convert SecureString to plain text for sc.exe; this keeps the password out of the script's logs.
                        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($pw)
                        try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
                        $ServicePassword = $plain
                    }
                } else {
                    Write-Host "Using ServicePassword provided via parameters or config.json."
                }
            } else {
                # LocalSystem doesn't require a password
                $ServicePassword = ''
            }
        }
    }
} catch {
    Write-Host "Interactive configuration skipped (non-interactive host or error): $_"
}


$exeName = 'qb-port-updater.exe'
$ServiceName = 'QbtPortUpdater'

Write-Host "Service executable: $exeName"

# If service exists, stop and delete
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service $ServiceName already exists. Stopping and removing..."
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Set ACLs (grant Read/Execute to service account if not LocalSystem)
if ($ServiceAccount -ne 'LocalSystem' -and $ServiceAccount -ne 'NT AUTHORITY\LocalSystem') {
    Write-Host "Granting read/execute permissions on $publishFull to $ServiceAccount"
    $grantArg = "{0}:(OI)(CI)RX" -f $ServiceAccount
    & icacls $publishFull /grant $grantArg /T | Out-Null
}

Write-Host "Creating service $ServiceName"
$binPath = '"' + $PSScriptRoot + $exeName + '"'
Write-Host "Service binPath: $binPath"
if ($ServiceAccount -eq 'LocalSystem' -or $ServiceAccount -eq 'NT AUTHORITY\LocalSystem') {
    sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "$ServiceName"  | Out-Null
} else {
    if ([string]::IsNullOrEmpty($ServicePassword)) {
        Write-Host "Warning: installing service with account $ServiceAccount but no ServicePassword provided. The create may fail."
    }
    sc.exe create $ServiceName binPath= $binPath start= auto obj= "$ServiceAccount" password= "$ServicePassword" DisplayName= "$ServiceName" | Out-Null
}

sc.exe description $ServiceName "Updates qBittorrent listen port based on VPN forwarded ports" | Out-Null

Write-Host "Starting service $ServiceName"
sc.exe start $ServiceName

Write-Host "Install complete. Check service status in Services MMC or use 'Get-Service -Name $ServiceName'."
