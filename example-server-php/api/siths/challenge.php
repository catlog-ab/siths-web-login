<?php
declare(strict_types=1);
require_once __DIR__ . '/../../lib/bootstrap.php';

siths_input();
$domain = siths_domain();
siths_json(['nonce' => siths_store()->createChallenge($domain), 'domain' => $domain]);
