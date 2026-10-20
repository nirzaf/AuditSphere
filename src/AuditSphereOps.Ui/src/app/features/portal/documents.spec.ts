import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { PortalDocuments, sharedDocuments } from './documents';

const userId = '11111111-1111-4111-8111-111111111111';
const firmId = '22222222-2222-4222-8222-222222222222';
const bundleId = '33333333-3333-4333-8333-333333333333';
const receiptId = '44444444-4444-4444-8444-444444444444';
const contentSha256 = 'a'.repeat(64);
const manifestSha256 = 'b'.repeat(64);
const bundleAssembledAt = '2026-10-15T13:00:00+03:00';
const bundleAcknowledgedAt = '2026-10-15T13:05:00+03:00';
const documentReviewId = '55555555-5555-4555-8555-555555555555';
const deliverableId = '66666666-6666-4666-8666-666666666666';
type SharedComment = { body: string; fromClient: boolean; resolution: string | null };
type PortalContext = { clientLegalName: string; serviceRoute: string; periodStart: string; periodEnd: string };
const portalContext = (changes: Partial<PortalContext> = {}): PortalContext => ({
  clientLegalName: 'Evidence Client Ltd', serviceRoute: 'STATUTORY_AUDIT', periodStart: '2025-01-01', periodEnd: '2025-12-31', ...changes,
});
const bundle = (acknowledgedAt: string | null = null, assembledAt = '2026-10-15T10:00:00Z', context = portalContext()) => ({
  id: bundleId, sha256: contentSha256, manifestSha256, assembledAt, acknowledgedAt, context,
});
const sharedDocument = { reviewId: documentReviewId, deliverableId, kind: 'AUDIT_REPORT', title: 'Audited financial statements',
  version: '3', sha256: contentSha256, acknowledgedAt: null, comments: [] as SharedComment[], context: portalContext() };
const documents = (acknowledgedAt: string | null = null, items: Array<typeof sharedDocument> = [], assembledAt?: string) => ({
  documents: items, signedLetters: [], bundles: [bundle(acknowledgedAt, assembledAt)],
});
const displayedContext = (card: Element) => Object.fromEntries(
  Array.from(card.querySelectorAll('dt')).map(label => [label.textContent?.trim() ?? '', label.nextElementSibling?.textContent?.trim() ?? '']),
);

describe('client portal document context contract', () => {
  it('requires complete string context on document and bundle projections', () => {
    const valid = documents(null, [sharedDocument]);
    expect(decode(sharedDocuments, valid).documents[0].context).toEqual(portalContext());
    expect(() => decode(sharedDocuments, {
      ...valid, documents: [{ ...sharedDocument, context: undefined }],
    })).toThrow();
    expect(() => decode(sharedDocuments, {
      ...valid, bundles: [{ ...bundle(), context: undefined }],
    })).toThrow();
    expect(() => decode(sharedDocuments, {
      ...valid, bundles: [{ ...bundle(), context: { ...portalContext(), periodEnd: 2025 } }],
    })).toThrow();
  });
});

