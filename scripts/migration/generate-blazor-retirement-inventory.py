#!/usr/bin/env python3
"""Render a discovery-only register of the legacy Web project sources."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / "src" / "AuditSphereOps.Web"
SOURCE_INVENTORY = ROOT / "docs" / "execution" / "angular-source-inventory.json"
OUTPUT = ROOT / "docs" / "migration" / "auditsphere-migration-blazor-retirement-inventory.md"
REVIEWS = ROOT / "docs" / "migration" / "auditsphere-migration-blazor-source-action-reviews.json"

ALLOWED_DISPOSITIONS = {"PARITY_VERIFIED", "PARTIAL", "INTENTIONALLY_RETIRED"}


def cell(value: object) -> str:
    text = str(value).replace("|", "\\|").replace("\n", " ").strip()
    return text or "—"


def joined(values: list[str], limit: int = 5) -> str:
    if not values:
        return "—"
    visible = values[:limit]
    remainder = len(values) - len(visible)
    text = "; ".join(visible)
    return f"{text}; … (+{remainder})" if remainder else text


def reviewed_sources(items: list[dict[str, object]]) -> dict[str, dict[str, str]]:
    """Load explicit, evidence-linked reviews without treating discovery hints as proof."""
    if not REVIEWS.exists():
        return {}
    review_data = json.loads(REVIEWS.read_text())
    if review_data.get("schemaVersion") != 1:
        raise SystemExit("Unsupported source-action review manifest schema.")

    known_sources = {str(item["source"]) for item in items}
    result: dict[str, dict[str, str]] = {}
    for record in review_data.get("records", []):
        source = record.get("source")
        disposition = record.get("disposition")
        evidence = record.get("evidence")
        reviewed_commit = record.get("reviewedAtCommit")
        if source not in known_sources:
            raise SystemExit(f"Source-action review references an undiscovered source: {source}")
        if source in result:
            raise SystemExit(f"Duplicate source-action review: {source}")
        if disposition not in ALLOWED_DISPOSITIONS:
            raise SystemExit(f"Unsupported disposition for {source}: {disposition}")
        evidence_path = ROOT / "docs" / "migration" / str(evidence)
        if not evidence or not evidence_path.is_file():
            raise SystemExit(f"Missing evidence document for reviewed source: {source}")
        if not isinstance(reviewed_commit, str) or len(reviewed_commit) != 40 or any(c not in "0123456789abcdef" for c in reviewed_commit.lower()):
            raise SystemExit(f"Review must identify a full 40-character source commit: {source}")
        if disposition == "INTENTIONALLY_RETIRED" and not str(record.get("ownerApproval", "")).strip():
            raise SystemExit(f"Intentional retirement requires an explicit owner-approval reference: {source}")
        open_gaps = record.get("openGaps", [])
        if disposition == "PARTIAL" and (not isinstance(open_gaps, list) or not open_gaps):
            raise SystemExit(f"A partial review must list its unresolved parity evidence: {source}")
        if disposition == "PARITY_VERIFIED" and open_gaps:
            raise SystemExit(f"A parity-verified review cannot retain open gaps: {source}")
        result[source] = {
            "disposition": disposition,
            "evidence": str(evidence),
            "reviewedAtCommit": reviewed_commit,
        }
    return result


def render() -> str:
    source = json.loads(SOURCE_INVENTORY.read_text())
    items = source["items"]
    reviews = reviewed_sources(items)
    files = sorted(
        path.relative_to(ROOT).as_posix()
        for path in WEB.rglob("*")
        if path.is_file() and not ({"bin", "obj"} & set(path.parts))
    )
    discovered = {item["source"] for item in items}
    missing = discovered - set(files)
    if missing:
        raise SystemExit("Source inventory references missing Web files: " + ", ".join(sorted(missing)))

    routes = sorted({route for item in items for route in item["routes"]})
    support_files = [path for path in files if path not in discovered]
    lines = [
        "# AuditSphere — Blazor Retirement Inventory",
        "",
        "> **Discovery register with curated reviews.** Route/action hints do not prove parity. Manifest rows carry evidence-linked dispositions; `PARTIAL` records unresolved gaps, `PARITY_VERIFIED` requires complete behavior evidence, and neither disposition alone establishes removal readiness.",
        "",
        "Exact source counts and the discovery snapshot are recorded in `docs/execution/status.json`.",
        "",
        "## Observed inventory",
        "",
        "- The complete non-generated Web file list is enumerated below.",
        "- Razor route/action files are listed with their scanner-derived hints.",
        "- Exact source and route totals are maintained in `docs/execution/status.json`.",
        "- Source discovery drift is separately checked by `python3 scripts/ui/inventory.py --check`.",
        "- Only rows named in `auditsphere-migration-blazor-source-action-reviews.json` carry a reviewed disposition; `PARTIAL` rows must name open evidence gaps, and a disposition applies to the cited source artifact only.",
        "",
        "## Razor routes and action components",
        "",
        "Action/dependency values below are syntax-level scanner hints. Roles, business authority, API contracts, and test equivalence still require manual review.",
        "",
        "| Blazor artifact | Route(s) | Candidate Angular feature / story | Parsed action hints | Injected dependencies | Review status |",
        "|---|---|---|---|---|---|",
    ]
    for item in items:
        destination = item.get("destination") or {}
        route = joined(item.get("routes", []), limit=8)
        actions = joined(item.get("asyncCalls", []) or item.get("actions", []), limit=6)
        dependencies = joined([pair[0] for pair in item.get("injectedServices", [])], limit=5)
        feature = f"{destination.get('feature', 'unmapped')} / {destination.get('story', 'unmapped')} (candidate)"
        review = reviews.get(item["source"])
        review_status = f"[`{review['disposition']}`]({review['evidence']})" if review else "`NOT_ANALYZED`"
        lines.append(
            "| "
            + " | ".join(
                map(
                    cell,
                    [
                        f"`{item['source']}`",
                        route,
                        feature,
                        actions,
                        dependencies,
                        review_status,
                    ],
                )
            )
            + " |"
        )

    lines += [
        "",
        "## Supporting Web project files",
        "",
        "These files were enumerated but their retirement impact has not been accepted. Static asset and host dependencies must be traced before removal.",
        "",
        "| Artifact | Type hint | Review status |",
        "|---|---|---|",
    ]
    for path in support_files:
        suffix = Path(path).suffix or "(no extension)"
        lines.append(f"| `{path}` | {cell(suffix)} | `NOT_ANALYZED` |")

    lines += [
        "",
        "## Evidence required to advance a row",
        "",
        "Review the complete Blazor artifact and record its user-invokable actions, roles and scope, backend owner, API contract, Angular counterpart, validation and empty/error states, concurrency and unknown-result recovery, accessibility/navigation behavior, and tests that exercise the API/Angular path. Record `PARITY_VERIFIED` only with behavior-level evidence; record `PARTIAL` with the remaining gaps; record `INTENTIONALLY_RETIRED` only with an explicit owner-approval reference. Physical removal readiness is tracked separately in `auditsphere-migration-blazor-removal-readiness.md`.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="fail if the register is stale")
    args = parser.parse_args()
    expected = render()
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text() != expected:
            raise SystemExit("Blazor retirement inventory is stale; regenerate it.")
        print("PASS: Blazor retirement inventory matches source discovery.")
        return
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(expected)
    print(f"Wrote discovery register for {len(json.loads(SOURCE_INVENTORY.read_text())['items'])} Razor artifacts.")


if __name__ == "__main__":
    main()
