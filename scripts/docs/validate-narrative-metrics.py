#!/usr/bin/env python3
"""Validates that top-of-authority narrative documents stay free of volatile metrics.

Enforces:
1. Exact test-count claims (e.g. "396/396", "334 test cases") do not appear in the
   protected narrative set.
2. Exact migration counts (e.g. "96 migrations") do not appear there either.
3. Full 40-character SHAs and long numeric CI run identifiers do not appear there.

The single authority for volatile verified facts is docs/execution/status.json.
Dated checkpoint journals (docs/execution/**, docs/testing/**, task-breakdown
coverage ledgers) are deliberately outside this guard because recording observed,
dated evidence is their responsibility.
"""

import re
import subprocess
import sys
from pathlib import Path

# The top-of-authority narrative documents. Everything else that legitimately
# records dated observations (execution ledgers, testing checkpoint journals,
# task-breakdown coverage ledgers, evidence) is outside this guard.
PROTECTED_FILES = [
    "README.md",
    "AGENTS.md",
    "docs/auditsphere-docs-index.md",
    "docs/architecture/auditsphere-architecture-current-architecture.md",
    "docs/architecture/auditsphere-architecture-code-map.md",
    "docs/architecture/auditsphere-architecture-document-naming-policy.md",
]

# Each pattern targets one unambiguous volatile-metric shape. Version numbers,
# dates (yyyy-mm-dd), ports and route paths are deliberately not matched.
PATTERNS = [
    (re.compile(r"(?i)\b\d{2,}\s+test cases\b"), "exact test-case count"),
    (re.compile(r"(?i)\b\d{1,3}\s+migrations\b"), "exact migration count"),
    (re.compile(r"(?i)\b\d{1,3}/\d{1,3}\b"), "test-run count claim"),
    (re.compile(r"\b[0-9a-f]{40}\b"), "full commit SHA"),
    (re.compile(r"\b\d{10,}\b"), "long numeric run identifier"),
]


def get_repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def validate_narrative_metrics():
    root = get_repo_root()
    errors = []
    for rel in PROTECTED_FILES:
        path = root / rel
        if not path.is_file():
            errors.append(f"Protected narrative file missing: {rel}")
            continue
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            for pattern, label in PATTERNS:
                match = pattern.search(line)
                if match:
                    errors.append(
                        f"{rel}:{lineno}: {label} '{match.group(0)}' — volatile facts "
                        f"belong only in docs/execution/status.json"
                    )
    if errors:
        print("FAIL: Volatile metrics found in protected narrative documents:")
        for error in errors:
            print(f"  - {error}")
        sys.exit(1)
    print(f"PASS: No volatile metrics in {len(PROTECTED_FILES)} protected narrative documents.")
    sys.exit(0)


if __name__ == "__main__":
    validate_narrative_metrics()
