<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

$in = siths_input();
$nonce = is_string($in['nonce'] ?? null) ? $in['nonce'] : '';
$cert = is_string($in['cert'] ?? null) ? $in['cert'] : '';
$signature = is_string($in['signature'] ?? null) ? $in['signature'] : '';
$alg = is_string($in['alg'] ?? null) ? $in['alg'] : '';

$domain = siths_domain();
if ($nonce === '' || !siths_store()->consumeChallenge($nonce, $domain)) {
    siths_json(['error' => 'invalid_challenge'], 400);
}

try {
    $id = (new SithsVerifier(siths_config()))->verify($cert, $signature, $alg, $nonce, $domain);
} catch (SithsVerificationException $e) {
    error_log('SITHS-inloggning nekad: ' . $e->getMessage());
    siths_json(['error' => $e->getMessage()], 403);
}

$user = app_find_user_by_hsa_id($id['hsaId'], $domain);
if ($user === null) {
    error_log('SITHS-inloggning: inget konto för ' . $id['hsaId'] . ' hos ' . $domain);
    siths_json(['error' => 'no_account'], 403);
}

$token = siths_store()->createCardSession($domain, $user['id'], $id['hsaId'], $user['name'] ?: $id['name'], $id['fingerprint']);
siths_json([
    'cardToken' => $token,
    'user' => ['name' => $user['name'] ?: $id['name'], 'hsaId' => $id['hsaId']],
]);
