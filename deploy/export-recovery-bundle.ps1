[CmdletBinding()]
param(
    [string]$Server = "ubuntu@145.239.78.104",
    [string]$KeyPath = (Join-Path $HOME ".ssh\emecworks_ovh"),
    [string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Emecworks-Recovery")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Find-CommandPath {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $command) {
        throw "Required command was not found: $Name"
    }

    return $command.Source
}

function Find-SevenZip {
    $command = Get-Command 7z -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "7-Zip\7z.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "7-Zip\7z.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    throw "7-Zip was not found. Install 7-Zip before exporting the recovery bundle."
}

if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf)) {
    throw "SSH private key was not found: $KeyPath"
}

$ssh = Find-CommandPath -Name "ssh"
$scp = Find-CommandPath -Name "scp"
$sevenZip = Find-SevenZip

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$timestamp = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$transferId = [Guid]::NewGuid().ToString("N")
$remoteArchive = "/home/ubuntu/.emecworks-recovery-$transferId.tar.gz"
$localPlaintext = Join-Path ([IO.Path]::GetTempPath()) "emecworks-recovery-$transferId.tar.gz"
$encryptedArchive = Join-Path $outputRoot "emecworks-recovery-$timestamp.7z"
$archiveVerified = $false

if (Test-Path -LiteralPath $encryptedArchive) {
    throw "Recovery archive already exists: $encryptedArchive"
}

$sshArguments = @(
    "-o", "BatchMode=yes",
    "-o", "StrictHostKeyChecking=yes",
    "-i", $KeyPath
)

$remoteCreateScript = @"
set -Eeuo pipefail
umask 077
remote_archive='$remoteArchive'
temporary_archive="`${remote_archive}.tmp"
cleanup() {
    sudo rm -f -- "`$temporary_archive"
}
trap cleanup EXIT

required_files=(
    /etc/emecworks/cloudflared.env
    /etc/emecworks/n8n-app.env
    /etc/emecworks/n8n-db.env
    /etc/emecworks/n8n-runners.env
    /etc/emecworks/portfolio.env
)

for required_file in "`${required_files[@]}"; do
    if ! sudo test -f "`$required_file"; then
        echo "Required recovery file is missing: `$required_file" >&2
        exit 1
    fi
done

latest_backup="`$(sudo find /var/backups/emecworks \
    -mindepth 1 \
    -maxdepth 1 \
    -type d \
    -name '????????T??????Z' \
    -printf '%f\n' |
    sort |
    tail -n 1)"

if [[ -z "`$latest_backup" ]]; then
    echo "No completed Emecworks backup was found." >&2
    exit 1
fi

sudo tar \
    -C / \
    -czf "`$temporary_archive" \
    etc/emecworks/cloudflared.env \
    etc/emecworks/n8n-app.env \
    etc/emecworks/n8n-db.env \
    etc/emecworks/n8n-runners.env \
    etc/emecworks/portfolio.env \
    "var/backups/emecworks/`${latest_backup}"

sudo chown ubuntu:ubuntu "`$temporary_archive"
sudo chmod 0600 "`$temporary_archive"
mv "`$temporary_archive" "`$remote_archive"
trap - EXIT
"@
$remoteCreateScript = $remoteCreateScript.Replace("`r`n", "`n")
$remoteCreateScriptBase64 = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($remoteCreateScript)
)

try {
    Write-Host "Preparing a temporary recovery package on the VPS..."
    & $ssh @sshArguments $Server "printf '%s' '$remoteCreateScriptBase64' | base64 -d | bash"
    if ($LASTEXITCODE -ne 0) {
        throw "The VPS recovery package could not be prepared."
    }

    Write-Host "Downloading the temporary package over SSH..."
    & $scp @sshArguments "${Server}:$remoteArchive" $localPlaintext
    if ($LASTEXITCODE -ne 0) {
        throw "The temporary recovery package could not be downloaded."
    }

    Write-Host ""
    Write-Host "7-Zip will ask for a password."
    Write-Host "Use a unique passphrase of at least five random words and store it separately."
    Write-Host ""

    & $sevenZip "a" "-t7z" "-mhe=on" "-p" $encryptedArchive $localPlaintext
    if ($LASTEXITCODE -ne 0) {
        throw "The encrypted recovery archive could not be created."
    }

    Write-Host ""
    Write-Host "Enter the same password once more so 7-Zip can test the archive."
    Write-Host ""

    & $sevenZip "t" $encryptedArchive
    if ($LASTEXITCODE -ne 0) {
        throw "The encrypted recovery archive failed verification."
    }

    $archiveVerified = $true
    $hash = Get-FileHash -LiteralPath $encryptedArchive -Algorithm SHA256
    Write-Host ""
    Write-Host "Recovery bundle created and verified:"
    Write-Host $encryptedArchive
    Write-Host "SHA256: $($hash.Hash)"
    Write-Host ""
    Write-Host "Copy this encrypted file to a second device or trusted cloud storage."
    Write-Host "Keep its password somewhere separate from the archive."
}
finally {
    if (Test-Path -LiteralPath $localPlaintext) {
        Remove-Item -LiteralPath $localPlaintext -Force
    }

    & $ssh @sshArguments $Server "rm -f -- '$remoteArchive'" 2>$null

    if (-not $archiveVerified -and (Test-Path -LiteralPath $encryptedArchive)) {
        Remove-Item -LiteralPath $encryptedArchive -Force
    }
}