describe('client document comments', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PortalDocuments],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: false });
  });

  afterEach(() => {
    try { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); }
  });

  it('clears only a successfully sent draft and preserves a refused draft', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [sharedDocument]));
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(displayedContext(root.querySelector('article.document')!)).toEqual({
      'Client legal name': 'Evidence Client Ltd', Service: 'STATUTORY_AUDIT',
      'Period start': '2025-01-01', 'Period end': '2025-12-31',
    });

    fixture.componentInstance.select(documentReviewId);
    fixture.componentInstance.review.controls.body.setValue('Please clarify this statement.');
    fixture.componentInstance.review.controls.body.markAsDirty();
    const sent = fixture.componentInstance.comment(documentReviewId);
    const accepted = http.expectOne({ method: 'POST', url: `/api/ui/portal/documents/${documentReviewId}/comment` });
    expect(accepted.request.body.body).toBe('Please clarify this statement.');
    expect(accepted.request.body.requestId).toMatch(/^[0-9a-f-]{36}$/i);
    accepted.flush({ value: receiptId });
    await sent; fixture.detectChanges();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [{ ...sharedDocument,
      comments: [{ body: 'Please clarify this statement.', fromClient: true, resolution: null }] }]));
    await fixture.whenStable(); TestBed.tick(); fixture.detectChanges();
    expect(fixture.componentInstance.review.controls.body.value).toBe('');
    expect(fixture.componentInstance.review.controls.body.pristine).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Client: Please clarify this statement.');

    fixture.componentInstance.review.controls.body.setValue('Keep this draft if refused.');
    fixture.componentInstance.review.controls.body.markAsDirty();
    const refused = fixture.componentInstance.comment(documentReviewId);
    http.expectOne({ method: 'POST', url: `/api/ui/portal/documents/${documentReviewId}/comment` })
      .flush({ code: 'portal.comment-refused', message: 'Comment was refused.' }, { status: 400, statusText: 'Bad Request' });
    await refused; fixture.detectChanges();
    expect(fixture.componentInstance.review.controls.body.value).toBe('Keep this draft if refused.');
    expect(fixture.componentInstance.review.controls.body.dirty).toBe(true);
    fixture.destroy();
  });

  it('recovers only the exact comment receipt after an unknown command outcome', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [sharedDocument]));
    TestBed.tick(); fixture.detectChanges();
    const editor = fixture.componentInstance;
    editor.select(documentReviewId);
    editor.review.controls.body.setValue('Please confirm this exact disclosure.');
    const sent = editor.comment(documentReviewId);
    const url = `/api/ui/portal/documents/${documentReviewId}/comment`;
    const post = http.expectOne({ method: 'POST', url });
    const original = post.request.body;
    post.flush({}, { status: 503, statusText: 'Unavailable' });
    await sent; fixture.detectChanges();
    expect(editor.uncertain()).toBe(true);
    http.expectNone('/api/ui/portal/documents');

    const recovery = editor.recoverComment();
    const get = http.expectOne(`/api/ui/portal/documents/${documentReviewId}/comment-receipts/${original.requestId}`);
    get.flush({ succeeded: true, value: receiptId });
    await recovery; fixture.detectChanges();
    expect(editor.uncertain()).toBe(false);
    expect(editor.review.controls.body.value).toBe('');
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [{ ...sharedDocument,
      comments: [{ body: 'Please confirm this exact disclosure.', fromClient: true, resolution: null }] }]));
    await fixture.whenStable(); TestBed.tick(); fixture.destroy();
  });

  it.each(['destroyed', 'revoked'] as const)('keeps a late comment reply inert once the editor visit is %s', async (ending) => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [sharedDocument]));
    TestBed.tick(); fixture.detectChanges();
    const editor = fixture.componentInstance;
    const root = fixture.nativeElement as HTMLElement;
    editor.select(documentReviewId);
    editor.review.controls.body.setValue('This reply arrives after the visit ended.');
    const sent = editor.comment(documentReviewId);
    const post = http.expectOne({ method: 'POST', url: `/api/ui/portal/documents/${documentReviewId}/comment` });
    if (ending === 'destroyed') fixture.destroy();
    else {
      TestBed.inject(SessionService).clear(); TestBed.tick(); fixture.detectChanges();
      http.expectOne('/api/ui/portal/documents').flush({ code: 'scope.denied', message: 'Denied' }, { status: 403, statusText: 'Forbidden' });
      TestBed.tick(); fixture.detectChanges();
    }

    post.flush({ value: receiptId }); await sent;
    http.expectNone('/api/ui/portal/documents');
    http.expectNone((request) => request.method === 'POST');
    expect(editor.message()).toBe('');
    if (ending === 'revoked') {
      fixture.detectChanges();
      expect(root.textContent).not.toContain(sharedDocument.title);
      expect(editor.selected()).toBe('');
      expect(editor.review.controls.body.value).toBe('');
      fixture.destroy();
    }
  });
});

