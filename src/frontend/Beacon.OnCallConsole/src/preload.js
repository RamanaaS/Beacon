const { contextBridge, ipcRenderer } = require("electron");

// The entire surface the renderer is allowed to touch — no fs, no child_process, no direct
// database access, nothing beyond these six calls. This is what contextIsolation + a narrow
// contextBridge API actually buys you: the renderer (which loads local HTML/JS, but the same
// principle applies to any remote content a desktop app might ever load) can only do exactly
// what's listed here, not "everything Node can do."
contextBridge.exposeInMainWorld("beacon", {
  login: (email, password) => ipcRenderer.invoke("beacon:login", { email, password }),
  fetchIncidents: () => ipcRenderer.invoke("beacon:fetch-incidents"),
  acknowledge: (incidentId) => ipcRenderer.invoke("beacon:acknowledge", incidentId),
  escalate: (incidentId, escalatedTo) => ipcRenderer.invoke("beacon:escalate", { incidentId, escalatedTo }),
  queueDepth: () => ipcRenderer.invoke("beacon:queue-depth"),
  onIncidentsUpdated: (callback) => {
    ipcRenderer.on("beacon:incidents-updated", (_event, incidents) => callback(incidents));
  },
  onQueueDepthChanged: (callback) => {
    ipcRenderer.on("beacon:queue-depth", (_event, depth) => callback(depth));
  },
});
