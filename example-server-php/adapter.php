<?php
declare(strict_types=1);

/*
 * ANPASSA TILL ER APPLIKATION
 * ---------------------------
 * De här funktionerna är den enda kopplingen mellan SITHS-inloggningen och applikationens
 * befintliga kunder, användare och inloggning. Exemplen nedan förutsätter tabellerna
 * `tenants` (id, domain, name, logo_path) och `users` (id, tenant_id, name, hsa_id), och att
 * applikationen markerar inloggning med $_SESSION['user_id']. Byt ut mot det ni använder.
 * Har applikationen bara en sajt kan $domain ignoreras.
 */

/**
 * Slå upp användaren med HSA-id:t hos sajten/kunden $domain (t.ex. "kund1.example.org").
 * Returnera null om personen inte har konto där.
 */
function app_find_user_by_hsa_id(string $hsaId, string $domain): ?array
{
    $st = siths_store_pdo()->prepare(
        'SELECT u.id, u.name FROM users u JOIN tenants t ON t.id = u.tenant_id
         WHERE u.hsa_id = ? AND t.domain = ? LIMIT 1'
    );
    $st->execute([$hsaId, $domain]);
    $row = $st->fetch(PDO::FETCH_ASSOC);
    return $row ? ['id' => (string)$row['id'], 'name' => (string)$row['name']] : null;
}

/**
 * Kundens namn och logga till tilläggets popup. Logga som absolut URL eller sökväg på sajten,
 * t.ex. "/images/logo.png". Returnera ['logo' => null] för bara text.
 */
function app_tenant_branding(string $domain): array
{
    $st = siths_store_pdo()->prepare('SELECT name, logo_path FROM tenants WHERE domain = ? LIMIT 1');
    $st->execute([$domain]);
    $row = $st->fetch(PDO::FETCH_ASSOC);
    return $row ? ['name' => $row['name'], 'logo' => $row['logo_path']] : ['name' => $domain, 'logo' => null];
}

/** Starta sessionen på samma sätt som resten av applikationen gör (cookie-namn, flaggor, handler). */
function app_start_session(): void
{
    if (session_status() !== PHP_SESSION_ACTIVE) {
        session_start([
            'cookie_secure' => !empty($_SERVER['HTTPS']),
            'cookie_httponly' => true,
            'cookie_samesite' => 'Lax',
        ]);
    }
}

/** Markera användaren som inloggad i den aktuella sessionen. */
function app_login_user(array $user): void
{
    $_SESSION['user_id'] = $user['id'];
}

/** Logga ut den aktuella sessionen. */
function app_logout_user(): void
{
    $_SESSION = [];
    if (ini_get('session.use_cookies')) {
        $p = session_get_cookie_params();
        setcookie(session_name(), '', time() - 3600, $p['path'], $p['domain'], $p['secure'], $p['httponly']);
    }
    session_destroy();
}

/** Samma databasanslutning som SITHS-tabellerna; byt gärna mot sajtens egen. */
function siths_store_pdo(): PDO
{
    static $pdo = null;
    if ($pdo === null) {
        $c = siths_config();
        $pdo = new PDO($c['db_dsn'], $c['db_user'] ?? null, $c['db_pass'] ?? null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    }
    return $pdo;
}
