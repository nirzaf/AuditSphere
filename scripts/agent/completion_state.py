"""Autonomous-completion run state (RUN-002).

Standard-library helpers shared by verify-completion-state.py, ci-status.py and complete.py:
a small JSON-schema subset validator, semantic checks for the task graph, atomic state writes
under a single-writer lock, result/evidence validation and next-task selection.

Developer tooling only. It stores no credentials and never runs model-generated commands.
"""

from __future__ import annotations

import contextlib
import hashlib
import json
import os
import re
import socket
import stat
import tempfile
from pathlib import Path
from typing import Any, Iterable

REPOSITORY = "nirzaf/AuditSphere"
TASK_ID = re.compile(r"^(RUN|WU)-[0-9]{3}$")
SHA = re.compile(r"^[a-f0-9]{40}$")
SCHEMA_DIR = Path(__file__).resolve().parent
MAX_EVIDENCE_BYTES = 32 * 1024 * 1024

IMPLEMENTATION = ("NOT_ASSESSED", "MISSING", "PARTIAL", "PRESENT")
VERIFICATION = ("NOT_RUN", "RUNNING", "PASS", "FAIL", "BLOCKED_ENVIRONMENT", "NOT_APPLICABLE_APPROVED")
CI = ("NOT_DISPATCHED", "QUEUED", "RUNNING", "PASS", "FAIL", "CANCELLED", "TIMED_OUT", "UNKNOWN")
ACCEPTANCE = ("NOT_REQUIRED", "PENDING", "PASS", "BLOCKED_EXTERNAL", "BLOCKED_PROFESSIONAL", "HUMAN_AT_REQUIRED")
SCHEDULING = ("READY", "RUNNING", "WAITING_CI", "REPAIR_REQUIRED", "BLOCKED_DEPENDENCY",
              "BLOCKED_ENVIRONMENT", "BLOCKED_EXTERNAL", "ENGINEERING_VERIFIED")
NEXT_ACTIONS = ("IMPLEMENT", "REPAIR", "RUN_FOCUSED_CHECK", "POLL_CI", "NEXT_READY_TASK",
                "RESUME_REQUIRED", "WAITING_EXTERNAL", "VERIFY_ENGINEERING_COMPLETION")
SEVERITY_RANK = {"critical": 0, "high": 1, "medium": 2, "low": 3}


class StateError(Exception):
    """Raised for corrupt state, a foreign repository, a held lock or a violated invariant."""


# ---------------------------------------------------------------- JSON-schema subset

def load_schema(name: str) -> dict:
    return json.loads((SCHEMA_DIR / name).read_text(encoding="utf-8"))


def _type_ok(value: Any, expected: str) -> bool:
    if expected == "object":
        return isinstance(value, dict)
    if expected == "array":
        return isinstance(value, list)
    if expected == "string":
        return isinstance(value, str)
    if expected == "boolean":
        return isinstance(value, bool)
    if expected == "integer":
        return isinstance(value, int) and not isinstance(value, bool)
    if expected == "number":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if expected == "null":
        return value is None
    raise StateError(f"unsupported schema type {expected!r}")


