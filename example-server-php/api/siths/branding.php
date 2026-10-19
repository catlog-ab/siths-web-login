<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

// Kundens namn och logga, visas i tilläggets popup.
siths_input();
$domain = siths_domain();
$b = app_tenant_branding($domain);
$logo = $b['logo'] ?? null;
if (is_string($logo) && $logo !== '' && $logo[0] === '/') {
    $logo = (!empty($_SERVER['HTTPS']) ? 'https://' : 'http://') . $domain . $logo;
}
siths_json(['name' => (string)($b['name'] ?? $domain), 'logo' => $logo]);
