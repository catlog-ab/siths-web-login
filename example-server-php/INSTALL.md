# Installera exempelservern (PHP)

Den här instruktionen riktar sig till den (person eller AI-agent) som ska koppla in SITHS Web Login i en befintlig PHP-applikation. Gå igenom stegen i ordning och kör kontrollen i varje steg innan du går vidare. Avbryt och rapportera i stället för att gissa om något inte stämmer.

Protokollet beskrivs i [../docs/PROTOCOL.md](../docs/PROTOCOL.md). Använder applikationen ett annat språk än PHP kan servern implementeras efter den, med den här koden som förlaga.

## Vad paketet gör

Webbläsartillägget anropar sju endpoints under `/api/siths/`:

| Endpoint | Anropas av | Gör |
|---|---|---|
| `challenge.php` | tillägget | Skapar en engångsutmaning (nonce) bunden till sajtens domän. |
| `login.php` | tillägget | Verifierar kortets certifikat och signatur. Slår upp användaren på HSA-id och returnerar en `cardToken`. |
| `heartbeat.php` | tillägget, var 30:e sekund | Håller kortsessionen vid liv. |
| `logout.php` | tillägget, när kortet tas ur | Avslutar kortsessionen. |
| `code.php` | tillägget | Ger en engångskod som sajtens flik växlar in. |
| `session.php` | sajtens flik (samma origin, med cookie) | Loggar in flikens vanliga PHP-session och knyter den till kortsessionen. |
| `branding.php` | tillägget | Sajtens namn och logga till tilläggets popup. |

Därtill finns `siths_guard()`, som ska köras på **varje sidladdning**. Den loggar ut sessioner vars kort har tagits ur eller vars tillägg inte hörts av på 90 sekunder.

Har applikationen flera kunder på egna underdomäner (`kund1.example.org`, `kund2.example.org`) identifieras kunden av värdnamnet. Signatur, kortsession och engångskod är alla bundna till domänen.

```
example-server-php/
├── INSTALL.md            ← den här filen
├── config.example.php    → kopieras till config.php
├── adapter.php           ← MÅSTE anpassas till applikationen (steg 5)
├── api/siths/*.php       ← ska nås publikt på https://<sajt>/api/siths/
├── lib/                  ← ej publik
├── sql/001_siths.sql     ← databastabeller
└── certs/                ← ej publik; ev. CA-bundle
```

## Steg 0 – Förutsättningar

Kontrollera på servern:

```bash
php -v                                   # 7.4 eller senare
php -m | grep -Ei '^(openssl|pdo|pdo_mysql|json)$'
openssl version                          # behövs för OCSP-kontroll
curl -sI http://ocsp1.siths.se | head -1 # servern måste nå Ineras OCSP (utgående HTTP, port 80)
```

Ta också reda på följande. Allt behövs i senare steg:
- **Webbrot och webbserver** (Apache/nginx). Delar flera sajter eller kunder samma kodbas och vhost?
- **Databas**: DSN, användare och lösenord. Finns i applikationens befintliga konfiguration.
- **Användare och kunder**: vilka tabeller och kolumner applikationen använder för användare och, om det finns, kunder och deras domäner.
- **Inloggning**: hur applikationen startar sessionen (eget `session_name`, egen session handler, ramverk) och vilka sessionsvariabler som betyder "inloggad som användare X".
- **Gemensam fil**: var sessionen startas. Det är där `siths_guard()` ska in.

## Steg 1 – Placera filerna

Lägg paketet så att `api/siths/` hamnar under webbroten som `/api/siths/`. Övriga filer ska ligga **relativt** som i paketet, eftersom endpoints inkluderar `../../lib/bootstrap.php`. Lägg helst `lib/`, `certs/`, `sql/`, `config.php` och `adapter.php` utanför webbroten. Går det inte måste de spärras:

- **Apache**: de medföljande `.htaccess`-filerna (`Require all denied`) räcker om `AllowOverride` tillåter det.
- **nginx**: lägg till motsvarande, till exempel
  ```nginx
  location ~ ^/(lib|certs|sql)/ { deny all; }
  location ~ ^/(config|adapter)\.php$ { deny all; }
  ```

En annan sökväg än `/api/siths/` går bra. Sätt i så fall `api.base` i organisationens deployment-fil, så att tillägget hittar den.

Om sökvägen krockar med en befintlig router (till exempel en front controller som fångar alla URL:er), se till att de riktiga `.php`-filerna serveras direkt.

**Kontroll:**
```bash
curl -s -o /dev/null -w '%{http_code}\n' https://app.example.org/lib/bootstrap.php   # 403 eller 404
```

