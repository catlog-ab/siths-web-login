<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

// Anropas från sajtens flik (samma origin, med sajtens cookie). Loggar in fliken
// som kortets ägare och knyter sessionen till kortsessionen.
$in = siths_input();

$origin = $_SERVER['HTTP_ORIGIN'] ?? null;
if ($origin !== null) {
    $port = parse_url($origin, PHP_URL_PORT);
    if (parse_url($origin, PHP_URL_HOST) . ($port ? ':' . $port : '') !== siths_domain()) {
        siths_json(['error' => 'bad_origin'], 403);
    }
}

$code = is_string($in['code'] ?? null) ? $in['code'] : '';
$cs = $code !== '' ? siths_store()->consumeLoginCode($code, siths_domain()) : null;
if ($cs === null) {
    siths_json(['error' => 'invalid_code'], 401);
}

app_start_session();
if (($_SESSION['siths_card_session'] ?? null) === $cs['id']) {
    siths_json(['changed' => false]);
}

// Ny inloggning (eller annat kort än tidigare): nytt sessions-id mot session fixation.
$_SESSION = [];
session_regenerate_id(true);
app_login_user(['id' => $cs['user_id'], 'name' => $cs['display_name']]);
$_SESSION['siths_card_session'] = $cs['id'];
siths_json(['changed' => true]);
