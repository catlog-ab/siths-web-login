<?php
declare(strict_types=1);

class SithsVerificationException extends RuntimeException
{
}

/**
 * Verifierar att en inloggning kommer från ett giltigt SITHS-kort:
 * certifikatkedja till SITHS rot-CA, rätt utfärdare, giltighetstid, spärrstatus (OCSP)
 * och att kortets privata nyckel signerat just den här utmaningen.
 */
class SithsVerifier
{
    private array $cfg;

    public function __construct(array $cfg)
    {
        $this->cfg = $cfg;
    }

    /** Samma sträng som native-hosten signerar (CardSigner.Payload). */
    public static function signedPayload(string $domain, string $nonce): string
    {
        return 'siths-web-login|' . $domain . '|' . $nonce;
    }

    /**
     * @return array{hsaId:string, name:string, fingerprint:string, notAfter:int}
     */
    public function verify(string $certB64, string $signatureB64, string $alg, string $nonce, string $domain): array
    {
        $der = base64_decode($certB64, true);
        $sig = base64_decode($signatureB64, true);
        if ($der === false || $sig === false || strlen($der) > 8192 || strlen($sig) > 1024) {
            throw new SithsVerificationException('bad_encoding');
        }
        if (!in_array($alg, ['RS256', 'ES256'], true)) {
            throw new SithsVerificationException('bad_alg');
        }

        $pem = self::derToPem($der);
        $cert = openssl_x509_read($pem);
        if ($cert === false) {
            throw new SithsVerificationException('bad_certificate');
        }
        $info = openssl_x509_parse($cert, false);

        // 1. Utfärdare
        $issuerCn = $info['issuer']['commonName'] ?? '';
        if (!preg_match($this->cfg['issuer_cn_pattern'], $issuerCn)) {
            throw new SithsVerificationException('wrong_issuer');
        }

        // 2. Kedja + giltighetstid + användning för klientautentisering
        $bundle = $this->cfg['ca_bundle'];
        if (!is_file($bundle)) {
            throw new RuntimeException('CA-bundle saknas: ' . $bundle);
        }
        if (openssl_x509_checkpurpose($cert, X509_PURPOSE_SSL_CLIENT, [$bundle]) !== true) {
            throw new SithsVerificationException('untrusted_certificate');
        }
        $now = time();
        if ($now < $info['validFrom_time_t'] || $now > $info['validTo_time_t']) {
            throw new SithsVerificationException('certificate_expired');
        }

        // 3. Signaturen över utmaningen
        $payload = self::signedPayload($domain, $nonce);
        $pub = openssl_pkey_get_public($cert);
        if ($pub === false || openssl_verify($payload, $sig, $pub, OPENSSL_ALGO_SHA256) !== 1) {
            throw new SithsVerificationException('bad_signature');
        }

        // 4. Spärrstatus
        if (($this->cfg['revocation'] ?? 'ocsp') !== 'off') {
            $this->checkOcsp($pem, $info);
        }

        // 5. HSA-id ligger i subject serialNumber, t.ex. SE1234567890-AB12
        $hsaId = $info['subject']['serialNumber'] ?? '';
        if (!preg_match('/^SE\d{10}-[A-Za-z0-9]+$/', $hsaId)) {
            throw new SithsVerificationException('missing_hsa_id');
        }
        $name = $info['subject']['commonName'] ?? $hsaId;

        return [
            'hsaId' => $hsaId,
            'name' => is_array($name) ? implode(' ', $name) : $name,
            'fingerprint' => openssl_x509_fingerprint($cert, 'sha256'),
            'notAfter' => (int)$info['validTo_time_t'],
        ];
    }

    private function checkOcsp(string $leafPem, array $info): void
    {
        $fingerprint = hash('sha256', $leafPem);
        $cacheFile = sys_get_temp_dir() . '/siths_ocsp_' . $fingerprint;
        $ttl = (int)($this->cfg['ocsp_cache_seconds'] ?? 600);
        if (is_file($cacheFile) && filemtime($cacheFile) > time() - $ttl && file_get_contents($cacheFile) === 'good') {
            return;
        }

        $url = self::ocspUrl($info);
        $issuerPem = $this->findIssuer($leafPem);
        if ($url === null || $issuerPem === null) {
            throw new SithsVerificationException('ocsp_unavailable');
        }

        $dir = sys_get_temp_dir();
        $leafFile = tempnam($dir, 'sl');
        $issuerFile = tempnam($dir, 'si');
        file_put_contents($leafFile, $leafPem);
        file_put_contents($issuerFile, $issuerPem);
        try {
            $cmd = escapeshellarg($this->cfg['openssl_bin'] ?? 'openssl') . ' ocsp'
                . ' -issuer ' . escapeshellarg($issuerFile)
                . ' -cert ' . escapeshellarg($leafFile)
                . ' -url ' . escapeshellarg($url)
                . ' -CAfile ' . escapeshellarg($this->cfg['ca_bundle'])
                . ' -timeout 10 -no_nonce 2>&1';
            $out = shell_exec($cmd);
        } finally {
            @unlink($leafFile);
            @unlink($issuerFile);
        }

        // Utdata: "<fil>: good" plus "Response verify OK" när svaret är signerat av rätt CA.
        if (!is_string($out) || strpos($out, 'Response verify OK') === false) {
            throw new SithsVerificationException('ocsp_unavailable');
        }
        if (preg_match('/:\s*(good|revoked|unknown)\b/', $out, $m) !== 1 || $m[1] !== 'good') {
            throw new SithsVerificationException('certificate_revoked');
        }
        @file_put_contents($cacheFile, 'good');
    }

    private static function ocspUrl(array $info): ?string
    {
        $aia = $info['extensions']['authorityInfoAccess'] ?? '';
        return preg_match('~OCSP - URI:(https?://\S+)~', $aia, $m) ? $m[1] : null;
    }

    /** Utfärdarens certifikat ur CA-bundlen (behövs för OCSP-förfrågan). */
    private function findIssuer(string $leafPem): ?string
    {
        $leaf = openssl_x509_read($leafPem);
        $bundle = (string)file_get_contents($this->cfg['ca_bundle']);
        preg_match_all('/-----BEGIN CERTIFICATE-----.+?-----END CERTIFICATE-----/s', $bundle, $m);
        foreach ($m[0] as $caPem) {
            $ca = openssl_x509_read($caPem);
            if ($ca !== false && openssl_x509_verify($leaf, $ca) === 1) {
                return $caPem;
            }
        }
        return null;
    }

    private static function derToPem(string $der): string
    {
        return "-----BEGIN CERTIFICATE-----\n" . chunk_split(base64_encode($der), 64, "\n") . "-----END CERTIFICATE-----\n";
    }
}
