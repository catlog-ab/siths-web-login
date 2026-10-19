<?php
declare(strict_types=1);

require_once __DIR__ . '/SithsStore.php';
require_once __DIR__ . '/SithsVerifier.php';
require_once __DIR__ . '/../adapter.php';

function siths_config(): array
{
    static $cfg = null;
    if ($cfg === null) {
        $file = __DIR__ . '/../config.php';
        if (!is_file($file)) {
            throw new RuntimeException('example-server-php/config.php saknas (utgå från config.example.php)');
        }
        $cfg = require $file;
    }
    return $cfg;
}

function siths_store(): SithsStore
{
    static $store = null;
    if ($store === null) {
        $c = siths_config();
        $pdo = new PDO($c['db_dsn'], $c['db_user'] ?? null, $c['db_pass'] ?? null, [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        ]);
        $store = new SithsStore($pdo, $c);
    }
    return $store;
}

/** Kundsajtens domän för den här förfrågan (t.ex. kund1.example.org). */
function siths_domain(): string
{
    static $domain = null;
    if ($domain === null) {
        $host = strtolower((string)($_SERVER['HTTP_HOST'] ?? ''));
        if (!preg_match(siths_config()['domain_pattern'], $host)) {
            siths_json(['error' => 'unknown_domain'], 400);
        }
        $domain = $host;
    }
    return $domain;
}

function siths_json(array $data, int $status = 200)
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function siths_input(): array
{
    if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
        siths_json(['error' => 'method_not_allowed'], 405);
    }
    $data = json_decode((string)file_get_contents('php://input'), true);
    return is_array($data) ? $data : [];
}

function siths_bearer(): ?string
{
    $h = $_SERVER['HTTP_AUTHORIZATION'] ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '';
    if ($h === '' && function_exists('getallheaders')) {
        foreach (getallheaders() as $k => $v) {
            if (strcasecmp($k, 'Authorization') === 0) $h = $v;
        }
    }
    return preg_match('/^Bearer\s+([A-Za-z0-9_-]{20,200})$/', $h, $m) ? $m[1] : null;
}

/** Kortsessionen som bearer-token pekar på, eller 401. */
function siths_require_card_session(): array
{
    $token = siths_bearer();
    $cs = $token ? siths_store()->activeCardSessionByToken($token, siths_domain()) : null;
    if (!$cs) {
        siths_json(['error' => 'invalid_token'], 401);
    }
    return $cs;
}

function siths_public_user(array $cs): array
{
    return ['name' => $cs['display_name'], 'hsaId' => $cs['hsa_id']];
}

function siths_b64url(string $bin): string
{
    return rtrim(strtr(base64_encode($bin), '+/', '-_'), '=');
}
