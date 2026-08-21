export type Severity = "Sev1_Critical" | "Sev2_High" | "Sev3_Medium" | "Sev4_Low";
export type IncidentStatus = "Declared" | "Acknowledged" | "Resolved";

export interface IncidentSummary {
  id: string;
  title: string;
  severity: Severity;
  status: IncidentStatus;
  isEscalated: boolean;
  declaredAt: string;
}

export interface TimelineEntry {
  occurredAt: string;
  eventType: string;
  actor: string;
  description: string;
}

export interface IncidentDetail {
  id: string;
  title: string;
  severity: Severity;
  status: IncidentStatus;
  responders: string[];
  isEscalated: boolean;
  eventCount: number;
  timeline: TimelineEntry[];
}
