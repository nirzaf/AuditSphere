#!/bin/sh
# Fixed-call metadata only. Never emit paths, raw errors, process arguments or environment.
set -eu
pid=$1
expected_start=$2
inner_pid=$3
case "$pid:$expected_start:$inner_pid" in *[!0-9:]*|:*|*:|*::*) exit 65 ;; esac
ticks() {
  awk '{ line=$0; sub(/^.*\) /,"",line); split(line,f," "); print f[20] }' "/proc/$pid/stat" 2>/dev/null
}
[ "$(ticks)" = "$expected_start" ] || exit 65
classify_error() {
  case "$1" in
    *'Permission denied'*|*'Operation not permitted'*) printf permission_denied ;;
    *'No such file or directory'*) printf missing ;;
    *'Read-only file system'*) printf read_only_filesystem ;;
    *) printf other ;;
  esac
}
stat_call() {
  if message=$(LC_ALL=C stat -L -c '%u %a' -- "$2" 2>&1); then
    printf '%s_exit=0\n%s_result=accessible\n' "$1" "$1"
  else
    code=$?
    printf '%s_exit=%s\n%s_result=%s\n' "$1" "$code" "$1" "$(classify_error "$message")"
  fi
}
uid_value() {
  value=$(awk '/^Uid:/ {print $2}' "$1" 2>/dev/null) || value=''
  case "$value" in ''|*[!0-9]*) printf unavailable ;; *) printf '%s' "$value" ;; esac
}
printf 'probe_version=1\ntarget_pid=%s\ntarget_start_ticks=%s\n' "$pid" "$expected_start"
printf 'observer_uid=%s\ntarget_uid=%s\n' "$(uid_value /proc/self/status)" "$(uid_value "/proc/$pid/status")"
stat_call root_stat "/proc/$pid/root"
stat_call socket_stat "/proc/$pid/root/tmp/dotnet-diagnostic-$inner_pid-$expected_start-socket"
# mktemp performs exclusive creation; no existing regular file, FIFO or symlink is opened.
if message=$(LC_ALL=C mktemp /output/counter-access-probe.XXXXXX 2>&1); then
  created=$message
  case "$created" in /output/counter-access-probe.??????) ;; *) exit 65 ;; esac
  printf 'output_write_exit=0\noutput_write_result=accessible\n'
  if rm -- "$created" 2>/dev/null; then
    printf 'output_cleanup=removed\n'
  else
    printf 'output_cleanup=unavailable\n'
  fi
else
  code=$?
  printf 'output_write_exit=%s\noutput_write_result=%s\noutput_cleanup=not_created\n' "$code" "$(classify_error "$message")"
fi
[ "$(ticks)" = "$expected_start" ] || exit 65
