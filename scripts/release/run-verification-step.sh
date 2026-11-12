#!/usr/bin/env bash
# Runs one command of the full release verification (.github/workflows/release-verification.yml).
#
# The complete output of the command is written to a numbered log file in VERIFICATION_LOG_DIR and
# also streamed to the step log. The command, its exact exit status and its outcome are appended to
# the job summary ($GITHUB_STEP_SUMMARY). The wrapper exits with the command's own status, so a
# failing command fails the step and no failure is converted into success.
#
# Usage: bash scripts/release/run-verification-step.sh "<label>" <command> [arguments...]
set -uo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 \"<label>\" <command> [arguments...]" >&2
  exit 2
fi
if [ -z "${VERIFICATION_LOG_DIR:-}" ]; then
  echo "VERIFICATION_LOG_DIR must name the directory for verification logs." >&2
  exit 2
fi

label="$1"
shift
command_text="$*"

mkdir -p "$VERIFICATION_LOG_DIR"
sequence=$(( $(find "$VERIFICATION_LOG_DIR" -maxdepth 1 -name '*.log' | wc -l) + 1 ))
slug="$(printf '%s' "$label" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9' '-' | sed 's/^-//; s/-$//')"
log_file="$(printf '%s/%02d-%s.log' "$VERIFICATION_LOG_DIR" "$sequence" "$slug")"

printf '### %s\n$ %s\n\n' "$label" "$command_text" | tee "$log_file"
# Only the full API gate needs runner-loss evidence. Do not change its test command,
# deadline or exit status. The sampler has no child processes and prints no command lines.
sampler_pid=
stop_sampler() {
  if [ -n "$sampler_pid" ]; then
    # A failed optional sampler may already have exited; signal only our still-running job.
    if jobs -pr | grep -qx "$sampler_pid"; then
      kill "$sampler_pid" 2>/dev/null || true
    fi
    wait "$sampler_pid" 2>/dev/null || true
    sampler_pid=
  fi
}
if [ "${TEST_PROJECT_KEY:-}" = api ] && { [ "$label" = 'Full api test project (no --filter)' ] || [ "$label" = 'Complete API suite via verified process-isolated shards' ]; }; then
  trap stop_sampler EXIT
  trap 'exit 143' TERM
  trap 'exit 130' INT
  python3 -B "$(dirname "$0")/ci-api-resource-sampler.py" "$log_file" &
  sampler_pid=$!
fi
"$@" 2>&1 | tee -a "$log_file"
status=${PIPESTATUS[0]}
stop_sampler
printf '\nexit status: %s\n' "$status" | tee -a "$log_file"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  outcome=passed
  [ "$status" -eq 0 ] || outcome=FAILED
  # shellcheck disable=SC2016 # The backticks are Markdown code spans, not command substitution.
  printf '| %s | `%s` | %s | %s |\n' "$label" "${command_text//|/\\|}" "$status" "$outcome" \
    >> "$GITHUB_STEP_SUMMARY"
fi

exit "$status"