## Steg 2 – Konfiguration

```bash
cp config.example.php config.php
```
Fyll i:
- `domain_pattern`: reguljärt uttryck för de värdnamn som får använda SITHS-inloggning. Det måste motsvara `sites.originPattern` i deployment-filen.
- `db_dsn`, `db_user`, `db_pass`: samma databas som applikationen.
- `ca_bundle`: sökväg till SITHS CA-bundle (steg 4).
- `revocation`: ska vara `'ocsp'` i drift. `'off'` får bara användas vid felsökning.
- `openssl_bin`: fullständig sökväg om `openssl` inte finns i PHP-processens PATH.

Se till att `config.php` inte hamnar i versionshanteringen, eftersom den innehåller databaslösenordet.

## Steg 3 – Databas

Kör `sql/001_siths.sql`. Den skapar `siths_challenges`, `siths_card_sessions` och `siths_login_codes`.

Användarna behöver också ett HSA-id. Lägg till en kolumn i applikationens **befintliga** användartabell. Anpassa namnen, det här är bara ett exempel:
```sql
ALTER TABLE users ADD COLUMN hsa_id VARCHAR(64) NULL;
CREATE UNIQUE INDEX users_tenant_hsa ON users (tenant_id, hsa_id);   -- eller bara (hsa_id) om det inte finns kunder
```
HSA-id har formatet `SE1234567890-AB12`. Det fylls i per användare, till exempel via applikationens admin. Utan HSA-id kan användaren inte logga in med kort (felet `no_account`).

**Kontroll:** `SHOW TABLES LIKE 'siths_%';` ska visa 3 tabeller.

## Steg 4 – SITHS CA-certifikat

Servern måste lita på den CA som utfärdar kortens HSA-id-certifikat. I dag är det *SITHS e-id Person HSA-id 3 CA v1*, och dessutom SITHS rot-CA ovanför den. Båda ska ligga i **en PEM-fil**.

Om Ineras certifikat redan finns på servern, peka `ca_bundle` på den filen. Kontrollera den så här:
```bash
grep -c 'BEGIN CERTIFICATE' /sökväg/till/bundle.pem
openssl crl2pkcs7 -nocrl -certfile /sökväg/till/bundle.pem | openssl pkcs7 -print_certs -noout | grep subject
```
Utdata ska innehålla både `SITHS e-id Person HSA-id 3 CA v1` och rot-CA:n. Saknas den utfärdande CA:n, hämta den och lägg till den:
```bash
curl -s http://aia.siths.se/sithseidpersonhsaid3cav1.cer | openssl x509 -inform DER >> /sökväg/till/bundle.pem
```
Hämta rot-CA:n från Ineras webbplats och jämför fingeravtrycket mot det Inera publicerar. Se även `certs/README.md`.

**Kontroll med ett riktigt kortcertifikat** (`kort.pem`, publik del av ett SITHS-kort):
```bash
openssl verify -purpose sslclient -CAfile /sökväg/till/bundle.pem kort.pem   # ska ge: kort.pem: OK
```

## Steg 5 – Anpassa `adapter.php` (viktigast)

Filen innehåller fem funktioner. Exemplen i filen förutsätter tabellerna `tenants(id, domain, name, logo_path)` och `users(id, tenant_id, name, hsa_id)` samt `$_SESSION['user_id']`. **Det är exempel, inte applikationens riktiga schema.** Läs applikationens befintliga inloggningskod och skriv om varje funktion så att den gör exakt det som den vanliga inloggningen gör.

| Funktion | Ska göra |
|---|---|
| `app_find_user_by_hsa_id($hsaId, $domain)` | Returnera `['id' => …, 'name' => …]` för användaren med detta HSA-id **på sajten/hos kunden `$domain`**, annars `null`. Ta hänsyn till inaktiverade och spärrade konton på samma sätt som vanlig inloggning. |
| `app_tenant_branding($domain)` | Returnera `['name' => namn, 'logo' => URL eller sökväg]`. Sökvägar som börjar med `/` görs absoluta automatiskt. |
| `app_start_session()` | Starta sessionen **exakt som resten av applikationen** gör: samma `session_name`, cookie-parametrar och session handler. Annars hamnar inloggningen i fel session. Om applikationen har en egen bootstrap, inkludera den i stället. |
| `app_login_user($user)` | Gör samma som applikationens vanliga inloggning gör efter godkänt lösenord: sätt sessionsvariabler, behörigheter, inloggningslogg och liknande. Anropas efter `session_regenerate_id(true)`. |
| `app_logout_user()` | Gör samma som applikationens vanliga utloggning. |

