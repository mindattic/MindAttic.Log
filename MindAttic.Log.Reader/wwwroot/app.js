// Bridge to MainWindow.xaml.cs's OnMessage/PostAsync. Every action is read-only — this app
// never writes a log entry, only queries them.
(function () {
  "use strict";

  const LEVEL_NAMES = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

  let apps = []; // [{ appId, path }], from onApps — one entry per Vault Logging\<appId>\MindAttic.Log.db
  let currentSource = null; // { appId, path }

  function post(action, payload) {
    window.chrome.webview.postMessage(Object.assign({ action }, payload || {}));
  }

  function selectApp(appId, { query = true } = {}) {
    const app = apps.find((a) => a.appId === appId);
    if (!app) return;
    currentSource = app;
    document.getElementById("appSelect").value = appId;
    document.getElementById("sourceLabel").textContent = app.path;
    post("selectApp", { appId });
    if (query) runQuery();
  }

  function renderAppOptions() {
    const select = document.getElementById("appSelect");
    select.innerHTML = "";
    if (apps.length === 0) {
      const option = document.createElement("option");
      option.value = "";
      option.textContent = "(no apps found under %APPDATA%\\MindAttic\\Logging)";
      select.appendChild(option);
      return;
    }
    for (const app of apps) {
      const option = document.createElement("option");
      option.value = app.appId;
      option.textContent = app.appId;
      select.appendChild(option);
    }
  }

  function collectFilter() {
    const toIso = (id) => {
      const v = document.getElementById(id).value;
      return v ? new Date(v).toISOString() : null;
    };
    const level = document.getElementById("fLevel").value;
    return {
      sinceUtc: toIso("fSince"),
      untilUtc: toIso("fUntil"),
      minLevel: level === "" ? null : Number(level),
      application: document.getElementById("fApplication").value || null,
      category: document.getElementById("fCategory").value || null,
      searchText: document.getElementById("fSearch").value || null,
      maxResults: Number(document.getElementById("fMax").value) || 500,
    };
  }

  function runQuery() {
    if (!currentSource) {
      document.getElementById("statusLabel").textContent = "Choose an app first.";
      return;
    }
    document.getElementById("statusLabel").textContent = "Querying…";
    post("query", { source: currentSource, filter: collectFilter() });
  }

  function escapeHtml(s) {
    return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  function renderRows(entries) {
    const body = document.getElementById("gridBody");
    body.innerHTML = "";
    for (const e of entries) {
      const tr = document.createElement("tr");
      const levelName = LEVEL_NAMES[e.level] ?? e.level;
      tr.innerHTML = `
        <td class="col-time">${escapeHtml(e.timestampUtc)}</td>
        <td class="col-level level-${levelName}">${levelName}</td>
        <td class="col-app">${escapeHtml(e.application)}</td>
        <td class="col-category">${escapeHtml(e.category)}</td>
        <td class="col-message">${escapeHtml(e.message)}</td>
      `;
      tr.addEventListener("click", () => showDetail(e));
      body.appendChild(tr);
    }
    document.getElementById("statusLabel").textContent = `${entries.length} row(s)`;
  }

  function showDetail(entry) {
    const pane = document.getElementById("detailPane");
    document.getElementById("detailContent").textContent = JSON.stringify(entry, null, 2);
    pane.classList.remove("hidden");
  }

  window.mindAtticLogReader = {
    // MainWindow.OnMessage("ready") always posts onApps before onSettings (two awaited
    // PostAsync calls in that order), so `apps` is already populated by the time this runs.
    onApps(discovered) {
      apps = discovered;
      renderAppOptions();
    },
    onSettings(settings) {
      if (settings.lastAppId && apps.some((a) => a.appId === settings.lastAppId)) selectApp(settings.lastAppId, { query: false });
    },
    onResults(entries) { renderRows(entries); },
    onError(err) { document.getElementById("statusLabel").textContent = `Error: ${err.message}`; },
  };

  document.getElementById("appSelect").addEventListener("change", (e) => selectApp(e.target.value));
  document.getElementById("btnQuery").addEventListener("click", runQuery);
  document.getElementById("btnCloseDetail").addEventListener("click", () => document.getElementById("detailPane").classList.add("hidden"));

  post("ready");
})();
