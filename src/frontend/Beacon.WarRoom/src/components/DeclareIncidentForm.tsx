import { type FormEvent, useState } from "react";
import { incidentStore } from "../stores/IncidentStore";

export function DeclareIncidentForm({ onClose }: { onClose: () => void }) {
  const [title, setTitle] = useState("");
  const [severity, setSeverity] = useState("Sev3_Medium");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      // Manually declared incidents don't come from a real alert, so there's no natural
      // fingerprint the way IngestAlertHandler derives one from alertName:instance — a
      // timestamp-based one is unique enough for a human-initiated declaration.
      await incidentStore.declare(title, severity, `manual:${Date.now()}`);
      onClose();
    } catch {
      setError("Failed to declare incident.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="declare-form" onSubmit={handleSubmit}>
      <label>
        Title
        <input value={title} onChange={(e) => setTitle(e.target.value)} required autoFocus />
      </label>
      <label>
        Severity
        <select value={severity} onChange={(e) => setSeverity(e.target.value)}>
          <option value="Sev1_Critical">Sev1 — Critical</option>
          <option value="Sev2_High">Sev2 — High</option>
          <option value="Sev3_Medium">Sev3 — Medium</option>
          <option value="Sev4_Low">Sev4 — Low</option>
        </select>
      </label>
      {error && <p className="error">{error}</p>}
      <div className="form-actions">
        <button type="submit" disabled={submitting}>
          {submitting ? "Declaring…" : "Declare"}
        </button>
        <button type="button" onClick={onClose}>
          Cancel
        </button>
      </div>
    </form>
  );
}
