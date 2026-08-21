// node:sqlite, not better-sqlite3 — Electron 43 bundles Node 24, which ships a stable built-in
// SQLite module. A native npm addon (better-sqlite3) needs compiling against Electron's specific
// ABI via @electron/rebuild, which in turn needs a full Visual Studio C++ workload; this machine
// only has VS2022 Community without that workload installed. node:sqlite needs none of that —
// it's already inside the Node binary Electron ships. A real, deliberate trade: less ecosystem
// flexibility (node:sqlite's API is smaller than better-sqlite3's) for zero native-build
// dependency, which is the right trade for a small embedded action queue like this one.
const { DatabaseSync } = require("node:sqlite");

class OfflineQueue {
  constructor(dbPath) {
    this.db = new DatabaseSync(dbPath);
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS queued_actions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        action_type TEXT NOT NULL,
        incident_id TEXT NOT NULL,
        payload TEXT NOT NULL,
        queued_at TEXT NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 0,
        last_error TEXT
      )
    `);
  }

  enqueue(actionType, incidentId, payload) {
    this.db
      .prepare(
        "INSERT INTO queued_actions (action_type, incident_id, payload, queued_at) VALUES (?, ?, ?, ?)",
      )
      .run(actionType, incidentId, JSON.stringify(payload), new Date().toISOString());
  }

  all() {
    return this.db.prepare("SELECT * FROM queued_actions ORDER BY id").all();
  }

  count() {
    return this.db.prepare("SELECT COUNT(*) AS n FROM queued_actions").get().n;
  }

  markFailed(id, error) {
    this.db
      .prepare("UPDATE queued_actions SET attempts = attempts + 1, last_error = ? WHERE id = ?")
      .run(String(error), id);
  }

  remove(id) {
    this.db.prepare("DELETE FROM queued_actions WHERE id = ?").run(id);
  }
}

module.exports = { OfflineQueue };
