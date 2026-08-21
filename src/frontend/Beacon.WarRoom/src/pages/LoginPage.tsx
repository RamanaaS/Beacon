import { observer } from "mobx-react-lite";
import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router-dom";
import { authStore } from "../stores/AuthStore";

export const LoginPage = observer(() => {
  const navigate = useNavigate();
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [role, setRole] = useState("Responder");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setSubmitting(true);
    const ok =
      mode === "login"
        ? await authStore.login(email, password)
        : await authStore.register(email, password, displayName, role);
    setSubmitting(false);
    if (ok) navigate("/");
  }

  return (
    <div className="auth-page">
      <h1>Beacon</h1>
      <p className="tagline">Incident command and SRE operations</p>

      <form onSubmit={handleSubmit} className="auth-form">
        <label>
          Email
          <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" required />
        </label>
        <label>
          Password
          <input value={password} onChange={(e) => setPassword(e.target.value)} type="password" required minLength={8} />
        </label>

        {mode === "register" && (
          <>
            <label>
              Display name
              <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
            </label>
            <label>
              Role
              <select value={role} onChange={(e) => setRole(e.target.value)}>
                <option value="Responder">Responder</option>
                <option value="Viewer">Viewer</option>
                <option value="Admin">Admin</option>
              </select>
            </label>
          </>
        )}

        {authStore.error && <p className="error">{authStore.error}</p>}

        <button type="submit" disabled={submitting}>
          {submitting ? "Please wait…" : mode === "login" ? "Log in" : "Register"}
        </button>
      </form>

      <button type="button" className="link-button" onClick={() => setMode(mode === "login" ? "register" : "login")}>
        {mode === "login" ? "Need an account? Register" : "Have an account? Log in"}
      </button>
    </div>
  );
});
