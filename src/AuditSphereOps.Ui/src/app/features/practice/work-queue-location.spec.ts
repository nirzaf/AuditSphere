import { convertToParamMap } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { parseWorkQueueLocation, QUEUE_VIEW_KEYS } from './work-queue-location';

describe('work queue route state', () => {
  it('accepts existing values and keeps exact reminder parameters separate', () => {
    expect(parseWorkQueueLocation(convertToParamMap({}))).toEqual({ view: 'MY_TASKS', owner: 'ALL', display: 'LIST' });
    for (const view of QUEUE_VIEW_KEYS) {
      expect(parseWorkQueueLocation(convertToParamMap({ queueView: view, queueAssignment: 'ALL', queueDisplay: 'GROUPED',
        engagementId: 'exact-engagement', taskId: 'exact-task' })))
        .toEqual({ view, owner: 'ALL', display: 'GROUPED' });
    }
    for (const owner of ['ALL', 'ME', 'UNASSIGNED'])
      for (const display of ['LIST', 'BOARD', 'CALENDAR', 'GROUPED'])
        expect(parseWorkQueueLocation(convertToParamMap({ queueView: 'TEAM', queueAssignment: owner, queueDisplay: display })))
          .toEqual({ view: 'TEAM', owner, display });
  });

  it('refuses duplicate, unsupported, empty and incompatible values rather than defaulting', () => {
    for (const params of [
      { queueView: ['TEAM', 'TEAM'] }, { queueAssignment: ['ALL', 'ME'] }, { queueDisplay: ['LIST', 'BOARD'] },
      { queueView: '' }, { queueView: 'team' }, { queueView: 'UNSUPPORTED' },
      { queueAssignment: '' }, { queueAssignment: 'someone-else' }, { queueDisplay: '' },
      { queueDisplay: 'TABLE' }, { queueView: 'MY_TASKS', queueAssignment: 'ME' },
      { queueAssignment: 'UNASSIGNED' },
    ]) expect(parseWorkQueueLocation(convertToParamMap(params))).toBeNull();
  });
});
