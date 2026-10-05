import { describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { BehaviorSubject, Subject } from 'rxjs';
import { decodeWorkpaper, WorkpaperEditor } from './workpaper';
import { SessionService } from '../../core/session';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';

describe('Audit Workpaper Contracts', () => {
  it('decodes a valid workpaper payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      index: 'C.01',
      title: 'Cash and Bank Testing',
      objective: 'Verify existence and completeness of bank balances at year end.',
      templateVersion: '1.0',
      procedure: 'Perform bank reconciliation testing for all open bank accounts.',
      linkedProcedureTitle: 'Bank reconciliations',
      revision: 1,
      status: 'WORKING',
      workPerformed: 'Tested reconciliation between GL and bank statements.',
      conclusion: 'Reconciliation is complete and free of material difference.',
      createdAt: '2026-10-01T08:00:00Z',
      submittedAt: null,
      submissions: [
        {
          revision: 1,
          submittedAt: '2026-10-01T12:00:00Z',
          conclusion: 'Initial review conclusion.',
        },
      ],
      draft: {
        workpaperId: id1,
        draftId: id3,
        baseWorkpaperRevision: 1,
        baseInputGeneration: 1,
        basePolicyGeneration: 1,
        draftRevision: 2,
        workPerformed: 'Tested reconciliation between GL and bank statements.',
        conclusion: 'Reconciliation is complete and free of material difference.',
        lastSaveId: id2,
        lastSavedAt: '2026-10-01T11:45:00Z',
        lifecycle: 'ACTIVE',
      },
    };

    const decoded = decodeWorkpaper(raw, 'workpaper');
    expect(decoded.id).toBe(id1);
    expect(decoded.engagementId).toBe(id2);
    expect(decoded.index).toBe('C.01');
    expect(decoded.status).toBe('WORKING');
    expect(decoded.submissions.length).toBe(1);
    expect(decoded.draft.draftRevision).toBe(2);
    expect(decoded.draft.lifecycle).toBe('ACTIVE');
  });

  it('rejects invalid workpaper payloads', () => {
    expect(() => decodeWorkpaper(null, 'workpaper')).toThrow();
    expect(() => decodeWorkpaper({}, 'workpaper')).toThrow();
  });
});

const draftPayload = () => ({
  workpaperId: id1, draftId: id3, baseWorkpaperRevision: 1, baseInputGeneration: 1, basePolicyGeneration: 1,
  draftRevision: 2, workPerformed: 'Worked.', conclusion: 'Clear.', lastSaveId: id2, lastSavedAt: '2026-10-01T11:45:00Z', lifecycle: 'ACTIVE',
});
const workpaperPayload = () => ({
  id: id1, engagementId: id2, index: 'C.01', title: 'Cash and Bank Testing', objective: 'Verify balances.',
  templateVersion: '1.0', procedure: 'Reconciliation testing.', linkedProcedureTitle: null, revision: 1, status: 'WORKING',
  workPerformed: null, conclusion: null, createdAt: '2026-10-01T08:00:00Z', submittedAt: null, submissions: [], draft: draftPayload(),
});
const draftUrl = `/api/ui/audit/workpapers/${id1}/draft`;
const readUrl = `/api/ui/audit/workpapers/${id1}`;

