/**
 * Foreground staff timer (AS-PM8-02B). A pure model: it never reads the clock, schedules work, stores anything or calls
 * the server. The caller supplies every timestamp, and the result only proposes separate whole-minute time-draft rows
 * that a person reviews and saves one by one through the existing time-draft command. Paused time is never worked time,
 * and nothing here applies a billing-rounding policy: partial minutes are reported as excluded, not rounded up.
 */
export type TrackerPhase = 'IDLE' | 'RUNNING' | 'PAUSED' | 'STOPPED';
export type TrackerInterval = Readonly<{ startUtcMs: number; endUtcMs: number }>;
export type TrackerState = Readonly<{
  phase: TrackerPhase;
  taskId: string | null;
  startedUtcMs: number | null;
  intervals: readonly TrackerInterval[];
}>;
export type TrackerAction =
  | { kind: 'START'; taskId: string; atUtcMs: number }
  | { kind: 'PAUSE' | 'RESUME' | 'STOP'; atUtcMs: number }
  | { kind: 'DISCARD' };
export type TrackerResult =
  | { ok: true; state: TrackerState }
  | { ok: false; code: 'timer.invalid' | 'timer.state' | 'timer.limit'; message: string; state: TrackerState };
export type TrackerDraft = Readonly<{
  segment: number;
  workDate: string;
  startMinute: number;
  durationMinutes: number;
}>;

type RefusalCode = 'timer.invalid' | 'timer.state' | 'timer.limit';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
const ISO_FOUR_DIGIT_YEAR = /^\d{4}-/;
const MINUTE_MS = 60_000;
const DAY_MS = 86_400_000;
const MAX_INTERVALS = 32;
const MAX_ACTIVE_MS = 86_400_000;

const REFUSAL_MESSAGES: Readonly<Record<RefusalCode, string>> = {
  'timer.invalid': 'Choose a valid task and timestamp.',
  'timer.state': 'The timer action does not match its current state.',
  'timer.limit': 'Stop and review this timer before recording more time.',
};

export function emptyTracker(): TrackerState {
  return { phase: 'IDLE', taskId: null, startedUtcMs: null, intervals: [] };
}

/** Applies one timer action. Every refusal returns the supplied state itself, unchanged. */
export function applyTracker(state: TrackerState, action: TrackerAction): TrackerResult {
  if (action?.kind === 'DISCARD') return { ok: true, state: emptyTracker() };
  if (!action || !isTimestamp(action.atUtcMs)) return refuse('timer.invalid', state);

  switch (action.kind) {
    case 'START': {
      const taskId = typeof action.taskId === 'string' && GUID.test(action.taskId) ? action.taskId.toLowerCase() : null;
      if (taskId === null || taskId === EMPTY_GUID) return refuse('timer.invalid', state);
      if (state.phase !== 'IDLE') return refuse('timer.state', state);
      return { ok: true, state: { phase: 'RUNNING', taskId, startedUtcMs: action.atUtcMs, intervals: [] } };
    }
    case 'PAUSE':
      if (state.phase !== 'RUNNING') return refuse('timer.state', state);
      return closeActiveInterval(state, action.atUtcMs, 'PAUSED');
    case 'RESUME': {
      if (state.phase !== 'PAUSED') return refuse('timer.state', state);
      const lastEnd = state.intervals.at(-1)?.endUtcMs;
      if (lastEnd !== undefined && action.atUtcMs < lastEnd) return refuse('timer.state', state);
      return {
        ok: true,
        state: { phase: 'RUNNING', taskId: state.taskId, startedUtcMs: action.atUtcMs, intervals: state.intervals },
      };
    }
    case 'STOP':
      if (state.phase === 'STOPPED') return { ok: true, state };
      if (state.phase === 'RUNNING') return closeActiveInterval(state, action.atUtcMs, 'STOPPED');
      if (state.phase === 'PAUSED') {
        return {
          ok: true,
          state: { phase: 'STOPPED', taskId: state.taskId, startedUtcMs: null, intervals: state.intervals },
        };
      }
      return refuse('timer.state', state);
    default:
      return refuse('timer.invalid', state);
  }
}

