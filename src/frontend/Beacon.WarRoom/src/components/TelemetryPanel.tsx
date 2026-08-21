import {
  CategoryScale,
  Chart as ChartJS,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  Tooltip,
} from "chart.js";
import { observer } from "mobx-react-lite";
import { useEffect } from "react";
import { Line } from "react-chartjs-2";
import { telemetryStore } from "../stores/TelemetryStore";

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend);

const SERIES_COLORS = ["#4f46e5", "#059669", "#dc2626"];

export const TelemetryPanel = observer(() => {
  useEffect(() => {
    telemetryStore.connect();
    return () => telemetryStore.disconnect();
  }, []);

  const services = Object.keys(telemetryStore.samplesByService);

  const data = {
    labels:
      services.length > 0
        ? telemetryStore.samplesByService[services[0]].map((s) => new Date(s.timestamp).toLocaleTimeString())
        : [],
    datasets: services.map((service, index) => ({
      label: `${service} CPU %`,
      data: telemetryStore.samplesByService[service].map((s) => s.cpuPercent),
      borderColor: SERIES_COLORS[index % SERIES_COLORS.length],
      tension: 0.3,
    })),
  };

  return (
    <section className="telemetry-panel">
      <div className="section-header">
        <h2>Live Telemetry</h2>
        <span className={telemetryStore.connected ? "status-live" : "status-offline"}>
          {telemetryStore.connected ? "● live" : "○ disconnected"}
        </span>
      </div>
      {services.length === 0 ? (
        <p className="muted">Waiting for telemetry…</p>
      ) : (
        <Line data={data} options={{ animation: false, scales: { y: { min: 0, max: 100 } } }} />
      )}
    </section>
  );
});
