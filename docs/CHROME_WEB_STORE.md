# Publicera tillägget i Chrome Web Store och Edge Add-ons

Texterna nedan går att klistra in i butikernas formulär. Byt *Catlog* mot er organisation vid behov.

## Inför uppladdningen
1. Kör `tools\package.ps1 -Deployment deployments\<org>.json`. Ladda upp **`dist\<hostName>\extension.zip`**. Den saknar `key`, eftersom butiken tilldelar id.
2. Höj `extension.version` i deployment-filen inför varje ny uppladdning. Butiken godtar inte samma version två gånger.
3. Efter första uppladdningen visas tilläggets id i butikens dashboard (32 bokstäver a–p).
   - Lägg in det i deployment-filen under både `extension.ids` och `extension.store.chrome` (eller `.edge`).
   - Paketera om och lägg ut den nya `SithsWebLogin-<hostName>.zip`. Annars får butiksversionen inte prata med kortprogrammet.
   - Själva tillägget behöver inte laddas upp igen för detta.

## Butikstext

**Namn:** Catlog SITHS

**Kort beskrivning** (max 132 tecken):
> Logga in i Catlog med SITHS-kortet: kortet i läsaren loggar in, kortet ur loggar ut.

**Beskrivning:**
> Med tillägget loggar du in i Catlog med ditt SITHS-kort i stället för lösenord.
>
> • Sätt i kortet och ange din PIN – du loggas in.
> • Ta ur kortet – du loggas ut direkt, i alla flikar.
> • Fungerar på delade datorer, även i inkognitoläge om du tillåter det under tilläggets inställningar.
>
> Kräver Windows, en kortläsare, Net iD eller SITHS eID-programvaran samt Catlogs kortprogram (SithsWebLogin), som laddas ned från Catlog. Inloggningen verifieras kryptografiskt av Catlogs server; tillägget lagrar inga lösenord eller PIN-koder.
>
> Tillägget är öppen källkod (MIT): https://github.com/catlog-ab/siths-web-login

**Kategori:** Verktyg / Produktivitet  **Språk:** svenska

**Bilder:**
- Ikon 128×128: `extension/icons/128.png`. Byt gärna mot en snyggare.
- Minst en skärmbild i 1280×800 eller 640×400, till exempel popupen med "Inloggad" ovanpå Catlog.
- Liten kampanjbild 440×280. Den kan krävas av butiken.

## Fliken "Integritet" (Privacy)

**Enda syfte (single purpose):**
> Logga in användaren på organisationens webbplats med SITHS-kortet och logga ut när kortet tas ur.

**Motivering per behörighet:**

| Behörighet | Motivering |
|---|---|
| `nativeMessaging` | Kommunicerar med det lokala kortprogrammet SithsWebLogin, som läser SITHS-kortet och signerar inloggningsutmaningen med kortets nyckel (efter PIN). |
| `alarms` | Skickar ett livstecken till webbplatsen var 30:e sekund så att servern kan logga ut om kortet tas ur eller webbläsaren stängs. |
| `storage` | Sparar inloggningstoken (endast i minnet för sessionen), senast använda webbplats samt webbplatsens namn och logga för popupen. |
| Värdbehörigheter (`https://beta.catlog.se/*`, `https://trc.catlog.se/*`) | Anropar webbplatsens inloggnings-API och låter sidan visa inloggningsstatus. Tillägget körs inte på några andra webbplatser. |

**Fjärrkod:** Nej. All kod ingår i paketet.

**Dataanvändning.** Kryssa i:
- *Personligt identifierbar information*: namn och HSA-id från kortets certifikat skickas till Catlogs server för inloggning.
- *Autentiseringsinformation*: certifikat, signatur och sessionstoken.

Intyga också de tre standardpåståendena: data säljs inte, används bara för tilläggets syfte och används inte för kreditbedömning.

**Integritetspolicy (URL):** krävs, eftersom personuppgifter hanteras. Lägg en sida på catlog.se, till exempel `/integritet/siths`, med ungefär följande:
> Tillägget Catlog SITHS skickar ditt SITHS-certifikat (namn och HSA-id) och en signatur till Catlogs server när du loggar in, samt ett livstecken medan kortet sitter i. Uppgifterna används enbart för att logga in och ut dig i Catlog och behandlas enligt Catlogs personuppgiftspolicy. Tillägget lagrar inga PIN-koder eller lösenord, och inga uppgifter lämnas till tredje part.

## Fliken "Distribution"
- **Synlighet:** *Olistad* (Unlisted). Bara den som har länken kan installera, och tillägget syns inte i sökningar.
- **Regioner:** alla, eller bara Sverige.

## Granskning
Tillägg med `nativeMessaging` och värdbehörigheter granskas manuellt. Räkna med några dagar upp till ett par veckor. Om granskarna frågar, förklara att kortprogrammet är öppen källkod och länka till repot.

## Edge Add-ons
Samma `extension.zip` kan laddas upp via Microsoft Partner Center (*Edge*-programmet), som är gratis. Formuläret är snarlikt, och samma texter fungerar. Edge ger ett eget id, som läggs i `extension.ids` och `extension.store.edge`.
