import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { PortalDocuments } from './documents';
import { PortalFinance } from './finance';
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
const packageDetail = (packageId: string) => ({
  packageId, framework: 'Synthetic framework', periodStart: '2026-01-01', periodEnd: '2026-12-31', currency: 'QAR',
  templateVersion: '1', packageHash: 'a'.repeat(64), hasCashFlow: false, hasDisclosures: false,
  managementDecision: 'PENDING', managementDecidedAt: null, statementTotals: [], composition: null, latestDecisionId: null,
});
const branding = {
  profileId: null, version: '0', legalName: 'Synthetic audit firm', accentColorHex: '#2B6CB0',
  hasLogo: false, logoSha256Hex: null,
};
const packageRows = (page = 0, pageSize = 10) => packages.slice(page * pageSize, (page + 1) * pageSize);
const portal = (requests: ReturnType<typeof requestRows>, hasMoreRequests: boolean, packagePage = 0, packagePageSize = 10) => ({
  firstSignIn: { completed: true, identityPath: 'EXTERNAL_IDENTITY', canComplete: false, message: 'Complete sign-in.' },
  pendingOnboarding: false, engagementCount: 1, hasMoreRequests, requests, packages: packageRows(packagePage, packagePageSize),
  packagePage, packagePageSize, hasMorePackages: (packagePage + 1) * packagePageSize < packages.length,
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
    try { http.verify(); } finally { TestBed.resetTestingModule(); vi.useRealTimers(); }
  });

  it('keeps the document editor, its target, draft, file and one in-flight comment across polling and request paging', async () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges(); TestBed.tick();
    const root = fixture.nativeElement as HTMLElement;
    const summary = (requests: ReturnType<typeof requestRows>, more: boolean) => ({ ...portal(requests, more), packages: [] });
    const settle = () => { TestBed.tick(); fixture.detectChanges(); };
    const reviewId = id(501), letterReviewId = id(502), commentId = id(503);
    const shared = { documents: [
      { reviewId, deliverableId: id(511), kind: 'AUDIT_REPORT', title: 'Audited financial statements', version: '3', sha256: 'a'.repeat(64), acknowledgedAt: null, comments: [] },
      { reviewId: letterReviewId, deliverableId: id(512), kind: 'REPRESENTATION_LETTER', title: 'Representation letter', version: '2', sha256: 'b'.repeat(64),
        acknowledgedAt: '2026-10-15T10:00:00Z', comments: [] },
    ], signedLetters: [], bundles: [] };
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush(summary(requestRows(10), true)); settle();
    http.expectOne('/api/ui/portal/branding').flush(branding);
    http.expectOne('/api/ui/portal/documents').flush(shared);
    http.expectOne('/api/ui/portal/accounting/journals?page=0').flush({ items: [], page: 0, hasMore: false });
    http.expectOne('/api/ui/portal/finance').flush({ agreements: [], invoices: [], receipts: [], totalOutstanding: '0.00', currency: 'QAR' });
    settle();
    const editor = () => fixture.debugElement.query(By.directive(PortalDocuments))?.componentInstance as PortalDocuments | undefined;
    const original = editor()!;
    const button = (text: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === text)!;
    const pollSummary = async (url: string, value: ReturnType<typeof summary>) => {
      await vi.advanceTimersByTimeAsync(30_000); fixture.detectChanges();
      const read = http.expectOne(url);
      expect(editor()).toBe(original); // a summary reload in progress keeps the authorized shell and its editor
      read.flush(value); settle();
    };

    button('Review this version').click(); fixture.detectChanges();
    const textarea = root.querySelector('#document-comment') as HTMLTextAreaElement;
    textarea.value = 'Please clarify note 4.'; textarea.dispatchEvent(new Event('input')); fixture.detectChanges();
    await pollSummary('/api/ui/portal?page=0&pageSize=10', summary(requestRows(10), true));
    button('Next requests').click(); settle();
    http.expectOne('/api/ui/portal?page=1&pageSize=10').flush(summary(requestRows(2, 11), false)); settle();
    expect(editor()).toBe(original);
    expect(original.selected()).toBe(reviewId);
    expect(root.querySelector('#document-comment')).toBe(textarea);
    expect(textarea.value).toBe('Please clarify note 4.');
    http.expectNone('/api/ui/portal/documents');

    // The comment is in flight when the next poll fires: polling neither repeats it nor replaces its editor.
    await vi.advanceTimersByTimeAsync(20_000);
    const sent = original.comment(reviewId);
    const post = http.expectOne({ method: 'POST', url: `/api/ui/portal/documents/${reviewId}/comment` });
    await vi.advanceTimersByTimeAsync(10_000); fixture.detectChanges();
    http.expectOne('/api/ui/portal?page=1&pageSize=10').flush(summary(requestRows(2, 11), false)); settle();
    http.expectNone((request) => request.method === 'POST');
    expect(editor()).toBe(original);
    expect(original.busy()).toBe(true);
    post.flush({ value: commentId }); await sent;
    http.expectOne('/api/ui/portal/documents').flush({ ...shared, documents: [{ ...shared.documents[0],
      comments: [{ body: 'Please clarify note 4.', fromClient: true, resolution: null }] }, shared.documents[1]] }); settle();
    expect(root.textContent).toContain('Comment sent to the audit team.');
    expect(root.textContent).toContain('Client: Please clarify note 4.');

    button('Upload signed letter').click(); fixture.detectChanges();
    const signatory = root.querySelector('#management-signatory') as HTMLInputElement;
    signatory.value = 'Authorized Management'; signatory.dispatchEvent(new Event('input'));
    const fileInput = root.querySelector('#signed-file') as HTMLInputElement;
    const file = new File(['%PDF-1.7 synthetic signed letter'], 'signed.pdf', { type: 'application/pdf' });
    Object.defineProperty(fileInput, 'files', { value: [file], configurable: true });
    fileInput.dispatchEvent(new Event('change')); fixture.detectChanges();
    await pollSummary('/api/ui/portal?page=1&pageSize=10', summary(requestRows(2, 11), false));
    button('Previous requests').click(); settle();
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush(summary(requestRows(10), true)); settle();
    expect(editor()).toBe(original);
    expect(original.selected()).toBe(letterReviewId);
    expect(original.file()).toBe(file);
    expect(root.querySelector('#signed-file')).toBe(fileInput);
    expect((root.querySelector('#management-signatory') as HTMLInputElement).value).toBe('Authorized Management');
    http.expectNone('/api/ui/portal/documents');

    // A known denial of the summary still removes the editor and its protected content.
    await vi.advanceTimersByTimeAsync(30_000);
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush({ code: 'scope.denied', message: 'Denied' }, { status: 403, statusText: 'Forbidden' });
    settle();
    expect(editor()).toBeUndefined();
    expect(root.querySelector('audit-portal-documents')).toBeNull();
    expect(root.textContent).not.toContain('Audited financial statements');
    fixture.destroy();
  });

  it('restores bounded 10/25/50 request and package page choices', () => {
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges();
    TestBed.tick();
    const root = fixture.nativeElement as HTMLElement;
    const flushPortal = (url: string, value: ReturnType<typeof portal>) => {
      http.expectOne(url).flush(value);
      TestBed.tick(); fixture.detectChanges();
      http.match('/api/ui/portal/branding').forEach((req) => req.flush(branding));
      http.match('/api/ui/portal/documents').forEach((req) => req.flush({ documents: [], signedLetters: [], bundles: [] }));
      http.match('/api/ui/portal/accounting/journals?page=0').forEach((req) => req.flush({ items: [], page: 0, hasMore: false }));
      http.match('/api/ui/portal/finance').forEach((req) => req.flush({ agreements: [], invoices: [], receipts: [], totalOutstanding: '0.00', currency: 'QAR' }));
      const reports = http.match((req) => req.url.startsWith('/api/ui/portal/accounting/packages/'));
      reports.forEach((req) => req.flush(packageDetail(req.request.url.split('/').at(-1)!)));
      TestBed.tick(); fixture.detectChanges();
      return reports.length;
    };
    expect(flushPortal('/api/ui/portal?page=0&pageSize=10', portal(requestRows(10), true))).toBe(10);

    const packageSection = () => Array.from(root.querySelectorAll('section.panel'))
      .find((section) => section.querySelector('h2')?.textContent?.includes('Financial packages for management review'))!;
    expect(root.querySelectorAll('.portal-request').length).toBe(10);
    expect(root.querySelector('[aria-label="Request pages"]')?.textContent).toContain('Page 1 · 10 shown');
    expect(packageSection().querySelectorAll('a').length).toBe(10);
    expect(packageSection().querySelectorAll('audit-portal-report-action').length).toBe(10);
    expect(packageSection().textContent).toContain('no current sealed report invitation');
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 1 · 10 shown');
    expect(root.textContent).toContain('Action counts describe this request page, not all engagements.');
    expect(root.textContent).not.toContain('Provide requested document');
    expect(root.textContent).not.toContain('Contact your audit team for older reports.');

    (Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === 'Next requests')!).click();
    TestBed.tick();
    flushPortal('/api/ui/portal?page=1&pageSize=10', portal(requestRows(2, 11), false));
    expect(root.querySelectorAll('.portal-request').length).toBe(2);
    expect(root.querySelectorAll('.portal-request h4').length).toBe(2);
    expect(root.querySelector('a[href="/portal/requests/40000000-0000-4000-8000-000000000001"]')).toBeNull();

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
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    expect(flushPortal('/api/ui/portal?page=0&pageSize=50&packagePage=1&packagePageSize=10',
      portal(requestRows(26), false, 1, 10))).toBe(10);
    TestBed.tick(); fixture.detectChanges();
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 2 · 10 shown');
    expect(packageSection().querySelectorAll('audit-portal-report-action').length).toBe(10);
    expect(packageSection().querySelector(`a[href="/portal/accounting/packages/${id(101)}"]`)).toBeNull();
    expect(packageSection().querySelector(`a[href="/portal/accounting/packages/${id(111)}"]`)).not.toBeNull();

    const packageSizeAfterPage = root.querySelector('#portal-package-page-size') as HTMLSelectElement;
    packageSizeAfterPage.value = '25'; packageSizeAfterPage.dispatchEvent(new Event('change'));
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    flushPortal('/api/ui/portal?page=0&pageSize=50&packagePage=0&packagePageSize=25', portal(requestRows(26), false, 0, 25));
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 1 · 25 shown');
    expect(packageSection().querySelectorAll('a').length).toBe(25);

    const packageSizeAfterResize = root.querySelector('#portal-package-page-size') as HTMLSelectElement;
    packageSizeAfterResize.value = '50'; packageSizeAfterResize.dispatchEvent(new Event('change'));
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    flushPortal('/api/ui/portal?page=0&pageSize=50&packagePage=0&packagePageSize=50', portal(requestRows(26), false, 0, 50));
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Page 1 · 26 shown');
    expect(packageSection().querySelectorAll('a').length).toBe(26);
    expect(packageSection().querySelectorAll('audit-portal-report-action').length).toBe(0);
    http.expectNone((request) => request.method === 'POST');
    fixture.destroy();
  });

  it('shows the original receipt allocation separately from currently applied cash', async () => {
    const fixture = TestBed.createComponent(PortalFinance);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/finance').flush({ agreements: [{
      agreementId: id(601), engagementId: id(602), agreedFee: '25000', currency: 'QAR', advancePercent: '50',
      outstandingBalance: '100', invoices: [], receipts: [{ receiptId: id(603), documentId: null, kind: 'ADVANCE',
        amount: '5000', appliedAmount: '4900', currency: 'QAR', reference: 'SYNTHETIC-ADVANCE',
        receivedAt: '2026-10-18T08:00:00Z', downloadUrl: null }],
    }] });
    await fixture.whenStable(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const cells = root.querySelectorAll('tbody tr td');
    expect(cells[2].textContent).toContain('5000 QAR');
    expect(cells[3].textContent).toContain('4900 QAR');
    expect(root.textContent).toContain('independently approved allocation reversals');
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
    http.expectOne('/api/ui/portal/branding').flush(branding);
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

  it('groups the mixed current request page and renders only server-provided clarification text', () => {
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges(); TestBed.tick();
    const clarification = { ...requestRows(1, 201)[0], state: 'CLARIFICATION_REQUIRED', dueDate: '2026-10-14',
      clarificationReason: 'Please replace the unsigned schedule.', clarificationReasonTruncated: true, canWrite: true };
    const clarification2 = { ...requestRows(1, 202)[0], state: 'CLARIFICATION_REQUIRED', dueDate: '2026-10-14',
      clarificationReason: null, clarificationReasonTruncated: false, canWrite: false };
    const pending = { ...requestRows(1, 203)[0], state: 'ACKNOWLEDGED', dueDate: '2026-10-15', canWrite: true };
    const review = { ...requestRows(1, 204)[0], state: 'UNDER_REVIEW', dueDate: '2026-10-16', canWrite: false };
    const completed = { ...requestRows(1, 205)[0], state: 'CLOSED', dueDate: '2026-10-17', canWrite: false };
    const unknown = { ...requestRows(1, 206)[0], state: 'FUTURE_STATE', dueDate: '2026-10-18', canWrite: true };
    const value = { ...portal([clarification, clarification2, pending, review, completed, unknown], true), asOfDateUtc: '2026-10-15' };
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush(value);
    TestBed.tick(); fixture.detectChanges();
    http.expectOne('/api/ui/portal/branding').flush(branding);
    http.match('/api/ui/portal/documents').forEach((req) => req.flush({ documents: [], signedLetters: [], bundles: [] }));
    http.match('/api/ui/portal/accounting/journals?page=0').forEach((req) => req.flush({ items: [], page: 0, hasMore: false }));
    http.match('/api/ui/portal/finance').forEach((req) => req.flush({ agreements: [], invoices: [], receipts: [], totalOutstanding: '0.00', currency: 'QAR' }));
    http.match((req) => req.url.startsWith('/api/ui/portal/accounting/packages/'))
      .forEach((req) => req.flush(packageDetail(req.request.url.split('/').at(-1)!)));
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const groupHeadings = Array.from(root.querySelectorAll('h3[id^="portal-request-group-"]')).map((heading) => heading.textContent?.trim());
    expect(groupHeadings).toEqual([
      'Needs re-upload (2)', 'Documents to provide (1)', 'Under firm review (1)', 'Completed requests (1)', 'Status needs review (1)',
    ]);
    expect(root.textContent).toContain('Please replace the unsigned schedule.');
    expect(root.textContent).toContain('Open request for the full reason.');
    expect(root.textContent).toContain("Open the request to review the firm's instructions.");
    expect(root.textContent).toContain('Overdue');
    expect(root.textContent).toContain('Due today');
    expect(root.textContent).toContain('Provide requested document');
    expect(root.textContent).toContain('Action counts describe this request page, not all engagements.');
    const unknownActions = Array.from(root.querySelectorAll('a[href="/portal/requests/40000000-0000-4000-8000-000000000206"]'));
    expect(unknownActions.some((link) => link.textContent?.trim() === 'View request')).toBe(true);
    expect(unknownActions.some((link) => link.textContent?.trim() === 'Provide requested document')).toBe(false);
    fixture.destroy();
  });

  it('keeps first-sign-in gating visible and links to the existing request detail route', () => {
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges(); TestBed.tick();
    const gated = { ...requestRows(1, 301)[0], state: 'SENT', canWrite: false };
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush({
      ...portal([gated], false),
      firstSignIn: { completed: false, identityPath: 'EXTERNAL_IDENTITY', canComplete: true, message: 'Acknowledge portal terms.' },
    });
    TestBed.tick(); fixture.detectChanges();
    http.expectOne('/api/ui/portal/branding').flush(branding);
    http.match('/api/ui/portal/documents').forEach((req) => req.flush({ documents: [], signedLetters: [], bundles: [] }));
    http.match('/api/ui/portal/accounting/journals?page=0').forEach((req) => req.flush({ items: [], page: 0, hasMore: false }));
    http.match('/api/ui/portal/finance').forEach((req) => req.flush({ agreements: [], invoices: [], receipts: [], totalOutstanding: '0.00', currency: 'QAR' }));
    http.match((req) => req.url.startsWith('/api/ui/portal/accounting/packages/'))
      .forEach((req) => req.flush(packageDetail(req.request.url.split('/').at(-1)!)));
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Complete your first sign-in');
    expect(root.textContent).toContain('Acknowledge portal terms.');
    expect(root.textContent).toContain('View request');
    expect(root.textContent).not.toContain('Provide requested document');
    expect(root.querySelector('a[href="/portal/requests/40000000-0000-4000-8000-000000000301"]')).not.toBeNull();
    fixture.destroy();
  });

  it('keeps completed requests view-only and exposes continuation for older package summaries', () => {
    const fixture = TestBed.createComponent(ClientPortalHome);
    fixture.detectChanges(); TestBed.tick();
    const closed = { ...requestRows(1, 401)[0], state: 'CLOSED', canWrite: false };
    http.expectOne('/api/ui/portal?page=0&pageSize=10').flush({
      ...portal([closed], false), hasMorePackages: true,
    });
    TestBed.tick(); fixture.detectChanges();
    http.expectOne('/api/ui/portal/branding').flush(branding);
    http.match('/api/ui/portal/documents').forEach((req) => req.flush({ documents: [], signedLetters: [], bundles: [] }));
    http.match('/api/ui/portal/accounting/journals?page=0').forEach((req) => req.flush({ items: [], page: 0, hasMore: false }));
    http.match('/api/ui/portal/finance').forEach((req) => req.flush({ agreements: [], invoices: [], receipts: [], totalOutstanding: '0.00', currency: 'QAR' }));
    http.match((req) => req.url.startsWith('/api/ui/portal/accounting/packages/'))
      .forEach((req) => req.flush(packageDetail(req.request.url.split('/').at(-1)!)));
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Completed requests (1)');
    expect(root.textContent).not.toContain('Documents to provide');
    expect(root.textContent).not.toContain('Provide requested document');
    expect(root.textContent).not.toContain('Only the 100 most recent package summaries are loaded.');
    expect(root.querySelector('[aria-label="Financial package pages"]')?.textContent).toContain('Next packages');
    fixture.destroy();
  });
});
