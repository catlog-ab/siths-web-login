# SITHS Web Login

[Svenska](README.sv.md)

Sign in to a web application with a Swedish healthcare **SITHS smart card**: **card in the reader = signed in, card removed = signed out**.

The project has three parts:
- a small Windows program that reads the card,
- a Chrome/Edge extension,
- a server protocol with a ready-made example implementation in PHP.

Login requires the card PIN and a cryptographic signature made by the card, so presenting the (public) certificate is not enough. When the card is removed, the user is signed out within a second.

```
SITHS card ─PC/SC─> SithsWebLogin.exe ─native messaging─> browser extension ─HTTPS─> your web application
```

| Folder | Contents |
|---|---|
| [`native-host/`](native-host/) | `SithsWebLogin.exe`: reads the card via PC/SC and signs with the card's key through Windows (Net iD / SITHS eID). |
| [`extension/`](extension/) | The browser extension (Manifest V3). |
| [`example-server-php/`](example-server-php/) | Example PHP server that plugs into an existing application, see [INSTALL.md](example-server-php/INSTALL.md) (Swedish). |
| [`deployments/`](deployments/) | One JSON file per organization with domains, names and extension IDs. |
| [`tools/`](tools/) | `package.ps1` builds the extension and the installer zip for an organization; `make-icons.ps1` and `make-screenshot.ps1` create icons and store images. |
| [`docs/PROTOCOL.md`](docs/PROTOCOL.md) | The protocol, for anyone implementing the server in another language. |

## How it works
1. The card is inserted and the program notifies the extension.
2. The extension fetches a one-time challenge from the site. The program signs `siths-web-login|<domain>|<challenge>` with the card's HSA-id certificate; Net iD / SITHS eID asks for the PIN.
3. The server verifies that:
   - the certificate chains to the SITHS root CA,
   - the certificate is valid and not revoked (OCSP),
   - the signature is correct.

   It then looks up the user by HSA-id and returns a token to the extension.
4. The site's tabs are signed in with a normal session cookie that is bound to the card session.
5. The extension sends a heartbeat every 30 seconds. Removing the card signs out all tabs immediately. If the browser or the program crashes, the server signs the user out after 90 seconds.

After a cancelled PIN, a failed login or a logout from the site, automatic login pauses until the user clicks *Try again* or removes and reinserts the card. This way password login still works while a card is in the reader.

Details are in [docs/PROTOCOL.md](docs/PROTOCOL.md); the security model is described in [SECURITY.md](SECURITY.md).

## Requirements
- **Client**: Windows 10/11 with .NET Framework 4.8 (included in Windows) and Chrome or Edge. Net iD or the SITHS eID software must be installed so that the card's certificates appear in the Windows certificate store.
- **Server**: any language. The example server requires PHP 7.4+ with `openssl` and `pdo`.

## Getting started (development)

```bash
native-host\build.cmd
```
The build uses `csc.exe` from .NET Framework, so no SDK is needed. The output is `native-host\bin\SithsWebLogin.exe`.

Test card detection and signing without installing anything:
```bash
powershell -ExecutionPolicy Bypass -File native-host\selftest.ps1
```

Run the whole chain locally against the example server on `http://localhost:8080`:
1. Copy `native-host\siths-host.example.json` to `native-host\bin\siths-host.json` and double-click `native-host\bin\SithsWebLogin.exe`. It installs for the current user, no admin rights needed.
2. Open `chrome://extensions`, enable **Developer mode**, choose **Load unpacked** and select the `extension/` folder. The ID becomes `dgfnlehhckaoeoiaipbbaibicbheadia` thanks to the development key in the manifest.
3. Start the example server with `domain_pattern` set to `'/^localhost:8080$/'` and `revocation` set to `'off'` (see [INSTALL.md](example-server-php/INSTALL.md)).

## Deploying for an organization

