import { describe, expect, it } from 'vitest';
import { boardColumns, firmWeekStart, groupedTasks, queueCalendar } from './work-queue-views';

const task = (taskId: string, taskStatus: string, dueDate: string | null) => ({ taskId, taskStatus, dueDate });
const tasks = [
  task('a', 'OPEN', '2026-10-11'),
  task('b', 'IN_PROGRESS', '2026-10-15'),
  task('c', 'COMPLETED', '2026-10-17'),
  task('d', 'OPEN', '2026-10-18'),
  task('e', 'OPEN', null),
  task('f', 'ON_HOLD', '2026-10-15'),
  task('g', 'CANCELLED', '2026-10-18'),
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
    expect(columns.map((c) => c.key)).toEqual(['OPEN', 'IN_PROGRESS', 'COMPLETED', 'ON_HOLD', 'CANCELLED']);
    expect(ids(columns[0].tasks)).toEqual(['a', 'd', 'e']);
    expect(ids(columns[4].tasks)).toEqual(['g']);
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
    expect(ids(calendar.weeks[1].days[0].tasks)).toEqual(['d', 'g']);
    expect(ids(first.days[6].tasks)).toEqual(['c']);
    expect(ids(calendar.undated)).toEqual(['e']);
  });

  it('refuses a malformed date instead of placing the task on a wrong day', () => {
    expect(() => queueCalendar([task('x', 'OPEN', 'soon')])).toThrow();
  });
});

describe('work queue grouped arrangement', () => {
  const grouped = (taskId: string, engagementId: string, phase: string, ordinal: number, taskGroupName: string | null) => ({
    taskId, taskStatus: 'OPEN', dueDate: null, engagementId, clientName: `Client ${engagementId}`, engagementServiceRoute: 'AUDIT',
    engagementPeriod: '2026', phase, ordinal, taskGroupName,
  });
  const page = [
    grouped('t3', 'E2', 'AUDIT_FIELDWORK', 3, null), grouped('t1', 'E1', 'AUDIT_FIELDWORK', 5, 'Cash'),
    grouped('t2', 'E1', 'AUDIT_PLANNING', 1, '  '), grouped('t4', 'E1', 'AUDIT_FIELDWORK', 4, 'Cash'), grouped('t5', 'E1', 'AUDIT_FIELDWORK', 2, 'Revenue'),
  ];

  it('shows every loaded task exactly once, by engagement, phase and task group', () => {
    const groups = groupedTasks(page);
    expect(ids(groups.flatMap(g => g.phases.flatMap(p => p.taskGroups.flatMap(t => t.tasks))))).toEqual(ids(page));
    expect(groups.map(g => [g.engagementId, g.count])).toEqual([['E2', 1], ['E1', 4]]);
    const e1 = groups[1];
    expect(e1.phases.map(p => [p.phase, p.label, p.count])).toEqual([['AUDIT_PLANNING', 'Audit planning', 1], ['AUDIT_FIELDWORK', 'Audit fieldwork', 3]]);
    expect(e1.phases[0].taskGroups).toEqual([{ name: null, tasks: [page[2]] }]);
    expect(e1.phases[1].taskGroups.map(t => [t.name, t.tasks.map(x => x.taskId)])).toEqual([['Revenue', ['t5']], ['Cash', ['t4', 't1']]]);
    expect(groupedTasks([])).toEqual([]);
  });
});
