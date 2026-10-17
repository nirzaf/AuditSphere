#!/usr/bin/env python3
"""Verify the audit-program JSON manifest against its preserved Word source."""

from __future__ import annotations

import hashlib
import json
import sys
import zipfile
from pathlib import Path
from typing import NoReturn
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs/task_breakdown/source/auditsphere-audit-source-working-process.docx"
MANIFEST = ROOT / "tests/AuditSphereOps.Domain.Tests/Fixtures/audit-working-process-source.json"
WORD_NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}


def fail(message: str) -> NoReturn:
    print(f"Audit programme source verification failed: {message}", file=sys.stderr)
    raise SystemExit(1)


def word_paragraphs(path: Path) -> list[str]:
    try:
        with zipfile.ZipFile(path) as document:
            xml = ElementTree.fromstring(document.read("word/document.xml"))
    except (OSError, KeyError, zipfile.BadZipFile, ElementTree.ParseError) as error:
        fail(f"cannot read Word document {path.relative_to(ROOT)}: {error}")
    paragraphs: list[str] = []
    for paragraph in xml.findall(".//w:p", WORD_NS):
        text = "".join(node.text or "" for node in paragraph.findall(".//w:t", WORD_NS)).strip()
        if text:
            paragraphs.append(text)
    return paragraphs


def main() -> int:
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read JSON manifest: {error}")

    try:
        source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    except OSError as error:
        fail(f"cannot read Word source: {error}")
    if source_hash != manifest.get("sourceSha256"):
        fail(f"Word source SHA-256 {source_hash} does not match the manifest")

    expected: list[str] = []
    sections = manifest.get("sections")
    if not isinstance(sections, list) or len(sections) != 20:
        fail("manifest must contain exactly 20 sections")
    for number, section in enumerate(sections, start=1):
        if not isinstance(section, dict):
            fail(f"section entry {number} is not an object")
        if section.get("sectionNumber") != number:
            fail(f"section order/number mismatch at section {number}")
        title = section.get("sectionTitle")
        if not isinstance(title, str) or not title.strip():
            fail(f"section {number} has no title")
        expected.append(f"{number}. {title}")
        procedures = section.get("procedures")
        if not isinstance(procedures, list):
            fail(f"section {number} has no procedure list")
        seen_ids: set[str] = set()
        for ordinal, procedure in enumerate(procedures, start=1):
            if not isinstance(procedure, dict):
                fail(f"section {number} procedure {ordinal} is not an object")
            procedure_id = procedure.get("sourceProcedureId")
            wording = procedure.get("sourceWording")
            if procedure.get("ordinal") != ordinal:
                fail(f"section {number} procedure {ordinal} has the wrong ordinal")
            if procedure_id != f"AWP-{number:02d}-{ordinal:02d}" or procedure_id in seen_ids:
                fail(f"section {number} procedure {ordinal} has a missing, duplicate or misordered source ID")
            if not isinstance(wording, str) or not wording.strip():
                fail(f"{procedure_id} has no source wording")
            seen_ids.add(procedure_id)
            expected.append(wording.strip())

    if len(expected) != 185:
        fail(f"manifest contains {len(expected) - 20} procedures; expected 165")
    actual = word_paragraphs(SOURCE)
    if len(actual) != len(expected):
        fail(f"Word document has {len(actual)} non-empty paragraphs; expected {len(expected)}")
    for index, (word_text, manifest_text) in enumerate(zip(actual, expected, strict=True), start=1):
        if word_text != manifest_text:
            fail(f"paragraph {index} differs: Word={word_text!r}; manifest={manifest_text!r}")

    print("Verified Word source SHA-256, 20 ordered sections, and all 165 exact procedure paragraphs.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