1. **Deployment file.** Copy [`deployments/example.json`](deployments/example.json) to `deployments/<org>.json` and fill in:

   | Field | Meaning |
   |---|---|
   | `displayName` | Name shown in the installer dialog, under Apps and in the popup. |
   | `hostName` | Unique name for the card program, e.g. `se.company.siths`. Lets several organizations have separate installations on the same computer. |
   | `extension.name`, `extension.version` | Extension name and version. |
   | `extension.ids` | Extension IDs allowed to talk to the card program (store IDs plus any development ID). |
   | `extension.store.chrome`, `extension.store.edge` | IDs in Chrome Web Store and Edge Add-ons. When set, the card program registers them at install time so the browser downloads the extension and asks the user to enable it; uninstalling removes it again. |
   | `extension.key` | Public key for a fixed ID in development builds. It is stripped automatically from the store upload. |
   | `sites.matchPatterns` | Chrome match patterns for the sites, e.g. `https://*.company.se/*`. |
   | `sites.originPattern` | The same sites as a regular expression for the origin, e.g. `^https://[a-z0-9-]+\.company\.se$`. |
   | `api.base`, `api.suffix` | Where the server endpoints live. Default `/api/siths/<name>.php`. |
   | `downloadUrl` | Where users download the card program zip; shown in the popup when it is missing. A path starting with `/` refers to the site the user last visited. |

   Deployment files for real organizations are ignored by git; only the template is in the repository.

2. **Package.**
   ```bash
   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Deployment deployments\<org>.json -Exe <signed SithsWebLogin.exe>
   ```
   This creates `dist\<hostName>\` with:
   - `extension\`: load unpacked for testing.
   - `extension.zip`: upload to Chrome Web Store / Edge Add-ons, e.g. as *unlisted*. See [docs/CHROME_WEB_STORE.md](docs/CHROME_WEB_STORE.md) (Swedish).
   - `SithsWebLogin-<hostName>.zip`: the exe and `siths-host.json`. Users extract it and double-click the exe.

3. **Server.** Implement the protocol or integrate the example server following [INSTALL.md](example-server-php/INSTALL.md). The server's `domain_pattern` must match `sites.originPattern`.

4. **Users.** Every user needs their HSA-id registered in the application.

Uninstall via *Settings → Apps* or `SithsWebLogin.exe --uninstall`.

**Incognito / InPrivate:** browsers disable extensions there until the user turns on *Allow in Incognito* (Chrome) or *Allow in InPrivate* (Edge) in the extension's details. The popup shows how when it is off.

## Code signing

Windows 11 with Smart App Control, and computers with AppLocker or App Control for Business, may block unsigned executables. That is why the exe is identical for every organization: it can be signed once, and everything organization-specific lives in `siths-host.json`.

- **Official releases** are built by [GitHub Actions](.github/workflows/build.yml). The plan is to sign them through SignPath Foundation's free signing for open source.
- **Your own builds** can be signed with your own code signing certificate:
  ```bash
  signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a native-host\bin\SithsWebLogin.exe
  ```

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

Only `SithsWebLogin.exe` built by this repository's [GitHub Actions workflow](.github/workflows/build.yml) from the tagged source is signed. Every release is approved manually before signing.

**Team roles**
- Committers and reviewers: [members of catlog-ab](https://github.com/orgs/catlog-ab/people)
- Approvers: [Tomas Wallentinus](https://github.com/tomaswallentinus) (Catlog AB)

**Privacy policy:** SithsWebLogin.exe and the browser extension do not transfer any information to other networked systems unless specifically requested by the user. The only data sent is the card certificate (name and HSA-id) and a signature, to the website the user signs in to (configured by the organization that deploys the software), when the user inserts the card and enters the PIN. The program changes only per-user settings (registry entries under `HKEY_CURRENT_USER` for browser integration), shows a confirmation when installed, and can be fully uninstalled via *Settings → Apps* or `SithsWebLogin.exe --uninstall`.

## License

[MIT](LICENSE)
