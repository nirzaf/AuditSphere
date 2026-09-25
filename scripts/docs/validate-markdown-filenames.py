#!/usr/bin/env python3
"""Validates all repository Markdown filenames against the documentation naming policy.

Enforces:
1. lowercase kebab-case (except approved conventional exceptions README.md and AGENTS.md).
2. .md extension.
3. Globally unique basename across the entire repository.
4. No spaces, no underscores.
5. No generic names (new.md, final.md, misc.md, notes.md, document.md, file.md, etc.).
6. Must start with 'auditsphere-' prefix (except conventional exceptions).
"""

import os
import re
import sys
import subprocess
from pathlib import Path

CONVENTIONAL_EXCEPTIONS = {"README.md", "AGENTS.md"}
PROHIBITED_GENERIC_BASENAMES = {
    "new.md", "final.md", "misc.md", "notes.md", "document.md", "file.md",
    "temp.md", "tmp.md", "test.md", "doc.md", "task.md", "spec.md"
}

def get_repo_root() -> Path:
    return Path(__file__).resolve().parents[2]

def validate_markdown_filenames():
    root = get_repo_root()
    # List git-tracked .md files
    try:
        raw = subprocess.check_output(["git", "ls-files", "*.md"], cwd=root).decode("utf-8")
        files = [Path(line.strip()) for line in raw.splitlines() if line.strip()]
    except Exception as e:
        print(f"Error listing git-tracked markdown files: {e}", file=sys.stderr)
        sys.exit(1)

    errors = []
    seen_basenames = {}

    for rel_path in files:
        basename = rel_path.name

        # 1. Check conventional exceptions
        if basename in CONVENTIONAL_EXCEPTIONS:
            # Must be in root
            if len(rel_path.parts) > 1:
                errors.append(
                    f"Conventional exception {basename} is only permitted at the repository root.\n"
                    f"Found at: {rel_path}"
                )
            # Check for duplicates
            if basename in seen_basenames:
                errors.append(
                    f"Duplicate Markdown basename found: '{basename}'\n"
                    f"  1: {seen_basenames[basename]}\n"
                    f"  2: {rel_path}"
                )
            else:
                seen_basenames[basename] = rel_path
            continue

        # 2. Check globally unique basename
        if basename in seen_basenames:
            errors.append(
                f"Duplicate Markdown basename found: '{basename}'\n"
                f"  1: {seen_basenames[basename]}\n"
                f"  2: {rel_path}\n"
                f"Every Markdown filename must be globally unique across the repository."
            )
        else:
            seen_basenames[basename] = rel_path

        # 3. Check extension
        if not basename.endswith(".md"):
            errors.append(f"Invalid extension (must be .md): {rel_path}")

        # 4. Check prohibited generic names
        if basename.lower() in PROHIBITED_GENERIC_BASENAMES:
            errors.append(
                f"Prohibited generic Markdown filename:\n"
                f"  {rel_path}\n"
                f"Filenames must be business-semantic and descriptive."
            )

        # 5. Check spaces
        if " " in basename:
            errors.append(
                f"Invalid Markdown filename (contains spaces):\n"
                f"  {rel_path}\n"
                f"Use lowercase kebab-case with hyphens instead of spaces."
            )

        # 6. Check underscores
        if "_" in basename:
            errors.append(
                f"Invalid Markdown filename (contains underscores):\n"
                f"  {rel_path}\n"
                f"Use lowercase kebab-case with hyphens instead of underscores."
            )

        # 7. Check uppercase letters
        if any(c.isupper() for c in basename):
            errors.append(
                f"Invalid Markdown filename (contains uppercase characters):\n"
                f"  {rel_path}\n"
                f"Use lowercase kebab-case only."
            )

        # 8. Check prefix auditsphere-
        if not basename.startswith("auditsphere-"):
            errors.append(
                f"Invalid Markdown filename (missing required 'auditsphere-' prefix):\n"
                f"  {rel_path}\n"
                f"Expected format: auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md"
            )

        # 9. Check valid kebab-case characters
        name_no_ext = basename[:-3]
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", name_no_ext):
            errors.append(
                f"Invalid Markdown filename format:\n"
                f"  {rel_path}\n"
                f"Must consist of lowercase alphanumeric segments separated by single hyphens."
            )

    if errors:
        print(f"FAILED: Found {len(errors)} markdown filename violations:\n", file=sys.stderr)
        for err in errors:
            print(f"- {err}\n", file=sys.stderr)
        sys.exit(1)
    else:
        print(f"PASS: All {len(files)} Markdown filenames conform to the naming policy.")
        print(f"  - Conventional exceptions: {len(CONVENTIONAL_EXCEPTIONS)}")
        print(f"  - Standardized documents: {len(files) - len(CONVENTIONAL_EXCEPTIONS)}")
        print(f"  - Globally unique basenames verified: 100%")

if __name__ == "__main__":
    validate_markdown_filenames()
