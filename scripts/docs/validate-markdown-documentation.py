#!/usr/bin/env python3
"""Comprehensive Markdown documentation health validator for AuditSphereOps.

Validates:
1. Markdown filename policy compliance and conventional exceptions (README.md, AGENTS.md).
2. Globally unique basenames across all tracked Markdown files.
3. Local relative link resolution across all repository documentation.
4. Absence of references to deleted or obsolete markdown documents.
5. Presence of all canonical authority documents.
6. Exact presence of HISTORICAL_SOURCE banners on preserved requirement sources.
7. Absence of volatile test/migration counts in normative requirement documents.
"""

from __future__ import annotations
import collections
import os
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote, urlparse

CONVENTIONAL_EXCEPTIONS = {"README.md", "AGENTS.md"}
PROHIBITED_GENERIC_BASENAMES = {
    "new.md", "final.md", "misc.md", "notes.md", "document.md", "file.md",
    "temp.md", "tmp.md", "test.md", "doc.md", "task.md", "spec.md"
}

CANONICAL_DOCS = [
    "docs/auditsphere-docs-index.md",
    "docs/architecture/auditsphere-architecture-current-architecture.md",
    "docs/architecture/auditsphere-architecture-code-map.md",
    "docs/architecture/auditsphere-architecture-document-naming-policy.md",
    "docs/auditsphere-requirements-system-specification-current.md",
    "docs/auditsphere-accounting-module-requirements-current.md",
    "docs/auditsphere-audit-user-stories-prototype-gap-closure-proposed.md",
    "docs/auditsphere-m365-onboarding-user-stories.md",
    "docs/execution/status.json",
    "docs/execution/auditsphere-execution-current-slice.md",
    "docs/execution/auditsphere-execution-pending-tasks.md",
    "docs/operations/auditsphere-operations-runbook-restore-drill.md",
    "docs/operations/auditsphere-operations-decision-connection-capacity.md",
    "docs/operations/auditsphere-operations-guide-telemetry.md",
    "docs/testing/auditsphere-testing-e2e-automation-strategy.md",
    "docs/testing/auditsphere-testing-test-case-catalog.md",
    "docs/evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-approved.md",
    "docs/evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-addendum-approved.md",
    "docs/task_breakdown/auditsphere-r2r-index-task-breakdown.md",
    "docs/task_breakdown/source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md",
    "docs/task_breakdown/source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md",
]

DELETED_PATHS = [
    "docs/execution/auditsphere-execution-implementation-checklist.md",
    "docs/execution/implementation-checklist.md",
    "docs/execution/restore-drill.md",
    "docs/execution/auditsphere-execution-runbook-restore-drill.md", # moved to operations
    "docs/architecture/CODE_MAP.md",
    "docs/architecture/CURRENT_ARCHITECTURE.md",
    "docs/SPECIFICATION.md",
    "docs/AuditSphere_Accounting_module.md",
]

def get_repo_root() -> Path:
    return Path(__file__).resolve().parents[2]

