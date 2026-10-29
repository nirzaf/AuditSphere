#!/usr/bin/env bash
# Focused contract checks use an owned fake Docker adapter; no daemon, image build or credentials.
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
wrapper=${1:-$script_dir/image-build-memory.sh}
fixture=$(mktemp -d "${TMPDIR:-/tmp}/auditsphere-memory-test.XXXXXXXX")
trap 'rm -rf "$fixture"' EXIT
mkdir "$fixture/bin" "$fixture/temp"
cat > "$fixture/bin/timeout" <<'SH'
#!/usr/bin/env bash
[[ $1 == -k && $2 == 1s ]] || exit 91
if [[ $3 != 3s ]]; then
  [[ $3 == 60s && ${4:-} == python3 && ${5##*/} == image-build-counters.py && ${6:-} == prepare ]] || exit 91
fi
shift 3
exec "$@"
SH
cat > "$fixture/bin/docker" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$1" >> "$DIAGNOSTIC_CALLS"
if [[ ${DIAGNOSTIC_PROBE_FAILURE:-0} == 1 ]]; then exit 125; fi
case $1 in
  inspect)
    if [[ ${DIAGNOSTIC_LAZY_BUILDER:-0} == 1 && ! -s $BUILD_CALLS ]]; then exit 1; fi
    printf 'builder_running=true builder_oom_killed=false builder_limit_bytes=1048576 builder_swap_limit_bytes=2097152\n' ;;
  stats) printf 'builder_memory=512KiB/1MiB builder_memory_percent=50\n' ;;
  exec)
    if [[ ${2:-} == -i ]]; then
      sh -s -- "$DIAGNOSTIC_PROC_FIXTURE"
    else
      printf 'builder_memory.current=524288\nbuilder_memory.events=oom 1,oom_kill 1,\n'
    fi ;;
  *) exit 92 ;;
esac
SH
cat > "$fixture/build" <<'SH'
#!/usr/bin/env bash
printf 'invoked\n' >> "$BUILD_CALLS"
printf 'BUILD_STDOUT_PRESERVED\n'
printf 'BUILD_STDERR_PRESERVED\n' >&2
exit "$1"
SH
chmod +x "$fixture/bin/timeout" "$fixture/bin/docker" "$fixture/build"
export PATH="$fixture/bin:$PATH" DIAGNOSTIC_CALLS="$fixture/probes" BUILD_CALLS="$fixture/build-calls"
export RUNNER_TEMP="$fixture/temp" GITHUB_SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
export DIAGNOSTIC_SECRET_SENTINEL='not-for-telemetry'
export DIAGNOSTIC_PROC_FIXTURE="$fixture/proc"
mkdir -p "$DIAGNOSTIC_PROC_FIXTURE/101" "$fixture/empty-proc"
printf 'dotnet\n' > "$DIAGNOSTIC_PROC_FIXTURE/101/comm"
printf '/usr/share/dotnet/dotnet\000/usr/share/dotnet/sdk/10.0.300/Roslyn/bincore/csc.dll\000private-command-argument-not-for-telemetry\000' > "$DIAGNOSTIC_PROC_FIXTURE/101/cmdline"
{
  printf '101 (dotnet compiler) S'
  for unused in {1..18}; do printf ' 0'; done
  printf ' 7001 0 0\n'
} > "$DIAGNOSTIC_PROC_FIXTURE/101/stat"
printf 'VmRSS:\t120 kB\nVmHWM:\t140 kB\nVmSwap:\t7 kB\nRssAnon:\t100 kB\nRssFile:\t20 kB\nThreads:\t3\nSecret:\tnot-for-telemetry\n' > "$DIAGNOSTIC_PROC_FIXTURE/101/status"
printf 'Rss: 120 kB\nPss_Anon: 99 kB\nPss_File: 19 kB\nSwap: 7 kB\nSecret: not-for-telemetry\n' > "$DIAGNOSTIC_PROC_FIXTURE/101/smaps_rollup"

