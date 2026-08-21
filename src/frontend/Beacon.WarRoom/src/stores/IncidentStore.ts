import { makeAutoObservable, runInAction } from "mobx";
import { apiClient } from "../api/client";
import type { IncidentDetail, IncidentSummary } from "../types/incident";

class IncidentStore {
  incidents: IncidentSummary[] = [];
  selected: IncidentDetail | null = null;
  loading = false;
  error: string | null = null;

  constructor() {
    makeAutoObservable(this);
  }

  async fetchAll(): Promise<void> {
    this.loading = true;
    this.error = null;
    try {
      const response = await apiClient.get<IncidentSummary[]>("/api/incidents");
      runInAction(() => {
        this.incidents = response.data;
      });
    } catch {
      runInAction(() => {
        this.error = "Failed to load incidents.";
      });
    } finally {
      runInAction(() => {
        this.loading = false;
      });
    }
  }

  async fetchOne(id: string): Promise<void> {
    this.loading = true;
    this.error = null;
    try {
      const response = await apiClient.get<IncidentDetail>(`/api/incidents/${id}`);
      runInAction(() => {
        this.selected = response.data;
      });
    } catch {
      runInAction(() => {
        this.error = "Failed to load incident.";
      });
    } finally {
      runInAction(() => {
        this.loading = false;
      });
    }
  }

  async declare(title: string, severity: string, alertFingerprint: string): Promise<string> {
    const response = await apiClient.post<{ id: string }>("/api/incidents", {
      title,
      severity,
      alertFingerprint,
    });
    await this.fetchAll();
    return response.data.id;
  }

  async acknowledge(id: string): Promise<void> {
    await apiClient.post(`/api/incidents/${id}/acknowledge`, {});
    await this.fetchOne(id);
  }

  async resolve(id: string, summary: string): Promise<void> {
    await apiClient.post(`/api/incidents/${id}/resolve`, { summary });
    await this.fetchOne(id);
  }

  async escalate(id: string, escalatedTo: string): Promise<void> {
    await apiClient.post(`/api/incidents/${id}/escalate`, { escalatedTo });
    await this.fetchOne(id);
  }
}

export const incidentStore = new IncidentStore();
