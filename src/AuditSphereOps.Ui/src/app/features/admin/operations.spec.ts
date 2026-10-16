import { describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { decodeOperations } from './operations';
import { Operations } from './operations';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';

describe('Durable Operations Console Contracts', () => {
  it('decodes a valid operations console payload', () => {
    const raw = {
      operatingMode: 'LOCAL_ONLY',
      retryableStates: ['RETRY_WAIT', 'FAILED', 'DEAD_LETTER'],
      operations: [
        {
          id: id1,
          kind: 'GL_COMPLETENESS_CALCULATION',
          status: 'COMPLETED',
          attemptCount: 1,
          nextAttemptAt: null,
          errorCode: null,
          cancellationDisposition: null,
        },
        {
          id: id2,
          kind: 'PBC_DOCUMENT_TRANSFER',
          status: 'RETRY_WAIT',
          attemptCount: 2,
          nextAttemptAt: '2026-10-03T05:00:00Z',
          errorCode: 'NETWORK_TIMEOUT',
          cancellationDisposition: null,
        },
      ],
    };

    const decoded = decodeOperations(raw, 'operations');
    expect(decoded.operatingMode).toBe('LOCAL_ONLY');
    expect(decoded.retryableStates).toContain('RETRY_WAIT');
    expect(decoded.operations.length).toBe(2);
    expect(decoded.operations[0].status).toBe('COMPLETED');
    expect(decoded.operations[1].errorCode).toBe('NETWORK_TIMEOUT');
  });

  it('rejects invalid operations payloads', () => {
    expect(() => decodeOperations(null, 'operations')).toThrow();
    expect(() => decodeOperations({}, 'operations')).toThrow();
  });
});

describe('Angular Operations pager', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Operations],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({
      userId: id1,
      firmId: id2,
      generation: '1',
      staff: true,
    });
  });

  function flushRecovery(recoverySession: Record<string, unknown> | null = null) {
    const http = TestBed.inject(HttpTestingController);
    for (const request of http.match('/api/ui/operations/commands/state')) request.flush({ before: { operatingMode: 'LOCAL_ONLY', deploymentEpoch: '1', recoveryEpoch: recoverySession ? '1' : '0', policyGeneration: '1', operation: null, recoverySession },
      reviewBasis: 'a'.repeat(64), canRetry: false, canCancel: false, canLiftQuarantine: false, canApproveRecoveryRestart: false, blockers: [], retryBlockers: [], cancelBlockers: [], liftQuarantineBlockers: [], approveRecoveryRestartBlockers: [] });
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('explains the administrator requirement to signed-in staff without revealing operations', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush(
      { code: 'authorization.denied', message: 'Forbidden.' },
      { status: 403, statusText: 'Forbidden' },
    );
    TestBed.tick();
    fixture.detectChanges(); flushRecovery();

    const root = fixture.nativeElement as HTMLElement;
    const alert = root.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('firm-wide AuditSphere Administrators');
    expect(alert?.textContent).toContain('Your current account does not have that access');
    expect(alert?.textContent).not.toContain('Sign in');
    expect(root.querySelector('table')).toBeNull();
    expect(Array.from(root.querySelectorAll('button')).some((button) => button.textContent?.trim() === 'Refresh operations')).toBe(false);
    http.expectNone('/api/ui/operations');
    fixture.destroy();
  });

  it('keeps refresh available for temporary failures and recovers after an explicit retry', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({}, { status: 503, statusText: 'Unavailable' });
    TestBed.tick();
    fixture.detectChanges(); flushRecovery();

    const root = fixture.nativeElement as HTMLElement;
    const refresh = Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Refresh operations');
    expect(refresh).toBeDefined();
    refresh!.click();
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({ operatingMode: 'LOCAL_ONLY', retryableStates: [], operations: [] });
    TestBed.tick();
    fixture.detectChanges(); flushRecovery();

    expect(root.textContent).toContain('No durable operations are recorded for this firm.');
    expect(root.textContent).not.toContain('Temporarily unavailable. Retry shortly.');
    fixture.destroy();
  });

  it('keeps approved recovery-session evidence visible without exposing approval controls', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({ operatingMode: 'LOCAL_ONLY', retryableStates: [], operations: [] });
    TestBed.tick();
    fixture.detectChanges();
    flushRecovery({
      id: '33333333-3333-4333-8333-333333333333',
      externalEpoch: '1',
      restorePoint: 'synthetic-restore-point',
      reconciliationScope: 'firm ledger and client workspaces',
      findings: 'Reconciliation completed and reviewed.',
      createdAt: '2026-10-15T20:00:00Z',
      approvedRestartAt: '2026-10-15T21:00:00Z',
      approvedByUserId: id1,
    });
    TestBed.tick();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const evidence = root.querySelector('section[aria-labelledby="recovery-session-heading"]');
    expect(evidence?.textContent).toContain('Recovery session evidence');
    expect(evidence?.textContent).toContain('Reconciliation completed and reviewed.');
    expect(evidence?.querySelector('time')?.getAttribute('datetime')).toBe('2026-10-15T21:00:00Z');
    expect(evidence?.textContent).toContain(id1);
    expect(root.querySelector('#operation-findings')).toBeNull();
    expect(root.textContent).not.toContain('Preview recovery restart approval');
    fixture.destroy();
  });

  it('discovers older unresolved operations by state and pages with the server cursor', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({ operatingMode: 'LOCAL_ONLY', retryableStates: ['DEAD_LETTER'], operations: [] });
    TestBed.tick(); fixture.detectChanges(); flushRecovery();

    const root = fixture.nativeElement as HTMLElement;
    const select = root.querySelector('#operation-state-filter') as HTMLSelectElement;
    select.value = 'DEAD_LETTER'; select.dispatchEvent(new Event('change'));
    const search = Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes('Search unresolved work'))!;
    search.click(); fixture.detectChanges(); flushRecovery();
    const first = http.expectOne('/api/ui/operations/discovery?state=DEAD_LETTER&pageSize=25');
    first.flush({ state: 'DEAD_LETTER', operationId: null, operations: [{ id: id1, kind: 'PBC_DOCUMENT_TRANSFER', status: 'DEAD_LETTER',
      targetId: id2, expectedRevision: 1, attemptCount: 3, nextAttemptAt: null, leaseExpiresAt: null, errorCode: 'REMOTE_TIMEOUT',
      cancellationDisposition: null, createdAt: '2026-10-03T05:00:00Z', completedAt: null, hasResultEvidence: false }],
      nextCursor: 'opaque-next', firstPageCursor: 'opaque-first' });
    TestBed.tick(); fixture.detectChanges(); flushRecovery();
    expect(root.textContent).toContain('1 result(s) on this page');
    expect(root.textContent).toContain(id1);
    expect(root.textContent).not.toContain('1 total');

    Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes('More results'))!.click();
    fixture.detectChanges(); flushRecovery();
    const second = http.expectOne('/api/ui/operations/discovery?state=DEAD_LETTER&cursor=opaque-next&pageSize=25');
    second.flush({ state: 'DEAD_LETTER', operationId: null, operations: [], nextCursor: null, firstPageCursor: 'opaque-first' });
    TestBed.tick(); fixture.detectChanges(); flushRecovery();
    expect(root.textContent).toContain('No unresolved operations match this search.');
    expect(Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes('Previous results'))!.disabled).toBe(false);
    fixture.destroy();
  });

  it('re-arms a discovery row with its own retryable status, not the latest overview row', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({ operatingMode: 'LOCAL_ONLY', retryableStates: ['DEAD_LETTER'], operations: [] });
    TestBed.tick(); fixture.detectChanges(); flushRecovery();
    const root = fixture.nativeElement as HTMLElement;
    const select = root.querySelector('#operation-state-filter') as HTMLSelectElement;
    select.value = 'DEAD_LETTER'; select.dispatchEvent(new Event('change'));
    Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes('Search unresolved work'))!.click();
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations/discovery?state=DEAD_LETTER&pageSize=25').flush({ state: 'DEAD_LETTER', operationId: null,
      operations: [{ id: id1, kind: 'PBC_DOCUMENT_TRANSFER', status: 'DEAD_LETTER', targetId: id2, expectedRevision: 1,
        attemptCount: 3, nextAttemptAt: null, leaseExpiresAt: null, errorCode: 'REMOTE_TIMEOUT', cancellationDisposition: null,
        createdAt: '2026-10-03T05:00:00Z', completedAt: null, hasResultEvidence: false }],
      nextCursor: null, firstPageCursor: 'opaque-first' });
    TestBed.tick(); fixture.detectChanges(); flushRecovery();
    const retry = Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes('Re-arm for retry'))!;
    expect(retry.getAttribute('aria-label')).toContain(id1);
    expect(root.querySelector('audit-operations-command-review')).not.toBeNull();
    fixture.destroy();
  });

  it('rejects partial exact operation IDs without issuing a discovery request', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({ operatingMode: 'LOCAL_ONLY', retryableStates: [], operations: [] });
    TestBed.tick(); fixture.detectChanges(); flushRecovery();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector('#operation-exact-id') as HTMLInputElement;
    input.value = '11111111'; input.dispatchEvent(new Event('input'));
    Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.includes('Search unresolved work'))!.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('complete operation ID');
    http.expectNone((request) => request.url === '/api/ui/operations/discovery');
    fixture.destroy();
  });

  it('matches legacy 10/25/50 page sizes and exposes table semantics', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); flushRecovery();
    http.expectOne('/api/ui/operations').flush({
      operatingMode: 'LOCAL_ONLY',
      retryableStates: ['DEAD_LETTER'],
      operations: Array.from({ length: 26 }, (_, index) => ({
        id: `30000000-0000-4000-8000-${(index + 1).toString().padStart(12, '0')}`,
        kind: `SYNTHETIC_OPERATION_${index + 1}`,
        status: index === 0 ? 'DEAD_LETTER' : 'COMPLETED',
        attemptCount: 1,
        nextAttemptAt: index === 0 ? '2026-10-03T05:00:00Z' : null,
        errorCode: null,
        cancellationDisposition: null,
      })),
    });
    TestBed.tick();
    fixture.detectChanges(); flushRecovery();

    const root = fixture.nativeElement as HTMLElement;
    const table = root.querySelector('table');
    expect(table?.querySelector('caption')?.textContent).toContain('Latest 50 durable operations');
    expect(table?.querySelectorAll('thead th[scope="col"]').length).toBe(7);
    expect(table?.querySelector('thead th:nth-child(4)')?.textContent).toContain('UTC');
    const firstRow = table?.querySelector('tbody tr');
    expect(firstRow?.textContent).toContain('ID 30000000-0000-4000-8000-000000000001');
    expect(firstRow?.querySelector('td:nth-child(4)')?.textContent).toContain('UTC');
    expect(firstRow?.querySelector('button')?.getAttribute('aria-label')).toContain('SYNTHETIC_OPERATION_1');
    expect(firstRow?.querySelector('button')?.getAttribute('aria-label')).toContain('30000000-0000-4000-8000-000000000001');
    expect(root.querySelector('label[for="operation-disposition"] input#operation-disposition')).not.toBeNull();
    expect(root.querySelectorAll('tbody tr').length).toBe(10);
    const pager = root.querySelector('nav[aria-label="Durable operation pages"]')!;
    const select = pager.querySelector('select')!;
    expect(Array.from(select.options).map((option) => Number(option.value))).toEqual([10, 25, 50]);
    expect(pager.textContent).toContain('Page 1 of 3 · 1–10 of 26');

    (Array.from(pager.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Next')!).click();
    fixture.detectChanges(); flushRecovery();
    expect(pager.textContent).toContain('Page 2 of 3 · 11–20 of 26');
    expect(root.querySelectorAll('tbody tr').length).toBe(10);

    select.value = '25';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges(); flushRecovery();
    expect(pager.textContent).toContain('Page 1 of 2 · 1–25 of 26');
    expect(root.querySelectorAll('tbody tr').length).toBe(25);

    select.value = '50';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges(); flushRecovery();
    expect(pager.textContent).toContain('Page 1 of 1 · 1–26 of 26');
    expect(root.querySelectorAll('tbody tr').length).toBe(26);
    fixture.destroy();
  });
});
