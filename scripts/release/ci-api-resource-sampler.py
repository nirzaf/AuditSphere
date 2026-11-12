#!/usr/bin/env python3
"""One 60-second fixed-field Linux runner sampler; no subprocesses or credentials."""
import json
import os
from pathlib import Path
import sys
import time


def counters(path, keys):
    result = {key: None for key in keys}
    try:
        for line in Path(path).read_text().splitlines():
            parts = line.replace(':', ' ').split()
            if len(parts) >= 2 and parts[0] in result and parts[1].isdigit():
                result[parts[0]] = int(parts[1])
    except (OSError, ValueError):
        pass
    return result


def sample(kind='API_RUNNER_RESOURCES'):
    # Host totals, not a claim about just the test process. Never read /proc/*/cmdline.
    result = {'kind': kind, 'sampler_pid': os.getpid(), 'unix_seconds': int(time.time()),
              'memory_kib': counters('/proc/meminfo', ('MemTotal', 'MemAvailable', 'SwapFree')),
              'cgroup_memory_events': counters('/sys/fs/cgroup/memory.events', ('low', 'high', 'max', 'oom', 'oom_kill')),
              'cgroup_cpu': counters('/sys/fs/cgroup/cpu.stat', ('usage_usec', 'nr_throttled', 'throttled_usec'))}
    try:
        disk = os.statvfs('.')
        result['disk_available_bytes'] = disk.f_bavail * disk.f_frsize
    except OSError:
        result['disk_available_bytes'] = None
    try:
        fields = Path('/proc/stat').read_text().splitlines()[0].split()
        result['host_cpu_ticks'] = [int(v) for v in fields[1:9]] if fields[0] == 'cpu' else None
    except (OSError, ValueError, IndexError):
        result['host_cpu_ticks'] = None
    # Aggregate RSS is observational and can double-count shared pages; missing PIDs race normally.
    rss = 0
    count = 0
    try:
        for entry in Path('/proc').iterdir():
            if entry.name.isdigit():
                value = counters(entry / 'status', ('VmRSS',))['VmRSS']
                if value is not None:
                    rss += value
                    count += 1
        result.update(host_process_rss_kib=rss, host_process_count=count)
    except OSError:
        result.update(host_process_rss_kib=None, host_process_count=None)
    return result


def main():
    kind = sys.argv[2] if len(sys.argv) > 2 else 'API_RUNNER_RESOURCES'
    if kind not in ('API_RUNNER_RESOURCES', 'DOTNET_BUILD_RESOURCES'):
        raise SystemExit('Unsupported resource sample kind')
    parent = os.getppid()
    while os.getppid() == parent:
        line = json.dumps(sample(kind), separators=(',', ':'), allow_nan=False) + '\n'
        try:
            # Both the command artifact and live Actions log retain the same bounded record.
            with open(sys.argv[1], 'a', encoding='utf-8') as output:
                output.write(line)
            print(line, end='', flush=True)
        except (OSError, BrokenPipeError):
            return
        time.sleep(60)


if __name__ == '__main__':
    main()