`siths_store_pdo()` får gärna bytas mot applikationens befintliga databasanslutning.

Ändra **inte** filerna i `lib/` eller `api/` för att anpassa till applikationen. All applikationsspecifik kod hör hemma i adaptern, så att paketet kan uppdateras utan att ändringarna går förlorade.

## Steg 6 – Koppla in `siths_guard()`

Lägg in följande i den fil som körs på **varje** sidladdning, direkt efter att sessionen startats och före all utskrift och behörighetskontroll:
```php
require_once '/sökväg/till/example-server-php/lib/siths_guard.php';
siths_guard();
```
`siths_guard()` gör ingenting för sessioner som inte loggats in med kort. För kortsessioner som avslutats loggar den ut via `app_logout_user()`, och applikationens vanliga kontroll skickar då användaren till inloggningssidan. Den ska **inte** köras i `/api/siths/*`.

## Steg 6b – Inloggningssidan (valfritt men rekommenderat)

**Ingen knapp och inget eget anrop behövs.** `login.php` anropas aldrig av sidan. Tillägget sköter hela inloggningen när kortet sitter i och laddar sedan om sidan, som då är inloggad via den vanliga sessions-cookien. Sidan behöver bara visa rätt instruktion beroende på läget. Kontraktet är fast och ingår i tillägget.

**1. Attribut på `<html>`**, satta av tillägget innan sidans egna skript körs:

| Attribut | Värde |
|---|---|
| `data-siths-web-login` | Tilläggets version, t.ex. `1.0.0`. Saknas attributet är tillägget inte installerat. |
| `data-siths-web-login-status` | Aktuellt läge (se nedan). Uppdateras löpande. |

Lägen:

| Läge | Betyder | Lämplig text |
|---|---|---|
| `no_host` | Kortprogrammet saknas | "Installera SITHS-programmet" + nedladdningslänk |
| `no_card` | Inget kort i läsaren | "Sätt i ditt SITHS-kort" |
| `logging_in` | PIN-dialog visas | "Ange din PIN-kod" |
| `logged_in` | Inloggad | (sidan laddas om automatiskt) |
| `error` | Misslyckades eller pausad, se `error` nedan | Text efter felkod + knappen *Försök igen* |
| `no_site` | Visas inte på sajtens sidor | – |

**Paus i stället för upprepade försök.** Efter läget `error` försöker tillägget **inte** logga in automatiskt igen, varken vid sidladdning eller flikbyte. Det gör det först när användaren trycker *Försök igen* (meddelandet `retry`) eller tar ur och sätter i kortet. Lösenordsinloggning fungerar därför även när kortet sitter i. Exempel på `error`:
- `pin_cancelled`: användaren avbröt PIN-dialogen.
- `session_ended`: servern avslutade kortsessionen, t.ex. via *Logga ut* eller efter 90 sekunder utan livstecken. Tillägget märker det genom att heartbeat får 401.
- Serverns felkoder från `login`, t.ex. `no_account`.

`session.php` får svara `{"changed": false}` med en giltig kod om fliken redan är inloggad på annat sätt, till exempel med lösenord. Fliken gör då ingenting. Svarar `session.php` 401, till exempel för att sajtens inloggning gått ut av inaktivitet och kortsessionen avslutats, pausar tillägget med `session_ended` utan att ladda om sidan.

`retry` startar alltid en ny inloggning med PIN om kortet sitter i, även när servern redan har avslutat kortsessionen. En sida med knappen *"Logga in igen med SITHS-kort"* kan alltså skicka `retry` direkt när den laddas.

**Inkognitoläge och InPrivate.** Webbläsaren stänger av tillägg där tills användaren slår på *Tillåt i inkognitoläge* (Chrome) eller *Tillåt i InPrivate* (Edge) under tilläggets information. Det går inte att slå på via installationsprogram eller policy. Utan det saknas `data-siths-web-login`, precis som när tillägget inte är installerat. Hjälptexten för saknat tillägg bör därför också nämna detta, till exempel: *"Har du tillägget men använder ett inkognitofönster? Öppna chrome://extensions → Information → Tillåt i inkognitoläge."* Det är särskilt viktigt på delade datorer. När det är påslaget delar inkognitofönster och vanliga fönster kortinloggning, och när kortet tas ur loggas alla ut.

Ren CSS räcker:
```css
.siths-hint { display: none; }
html:not([data-siths-web-login]) .siths-hint.no-extension,
html[data-siths-web-login-status="no_host"] .siths-hint.no-host,
html[data-siths-web-login-status="no_card"] .siths-hint.no-card,
html[data-siths-web-login-status="logging_in"] .siths-hint.pin,
html[data-siths-web-login-status="error"] .siths-hint.error { display: block; }
```

