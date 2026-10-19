# SITHS Web Login

[English](README.md)

Logga in på en webbapplikation med SITHS-kortet: **kort i läsaren = inloggad, kort ur = utloggad**.

Projektet består av tre delar:
- ett litet Windows-program som läser kortet,
- ett tillägg till Chrome/Edge,
- ett serverprotokoll med en färdig exempelimplementation i PHP.

Inloggningen kräver PIN och en kryptografisk signatur från kortet, så det räcker inte att visa upp certifikatet. När kortet tas ur loggas användaren ut inom någon sekund.

```
SITHS-kort ─PC/SC─> SithsWebLogin.exe ─native messaging─> webbläsartillägg ─HTTPS─> er webbapplikation
```

| Mapp | Innehåll |
|---|---|
| [`native-host/`](native-host/) | `SithsWebLogin.exe`: läser kortet via PC/SC och signerar med kortets nyckel via Windows (Net iD / SITHS eID). |
| [`extension/`](extension/) | Webbläsartillägget (Manifest V3). |
| [`example-server-php/`](example-server-php/) | Exempelserver i PHP som kan kopplas in i en befintlig applikation, se [INSTALL.md](example-server-php/INSTALL.md). |
| [`deployments/`](deployments/) | En JSON-fil per organisation med domäner, namn och tilläggs-id. |
| [`tools/`](tools/) | `package.ps1` bygger tillägg och installationszip för en organisation. `make-icons.ps1` och `make-screenshot.ps1` skapar ikoner och butiksbilder. |
| [`docs/PROTOCOL.md`](docs/PROTOCOL.md) | Protokollet (på engelska), för den som vill skriva servern i ett annat språk. |

## Hur det fungerar
1. Kortet sätts i, och programmet meddelar tillägget.
2. Tillägget hämtar en engångsutmaning från sajten. Programmet signerar `siths-web-login|<domän>|<utmaning>` med kortets HSA-id-certifikat, och Net iD/SITHS eID frågar efter PIN.
3. Servern kontrollerar flera saker:
   - att certifikatkedjan går till SITHS rot-CA,
   - att certifikatet är giltigt och inte spärrat (OCSP),
   - att signaturen stämmer.

   Sedan slår servern upp användaren på HSA-id och lämnar en token till tillägget.
4. Sajtens flikar loggas in med en vanlig sessions-cookie som är knuten till kortsessionen.
5. Tillägget skickar ett livstecken var 30:e sekund. Tas kortet ur loggas alla flikar ut direkt. Kraschar webbläsaren eller programmet loggar servern ut efter 90 sekunder.

Efter avbruten PIN, misslyckad inloggning eller utloggning från sajten pausas den automatiska inloggningen. Den återupptas först när användaren klickar *Försök igen* eller tar ur och sätter i kortet igen. Därför fungerar lösenordsinloggning även när kortet sitter i läsaren.

Detaljer finns i [docs/PROTOCOL.md](docs/PROTOCOL.md). Säkerhetsmodellen beskrivs i [SECURITY.md](SECURITY.md).

## Krav
- **Klient**: Windows 10/11 med .NET Framework 4.8 (ingår i Windows) och Chrome eller Edge. Dessutom krävs Net iD eller SITHS eID-programvaran, så att kortets certifikat finns i Windows certifikatlager.
- **Server**: valfri. Exempelservern kräver PHP 7.4+ med `openssl` och `pdo`.

## Kom igång (utveckling)

```bash
native-host\build.cmd
```
Bygget använder `csc.exe` från .NET Framework, så inget SDK behövs. Resultatet blir `native-host\bin\SithsWebLogin.exe`.

Testa kortläsning och signering utan att installera något:
```bash
powershell -ExecutionPolicy Bypass -File native-host\selftest.ps1
```

Kör hela kedjan lokalt mot exempelservern på `http://localhost:8080`:
1. Kopiera `native-host\siths-host.example.json` till `native-host\bin\siths-host.json` och dubbelklicka på `native-host\bin\SithsWebLogin.exe`. Programmet installeras för den aktuella användaren, utan admin.
2. Öppna `chrome://extensions`, slå på **Utvecklarläge**, välj **Läs in okomprimerat** och välj mappen `extension/`. Id:t blir `dgfnlehhckaoeoiaipbbaibicbheadia` tack vare utvecklingsnyckeln i manifestet.
3. Starta exempelservern med `domain_pattern` satt till `'/^localhost:8080$/'` och `revocation` satt till `'off'` (se [INSTALL.md](example-server-php/INSTALL.md)).

