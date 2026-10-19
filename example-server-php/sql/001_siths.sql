-- SITHS-kortinloggning. Fungerar i MySQL/MariaDB och SQLite.

CREATE TABLE siths_challenges (
    nonce       VARCHAR(64)  NOT NULL PRIMARY KEY,
    domain      VARCHAR(255) NOT NULL,
    expires_at  INTEGER      NOT NULL
);

CREATE TABLE siths_card_sessions (
    id               CHAR(64)     NOT NULL PRIMARY KEY, -- SHA-256 av bearer-token
    domain           VARCHAR(255) NOT NULL,           -- kundsajt, t.ex. kund1.example.org
    user_id          VARCHAR(64)  NOT NULL,
    hsa_id           VARCHAR(64)  NOT NULL,
    display_name     VARCHAR(255) NOT NULL,
    cert_fingerprint CHAR(64)     NOT NULL,
    created_at       INTEGER      NOT NULL,
    last_heartbeat   INTEGER      NOT NULL,
    ended_at         INTEGER      NULL
);

CREATE TABLE siths_login_codes (
    code_hash        CHAR(64) NOT NULL PRIMARY KEY,
    card_session_id  CHAR(64) NOT NULL,
    expires_at       INTEGER  NOT NULL
);

-- Koppla användare till HSA-id (anpassa tabell-/kolumnnamn efter er applikation):
-- ALTER TABLE users ADD COLUMN hsa_id VARCHAR(64) NULL;
-- CREATE UNIQUE INDEX users_tenant_hsa ON users (tenant_id, hsa_id);