**2. Meddelanden** (för den som vill ha felkod eller en knapp):
```js
// Tillägget -> sidan, vid varje lägesändring:
window.addEventListener("message", (e) => {
  if (e.source !== window || e.data?.source !== "siths-web-login" || e.data.type !== "siths-state") return;
  const { status, error, user } = e.data.state;   // error t.ex. "no_account", "pin_cancelled"; user = { name }
});
// Sidan -> tillägget:
window.postMessage({ source: "siths-web-login-page", type: "get_state" }, location.origin); // be om läget nu
window.postMessage({ source: "siths-web-login-page", type: "retry" }, location.origin);     // knappen "Försök igen"
```

Se till att sajtens Content-Security-Policy inte stoppar `fetch` till `/api/siths/session.php` från sidan, eftersom tillägget gör det anropet i sidans namn. Samma origin är normalt tillåtet.

## Steg 7 – Prov

Ersätt `app.example.org` med en domän som matchar `domain_pattern`.

```bash
# Utmaning: ska ge {"nonce":"…","domain":"app.example.org"}
curl -s -X POST https://app.example.org/api/siths/challenge.php

# Ogiltig token: ska ge 401 {"error":"invalid_token"}
curl -s -X POST -H 'Authorization: Bearer xxxxxxxxxxxxxxxxxxxxxxxxxx' https://app.example.org/api/siths/heartbeat.php

# Felaktig inloggning: ska ge 400 invalid_challenge
curl -s -X POST -d '{"nonce":"fel"}' https://app.example.org/api/siths/login.php

# Branding: ska ge sajtens namn och logga
curl -s -X POST https://app.example.org/api/siths/branding.php

# GET ska nekas: 405
curl -s -o /dev/null -w '%{http_code}\n' https://app.example.org/api/siths/challenge.php
```
Kontrollera PHP:s fellogg efter proven. Ingen rad får innehålla `config.php saknas` eller `CA-bundle saknas`.

Fullt prov med kort görs från en Windows-dator med kortläsare, kortprogrammet och tillägget installerade för organisationen (se huvud-README):
1. Lägg in testanvändarens HSA-id på hens konto.
2. Öppna sajten och sätt i kortet. PIN-dialogen ska visas, och efter rätt PIN laddas sidan om som inloggad.
3. Ta ur kortet. Sidan ska laddas om som utloggad inom någon sekund.
4. Sätt i kortet och stäng webbläsaren helt. Efter cirka 90 sekunder ska kortsessionen vara avslutad (se `last_heartbeat` i `siths_card_sessions`).

## Felsökning

`login.php` returnerar felkoder som också loggas med `error_log`:

| Fel | Orsak |
|---|---|
| `invalid_challenge` | Noncen är redan använd, äldre än 2 minuter, eller hör till en annan domän. |
| `wrong_issuer` | Certifikatet är inte utfärdat av SITHS HSA-id-CA. Justera `issuer_cn_pattern` om Inera byter CA-namn. |
| `untrusted_certificate` | Kedjan går inte att verifiera. CA-bundlen saknar utfärdande CA eller rot (steg 4). |
| `bad_signature` | Signaturen stämmer inte. Ofta har tillägget och servern olika uppfattning om domänen, t.ex. proxy som skriver om `Host`. |
| `ocsp_unavailable` | Servern når inte `ocsp1.siths.se`, `openssl` saknas eller `shell_exec` är avstängd. |
| `certificate_revoked` | Kortet är spärrat. |
| `missing_hsa_id` | Certifikatet saknar HSA-id i `serialNumber`. |
| `no_account` | Inget konto med det HSA-id:t på sajten (steg 3 och 5). |
| `unknown_domain` | Värdnamnet matchar inte `domain_pattern`. Bakom proxy: se till att `HTTP_HOST` är sajtens domän. |

## Säkerhet: gör inte detta
- Sätt inte `revocation` till `'off'` i drift.
- Lägg inte till CORS-headers på `/api/siths/`. Tillägget behöver dem inte, eftersom det har host-behörighet.
- Logga inte `cardToken`, inloggningskoder eller signaturer.
- Lita inte på HSA-id från något annat håll än `login.php`. HSA-id i ett certifikat är publikt och bevisar ingenting utan signaturen.

## Avinstallation
Ta bort anropet till `siths_guard()`, katalogen `/api/siths/` och tabellerna `siths_challenges`, `siths_card_sessions` och `siths_login_codes`. Kolumnen `hsa_id` kan ligga kvar.