def validate(instance: Any, schema: dict, root: dict | None = None, path: str = "$") -> list[str]:
    """Validate the subset of draft 2020-12 used by the repository schemas."""
    root = root if root is not None else schema
    if "$ref" in schema:
        ref = schema["$ref"]
        if not ref.startswith("#/$defs/"):
            raise StateError(f"unsupported $ref {ref!r}")
        return validate(instance, root["$defs"][ref.split("/")[-1]], root, path)
    errors: list[str] = []
    if "type" in schema:
        types = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if not any(_type_ok(instance, t) for t in types):
            return [f"{path}: expected {'|'.join(types)}"]
    if "const" in schema and instance != schema["const"]:
        errors.append(f"{path}: must equal {schema['const']!r}")
    if "enum" in schema and instance not in schema["enum"]:
        errors.append(f"{path}: {instance!r} not in {schema['enum']}")
    if isinstance(instance, str):
        if "pattern" in schema and not re.search(schema["pattern"], instance):
            errors.append(f"{path}: does not match {schema['pattern']}")
        if len(instance) < schema.get("minLength", 0):
            errors.append(f"{path}: shorter than {schema['minLength']}")
        if "maxLength" in schema and len(instance) > schema["maxLength"]:
            errors.append(f"{path}: longer than {schema['maxLength']}")
    if isinstance(instance, (int, float)) and not isinstance(instance, bool):
        if "minimum" in schema and instance < schema["minimum"]:
            errors.append(f"{path}: below {schema['minimum']}")
        if "maximum" in schema and instance > schema["maximum"]:
            errors.append(f"{path}: above {schema['maximum']}")
    if isinstance(instance, list):
        if len(instance) < schema.get("minItems", 0):
            errors.append(f"{path}: fewer than {schema['minItems']} items")
        if schema.get("uniqueItems"):
            seen = [json.dumps(i, sort_keys=True) for i in instance]
            if len(seen) != len(set(seen)):
                errors.append(f"{path}: items are not unique")
        if "items" in schema:
            for i, item in enumerate(instance):
                errors += validate(item, schema["items"], root, f"{path}[{i}]")
    if isinstance(instance, dict):
        if len(instance) < schema.get("minProperties", 0):
            errors.append(f"{path}: fewer than {schema['minProperties']} properties")
        for key in schema.get("required", []):
            if key not in instance:
                errors.append(f"{path}: missing required {key!r}")
        props = schema.get("properties", {})
        extra = schema.get("additionalProperties", True)
        pattern_props = schema.get("patternProperties", {})
        for key, value in instance.items():
            if key in props:
                errors += validate(value, props[key], root, f"{path}.{key}")
                continue
            matched = False
            for pattern, sub in pattern_props.items():
                if re.search(pattern, key):
                    matched = True
                    errors += validate(value, sub, root, f"{path}.{key}")
            if matched:
                continue
            if extra is False:
                errors.append(f"{path}: unknown property {key!r}")
            elif isinstance(extra, dict):
                errors += validate(value, extra, root, f"{path}.{key}")
    return errors


# ---------------------------------------------------------------- loading and writing

def load_json(path: Path) -> Any:
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise StateError(f"{path}: not found") from exc
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise StateError(f"{path}: corrupt JSON ({exc})") from exc


