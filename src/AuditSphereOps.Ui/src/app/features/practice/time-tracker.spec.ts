import { describe, expect, it } from 'vitest';
import { emptyTracker, applyTracker, draftSegments } from './time-tracker';

type State = ReturnType<typeof emptyTracker>;
type Action = Parameters<typeof applyTracker>[1];

const TASK = '3C2D3786-43BB-4353-9BF7-9710C54F5315';
const TASK_LOWER = '3c2d3786-43bb-4353-9bf7-9710c54f5315';
const INVALID = { code: 'timer.invalid', message: 'Choose a valid task and timestamp.' } as const;
const WRONG_STATE = { code: 'timer.state', message: 'The timer action does not match its current state.' } as const;
const LIMIT = { code: 'timer.limit', message: 'Stop and review this timer before recording more time.' } as const;
const MINUTE = 60_000;
const DAY = 86_400_000;

const at = (iso: string): number => Date.parse(iso);

function run(...actions: Action[]): State {
  let state = emptyTracker();
  for (const action of actions) {
    const result = applyTracker(state, action);
    if (!result.ok) throw new Error(`${result.code}: ${result.message}`);
    state = result.state;
  }
  return state;
}

function deepFreeze<T>(value: T): T {
  if (value !== null && typeof value === 'object' && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const child of Object.values(value)) deepFreeze(child);
  }
  return value;
}

function expectRefusal(state: State, action: Action, refusal: { code: string; message: string }): void {
  const before = JSON.stringify(state);
  const result = applyTracker(state, action);
  expect(result).toEqual({ ok: false, ...refusal, state });
  expect(result.state).toBe(state);
  expect(JSON.stringify(state)).toBe(before);
}

/** Replaces clock, timer, network, storage and randomness entry points with recorders for the duration of `body`. */
function withForbiddenEffects(body: () => void): string[] {
  const calls: string[] = [];
  const g = globalThis as unknown as Record<string, unknown>;
  const targets: [object, string][] = [
    [globalThis, 'setTimeout'],
    [globalThis, 'setInterval'],
    [globalThis, 'queueMicrotask'],
    [globalThis, 'requestAnimationFrame'],
    [globalThis, 'fetch'],
    [globalThis, 'XMLHttpRequest'],
    [globalThis, 'WebSocket'],
    [Date, 'now'],
    [Math, 'random'],
  ];
  if (g['performance']) targets.push([g['performance'] as object, 'now']);
  if (g['crypto']) targets.push([g['crypto'] as object, 'randomUUID']);
  const storage = g['Storage'] as { prototype: object } | undefined;
  if (storage) for (const method of ['getItem', 'setItem', 'removeItem', 'clear']) targets.push([storage.prototype, method]);
  // Stub where each member is defined, so a Storage instance's named-property setter is never invoked.
  const saved = targets.map(([target, key]) => {
    let owner: object | null = target;
    while (owner !== null && !Object.prototype.hasOwnProperty.call(owner, key)) owner = Object.getPrototypeOf(owner);
    const resolved = owner ?? target;
    return { owner: resolved, key, descriptor: Object.getOwnPropertyDescriptor(resolved, key) };
  });
  for (const { owner, key } of saved) {
    Object.defineProperty(owner, key, {
      configurable: true,
      writable: true,
      value: () => {
        calls.push(key);
        throw new Error(`${key} must not be used by the timer model`);
      },
    });
  }
  try {
    body();
  } finally {
    for (const { owner, key, descriptor } of saved) {
      if (descriptor) Object.defineProperty(owner, key, descriptor);
      else delete (owner as Record<string, unknown>)[key];
    }
  }
  return calls;
}

describe('emptyTracker', () => {
  it('starts idle with no task, start or intervals, as a fresh value each time', () => {
    const first = emptyTracker();
    expect(first).toEqual({ phase: 'IDLE', taskId: null, startedUtcMs: null, intervals: [] });
    expect(emptyTracker()).not.toBe(first);
  });
});

