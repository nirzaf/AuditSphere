import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { WorkQueueActionRegistry, type WorkQueueRegistryOwner } from './work-queue-action-registry';
import { WorkflowTaskCancellationPanel, type WorkflowTaskRecoveryAction } from './workflow-task-cancellation';

const firm = '11111111-1111-4111-8111-111111111111';
const user = '22222222-2222-4222-8222-222222222222';
const engagement = '33333333-3333-4333-8333-333333333333';
const taskId = '44444444-4444-4444-8444-444444444444';
const key = `auditsphere.work-queue-actions.v1:${firm}:${user}`;
const task = { taskId, clientId: '55555555-5555-4555-8555-555555555555', clientName: 'Synthetic client',
  engagementId: engagement, engagementServiceRoute: 'AUDIT', engagementPeriod: 'FY2026', title: 'Synthetic task',
  taskStatus: 'OPEN', revision: 4, canCancel: true, canReopen: false, canSuspend: true, canResume: true };

describe('persistent task recovery fence', () => {
  afterEach(() => { TestBed.resetTestingModule(); sessionStorage.removeItem(key); });

  function setup(action: WorkflowTaskRecoveryAction = 'cancel', storage?: () => Storage) {
    sessionStorage.removeItem(key);
    TestBed.configureTestingModule({ imports: [WorkflowTaskCancellationPanel], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService);
    session.current.set({ firmId: firm, userId: user, generation: '1', staff: true });
    const currentOwner = (): WorkQueueRegistryOwner | null => {
      const actor = session.current();
      return actor ? { firmId: actor.firmId, userId: actor.userId, generation: actor.generation,
        invalidation: session.invalidation(), staff: actor.staff } : null;
    };
    const registry = new WorkQueueActionRegistry(currentOwner, storage);
    const fixture = TestBed.createComponent(WorkflowTaskCancellationPanel);
    fixture.componentRef.setInput('task', action === 'reopen' ? { ...task, taskStatus: 'CANCELLED', canReopen: true } : task);
    fixture.componentRef.setInput('action', action);
    fixture.componentRef.setInput('recoveryRegistry', registry);
    fixture.detectChanges(); TestBed.tick();
    return { fixture, panel: fixture.componentInstance, session, registry, currentOwner, http: TestBed.inject(HttpTestingController) };
  }

  for (const action of ['cancel', 'reopen', 'suspend', 'resume'] as const) {
    it(`persists ${action} before sending without storing its reason and retains it across a session change`, async () => {
      const { fixture, panel, session, registry, currentOwner, http } = setup(action);
      try {
        panel.updateReason('Private synthetic reason'); panel.prepareReview(); panel.assent.set(true);
        const sending = panel.submit();
        const post = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/${action}`);
        expect(post.request.body).toEqual({ expectedRevision: 4, reason: 'Private synthetic reason' });
        expect(sessionStorage.getItem(key)).not.toContain('Private synthetic reason');
        expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ action, taskId, expectedRevision: 4 }] });
        session.clear(); TestBed.tick();
        post.flush({ value: true }); await sending;
        session.current.set({ firmId: firm, userId: user, generation: '2', staff: true }); TestBed.tick();
        expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ action, taskId }] });
        expect(panel.activeRegistryRequest()).toBeNull();
        http.verify({ ignoreCancelled: true });
      } finally { fixture.destroy(); }
    });
  }

  it('retains unknown outcomes and requires explicit advanced observation to acknowledge', async () => {
    const { fixture, panel, registry, currentOwner, http } = setup();
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Unknown' });
      await sending; expect(panel.state()).toBe('unknown');
      const unchanged = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 4, status: 'OPEN' }] });
      await unchanged; expect(panel.state()).toBe('retryable');
      await panel.submit(); http.expectNone(r => r.method === 'POST');
      panel.invalidateAvailability();
      const advanced = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 5, status: 'CANCELLED' }] });
      await advanced; expect(panel.state()).toBe('observed');
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ taskId }] });
      const captured = panel.activeRegistryRequest();
      expect(captured?.requestId).toBeTruthy();
      panel.acknowledgeAndReviewActivity();
      expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('rechecks the exact task before reconciling an acknowledgement whose readback failed', async () => {
    let raw: string | null = null, failNextRead = false, injectFailure = false;
    const storage = { getItem: () => { if (failNextRead) { failNextRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (injectFailure) { injectFailure = false; failNextRead = true; } } } as unknown as Storage;
    const { fixture, panel, registry, currentOwner, http } = setup('cancel', () => storage);
    const exactUrl = `/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`;
    const observed = { engagementId: engagement, truncated: false, tasks: [{ id: taskId, assigneeUserId: null,
      assigneeDisplayName: null, revision: 5, status: 'CANCELLED' }] };
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Unknown' }); await sending;
      const first = panel.checkStatus(); http.expectOne(exactUrl).flush(observed); await first;
      injectFailure = true; panel.acknowledgeAndReviewActivity();
      expect(panel.state()).toBe('unknown'); expect(panel.observed()).toBeNull();
      expect(registry.pendingReconciliation(currentOwner()!)).toMatchObject({ ok: true, value: { kind: 'acknowledgement', sameSession: true } });
      panel.acknowledgeAndReviewActivity();
      expect(registry.pendingReconciliation(currentOwner()!)).toMatchObject({ ok: true, value: { kind: 'acknowledgement' } });
      const second = panel.checkStatus(); http.expectOne(exactUrl).flush(observed); await second;
      panel.acknowledgeAndReviewActivity();
      expect(panel.activeRegistryRequest()).toBeNull();
      expect(registry.pendingReconciliation(currentOwner()!)).toEqual({ ok: true, value: null });
      expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      http.expectNone(request => request.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  for (const action of ['cancel', 'reopen', 'suspend', 'resume'] as const) {
    it(`requires fresh observation and renewed assent to retry the original ${action} payload on the same fence`, async () => {
      const { fixture, panel, registry, currentOwner, http } = setup(action);
      try {
        panel.updateReason('Exact private reason'); panel.prepareReview(); panel.assent.set(true);
        const original = panel.submit();
        const first = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/${action}`);
        const originalBody = first.request.body;
        const requestId = panel.activeRegistryRequest()!.requestId;
        first.flush({}, { status: 503, statusText: 'Unknown' }); await original;
        http.expectNone(r => r.method === 'POST');
        const check = panel.checkStatus();
        http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
          tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 4,
            status: action === 'reopen' ? 'CANCELLED' : 'OPEN', onHold: action === 'resume' }] });
        await check; expect(panel.state()).toBe('retryable');
        await panel.submit(); http.expectNone(r => r.method === 'POST');
        panel.assent.set(true); const retry = panel.submit();
        const post = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/${action}`);
        expect(post.request.body).toEqual(originalBody);
        expect(panel.activeRegistryRequest()?.requestId).toBe(requestId);
        expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ requestId, taskId }] });
        expect(sessionStorage.getItem(key)).not.toContain('Exact private reason');
        post.flush({}, { status: 403, statusText: 'Forbidden' }); await retry;
        expect(panel.state()).toBe('unknown');
        expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ requestId, taskId }] });
        http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true });
      } finally { fixture.destroy(); }
    });
  }

  it('clears the same fence on a successful explicit exact retry', async () => {
    const { fixture, panel, registry, currentOwner, http } = setup();
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const original = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Unknown' }); await original;
      const checking = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 4, status: 'OPEN' }] });
      await checking; panel.assent.set(true);
      const retry = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`).flush({ value: true }); await retry;
      expect(panel.state()).toBe('confirmed'); expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('does not offer retry while an invalidated original request is still awaiting its response', async () => {
    const { fixture, panel, registry, currentOwner, http } = setup();
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      const original = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`);
      const requestId = panel.activeRegistryRequest()!.requestId;
      panel.invalidateAvailability();
      expect(panel.busy()).toBe(false);
      const firstCheck = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 4, status: 'OPEN' }] });
      await firstCheck; fixture.detectChanges();
      expect(panel.state()).toBe('unknown');
      expect(panel.message()).toContain('still awaiting a response');
      expect(fixture.nativeElement.querySelector('input[type="checkbox"]')).toBeNull();
      panel.assent.set(true); await panel.submit(); http.expectNone(r => r.method === 'POST');
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ requestId, taskId }] });

      original.flush({}, { status: 503, statusText: 'Unknown' }); await sending;
      expect(panel.state()).toBe('unknown'); http.expectNone(r => r.method === 'POST');
      const laterCheck = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 4, status: 'OPEN' }] });
      await laterCheck;
      expect(panel.state()).toBe('retryable'); expect(panel.assent()).toBe(false);
      await panel.submit(); http.expectNone(r => r.method === 'POST');
      panel.assent.set(true);
      const retry = panel.submit();
      const resent = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`);
      expect(resent.request.body).toEqual(original.request.body);
      expect(panel.activeRegistryRequest()?.requestId).toBe(requestId);
      resent.flush({}, { status: 503, statusText: 'Unknown' }); await retry;
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ requestId, taskId }] });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('does not retry an observation after the availability revision or session changes', async () => {
    const { fixture, panel, session, registry, currentOwner, http } = setup();
    const accessRevision = signal(0);
    fixture.componentRef.setInput('accessRevision', accessRevision); fixture.detectChanges(); TestBed.tick();
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const original = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Unknown' }); await original;
      const checking = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 4, status: 'OPEN' }] });
      await checking; accessRevision.set(1); panel.assent.set(true); await panel.submit();
      http.expectNone(r => r.method === 'POST');
      session.clear(); TestBed.tick();
      session.current.set({ firmId: firm, userId: user, generation: '2', staff: true }); TestBed.tick();
      panel.assent.set(true); await panel.submit(); http.expectNone(r => r.method === 'POST');
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ taskId }] });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('keeps existing standalone submission available when persistence is not configured', async () => {
    const { fixture, panel, http } = setup();
    fixture.componentRef.setInput('recoveryRegistry', null); fixture.detectChanges(); TestBed.tick();
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`).flush({ value: true });
      await sending;
      expect(panel.state()).toBe('confirmed'); expect(sessionStorage.getItem(key)).toBeNull();
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('does not acknowledge an observation after the availability revision changes', async () => {
    const { fixture, panel, registry, currentOwner, http } = setup();
    const accessRevision = signal(0);
    fixture.componentRef.setInput('accessRevision', accessRevision); fixture.detectChanges(); TestBed.tick();
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Unknown' });
      await sending;
      const checking = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ engagementId: engagement, truncated: false,
        tasks: [{ id: taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 5, status: 'CANCELLED' }] });
      await checking; expect(panel.state()).toBe('observed');
      accessRevision.set(1); panel.acknowledgeAndReviewActivity();
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ taskId }] });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('aborts a verified reservation when access changes before invocation', async () => {
    const { fixture, panel, registry, currentOwner, http } = setup();
    const access = signal(true);
    fixture.componentRef.setInput('accessCurrent', access); fixture.detectChanges(); TestBed.tick();
    const subscription = panel.registryChanged.subscribe(() => access.set(false));
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      await panel.submit();
      http.expectNone(r => r.method === 'POST');
      expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      expect(panel.message()).toContain('No action was sent');
      http.verify({ ignoreCancelled: true });
    } finally { subscription.unsubscribe(); fixture.destroy(); }
  });

  it('automatically clears a verified unsent reservation after a transient dispatch read failure', async () => {
    let raw: string | null = null, reads = 0;
    const storage = { getItem: () => { if (++reads === 3) throw new Error('transient read'); return raw; },
      setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const { fixture, panel, registry, currentOwner, http } = setup('cancel', () => storage);
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      await panel.submit(); http.expectNone(r => r.method === 'POST');
      expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      expect(panel.activeRegistryRequest()).toBeNull(); expect(panel.state()).toBe('review');
      expect(panel.assent()).toBe(false); expect(panel.message()).toContain('No action was sent');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('keeps the unsent ticket for an explicit cleanup retry without posting', async () => {
    let raw: string | null = null, reads = 0, blocked = false;
    const storage = { getItem: () => { if (++reads === 3) blocked = true; if (blocked) throw new Error('unavailable'); return raw; },
      setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const { fixture, panel, registry, currentOwner, http } = setup('cancel', () => storage);
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      await panel.submit(); http.expectNone(r => r.method === 'POST');
      expect(panel.state()).toBe('cleanup'); expect(panel.hasUnresolvedOutcome()).toBe(true);
      const requestId = panel.activeRegistryRequest()!.requestId;
      panel.retryUnsentCleanup(); expect(panel.state()).toBe('cleanup');
      expect(panel.activeRegistryRequest()?.requestId).toBe(requestId);
      blocked = false; panel.retryUnsentCleanup();
      expect(panel.state()).toBe('review'); expect(panel.assent()).toBe(false);
      expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      expect(panel.activeRegistryRequest()).toBeNull();
      http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('does not clean an unsent reservation after the issuing session changes', async () => {
    let raw: string | null = null, reads = 0, blocked = false;
    const storage = { getItem: () => { if (++reads === 3) blocked = true; if (blocked) throw new Error('unavailable'); return raw; },
      setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const { fixture, panel, session, registry, currentOwner, http } = setup('cancel', () => storage);
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      await panel.submit(); expect(panel.state()).toBe('cleanup');
      blocked = false; session.clear();
      panel.retryUnsentCleanup();
      session.current.set({ firmId: firm, userId: user, generation: '2', staff: true }); TestBed.tick();
      panel.retryUnsentCleanup();
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ action: 'cancel', taskId }] });
      http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  it('retains a cleanup-only ticket after reservation readback fails and never sends the action', async () => {
    let raw: string | null = null, reads = 0, blocked = false;
    const storage = { getItem: () => { if (++reads === 2) blocked = true; if (blocked) throw new Error('readback failed'); return raw; },
      setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const { fixture, panel, registry, currentOwner, http } = setup('cancel', () => storage);
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true); await panel.submit();
      http.expectNone(r => r.method === 'POST'); expect(raw).not.toBeNull();
      expect(panel.state()).toBe('cleanup'); expect(panel.activeRegistryRequest()).not.toBeNull();
      blocked = false;
      expect(registry.read(currentOwner()!)).toMatchObject({ ok: true, value: [{ action: 'cancel', taskId }] });
      panel.retryUnsentCleanup();
      expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
      expect(panel.state()).toBe('review'); expect(panel.message()).toContain('No action was sent');
      http.expectNone(r => r.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); }
  });

  for (const confirmed of [true, false]) {
    it(`reconciles an uncertain definitive removal for a ${confirmed ? 'successful' : 'refused'} response without resending`, async () => {
      let raw: string | null = null, writes = 0, failNextRead = false;
      const storage = { getItem: () => { if (failNextRead) { failNextRead = false; throw new Error('readback'); } return raw; },
        setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failNextRead = true; } } as unknown as Storage;
      const { fixture, panel, registry, currentOwner, http } = setup('cancel', () => storage);
      try {
        panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
        const sending = panel.submit();
        const post = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`);
        if (confirmed) post.flush({ value: true }); else post.flush({}, { status: 403, statusText: 'Forbidden' });
        await sending; expect(panel.state()).toBe('finalizing'); expect(panel.hasUnresolvedOutcome()).toBe(true);
        expect(registry.read(currentOwner()!)).toEqual({ ok: true, value: [] });
        expect(panel.activeRegistryRequest()).toMatchObject({ engagementId: engagement, taskId, action: 'cancel', expectedRevision: 4 });
        const unrelated = { engagementId: engagement, taskId: '66666666-6666-4666-8666-666666666666', expectedRevision: 4,
          action: 'complete' as const, requestedDueDate: null, requestId: '77777777-7777-4777-8777-777777777777' };
        expect(registry.reserve(currentOwner()!, unrelated)).toEqual({ ok: false, reason: 'reconciliation_pending' });
        panel.retryDefinitiveCleanup();
        expect(panel.state()).toBe(confirmed ? 'confirmed' : 'refused');
        expect(panel.activeRegistryRequest()).toBeNull();
        expect(registry.reserve(currentOwner()!, unrelated).ok).toBe(true);
        http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true });
      } finally { fixture.destroy(); }
    });
  }

  it('does not apply a captured definitive outcome to a newer session', async () => {
    let raw: string | null = null, writes = 0, failNextRead = false;
    const storage = { getItem: () => { if (failNextRead) { failNextRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failNextRead = true; } } as unknown as Storage;
    const { fixture, panel, session, http } = setup('cancel', () => storage);
    let changed = 0;
    const subscription = panel.changed.subscribe(() => changed++);
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`).flush({ value: true });
      await sending; expect(panel.state()).toBe('finalizing'); expect(changed).toBe(0);
      session.clear(); panel.retryDefinitiveCleanup();
      session.current.set({ firmId: firm, userId: user, generation: '2', staff: true }); TestBed.tick();
      panel.retryDefinitiveCleanup();
      expect(changed).toBe(0); expect(panel.state()).not.toBe('confirmed');
      http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { subscription.unsubscribe(); fixture.destroy(); }
  });

  it('emits scope-check failure for current errors but not for a stale session result', async () => {
    const { fixture, panel, session, http } = setup();
    let failures = 0;
    const subscription = panel.scopeCheckFailed.subscribe(() => failures++);
    try {
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks/${taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Unknown' }); await sending;
      const forbidden = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`)
        .flush({ message: 'Sensitive details' }, { status: 403, statusText: 'Forbidden' });
      await forbidden; expect(failures).toBe(1); expect(panel.message()).not.toContain('Sensitive details');
      const malformed = panel.checkStatus();
      http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`).flush({ invalid: true });
      await malformed; expect(failures).toBe(2);
      const late = panel.checkStatus();
      const request = http.expectOne(`/api/ui/engagements/${engagement}/workflow/tasks?taskId=${taskId}`);
      session.clear(); TestBed.tick();
      request.flush({}, { status: 403, statusText: 'Forbidden' }); await late;
      expect(failures).toBe(2); http.verify({ ignoreCancelled: true });
    } finally { subscription.unsubscribe(); fixture.destroy(); }
  });
});
