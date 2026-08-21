// Plain DOM, no framework — a small enough surface (one list, one form) that React/MobX would be
// pure ceremony here. Beacon.WarRoom earns MobX because it has several interlocking stores and
// views; this console has one screen and one piece of live state.

const loginView = document.getElementById("login-view");
const dashboardView = document.getElementById("dashboard-view");
const loginForm = document.getElementById("login-form");
const loginError = document.getElementById("login-error");
const incidentList = document.getElementById("incident-list");
const emptyState = document.getElementById("empty-state");
const queueIndicator = document.getElementById("queue-indicator");
const queueCount = document.getElementById("queue-count");

loginForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  loginError.classList.add("hidden");

  const email = document.getElementById("email").value;
  const password = document.getElementById("password").value;

  const result = await window.beacon.login(email, password);
  if (!result.success) {
    loginError.textContent = result.error;
    loginError.classList.remove("hidden");
    return;
  }

  loginView.classList.add("hidden");
  dashboardView.classList.remove("hidden");
  await refreshIncidents();
});

async function refreshIncidents() {
  const result = await window.beacon.fetchIncidents();
  if (result.success) renderIncidents(result.incidents);
}

function renderIncidents(incidents) {
  incidentList.innerHTML = "";
  emptyState.classList.toggle("hidden", incidents.length > 0);

  for (const incident of incidents) {
    const item = document.createElement("li");
    item.className = `incident severity-${incident.severity.toLowerCase()}`;

    const title = document.createElement("div");
    title.className = "incident-title";
    title.textContent = incident.title;

    const meta = document.createElement("div");
    meta.className = "incident-meta";
    meta.textContent = `${incident.severity} · ${incident.status}${incident.isEscalated ? " · Escalated" : ""}`;

    const actions = document.createElement("div");
    actions.className = "incident-actions";

    if (incident.status === "Declared") {
      const ackButton = document.createElement("button");
      ackButton.textContent = "Acknowledge";
      ackButton.onclick = async () => {
        ackButton.disabled = true;
        const result = await window.beacon.acknowledge(incident.id);
        if (result.queued) flashQueued(item);
        await refreshIncidents();
      };
      actions.appendChild(ackButton);
    }

    const escalateButton = document.createElement("button");
    escalateButton.textContent = "Escalate";
    escalateButton.className = "secondary";
    escalateButton.onclick = async () => {
      escalateButton.disabled = true;
      const result = await window.beacon.escalate(incident.id, "secondary-oncall");
      if (result.queued) flashQueued(item);
      await refreshIncidents();
    };
    actions.appendChild(escalateButton);

    item.append(title, meta, actions);
    incidentList.appendChild(item);
  }
}

function flashQueued(item) {
  const note = document.createElement("div");
  note.className = "queued-note";
  note.textContent = "No connection — action queued, will send when back online.";
  item.appendChild(note);
}

window.beacon.onIncidentsUpdated((incidents) => renderIncidents(incidents));

window.beacon.onQueueDepthChanged((depth) => {
  queueIndicator.classList.toggle("hidden", depth === 0);
  queueCount.textContent = String(depth);
});
