<?php
// Kopiera till config.php och fyll i. config.php ska inte checkas in.
return [
    // Värdnamn (Host-header) som får använda SITHS-inloggning, som reguljärt uttryck.
    // Domänen ingår i det kortet signerar, så en signatur för en sajt kan inte användas på en annan.
    // En sajt:       '/^app\.example\.org$/'
    // Underdomäner:  '/^[a-z0-9-]+\.example\.org$/'
    // Lokal test:    '/^localhost:8080$/'
    'domain_pattern' => '/^[a-z0-9-]+\.example\.org$/',

    // Databas (PDO). MySQL/MariaDB/PostgreSQL i drift; SQLite fungerar för lokal test.
    'db_dsn'  => 'mysql:host=localhost;dbname=app;charset=utf8mb4',
    'db_user' => 'app',
    'db_pass' => '',

    // PEM-fil med SITHS rot-CA och utfärdande CA för HSA-id-certifikat (se certs/README.md).
    'ca_bundle' => __DIR__ . '/certs/siths-ca-bundle.pem',

    // Endast certifikat utfärdade av dessa CA (CN, reguljärt uttryck) godtas.
    'issuer_cn_pattern' => '/^SITHS e-id Person HSA-id \d+ CA v\d+$/',

    // Spärrkontroll: 'ocsp' (kräver openssl-kommandot och utgående HTTP) eller 'off' (endast för test).
    'revocation'   => 'ocsp',
    'openssl_bin'  => 'openssl',
    'ocsp_cache_seconds' => 600,

    'challenge_ttl'     => 120, // sekunder
    'login_code_ttl'    => 60,
    'heartbeat_timeout' => 90,  // kortsessionen dör om tillägget inte hörts av så här länge
];