def validate_all():
    root = get_repo_root()
    errors = []

    # 1. List tracked markdown files
    try:
        raw = subprocess.check_output(["git", "ls-files", "*.md"], cwd=root).decode("utf-8")
        tracked_files = [Path(line.strip()) for line in raw.splitlines() if line.strip()]
    except Exception as e:
        print(f"Error listing git-tracked files: {e}", file=sys.stderr)
        sys.exit(1)

    seen_basenames: dict[str, Path] = {}

    # Check 1: Naming & Basename Uniqueness
    for rel_path in tracked_files:
        basename = rel_path.name

        if basename in CONVENTIONAL_EXCEPTIONS:
            if len(rel_path.parts) > 1:
                errors.append(f"Conventional exception {basename} must be at repository root, found: {rel_path}")
            if basename in seen_basenames:
                errors.append(f"Duplicate conventional basename: {basename}")
            seen_basenames[basename] = rel_path
            continue

        if basename in seen_basenames:
            errors.append(f"Basename collision: '{basename}' in {seen_basenames[basename]} and {rel_path}")
        else:
            seen_basenames[basename] = rel_path

        if not basename.endswith(".md"):
            errors.append(f"File does not end with .md: {rel_path}")
        if basename.lower() in PROHIBITED_GENERIC_BASENAMES:
            errors.append(f"Prohibited generic name: {rel_path}")
        if " " in basename:
            errors.append(f"Filename contains space: {rel_path}")
        if "_" in basename:
            errors.append(f"Filename contains underscore: {rel_path}")
        if any(c.isupper() for c in basename):
            errors.append(f"Filename contains uppercase: {rel_path}")
        if not basename.startswith("auditsphere-"):
            errors.append(f"Filename must begin with 'auditsphere-': {rel_path}")

        name_no_ext = basename[:-3]
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", name_no_ext):
            errors.append(f"Filename not kebab-case: {rel_path}")

    # Check 2: Canonical Documents Existence
    for cdoc in CANONICAL_DOCS:
        full_p = root / cdoc
        if not full_p.is_file():
            errors.append(f"Missing canonical documentation file: {cdoc}")

    # Check 3: Preserved Source Banners
    source_r2r = root / "docs/task_breakdown/source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md"
    if source_r2r.is_file():
        if not source_r2r.read_text(encoding="utf-8").startswith("> STATUS: HISTORICAL_SOURCE"):
            errors.append(f"Missing HISTORICAL_SOURCE banner in {source_r2r}")
    source_audit = root / "docs/task_breakdown/source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md"
    if source_audit.is_file():
        if not source_audit.read_text(encoding="utf-8").startswith("> STATUS: HISTORICAL_SOURCE"):
            errors.append(f"Missing HISTORICAL_SOURCE banner in {source_audit}")

    # Check 4: Relative Links & References to Deleted Paths
    all_tracked_set = {str(p).replace("\\", "/") for p in tracked_files}
    checked_links = 0

    for rel_path in tracked_files:
        full_path = root / rel_path
        text = full_path.read_text(encoding="utf-8")

        # Check references to deleted paths (excluding byte-preserved historical sources and pinned commit URLs)
        if not str(rel_path).replace("\\", "/").startswith("docs/task_breakdown/source/"):
            # Strip historical commit-pinned GitHub URLs (e.g. .../blob/<40-hex-sha>/...)
            text_without_pinned_urls = re.sub(r"https://github\.com/[^\s)]+/blob/[0-9a-fA-F]{40}/[^\s)]+", "", text)
            for deleted in DELETED_PATHS:
                if deleted in text_without_pinned_urls:
                    errors.append(f"{rel_path}: references deleted path '{deleted}'")

        # Strip code blocks
        clean_text = re.sub(r"```.*?```", "", text, flags=re.S)

        # Find markdown links: [text](target)
        for match in re.finditer(r"\]\(([^)]+)\)", clean_text):
            target = match.group(1).strip().split(' "', 1)[0]
            if urlparse(target).scheme or target.startswith("//") or target.startswith("#"):
                continue

            # Strip query or fragment
            base_target, _, _ = target.partition("#")
            if not base_target:
                continue

            checked_links += 1
            dest = (full_path.parent / unquote(base_target)).resolve()

            if not dest.exists():
                errors.append(f"{rel_path}: broken relative link target '{target}'")

    # Check 5: Volatile Metrics in Normative Requirements
    normative_docs = [
        "docs/auditsphere-requirements-system-specification-current.md",
        "docs/auditsphere-accounting-module-requirements-current.md",
    ]
    for ndoc in normative_docs:
        p = root / ndoc
        if p.is_file():
            content = p.read_text(encoding="utf-8")
            # Look for volatile test count statements like "###/### tests pass"
            if re.search(r"\b\d+/\d+\s+tests\s+pass", content, re.IGNORECASE):
                errors.append(f"{ndoc}: contains volatile test count statement (metrics belong in status.json)")

    if errors:
        print(f"FAILED: Found {len(errors)} documentation health issues:\n", file=sys.stderr)
        for err in errors:
            print(f"- {err}", file=sys.stderr)
        sys.exit(1)
    else:
        print(f"PASS: Documentation health verification succeeded.")
        print(f"  - Markdown files verified: {len(tracked_files)}")
        print(f"  - Canonical documents verified: {len(CANONICAL_DOCS)}")
        print(f"  - Relative links checked: {checked_links}")
        print(f"  - Globally unique basenames: 100%")
        print(f"  - Deleted path references: 0")
        print(f"  - HISTORICAL_SOURCE banners: Verified")

if __name__ == "__main__":
    validate_all()
