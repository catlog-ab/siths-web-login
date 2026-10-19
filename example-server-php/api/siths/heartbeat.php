<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

siths_input();
$cs = siths_require_card_session();
siths_store()->heartbeat($cs['id']);
siths_json(['ok' => true, 'user' => siths_public_user($cs)]);