assert_case() {
  local expected=$1 name=$2 actual
  shift 2
  : > "$BUILD_CALLS"
  : > "$DIAGNOSTIC_CALLS"
  if bash "$wrapper" buildx_buildkit_fixture0 -- "$@" > "$fixture/output" 2>&1; then actual=0; else actual=$?; fi
  [[ $actual == "$expected" ]] || { echo "FAIL $name: expected exit $expected, got $actual"; cat "$fixture/output"; exit 1; }
  [[ ! -n $(find "$fixture/temp" -mindepth 1 -print -quit) ]] || { echo "FAIL $name: telemetry files remain"; exit 1; }
  if [[ $name != missing-command ]]; then
    [[ $(wc -l < "$BUILD_CALLS" | tr -d ' ') == 1 ]] || { echo "FAIL $name: build not invoked exactly once"; exit 1; }
    grep -q BUILD_STDOUT_PRESERVED "$fixture/output"
    grep -q BUILD_STDERR_PRESERVED "$fixture/output"
  fi
  if [[ $name == temporary-file-unavailable ]]; then
    grep -q IMAGE_MEMORY_DIAGNOSTICS_UNAVAILABLE "$fixture/output"
  else
    grep -q 'IMAGE_MEMORY_SAMPLE phase=start' "$fixture/output" || { echo "FAIL $name: start memory sample missing"; exit 1; }
    grep -q 'IMAGE_MEMORY_SAMPLE phase=end' "$fixture/output" || { echo "FAIL $name: end memory sample missing"; exit 1; }
    if [[ ${DIAGNOSTIC_PROBE_FAILURE:-0} == 1 ]]; then
      grep -q 'unavailable probe_exit=125' "$fixture/output"
    else
      grep -q 'builder_limit_bytes=1048576' "$fixture/output" || { echo "FAIL $name: builder limit missing"; exit 1; }
      grep -q 'builder_memory.events=oom 1,oom_kill 1,' "$fixture/output" || { echo "FAIL $name: builder memory events missing"; exit 1; }
      grep -q 'builder_process kind=csc pid=101 start_ticks=7001' "$fixture/output" || { echo "FAIL $name: compiler process identity missing"; exit 1; }
      grep -q 'builder_process_VmRSS=120' "$fixture/output" || { echo "FAIL $name: compiler process RSS missing"; exit 1; }
      grep -q 'builder_process_smaps_Pss_Anon=99' "$fixture/output" || { echo "FAIL $name: compiler anonymous counter missing"; exit 1; }
    fi
  fi
  if grep -q "$DIAGNOSTIC_SECRET_SENTINEL" "$fixture/output"; then echo "FAIL $name: environment value disclosed"; exit 1; fi
  if grep -q 'private-command-argument-not-for-telemetry' "$fixture/output"; then echo "FAIL $name: command argument disclosed"; exit 1; fi
  echo "PASS $name exit=$actual"
}

assert_case 0 success "$fixture/build" 0 'private-command-argument-not-for-telemetry'
assert_case 102 build-failure "$fixture/build" 102
assert_case 137 compiler-kill-status "$fixture/build" 137
assert_case 127 missing-command "$fixture/does-not-exist"
export DIAGNOSTIC_PROBE_FAILURE=1
assert_case 102 probe-failure-does-not-mask-build "$fixture/build" 102
unset DIAGNOSTIC_PROBE_FAILURE
touch "$fixture/not-a-directory"
export RUNNER_TEMP="$fixture/not-a-directory"
assert_case 102 temporary-file-unavailable "$fixture/build" 102

sh "$script_dir/image-build-process-memory.sh" "$fixture/empty-proc" > "$fixture/process-output"
grep -qx 'builder_process_memory=not_observed' "$fixture/process-output"
echo 'PASS process-parser-empty-is-not-observed'
for pid in {102..112}; do cp -R "$DIAGNOSTIC_PROC_FIXTURE/101" "$DIAGNOSTIC_PROC_FIXTURE/$pid"; done
sh "$script_dir/image-build-process-memory.sh" "$DIAGNOSTIC_PROC_FIXTURE" > "$fixture/process-output"
[[ $(grep -c '^builder_process kind=' "$fixture/process-output") == 8 ]]
grep -qx 'builder_process_memory=observed count=8 limit=8' "$fixture/process-output"
echo 'PASS process-parser-bounded-to-eight'
printf 'not-a-start-tick\n' > "$DIAGNOSTIC_PROC_FIXTURE/101/stat"
printf 'VmRSS: not-a-number\nThreads: not-for-telemetry\n' > "$DIAGNOSTIC_PROC_FIXTURE/101/status"
sh "$script_dir/image-build-process-memory.sh" "$DIAGNOSTIC_PROC_FIXTURE" > "$fixture/process-output"
grep -q 'pid=101 start_ticks=unavailable' "$fixture/process-output"
if grep -q 'not-a-number\|not-for-telemetry\|private-command-argument-not-for-telemetry' "$fixture/process-output"; then
  echo 'FAIL process-parser: untrusted field or arguments disclosed'; exit 1
fi
echo 'PASS process-parser-invalid-fields-not-emitted'