describe('Workpaper authored-content protection', () => {
  let http: HttpTestingController;
  let routeParams: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let decisions: Subject<string | undefined>;
  const dialogSpy = {
    open: vi.fn(() => {
      decisions = new Subject<string | undefined>();
      return { afterClosed: () => decisions.asObservable() };
    }),
  };
  beforeEach(() => {
    routeParams = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({
      imports: [WorkpaperEditor],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: MatDialog, useValue: dialogSpy },
        { provide: ActivatedRoute, useValue: { paramMap: routeParams, snapshot: { paramMap: convertToParamMap({ id: id1 }) } } }],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id1, firmId: id1, generation: '1', staff: true });
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() {
    const f = TestBed.createComponent(WorkpaperEditor);
    f.detectChanges();
    http.expectOne(readUrl).flush(workpaperPayload());
    f.detectChanges(); TestBed.tick();
    return f;
  }
  const acknowledged = () => ({ value: { ...draftPayload(), draftRevision: 3, lastSavedAt: '2026-10-01T11:46:00Z' } });
  async function decide(choice: 'save' | 'discard' | 'keep') {
    decisions.next(choice);
  }

  it('offers save, discard or stay when navigating inside the autosave debounce, and an acknowledged save releases the guard', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Typed before navigation.'; c.changed();
    const keep = c.confirmNavigation();
    expect(dialogSpy.open).toHaveBeenCalledTimes(1);
    await decide('keep');
    await expect(keep).resolves.toBe(false);
    expect(http.match(r => r.url === draftUrl).length).toBe(0); // no write was triggered by staying
    const nav = c.confirmNavigation();
    await decide('save');
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl).flush(acknowledged());
    await expect(nav).resolves.toBe(true);
  });

  it('awaits an in-flight autosave and proceeds without a second write when it is acknowledged', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Typed content.'; c.changed();
    const saving = c.save();
    const nav = c.confirmNavigation();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl).flush(acknowledged());
    await expect(saving).resolves.toBe(true);
    await expect(nav).resolves.toBe(true);
    expect(http.match(r => r.url === draftUrl).length).toBe(0); // no second write was issued
  });

  it('blocks navigation when the save is refused and keeps the failure visible', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Refused content.'; c.changed();
    const nav = c.confirmNavigation();
    await decide('save');
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl)
      .flush({ code: 'request.failed', message: 'The draft save was refused.' }, { status: 403, statusText: 'Forbidden' });
    await expect(nav).resolves.toBe(false);
    expect(c.failed()).toBe(true);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('The draft save was refused.');
  });

  it('clears a refused-save error after the corrected draft is acknowledged', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Invalid draft.'; c.changed();
    const rejected = c.save();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl)
      .flush({ code: 'request.invalid', message: 'The draft save was refused.' }, { status: 400, statusText: 'Bad Request' });
    await expect(rejected).resolves.toBe(false);
    expect(c.failed()).toBe(true);

    c.work = 'Corrected draft.'; c.changed();
    const recovered = c.save();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl).flush(acknowledged());
    await expect(recovered).resolves.toBe(true);
    expect(c.failed()).toBe(false);
    expect(c.message()).toBe('Draft saved.');
    expect(c.status()).toContain('Saved at');
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('The draft save was refused.');
  });

  it('clears prior save feedback when the reused route changes workpaper identity', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Refused draft.'; c.changed();
    const saving = c.save();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl)
      .flush({ code: 'request.failed', message: 'The draft save was refused.' }, { status: 400, statusText: 'Bad Request' });
    await expect(saving).resolves.toBe(false);
    expect(c.failed()).toBe(true);

    routeParams.next(convertToParamMap({ id: id2 }));
    TestBed.tick();
    const next = workpaperPayload();
    next.id = id2;
    next.engagementId = id3;
    next.draft = { ...draftPayload(), workpaperId: id2 };
    http.expectOne(`/api/ui/audit/workpapers/${id2}`).flush(next);
    TestBed.tick(); f.detectChanges();

    expect(c.failed()).toBe(false);
    expect(c.message()).toBe('');
    expect(f.nativeElement.textContent).not.toContain('The draft save was refused.');
  });

  it('checks the persisted draft after a lost acknowledgement and confirms the exact save without resending', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Unacknowledged work.'; c.conclusion = 'Unacknowledged conclusion.'; c.changed();
    const saving = c.save();
    const request = http.expectOne(r => r.method === 'POST' && r.url === draftUrl);
    const attempt = request.request.body;
    request.flush({}, { status: 500, statusText: 'Server Error' });
    await expect(saving).resolves.toBe(false);
    expect(c.uncertain()).toBe(true);
    await expect(c.confirmNavigation()).resolves.toBe(false);
    expect(http.match(r => r.method === 'POST' && r.url === draftUrl).length).toBe(0);

    const check = c.checkSaveOutcome();
    http.expectOne(r => r.method === 'GET' && r.url === readUrl).flush({
      ...workpaperPayload(),
      draft: { ...draftPayload(), draftRevision: attempt.expectedDraftRevision + 1,
        lastSaveId: attempt.saveId, workPerformed: attempt.workPerformed.trim(), conclusion: attempt.conclusion.trim() },
    });
    await check;
    expect(c.uncertain()).toBe(false);
    expect(c.retryReady()).toBe(false);
    expect(c.failed()).toBe(false);
    expect(c.message()).toBe('The persisted draft confirms that save.');
    expect(http.match(r => r.method === 'POST' && r.url === draftUrl).length).toBe(0);
  });

  it('offers only the same idempotent save after the server confirms the draft revision did not advance', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Retryable work.'; c.conclusion = 'Retryable conclusion.'; c.changed();
    const saving = c.save();
    const first = http.expectOne(r => r.method === 'POST' && r.url === draftUrl);
    const attempt = first.request.body;
    first.flush({}, { status: 500, statusText: 'Server Error' });
    await expect(saving).resolves.toBe(false);

    const check = c.checkSaveOutcome();
    http.expectOne(r => r.method === 'GET' && r.url === readUrl).flush(workpaperPayload());
    await check;
    expect(c.uncertain()).toBe(true);
    expect(c.retryReady()).toBe(true);

    const retry = c.retryExactSave();
    const second = http.expectOne(r => r.method === 'POST' && r.url === draftUrl);
    expect(second.request.body).toEqual({
      expectedDraftRevision: attempt.expectedDraftRevision,
      baseWorkpaperRevision: attempt.baseWorkpaperRevision,
      baseInputGeneration: attempt.baseInputGeneration,
      basePolicyGeneration: attempt.basePolicyGeneration,
      saveId: attempt.saveId,
      workPerformed: attempt.workPerformed,
      conclusion: attempt.conclusion,
    });
    second.flush({ value: { ...draftPayload(), draftRevision: attempt.expectedDraftRevision + 1,
      lastSaveId: attempt.saveId, workPerformed: attempt.workPerformed.trim(), conclusion: attempt.conclusion.trim() } });
    await retry;
    expect(c.uncertain()).toBe(false);
    expect(c.retryReady()).toBe(false);
    expect(c.failed()).toBe(false);
    expect(c.message()).toBe('The exact draft save was confirmed.');
  });

  it('clears workpaper content when access is revoked during save reconciliation', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Protected workpaper content.'; c.changed();
    const saving = c.save();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl)
      .flush({}, { status: 500, statusText: 'Server Error' });
    await expect(saving).resolves.toBe(false);
    expect(c.uncertain()).toBe(true);

    const check = c.checkSaveOutcome();
    http.expectOne(r => r.method === 'GET' && r.url === readUrl)
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    await check;
    http.expectOne(r => r.method === 'GET' && r.url === readUrl)
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    f.detectChanges();
    expect(c.wp.data()).toBeNull();
    expect(c.uncertain()).toBe(false);
    expect(f.nativeElement.textContent).not.toContain('Protected workpaper content.');
  });

  it('reports a conflicted target, blocks the save, and allows leaving without a further save attempt', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Stale content.'; c.changed();
    const saving = c.save();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl)
      .flush({ code: 'revision.stale', message: 'stale' }, { status: 409, statusText: 'Conflict' });
    await expect(saving).resolves.toBe(false);
    expect(c.conflict()).toBe(true);
    const nav = c.confirmNavigation();
    await expect(nav).resolves.toBe(true);
    expect(http.match(r => r.url === draftUrl).length).toBe(0); // no further save attempt
  });

  it('discards the server draft first when the practitioner chooses to discard, and a refused discard stays', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'To discard.'; c.changed();
    const nav = c.confirmNavigation();
    await decide('discard');
    http.expectOne(r => r.method === 'POST' && r.url === `/api/ui/audit/workpapers/${id1}/draft/discard`).flush({ value: true });
    await Promise.resolve(); await Promise.resolve(); TestBed.tick();
    http.expectOne(readUrl).flush(workpaperPayload()); // the reload after an acknowledged discard
    await expect(nav).resolves.toBe(true);
  });

  it('prevents beforeunload while changes are unacknowledged or a save is unresolved', async () => {
    const f = open(); const c = f.componentInstance;
    c.work = 'Unload risk.'; c.changed();
    const blocked = { preventDefault: vi.fn(), returnValue: '' } as unknown as BeforeUnloadEvent;
    c.beforeUnload(blocked);
    expect(blocked.preventDefault).toHaveBeenCalledTimes(1);
    const saving = c.save();
    http.expectOne(r => r.method === 'POST' && r.url === draftUrl).flush(acknowledged());
    await expect(saving).resolves.toBe(true);
    const allowed = { preventDefault: vi.fn(), returnValue: '' } as unknown as BeforeUnloadEvent;
    c.beforeUnload(allowed);
    expect(allowed.preventDefault).not.toHaveBeenCalled();
  });
});
