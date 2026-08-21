// Minimal JS interop surface for ApexCharts — Blazor Server has no direct DOM/canvas access from
// C#, so any charting library needs a thin JS bridge like this one. Two functions: render (create
// or replace the chart in a container) and dispose (avoid leaking chart instances across
// Blazor's OnAfterRenderAsync re-renders).
const charts = {};

window.beaconCharts = {
  renderBarChart: function (elementId, categories, series) {
    if (charts[elementId]) {
      charts[elementId].destroy();
    }

    const options = {
      chart: { type: "bar", height: 300, toolbar: { show: false } },
      series: [{ name: "Count", data: series }],
      xaxis: { categories: categories },
      colors: ["#4f46e5"],
      dataLabels: { enabled: true },
    };

    charts[elementId] = new ApexCharts(document.querySelector("#" + elementId), options);
    charts[elementId].render();
  },

  dispose: function (elementId) {
    if (charts[elementId]) {
      charts[elementId].destroy();
      delete charts[elementId];
    }
  },
};
