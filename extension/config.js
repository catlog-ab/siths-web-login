// Organisationens inställningar. Den här filen skrivs över av tools/package.ps1 utifrån
// en fil i deployments/. Värdena nedan används vid utveckling mot exempelservern på localhost.
export const CONFIG = {
  // Namn som visas i popupen.
  displayName: "SITHS Web Login (utveckling)",
  // Native messaging-namnet; samma som hostName i siths-host.json.
  hostName: "org.example.siths",
  // Origins (protokoll + värd + ev. port) som tillägget loggar in mot, som reguljärt uttryck.
  // Måste täckas av host_permissions/content_scripts i manifest.json.
  originPattern: "^http://localhost:8080$",
  // Var serverns endpoints ligger på varje sajt: apiBase + namn + apiSuffix,
  // t.ex. /api/siths/challenge.php. Se docs/PROTOCOL.md.
  apiBase: "/api/siths/",
  apiSuffix: ".php",
  heartbeatSeconds: 30,
  // Länk i popupen när kortprogrammet saknas (null = ingen länk).
  downloadUrl: null,
};

const originRe = new RegExp(CONFIG.originPattern);

export function isSiteOrigin(origin) {
  return typeof origin === "string" && originRe.test(origin);
}
