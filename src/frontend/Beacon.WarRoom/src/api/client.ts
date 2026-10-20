import axios from "axios";

// VITE_API_URL is baked in at build time for a deployed copy (the GitHub Pages workflow sets it);
// local dev keeps talking to Beacon.Api on its usual port.
export const apiBaseUrl: string = import.meta.env.VITE_API_URL || "http://localhost:5252";

export const apiClient = axios.create({
  baseURL: apiBaseUrl,
});

// A plain function instead of an axios request interceptor reading from AuthStore directly —
// that would make this module import AuthStore while AuthStore imports this module for its own
// login/register calls, a circular dependency for no real benefit. AuthStore calls this whenever
// the token changes instead.
export function setAuthToken(token: string | null) {
  if (token) {
    apiClient.defaults.headers.common.Authorization = `Bearer ${token}`;
  } else {
    delete apiClient.defaults.headers.common.Authorization;
  }
}
