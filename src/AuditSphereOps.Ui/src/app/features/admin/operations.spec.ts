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

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('explains the administrator requirement to signed-in staff without revealing operations', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/operations').flush(
      { code: 'authorization.denied', message: 'Forbidden.' },
      { status: 403, statusText: 'Forbidden' },
    );
    TestBed.tick();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const alert = root.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('firm-wide AuditSphere Administrators');
    expect(alert?.textContent).toContain('Your current account does not have that access');
    expect(alert?.textContent).not.toContain('Sign in');
    expect(root.querySelector('table')).toBeNull();
    fixture.destroy();
  });

  it('matches legacy 10/25/50 page sizes and exposes table semantics', () => {
    const fixture = TestBed.createComponent(Operations);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/operations').flush({
      operatingMode: 'LOCAL_ONLY',
      retryableStates: ['DEAD_LETTER'],
      operations: Array.from({ length: 26 }, (_, index) => ({
        id: `30000000-0000-4000-8000-${(index + 1).toString().padStart(12, '0')}`,
        kind: `SYNTHETIC_OPERATION_${index + 1}`,
        status: 'COMPLETED',
        attemptCount: 1,
        nextAttemptAt: null,
        errorCode: null,
        cancellationDisposition: null,
      })),
    });
    TestBed.tick();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const table = root.querySelector('table');
    expect(table?.querySelector('caption')?.textContent).toContain('Latest 50 durable operations');
    expect(table?.querySelectorAll('thead th[scope="col"]').length).toBe(7);
    expect(root.querySelector('label[for="operation-disposition"] input#operation-disposition')).not.toBeNull();
    expect(root.querySelectorAll('tbody tr').length).toBe(10);
    const pager = root.querySelector('nav[aria-label="Durable operation pages"]')!;
    const select = pager.querySelector('select')!;
    expect(Array.from(select.options).map((option) => Number(option.value))).toEqual([10, 25, 50]);
    expect(pager.textContent).toContain('Page 1 of 3 · 1–10 of 26');

    (Array.from(pager.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Next')!).click();
    fixture.detectChanges();
    expect(pager.textContent).toContain('Page 2 of 3 · 11–20 of 26');
    expect(root.querySelectorAll('tbody tr').length).toBe(10);

    select.value = '25';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(pager.textContent).toContain('Page 1 of 2 · 1–25 of 26');
    expect(root.querySelectorAll('tbody tr').length).toBe(25);

    select.value = '50';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(pager.textContent).toContain('Page 1 of 1 · 1–26 of 26');
    expect(root.querySelectorAll('tbody tr').length).toBe(26);
    fixture.destroy();
  });
});