def atomic_write_json(path: Path, value: Any) -> None:
    """Write via a temporary file in the same directory, fsync it, then replace."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            json.dump(value, handle, indent=2, sort_keys=True)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    except BaseException:
        with contextlib.suppress(FileNotFoundError):
            os.unlink(tmp)
        raise


def _pid_alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


@contextlib.contextmanager
def single_writer(state_path: Path):
    """Exclusive lock beside the state file. A live holder makes a second writer fail safely."""
    lock = Path(f"{state_path}.lock")
    lock.parent.mkdir(parents=True, exist_ok=True)
    owner = {"pid": os.getpid(), "host": socket.gethostname()}
    for _ in range(2):
        try:
            fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                json.dump(owner, handle)
            break
        except FileExistsError:
            try:
                held = json.loads(lock.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                raise StateError(f"{lock}: unreadable lock; inspect before removing")
            if held.get("host") == owner["host"] and not _pid_alive(int(held.get("pid", -1))):
                lock.unlink()  # stale lock from a dead local process
                continue
            raise StateError(f"{lock}: held by pid {held.get('pid')} on {held.get('host')}")
    else:
        raise StateError(f"{lock}: could not acquire")
    try:
        yield
    finally:
        with contextlib.suppress(FileNotFoundError):
            lock.unlink()


# ---------------------------------------------------------------- semantic checks

def implementation_satisfied(task: dict) -> bool:
    if task["scheduling"] == "ENGINEERING_VERIFIED":
        return True
    return task["implementation"] == "PRESENT" and task["localVerification"] in ("PASS", "NOT_APPLICABLE_APPROVED")


def find_cycle(tasks: dict) -> list[str] | None:
    colour: dict[str, int] = {}
    stack: list[str] = []

    def visit(node: str) -> list[str] | None:
        colour[node] = 1
        stack.append(node)
        for dep in tasks[node]["implementationDependencies"]:
            if dep not in tasks:
                continue
            if colour.get(dep) == 1:
                return stack[stack.index(dep):] + [dep]
            if colour.get(dep) is None:
                found = visit(dep)
                if found:
                    return found
        stack.pop()
        colour[node] = 2
        return None

    for node in sorted(tasks):
        if colour.get(node) is None:
            found = visit(node)
            if found:
                return found
    return None


def check_state(state: Any, expected_repository: str | None = REPOSITORY) -> list[str]:
    errors = validate(state, load_schema("completion-state.schema.json"))
    if errors:
        return errors
    if expected_repository and state["repository"] != expected_repository:
        errors.append(f"$.repository: foreign repository {state['repository']!r}")
    tasks = state["tasks"]
    for task_id, task in tasks.items():
        for kind in ("implementationDependencies", "acceptanceDependencies"):
            for dep in task[kind]:
                if dep == task_id:
                    errors.append(f"{task_id}: depends on itself")
                elif TASK_ID.match(dep) and dep not in tasks:
                    errors.append(f"{task_id}: unknown {kind[:-12]} dependency {dep}")
        if task["scheduling"] == "ENGINEERING_VERIFIED":
            if task["implementation"] != "PRESENT":
                errors.append(f"{task_id}: ENGINEERING_VERIFIED requires implementation PRESENT")
            if task["localVerification"] not in ("PASS", "NOT_APPLICABLE_APPROVED"):
                errors.append(f"{task_id}: ENGINEERING_VERIFIED requires local verification PASS")
            if not task["evidencePaths"]:
                errors.append(f"{task_id}: ENGINEERING_VERIFIED requires evidence")
    cycle = find_cycle(tasks)
    if cycle:
        errors.append("implementation dependency cycle: " + " -> ".join(cycle))
    active = state.get("activeTaskId")
    if active is not None and active not in tasks:
        errors.append(f"$.activeTaskId: unknown task {active}")
    return errors


# ---------------------------------------------------------------- results and evidence

def resolve_evidence(root: Path, relative: str) -> Path:
    if not relative or os.path.isabs(relative) or ".." in Path(relative).parts:
        raise StateError(f"evidence path {relative!r} must be relative without '..'")
    root = Path(root).resolve()
    candidate = (root / relative)
    current = root
    for part in Path(relative).parts:
        current = current / part
        if current.is_symlink():
            raise StateError(f"evidence path {relative!r} traverses a symlink")
    resolved = candidate.resolve()
    if root not in resolved.parents and resolved != root:
        raise StateError(f"evidence path {relative!r} escapes the evidence root")
    if candidate.is_symlink():
        raise StateError(f"evidence path {relative!r} is a symlink")
    if not resolved.is_file():
        raise StateError(f"evidence path {relative!r} does not exist")
    return resolved


def read_evidence(root: Path, relative: str) -> bytes:
    """Bound reads before hashing/parsing; empty artifacts cannot substantiate completion."""
    resolve_evidence(root, relative)
    if os.open not in os.supports_dir_fd or not hasattr(os, "O_NOFOLLOW"):
        raise StateError("runner lacks safe descriptor-relative evidence access")
    # Anchor every component beneath the opened root. A rename/symlink swap after
    # resolve_evidence must not redirect the read to another directory or block on a FIFO.
    with contextlib.ExitStack() as stack:
        def open_at(name, flags, parent=None):
            fd = os.open(name, flags, dir_fd=parent)
            stack.callback(os.close, fd)
            return fd

        directory_flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
        parent = open_at(Path(root).resolve(), directory_flags)
        parts = Path(relative).parts
        for part in parts[:-1]:
            parent = open_at(part, directory_flags, parent)
        fd = open_at(parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, parent)
        info = os.fstat(fd)
        size = info.st_size
        if not stat.S_ISREG(info.st_mode) or size == 0 or size > MAX_EVIDENCE_BYTES:
            raise StateError(f"{relative}: evidence must be a regular file of 1..{MAX_EVIDENCE_BYTES} bytes")
        with os.fdopen(os.dup(fd), "rb") as stream:
            data = stream.read(MAX_EVIDENCE_BYTES + 1)
        if len(data) != size:
            raise StateError(f"{relative}: evidence changed size during read")
    return data


def evidence_digests(root: Path, paths: list[str]) -> dict:
    records = {}
    for relative in paths:
        data = read_evidence(root, relative)
        records[relative] = {"sha256": hashlib.sha256(data).hexdigest(), "sizeBytes": len(data)}
    return records


def check_retained_evidence(state: dict, root: Path) -> list[str]:
    errors = []
    for task_id, task in state["tasks"].items():
        if not implementation_satisfied(task):
            continue
        expected = task.get("evidenceDigests", {})
        for relative in task["evidencePaths"]:
            if relative not in expected:
                errors.append(f"{task_id}: {relative}: missing supervisor evidence digest")
                continue
            try:
                actual = evidence_digests(root, [relative])[relative]
                if actual != expected[relative]:
                    errors.append(f"{task_id}: {relative}: evidence hash/size mismatch")
            except (StateError, OSError) as exc:
                errors.append(f"{task_id}: {exc}")
    return errors


def check_result(result: Any, evidence_root: Path, captured: dict | None = None) -> list[str]:
    errors = validate(result, load_schema("completion-result.schema.json"))
    if errors:
        return errors
    paths = result["evidencePaths"]
    if result["verificationStatus"] == "PASS" and not paths:
        errors.append("verificationStatus PASS without evidence")
    if result["verificationStatus"] != "PASS" and result["nextAction"] == "VERIFY_ENGINEERING_COMPLETION":
        errors.append("only a passing verification can request engineering completion")
    for relative in paths:
        try:
            data_bytes = read_evidence(evidence_root, relative)
        except (StateError, OSError) as exc:
            errors.append(str(exc))
            continue
        if captured is not None:
            captured[relative] = {"sha256": hashlib.sha256(data_bytes).hexdigest(), "sizeBytes": len(data_bytes)}
        if Path(relative).suffix == ".json" and result["verificationStatus"] == "PASS":
            try:
                data = json.loads(data_bytes.decode("utf-8"))
            except (json.JSONDecodeError, UnicodeDecodeError):
                errors.append(f"{relative}: evidence JSON is corrupt")
                continue
            if isinstance(data, dict) and data.get("testsExecuted") == 0:
                errors.append(f"{relative}: zero tests executed cannot support PASS")
    return errors


# ---------------------------------------------------------------- selection

def _priority(task_id: str, task: dict) -> tuple:
    order = 0 if task_id.startswith("RUN") else 1
    return (SEVERITY_RANK.get(task.get("severity", "medium"), 2), order, task_id)


def ready_tasks(state: dict) -> list[str]:
    tasks = state["tasks"]
    ready = []
    for task_id, task in tasks.items():
        if task["scheduling"] not in ("READY", "REPAIR_REQUIRED", "BLOCKED_DEPENDENCY"):
            continue
        if task.get("blockedBy"):  # prerequisites outside the catalog keep the task parked
            continue
        deps = [tasks[d] for d in task["implementationDependencies"] if d in tasks]
        if all(implementation_satisfied(d) for d in deps):
            ready.append(task_id)
    return sorted(ready, key=lambda t: _priority(t, tasks[t]))


def engineering_complete(state: dict) -> bool:
    tasks = state["tasks"].values()
    if not all(t["scheduling"] in ("ENGINEERING_VERIFIED", "BLOCKED_EXTERNAL") and implementation_satisfied(t)
               for t in tasks):
        return False
    ci = state.get("ci") or {}
    return ci.get("status") == "PASS" and ci.get("resolvedSha") == state.get("currentSha")


def select_next(state: dict) -> dict:
    """Choose the next action. Acceptance dependencies never block implementation work."""
    tasks = state["tasks"]
    ready = ready_tasks(state)
    repairs = [t for t in ready if tasks[t]["scheduling"] == "REPAIR_REQUIRED"]
    if repairs:
        return {"taskId": repairs[0], "nextAction": "REPAIR"}
    if ready:
        task_id = ready[0]
        action = "RUN_FOCUSED_CHECK" if tasks[task_id]["implementation"] == "PRESENT" else "IMPLEMENT"
        return {"taskId": task_id, "nextAction": action}
    waiting = sorted(t for t, v in tasks.items() if v["scheduling"] == "WAITING_CI")
    if waiting or (state.get("ci") or {}).get("status") in ("QUEUED", "RUNNING"):
        return {"taskId": waiting[0] if waiting else None, "nextAction": "POLL_CI"}
    if engineering_complete(state):
        return {"taskId": None, "nextAction": "VERIFY_ENGINEERING_COMPLETION"}
    # A RUNNING checkpoint is not evidence that a child is alive or that work is externally blocked.
    # Reconcile its session/diff before dispatching another writer or declaring an external wait.
    running = sorted((t for t, v in tasks.items() if v["scheduling"] == "RUNNING"),
                     key=lambda t: _priority(t, tasks[t]))
    if running:
        active = state.get("activeTaskId")
        return {"taskId": active if active in running else running[0], "nextAction": "RESUME_REQUIRED"}
    unfinished = [v for v in tasks.values() if v["scheduling"] != "ENGINEERING_VERIFIED"]
    if unfinished and all(v["scheduling"] in ("BLOCKED_EXTERNAL", "BLOCKED_ENVIRONMENT") for v in unfinished):
        return {"taskId": None, "nextAction": "WAITING_EXTERNAL"}
    return {"taskId": None, "nextAction": "RESUME_REQUIRED"}


# ---------------------------------------------------------------- seeding from the specification

PROFESSIONAL_UNITS = {"WU-012", "WU-014"}
_UNIT = re.compile(r"^## ((?:RUN|WU)-[0-9]{3}): (.+)$", re.M)
_DEPS = re.compile(r"\*\*(?:Implementation dependencies|Dependencies):\*\*\s*([^\n]*)")
_CATEGORY = re.compile(r"\*\*\[Category\]:\*\*\s*([^\n]+)")


def parse_spec_units(spec_text: str) -> dict[str, dict]:
    units: dict[str, dict] = {}
    matches = list(_UNIT.finditer(spec_text))
    for index, match in enumerate(matches):
        body = spec_text[match.end(): matches[index + 1].start() if index + 1 < len(matches) else len(spec_text)]
        deps_match = _DEPS.search(body)
        deps = re.findall(r"(?:RUN|WU)-[0-9]{3}", deps_match.group(1)) if deps_match else []
        category = _CATEGORY.search(body)
        units[match.group(1)] = {"title": match.group(2).strip(), "deps": deps,
                                 "category": category.group(1).strip() if category else "Unspecified"}
    return units


def seed_tasks(units: dict[str, dict], external_overrides: dict[str, list[str]] | None = None) -> dict:
    """Split listed dependencies: professional-policy units move to acceptance dependencies (spec 8.1)."""
    external_overrides = external_overrides or {}
    tasks = {}
    for task_id, unit in sorted(units.items()):
        impl = [d for d in unit["deps"] if d not in PROFESSIONAL_UNITS or task_id in PROFESSIONAL_UNITS]
        acceptance = [d for d in unit["deps"] if d not in impl] + external_overrides.get(task_id, [])
        tasks[task_id] = {
            "title": unit["title"], "category": unit["category"], "severity": "high" if task_id.startswith("RUN") else "medium",
            "implementationDependencies": impl, "acceptanceDependencies": sorted(set(acceptance)),
            "implementation": "NOT_ASSESSED", "localVerification": "NOT_RUN", "browserVerification": "NOT_RUN",
            "ciVerification": "NOT_DISPATCHED", "acceptance": "PENDING", "scheduling": "READY",
            "evidencePaths": [], "notes": "",
        }
    return tasks


def iter_unmet(tasks: dict, task_id: str) -> Iterable[str]:
    for dep in tasks[task_id]["implementationDependencies"]:
        if dep in tasks and not implementation_satisfied(tasks[dep]):
            yield dep
