#!/usr/bin/env python3
"""Synthetic alert generator for Beacon.Api.

Fires Alertmanager-webhook-shaped alert bursts at POST /api/alerts/ingest (structured JSON) or
/api/alerts/ingest-raw (pipe-delimited text, exercising the native log-parser-shim from Phase 13),
for local dev / load testing without needing a real Prometheus + Alertmanager stack running.

Usage:
    python alert_simulator.py --count 30 --rate 10
    python alert_simulator.py --raw --count 10
    python alert_simulator.py --dedup-demo
"""

from __future__ import annotations

import argparse
import random
import sys
import time
from dataclasses import dataclass

import requests

SERVICES = ["payments-api", "checkout-api", "auth-api"]
ALERT_NAMES = ["HighErrorRate", "LatencySpike", "PodCrashLooping", "DiskSpaceLow"]
SEVERITIES = ["Sev1_Critical", "Sev2_High", "Sev3_Medium", "Sev4_Low"]


@dataclass
class Alert:
    source: str
    alert_name: str
    instance: str
    severity: str
    message: str

    def to_json_payload(self) -> dict:
        return {
            "source": self.source,
            "alertName": self.alert_name,
            "instance": self.instance,
            "severity": self.severity,
            "message": self.message,
        }

    def to_raw_line(self) -> str:
        # Mirrors native/log-parser-shim/log_parser.h's wire format exactly:
        # severity|source|alert_name|instance|message
        return f"{self.severity}|{self.source}|{self.alert_name}|{self.instance}|{self.message}"


def random_alert() -> Alert:
    alert_name = random.choice(ALERT_NAMES)
    instance = f"{random.choice(SERVICES)}-{random.randint(1, 9)}"
    severity = random.choice(SEVERITIES)
    return Alert(
        source="alert-simulator",
        alert_name=alert_name,
        instance=instance,
        severity=severity,
        message=f"{alert_name} synthetic alert fired by alert-simulator at {time.strftime('%H:%M:%S')}",
    )


def fire(base_url: str, alert: Alert, raw: bool, timeout: float) -> requests.Response:
    if raw:
        return requests.post(
            f"{base_url}/api/alerts/ingest-raw",
            json={"rawLine": alert.to_raw_line()},
            timeout=timeout,
        )
    return requests.post(
        f"{base_url}/api/alerts/ingest",
        json=alert.to_json_payload(),
        timeout=timeout,
    )


def run_burst(base_url: str, count: int, rate_per_sec: float, raw: bool, timeout: float) -> dict[str, int]:
    delay = 1.0 / rate_per_sec if rate_per_sec > 0 else 0.0
    decisions: dict[str, int] = {}

    for i in range(count):
        alert = random_alert()
        try:
            response = fire(base_url, alert, raw, timeout)
        except requests.RequestException as exc:
            print(f"  [{i + 1}/{count}] ERROR: {exc}", file=sys.stderr)
            decisions["Error"] = decisions.get("Error", 0) + 1
            continue

        if response.status_code != 200:
            print(f"  [{i + 1}/{count}] HTTP {response.status_code}: {response.text[:200]}", file=sys.stderr)
            decisions["Error"] = decisions.get("Error", 0) + 1
            continue

        decision = response.json().get("decision", "Unknown")
        decisions[decision] = decisions.get(decision, 0) + 1
        print(f"  [{i + 1}/{count}] {alert.severity:14s} {alert.alert_name:18s} {alert.instance:20s} -> {decision}")

        if delay:
            time.sleep(delay)

    return decisions


def run_dedup_demo(base_url: str, raw: bool, timeout: float) -> bool:
    """
    Fires the *same* (alert_name, instance) fingerprint 8 times in under a second — well inside
    Beacon's 5-minute dedup window (IngestAlertCommand.DedupWindow) — and checks that exactly one
    of them actually declares a new incident. This is the real point of the whole tool: proving
    the dedup behavior live against the running API, not just trusting the unit tests that already
    cover AlertDeduplicator in isolation.
    """
    alert_name, instance = "HighErrorRate", "payments-api-storm-test"
    print(f"Firing 8 identical alerts for {alert_name}/{instance} (should collapse to 1 new incident)...")

    decisions: dict[str, int] = {}
    for i in range(8):
        alert = Alert(
            source="alert-simulator",
            alert_name=alert_name,
            instance=instance,
            severity="Sev1_Critical",
            message="Dedup demo alert",
        )
        response = fire(base_url, alert, raw, timeout)
        decision = response.json().get("decision", "Unknown") if response.status_code == 200 else f"HTTP{response.status_code}"
        decisions[decision] = decisions.get(decision, 0) + 1
        print(f"  [{i + 1}/8] -> {decision}")

    new_count = decisions.get("New", 0)
    print(f"\nResult: {new_count} 'New' decision(s), {decisions.get('Duplicate', 0)} 'Duplicate' decision(s).")

    if new_count == 1:
        print("PASS: exactly one incident was declared for 8 identical alerts.")
        return True

    if new_count == 8:
        # Not a bug in this tool or in Beacon: RedisRecentAlertsStore fails *open* when Redis is
        # unreachable (GetRecentAsync returns no recent alerts rather than erroring), a deliberate
        # choice that an occasional duplicate incident is cheaper than an alert getting rejected
        # outright during a Redis outage. The direct, expected consequence is that dedup simply
        # doesn't happen while Redis is down — which this machine's has been for this whole
        # project (Docker's daemon never came up). This demo needs a reachable Redis to prove
        # anything meaningful; report that plainly instead of a bare, confusing failure.
        print(
            "NOTE: every alert came back 'New' — this is what Beacon.Api's Redis-down graceful "
            "degradation looks like (dedup is skipped, not enforced with stale data), not "
            "necessarily a bug. Re-run this against an instance with Redis actually reachable to "
            "see real dedup collapse the burst to 1 incident."
        )
        return False

    print(f"FAIL: expected either 1 'New' (Redis working) or 8 'New' (Redis down, degrading as designed), got {new_count}.")
    return False


def main() -> int:
    parser = argparse.ArgumentParser(description="Fire synthetic alerts at a running Beacon.Api instance.")
    parser.add_argument("--url", default="http://localhost:5252", help="Beacon.Api base URL")
    parser.add_argument("--count", type=int, default=20, help="number of alerts to fire")
    parser.add_argument("--rate", type=float, default=5.0, help="alerts per second")
    parser.add_argument("--raw", action="store_true", help="use /api/alerts/ingest-raw (native log-parser-shim path) instead of structured JSON")
    parser.add_argument("--dedup-demo", action="store_true", help="fire one fingerprint repeatedly and verify the 5-minute dedup window collapses it to one incident")
    parser.add_argument(
        "--timeout",
        type=float,
        default=15.0,
        help="per-request timeout in seconds (kept generous on purpose: the first request or two "
        "after Beacon.Api starts can be genuinely slow while its Redis circuit breaker learns "
        "Redis is unreachable, before failing fast on every request after — 5s clipped that)",
    )
    args = parser.parse_args()

    print(f"Target: {args.url} ({'raw pipe-delimited' if args.raw else 'structured JSON'} endpoint)\n")

    if args.dedup_demo:
        return 0 if run_dedup_demo(args.url, args.raw, args.timeout) else 1

    decisions = run_burst(args.url, args.count, args.rate, args.raw, args.timeout)

    print("\nSummary:")
    for decision, n in sorted(decisions.items()):
        print(f"  {decision}: {n}")

    return 1 if decisions.get("Error", 0) > 0 else 0


if __name__ == "__main__":
    raise SystemExit(main())
