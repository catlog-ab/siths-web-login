<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

// Tillägget hämtar en kortlivad engångskod som sajtens flik växlar in i session.php.
siths_input();
$cs = siths_require_card_session();
siths_json(['code' => siths_store()->createLoginCode($cs['id'])]);
