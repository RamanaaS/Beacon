const axios = require("axios");

class BeaconApi {
  constructor(baseURL) {
    this.client = axios.create({ baseURL, timeout: 8000 });
    this.token = null;
  }

  setToken(token) {
    this.token = token;
    this.client.defaults.headers.common.Authorization = token ? `Bearer ${token}` : undefined;
  }

  async login(email, password) {
    const response = await this.client.post("/api/auth/login", { email, password });
    return response.data; // { token, expiresAt, roles }
  }

  async listOpenIncidents() {
    const response = await this.client.get("/api/incidents");
    return response.data.filter((i) => i.status !== "Resolved");
  }

  async acknowledge(incidentId) {
    await this.client.post(`/api/incidents/${incidentId}/acknowledge`, {});
  }

  async escalate(incidentId, escalatedTo) {
    await this.client.post(`/api/incidents/${incidentId}/escalate`, { escalatedTo });
  }
}

module.exports = { BeaconApi };
