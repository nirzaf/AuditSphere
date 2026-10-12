import { describe, expect, it } from 'vitest';
import { boardColumns, firmWeekStart, queueCalendar } from './work-queue-views';

const task = (taskId: string, taskStatus: string, dueDate: string | null) => ({ taskId, taskStatus, dueDate });
const tasks = [
  task('a', 'OPEN', '2026-10-11'),
  task('b', 'IN_PROGRESS', '2026-10-15'),
  task('c', 'COMPLETED', '2026-10-17'),
  task('d', 'OPEN', '2026-10-18'),
  task('e', 'OPEN', null),
  task('f', 'ON_HOLD', '2026-10-15'),
];
const ids = (list: { taskId: string }[]) => list.map((x) => x.taskId).sort();

describe('work queue board and calendar', () => {
  it('shows every loaded task exactly once in each arrangement', () => {
    const board = boardColumns(tasks).flatMap((c) => c.tasks);
    const calendar = queueCalendar(tasks);
    const onCalendar = [...calendar.weeks.flatMap((w) => w.days.flatMap((d) => d.tasks)), ...calendar.undated];
    expect(ids(board)).toEqual(ids(tasks));
    expect(ids(onCalendar)).toEqual(ids(tasks));
  });

  it('keeps a fixed column order and never drops an unknown status', () => {
    const columns = boardColumns(tasks);
    expect(columns.map((c) => c.key)).toEqual(['OPEN', 'IN_PROGRESS', 'COMPLETED', 'ON_HOLD']);
    expect(ids(columns[0].tasks)).toEqual(['a', 'd', 'e']);
    expect(boardColumns([]).map((c) => c.tasks.length)).toEqual([0, 0, 0]);
  });

  it('groups due dates into Sunday-start weeks with the weekend marked', () => {
    expect(firmWeekStart('2026-10-11')).toBe('2026-10-11');
    expect(firmWeekStart('2026-10-17')).toBe('2026-10-11');
    expect(firmWeekStart('2026-10-18')).toBe('2026-10-18');
    const calendar = queueCalendar(tasks);
    expect(calendar.weeks.map((w) => w.weekStart)).toEqual(['2026-10-11', '2026-10-18']);
    const first = calendar.weeks[0];
    expect(first.days.map((d) => d.weekday)).toEqual(['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']);
    expect(first.days.map((d) => d.workingDay)).toEqual([true, true, true, true, true, false, false]);
    expect(ids(first.days[4].tasks)).toEqual(['b', 'f']);
    expect(ids(first.days[6].tasks)).toEqual(['c']);
    expect(ids(calendar.undated)).toEqual(['e']);
  });

  it('refuses a malformed date instead of placing the task on a wrong day', () => {
    expect(() => queueCalendar([task('x', 'OPEN', 'soon')])).toThrow();
  });
});
