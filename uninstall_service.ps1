param([string]$ServiceName = 'QbPortUpdater')

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping service $ServiceName..."
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "Deleting service $ServiceName..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
    Write-Host "Service $ServiceName removed."
} else { Write-Host "Service $ServiceName not found." }

Write-Host "Uninstall complete."
