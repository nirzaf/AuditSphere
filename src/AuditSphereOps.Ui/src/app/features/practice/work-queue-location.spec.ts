import { convertToParamMap } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { parseWorkQueueLocation, QUEUE_VIEW_KEYS } from './work-queue-location';

describe('work queue route state', () => {
  it('accepts existing values and keeps exact reminder parameters separate', () => {
    expect(parseWorkQueueLocation(convertToParamMap({}))).toEqual({ view: 'MY_TASKS', owner: 'ALL', display: 'LIST',
      clientFilterId: null, engagementFilterId: null });
    for (const view of QUEUE_VIEW_KEYS) {
      expect(parseWorkQueueLocation(convertToParamMap({ queueView: view, queueAssignment: 'ALL', queueDisplay: 'GROUPED',
        engagementId: 'exact-engagement', taskId: 'exact-task' })))
        .toEqual({ view, owner: 'ALL', display: 'GROUPED', clientFilterId: null, engagementFilterId: null });
    }
    for (const owner of ['ALL', 'ME', 'UNASSIGNED'])
      for (const display of ['LIST', 'BOARD', 'CALENDAR', 'GROUPED'])
        expect(parseWorkQueueLocation(convertToParamMap({ queueView: 'TEAM', queueAssignment: owner, queueDisplay: display })))
          .toEqual({ view: 'TEAM', owner, display, clientFilterId: null, engagementFilterId: null });
  });

  it('restores optional scoped filters without interpreting exact reminder selection as a filter', () => {
    const clientId = '10000000-0000-0000-0000-000000000001';
    const engagementId = '20000000-0000-0000-0000-000000000002';
    expect(parseWorkQueueLocation(convertToParamMap({ queueView: 'TEAM', queueAssignment: 'ME', queueDisplay: 'GROUPED',
      queueClientId: clientId.toUpperCase(), queueEngagementId: engagementId,
      engagementId: '30000000-0000-0000-0000-000000000003', taskId: '40000000-0000-0000-0000-000000000004' })))
      .toEqual({ view: 'TEAM', owner: 'ME', display: 'GROUPED', clientFilterId: clientId, engagementFilterId: engagementId });
    expect(parseWorkQueueLocation(convertToParamMap({ queueEngagementId: engagementId })))
      .toEqual({ view: 'MY_TASKS', owner: 'ALL', display: 'LIST', clientFilterId: null, engagementFilterId: engagementId });
  });

  it('refuses duplicate, unsupported, empty and incompatible values rather than defaulting', () => {
    for (const params of [
      { queueView: ['TEAM', 'TEAM'] }, { queueAssignment: ['ALL', 'ME'] }, { queueDisplay: ['LIST', 'BOARD'] },
      { queueView: '' }, { queueView: 'team' }, { queueView: 'UNSUPPORTED' },
      { queueAssignment: '' }, { queueAssignment: 'someone-else' }, { queueDisplay: '' },
      { queueDisplay: 'TABLE' }, { queueView: 'MY_TASKS', queueAssignment: 'ME' },
      { queueAssignment: 'UNASSIGNED' },
      { queueClientId: '' }, { queueClientId: 'not-a-guid' },
      { queueEngagementId: '00000000-0000-0000-0000-000000000000' },
      { queueClientId: ['10000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002'] },
      { queueEngagementId: ['20000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002'] },
    ]) expect(parseWorkQueueLocation(convertToParamMap(params))).toBeNull();
  });
});