/**
 * Proposes one draft row per stopped interval piece. Each interval is split at every UTC midnight, and each piece is
 * shrunk inward to whole UTC minutes (start up, end down), so rows stay within one work date and never overlap.
 * `excludedMilliseconds` is the exact active time those rows leave out; paused time is never part of it.
 */
export function draftSegments(state: TrackerState): { rows: readonly TrackerDraft[]; excludedMilliseconds: number } {
  if (state.phase !== 'STOPPED') return { rows: [], excludedMilliseconds: 0 };

  const rows: TrackerDraft[] = [];
  let activeMilliseconds = 0;
  let emittedMinutes = 0;
  for (const interval of state.intervals) {
    activeMilliseconds += interval.endUtcMs - interval.startUtcMs;
    let pieceStart = interval.startUtcMs;
    while (pieceStart < interval.endUtcMs) {
      const pieceEnd = Math.min(interval.endUtcMs, floorTo(pieceStart, DAY_MS) + DAY_MS);
      const roundedStart = ceilTo(pieceStart, MINUTE_MS);
      const roundedEnd = floorTo(pieceEnd, MINUTE_MS);
      if (roundedEnd > roundedStart) {
        const startedAt = new Date(roundedStart);
        const durationMinutes = (roundedEnd - roundedStart) / MINUTE_MS;
        rows.push({
          segment: rows.length + 1,
          workDate: startedAt.toISOString().slice(0, 10),
          startMinute: startedAt.getUTCHours() * 60 + startedAt.getUTCMinutes(),
          durationMinutes,
        });
        emittedMinutes += durationMinutes;
      }
      pieceStart = pieceEnd;
    }
  }
  return { rows, excludedMilliseconds: activeMilliseconds - emittedMinutes * MINUTE_MS };
}

/** Closes `[startedUtcMs, endUtcMs)`, dropping a zero-length interval and joining only an exactly adjacent one. */
function closeActiveInterval(state: TrackerState, endUtcMs: number, phase: 'PAUSED' | 'STOPPED'): TrackerResult {
  const startUtcMs = state.startedUtcMs;
  if (startUtcMs === null || endUtcMs < startUtcMs) return refuse('timer.state', state);

  const intervals = state.intervals.slice();
  if (endUtcMs > startUtcMs) {
    const last = intervals.at(-1);
    if (last !== undefined && last.endUtcMs === startUtcMs) {
      intervals[intervals.length - 1] = { startUtcMs: last.startUtcMs, endUtcMs };
    } else {
      intervals.push({ startUtcMs, endUtcMs });
    }
  }
  const activeMilliseconds = intervals.reduce((total, x) => total + (x.endUtcMs - x.startUtcMs), 0);
  if (intervals.length > MAX_INTERVALS || activeMilliseconds > MAX_ACTIVE_MS) return refuse('timer.limit', state);
  return { ok: true, state: { phase, taskId: state.taskId, startedUtcMs: null, intervals } };
}

/** A nonnegative safe-integer UTC instant whose ISO year has exactly four digits. */
function isTimestamp(value: unknown): value is number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) return false;
  const instant = new Date(value);
  return !Number.isNaN(instant.getTime()) && ISO_FOUR_DIGIT_YEAR.test(instant.toISOString());
}

function floorTo(milliseconds: number, unit: number): number {
  return milliseconds - (milliseconds % unit);
}

function ceilTo(milliseconds: number, unit: number): number {
  return floorTo(milliseconds + unit - 1, unit);
}

function refuse(code: RefusalCode, state: TrackerState): TrackerResult {
  return { ok: false, code, message: REFUSAL_MESSAGES[code], state };
}
