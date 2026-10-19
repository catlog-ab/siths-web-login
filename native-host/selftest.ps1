# Testar kortläsning och signering utan att installera något.
# Laddar den byggda koden direkt i PowerShell (fungerar även när Smart App Control blockerar exe-filen).
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\SithsWebLogin.exe'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$signer = $asm.GetType('SithsWebLogin.CardSigner')

$mon = [Activator]::CreateInstance($asm.GetType('SithsWebLogin.CardMonitor'))
$mon.Start()
Write-Host "Sätt i eller ta ur kortet. Tryck S för att signera (PIN-dialog visas), Q för att avsluta.`n"
$last = $null
while ($true) {
    if ($mon.CardPresent -ne $last) {
        $last = $mon.CardPresent
        if ($last) { Write-Host ("{0:HH:mm:ss}  KORT IN   {1}" -f (Get-Date), $mon.CurrentReader) -ForegroundColor Green }
        else { Write-Host ("{0:HH:mm:ss}  KORT UR" -f (Get-Date)) -ForegroundColor Yellow }
    }
    if ([Console]::KeyAvailable) {
        $k = [Console]::ReadKey($true).Key
        if ($k -eq 'Q') { break }
        if ($k -eq 'S') {
            try {
                $nonce = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
                $r = $signer.GetMethod('Sign').Invoke($null, @('selftest.invalid', $nonce))
                # Verifiera signaturen med certifikatets publika nyckel, precis som servern gör.
                $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($r.CertDer))
                $payload = $signer.GetMethod('Payload').Invoke($null, @('selftest.invalid', $nonce))
                $ok = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert).VerifyData(
                    $payload, [Convert]::FromBase64String($r.Signature),
                    [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
                Write-Host ("Signerat ({0}) med {1} - verifiering: {2}" -f $r.Alg, $cert.GetNameInfo('SimpleName', $false), $(if ($ok) { 'OK' } else { 'FEL' })) -ForegroundColor Cyan
            } catch {
                Write-Host ("Signering misslyckades: " + $_.Exception.GetBaseException().Message) -ForegroundColor Red
            }
        }
    }
    Start-Sleep -Milliseconds 200
}