describe('client retained signed representation scans', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PortalDocuments],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: false });
  });

  afterEach(() => {
    try { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); }
  });

  it('renders all 21 retained scans with exact identity, UTC metadata, source context and separate statuses', () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    const scans = Array.from({ length: 21 }, (_, index) => {
      const id = `77777777-7777-4777-8777-${String(index + 1).padStart(12, '0')}`;
      const uploadedAt = `2026-10-16T${String(13 + Math.floor(index / 60)).padStart(2, '0')}:${String(index % 60).padStart(2, '0')}:00+03:00`;
      return {
        id, deliverableId, version: 3, managementSignatory: `Management Signatory ${index + 1}`,
        contentSha256: (index + 1).toString(16).padStart(64, '0'), uploadedAt,
        verified: index % 3 === 0, current: index % 2 === 0,
      };
    });
    http.expectOne('/api/ui/portal/documents').flush({
      ...documents(null, [{ ...sharedDocument, kind: 'REPRESENTATION_LETTER' }]), signedLetters: scans,
    });
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const card = root.querySelector<HTMLElement>('article.document')!;
    const rows = Array.from(card.querySelectorAll<HTMLElement>('.signed-scan'));
    expect(rows).toHaveLength(21);
    expect(displayedContext(card.querySelector('dl.context')!)).toEqual({
      'Client legal name': 'Evidence Client Ltd', Service: 'STATUTORY_AUDIT',
      'Period start': '2025-01-01', 'Period end': '2025-12-31',
    });
    rows.forEach((row, index) => {
      const scan = scans[index];
      const link = row.querySelector<HTMLAnchorElement>('a')!;
      const uploaded = row.querySelector('time')!;
      const statuses = Array.from(row.querySelectorAll('audit-status')).map(x => x.textContent?.trim());
      const utcUpload = `2026-10-16 ${String(10 + Math.floor(index / 60)).padStart(2, '0')}:${String(index % 60).padStart(2, '0')} UTC`;
      expect(link.textContent?.trim()).toBe('Management-signed PDF scan');
      expect(link.getAttribute('href')).toBe(`/api/representation-scans/${scan.id}/download`);
      expect(link.getAttribute('aria-label')).toBe(`Download Management-signed PDF scan for client Evidence Client Ltd; management signatory ${scan.managementSignatory}; source letter version ${scan.version}; uploaded ${utcUpload}; scan ${scan.id}`);
      expect(row.textContent).toContain(`Management signatory${scan.managementSignatory}`);
      expect(row.textContent).toContain(`Source letter version${scan.version}`);
      expect(row.textContent).toContain(`Scan SHA-256${scan.contentSha256}`);
      expect(uploaded.getAttribute('datetime')).toBe(scan.uploadedAt);
      expect(uploaded.textContent).toBe(utcUpload);
      expect(statuses).toEqual([scan.current ? 'current' : 'stale', scan.verified ? 'verification recorded' : 'not verified']);
      expect(row.textContent).toContain('does not verify signature authenticity');
    });
    http.expectNone(request => request.method === 'POST');
    fixture.destroy();
  });
});