describe('draftSegments', () => {
  it('drafts each active interval separately and leaves the pause out', () => {
    const state = run(
      { kind: 'START', taskId: TASK, atUtcMs: at('2026-10-15T09:00:00Z') },
      { kind: 'PAUSE', atUtcMs: at('2026-10-15T09:10:00Z') },
      { kind: 'RESUME', atUtcMs: at('2026-10-15T09:20:00Z') },
      { kind: 'STOP', atUtcMs: at('2026-10-15T09:25:00Z') },
    );

    expect(state.taskId).toBe(TASK_LOWER);
    const drafts = draftSegments(state);
    expect(drafts).toEqual({
      rows: [
        { segment: 1, workDate: '2026-10-15', startMinute: 540, durationMinutes: 10 },
        { segment: 2, workDate: '2026-10-15', startMinute: 560, durationMinutes: 5 },
      ],
      excludedMilliseconds: 0,
    });
    expect(drafts.rows.reduce((total, row) => total + row.durationMinutes, 0)).toBe(15);
    expect(drafts.rows.some((row) => row.startMinute < 560 && row.startMinute + row.durationMinutes > 550)).toBe(false);
  });

  it('rounds the start up and the end down to whole UTC minutes and reports the excluded remainder', () => {
    const state = run(
      { kind: 'START', taskId: TASK, atUtcMs: at('2026-10-15T09:00:20Z') },
      { kind: 'STOP', atUtcMs: at('2026-10-15T09:10:40Z') },
    );

    expect(draftSegments(state)).toEqual({
      rows: [{ segment: 1, workDate: '2026-10-15', startMinute: 541, durationMinutes: 9 }],
      excludedMilliseconds: 80_000,
    });
  });

  it('splits an interval at UTC midnight into rows on different work dates', () => {
    const state = run(
      { kind: 'START', taskId: TASK, atUtcMs: at('2026-10-15T23:58:00Z') },
      { kind: 'STOP', atUtcMs: at('2026-10-16T00:02:00Z') },
    );

    expect(draftSegments(state)).toEqual({
      rows: [
        { segment: 1, workDate: '2026-10-15', startMinute: 1438, durationMinutes: 2 },
        { segment: 2, workDate: '2026-10-16', startMinute: 0, durationMinutes: 2 },
      ],
      excludedMilliseconds: 0,
    });
  });

  it('emits no draft for a twenty-second segment and reports it as excluded', () => {
    const state = run(
      { kind: 'START', taskId: TASK, atUtcMs: at('2026-10-15T09:00:00Z') },
      { kind: 'STOP', atUtcMs: at('2026-10-15T09:00:20Z') },
    );

    expect(draftSegments(state)).toEqual({ rows: [], excludedMilliseconds: 20_000 });
  });

  it('keeps a full UTC day within the existing one-date time-entry bounds', () => {
    const start = at('2026-10-15T00:00:00Z');
    const state = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'STOP', atUtcMs: start + DAY });

    expect(draftSegments(state)).toEqual({
      rows: [{ segment: 1, workDate: '2026-10-15', startMinute: 0, durationMinutes: 1440 }],
      excludedMilliseconds: 0,
    });
  });

  it('never bridges a pause gap, even one shorter than a minute', () => {
    const state = run(
      { kind: 'START', taskId: TASK, atUtcMs: at('2026-10-15T09:00:00Z') },
      { kind: 'PAUSE', atUtcMs: at('2026-10-15T09:10:20Z') },
      { kind: 'RESUME', atUtcMs: at('2026-10-15T09:10:50Z') },
      { kind: 'STOP', atUtcMs: at('2026-10-15T09:20:00Z') },
    );

    expect(state.intervals).toHaveLength(2);
    expect(draftSegments(state)).toEqual({
      rows: [
        { segment: 1, workDate: '2026-10-15', startMinute: 540, durationMinutes: 10 },
        { segment: 2, workDate: '2026-10-15', startMinute: 551, durationMinutes: 9 },
      ],
      excludedMilliseconds: 30_000,
    });
  });

  it('proposes nothing until the timer is stopped', () => {
    const start = at('2026-10-15T09:00:00Z');
    const running = run({ kind: 'START', taskId: TASK, atUtcMs: start });
    const paused = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'PAUSE', atUtcMs: start + 10 * MINUTE });

    for (const state of [emptyTracker(), running, paused]) {
      expect(draftSegments(state)).toEqual({ rows: [], excludedMilliseconds: 0 });
    }
  });
});

