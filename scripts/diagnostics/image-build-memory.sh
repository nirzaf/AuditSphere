#!/usr/bin/env bash
# Read-only, bounded telemetry for the existing image build. Never replace its exit status.
set -uo pipefail

if [[ $# -lt 3 || $2 != -- ]]; then
  echo 'Usage: image-build-memory.sh <buildkit-container> -- <command> [args...]' >&2
  exit 64
fi
builder_container=$1
shift 2
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
trace_dir=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/auditsphere-image-memory.XXXXXXXX" 2>/dev/null) || trace_dir=''
trace_file=${trace_dir:+$trace_dir/memory.log}
sampler_pid=''
counter_ready=false
counter_dir=${trace_dir:+$trace_dir/counters}

probe() {
  local label=$1 status
  shift
  if command -v timeout >/dev/null 2>&1 && command -v docker >/dev/null 2>&1; then
    if timeout -k 1s 3s "$@" 2>/dev/null; then
      :
    else
      status=$?
      printf '%s=unavailable probe_exit=%s\n' "$label" "$status"
    fi
  else
    printf '%s=unavailable required_probe_command_missing\n' "$label"
  fi
}

sample() {
  local phase=$1 file
  {
    printf 'IMAGE_MEMORY_SAMPLE phase=%s utc=%s\n' "$phase" "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    if [[ -r /proc/meminfo ]]; then
      awk '/^(MemTotal|MemAvailable|SwapTotal|SwapFree):/ { printf "host_%s=%s%s ", $1, $2, $3 } END { print "" }' /proc/meminfo
    else
      echo 'host_meminfo=unavailable'
    fi
    for file in memory.current memory.max memory.peak memory.events; do
      if [[ -r /sys/fs/cgroup/$file ]]; then
        printf 'runner_cgroup_%s=' "$file"
        tr '\n' ',' < "/sys/fs/cgroup/$file"
        printf '\n'
      fi
    done
    probe builder_state docker inspect --format \
      'builder_running={{.State.Running}} builder_oom_killed={{.State.OOMKilled}} builder_limit_bytes={{.HostConfig.Memory}} builder_swap_limit_bytes={{.HostConfig.MemorySwap}}' "$builder_container"
    probe builder_stats docker stats --no-stream --format 'builder_memory={{.MemUsage}} builder_memory_percent={{.MemPerc}}' "$builder_container"
    probe builder_cgroup docker exec "$builder_container" sh -c '
      for file in /proc/self/cgroup /sys/fs/cgroup/memory.current /sys/fs/cgroup/memory.max /sys/fs/cgroup/memory.peak /sys/fs/cgroup/memory.events /sys/fs/cgroup/memory/memory.usage_in_bytes /sys/fs/cgroup/memory/memory.limit_in_bytes /sys/fs/cgroup/memory/memory.max_usage_in_bytes /sys/fs/cgroup/memory/memory.failcnt; do
        if [ -r "$file" ]; then
          printf "builder_%s=" "${file##*/}"
          tr "\n" "," < "$file"
          printf "\n"
        fi
      done'
    if [[ -r $script_dir/image-build-process-memory.sh ]]; then
      probe builder_process_memory docker exec -i "$builder_container" sh -s < "$script_dir/image-build-process-memory.sh"
    else
      echo 'builder_process_memory=unavailable helper_missing'
    fi
  } >> "$trace_file" 2>&1 || :
}

sample_periodically() {
  local sleeper='' counter_pid='' count=0 running preparation_attempted=false
  stop_sampler() {
    for running in $(jobs -pr); do
      if [[ $running == "$sleeper" || $running == "$counter_pid" ]]; then
        kill "$running" 2>/dev/null || :
      fi
    done
    if [[ -n $sleeper ]]; then wait "$sleeper" 2>/dev/null || :; fi
    if [[ -n $counter_pid ]]; then wait "$counter_pid" 2>/dev/null || :; fi
    exit 0
  }
  trap stop_sampler TERM INT
  # At most 179 periodic samples, plus start/end, within the existing 45-minute job.
  while [[ $count -lt 179 ]]; do
    sleep 15 &
    sleeper=$!
    wait "$sleeper" 2>/dev/null || :
    sleeper=''
    sample periodic
    if [[ $preparation_attempted == false ]] &&
       command -v python3 >/dev/null 2>&1 && command -v timeout >/dev/null 2>&1 &&
       awk '/^IMAGE_MEMORY_SAMPLE / { ready=0 } /^builder_running=true / { ready=1 } END { exit !ready }' "$trace_file"; then
      # Buildx starts its container lazily during the unchanged build command.
      # Prepare once, only after a current read-only sample observes it running.
      preparation_attempted=true
      timeout -k 1s 60s python3 "$script_dir/image-build-counters.py" prepare "$counter_dir" "$builder_container" >> "$trace_file" 2>/dev/null &
      counter_pid=$!
      if wait "$counter_pid" 2>/dev/null; then counter_ready=true; fi
      counter_pid=''
    fi
    if [[ $counter_ready == true ]]; then
      # At most four growth-stage attempts. Capture failure never replaces the build result.
      python3 "$script_dir/image-build-counters.py" capture "$counter_dir" "$trace_file" >> "$trace_file" 2>/dev/null &
      counter_pid=$!
      wait "$counter_pid" 2>/dev/null || :
      counter_pid=''
    fi
    count=$((count + 1))
  done
}

cleanup() {
  local status=$? running
  trap - EXIT
  if [[ -n $sampler_pid ]]; then
    # Check the shell's own running jobs before signalling, rather than a potentially reused PID.
    for running in $(jobs -pr); do
      if [[ $running == "$sampler_pid" ]]; then
        kill "$sampler_pid" 2>/dev/null || :
      fi
    done
    wait "$sampler_pid" 2>/dev/null || :
  fi
  if [[ -n $trace_dir ]]; then
    sample end
    if [[ -d $counter_dir ]]; then
      python3 "$script_dir/image-build-counters.py" cleanup "$counter_dir" >> "$trace_file" 2>/dev/null || :
    fi
    cat "$trace_file" || :
    rm -f "$trace_file" || :
    rmdir "$trace_dir" 2>/dev/null || :
  fi
  exit "$status"
}
trap cleanup EXIT

if [[ -n $trace_dir ]]; then
  # Source identity is safe to emit; arbitrary environment values and command arguments are not.
  source_identity=${GITHUB_SHA:-}
  if [[ ! $source_identity =~ ^[a-fA-F0-9]{40,64}$ ]]; then source_identity=unavailable; fi
  printf 'IMAGE_MEMORY_SOURCE sha=%s\n' "$source_identity" > "$trace_file"
  sample start
  sample_periodically &
  sampler_pid=$!
else
  echo 'IMAGE_MEMORY_DIAGNOSTICS_UNAVAILABLE temporary_file_creation_failed' >&2
fi

if "$@"; then
  build_status=0
else
  build_status=$?
fi
exit "$build_status"
