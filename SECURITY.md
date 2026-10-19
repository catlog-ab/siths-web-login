# Security

Please report vulnerabilities **privately** using GitHub's *Report a vulnerability* button (Security tab) rather than as a public issue. Describe how the issue can be exploited and which part is affected (card program, extension or example server). Reports in English or Swedish are welcome.

## Security model in brief
- The certificate on the card is public and proves nothing on its own. Login requires the card's private key to sign a one-time challenge, which requires the PIN.
- What is signed is `siths-web-login|<domain>|<nonce>`. A signature for one site therefore cannot be replayed on another, and a nonce can be used only once, within 2 minutes.
- The server verifies the certificate chain to the SITHS root CA, the issuer, the validity period, the revocation status (OCSP) and the signature before the HSA-id is used.
- `cardToken` and one-time codes are stored only as SHA-256 hashes on the server. In the extension the token lives only in memory (`chrome.storage.session`).
- The card program only accepts messages from extensions listed in `allowedExtensionIds`. Chrome enforces this through `allowed_origins` in the native messaging host manifest.
- If the card is removed, the card program exits or the browser closes, heartbeats stop and the server ends the card session within `heartbeat_timeout` (90 s).
- After a failed or cancelled login, or when the site has ended the card session, the extension does not retry on its own, so the PIN dialog is never shown repeatedly without user action.
