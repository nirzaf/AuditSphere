/**
 * Board and calendar arrangements of one loaded work-queue page. They only regroup the tasks the server returned for the
 * selected view, so list, board and calendar always show the same membership. Nothing here changes a task: there is no
 * drag between columns, because a status changes only through its own command.
 */
export interface QueueTaskLike {
  taskId: string;
  taskStatus: string;
  dueDate: string | null;
}

export type QueueDisplay = 'LIST' | 'BOARD' | 'CALENDAR';
export const QUEUE_DISPLAYS: { key: QueueDisplay; label: string }[] = [
  { key: 'LIST', label: 'List' },
  { key: 'BOARD', label: 'Board' },
  { key: 'CALENDAR', label: 'Calendar' },
];

export interface BoardColumn<T> {
  key: string;
  label: string;
  tasks: T[];
}

const BOARD_COLUMNS: { key: string; label: string }[] = [
  { key: 'OPEN', label: 'Open' },
  { key: 'IN_PROGRESS', label: 'In progress' },
  { key: 'COMPLETED', label: 'Completed' },
];

/** One column per operational status, in a fixed order. A status the board does not know gets its own column, never dropped. */
export function boardColumns<T extends QueueTaskLike>(tasks: readonly T[]): BoardColumn<T>[] {
  const columns: BoardColumn<T>[] = BOARD_COLUMNS.map((c) => ({ ...c, tasks: [] as T[] }));
  for (const task of tasks) {
    let column = columns.find((c) => c.key === task.taskStatus);
    if (!column) {
      column = { key: task.taskStatus, label: task.taskStatus.replaceAll('_', ' ').toLowerCase(), tasks: [] };
      columns.push(column);
    }
    column.tasks.push(task);
  }
  return columns;
}

export interface CalendarDay<T> {
  date: string;
  weekday: string;
  workingDay: boolean;
  tasks: T[];
}
export interface CalendarWeek<T> {
  weekStart: string;
  days: CalendarDay<T>[];
}
export interface QueueCalendar<T> {
  weeks: CalendarWeek<T>[];
  undated: T[];
}

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const DAY_MS = 86_400_000;
const iso = (ms: number) => new Date(ms).toISOString().slice(0, 10);

/** The Sunday that opens the firm week containing the date, matching the server's allocation weeks. */
export function firmWeekStart(date: string): string {
  const ms = Date.parse(date + 'T00:00:00Z');
  if (Number.isNaN(ms)) throw new Error('Invalid calendar date');
  return iso(ms - new Date(ms).getUTCDay() * DAY_MS);
}

/**
 * Tasks by due date in Sunday-to-Saturday weeks. Only weeks that hold a task are shown, each with all seven days so an empty
 * day is visible. Friday and Saturday are marked as the weekend. Tasks with no due date are listed separately.
 */
export function queueCalendar<T extends QueueTaskLike>(tasks: readonly T[]): QueueCalendar<T> {
  const undated: T[] = [];
  const byWeek = new Map<string, Map<string, T[]>>();
  for (const task of tasks) {
    if (!task.dueDate) {
      undated.push(task);
      continue;
    }
    const week = firmWeekStart(task.dueDate);
    const days = byWeek.get(week) ?? new Map<string, T[]>();
    days.set(task.dueDate, [...(days.get(task.dueDate) ?? []), task]);
    byWeek.set(week, days);
  }
  const weeks = [...byWeek.keys()].sort().map((weekStart) => {
    const start = Date.parse(weekStart + 'T00:00:00Z');
    const days = WEEKDAYS.map((weekday, offset) => {
      const date = iso(start + offset * DAY_MS);
      return { date, weekday, workingDay: offset <= 4, tasks: byWeek.get(weekStart)!.get(date) ?? [] };
    });
    return { weekStart, days };
  });
  return { weeks, undated };
}
