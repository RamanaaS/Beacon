import { observer } from "mobx-react-lite";
import { type FormEvent, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { apiClient } from "../api/client";
import { authStore } from "../stores/AuthStore";
import { incidentStore } from "../stores/IncidentStore";

// Plain <a href="..."> can't carry the Authorization header every incident endpoint now
// requires (browsers don't attach custom headers to anchor navigation) — fetching the file
// through the authenticated apiClient and handing the browser a Blob/object URL is the actual
// correct pattern for an authenticated download in an SPA, not a link.
async function downloadReport(id: string, format: "xlsx" | "pdf"): Promise<void> {
  const response = await apiClient.get(`/api/incidents/${id}/report.${format}`, { responseType: "blob" });
  const url = URL.createObjectURL(response.data as Blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `incident-${id}.${format}`;
  link.click();
  URL.revokeObjectURL(url);
}

export const IncidentDetailPage = observer(() => {
  const { id } = useParams<{ id: string }>();
  const [resolveSummary, setResolveSummary] = useState("");
  const [escalateTo, setEscalateTo] = useState("secondary-oncall");
  const [postmortemDraft, setPostmortemDraft] = useState<string | null>(null);
  const [draftError, setDraftError] = useState<string | null>(null);
  const [draftLoading, setDraftLoading] = useState(false);

  useEffect(() => {
    if (id) void incidentStore.fetchOne(id);
  }, [id]);

  if (!id) return null;

  const incident = incidentStore.selected;
  if (!incident) return <p className="muted">Loading…</p>;

  async function handleAcknowledge(): Promise<void> {
    await incidentStore.acknowledge(id!);
  }

  async function handleResolve(e: FormEvent): Promise<void> {
    e.preventDefault();
    await incidentStore.resolve(id!, resolveSummary);
    setResolveSummary("");
  }

  async function handleEscalate(e: FormEvent): Promise<void> {
    e.preventDefault();
    await incidentStore.escalate(id!, escalateTo);
  }

  async function handleDraftPostmortem(): Promise<void> {
    setDraftLoading(true);
    setDraftError(null);
    try {
      const response = await apiClient.post<{ draft: string }>(`/api/incidents/${id}/postmortem/draft`);
      setPostmortemDraft(response.data.draft);
    } catch (err) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      setDraftError(
        status === 503
          ? "AI postmortem drafting isn't configured on this server (no Claude API key set)."
          : "Failed to generate postmortem draft.",
      );
    } finally {
      setDraftLoading(false);
    }
  }

  return (
    <div className="incident-detail">
      <Link to="/">&larr; Back to dashboard</Link>
      <h1>{incident.title}</h1>
      <div className="incident-meta">
        <span className={`badge severity-${incident.severity.toLowerCase()}`}>{incident.severity}</span>
        <span className="badge">{incident.status}</span>
        {incident.isEscalated && <span className="badge escalated-badge">Escalated</span>}
      </div>

      {authStore.isResponder && incident.status !== "Resolved" && (
        <div className="actions">
          {incident.status === "Declared" && (
            <button type="button" onClick={() => void handleAcknowledge()}>
              Acknowledge
            </button>
          )}

          <form onSubmit={(e) => void handleResolve(e)}>
            <input
              placeholder="Resolution summary"
              value={resolveSummary}
              onChange={(e) => setResolveSummary(e.target.value)}
              required
            />
            <button type="submit">Resolve</button>
          </form>

          <form onSubmit={(e) => void handleEscalate(e)}>
            <input value={escalateTo} onChange={(e) => setEscalateTo(e.target.value)} />
            <button type="submit">Escalate</button>
          </form>
        </div>
      )}

      {incident.status === "Resolved" && (
        <div className="reports">
          <button type="button" onClick={() => void downloadReport(id, "xlsx")}>
            Download Excel report
          </button>
          <button type="button" onClick={() => void downloadReport(id, "pdf")}>
            Download PDF report
          </button>
          {authStore.isResponder && (
            <button type="button" onClick={() => void handleDraftPostmortem()} disabled={draftLoading}>
              {draftLoading ? "Drafting…" : "Draft Postmortem (AI)"}
            </button>
          )}
        </div>
      )}

      {draftError && <p className="error">{draftError}</p>}
      {postmortemDraft && <pre className="postmortem-draft">{postmortemDraft}</pre>}

      <h2>Timeline</h2>
      <ul className="timeline">
        {incident.timeline.map((entry, index) => (
          <li key={index}>
            <span className="timeline-time">{new Date(entry.occurredAt).toLocaleString()}</span>
            <span className="timeline-type">{entry.eventType}</span>
            <span className="timeline-actor">{entry.actor}</span>
            <p>{entry.description}</p>
          </li>
        ))}
      </ul>
    </div>
  );
});
