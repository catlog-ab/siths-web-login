<?php
declare(strict_types=1);

/**
 * Databaslagring för utmaningar, kortsessioner och engångskoder.
 * Tokens och koder lagras endast som SHA-256-hash.
 */
class SithsStore
{
    private PDO $db;
    private array $cfg;

    public function __construct(PDO $db, array $cfg)
    {
        $this->db = $db;
        $this->cfg = $cfg;
    }

    // ---------- Utmaningar ----------

    public function createChallenge(string $domain): string
    {
        $this->cleanup();
        $nonce = siths_b64url(random_bytes(32));
        $this->db->prepare('INSERT INTO siths_challenges (nonce, domain, expires_at) VALUES (?, ?, ?)')
            ->execute([$nonce, $domain, time() + (int)$this->cfg['challenge_ttl']]);
        return $nonce;
    }

    /** Förbrukar utmaningen; true endast första gången, för samma kund och inom giltighetstiden. */
    public function consumeChallenge(string $nonce, string $domain): bool
    {
        $st = $this->db->prepare('DELETE FROM siths_challenges WHERE nonce = ? AND domain = ? AND expires_at >= ?');
        $st->execute([$nonce, $domain, time()]);
        return $st->rowCount() === 1;
    }

    // ---------- Kortsessioner ----------

    /** Skapar en kortsession och returnerar bearer-token (visas bara här). */
    public function createCardSession(string $domain, string $userId, string $hsaId, string $displayName, string $certFingerprint): string
    {
        $token = siths_b64url(random_bytes(32));
        $now = time();
        $this->db->prepare(
            'INSERT INTO siths_card_sessions (id, domain, user_id, hsa_id, display_name, cert_fingerprint, created_at, last_heartbeat)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)'
        )->execute([hash('sha256', $token), $domain, $userId, $hsaId, $displayName, $certFingerprint, $now, $now]);
        return $token;
    }

    public function activeCardSessionByToken(string $token, string $domain): ?array
    {
        return $this->activeCardSession(hash('sha256', $token), $domain);
    }

    /** Kortsessionen om den är aktiv och hör till kunden $domain. */
    public function activeCardSession(string $id, string $domain): ?array
    {
        $st = $this->db->prepare(
            'SELECT * FROM siths_card_sessions WHERE id = ? AND domain = ? AND ended_at IS NULL AND last_heartbeat >= ?'
        );
        $st->execute([$id, $domain, time() - (int)$this->cfg['heartbeat_timeout']]);
        $row = $st->fetch();
        return $row ?: null;
    }

    public function heartbeat(string $id): void
    {
        $this->db->prepare('UPDATE siths_card_sessions SET last_heartbeat = ? WHERE id = ? AND ended_at IS NULL')
            ->execute([time(), $id]);
    }

    public function endCardSession(string $id): void
    {
        $this->db->prepare('UPDATE siths_card_sessions SET ended_at = ? WHERE id = ? AND ended_at IS NULL')
            ->execute([time(), $id]);
    }

    // ---------- Engångskoder (tillägg -> flikens cookie-session) ----------

    public function createLoginCode(string $cardSessionId): string
    {
        $code = siths_b64url(random_bytes(32));
        $this->db->prepare('INSERT INTO siths_login_codes (code_hash, card_session_id, expires_at) VALUES (?, ?, ?)')
            ->execute([hash('sha256', $code), $cardSessionId, time() + (int)$this->cfg['login_code_ttl']]);
        return $code;
    }

    /** Förbrukar koden och returnerar den aktiva kortsessionen den hör till. */
    public function consumeLoginCode(string $code, string $domain): ?array
    {
        $hash = hash('sha256', $code);
        $st = $this->db->prepare('SELECT card_session_id FROM siths_login_codes WHERE code_hash = ? AND expires_at >= ?');
        $st->execute([$hash, time()]);
        $id = $st->fetchColumn();
        $del = $this->db->prepare('DELETE FROM siths_login_codes WHERE code_hash = ?');
        $del->execute([$hash]);
        if ($id === false || $del->rowCount() !== 1) {
            return null;
        }
        return $this->activeCardSession((string)$id, $domain);
    }

    private function cleanup(): void
    {
        $now = time();
        $this->db->prepare('DELETE FROM siths_challenges WHERE expires_at < ?')->execute([$now]);
        $this->db->prepare('DELETE FROM siths_login_codes WHERE expires_at < ?')->execute([$now]);
        // Avslutade/utgångna kortsessioner sparas en dag som spårbarhet.
        $this->db->prepare(
            'DELETE FROM siths_card_sessions WHERE (ended_at IS NOT NULL AND ended_at < ?) OR last_heartbeat < ?'
        )->execute([$now - 86400, $now - 86400]);
    }
}
