import { CONFIG, isSiteOrigin } from "./config.js";

// Varje sajt (origin, t.ex. https://kund1.example.org) får en egen kortsession och token.
// Tillstånd: "no_host" | "no_card" | "no_site" | "logging_in" | "logged_in" | "error"
let state = { status: "no_host", error: null, user: null, origin: null };
let port = null;
let cardPresent = false;
let nextId = 1;
const pending = new Map();
const loginsInFlight = new Map();

// ---------- Tillstånd ----------

function setState(patch) {
  state = { ...state, ...patch };
  chrome.storage.session.set({ uiState: state }).catch(() => {});
  const badge = { no_host: "!", no_card: "", no_site: "", logging_in: "…", logged_in: "✓", error: "!" }[state.status];
  chrome.action.setBadgeText({ text: badge || "" });
  chrome.action.setBadgeBackgroundColor({ color: state.status === "logged_in" ? "#1a7f37" : "#b42318" });
  broadcast({ type: "siths_state", state: publicState() });
}

function publicState() {
  return {
    status: state.status, error: state.error, origin: state.origin,
    user: state.user ? { name: state.user.name } : null,
  };
}

async function getTokens() {
  const { tokens } = await chrome.storage.session.get("tokens");
  return tokens || {};
}

async function setToken(origin, token) {
  const tokens = await getTokens();
  if (token) tokens[origin] = token;
  else delete tokens[origin];
  await chrome.storage.session.set({ tokens });
}

// Pausad automatisk inloggning: origin (eller "*" för alla) -> felkod. Sätts när användaren
// avbrutit PIN, inloggningen misslyckats eller sajten loggat ut kortsessionen, så att PIN-dialogen
// inte dyker upp igen vid varje sidladdning. Hävs med "Försök igen" eller när kortet tas ur.
async function getPaused() {
  const { paused } = await chrome.storage.session.get("paused");
  return paused || {};
}

async function pause(origin, reason) {
  const paused = await getPaused();
  paused[origin] = reason;
  await chrome.storage.session.set({ paused });
}

async function clearPaused() {
  await chrome.storage.session.remove("paused");
}

async function pausedReason(origin) {
  const paused = await getPaused();
  return paused["*"] || paused[origin] || null;
}

async function getLastOrigin() {
  const { lastOrigin } = await chrome.storage.local.get("lastOrigin");
  return lastOrigin && isSiteOrigin(lastOrigin) ? lastOrigin : null;
}

// ---------- Native host ----------

function connect() {
  if (port) return;
  try {
    port = chrome.runtime.connectNative(CONFIG.hostName);
  } catch (e) {
    port = null;
    setState({ status: "no_host", error: String(e) });
    return;
  }
  port.onMessage.addListener(onHostMessage);
  port.onDisconnect.addListener(() => {
    const err = chrome.runtime.lastError ? chrome.runtime.lastError.message : null;
    port = null;
    cardPresent = false;
    for (const p of pending.values()) p.reject(new Error("host_disconnected"));
    pending.clear();
    // Utan host vet vi inte om kortet sitter kvar: logga ut för säkerhets skull.
    logoutAll();
    setState({ status: "no_host", error: err, user: null });
  });
  hostRequest({ cmd: "status" })
    .then((r) => (r.cardPresent ? onCardInserted() : onCardRemoved()))
    .catch(() => {});
}

function hostRequest(msg, timeoutMs = 120000) {
  return new Promise((resolve, reject) => {
    if (!port) return reject(new Error("no_host"));
    const id = nextId++;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error("timeout"));
    }, timeoutMs);
    pending.set(id, {
      resolve: (v) => { clearTimeout(timer); resolve(v); },
      reject: (e) => { clearTimeout(timer); reject(e); },
    });
    port.postMessage({ ...msg, id });
  });
}

function onHostMessage(msg) {
  if (msg.type === "reply") {
    const p = pending.get(msg.id);
    if (!p) return;
    pending.delete(msg.id);
    msg.ok ? p.resolve(msg) : p.reject(new Error(msg.error || "host_error"));
  } else if (msg.type === "card_inserted") {
    onCardInserted();
  } else if (msg.type === "card_removed") {
    // Användaren har tagit ur kortet: nästa isättning är ett nytt, avsiktligt inloggningsförsök.
    cardPresent = false;
    clearPaused().then(onCardRemoved);
  }
}

