// Bridge to MainWindow.xaml.cs's OnMessage/PostAsync. Every action is read-only — this app
// never writes a log entry, only queries them.
(function () {
  "use strict";

  const LEVEL_NAMES = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

  let currentSource = null; // { kind: "folder"|"sqliteFile"|"sqlServer", path: string }

  function post(action, payload) {
    window.chrome.webview.postMessage(Object.assign({ action }, payload || {}));
  }

  function setSource(kind, path) {
    currentSource = { kind, path };
    document.getElementById("sourceLabel").textContent = `${kind}: ${path}`;
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
      document.getElementById("statusLabel").textContent = "Choose a source first.";
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
    onSettings(settings) {
      if (settings.lastSourceKind === "Folder" && settings.lastFolderPath) setSource("folder", settings.lastFolderPath);
      else if (settings.lastSourceKind === "SqliteFile" && settings.lastSqliteFilePath) setSource("sqliteFile", settings.lastSqliteFilePath);
    },
    onSourceChosen(src) { setSource(src.kind, src.path); },
    onResults(entries) { renderRows(entries); },
    onError(err) { document.getElementById("statusLabel").textContent = `Error: ${err.message}`; },
  };

  document.getElementById("btnBrowseFolder").addEventListener("click", () => post("browseFolder"));
  document.getElementById("btnBrowseFile").addEventListener("click", () => post("browseFile"));
  document.getElementById("btnConnectSqlServer").addEventListener("click", () => {
    const connectionString = document.getElementById("sqlServerInput").value.trim();
    if (connectionString) setSource("sqlServer", connectionString);
  });
  document.getElementById("btnQuery").addEventListener("click", runQuery);
  document.getElementById("btnCloseDetail").addEventListener("click", () => document.getElementById("detailPane").classList.add("hidden"));

  post("ready");
})();
