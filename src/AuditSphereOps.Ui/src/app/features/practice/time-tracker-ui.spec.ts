import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { int64String, TimeEntryChangePanel } from './time-entry-change';
import { decodeTime, PracticeTime, supportedBudgetPhases } from './time';

const userId = '11111111-1111-4111-8111-111111111111';
const taskId = '22222222-2222-4222-8222-222222222222';
const entryId = '33333333-3333-4333-8333-333333333333';
const workspace = {
  firmWide: true, isApprover: false, periods: [], engagements: [],
  openTasks: [{ id: taskId, title: 'Prepare lead schedule', status: 'OPEN', mappingVersionId: null, fsliCode: null }],
  myEntries: [], awaitingApproval: [],
};

function open(initialWorkspace: object = workspace) {
  TestBed.configureTestingModule({
    imports: [PracticeTime],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const session = TestBed.inject(SessionService);
  session.current.set({ userId, firmId: userId, generation: '1', staff: true });
  const fixture = TestBed.createComponent(PracticeTime);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges(); TestBed.tick();
  http.expectOne('/api/ui/practice/time').flush(initialWorkspace);
  TestBed.tick();
  return { fixture, component: fixture.componentInstance, http, session };
}

async function settleFlushedRead(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  // HttpTestingController.flush is synchronous; readScoped and its caller each resume in a promise continuation.
  await Promise.resolve();
  await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('foreground practice time tracker integration', () => {
  it('defaults legacy time-change flags closed and accepts only exact Int64 revision strings', () => {
    const savedEntry = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 30, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: '',
      status: 'DRAFT', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
    };
    const legacy = decode(decodeTime, { ...workspace, myEntries: [savedEntry] }).myEntries[0];
    expect(legacy.revision).toBe('1');
    expect(legacy.canEdit).toBe(false);
    expect(legacy.canCorrect).toBe(false);
    expect(legacy.hasChanges).toBe(false);
    expect(legacy.supersedesId).toBeNull();
    expect(legacy.correctionReason).toBeNull();

    expect(int64String('9007199254740993', 'revision')).toBe('9007199254740993');
    for (const invalid of [9007199254740993, '01', '-1', '9223372036854775808'])
      expect(() => int64String(invalid, 'revision')).toThrow();
    expect(() => decode(decodeTime, { ...workspace, myEntries: [{ ...savedEntry, revision: 9007199254740993 }] })).toThrow();
  });

  it('shows a retained legacy correction reason without inventing a historical corrector or snapshots', async () => {
    const sourceId = '66666666-6666-4666-8666-666666666666';
    const legacyCorrection = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 30, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: '',
      status: 'APPROVED', mappingVersionId: null, fsliCode: null, lastReturnReason: null, supersedesId: sourceId,
      correctionReason: 'Reconstructed from the retained legacy correction note.',
    };
    const { fixture } = open({ ...workspace, myEntries: [legacyCorrection] });
    try {
      fixture.detectChanges();
      const row = fixture.nativeElement.querySelector('[aria-labelledby="my-time-heading"] tbody tr') as HTMLTableRowElement;
      expect(row.textContent).toContain('Reconstructed from the retained legacy correction note.');
      expect(row.textContent).toContain('No historical change receipt is recorded');
      expect(row.textContent).toContain('the corrector and before/after snapshots were not retained');
      expect(row.textContent).not.toContain('Recorded by');
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('renders retained change history and retries an unknown returned-entry edit with the exact saved request', async () => {
    const returned = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 60, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: 'Source note',
      status: 'RETURNED', mappingVersionId: null, fsliCode: null, lastReturnReason: 'Please clarify the time period.',
      phase: 'FIELDWORK', revision: '9007199254740993', canEdit: true, canCorrect: false, hasChanges: true, supersedesId: null,
    };
    const legacyLongNarrative = 'Retained legacy narrative '.repeat(120);
    const historicalReceipt = {
      id: '44444444-4444-4444-8444-444444444444', actorId: userId, actorName: 'Time preparer', actorEpoch: '1',
      requestId: '55555555-5555-4555-8555-555555555555', requestHash: 'a'.repeat(64), kind: 'EDIT_UNAPPROVED',
      timeEntryId: entryId, resultTimeEntryId: entryId, sourceRevision: '7', resultRevision: '8',
      reason: 'Aligned with the retained work note.', createdAt: '2026-10-20T12:30:00.123Z',
      before: { workDate: '2026-10-15', startMinute: 540, durationMinutes: 60, activity: 'Fieldwork', narrative: legacyLongNarrative },
      after: { workDate: '2026-10-15', startMinute: 542, durationMinutes: 58, activity: 'Fieldwork', narrative: 'Revised note' },
      sourceStatusBefore: 'RETURNED', sourceStatusAfter: 'RETURNED', resultStatus: 'RETURNED',
    };
    const initial = { ...workspace, myEntries: [returned] };
    const { fixture, component, http } = open(initial);
    try {
      component.task.title = 'Keep this task draft';
      component.draft.narrative = 'Keep this time draft';
      component.openTimeChange(component.view.data()!.myEntries[0], 'EDIT_UNAPPROVED');
      fixture.detectChanges(); TestBed.tick();
      const panel = fixture.debugElement.query(By.directive(TimeEntryChangePanel)).componentInstance as TimeEntryChangePanel;
      expect(fixture.nativeElement.ownerDocument.activeElement?.id).toBe('time-change-heading');
      http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`).flush({
        items: [historicalReceipt], pageSize: 20, hasMore: false, nextCursor: null,
      });
      await settleFlushedRead(fixture);
      const historyText = fixture.nativeElement.textContent as string;
      expect(historyText).toContain('Time preparer');
      expect(historyText).toContain('Aligned with the retained work note.');
      expect(historyText).toContain(legacyLongNarrative);
      expect(historyText).toContain('Revised note');
      expect(panel.source()?.revision).toBe('9007199254740993');

      panel.form = {
        workDate: '2026-10-16', startTime: '09:05', durationMinutes: 55,
        activity: 'Reconciled fieldwork', narrative: 'Updated retained note', reason: 'Matched to the source work record.',
      };
      panel.reviewChange();
      await fixture.whenStable();
      expect(fixture.nativeElement.ownerDocument.activeElement?.id).toBe('time-change-review-heading');
      expect(fixture.nativeElement.textContent).toContain('9007199254740993');
      const saving = panel.saveReviewedChange();
      const postPath = `/api/ui/practice/time/entries/${entryId}/changes`;
      const firstPost = http.expectOne(postPath);
      const frozenBody = JSON.parse(JSON.stringify(firstPost.request.body)) as Record<string, unknown>;
      expect(frozenBody).toMatchObject({
        expectedRevision: '9007199254740993', kind: 'EDIT_UNAPPROVED', workDate: '2026-10-16',
        startMinute: 545, durationMinutes: 55, activity: 'Reconciled fieldwork',
        narrative: 'Updated retained note', reason: 'Matched to the source work record.',
      });
      expect(Object.keys(frozenBody).sort()).toEqual([
        'activity', 'durationMinutes', 'expectedRevision', 'kind', 'narrative', 'reason', 'requestId', 'startMinute', 'workDate',
      ]);
      firstPost.error(new ProgressEvent('network error'));
      await saving;
      await fixture.whenStable();
      expect(panel.hasUncertainOutcome()).toBe(true);
      expect(fixture.nativeElement.ownerDocument.activeElement?.id).toBe('time-change-recovery-heading');
      expect(panel.hasPendingRequest()).toBe(true);
      expect(component.task.title).toBe('Keep this task draft');
      expect(component.draft.narrative).toBe('Keep this time draft');
      component.refreshEntriesQueue();
      http.expectNone(request => request.method === 'POST' && request.url === postPath);
      http.expectNone('/api/ui/practice/time');
      const dialog = fixture.debugElement.injector.get(MatDialog);
      const stay = vi.spyOn(dialog, 'open').mockImplementation(() => ({ afterClosed: () => of('stay') }) as never);
      expect(await component.confirmNavigation()).toBe(false);
      expect(stay).toHaveBeenCalledTimes(1);

      const checking = panel.checkSavedChange();
      http.expectOne(`${postPath}/requests/${frozenBody['requestId']}`).flush({ found: false, receipt: null });
      await checking;
      expect(panel.receiptState()).toBe('NOT_FOUND');
      const retrying = panel.retryExactChange();
      const retry = http.expectOne(postPath);
      expect(retry.request.body).toEqual(frozenBody);
      retry.flush({ value: entryId });
      await retrying;
      http.expectOne('/api/ui/practice/time').flush({ ...workspace, myEntries: [{ ...returned, revision: '9007199254740994', activity: 'Reconciled fieldwork' }] });
      TestBed.tick(); fixture.detectChanges();
      expect(component.timeChangeNotice()).toContain('remains RETURNED until submitted separately');
      stay.mockRestore();
    } finally {
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith('auditsphere-time-entry-change:v1:')) sessionStorage.removeItem(key);
      }
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks();
    }
  });

  it('returns focus through review and close without leaving the source row trigger', async () => {
    const returned = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 60, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: 'Source note',
      status: 'RETURNED', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
      revision: '1', canEdit: true, canCorrect: false, hasChanges: false, supersedesId: null,
    };
    const { fixture, component, http } = open({ ...workspace, myEntries: [returned] });
    try {
      const trigger = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.trim() === 'Edit time') as HTMLButtonElement;
      trigger.focus();
      component.openTimeChange(component.view.data()!.myEntries[0], 'EDIT_UNAPPROVED');
      fixture.detectChanges(); TestBed.tick();
      expect(fixture.nativeElement.ownerDocument.activeElement?.id).toBe('time-change-heading');
      http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`).flush({ items: [], pageSize: 20, hasMore: false, nextCursor: null });
      await fixture.whenStable();
      expect(fixture.nativeElement.ownerDocument.activeElement?.id).toBe('time-change-heading');

      const panel = fixture.debugElement.query(By.directive(TimeEntryChangePanel)).componentInstance as TimeEntryChangePanel;
      panel.form = { workDate: '2026-10-16', startTime: '09:00', durationMinutes: 30,
        activity: 'Reconciled fieldwork', narrative: '', reason: 'Matched to the source work record.' };
      fixture.detectChanges();
      await fixture.whenStable();
      const review = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.trim() === 'Review exact change') as HTMLButtonElement;
      expect(panel.formIsValid()).toBe(true);
      expect(review.disabled).toBe(false);
      review.focus();
      expect(fixture.nativeElement.ownerDocument.activeElement).toBe(review);
      review.click();
      await fixture.whenStable();
      expect(panel.reviewing()).toBe(true);
      expect(fixture.nativeElement.ownerDocument.activeElement?.id).toBe('time-change-review-heading');
      expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).some(button => button.textContent?.trim() === 'Save time change')).toBe(true);

      const back = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.trim() === 'Back to edit') as HTMLButtonElement;
      back.focus(); back.click();
      await fixture.whenStable();
      expect(fixture.nativeElement.ownerDocument.activeElement?.getAttribute('name')).toBe('changeWorkDate');

      const dialog = fixture.debugElement.injector.get(MatDialog);
      vi.spyOn(dialog, 'open').mockImplementation(() => ({ afterClosed: () => of('discard') }) as never);
      const close = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.trim() === 'Close time change') as HTMLButtonElement;
      close.focus(); close.click();
      await fixture.whenStable(); TestBed.tick();
      expect(fixture.nativeElement.ownerDocument.activeElement).toBe(trigger);
    } finally {
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks();
    }
  });

  it('keeps a newer change busy when a prior entry request completes after context reuse', async () => {
    const otherEntryId = '77777777-7777-4777-8777-777777777777';
    const returned = (id: string) => ({
      id, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 60, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: 'Source note',
      status: 'RETURNED', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
      revision: '1', canEdit: true, canCorrect: false, hasChanges: false, supersedesId: null,
    });
    const { fixture, component, http } = open({ ...workspace, myEntries: [returned(entryId), returned(otherEntryId)] });
    try {
      component.openTimeChange(component.view.data()!.myEntries[0], 'EDIT_UNAPPROVED');
      fixture.detectChanges(); TestBed.tick();
      const panel = fixture.debugElement.query(By.directive(TimeEntryChangePanel)).componentInstance as TimeEntryChangePanel;
      const firstHistory = http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`);
      panel.form = { workDate: '2026-10-16', startTime: '09:00', durationMinutes: 30,
        activity: 'Reconciled fieldwork', narrative: '', reason: 'Matched to the source work record.' };
      panel.reviewChange();
      const firstSaving = panel.saveReviewedChange();
      const firstPost = http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`);

      component.activeTimeChange.set({ entryId: otherEntryId, action: 'EDIT_UNAPPROVED', focusOnOpen: false, focusOrigin: null });
      fixture.detectChanges(); TestBed.tick();
      const secondHistory = http.expectOne(`/api/ui/practice/time/entries/${otherEntryId}/changes`);
      expect(panel.source()?.id).toBe(otherEntryId);
      panel.form = { workDate: '2026-10-17', startTime: '10:00', durationMinutes: 45,
        activity: 'Reconciled follow-up', narrative: '', reason: 'Matched to the source work record.' };
      panel.reviewChange();
      const secondSaving = panel.saveReviewedChange();
      const secondPost = http.expectOne(`/api/ui/practice/time/entries/${otherEntryId}/changes`);

      firstHistory.flush({ items: [], pageSize: 20, hasMore: false, nextCursor: null });
      await settleFlushedRead(fixture);
      expect(panel.historyLoading()).toBe(true);
      secondHistory.flush({ items: [], pageSize: 20, hasMore: false, nextCursor: null });
      await settleFlushedRead(fixture);
      expect(panel.historyLoading()).toBe(false);

      firstPost.flush({ value: entryId });
      await firstSaving;
      expect(panel.busy()).toBe(true);

      secondPost.error(new ProgressEvent('network error'));
      await secondSaving;
      expect(panel.busy()).toBe(false);
      expect(panel.hasUncertainOutcome()).toBe(true);
    } finally {
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith('auditsphere-time-entry-change:v1:')) sessionStorage.removeItem(key);
      }
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks();
    }
  });

  it('does not move focus to recovery after the user leaves during an unresolved save', async () => {
    const returned = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 60, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: 'Source note',
      status: 'RETURNED', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
      revision: '2', canEdit: true, canCorrect: false, hasChanges: false, supersedesId: null,
    };
    const { fixture, component, http } = open({ ...workspace, myEntries: [returned] });
    try {
      component.openTimeChange(component.view.data()!.myEntries[0], 'EDIT_UNAPPROVED');
      fixture.detectChanges(); TestBed.tick();
      const panel = fixture.debugElement.query(By.directive(TimeEntryChangePanel)).componentInstance as TimeEntryChangePanel;
      http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`).flush({ items: [], pageSize: 20, hasMore: false, nextCursor: null });
      TestBed.tick(); fixture.detectChanges();
      panel.form = { workDate: '2026-10-16', startTime: '09:00', durationMinutes: 30,
        activity: 'Reconciled fieldwork', narrative: '', reason: 'Matched to the source work record.' };
      panel.reviewChange(); fixture.detectChanges(); TestBed.tick();
      const save = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.trim() === 'Save time change') as HTMLButtonElement;
      save.focus();
      const saving = panel.saveReviewedChange();
      const post = http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`);
      const elsewhere = fixture.nativeElement.querySelector('#my-time-heading') as HTMLElement;
      elsewhere.focus();
      elsewhere.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
      post.error(new ProgressEvent('network error'));
      await saving;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.hasUncertainOutcome()).toBe(true);
      expect(fixture.nativeElement.ownerDocument.activeElement).toBe(elsewhere);
    } finally {
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith('auditsphere-time-entry-change:v1:')) sessionStorage.removeItem(key);
      }
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule();
    }
  });

  it('recovers an off-page unknown change only after receipt authorization and clears it on scope denial', async () => {
    const returned = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 60, startMinute: 540, activity: 'Private source activity', billableClassification: 'BILLABLE', narrative: 'Private source note',
      status: 'RETURNED', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
      revision: '4', canEdit: true, canCorrect: false, hasChanges: false, supersedesId: null,
    };
    const first = open({ ...workspace, myEntries: [returned] });
    const postPath = `/api/ui/practice/time/entries/${entryId}/changes`;
    let savedKey: string | null = null;
    let savedJson: string | null = null;
    try {
      first.component.openTimeChange(first.component.view.data()!.myEntries[0], 'EDIT_UNAPPROVED');
      first.fixture.detectChanges(); TestBed.tick();
      const panel = first.fixture.debugElement.query(By.directive(TimeEntryChangePanel)).componentInstance as TimeEntryChangePanel;
      first.http.expectOne(`/api/ui/practice/time/entries/${entryId}/changes`).flush({ items: [], pageSize: 20, hasMore: false, nextCursor: null });
      TestBed.tick(); first.fixture.detectChanges();
      panel.form = { workDate: '2026-11-02', startTime: '09:15', durationMinutes: 45,
        activity: 'Private requested activity', narrative: 'Private requested note', reason: 'Private saved correction reason' };
      panel.reviewChange();
      const saving = panel.saveReviewedChange();
      first.http.expectOne(postPath).error(new ProgressEvent('network error'));
      await saving;
      expect(panel.hasUncertainOutcome()).toBe(true);
    } finally {
      savedKey = Array.from({ length: sessionStorage.length }, (_, index) => sessionStorage.key(index))
        .find(key => key?.startsWith('auditsphere-time-entry-change:v1:')) ?? null;
      savedJson = savedKey ? sessionStorage.getItem(savedKey) : null;
      first.fixture.destroy();
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith('auditsphere-time-entry-change:v1:')) sessionStorage.removeItem(key);
      }
      first.http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule();
    }

    if (!savedKey || !savedJson) throw new Error('Expected the unknown change request to remain in session storage.');
    sessionStorage.setItem(savedKey, savedJson);
    const { fixture, http } = open(workspace);
    try {
      fixture.detectChanges(); TestBed.tick();
      const pendingKey = savedKey;
      const saved = JSON.parse(savedJson) as { request: { requestId: string } };
      const receiptPath = `${postPath}/requests/${saved.request.requestId}`;
      const receiptCheck = http.expectOne(receiptPath);
      const beforeAuthorization = fixture.nativeElement.textContent as string;
      expect(beforeAuthorization).not.toContain(entryId);
      expect(beforeAuthorization).not.toContain('Private source activity');
      expect(beforeAuthorization).not.toContain('Private source note');
      expect(beforeAuthorization).not.toContain('Private requested activity');
      expect(beforeAuthorization).not.toContain('Private requested note');
      expect(beforeAuthorization).not.toContain('Private saved correction reason');

      receiptCheck.flush({ found: false, receipt: null });
      await settleFlushedRead(fixture);
      expect(fixture.nativeElement.textContent).toContain('Private saved correction reason');
      const history = http.expectOne(request => request.method === 'GET' && request.url === postPath);
      history.flush({}, { status: 403, statusText: 'Forbidden' });
      await settleFlushedRead(fixture);
      const afterDenial = fixture.nativeElement.textContent as string;
      expect(afterDenial).not.toContain(entryId);
      expect(afterDenial).not.toContain('Private source activity');
      expect(afterDenial).not.toContain('Private source note');
      expect(afterDenial).not.toContain('Private requested activity');
      expect(afterDenial).not.toContain('Private requested note');
      expect(afterDenial).not.toContain('Private saved correction reason');
      expect(sessionStorage.getItem(pendingKey)).toBeNull();
    } finally {
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith('auditsphere-time-entry-change:v1:')) sessionStorage.removeItem(key);
      }
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks();
    }
  });

  it('offers only assignable budget phases and forwards optional selections without requiring FSLI mapping', async () => {
    const { fixture, component, http } = open();
    try {
      await fixture.whenStable();
      expect(supportedBudgetPhases.map(phase => phase.value)).toEqual(['PLANNING', 'FIELDWORK', 'COMPLETION', 'REPORTING']);
      const phaseSelect = fixture.nativeElement.querySelector('select[name="phase"]') as HTMLSelectElement;
      expect(Array.from(phaseSelect.options).map(option => option.value)).toEqual(['', 'PLANNING', 'FIELDWORK', 'COMPLETION', 'REPORTING']);
      phaseSelect.value = 'FIELDWORK';
      phaseSelect.dispatchEvent(new Event('change', { bubbles: true }));
      await fixture.whenStable();
      expect(component.task.phase).toBe('FIELDWORK');

      component.task.title = 'Firm-wide fieldwork support';
      const creating = component.createTask();
      const selectedPost = http.expectOne('/api/ui/practice/time/tasks');
      expect(selectedPost.request.body).toMatchObject({ title: 'Firm-wide fieldwork support', phase: 'FIELDWORK', engagementId: null,
        mappingVersionId: null, fsliCode: null });
      selectedPost.flush({ value: entryId });
      await creating;
      http.expectOne('/api/ui/practice/time').flush(workspace);

      component.task.title = 'Firm-wide unassigned support';
      const creatingWithoutPhase = component.createTask();
      const unassignedPost = http.expectOne('/api/ui/practice/time/tasks');
      expect(unassignedPost.request.body).not.toHaveProperty('phase');
      unassignedPost.flush({ value: entryId });
      await creatingWithoutPhase;
      http.expectOne('/api/ui/practice/time').flush(workspace);
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('renders stored phases in task selectors and both time tables and defaults legacy payloads to UNASSIGNED', async () => {
    const savedEntry = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 30, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: '',
      status: 'DRAFT', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
    };
    const legacy = decode(decodeTime, { ...workspace, myEntries: [savedEntry], awaitingApproval: [savedEntry] });
    expect(legacy.openTasks[0].phase).toBe('UNASSIGNED');
    expect(legacy.myEntries[0].phase).toBe('UNASSIGNED');
    expect(legacy.awaitingApproval[0].phase).toBe('UNASSIGNED');

    const pendingEntryId = '44444444-4444-4444-8444-444444444444';
    const payload = {
      ...workspace,
      isApprover: true,
      openTasks: [{ ...workspace.openTasks[0], phase: 'FIELDWORK' }],
      myEntries: [{ ...savedEntry, phase: 'COMPLETION' }],
      awaitingApproval: [{ ...savedEntry, id: pendingEntryId, status: 'SUBMITTED', phase: 'UNASSIGNED' }],
    };
    const { fixture, http } = open(payload);
    try {
      await fixture.whenStable();
      const taskOption = fixture.nativeElement.querySelector(`select[name="task"] option[value="${taskId}"]`) as HTMLOptionElement;
      const trackerOption = fixture.nativeElement.querySelector(`select[name="trackerTask"] option[value="${taskId}"]`) as HTMLOptionElement;
      expect(taskOption.textContent).toContain('Fieldwork');
      expect(trackerOption.textContent).toContain('Fieldwork');

      const myEntryRow = fixture.nativeElement.querySelector('[aria-labelledby="my-time-heading"] tbody tr') as HTMLTableRowElement;
      const approvalRow = fixture.nativeElement.querySelector('[aria-labelledby="queue-heading"] tbody tr') as HTMLTableRowElement;
      expect(myEntryRow.cells[2].textContent?.trim()).toBe('Completion');
      expect(approvalRow.cells[3].textContent?.trim()).toBe('UNASSIGNED');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('defaults an absent legacy start minute to 09:00 and sends midnight explicitly as zero', async () => {
    const oldEntry = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 30, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: '', status: 'DRAFT',
      mappingVersionId: null, fsliCode: null,
    };
    expect(decode(decodeTime, { ...workspace, myEntries: [oldEntry] }).myEntries[0].startMinute).toBe(540);
    const { fixture, component, http } = open();
    try {
      component.draft = { ...component.draft, task: taskId, date: '2026-10-15', startTime: '00:00', minutes: 30 };
      const saving = component.saveDraft();
      const post = http.expectOne('/api/ui/practice/time/entries');
      expect(post.request.body.startMinute).toBe(0);
      post.flush({ value: entryId }); await saving;
      http.expectOne('/api/ui/practice/time').flush(workspace);

      component.draft.minutes = 1.5;
      await component.saveDraft();
      http.expectNone(request => request.method === 'POST');
      expect(component.cmd.message()).toContain('whole-minute duration');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('returns submitted time with an explicit reason and surfaces retained feedback for resubmission', async () => {
    const submitted = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 60, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE',
      narrative: '', status: 'SUBMITTED', mappingVersionId: null, fsliCode: null, lastReturnReason: null,
    };
    const { fixture, component, http } = open({ ...workspace, isApprover: true, awaitingApproval: [submitted] });
    try {
      component.beginReturn(entryId);
      component.timeReturnReason.set('  Explain the source of the recorded hours.  ');
      fixture.detectChanges();
      const returning = component.returnTime(entryId);
      const returnRequest = http.expectOne(`/api/ui/practice/time/entries/${entryId}/return`);
      expect(returnRequest.request.body).toEqual({ reason: 'Explain the source of the recorded hours.' });
      returnRequest.flush({ value: true });
      await returning;
      http.expectOne('/api/ui/practice/time').flush({ ...workspace, myEntries: [{ ...submitted,
        status: 'RETURNED', lastReturnReason: 'Explain the source of the recorded hours.' }] });
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Latest return feedback: Explain the source of the recorded hours.');
      expect(fixture.nativeElement.textContent).toContain('Resubmit');
      const resubmitting = component.act(entryId, 'submit', 'Returned time was resubmitted for approval.');
      http.expectOne(`/api/ui/practice/time/entries/${entryId}/submit`).flush({ value: true });
      await resubmitting;
      http.expectOne('/api/ui/practice/time').flush(workspace);
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('pages the live independent approval queue without discarding recording or return drafts', async () => {
    const pendingEntry = (sequence: number) => ({
      id: `33333333-3333-4333-8333-${sequence.toString(16).padStart(12, '0')}`,
      workDate: '2026-10-15', taskId, taskTitle: 'Review scoped time', userId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      durationMinutes: 1, startMinute: 540, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: '',
      status: 'SUBMITTED', mappingVersionId: null, fsliCode: null, lastReturnReason: null, phase: 'FIELDWORK',
    });
    const rows = Array.from({ length: 50 }, (_, index) => pendingEntry(index + 1));
    const last = rows.at(-1)!;
    const firstPage = { ...workspace, isApprover: true, awaitingApproval: rows,
      approvalPage: { pageSize: 50, hasMore: true, nextCursor: { workDate: last.workDate, entryId: last.id } } };
    const laterPage = { ...workspace, isApprover: true, awaitingApproval: [pendingEntry(51)],
      approvalPage: { pageSize: 50, hasMore: false, nextCursor: null } };
    expect(() => decode(decodeTime, { ...firstPage, approvalPage: { ...firstPage.approvalPage, hasMore: false } })).toThrow();
    expect(() => decode(decodeTime, { ...firstPage, awaitingApproval: [...rows].reverse() })).toThrow();
    const { fixture, component, http } = open(firstPage);
    try {
      component.task.title = 'Keep this unsaved task';
      component.draft.narrative = 'Keep these unsaved recording details';
      component.beginReturn(rows[0].id);
      component.timeReturnReason.set('Keep this pending return reason');
      component.nextApprovalPage();
      expect(await component.act(rows[1].id, 'approve', 'Approved')).toBe(false);
      http.expectNone(request => request.url.startsWith('/api/ui/practice/time'));
      expect(component.timeReturnReason()).toBe('Keep this pending return reason');
      component.cancelReturn();
      component.nextApprovalPage(); TestBed.tick();
      const next = http.expectOne(request => request.url.startsWith('/api/ui/practice/time?'));
      const query = new URL(next.request.urlWithParams, 'http://localhost').searchParams;
      expect(query.get('approvalAfterWorkDate')).toBe(last.workDate);
      expect(query.get('approvalAfterEntryId')).toBe(last.id);
      next.flush(laterPage); TestBed.tick();
      expect(component.approvalPositions()).toHaveLength(2);
      expect(component.view.data()?.awaitingApproval[0].id).toBe(pendingEntry(51).id);
      expect(component.task.title).toBe('Keep this unsaved task');
      expect(component.draft.narrative).toBe('Keep these unsaved recording details');
      expect(fixture.nativeElement.querySelector('#queue-heading')).toBe(document.activeElement);
      component.previousApprovalPage(); TestBed.tick();
      http.expectOne('/api/ui/practice/time').flush(firstPage); TestBed.tick();
      expect(component.approvalPositions()).toHaveLength(1);
      component.nextApprovalPage(); TestBed.tick();
      http.expectOne(request => request.url.startsWith('/api/ui/practice/time?')).flush(laterPage); TestBed.tick();
      const approving = component.act(pendingEntry(51).id, 'approve', 'Approved');
      http.expectOne(`/api/ui/practice/time/entries/${pendingEntry(51).id}/approve`).flush({ value: true });
      await approving; TestBed.tick();
      http.expectOne('/api/ui/practice/time').flush(firstPage); TestBed.tick();
      expect(component.approvalPositions()).toHaveLength(1);
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('pages own recorded history at full timestamp precision independently of approvals and recovers read failures', () => {
    const savedEntry = (sequence: number) => ({
      id: `33333333-3333-4333-8333-${sequence.toString(16).padStart(12, '0')}`,
      workDate: '2026-10-15', createdAt: `2026-10-15T09:00:00.${((101 - sequence) * 10).toString().padStart(7, '0')}Z`,
      taskId, taskTitle: 'Retained own work', userId, durationMinutes: 1, startMinute: 540 + sequence * 2,
      activity: 'Fieldwork', billableClassification: 'NON_BILLABLE', narrative: '', status: 'APPROVED',
      mappingVersionId: null, fsliCode: null, lastReturnReason: null, phase: 'FIELDWORK',
    });
    const rows = Array.from({ length: 50 }, (_, index) => savedEntry(index + 1));
    const last = rows.at(-1)!;
    const approvalRows = rows.map(row => ({ ...row, id: row.id.replace('33333333-3333-4333-8333', '44444444-4444-4444-8444'),
      userId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', status: 'SUBMITTED' }));
    const lastApproval = approvalRows.at(-1)!;
    const firstPage = { ...workspace, isApprover: true, myEntries: rows, awaitingApproval: approvalRows,
      approvalPage: { pageSize: 50, hasMore: true, nextCursor: { workDate: lastApproval.workDate, entryId: lastApproval.id } },
      entriesPage: { pageSize: 50, hasMore: true, nextCursor: { workDate: last.workDate, createdAt: last.createdAt, entryId: last.id } } };
    const reviewLater = { ...firstPage, awaitingApproval: [], approvalPage: { pageSize: 50, hasMore: false, nextCursor: null } };
    const olderPage = { ...reviewLater, myEntries: [savedEntry(51)], entriesPage: { pageSize: 50, hasMore: false, nextCursor: null } };
    // Adjacent source times differ below JS Date precision; their retained order must survive.
    expect(Date.parse(rows[0].createdAt)).toBe(Date.parse(rows[1].createdAt));
    expect(decode(decodeTime, firstPage).myEntries[0].createdAt).toBe(rows[0].createdAt);
    expect(() => decode(decodeTime, { ...firstPage, myEntries: [...rows].reverse() })).toThrow();
    expect(() => decode(decodeTime, { ...firstPage, myEntries: [{ ...rows[0], createdAt: null }] })).toThrow();
    expect(() => decode(decodeTime, { ...firstPage, myEntries: [{ ...rows[0], createdAt: '2026-10-15T09:00:00.0001001Z' }] })).toThrow();
    const { fixture, component, http } = open(firstPage);
    try {
      component.task.title = 'Unsaved task remains';
      component.draft.narrative = 'Unsaved recording remains';
      component.nextApprovalPage(); TestBed.tick();
      http.expectOne(request => request.url.startsWith('/api/ui/practice/time?approvalAfterWorkDate='))
        .flush(reviewLater); TestBed.tick();
      component.nextEntriesPage(); TestBed.tick();
      const next = http.expectOne(request => request.url.includes('entriesBeforeCreatedAt='));
      const query = new URL(next.request.urlWithParams, 'http://localhost').searchParams;
      expect(query.get('entriesBeforeCreatedAt')).toBe(last.createdAt);
      expect(query.get('entriesBeforeEntryId')).toBe(last.id);
      expect(query.get('approvalAfterEntryId')).toBe(lastApproval.id);
      next.flush(olderPage); TestBed.tick();
      expect(component.entriesPositions()).toHaveLength(2);
      expect(component.approvalPositions()).toHaveLength(2);
      expect(fixture.nativeElement.querySelector('#my-time-heading')).toBe(document.activeElement);
      expect(component.task.title).toBe('Unsaved task remains');
      expect(component.draft.narrative).toBe('Unsaved recording remains');
      component.previousEntriesPage(); TestBed.tick();
      http.expectOne(request => request.url.includes('approvalAfterEntryId=') && !request.url.includes('entriesBefore'))
        .flush(reviewLater); TestBed.tick();
      component.nextEntriesPage(); TestBed.tick();
      http.expectOne(request => request.url.includes('entriesBeforeCreatedAt='))
        .flush({ code: 'time.invalid' }, { status: 400, statusText: 'Bad Request' }); TestBed.tick();
      expect(component.view.data()).toBeNull();
      expect(fixture.nativeElement.textContent).toContain('Reload time workspace from start');
      component.reloadTimeWorkspace(); TestBed.tick();
      http.expectOne('/api/ui/practice/time').flush(firstPage); TestBed.tick();
      expect(component.entriesPositions()).toHaveLength(1);
      expect(component.approvalPositions()).toHaveLength(1);
      expect(component.draft.narrative).toBe('Unsaved recording remains');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('requires explicit task selection, pauses active time, and saves reviewed segments one at a time', async () => {
    const now = new Date('2026-10-15T09:00:00Z').getTime();
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    const { fixture, component, http } = open();
    try {
      const data = component.view.data()!;
      expect(component.canStartTimer(data)).toBe(false);
      component.startTimer(data);
      expect(component.tracker().phase).toBe('IDLE');

      component.trackerTask.set(taskId);
      component.startTimer(data);
      expect(component.tracker().phase).toBe('RUNNING');
      clock.mockReturnValue(now + 10 * 60_000);
      component.pauseTimer();
      clock.mockReturnValue(now + 20 * 60_000);
      component.resumeTimer();
      clock.mockReturnValue(now + 25 * 60_000);
      component.stopTimer();
      expect(component.timerReview().rows.map(x => [x.workDate, x.startMinute, x.durationMinutes]))
        .toEqual([['2026-10-15', 540, 10], ['2026-10-15', 560, 5]]);
      expect(component.timerReview().excludedMilliseconds).toBe(0);
      http.expectNone(request => request.method === 'POST');

      component.useSegment(component.timerReview().rows[0]);
      expect(component.draft.startTime).toBe('09:00');
      expect(component.draft.minutes).toBe(10);
      const saving = component.saveDraft();
      const post = http.expectOne('/api/ui/practice/time/entries');
      expect(post.request.body).toMatchObject({ taskId, workDate: '2026-10-15', startMinute: 540, durationMinutes: 10 });
      post.flush({ value: entryId });
      await saving;
      expect(component.savedSegments().has(1)).toBe(true);
      expect(component.savedSegments().has(2)).toBe(false);
      http.expectOne('/api/ui/practice/time').flush(workspace);
      fixture.detectChanges();
    } finally {
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); clock.mockRestore();
    }
  });

  it('keeps a definite overlap refusal reviewable and freezes additional saves after an unknown result', async () => {
    const reviewWorkspace = { ...workspace, isApprover: true, awaitingApproval: [{
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Independent time review',
      userId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', durationMinutes: 1, startMinute: 540,
      activity: 'Fieldwork', billableClassification: 'NON_BILLABLE', narrative: '', status: 'SUBMITTED',
      mappingVersionId: null, fsliCode: null, lastReturnReason: null,
    }] };
    const { fixture, component, http } = open(reviewWorkspace);
    try {
      const now = new Date('2026-10-15T09:00:00Z').getTime();
      vi.spyOn(Date, 'now').mockReturnValue(now);
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      vi.spyOn(Date, 'now').mockReturnValue(now + 10 * 60_000);
      component.stopTimer();
      component.useSegment(component.timerReview().rows[0]);
      const first = component.saveDraft();
      http.expectOne('/api/ui/practice/time/entries').flush(
        { code: 'time.overlap', message: 'The time entry overlaps another active entry.' },
        { status: 400, statusText: 'Bad Request' },
      );
      await first;
      expect(component.savedSegments().has(1)).toBe(false);
      expect(component.draft.minutes).toBe(10);
      http.expectOne('/api/ui/practice/time').flush(reviewWorkspace);

      const second = component.saveDraft();
      http.expectOne('/api/ui/practice/time/entries').flush(
        { message: 'temporary failure' }, { status: 503, statusText: 'Service Unavailable' },
      );
      await second;
      expect(component.cmd.uncertain()).toBe(true);
      expect(component.unconfirmedSegment()).toBe(1);
      http.expectOne('/api/ui/practice/time').flush(reviewWorkspace);
      component.beginReturn(entryId);
      component.timeReturnReason.set('Retain this unsaved return reason');
      component.refreshRecordedTime();
      http.expectOne('/api/ui/practice/time').flush(reviewWorkspace);
      expect(component.returningTimeId()).toBe(entryId);
      expect(component.timeReturnReason()).toBe('Retain this unsaved return reason');
      expect(component.cmd.uncertain()).toBe(true);
      expect(component.unconfirmedSegment()).toBe(1);
      const blockedRetry = component.saveDraft();
      await blockedRetry;
      http.expectNone(request => request.method === 'POST');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks(); }
  });

  it('pauses on page hide, clears timer state on session invalidation, and requires an explicit discard before navigation', async () => {
    const { fixture, component, http, session } = open();
    const now = new Date('2026-10-15T09:00:00Z').getTime();
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    try {
      const dialog = fixture.debugElement.injector.get(MatDialog);
      const choices = ['keep', 'discard'];
      const openDialog = vi.spyOn(dialog, 'open').mockImplementation(() => {
        const choice = choices.shift() ?? 'keep';
        return { afterClosed: () => of(choice) } as never;
      });
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      clock.mockReturnValue(now + 60_000);
      const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
      document.dispatchEvent(new Event('visibilitychange'));
      visibility.mockRestore();
      expect(component.tracker().phase).toBe('PAUSED');
      clock.mockReturnValue(now + 120_000);
      component.resumeTimer();
      clock.mockReturnValue(now + 180_000);
      window.dispatchEvent(new Event('pagehide'));
      expect(component.tracker().phase).toBe('PAUSED');
      expect(await component.confirmNavigation()).toBe(false);
      expect(openDialog).toHaveBeenCalledTimes(1);
      expect(await component.confirmNavigation()).toBe(true);
      expect(openDialog).toHaveBeenCalledTimes(2);
      expect(component.tracker().phase).toBe('IDLE');
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      session.clear(); TestBed.tick(); fixture.detectChanges();
      expect(component.tracker().phase).toBe('IDLE');
      expect(component.trackerTask()).toBe('');
      expect(await component.confirmNavigation()).toBe(true);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); vi.restoreAllMocks(); }
  });

  it('freezes an unmeasurable backwards-clock interval without recording its open part', () => {
    const { fixture, component, http } = open();
    const now = new Date('2026-10-15T09:00:00Z').getTime();
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    try {
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      clock.mockReturnValue(now - 1);
      component.pauseTimer();
      expect(component.tracker().phase).toBe('STOPPED');
      expect(component.tracker().intervals).toEqual([]);
      expect(component.timerNotice()).toBe('Timer clock or interval limit changed. The open interval was not recorded; review completed intervals or enter time manually.');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); clock.mockRestore(); }
  });
});
