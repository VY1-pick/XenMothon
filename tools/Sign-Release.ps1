[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Thumbprint,
    [string]$PublisherPath,
    [string]$InstallerPath,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
$signtoolPath = $signtool.Source
if (-not $signtool) {
    $windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $windowsKits) {
        $signtool = Get-ChildItem $windowsKits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
    }
    if (-not $signtool) {
        throw 'signtool.exe was not found. Install the Windows SDK signing tools (or the Windows App SDK) first.'
    }
    $signtoolPath = $signtool.FullName
}

$certificate = Get-ChildItem 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -eq $Thumbprint.Replace(' ', '') } | Select-Object -First 1
if (-not $certificate) {
    $certificate = Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $Thumbprint.Replace(' ', '') } | Select-Object -First 1
}
if (-not $certificate) { throw "No certificate with thumbprint $Thumbprint was found in the current user or machine store." }
if (-not $certificate.HasPrivateKey) { throw 'The certificate has no private key available to this account.' }

Write-Host "Signing with '$($certificate.Subject)' (expires $($certificate.NotAfter.ToString('yyyy-MM-dd')))."

# 1. Application binaries first: the shell, the native quality probe and the bundled runtime.
$targets = @()
if ($PublisherPath) {
    $publish = (Resolve-Path (Join-Path $root $PublisherPath)).Path
    $targets += Get-ChildItem -LiteralPath $publish -Recurse -File -Include 'XenMotion.exe', 'XenMotion.dll', 'NativeProbe.exe' |
        Select-Object -ExpandProperty FullName
}
if ($InstallerPath) {
    $installer = Join-Path $root $InstallerPath
    if (-not (Test-Path -LiteralPath $installer)) { throw "Installer not found: $installer" }
    $targets += (Resolve-Path $installer).Path
}
if ($targets.Count -eq 0) { throw 'Nothing to sign. Pass -PublisherPath and/or -InstallerPath.' }

foreach ($target in $targets) {
    & $signtoolPath sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /v $target
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $target" }
}

# 2. Verify every signature before the files can be published.
foreach ($target in $targets) {
    $status = (Get-AuthenticodeSignature -LiteralPath $target).Status
    Write-Host ("{0,-8} {1}" -f $status, $target)
    if ($status -ne 'Valid') { throw "Signature verification failed for $target ($status)." }
}

Write-Host 'All requested files are signed and verified.' -ForegroundColor Green
Write-Host 'Now rebuild SHA256SUMS.txt and run tools\Verify-Release.ps1 again, because signed files have new hashes.'
