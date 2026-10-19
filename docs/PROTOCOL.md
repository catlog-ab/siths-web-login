# Protocol

Describes how the parts talk to each other, so that the server can be implemented in any language. [example-server-php](../example-server-php/) is the reference implementation.

```
SITHS card ─PC/SC─> SithsWebLogin.exe ─native messaging─> extension ─HTTPS─> the site
                                                              └─ content script in the site's tabs
```

## 1. Native messaging (extension ↔ SithsWebLogin.exe)

Chrome's standard format: each message is a 4-byte length (little endian) followed by UTF-8 JSON. Requests carry an `id` that is echoed in the reply.

**Requests from the extension**

```json
{ "id": 1, "cmd": "status" }
{ "id": 2, "cmd": "sign", "domain": "app.example.org", "nonce": "<from the server>" }
```

**Replies**

```json
{ "type": "reply", "id": 1, "ok": true, "cardPresent": true, "reader": "OMNIKEY 3x21 0", "version": "1.0.0" }
{ "type": "reply", "id": 2, "ok": true, "cert": "<base64 DER>", "signature": "<base64>", "alg": "RS256", "thumbprint": "…" }
{ "type": "reply", "id": 2, "ok": false, "error": "pin_cancelled" }
```

Possible errors: `no_card`, `no_certificate`, `pin_cancelled`, `pin_blocked`, `sign_failed: …`, `bad_request`, `unknown_command`.

**Events** (sent unsolicited)

```json
{ "type": "card_inserted", "reader": "…", "atr": "3BDF…" }
{ "type": "card_removed", "reader": null, "atr": null }
```

### What is signed

```
siths-web-login|<domain>|<nonce>
```

The string is encoded as UTF-8 and signed with SHA-256:
- **`RS256`**: RSASSA-PKCS1-v1_5.
- **`ES256`**: ECDSA with a DER-encoded signature.

The nonce is treated as an opaque string and signed exactly as received (up to 200 characters).

The card program picks the certificate in the user's certificate store that meets all of these requirements:
- It has a private key.
- It is currently valid.
- The issuer contains "SITHS" and "HSA-id".
- Key usage includes `digitalSignature` and extended key usage includes `clientAuth`.

## 2. HTTP API (extension/tab ↔ the site)

All calls are `POST` with a JSON body (may be `{}`) and a JSON response. The path is `apiBase + name + apiSuffix`, in the example server `/api/siths/<name>.php`. Errors are an HTTP status plus `{"error": "<code>"}`.

The *domain* that everything is bound to is the request's `Host`, e.g. `app.example.org` or `localhost:8080`. The server must accept only domains it serves itself.

| Name | Auth | Body | Response |
|---|---|---|---|
| `challenge` | – | `{}` | `{"nonce", "domain"}`. Nonce: at least 128 bits of randomness, single use, ≤ 2 min, bound to the domain. |
| `login` | – | `{"nonce", "cert", "signature", "alg"}` | `{"cardToken", "user": {"name", "hsaId"}}` |
| `heartbeat` | Bearer | `{}` | `{"ok": true, "user": {…}}`. Updates the last heartbeat. |
| `logout` | Bearer | `{}` | `{"ok": true}`. Ends the card session. |
| `code` | Bearer | `{}` | `{"code"}`. One-time code, ≤ 60 s. |
| `session` | site cookie | `{"code"}` | `{"changed": bool}`. Called from the site's tab (same origin). |
| `branding` | – | `{}` | `{"name", "logo"}`. `logo` is an absolute URL or `null`. |

`Bearer` means the header `Authorization: Bearer <cardToken>`. An invalid or expired token gives `401 {"error":"invalid_token"}`. The extension then treats the card session as ended (see section 3) and does not log in again until the user asks.

### `login`: server requirements
1. Consume the `nonce` atomically. It must exist, be unused, not be expired and belong to the same domain.
2. The certificate must chain to the SITHS root CA and be issued by an HSA-id CA. It must be valid now and allow client authentication.
3. Check revocation status with OCSP (AIA in the certificate).
4. Verify `signature` over `siths-web-login|<domain>|<nonce>` with the certificate's public key.
5. Read the HSA-id from the subject `serialNumber` and look up the user for the domain. No user: `403 no_account`.
6. Create a card session with domain, user and `last_heartbeat = now`, and return a random token. Store only a hash of the token.

Error codes: `invalid_challenge`, `bad_encoding`, `bad_alg`, `bad_certificate`, `wrong_issuer`, `untrusted_certificate`, `certificate_expired`, `bad_signature`, `ocsp_unavailable`, `certificate_revoked`, `missing_hsa_id`, `no_account`, `unknown_domain`.

### Sessions and logout
- A card session is active if it has not been ended and `last_heartbeat` is at most `heartbeat_timeout` seconds old (90 s; the extension sends one every 30 s).
- `session` signs in the tab's normal session and stores the card session's id in it. This must use a new session id (protection against session fixation). If the session already belongs to the same card session, or another login (e.g. a password login) takes precedence, the server answers `changed: false` and the tab does nothing. Otherwise it answers `true` and the tab reloads.
- `session` may answer `401` (e.g. the site's login expired due to inactivity and the server ended the card session). The extension then pauses with `session_ended` and does not reload the page.
- On **every** page load the server checks that a card-bound session still has an active card session, otherwise it is signed out. In the example server `siths_guard()` does this.

## 3. Extension ↔ the site's page

At `document_start` the content script sets the attribute `data-siths-web-login="<version>"` on `<html>`, plus `data-siths-web-login-status="<status>"`, which is updated on every state change. It also sends `window.postMessage` to the page when the state changes:

```js
{ source: "siths-web-login", type: "siths-state",
  state: { status: "no_host" | "no_card" | "no_site" | "logging_in" | "logged_in" | "error",
           error, user: { name }, origin } }
```

After `status: "error"`, automatic login is paused. No new PIN dialog is shown on page load or tab switch until the page sends `retry` or the card is removed and reinserted. `error` is `pin_cancelled`, `session_ended` (a heartbeat, `code` or `session` got 401, e.g. after a manual logout or inactivity) or an error code from `login`.

The page can ask for the current state or for a new attempt:

```js
window.postMessage({ source: "siths-web-login-page", type: "get_state" }, location.origin);
window.postMessage({ source: "siths-web-login-page", type: "retry" }, location.origin);
```

`retry` always starts a new login with PIN if the card is present, even if the server has already ended the previous card session. A page such as "Sign in again with SITHS card" can therefore send `retry` as soon as it loads.

For example, the page can show "Insert your SITHS card" when `status` is `no_card`, or link to the card program when it is `no_host`. When the extension is missing (no `data-siths-web-login` attribute) it may also be installed but not allowed in incognito/InPrivate windows.
