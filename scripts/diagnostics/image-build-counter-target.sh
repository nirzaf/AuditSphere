#!/bin/sh
# Validate one sampled compiler identity without emitting command lines or environment.
set -eu
pid=$1
expected_start=$2
case "$pid:$expected_start" in *[!0-9:]*|:*|*:) exit 65 ;; esac
process=/proc/$pid
start_ticks() {
  awk '{ line=$0; sub(/^.*\) /,"",line); split(line,f," "); print f[20] }' "$process/stat"
}
[ "$(start_ticks)" = "$expected_start" ] || exit 65
IFS= read -r name < "$process/comm"
case "$name" in
  csc) ;;
  dotnet) tr '\000' '\n' < "$process/cmdline" | grep -q '/csc\.dll$' || exit 65 ;;
  *) exit 65 ;;
esac
uid=$(awk '/^Uid:/ {print $2}' "$process/status")
inner_pid=$(awk '/^NSpid:/ {print $NF}' "$process/status")
case "$uid:$inner_pid" in *[!0-9:]*|:*|*:) exit 65 ;; esac
socket=$process/root/tmp/dotnet-diagnostic-$inner_pid-$expected_start-socket
[ -S "$socket" ] || exit 65
[ "$(stat -c %u "$socket")" = "$uid" ] || exit 65
[ "$(start_ticks)" = "$expected_start" ] || exit 65
printf 'counter_target pid=%s start_ticks=%s inner_pid=%s uid=%s\n' "$pid" "$expected_start" "$inner_pid" "$uid"
