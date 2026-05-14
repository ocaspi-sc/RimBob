#!/usr/bin/env python3
"""Summarize RimAI Serilog decision logs for one minister.

The current logs are Serilog JSONL, not the final structured decision-log
schema. This script intentionally uses defensive heuristics and prints JSON
that a refinement session can use as evidence.
"""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any


FRACTIONAL_SECONDS = re.compile(r"(\.\d{6})\d+([+-]\d{2}:\d{2}|$)")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Summarize RimAI minister decision logs.")
    parser.add_argument("--repo", required=True, help="Path to the RimAI repo root.")
    parser.add_argument("--minister", required=True, help="Minister name, e.g. Food or Mayor.")
    parser.add_argument("--days", type=int, default=None, help="Optional lookback window in days.")
    return parser.parse_args()


def parse_timestamp(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value.strip():
        return None
    text = value.strip()
    if text.endswith("Z"):
        text = f"{text[:-1]}+00:00"
    text = FRACTIONAL_SECONDS.sub(r"\1\2", text)
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def lower_text(*values: Any) -> str:
    return " ".join(str(v) for v in values if v is not None).lower()


def is_minister_event(entry: dict[str, Any], minister: str) -> bool:
    properties = entry.get("Properties") or {}
    haystack = lower_text(
        entry.get("MessageTemplate"),
        entry.get("RenderedMessage"),
        properties.get("SourceContext"),
        properties.get("Minister"),
        properties.get("SourceMinister"),
        properties.get("Reason"),
        properties.get("Trace"),
    )
    return minister.lower() in haystack


def exception_signature(exception: Any) -> str | None:
    if not isinstance(exception, str) or not exception.strip():
        return None
    first = exception.strip().splitlines()[0]
    if ":" in first:
        kind, detail = first.split(":", 1)
        detail = detail.strip()
        if len(detail) > 140:
            detail = f"{detail[:137]}..."
        return f"{kind.strip()}: {detail}"
    return first[:160]


def increment_failure_buckets(entry: dict[str, Any], buckets: dict[str, Counter[str]]) -> None:
    properties = entry.get("Properties") or {}
    text = lower_text(entry.get("MessageTemplate"), entry.get("RenderedMessage"), entry.get("Exception"))
    reason = str(properties.get("Reason") or "unknown")
    signature = exception_signature(entry.get("Exception")) or (
        reason if reason != "unknown" else str(entry.get("MessageTemplate") or "unknown")
    )

    if "jsonexception" in text or "could not be converted" in text or "deserialize" in text or "parse" in text:
        buckets["schema_parse_failures"][signature] += 1
    if "quota" in text or "rate-limit" in text or "free_tier" in text:
        buckets["quota_failures"][signature] += 1
    if (
        "httprequestexception" in text
        or "socketexception" in text
        or "forbidden by its access permissions" in text
        or "no such host" in text
        or "connection" in text
        or "timeout" in text
    ):
        buckets["network_failures"][signature] += 1
    if "failed" in text or entry.get("Level") in {"Warning", "Error", "Fatal"}:
        buckets["warnings_and_errors"][signature] += 1


def read_pushbacks(repo: Path, minister: str) -> dict[str, Any]:
    pushback_dir = repo / "Src" / "Cabinet" / minister / "Pushbacks"
    if not pushback_dir.exists():
        return {
            "path": str(pushback_dir),
            "exists": False,
            "files": [],
            "entries": 0,
            "sample_text": [],
        }

    files = sorted(pushback_dir.glob("*.jsonl"))
    entries = 0
    samples: list[str] = []
    for path in files:
        with path.open("r", encoding="utf-8-sig") as handle:
            for line in handle:
                if not line.strip():
                    continue
                entries += 1
                if len(samples) >= 5:
                    continue
                try:
                    payload = json.loads(line)
                except json.JSONDecodeError:
                    samples.append(line.strip()[:180])
                    continue
                text = payload.get("pushback_text") or payload.get("note") or payload.get("text")
                if text:
                    samples.append(str(text)[:180])

    return {
        "path": str(pushback_dir),
        "exists": True,
        "files": [str(p) for p in files],
        "entries": entries,
        "sample_text": samples,
    }


def replay_file_stem(minister: str) -> str:
    chars: list[str] = []
    for c in minister.strip().lower():
        if c.isalnum() or c in {"-", "_"}:
            chars.append(c)
        elif c.isspace():
            chars.append("-")
    return "".join(chars) or "unknown"


def read_replay_corpus(repo: Path, minister: str, cutoff: datetime | None) -> dict[str, Any]:
    replay_dir = repo / "logs" / "replay"
    stem = replay_file_stem(minister)
    if not replay_dir.exists():
        return {
            "path": str(replay_dir),
            "exists": False,
            "files": [],
            "records": 0,
        }

    counters: dict[str, Counter[str]] = {
        "schema_versions": Counter(),
        "paths": Counter(),
        "rule_traces": Counter(),
        "escalation_reasons": Counter(),
        "error_types": Counter(),
    }
    files = sorted(replay_dir.glob(f"{stem}-*.jsonl"))
    records = 0
    records_with_raw_output = 0
    records_with_guide_citations = 0
    parse_errors = 0
    examples: list[dict[str, Any]] = []

    for path in files:
        with path.open("r", encoding="utf-8-sig") as handle:
            for line_no, line in enumerate(handle, start=1):
                if not line.strip():
                    continue
                try:
                    record = json.loads(line)
                except json.JSONDecodeError:
                    parse_errors += 1
                    continue

                captured_at = parse_timestamp(record.get("captured_at"))
                if cutoff is not None and captured_at is not None and captured_at < cutoff:
                    continue

                records += 1
                counters["schema_versions"][str(record.get("schema_version") or "unknown")] += 1
                counters["paths"][str(record.get("path") or "unknown")] += 1
                if record.get("rule_trace"):
                    counters["rule_traces"][str(record["rule_trace"])] += 1
                if record.get("escalation_reason"):
                    counters["escalation_reasons"][str(record["escalation_reason"])] += 1

                error = record.get("error")
                if isinstance(error, dict) and error.get("type"):
                    counters["error_types"][str(error["type"])] += 1

                llm = record.get("llm")
                if isinstance(llm, dict) and llm.get("raw_output"):
                    records_with_raw_output += 1
                citations = record.get("guide_citations")
                if isinstance(citations, list) and len(citations) > 0:
                    records_with_guide_citations += 1

                if len(examples) < 8:
                    examples.append({
                        "file": str(path),
                        "line": line_no,
                        "captured_at": record.get("captured_at"),
                        "path": record.get("path"),
                        "rule_trace": record.get("rule_trace"),
                        "escalation_reason": record.get("escalation_reason"),
                        "advice_count": len(record.get("advice") or []),
                        "flag_count": len(record.get("flags") or []),
                        "error_type": error.get("type") if isinstance(error, dict) else None,
                    })

    return {
        "path": str(replay_dir),
        "exists": True,
        "files": [str(p) for p in files],
        "records": records,
        "json_parse_errors": parse_errors,
        "records_with_raw_output": records_with_raw_output,
        "records_with_guide_citations": records_with_guide_citations,
        "counts": {
            "schema_versions": top(counters["schema_versions"]),
            "paths": top(counters["paths"]),
            "rule_traces": top(counters["rule_traces"]),
            "escalation_reasons": top(counters["escalation_reasons"]),
            "error_types": top(counters["error_types"]),
        },
        "examples": examples,
    }


def top(counter: Counter[str], limit: int = 10, minimum: int = 1) -> list[dict[str, Any]]:
    return [
        {"value": value, "count": count}
        for value, count in counter.most_common(limit)
        if count >= minimum
    ]


def main() -> int:
    args = parse_args()
    repo = Path(args.repo).resolve()
    logs_dir = repo / "logs"
    cutoff = None
    if args.days is not None:
        cutoff = datetime.now(timezone.utc) - timedelta(days=args.days)

    counters: dict[str, Counter[str]] = {
        "levels": Counter(),
        "message_templates": Counter(),
        "rule_traces": Counter(),
        "escalation_reasons": Counter(),
        "schema_parse_failures": Counter(),
        "quota_failures": Counter(),
        "network_failures": Counter(),
        "warnings_and_errors": Counter(),
    }
    examples: list[dict[str, Any]] = []
    files_seen: list[str] = []
    total_lines = 0
    matched_events = 0
    skipped_old = 0
    parse_errors = 0
    first_seen: datetime | None = None
    last_seen: datetime | None = None

    for path in sorted(logs_dir.glob("decisions-*.jsonl")):
        file_matched = False
        with path.open("r", encoding="utf-8-sig") as handle:
            for line_no, line in enumerate(handle, start=1):
                if not line.strip():
                    continue
                total_lines += 1
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    parse_errors += 1
                    continue

                timestamp = parse_timestamp(entry.get("Timestamp"))
                if cutoff is not None and timestamp is not None and timestamp < cutoff:
                    skipped_old += 1
                    continue
                if not is_minister_event(entry, args.minister):
                    continue

                matched_events += 1
                file_matched = True
                if timestamp is not None:
                    first_seen = timestamp if first_seen is None else min(first_seen, timestamp)
                    last_seen = timestamp if last_seen is None else max(last_seen, timestamp)

                properties = entry.get("Properties") or {}
                template = str(entry.get("MessageTemplate") or "")
                counters["levels"][str(entry.get("Level") or "Unknown")] += 1
                if template:
                    counters["message_templates"][template] += 1
                trace = properties.get("Trace")
                if trace:
                    counters["rule_traces"][str(trace)] += 1
                reason = properties.get("Reason")
                if reason:
                    counters["escalation_reasons"][str(reason)] += 1
                increment_failure_buckets(entry, counters)

                if len(examples) < 12:
                    examples.append({
                        "file": str(path),
                        "line": line_no,
                        "timestamp": entry.get("Timestamp"),
                        "level": entry.get("Level"),
                        "message_template": template,
                        "properties": {
                            key: properties.get(key)
                            for key in ("SourceContext", "Trace", "Reason", "AdviceCount", "FlagCount")
                            if key in properties
                        },
                    })

        if file_matched:
            files_seen.append(str(path))

    repeated_patterns = {
        "rule_traces": top(counters["rule_traces"], minimum=2),
        "escalation_reasons": top(counters["escalation_reasons"], minimum=2),
        "message_templates": top(counters["message_templates"], minimum=2),
        "warnings_and_errors": top(counters["warnings_and_errors"], minimum=2),
    }

    output = {
        "repo": str(repo),
        "minister": args.minister,
        "lookback_days": args.days,
        "logs_dir": str(logs_dir),
        "log_files_with_matches": files_seen,
        "totals": {
            "lines_scanned": total_lines,
            "matched_events": matched_events,
            "skipped_old_events": skipped_old,
            "json_parse_errors": parse_errors,
        },
        "time_range_utc": {
            "first": first_seen.isoformat() if first_seen else None,
            "last": last_seen.isoformat() if last_seen else None,
        },
        "counts": {
            "levels": top(counters["levels"]),
            "message_templates": top(counters["message_templates"]),
            "rule_traces": top(counters["rule_traces"]),
            "escalation_reasons": top(counters["escalation_reasons"]),
            "schema_parse_failures": top(counters["schema_parse_failures"]),
            "quota_failures": top(counters["quota_failures"]),
            "network_failures": top(counters["network_failures"]),
            "warnings_and_errors": top(counters["warnings_and_errors"]),
        },
        "repeated_patterns": repeated_patterns,
        "replay_corpus": read_replay_corpus(repo, args.minister, cutoff),
        "pushbacks": read_pushbacks(repo, args.minister),
        "examples": examples,
    }
    print(json.dumps(output, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
