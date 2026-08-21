import { observer } from "mobx-react-lite";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { DeclareIncidentForm } from "../components/DeclareIncidentForm";
import { TelemetryPanel } from "../components/TelemetryPanel";
import { authStore } from "../stores/AuthStore";
import { incidentStore } from "../stores/IncidentStore";

export const DashboardPage = observer(() => {
  const [showDeclareForm, setShowDeclareForm] = useState(false);

  useEffect(() => {
    void incidentStore.fetchAll();
  }, []);

  return (
    <div className="dashboard">
      <header>
        <h1>Beacon</h1>
        <div className="user-info">
          <span>
            {authStore.email} ({authStore.roles.join(", ")})
          </span>
          <button type="button" onClick={() => authStore.logout()}>
            Log out
          </button>
        </div>
      </header>

      <TelemetryPanel />

      <section>
        <div className="section-header">
          <h2>Incidents</h2>
          {authStore.isResponder && (
            <button type="button" onClick={() => setShowDeclareForm(true)}>
              Declare Incident
            </button>
          )}
        </div>

        {showDeclareForm && <DeclareIncidentForm onClose={() => setShowDeclareForm(false)} />}

        {incidentStore.loading && <p className="muted">Loading…</p>}
        {incidentStore.error && <p className="error">{incidentStore.error}</p>}

        <table className="incident-table">
          <thead>
            <tr>
              <th>Title</th>
              <th>Severity</th>
              <th>Status</th>
              <th>Escalated</th>
              <th>Declared</th>
            </tr>
          </thead>
          <tbody>
            {incidentStore.incidents.map((incident) => (
              <tr key={incident.id} className={`severity-${incident.severity.toLowerCase()}`}>
                <td>
                  <Link to={`/incidents/${incident.id}`}>{incident.title}</Link>
                </td>
                <td>{incident.severity}</td>
                <td>{incident.status}</td>
                <td>{incident.isEscalated ? "Yes" : "—"}</td>
                <td>{new Date(incident.declaredAt).toLocaleString()}</td>
              </tr>
            ))}
            {incidentStore.incidents.length === 0 && !incidentStore.loading && (
              <tr>
                <td colSpan={5} className="muted">
                  No incidents yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
});
