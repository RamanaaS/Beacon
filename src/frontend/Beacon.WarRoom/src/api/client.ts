import axios from "axios";

export const apiClient = axios.create({
  baseURL: "http://localhost:5252",
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
