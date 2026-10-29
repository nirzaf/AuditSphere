#!/bin/sh
# Read-only process counters. A supplied proc root is for an owned filesystem fixture.
set -u
proc_root=${1:-/proc}
matched=0
for process in "$proc_root"/[0-9]*; do
  [ -r "$process/comm" ] && [ -r "$process/status" ] || continue
  IFS= read -r name < "$process/comm" || continue
  case "$name" in dotnet|csc|VBCSCompiler) ;; *) continue ;; esac
  pid=${process##*/}
  case "$pid" in ''|*[!0-9]*) continue ;; esac
  kind=dotnet
  # The arguments are inspected only for classification and never emitted.
  if [ -r "$process/cmdline" ] && tr '\000' '\n' < "$process/cmdline" | grep -q '/csc\.dll$'; then
    kind=csc
  elif [ "$name" = csc ]; then
    kind=csc
  elif [ "$name" = VBCSCompiler ]; then
    kind=compiler_server
  fi
  start_ticks=unavailable
  if [ -r "$process/stat" ]; then
    start_ticks=$(awk '{ line=$0; sub(/^.*\) /,"",line); split(line,fields," "); print fields[20] }' "$process/stat")
    case "$start_ticks" in ''|*[!0-9]*) start_ticks=unavailable ;; esac
  fi
  printf 'builder_process kind=%s pid=%s start_ticks=%s\n' "$kind" "$pid" "$start_ticks"
  awk '/^(VmRSS|VmHWM|VmSwap|RssAnon|RssFile|VmSize|Threads):/ && $2 ~ /^[0-9]+$/ {
    key=$1; sub(/:$/, "", key); printf "builder_process_%s=%s\n", key, $2
  }' "$process/status" || echo 'builder_process_status=unavailable'
  if [ -r "$process/smaps_rollup" ]; then
    awk '/^(Rss|Pss|Pss_Anon|Pss_File|Private_Dirty|Anonymous|Swap|SwapPss):/ && $2 ~ /^[0-9]+$/ {
      key=$1; sub(/:$/, "", key); printf "builder_process_smaps_%s=%s\n", key, $2
    }' "$process/smaps_rollup" || echo 'builder_process_smaps=unavailable'
  else
    echo 'builder_process_smaps=unavailable'
  fi
  matched=$((matched + 1))
  if [ "$matched" -ge 8 ]; then break; fi
done
if [ "$matched" -eq 0 ]; then
  echo 'builder_process_memory=not_observed'
else
  printf 'builder_process_memory=observed count=%s limit=8\n' "$matched"
fi
