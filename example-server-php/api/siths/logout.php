<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

// Anropas av tillägget när kortet tas ur. Alla flikar/sessioner som är knutna till
// kortsessionen loggas ut av siths_guard() vid nästa sidladdning.
siths_input();
$cs = siths_require_card_session();
siths_store()->endCardSession($cs['id']);
siths_json(['ok' => true]);
