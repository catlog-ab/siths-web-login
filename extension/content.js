// Körs på organisationens sajter. Kopplar flikens inloggning (sajtens cookie-session) till kortsessionen
// och laddar om sidan när kortet sätts i eller tas ur.

(function () {
  // Markera direkt (document_start) att tillägget finns, så att sidan kan visa rätt
  // instruktion utan att vänta på något meddelande. Status uppdateras löpande och kan
  // användas i CSS, t.ex. html[data-siths-web-login-status="no_card"] .kort-hint { display: block }
  const root = document.documentElement;
  root.setAttribute("data-siths-web-login", chrome.runtime.getManifest().version);

  function postToPage(state) {
    if (state && state.status) root.setAttribute("data-siths-web-login-status", state.status);
    // Sajten kan lyssna på detta för att visa t.ex. "Sätt i ditt SITHS-kort".
    window.postMessage({ source: "siths-web-login", type: "siths-state", state }, location.origin);
  }

  async function bindSession() {
    const { code, sessionUrl } = await chrome.runtime.sendMessage({ type: "get_login_code" });
    if (!code) return;
    const res = await fetch(sessionUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code }),
      credentials: "same-origin",
      cache: "no-store",
    });
    const data = await res.json().catch(() => ({}));
    // Ladda om bara om fliken faktiskt bytte inloggning, annars blir det en evig loop.
    // changed:false är inget fel (t.ex. pågående lösenordsinloggning går före kortet).
    if (res.ok && data.changed) location.reload();
    // 401: sajten har avslutat kortsessionen. Ingen omladdning och inget nytt försök.
    else if (res.status === 401) postToPage(await chrome.runtime.sendMessage({ type: "session_rejected" }));
  }

  function onState(state) {
    postToPage(state);
    if (state && state.status === "logged_in" && state.origin === location.origin) bindSession().catch(() => {});
  }

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg.type === "siths_state") postToPage(msg.state);
    if (msg.type === "siths_login") bindSession().catch(() => {});
    if (msg.type === "siths_logout") location.reload(); // Servern har redan avslutat sessionen.
  });

  chrome.runtime.sendMessage({ type: "page_opened" }).then(onState).catch(() => {});

  // Låt sajten be om aktuellt läge eller ett nytt inloggningsförsök (t.ex. knappen "Försök igen").
  window.addEventListener("message", (e) => {
    if (e.source !== window || !e.data || e.data.source !== "siths-web-login-page") return;
    if (e.data.type === "get_state") chrome.runtime.sendMessage({ type: "get_state" }).then(postToPage).catch(() => {});
    if (e.data.type === "retry") chrome.runtime.sendMessage({ type: "retry" }).catch(() => {});
  });
})();