describe('applyTracker', () => {
  it('joins exactly adjacent intervals and drops zero-length ones', () => {
    const start = at('2026-10-15T09:00:00Z');
    const joined = run(
      { kind: 'START', taskId: TASK, atUtcMs: start },
      { kind: 'PAUSE', atUtcMs: start + 10 * MINUTE },
      { kind: 'RESUME', atUtcMs: start + 10 * MINUTE },
      { kind: 'STOP', atUtcMs: start + 20 * MINUTE },
    );
    expect(joined.intervals).toEqual([{ startUtcMs: start, endUtcMs: start + 20 * MINUTE }]);

    const empty = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'PAUSE', atUtcMs: start });
    expect(empty).toEqual({ phase: 'PAUSED', taskId: TASK_LOWER, startedUtcMs: null, intervals: [] });
  });

  it('returns the unchanged state for a repeated stop', () => {
    const start = at('2026-10-15T09:00:00Z');
    const stopped = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'STOP', atUtcMs: start + MINUTE });
    const before = JSON.stringify(stopped);

    const result = applyTracker(stopped, { kind: 'STOP', atUtcMs: start + 5 * MINUTE });
    expect(result).toEqual({ ok: true, state: stopped });
    expect(result.state).toBe(stopped);
    expect(JSON.stringify(stopped)).toBe(before);
  });

  it('stops a paused timer without adding time', () => {
    const start = at('2026-10-15T09:00:00Z');
    const stopped = run(
      { kind: 'START', taskId: TASK, atUtcMs: start },
      { kind: 'PAUSE', atUtcMs: start + 10 * MINUTE },
      { kind: 'STOP', atUtcMs: start + 50 * MINUTE },
    );
    expect(stopped).toEqual({
      phase: 'STOPPED',
      taskId: TASK_LOWER,
      startedUtcMs: null,
      intervals: [{ startUtcMs: start, endUtcMs: start + 10 * MINUTE }],
    });
  });

  it('discards any state back to idle', () => {
    const start = at('2026-10-15T09:00:00Z');
    for (const state of [
      emptyTracker(),
      run({ kind: 'START', taskId: TASK, atUtcMs: start }),
      run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'STOP', atUtcMs: start + MINUTE }),
    ]) {
      expect(applyTracker(state, { kind: 'DISCARD' })).toEqual({ ok: true, state: emptyTracker() });
    }
  });

  it('refuses actions that do not match the current state without changing it', () => {
    const start = at('2026-10-15T09:00:00Z');
    const running = run({ kind: 'START', taskId: TASK, atUtcMs: start });
    const paused = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'PAUSE', atUtcMs: start + MINUTE });
    const stopped = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'STOP', atUtcMs: start + MINUTE });

    expectRefusal(emptyTracker(), { kind: 'RESUME', atUtcMs: start }, WRONG_STATE);
    expectRefusal(emptyTracker(), { kind: 'PAUSE', atUtcMs: start }, WRONG_STATE);
    expectRefusal(emptyTracker(), { kind: 'STOP', atUtcMs: start }, WRONG_STATE);
    expectRefusal(running, { kind: 'START', taskId: TASK, atUtcMs: start + MINUTE }, WRONG_STATE);
    expectRefusal(running, { kind: 'RESUME', atUtcMs: start + MINUTE }, WRONG_STATE);
    expectRefusal(paused, { kind: 'PAUSE', atUtcMs: start + 2 * MINUTE }, WRONG_STATE);
    expectRefusal(stopped, { kind: 'START', taskId: TASK, atUtcMs: start + 2 * MINUTE }, WRONG_STATE);
    expectRefusal(stopped, { kind: 'RESUME', atUtcMs: start + 2 * MINUTE }, WRONG_STATE);
  });

  it('refuses backwards timestamps without changing the state', () => {
    const start = at('2026-10-15T09:00:00Z');
    const running = run({ kind: 'START', taskId: TASK, atUtcMs: start });
    const paused = run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'PAUSE', atUtcMs: start + 10 * MINUTE });

    expectRefusal(running, { kind: 'PAUSE', atUtcMs: start - 1 }, WRONG_STATE);
    expectRefusal(running, { kind: 'STOP', atUtcMs: start - 1 }, WRONG_STATE);
    expectRefusal(paused, { kind: 'RESUME', atUtcMs: start + 10 * MINUTE - 1 }, WRONG_STATE);
  });

  it('refuses an invalid task identifier without changing the state', () => {
    const start = at('2026-10-15T09:00:00Z');
    for (const taskId of [
      'not-a-guid',
      '',
      '00000000-0000-0000-0000-000000000000',
      '{3c2d3786-43bb-4353-9bf7-9710c54f5315}',
      '3c2d378643bb43539bf79710c54f5315',
      ` ${TASK_LOWER}`,
    ]) {
      expectRefusal(emptyTracker(), { kind: 'START', taskId, atUtcMs: start }, INVALID);
    }
    expectRefusal(emptyTracker(), { kind: 'START', taskId: null, atUtcMs: start } as unknown as Action, INVALID);
  });

  it('refuses an invalid timestamp without changing the state', () => {
    const running = run({ kind: 'START', taskId: TASK, atUtcMs: at('2026-10-15T09:00:00Z') });
    const lastFourDigitYearMs = at('9999-12-31T23:59:59.999Z');
    for (const atUtcMs of [-1, 1.5, Number.NaN, Number.POSITIVE_INFINITY, Number.MAX_SAFE_INTEGER, lastFourDigitYearMs + 1]) {
      expectRefusal(emptyTracker(), { kind: 'START', taskId: TASK, atUtcMs }, INVALID);
      expectRefusal(running, { kind: 'STOP', atUtcMs }, INVALID);
    }
    expectRefusal(running, { kind: 'STOP', atUtcMs: '2026-10-15T09:10:00Z' } as unknown as Action, INVALID);
    expectRefusal(running, { kind: 'LAP', atUtcMs: at('2026-10-15T09:10:00Z') } as unknown as Action, INVALID);

    expect(applyTracker(emptyTracker(), { kind: 'START', taskId: TASK, atUtcMs: lastFourDigitYearMs }).ok).toBe(true);
    expect(applyTracker(emptyTracker(), { kind: 'START', taskId: TASK, atUtcMs: 0 }).ok).toBe(true);
  });

  it('refuses a 33rd interval without changing the state', () => {
    const start = at('2026-10-15T09:00:00Z');
    const actions: Action[] = [{ kind: 'START', taskId: TASK, atUtcMs: start }];
    for (let i = 0; i < 32; i++) {
      if (i > 0) actions.push({ kind: 'RESUME', atUtcMs: start + i * 2 * MINUTE });
      actions.push({ kind: 'PAUSE', atUtcMs: start + i * 2 * MINUTE + MINUTE });
    }
    const paused = run(...actions);
    expect(paused.intervals).toHaveLength(32);
    expect(draftSegments(run(...actions, { kind: 'STOP', atUtcMs: start + 64 * MINUTE })).rows).toHaveLength(32);

    const resumed = run(...actions, { kind: 'RESUME', atUtcMs: start + 64 * MINUTE });
    expectRefusal(resumed, { kind: 'PAUSE', atUtcMs: start + 65 * MINUTE }, LIMIT);
    expectRefusal(resumed, { kind: 'STOP', atUtcMs: start + 65 * MINUTE }, LIMIT);
  });

  it('refuses more than 24 active hours without changing the state', () => {
    const start = at('2026-10-15T00:00:00Z');
    const running = run({ kind: 'START', taskId: TASK, atUtcMs: start });
    expectRefusal(running, { kind: 'STOP', atUtcMs: start + DAY + 1 }, LIMIT);
    expectRefusal(running, { kind: 'PAUSE', atUtcMs: start + DAY + 1 }, LIMIT);
    expect(applyTracker(running, { kind: 'STOP', atUtcMs: start + DAY }).ok).toBe(true);

    const resumed = run(
      { kind: 'START', taskId: TASK, atUtcMs: start },
      { kind: 'PAUSE', atUtcMs: at('2026-10-15T20:00:00Z') },
      { kind: 'RESUME', atUtcMs: at('2026-10-15T21:00:00Z') },
    );
    expectRefusal(resumed, { kind: 'STOP', atUtcMs: at('2026-10-16T01:00:00.001Z') }, LIMIT);
    expect(applyTracker(resumed, { kind: 'STOP', atUtcMs: at('2026-10-16T01:00:00Z') }).ok).toBe(true);
  });

  it('works on a deep-frozen state without mutating it', () => {
    const start = at('2026-10-15T09:00:00Z');
    let state = deepFreeze(
      run({ kind: 'START', taskId: TASK, atUtcMs: start }, { kind: 'PAUSE', atUtcMs: start + 10 * MINUTE }),
    );
    for (const action of [
      { kind: 'RESUME', atUtcMs: start + 10 * MINUTE },
      { kind: 'PAUSE', atUtcMs: start + 15 * MINUTE },
      { kind: 'RESUME', atUtcMs: start + 20 * MINUTE },
      { kind: 'STOP', atUtcMs: start + 25 * MINUTE },
    ] satisfies Action[]) {
      const result = applyTracker(state, action);
      expect(result.ok).toBe(true);
      state = deepFreeze(result.state);
    }

    expect(draftSegments(state)).toEqual({
      rows: [
        { segment: 1, workDate: '2026-10-15', startMinute: 540, durationMinutes: 15 },
        { segment: 2, workDate: '2026-10-15', startMinute: 560, durationMinutes: 5 },
      ],
      excludedMilliseconds: 0,
    });
    expectRefusal(state, { kind: 'START', taskId: TASK, atUtcMs: start }, WRONG_STATE);
    expect(applyTracker(state, { kind: 'DISCARD' })).toEqual({ ok: true, state: emptyTracker() });
  });

  it('uses no clock, timer, network, storage, randomness or global state', () => {
    const start = at('2026-10-15T09:00:00Z');
    const globalsBefore = Object.getOwnPropertyNames(globalThis).sort();
    let drafts: ReturnType<typeof draftSegments> | undefined;

    const calls = withForbiddenEffects(() => {
      let state = emptyTracker();
      for (const action of [
        { kind: 'START', taskId: TASK, atUtcMs: start },
        { kind: 'PAUSE', atUtcMs: start + 10 * MINUTE },
        { kind: 'RESUME', atUtcMs: start + 20 * MINUTE },
        { kind: 'STOP', atUtcMs: start + 25 * MINUTE },
        { kind: 'RESUME', atUtcMs: start + 30 * MINUTE },
      ] satisfies Action[]) {
        state = applyTracker(state, action).state;
      }
      drafts = draftSegments(state);
      applyTracker(state, { kind: 'DISCARD' });
    });

    expect(calls).toEqual([]);
    expect(drafts?.rows).toHaveLength(2);
    expect(Object.getOwnPropertyNames(globalThis).sort()).toEqual(globalsBefore);
  });
});
