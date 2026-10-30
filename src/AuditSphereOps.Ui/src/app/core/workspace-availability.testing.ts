import { Provider, signal } from '@angular/core';
import { NAVIGATION } from '../navigation';
import { WorkspaceAvailability, WorkspaceAvailabilityView } from './workspace-availability';

/** Existing queue and shell behavior tests can supply explicit authorized presentation state. */
export function provideWorkspaceAvailabilityForTests(
  changes: Partial<WorkspaceAvailabilityView> = {},
): Provider {
  const current = signal<WorkspaceAvailabilityView | null>({
    userId: '11111111-1111-4111-8111-111111111111', firmId: '11111111-1111-4111-8111-111111111111',
    generation: '1', staff: true, destinations: NAVIGATION.flatMap(group => group.items.map(item => item.id)),
    tasks: 'available', reviews: 'available', templates: 'available', validForMs: 30000, ...changes,
  });
  const state = signal<'ready' | 'unavailable'>('ready');
  const revision = signal(0);
  return { provide: WorkspaceAvailability, useValue: {
    current, value: current, state, revision, refresh: () => {},
    invalidate: () => { current.set(null); state.set('unavailable'); revision.update(value => value + 1); },
  } };
}
