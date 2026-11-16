# The signing step's stand-in (rule 16), while the windows-signing environment has no values: the same files are signed
# with signtool and a throwaway self-signed code-signing certificate, then each signature is read back. It proves the
# file list, signtool and the read-back; when KIFF's Azure certificate exists, only the environment's values change.
# Nothing signed here is kept: the certificate is made in this run and removed at its end.
param(
    [Parameter(Mandatory = $true)] [string] $Folder,
    [string[]] $Files = @('Tafs.App.exe', 'Tafs.App.dll', 'Tafs.Core.dll')
)
$ErrorActionPreference = 'Stop'

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe not found in the Windows SDK' }

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=tafs signing dry run' `
    -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddHours(1)
try {
    foreach ($name in $Files) {
        $path = Join-Path $Folder $name
        if (-not (Test-Path $path)) { throw "missing: $path" }
        & $signtool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint /s My $path
        if ($LASTEXITCODE -ne 0) { throw "signtool failed on $name" }
        # Untrusted by design (a throwaway root), but it must be there, by this certificate, over this file.
        $signature = Get-AuthenticodeSignature $path
        if ($signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw "no dry-run signature on $name" }
        Write-Host "dry run: signed $name ($($signature.Status))"
    }
}
finally {
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
}
