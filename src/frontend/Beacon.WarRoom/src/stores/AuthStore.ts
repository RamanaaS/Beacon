import { makeAutoObservable } from "mobx";
import { apiClient, setAuthToken } from "../api/client";

interface LoginResponse {
  token: string;
  expiresAt: string;
  roles: string[];
}

interface ValidationProblem {
  errors?: Record<string, string[]>;
}

const STORAGE_KEY = "beacon.auth";

class AuthStore {
  token: string | null = null;
  roles: string[] = [];
  email: string | null = null;
  error: string | null = null;

  constructor() {
    makeAutoObservable(this);
    this.restoreFromStorage();
  }

  get isAuthenticated(): boolean {
    return this.token !== null;
  }

  // Mirrors the "IncidentResponder" policy on the server (RequireRole(Responder, Admin)) —
  // duplicated here deliberately, not a security boundary: the server enforces the real rule,
  // this only decides which buttons to render. A Viewer bypassing this check client-side still
  // gets a real 403 from the API, not a bypassed permission.
  get isResponder(): boolean {
    return this.roles.includes("Responder") || this.roles.includes("Admin");
  }

  private restoreFromStorage(): void {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return;

    try {
      const parsed = JSON.parse(raw) as { token: string; roles: string[]; email: string };
      this.token = parsed.token;
      this.roles = parsed.roles;
      this.email = parsed.email;
      setAuthToken(this.token);
    } catch {
      localStorage.removeItem(STORAGE_KEY);
    }
  }

  async login(email: string, password: string): Promise<boolean> {
    this.error = null;
    try {
      const response = await apiClient.post<LoginResponse>("/api/auth/login", { email, password });
      this.applySession(email, response.data);
      return true;
    } catch {
      this.error = "Invalid email or password.";
      return false;
    }
  }

  async register(email: string, password: string, displayName: string, role: string): Promise<boolean> {
    this.error = null;
    try {
      await apiClient.post("/api/auth/register", { email, password, displayName, role });
      return await this.login(email, password);
    } catch (err) {
      const data = (err as { response?: { data?: ValidationProblem } })?.response?.data;
      this.error = data?.errors
        ? Object.values(data.errors).flat().join(" ")
        : "Registration failed.";
      return false;
    }
  }

  logout(): void {
    this.token = null;
    this.roles = [];
    this.email = null;
    setAuthToken(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  private applySession(email: string, response: LoginResponse): void {
    this.token = response.token;
    this.roles = response.roles;
    this.email = email;
    setAuthToken(this.token);
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ token: this.token, roles: this.roles, email }));
  }
}

export const authStore = new AuthStore();
