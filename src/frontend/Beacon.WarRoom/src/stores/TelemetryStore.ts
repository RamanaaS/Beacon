import { makeAutoObservable, runInAction } from "mobx";
import { Subject, type Subscription, bufferTime, filter } from "rxjs";

export interface MetricSample {
  serviceName: string;
  cpuPercent: number;
  latencyMs: number;
  errorRatePercent: number;
  timestamp: string;
}

const MAX_POINTS_PER_SERVICE = 20;

class TelemetryStore {
  samplesByService: Record<string, MetricSample[]> = {};
  connected = false;

  private socket: WebSocket | null = null;
  private readonly rawSamples$ = new Subject<MetricSample>();
  private subscription: Subscription | null = null;

  constructor() {
    // socket/rawSamples$/subscription excluded — a raw WebSocket and an RxJS Subject aren't
    // meaningful things for MobX to make observable; only the derived state (samplesByService,
    // connected) needs to be.
    makeAutoObservable<TelemetryStore, "socket" | "rawSamples$" | "subscription">(this, {
      socket: false,
      rawSamples$: false,
      subscription: false,
    });
  }

  connect(): void {
    if (this.socket) return;

    // MetricsSimulatorService (Phase 4) broadcasts 3 messages per 2-second tick, one per
    // simulated service. Applying each one straight to MobX state would trigger 3 re-renders in
    // quick succession for what's really one "tick" of new data. bufferTime(500) batches
    // whatever arrived in the last half-second into one state update instead — a genuine reason
    // to reach for RxJS here, not just because the resume lists it.
    this.subscription = this.rawSamples$
      .pipe(
        bufferTime(500),
        filter((batch) => batch.length > 0),
      )
      .subscribe((batch) => this.applyBatch(batch));

    const socket = new WebSocket("ws://localhost:5252/ws/telemetry");
    socket.onopen = () => runInAction(() => { this.connected = true; });
    socket.onclose = () => runInAction(() => { this.connected = false; });
    socket.onmessage = (event: MessageEvent<string>) => {
      this.rawSamples$.next(JSON.parse(event.data) as MetricSample);
    };
    this.socket = socket;
  }

  disconnect(): void {
    this.socket?.close();
    this.socket = null;
    this.subscription?.unsubscribe();
    this.subscription = null;
  }

  private applyBatch(batch: MetricSample[]): void {
    runInAction(() => {
      const next = { ...this.samplesByService };
      for (const sample of batch) {
        const existing = next[sample.serviceName] ?? [];
        next[sample.serviceName] = [...existing, sample].slice(-MAX_POINTS_PER_SERVICE);
      }
      this.samplesByService = next;
    });
  }
}

export const telemetryStore = new TelemetryStore();
