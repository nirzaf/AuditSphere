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
[[ $1 == -k && $2 == 1s && $3 == 3s ]] || exit 91
shift 3
exec "$@"
SH
cat > "$fixture/bin/docker" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$1" >> "$DIAGNOSTIC_CALLS"
if [[ ${DIAGNOSTIC_PROBE_FAILURE:-0} == 1 ]]; then exit 125; fi
case $1 in
  inspect) printf 'builder_running=true builder_oom_killed=false builder_limit_bytes=1048576 builder_swap_limit_bytes=2097152\n' ;;
  stats) printf 'builder_memory=512KiB/1MiB builder_memory_percent=50\n' ;;
  exec) printf 'builder_memory.current=524288\nbuilder_memory.events=oom 1,oom_kill 1,\n' ;;
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
