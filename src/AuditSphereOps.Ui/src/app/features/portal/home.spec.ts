import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { ClientPortalHome } from './home';

const id = (n: number) => `40000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const requestRows = (count: number, start = 1) => Array.from({ length: count }, (_, index) => ({
  id: id(start + index), area: `Synthetic request ${start + index}`, objective: 'Synthetic objective',
  periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: '2027-01-31', state: 'SENT', delegated: false,
}));
const packages = Array.from({ length: 26 }, (_, index) => ({
  id: id(index + 101), framework: `Framework ${index + 1}`, periodStart: '2026-01-01',
  periodEnd: '2026-12-31', currency: 'QAR',
}));
const portal = (requests: ReturnType<typeof requestRows>, hasMoreRequests: boolean) => ({
  firstSignIn: { completed: true, identityPath: 'EXTERNAL_IDENTITY', canComplete: false, message: 'Complete sign-in.' },
  pendingOnboarding: false, engagementCount: 1, hasMoreRequests, requests, packages,
});

describe('Client portal workspace paging', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ClientPortalHome],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: false });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify(); } finally { TestBed.resetTestingModule(); }
  });

  it('restores bounded 10/25/50 request and package page choices', () => {
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges();
    TestBed.tick();
    const root = fixture.nativeElement as HTMLElement;
    const flushPortal = (url: string, value: ReturnType<typeof portal>) => {
      http.expectOne(url).flush(value);
      TestBed.tick(); fixture.detectChanges();
      http.match('/api/ui/portal/documents').forEach((req) => req.flush({ documents: [], signedLetters: [], bundles: [] }));
      http.match('/api/ui/portal/accounting/journals?page=0').forEach((req) => req.flush({ items: [], page: 0, hasMore: false }));
      http.match('/api/ui/portal/finance').forEach((req) => req.flush({ agreements: [], invoices: [], receipts: [], totalOutstanding: '0.00', currency: 'QAR' }));
      TestBed.tick(); fixture.detectChanges();
    };
    flushPortal('/api/ui/portal?page=0&pageSize=10', portal(requestRows(10), true));

    const packageSection = () => Array.from(root.querySelectorAll('section.panel'))
      .find((section) => section.querySelector('h2')?.textContent?.includes('Financial packages for management review'))!;
    expect(root.querySelectorAll('.portal-request').length).toBe(10);
    expect(root.querySelector('[aria-label="Request pages"]')?.textContent).toContain('Page 1 · 10 shown');
    expect(packageSection().querySelectorAll('a').length).toBe(10);
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 1 of 3 · 1–10 of 26');

    (Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Next requests')!).click();
    TestBed.tick();
    flushPortal('/api/ui/portal?page=1&pageSize=10', portal(requestRows(2, 11), false));
    expect(root.querySelectorAll('.portal-request').length).toBe(2);

    const requestSize = root.querySelector('#portal-request-page-size') as HTMLSelectElement;
    expect(Array.from(requestSize.options).map((option) => Number(option.value))).toEqual([10, 25, 50]);
    requestSize.value = '25'; requestSize.dispatchEvent(new Event('change'));
    TestBed.tick();
    flushPortal('/api/ui/portal?page=0&pageSize=25', portal(requestRows(25), true));
    expect(root.querySelectorAll('.portal-request').length).toBe(25);
    expect(root.querySelector('[aria-label="Request pages"]')?.textContent).toContain('Page 1 · 25 shown');

    const requestSizeAfterReload = root.querySelector('#portal-request-page-size') as HTMLSelectElement;
    requestSizeAfterReload.value = '50'; requestSizeAfterReload.dispatchEvent(new Event('change'));
    TestBed.tick();
    flushPortal('/api/ui/portal?page=0&pageSize=50', portal(requestRows(26), false));
    expect(root.querySelectorAll('.portal-request').length).toBe(26);

    const packageSize = root.querySelector('#portal-package-page-size') as HTMLSelectElement;
    expect(Array.from(packageSize.options).map((option) => Number(option.value))).toEqual([10, 25, 50]);
    (Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Next packages')!).click();
    fixture.detectChanges();
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 2 of 3 · 11–20 of 26');

    packageSize.value = '25'; packageSize.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 1 of 2 · 1–25 of 26');
    expect(packageSection().querySelectorAll('a').length).toBe(25);

    packageSize.value = '50'; packageSize.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 1 of 1 · 1–26 of 26');
    expect(packageSection().querySelectorAll('a').length).toBe(26);
    fixture.destroy();
  });

  it('shows only the setup message until a client engagement is active', () => {
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush({
      ...portal([], false),
      firstSignIn: { completed: false, identityPath: 'EXTERNAL_IDENTITY', canComplete: true, message: 'Acknowledgement needed.' },
      pendingOnboarding: true,
      engagementCount: 0,
    });
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Portal setup pending');
    expect(root.querySelector('h2#portal-requests')).toBeNull();
    expect(root.textContent).not.toContain('Complete your first sign-in');
    expect(root.textContent).not.toContain('Financial packages for management review');
    expect(root.querySelector('audit-portal-documents')).toBeNull();
    expect(root.querySelector('audit-portal-journals')).toBeNull();
    expect(root.querySelector('audit-portal-finance')).toBeNull();
    http.expectNone('/api/ui/portal/documents');
    http.expectNone('/api/ui/portal/accounting/journals?page=0');
    http.expectNone('/api/ui/portal/finance');
    fixture.destroy();
  });
});