# Keep all earlier cases intact; exercise owning-sampler cancellation with optional capture active.
cp "$DIAGNOSTIC_PROC_FIXTURE/102/stat" "$DIAGNOSTIC_PROC_FIXTURE/101/stat"
cp "$DIAGNOSTIC_PROC_FIXTURE/102/status" "$DIAGNOSTIC_PROC_FIXTURE/101/status"
# The active-capture contract needs one compiler; the preceding eight-process bound is already checked.
mkdir "$fixture/active-proc"
cp -R "$DIAGNOSTIC_PROC_FIXTURE/101" "$fixture/active-proc/101"
export DIAGNOSTIC_PROC_FIXTURE="$fixture/active-proc"
export RUNNER_TEMP="$fixture/temp"
export COUNTER_CHILD_RECORD="$fixture/counter-child"
cat > "$fixture/bin/python3" <<'SH'
#!/usr/bin/env bash
case ${2:-} in
  prepare)
    if [[ ${DIAGNOSTIC_LAZY_BUILDER:-0} == 1 ]]; then
      if [[ ! -s $BUILD_CALLS ]]; then printf 'early\n' >> "$PREPARATION_CALLS"; exit 1; fi
      printf 'during-build\n' >> "$PREPARATION_CALLS"
    fi
    mkdir "$3"
    if [[ ${COUNTER_PREPARATION_BLOCK:-0} == 1 ]]; then
      sleep 20 &
      child=$!
      printf '%s\n' "$child" > "$COUNTER_CHILD_RECORD"
      trap 'kill "$child" 2>/dev/null || :; wait "$child" 2>/dev/null || :; echo COUNTER_PREPARATION_CLEANED; exit 1' TERM INT
      wait "$child"
    fi
    printf 'IMAGE_MANAGED_COUNTERS {"status":"ready_fixture"}\n' ;;
  capture)
    if [[ ${COUNTER_FAKE_FAILURE:-0} == 1 ]]; then
      printf 'IMAGE_MANAGED_COUNTERS {"status":"unavailable","reason":"fixture_failed"}\n'; exit 1
    fi
    sleep 20 &
    child=$!
    printf '%s\n' "$child" > "$COUNTER_CHILD_RECORD"
    trap 'kill "$child" 2>/dev/null || :; wait "$child" 2>/dev/null || :; echo COUNTER_FIXTURE_CLEANED; exit 0' TERM INT
    wait "$child" ;;
  cleanup) rmdir "$3"; printf 'IMAGE_MANAGED_COUNTERS {"status":"cleaned_fixture"}\n' ;;
  *) exit 93 ;;
esac
SH
cat > "$fixture/bin/sleep" <<'SH'
#!/usr/bin/env bash
if [[ $1 == 15 ]]; then exec /bin/sleep 0.01; fi
exec /bin/sleep "$@"
SH
cat > "$fixture/timed-build" <<'SH'
#!/usr/bin/env bash
printf 'invoked\n' >> "$BUILD_CALLS"
printf 'BUILD_STDOUT_PRESERVED\n'
printf 'BUILD_STDERR_PRESERVED\n' >&2
sleep 0.5
exit "$1"
SH
chmod +x "$fixture/bin/python3" "$fixture/bin/sleep" "$fixture/timed-build"
for status in 0 102 137; do
  assert_case "$status" "active-counter-owned-cleanup-$status" "$fixture/timed-build" "$status"
  grep -q COUNTER_FIXTURE_CLEANED "$fixture/output" || { echo 'FAIL active counter cleanup not observed'; exit 1; }
  if kill -0 "$(cat "$COUNTER_CHILD_RECORD")" 2>/dev/null; then echo 'FAIL owned counter child remains'; exit 1; fi
done
export COUNTER_FAKE_FAILURE=1
assert_case 102 optional-counter-failure-does-not-mask-build "$fixture/timed-build" 102
grep -q '"status":"unavailable","reason":"fixture_failed"' "$fixture/output"
unset COUNTER_FAKE_FAILURE
export DIAGNOSTIC_LAZY_BUILDER=1 PREPARATION_CALLS="$fixture/preparation-calls"
: > "$PREPARATION_CALLS"
assert_case 0 lazy-builder-preparation-after-observed-start "$fixture/timed-build" 0
[[ $(cat "$PREPARATION_CALLS") == during-build ]] || { echo 'FAIL lazy builder: preparation must run exactly once during the build'; exit 1; }
grep -q COUNTER_FIXTURE_CLEANED "$fixture/output" || { echo 'FAIL lazy builder: counter never started after readiness'; exit 1; }
unset DIAGNOSTIC_LAZY_BUILDER
export COUNTER_PREPARATION_BLOCK=1
assert_case 102 active-preparation-owned-cleanup "$fixture/timed-build" 102
grep -q COUNTER_PREPARATION_CLEANED "$fixture/output" || { echo 'FAIL owned preparation cleanup not observed'; exit 1; }
if kill -0 "$(cat "$COUNTER_CHILD_RECORD")" 2>/dev/null; then echo 'FAIL owned preparation child remains'; exit 1; fi
