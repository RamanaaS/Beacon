const { app, BrowserWindow, Tray, Menu, Notification, ipcMain, nativeImage } = require("electron");
const path = require("node:path");
const { OfflineQueue } = require("./offlineQueue");
const { BeaconApi } = require("./api");

const API_BASE_URL = "http://localhost:5252";
const POLL_INTERVAL_MS = 15_000;
const NOTIFIABLE_SEVERITIES = new Set(["Sev1_Critical", "Sev2_High"]);

let mainWindow = null;
let tray = null;
let pollTimer = null;
const knownIncidentIds = new Set();

const api = new BeaconApi(API_BASE_URL);
let queue = null; // created after app.getPath('userData') is available

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 480,
    height: 680,
    icon: path.join(__dirname, "..", "renderer", "assets", "icon.png"),
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      // The two settings that actually matter for security here: the renderer gets no direct
      // Node access (nodeIntegration: false) and runs in a separate JS world from the preload
      // script (contextIsolation: true), so a compromised/malicious page loaded in this window
      // still can't reach fs, child_process, or the SQLite queue directly — only the specific,
      // narrow API preload.js exposes via contextBridge.
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  mainWindow.loadFile(path.join(__dirname, "..", "renderer", "index.html"));

  // Closing the window hides it instead of quitting — an on-call console that fully exits when
  // you close the window would stop notifying you of new pages, which defeats the point of it.
  mainWindow.on("close", (event) => {
    if (!app.isQuitting) {
      event.preventDefault();
      mainWindow.hide();
    }
  });
}

function createTray() {
  const icon = nativeImage.createFromPath(path.join(__dirname, "..", "renderer", "assets", "icon.png"));
  tray = new Tray(icon.resize({ width: 16, height: 16 }));
  tray.setToolTip("Beacon On-Call Console");
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: "Open", click: () => mainWindow.show() },
      { label: "Quit", click: () => { app.isQuitting = true; app.quit(); } },
    ]),
  );
  tray.on("click", () => mainWindow.show());
}

function notifyNewIncident(incident) {
  new Notification({
    title: `${incident.severity.replace("_", " ")}: ${incident.title}`,
    body: "New incident declared — click to open Beacon.",
    urgency: "critical",
  })
    .on("click", () => mainWindow?.show())
    .show();
}

async function pollForNewIncidents() {
  if (!api.token) return;

  try {
    const incidents = await api.listOpenIncidents();
    for (const incident of incidents) {
      const alreadyKnown = knownIncidentIds.has(incident.id);
      knownIncidentIds.add(incident.id);

      if (!alreadyKnown && NOTIFIABLE_SEVERITIES.has(incident.severity)) {
        notifyNewIncident(incident);
      }
    }
    mainWindow?.webContents.send("beacon:incidents-updated", incidents);
  } catch {
    // Network hiccup or the API being down — the next poll tick tries again. Not surfaced as
    // an error to the user; a single missed poll isn't actionable, a sustained outage is what
    // the "offline" indicator in the renderer is for (driven by queue depth, not poll failures).
  }
}

async function flushOfflineQueue() {
  if (!api.token) return;

  const pending = queue.all();
  for (const action of pending) {
    try {
      const payload = JSON.parse(action.payload);
      if (action.action_type === "acknowledge") {
        await api.acknowledge(action.incident_id);
      } else if (action.action_type === "escalate") {
        await api.escalate(action.incident_id, payload.escalatedTo);
      }
      queue.remove(action.id);
    } catch (err) {
      queue.markFailed(action.id, err?.message ?? String(err));
      break; // stop at the first still-failing action — preserves order, avoids hammering a downed API
    }
  }

  mainWindow?.webContents.send("beacon:queue-depth", queue.count());
}

ipcMain.handle("beacon:login", async (_event, { email, password }) => {
  try {
    const { token, roles } = await api.login(email, password);
    api.setToken(token);
    knownIncidentIds.clear();
    await pollForNewIncidents();
    return { success: true, roles };
  } catch {
    return { success: false, error: "Invalid email or password." };
  }
});

ipcMain.handle("beacon:fetch-incidents", async () => {
  try {
    return { success: true, incidents: await api.listOpenIncidents() };
  } catch {
    return { success: false, error: "Could not reach Beacon.Api." };
  }
});

// Both actions below try the real API first and only fall back to the offline queue on failure —
// this is deliberately optimistic (assume connectivity, queue as the exception path), not
// pessimistic (always queue, sync later), because the common case on an on-call console is a
// working connection, and a UI that always shows "queued" would be misleading most of the time.
ipcMain.handle("beacon:acknowledge", async (_event, incidentId) => {
  try {
    await api.acknowledge(incidentId);
    return { success: true, queued: false };
  } catch {
    queue.enqueue("acknowledge", incidentId, {});
    mainWindow?.webContents.send("beacon:queue-depth", queue.count());
    return { success: true, queued: true };
  }
});

ipcMain.handle("beacon:escalate", async (_event, { incidentId, escalatedTo }) => {
  try {
    await api.escalate(incidentId, escalatedTo);
    return { success: true, queued: false };
  } catch {
    queue.enqueue("escalate", incidentId, { escalatedTo });
    mainWindow?.webContents.send("beacon:queue-depth", queue.count());
    return { success: true, queued: true };
  }
});

ipcMain.handle("beacon:queue-depth", () => queue.count());

app.whenReady().then(() => {
  queue = new OfflineQueue(path.join(app.getPath("userData"), "offline-queue.db"));

  createWindow();
  createTray();

  pollTimer = setInterval(() => {
    void pollForNewIncidents();
    void flushOfflineQueue();
  }, POLL_INTERVAL_MS);

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("before-quit", () => {
  app.isQuitting = true;
  if (pollTimer) clearInterval(pollTimer);
});

app.on("window-all-closed", () => {
  // Deliberately not quitting here (unlike the typical Electron template) — this is a
  // menu-bar-resident on-call console; it should keep polling and notifying from the tray even
  // with no window open, on every platform, not just macOS.
});
