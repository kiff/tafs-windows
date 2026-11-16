# Each signed file's signature, read back: present, by the expected subject exactly (the certificate's full subject, so a
# misconfigured profile or another organisation's certificate in the account fails, even one that chains to a trusted
# root), and Valid unless -Untrusted (the dry run's throwaway certificate chains to nothing). The same check for the real
# signing and the dry run, so the comparison itself runs on every main push.
param(
    [Parameter(Mandatory = $true)] [string] $Folder,
    [Parameter(Mandatory = $true)] [string] $Subject,
    [string[]] $Files = @('Tafs.App.exe', 'Tafs.App.dll', 'Tafs.Core.dll'),
    [switch] $Untrusted
)
$ErrorActionPreference = 'Stop'
if (-not $Subject.Trim()) { throw 'no expected subject' }
foreach ($name in $Files) {
    $signature = Get-AuthenticodeSignature (Join-Path $Folder $name)
    if (-not $signature.SignerCertificate) { throw "${name}: not signed" }
    if ($signature.SignerCertificate.Subject -ne $Subject) {
        throw "${name}: signed by '$($signature.SignerCertificate.Subject)', not '$Subject'"
    }
    if (-not $Untrusted -and $signature.Status -ne 'Valid') { throw "${name}: $($signature.Status)" }
    Write-Host "${name}: signed by $Subject ($($signature.Status))"
}