describe('client final bundle receipt acknowledgement', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PortalDocuments],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: false });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); }
  });

  it('requires a deliberate exact-bundle confirmation and labels the saved state as client-reported', async () => {
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [], bundleAssembledAt));
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const button = () => Array.from(root.querySelectorAll('button')).find(x => x.textContent?.includes('Record receipt acknowledgment'))!;
    expect(button().disabled).toBe(true);
    expect(root.textContent).toContain(contentSha256);
    expect(root.textContent).toContain(manifestSha256);
    expect(root.textContent).toContain('Final bundle assembled at 2026-10-15 10:00 UTC.');
    expect(root.textContent).toContain('not evidence of delivery or client receipt');
    const initialAssemblyTime = root.querySelector('time');
    expect(initialAssemblyTime?.getAttribute('datetime')).toBe(bundleAssembledAt);
    expect(initialAssemblyTime?.textContent).toBe('2026-10-15 10:00 UTC');
    const bundleLink = root.querySelector<HTMLAnchorElement>(`a[href='/api/deliverable-bundles/${bundleId}/download']`);
    expect(bundleLink?.getAttribute('aria-label')).toBe(`Download five-part final bundle for client Evidence Client Ltd; service STATUTORY_AUDIT; period start 2025-01-01; period end 2025-12-31; current bundle ${bundleId}; assembled 2026-10-15 10:00 UTC`);

    const checkbox = root.querySelector('input[type="checkbox"]') as HTMLInputElement;
    checkbox.checked = true; checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(button().disabled).toBe(false);
    const action = fixture.componentInstance.acknowledgeBundle(bundle());
    const post = http.expectOne({ method: 'POST', url: `/api/ui/portal/bundles/${bundleId}/acknowledge` });
    expect(post.request.body).toEqual({ contentSha256, manifestSha256, received: true });
    post.flush({ value: receiptId });
    await action; fixture.detectChanges();

    http.expectOne('/api/ui/portal/documents').flush(documents(bundleAcknowledgedAt, [], bundleAssembledAt));
    await fixture.whenStable(); TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).toContain('Receipt acknowledged by your client account');
    expect(root.textContent).toContain('not proof of email delivery');
    const times = Array.from(root.querySelectorAll('time'));
    const assembledTime = times.find(time => time.getAttribute('datetime') === bundleAssembledAt);
    const acknowledgedTime = times.find(time => time.getAttribute('datetime') === bundleAcknowledgedAt);
    expect(assembledTime?.textContent).toBe('2026-10-15 10:00 UTC');
    expect(acknowledgedTime?.textContent).toBe('2026-10-15 10:05 UTC');
    expect(root.querySelector('input[type="checkbox"]')).toBeNull();
    fixture.destroy();
  });

  it('renders every retained bundle with a distinct download identity even when assembly times match', () => {
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    const contexts = [
      portalContext({ clientLegalName: 'Evidence Client Alpha Ltd', serviceRoute: 'STATUTORY_AUDIT', periodStart: '2024-01-01', periodEnd: '2024-12-31' }),
      portalContext({ clientLegalName: 'Evidence Client Beta Ltd', serviceRoute: 'TAX_ADVISORY', periodStart: '2025-04-01', periodEnd: '2026-03-31' }),
      portalContext({ clientLegalName: 'Legacy Evidence Client Ltd', serviceRoute: '', periodStart: '', periodEnd: '' }),
    ];
    const retained = Array.from({ length: 11 }, (_, index) => ({
      ...bundle(null, '2026-10-15T10:00:00Z', contexts[index % contexts.length]),
      id: `33333333-3333-4333-8333-${String(index + 1).padStart(12, '0')}`,
    }));
    http.expectOne('/api/ui/portal/documents').flush({ ...documents(), bundles: retained });
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>("a[href^='/api/deliverable-bundles/']"));
    const cards = Array.from(root.querySelectorAll<HTMLElement>('article.document'));
    expect(links).toHaveLength(11);
    expect(cards).toHaveLength(11);
    expect(cards.every(card => {
      const content = card.textContent ?? '';
      return content.includes(`ZIP SHA-256 ${contentSha256} · manifest SHA-256 ${manifestSha256}`) &&
        content.includes('Final bundle assembled at 2026-10-15 10:00 UTC');
    })).toBe(true);
    expect(links.map(link => link.getAttribute('href'))).toEqual(retained.map(b => `/api/deliverable-bundles/${b.id}/download`));
    expect(new Set(links.map(link => link.getAttribute('aria-label'))).size).toBe(11);
    expect(links.every(link => link.getAttribute('aria-label')?.includes('2026-10-15 10:00 UTC'))).toBe(true);
    expect(displayedContext(cards[0])).toEqual({
      'Client legal name': 'Evidence Client Alpha Ltd', Service: 'STATUTORY_AUDIT',
      'Period start': '2024-01-01', 'Period end': '2024-12-31',
    });
    expect(displayedContext(cards[1])).toEqual({
      'Client legal name': 'Evidence Client Beta Ltd', Service: 'TAX_ADVISORY',
      'Period start': '2025-04-01', 'Period end': '2026-03-31',
    });
    expect(displayedContext(cards[2])).toEqual({
      'Client legal name': 'Legacy Evidence Client Ltd', Service: 'Service not recorded',
      'Period start': 'Not recorded', 'Period end': 'Not recorded',
    });
    expect(links[0].getAttribute('aria-label')).toContain('client Evidence Client Alpha Ltd');
    expect(links[0].getAttribute('aria-label')).toContain('service STATUTORY_AUDIT');
    expect(links[0].getAttribute('aria-label')).toContain('period start 2024-01-01; period end 2024-12-31');
    expect(links[0].getAttribute('aria-label')).toContain(`current bundle ${retained[0].id}`);
    expect(links[1].getAttribute('aria-label')).toContain('client Evidence Client Beta Ltd');
    expect(links[1].getAttribute('aria-label')).toContain('service TAX_ADVISORY');
    expect(root.textContent).toContain('Final bundle assembled at 2026-10-15 10:00 UTC');
    expect(root.textContent).not.toContain('Receipt acknowledged by your client account');
    expect(Array.from(root.querySelectorAll('button')).filter(b => b.textContent?.includes('Record receipt acknowledgment'))
      .every(button => button.disabled)).toBe(true);
    http.expectNone(request => request.method === 'POST');
    fixture.destroy();
  });

  it('recovers a lost acknowledgement reply only after re-reading the persisted receipt', async () => {
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(documents());
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const checkbox = root.querySelector('input[type="checkbox"]') as HTMLInputElement;
    checkbox.checked = true; checkbox.dispatchEvent(new Event('change')); fixture.detectChanges();
    const action = fixture.componentInstance.acknowledgeBundle(bundle());
    http.expectOne({ method: 'POST', url: `/api/ui/portal/bundles/${bundleId}/acknowledge` })
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await action; fixture.detectChanges();
    expect(root.textContent).toContain('outcome is not confirmed');

    http.expectOne('/api/ui/portal/documents').flush(documents('2026-10-15T10:05:00Z'));
    await fixture.whenStable(); TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).toContain('Receipt acknowledged by your client account');
    expect(root.textContent).toContain('Saved portal state was reloaded');
    expect(Array.from(root.querySelectorAll('button')).some(x => x.textContent?.includes('Record receipt acknowledgment'))).toBe(false);
    fixture.destroy();
  });
});
