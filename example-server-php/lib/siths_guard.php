<?php
declare(strict_types=1);

/*
 * Inkludera i början av varje sida på sajten, efter att sessionen startats:
 *
 *     require_once '/sökväg/till/example-server-php/lib/siths_guard.php';
 *     siths_guard();
 *
 * Om sessionen loggades in med SITHS-kort och kortet tagits ur (eller tillägget inte hörts av
 * på heartbeat_timeout sekunder) loggas användaren ut. Andra inloggningar påverkas inte.
 */

require_once __DIR__ . '/bootstrap.php';

function siths_guard(): bool
{
    app_start_session();
    $id = $_SESSION['siths_card_session'] ?? null;
    if ($id === null) {
        return true;
    }
    $host = strtolower((string)($_SERVER['HTTP_HOST'] ?? ''));
    if (siths_store()->activeCardSession((string)$id, $host) !== null) {
        return true;
    }
    app_logout_user();
    return false;
}
