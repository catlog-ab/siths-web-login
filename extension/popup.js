import { CONFIG } from "./config.js";

const TEXT = {
  no_host: ["Kortprogrammet saknas", "Installera kortprogrammet (" + CONFIG.displayName + ") och starta om webbläsaren."],
  no_card: ["Inget kort i läsaren", "Sätt i ditt SITHS-kort för att logga in."],
  no_site: ["Kortet är isatt", "Öppna sajten du vill logga in på."],
  logging_in: ["Loggar in…", "Ange din PIN-kod i dialogen."],
  logged_in: ["Inloggad", ""],
  error: ["Inloggningen misslyckades", ""],
};

const ERRORS = {
  pin_cancelled: "PIN-inmatningen avbröts. Tryck \"Försök igen\" eller ta ur och sätt i kortet för att logga in med kortet.",
  session_ended: "Du har loggats ut. Tryck \"Försök igen\" eller ta ur och sätt i kortet för att logga in med kortet igen.",
  pin_blocked: "Kortets PIN är spärrad. Kontakta din SITHS-administratör.",
  no_certificate: "Hittade inget SITHS-certifikat på kortet.",
  no_account: "Kortet är giltigt men ditt HSA-id saknar konto på den här sajten.",
  certificate_revoked: "Kortets certifikat är spärrat.",
  certificate_expired: "Kortets certifikat har gått ut.",
};

async function renderBrand(origin) {
  const brand = document.getElementById("brand");
  const { branding, lastOrigin } = await chrome.storage.local.get(["branding", "lastOrigin"]);
  const b = (branding || {})[origin || lastOrigin];
  brand.hidden = !b;
  if (!b) return;
  const logo = document.getElementById("logo");
  const name = document.getElementById("brandName");
  name.textContent = b.name || "";
  if (b.logo) {
    logo.src = b.logo;
    logo.alt = b.name || "";
    logo.hidden = false;
    name.hidden = true;
    logo.onerror = () => { logo.hidden = true; name.hidden = false; };
  } else {
    logo.hidden = true;
    name.hidden = false;
  }
}

function render(s) {
  document.title = CONFIG.displayName;
  const [title, hint] = TEXT[s.status] || [s.status, ""];
  document.getElementById("text").textContent = title;
  document.getElementById("status").className = s.status === "logged_in" ? "ok" : s.status === "error" || s.status === "no_host" ? "bad" : "";
  let detail = hint;
  if (s.status === "logged_in" && s.user) detail = s.user.name + (s.user.hsaId ? " (" + s.user.hsaId + ")" : "");
  if (s.status === "error" && s.error) detail = ERRORS[s.error] || s.error;
  document.getElementById("detail").textContent = detail;
  document.getElementById("retry").hidden = !(s.status === "error" || s.status === "no_host");
  renderDownload(s);
  renderBrand(s.origin);
}

// En relativ downloadUrl ("/system/download/…") hämtas från den sajt användaren senast var på.
async function renderDownload(s) {
  const dl = document.getElementById("download");
  let href = null;
  if (s.status === "no_host" && CONFIG.downloadUrl) {
    const { lastOrigin } = await chrome.storage.local.get("lastOrigin");
    const base = s.origin || lastOrigin;
    try {
      href = /^https?:/.test(CONFIG.downloadUrl) ? CONFIG.downloadUrl : base ? new URL(CONFIG.downloadUrl, base).href : null;
    } catch (e) {}
  }
  dl.hidden = !href;
  if (href) dl.href = href;
}

// Delade datorer används ofta i inkognitoläge, där tillägget är avstängt tills användaren tillåter det.
chrome.extension.isAllowedIncognitoAccess().then((allowed) => {
  document.getElementById("incognito").hidden = allowed;
});

chrome.runtime.sendMessage({ type: "get_state" }).then(render);
chrome.storage.session.onChanged.addListener((c) => c.uiState && render(c.uiState.newValue));
document.getElementById("retry").onclick = () => chrome.runtime.sendMessage({ type: "retry" });
