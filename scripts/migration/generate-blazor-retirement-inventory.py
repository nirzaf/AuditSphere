#!/usr/bin/env python3
"""Render a discovery-only register of the legacy Web project sources."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / "src" / "AuditSphereOps.Web"
SOURCE_INVENTORY = ROOT / "docs" / "execution" / "angular-source-inventory.json"
OUTPUT = ROOT / "docs" / "migration" / "blazor-retirement-inventory.md"


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


def render() -> str:
    source = json.loads(SOURCE_INVENTORY.read_text())
    items = source["items"]
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
        "> **Discovery register only.** A route, candidate Angular feature, injected service, or test name does not prove behavioral parity. All source entries remain `NOT_ANALYZED` until source actions, server authority, scope rules, failures, recovery, and equivalent tests are reviewed together.",
        "",
        "Exact source counts and the discovery snapshot are recorded in `docs/execution/status.json`.",
        "",
        "## Observed inventory",
        "",
        "- The complete non-generated Web file list is enumerated below.",
        "- Razor route/action files are listed with their scanner-derived hints.",
        "- Exact source and route totals are maintained in `docs/execution/status.json`.",
        "- Source discovery drift is separately checked by `python3 scripts/ui/inventory.py --check`.",
        "- No row in this generated register is accepted as feature parity or removal-ready evidence.",
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
                        "`NOT_ANALYZED`",
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
        "Review the complete Blazor artifact and record its user-invokable actions, roles and scope, backend owner, API contract, Angular counterpart, validation and empty/error states, concurrency and unknown-result recovery, accessibility/navigation behavior, and tests that exercise the API/Angular path. Use `PARITY_VERIFIED` only after behavior-level evidence; use `INTENTIONALLY_RETIRED` only with explicit product-owner approval. Physical removal readiness is tracked separately in `blazor-removal-readiness.md`.",
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