## Driftsätta för en organisation

1. **Deployment-fil.** Kopiera [`deployments/example.json`](deployments/example.json) till `deployments/<org>.json` och fyll i:

   | Fält | Betydelse |
   |---|---|
   | `displayName` | Namn som visas i installationsrutan, under Appar och i popupen. |
   | `hostName` | Unikt namn för kortprogrammet, till exempel `se.foretaget.siths`. Gör att flera organisationer kan ha var sin installation på samma dator. |
   | `extension.name`, `extension.version` | Tilläggets namn och version. |
   | `extension.ids` | Tilläggs-id som får prata med kortprogrammet: butikernas id och eventuellt utvecklings-id. |
   | `extension.store.chrome`, `extension.store.edge` | Id i Chrome Web Store och Edge Add-ons. Om de är satta skriver kortprogrammet en nyckel i registret vid installationen, så att webbläsaren hämtar tillägget och frågar användaren om det ska aktiveras. Vid avinstallation tas tillägget bort igen. |
   | `extension.key` | Publik nyckel som ger ett fast id i utvecklingsbyggen. Tas bort automatiskt ur zip-filen för butiken. |
   | `sites.matchPatterns` | Chromes match patterns för sajterna, till exempel `https://*.foretaget.se/*`. |
   | `sites.originPattern` | Samma sajter som reguljärt uttryck för origin, till exempel `^https://[a-z0-9-]+\.foretaget\.se$`. |
   | `api.base`, `api.suffix` | Var serverns endpoints ligger. Standard är `/api/siths/<namn>.php`. |
   | `downloadUrl` | Var användarna hämtar zip-filen med kortprogrammet. Visas i popupen när programmet saknas. En sökväg som börjar med `/` gäller den sajt användaren senast var på. |

   Deployment-filer för riktiga organisationer ignoreras av git. Bara mallen finns i repot.

2. **Paketera.**
   ```bash
   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Deployment deployments\<org>.json -Exe <signerad SithsWebLogin.exe>
   ```
   Skriptet ger `dist\<hostName>\` med:
   - `extension\`: läses in okomprimerat vid test.
   - `extension.zip`: laddas upp till Chrome Web Store och Edge Add-ons, gärna som *olistad*. Se [docs/CHROME_WEB_STORE.md](docs/CHROME_WEB_STORE.md).
   - `SithsWebLogin-<hostName>.zip`: exe-filen och `siths-host.json`. Användarna packar upp och dubbelklickar.

3. **Servern.** Implementera protokollet eller koppla in exempelservern enligt [INSTALL.md](example-server-php/INSTALL.md). `domain_pattern` på servern ska motsvara `sites.originPattern`.

4. **Användare.** Varje användare behöver sitt HSA-id registrerat i applikationen.

Avinstallation görs via *Inställningar → Appar* eller med `SithsWebLogin.exe --uninstall`.

**Inkognitoläge och InPrivate:** webbläsarna stänger av tillägg där tills användaren slår på *Tillåt i inkognitoläge* (Chrome) eller *Tillåt i InPrivate* (Edge) under tilläggets information. Popupen visar hur när det är avstängt.

## Signering av exe-filen

Windows 11 med Smart App Control, och datorer med AppLocker eller App Control for Business, kan blockera osignerade exe-filer. Därför är exe-filen identisk för alla organisationer: den kan signeras en gång, och allt organisationsspecifikt ligger i `siths-host.json`.

- **Officiella releaser** byggs av [GitHub Actions](.github/workflows/build.yml). Planen är att signera dem via SignPath Foundations kostnadsfria signering för öppen källkod.
- **Egna byggen** kan signeras med eget kodsigneringscertifikat:
  ```bash
  signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a native-host\bin\SithsWebLogin.exe
  ```

## Licens

[MIT](LICENSE)