// ---------- Server-API ----------

async function api(origin, path, body, token) {
  const headers = { "Content-Type": "application/json" };
  if (token) headers["Authorization"] = "Bearer " + token;
  const res = await fetch(origin + CONFIG.apiBase + path + CONFIG.apiSuffix, {
    method: "POST",
    headers,
    body: JSON.stringify(body || {}),
    credentials: "omit",
    cache: "no-store",
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    const err = new Error(data.error || "http_" + res.status);
    err.status = res.status;
    throw err;
  }
  return data;
}

// Sajtens namn och logga till popupen.
async function refreshBranding(origin) {
  try {
    const b = await api(origin, "branding");
    const { branding } = await chrome.storage.local.get("branding");
    await chrome.storage.local.set({ branding: { ...(branding || {}), [origin]: { name: b.name, logo: b.logo } } });
  } catch (e) {
    // Popupen visar då bara texten.
  }
}

// ---------- Inloggning ----------

// Sajter som användaren har öppna, med den senast använda först.
async function targetOrigins() {
  const tabs = await chrome.tabs.query({}).catch(() => []);
  const origins = new Set();
  const last = await getLastOrigin();
  if (last) origins.add(last);
  for (const t of tabs) {
    try {
      const o = new URL(t.url).origin;
      if (isSiteOrigin(o)) origins.add(o);
    } catch (e) {}
  }
  return [...origins];
}

// fresh = användaren bad uttryckligen om inloggning ("Försök igen"): en token som servern
// redan avslutat ersätts då med en ny inloggning i stället för att pausa.
async function onCardInserted(fresh = false) {
  cardPresent = true;
  const origins = await targetOrigins();
  if (origins.length === 0) {
    setState({ status: "no_site", error: null, user: null });
    return;
  }
  // En i taget: varje inloggning är en signering, och PIN-dialogerna får inte krocka.
  for (const o of origins) {
    await login(o, fresh);
    if ((await getPaused())["*"]) break; // PIN avbruten: fråga inte om nästa sajt heller
  }
}

// Sajten har avslutat kortsessionen (utloggning, inaktivitet): glöm token och logga inte in
// igen förrän användaren trycker "Försök igen" eller tar ur och sätter i kortet.
async function sessionEnded(origin) {
  await setToken(origin, null);
  await pause(origin, "session_ended");
  setState({ status: "error", error: "session_ended", user: null, origin });
}

function login(origin, fresh = false) {
  if (loginsInFlight.has(origin)) return loginsInFlight.get(origin);
  const p = (async () => {
    const reason = await pausedReason(origin);
    if (reason) {
      if (!(await getTokens())[origin]) setState({ status: "error", error: reason, user: null, origin });
      return;
    }
    // Redan inloggad med giltig token (t.ex. efter omstart av service workern)?
    const existing = (await getTokens())[origin];
    if (existing) {
      try {
        const hb = await api(origin, "heartbeat", {}, existing);
        setState({ status: "logged_in", error: null, user: hb.user, origin });
        // Vid uttrycklig begäran: låt flikarna knyta sin session (t.ex. på sajtens inloggningssida).
        if (fresh) broadcast({ type: "siths_login" }, origin);
        return;
      } catch (e) {
        await setToken(origin, null);
        if (e.status === 401 && !fresh) {
          // Sajten har avslutat kortsessionen (t.ex. "Logga ut"): logga inte in igen av sig själv.
          await sessionEnded(origin);
          return;
        }
      }
    }
    setState({ status: "logging_in", error: null, origin });
    refreshBranding(origin);
    try {
      const { nonce, domain } = await api(origin, "challenge");
      if (domain !== new URL(origin).host) throw new Error("domain_mismatch");
      const signed = await hostRequest({ cmd: "sign", domain, nonce });
      const res = await api(origin, "login", {
        nonce,
        cert: signed.cert,
        signature: signed.signature,
        alg: signed.alg,
      });
      await setToken(origin, res.cardToken);
      setState({ status: "logged_in", error: null, user: res.user, origin });
      broadcast({ type: "siths_login" }, origin);
    } catch (e) {
      // Försök inte igen automatiskt (då skulle PIN-dialogen visas vid varje sidladdning).
      // Avbruten PIN gäller kortet, inte bara den här sajten.
      // Drogs kortet ur (eller dog kortprogrammet) under försöket ska nästa isättning få ett nytt försök.
      if (!cardPresent || ["no_card", "no_host", "host_disconnected"].includes(e.message)) return;
      await pause(e.message === "pin_cancelled" ? "*" : origin, e.message);
      setState({ status: "error", error: e.message, user: null, origin });
    }
  })().finally(() => loginsInFlight.delete(origin));
  loginsInFlight.set(origin, p);
  return p;
}

async function onCardRemoved() {
  cardPresent = false;
  await logoutAll();
  setState({ status: port ? "no_card" : "no_host", user: null, error: null });
}

async function logoutAll() {
  const tokens = await getTokens();
  await chrome.storage.session.set({ tokens: {} });
  for (const [origin, token] of Object.entries(tokens)) {
    // Servern loggar ändå ut när heartbeats uteblir, så fel här kan ignoreras.
    api(origin, "logout", {}, token).catch(() => {}).finally(() => broadcast({ type: "siths_logout" }, origin));
  }
}

async function heartbeat() {
  const tokens = await getTokens();
  for (const [origin, token] of Object.entries(tokens)) {
    try {
      await api(origin, "heartbeat", {}, token);
    } catch (e) {
      if (e.status === 401) {
        // Sajten har avslutat kortsessionen (manuell utloggning eller timeout).
        await sessionEnded(origin);
        broadcast({ type: "siths_logout" }, origin);
      }
    }
  }
}

// ---------- Flikar ----------

async function broadcast(msg, onlyOrigin) {
  const tabs = await chrome.tabs.query({}).catch(() => []);
  for (const t of tabs) {
    let o;
    try { o = new URL(t.url).origin; } catch (e) { continue; }
    if (!isSiteOrigin(o) || (onlyOrigin && o !== onlyOrigin)) continue;
    chrome.tabs.sendMessage(t.id, msg).catch(() => {});
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  const origin = sender.origin;
  const fromSite = sender.tab && isSiteOrigin(origin);

  if (msg.type === "page_opened" && fromSite) {
    // En sida på en av sajterna öppnades: kom ihåg den och logga in där om kortet sitter i.
    chrome.storage.local.set({ lastOrigin: origin });
    connect();
    (async () => {
      if (cardPresent && !(await getTokens())[origin]) await login(origin); // gör inget om pausad
      else if (!cardPresent && port) setState({ status: "no_card", origin });
      sendResponse(publicState());
    })();
    return true;
  }
  if (msg.type === "get_state") {
    connect();
    sendResponse(publicState());
    return false;
  }
  if (msg.type === "get_login_code" && fromSite) {
    // Engångskod som fliken växlar in mot en vanlig inloggad session (cookie) på sajten.
    (async () => {
      const token = (await getTokens())[origin];
      if (!token) return sendResponse({ code: null });
      try {
        const r = await api(origin, "code", {}, token);
        sendResponse({ code: r.code, sessionUrl: CONFIG.apiBase + "session" + CONFIG.apiSuffix });
      } catch (e) {
        if (e.status === 401) await sessionEnded(origin);
        sendResponse({ code: null });
      }
    })();
    return true;
  }
  if (msg.type === "session_rejected" && fromSite) {
    // session.php svarade 401 (t.ex. sajtens inloggning gick ut av inaktivitet och servern
    // avslutade kortsessionen). Samma som manuell utloggning: vänta på "Försök igen".
    sessionEnded(origin).then(() => sendResponse(publicState()));
    return true;
  }
  if (msg.type === "retry") {
    // Uttrycklig begäran (knappen "Försök igen" eller "Logga in igen med SITHS-kort"):
    // häv pauser och gör en ny inloggning med PIN, även om servern redan avslutat den gamla.
    if (fromSite) chrome.storage.local.set({ lastOrigin: origin });
    connect();
    clearPaused()
      .then(() => hostRequest({ cmd: "status" }))
      .then((r) => (r.cardPresent ? onCardInserted(true) : onCardRemoved()))
      .catch(() => {});
    return false;
  }
});

// ---------- Start ----------

chrome.alarms.create("siths-heartbeat", { periodInMinutes: CONFIG.heartbeatSeconds / 60 });
chrome.alarms.onAlarm.addListener((a) => {
  if (a.name !== "siths-heartbeat") return;
  connect(); // Håller också liv i service workern och porten.
  heartbeat();
});
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
connect();
