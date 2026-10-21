import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
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
const documentSharedAt = '2026-10-15T13:30:00+03:00';
const documentAcknowledgedAt = '2026-10-15T14:10:00+03:00';
type SharedComment = { body: string; fromClient: boolean; resolution: string | null };
type PortalContext = { clientLegalName: string; serviceRoute: string; periodStart: string; periodEnd: string };
type SharedDocument = {
  reviewId: string; deliverableId: string; kind: string; title: string; version: string; sha256: string;
  sharedAt?: string | null; acknowledgedAt: string | null; comments: SharedComment[]; context: PortalContext;
};
const portalContext = (changes: Partial<PortalContext> = {}): PortalContext => ({
  clientLegalName: 'Evidence Client Ltd', serviceRoute: 'STATUTORY_AUDIT', periodStart: '2025-01-01', periodEnd: '2025-12-31', ...changes,
});
const bundle = (acknowledgedAt: string | null = null, assembledAt = '2026-10-15T10:00:00Z', context = portalContext()) => ({
  id: bundleId, sha256: contentSha256, manifestSha256, assembledAt, acknowledgedAt, context,
});
const sharedDocument: SharedDocument = { reviewId: documentReviewId, deliverableId, kind: 'AUDIT_REPORT', title: 'Audited financial statements',
  version: '3', sha256: contentSha256, acknowledgedAt: null, comments: [] as SharedComment[], context: portalContext() };
const documents = (acknowledgedAt: string | null = null, items: SharedDocument[] = [], assembledAt?: string) => ({
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

  it('decodes retained share time while accepting legacy absence and explicit null, and rejects malformed values', () => {
    const legacy = decode(sharedDocuments, documents(null, [sharedDocument]));
    const explicitlyUnrecorded = decode(sharedDocuments, documents(null, [{ ...sharedDocument, sharedAt: null }]));
    const retained = decode(sharedDocuments, documents(null, [{ ...sharedDocument, sharedAt: documentSharedAt }]));

    expect(legacy.documents[0].sharedAt).toBeNull();
    expect(explicitlyUnrecorded.documents[0].sharedAt).toBeNull();
    expect(retained.documents[0].sharedAt).toBe(documentSharedAt);
    expect(() => decode(sharedDocuments, documents(null, [{ ...sharedDocument, sharedAt: 'not-an-instant' }]))).toThrow();
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

  it('renders retained share and document acknowledgment times as UTC on the exact document context', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    await fixture.whenStable();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [{ ...sharedDocument,
      sharedAt: documentSharedAt, acknowledgedAt: documentAcknowledgedAt }]));
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const card = Array.from(root.querySelectorAll<HTMLElement>('article.document'))
      .find(candidate => candidate.querySelector('h3 a')?.getAttribute('href') === `/api/deliverables/${deliverableId}/download`)!;
    expect(card.querySelector('h3')?.textContent?.trim()).toBe('Audited financial statements v3');
    expect(card.textContent).toContain(contentSha256);
    expect(card.textContent).toContain(`Shared with your client scope at 2026-10-15 10:30 UTC.`);
    expect(card.textContent).toContain(`Client acknowledgment of this document version recorded at 2026-10-15 11:10 UTC.`);
    expect(card.textContent).toContain('does not prove provider delivery or signature verification');
    expect(displayedContext(card)).toEqual({
      'Client legal name': 'Evidence Client Ltd', Service: 'STATUTORY_AUDIT',
      'Period start': '2025-01-01', 'Period end': '2025-12-31',
    });
    const times = Array.from(card.querySelectorAll('time'));
    const sharedTime = times.find(time => time.textContent?.includes('10:30'));
    const acknowledgmentTime = times.find(time => time.textContent?.includes('11:10'));
    expect(sharedTime?.getAttribute('datetime')).toBe(documentSharedAt);
    expect(sharedTime?.textContent).toBe('2026-10-15 10:30 UTC');
    expect(acknowledgmentTime?.getAttribute('datetime')).toBe(documentAcknowledgedAt);
    expect(acknowledgmentTime?.textContent).toBe('2026-10-15 11:10 UTC');
    expect(card.textContent).not.toContain('Review this version');
    fixture.destroy();
  });

  it('labels absent and null legacy share times without inventing timestamps', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    await fixture.whenStable();
    const legacy = { ...sharedDocument, reviewId: '77777777-7777-4777-8777-777777777777',
      deliverableId: '88888888-8888-4888-8888-888888888888', title: 'Legacy shared report' };
    const explicitlyUnrecorded = { ...sharedDocument, reviewId: '99999999-9999-4999-8999-999999999999',
      deliverableId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', title: 'Unrecorded share report', sharedAt: null };
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [legacy, explicitlyUnrecorded]));
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    for (const id of [legacy.deliverableId, explicitlyUnrecorded.deliverableId]) {
      const card = Array.from(root.querySelectorAll<HTMLElement>('article.document'))
        .find(candidate => candidate.querySelector('h3 a')?.getAttribute('href') === `/api/deliverables/${id}/download`)!;
      expect(card.textContent).toContain('Share time not recorded.');
      expect(card.querySelector('time')).toBeNull();
    }
    fixture.destroy();
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

describe('client document history', () => {
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

  const historyRows = (root: HTMLElement) => {
    const history = root.querySelector<HTMLElement>("section[aria-label='Client document history']");
    expect(history).not.toBeNull();
    return { history: history!, rows: Array.from(history!.querySelectorAll<HTMLElement>('ol > li')) };
  };

  const renderedEvent = (root: HTMLElement, row: HTMLElement) => {
    const time = row.querySelector<HTMLTimeElement>('time');
    const link = row.querySelector<HTMLAnchorElement>('a[href*="#"]');
    expect(time).not.toBeNull();
    expect(link).not.toBeNull();
    const href = new URL(link!.getAttribute('href')!, 'http://localhost').hash;
    const target = root.querySelector<HTMLElement>(href);
    expect(target).not.toBeNull();
    expect(target!.matches('article.document, section.signed-scan')).toBe(true);
    return { at: time!.getAttribute('datetime')!, utc: time!.textContent?.trim() ?? '', href,
      linkLabel: link!.textContent?.trim() ?? '', rowText: row.textContent ?? '', target: target! };
  };

  it('orders retained events by exact offset instant and microseconds, excludes unlinked scans, and flags unorderable history', () => {
    const http = TestBed.inject(HttpTestingController);
    const laterMicrosecond = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-900000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-900000000000', title: 'Later microsecond report',
      sharedAt: '2026-10-16T10:00:00.123456Z' };
    const earlierMicrosecond = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-100000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-100000000000', title: 'Earlier microsecond report',
      sharedAt: '2026-10-16T13:00:00.123455+03:00' };
    const offsetPeer = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-300000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-300000000000', title: 'Offset peer report',
      sharedAt: '2026-10-16T10:00:00Z' };
    const offsetReport = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-200000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-200000000000', title: 'Offset chronology report',
      sharedAt: '2026-10-16T11:00:00+01:00' };
    const letterContext = portalContext({ clientLegalName: 'Evidence Letter Client Ltd', serviceRoute: 'ADVISORY',
      periodStart: '2024-04-01', periodEnd: '2025-03-31' });
    const letter = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-400000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-400000000000', kind: 'REPRESENTATION_LETTER',
      title: 'Management representation letter', sharedAt: '2026-10-16T12:01:00+02:00',
      acknowledgedAt: '2026-10-16T12:03:00+02:00', context: letterContext };
    const legacy = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-500000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-500000000000', title: 'Legacy report without retained share time' };
    const invalidDate = { ...sharedDocument, reviewId: 'aaaaaaaa-aaaa-4aaa-8aaa-600000000000',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-600000000000', title: 'Unorderable report',
      sharedAt: '2026-99-15T13:00:00Z' };
    const attachedScan = { id: 'cccccccc-cccc-4ccc-8ccc-400000000001', deliverableId: letter.deliverableId,
      version: 3, managementSignatory: 'Client Director', contentSha256: 'c'.repeat(64),
      uploadedAt: '2026-10-16T13:04:00+03:00', verified: false, current: true };
    const orphanScan = { ...attachedScan, id: 'cccccccc-cccc-4ccc-8ccc-500000000001',
      deliverableId: 'bbbbbbbb-bbbb-4bbb-8bbb-700000000000', managementSignatory: 'Orphan Signatory',
      uploadedAt: '2026-10-17T00:00:00Z' };
    const wrongKindScan = { ...attachedScan, id: 'cccccccc-cccc-4ccc-8ccc-200000000001',
      deliverableId: offsetReport.deliverableId, managementSignatory: 'Wrong Kind Signatory' };
    const wrongVersionScan = { ...attachedScan, id: 'cccccccc-cccc-4ccc-8ccc-400000000002',
      version: 4, managementSignatory: 'Wrong Version Signatory' };
    const historyBundle = { ...bundle('2026-10-16T10:05:00Z', '2026-10-16T13:02:00+03:00'),
      id: 'dddddddd-dddd-4ddd-8ddd-000000000001',
      context: portalContext({ clientLegalName: 'Evidence Bundle Client Ltd', serviceRoute: 'TAX_ADVISORY',
        periodStart: '2025-04-01', periodEnd: '2026-03-31' }) };
    const allDocuments = [laterMicrosecond, earlierMicrosecond, offsetPeer, offsetReport, letter, legacy, invalidDate];
    const payload = {
      ...documents(), documents: allDocuments,
      signedLetters: [attachedScan, orphanScan, wrongKindScan, wrongVersionScan], bundles: [historyBundle],
    };
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(payload);
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const { history, rows } = historyRows(root);
    expect(rows).toHaveLength(9);
    const events = rows.map(row => renderedEvent(root, row));
    expect(events.map(event => event.at)).toEqual([
      historyBundle.acknowledgedAt, attachedScan.uploadedAt, letter.acknowledgedAt,
      historyBundle.assembledAt, letter.sharedAt, laterMicrosecond.sharedAt,
      earlierMicrosecond.sharedAt, offsetReport.sharedAt, offsetPeer.sharedAt,
    ]);
    expect(Date.parse(laterMicrosecond.sharedAt!)).toBe(Date.parse(earlierMicrosecond.sharedAt!));
    expect(events[5].href).toBe(`#doc-review-${laterMicrosecond.reviewId}`);
    expect(events[6].href).toBe(`#doc-review-${earlierMicrosecond.reviewId}`);
    expect(events[7].at).not.toBe(events[8].at);
    expect(Date.parse(events[7].at)).toBe(Date.parse(events[8].at));
    expect(events.every(event => event.utc.endsWith('UTC'))).toBe(true);

    const letterSharedEvent = events.find(event => event.at === letter.sharedAt)!;
    expect(letterSharedEvent.href).toBe(`#doc-review-${letter.reviewId}`);
    expect(displayedContext(letterSharedEvent.target.querySelector('dl.context')!)).toEqual({
      'Client legal name': 'Evidence Letter Client Ltd', Service: 'ADVISORY',
      'Period start': '2024-04-01', 'Period end': '2025-03-31',
    });
    expect(letterSharedEvent.rowText).toContain('Evidence Letter Client Ltd');
    expect(letterSharedEvent.rowText).toContain('2024-04-01 to 2025-03-31');

    const scanEvent = events.find(event => event.href === `#scan-${attachedScan.id}`)!;
    expect(scanEvent.at).toBe(attachedScan.uploadedAt);
    expect(scanEvent.target.closest('article.document')?.id).toBe(`doc-review-${letter.reviewId}`);
    const bundleEvent = events.find(event => event.href === `#bundle-${historyBundle.id}`)!;
    expect(bundleEvent.linkLabel).toBe('Final bundle details');
    expect(displayedContext(bundleEvent.target)).toEqual({
      'Client legal name': 'Evidence Bundle Client Ltd', Service: 'TAX_ADVISORY',
      'Period start': '2025-04-01', 'Period end': '2026-03-31',
    });
    expect(bundleEvent.rowText).toContain('Evidence Bundle Client Ltd');
    expect(bundleEvent.target.querySelector(`a[href='/api/deliverable-bundles/${historyBundle.id}/download']`)).not.toBeNull();
    expect(events.some(event => event.href.includes(orphanScan.id))).toBe(false);
    expect(events.some(event => event.href.includes(wrongKindScan.id))).toBe(false);
    expect(events.some(event => event.href.includes(wrongVersionScan.id))).toBe(false);
    expect(events.some(event => event.rowText.includes(legacy.title))).toBe(false);
    expect(events.some(event => event.rowText.includes(invalidDate.title))).toBe(false);
    expect(events.some(event => event.at === invalidDate.sharedAt)).toBe(false);
    expect(Number.isNaN(Date.parse(invalidDate.sharedAt!))).toBe(true);
    const warning = Array.from(history.querySelectorAll<HTMLElement>('*'))
      .find(element => !element.closest('li') && element.children.length === 0 &&
        /(?:omitted|not displayed|not included|could not be ordered|cannot be ordered|invalid timestamp)/i.test(element.textContent ?? ''));
    expect(warning).toBeDefined();
    expect(warning?.hidden).toBe(false);
    expect(warning?.getAttribute('aria-hidden')).not.toBe('true');

    fixture.destroy();
    const reversedFixture = TestBed.createComponent(PortalDocuments);
    reversedFixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush({
      ...payload, documents: [...allDocuments].reverse(),
      signedLetters: [...payload.signedLetters].reverse(), bundles: [historyBundle],
    });
    TestBed.tick(); reversedFixture.detectChanges();
    const reversedRoot = reversedFixture.nativeElement as HTMLElement;
    const reversedEvents = historyRows(reversedRoot).rows.map(row => renderedEvent(reversedRoot, row));
    expect(reversedEvents.map(event => `${event.at}|${event.href}|${event.rowText}`))
      .toEqual(events.map(event => `${event.at}|${event.href}|${event.rowText}`));
    reversedFixture.destroy();
  });

  it('does not move focus after a cancelled or superseded document fragment navigation', async () => {
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const currentUrl = vi.spyOn(router, 'url', 'get').mockReturnValue('/portal');
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(false);
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush(documents(null, [{ ...sharedDocument, sharedAt: documentSharedAt }]));
    TestBed.tick(); fixture.detectChanges();
    const event = fixture.componentInstance.historyEvents().find(item => item.targetId === `doc-review-${documentReviewId}`)!;
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(`.history a[href$='#${event.targetId}']`)!;
    expect(link.getAttribute('href')).toBe(`/portal#${event.targetId}`);
    const modified = new MouseEvent('click', { button: 0, ctrlKey: true, cancelable: true });
    await fixture.componentInstance.openHistoryEvent(modified, event);
    expect(modified.defaultPrevented).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
    const target = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`#${event.targetId}`)!;
    const focus = vi.spyOn(target, 'focus');
    const scroll = vi.fn();
    Object.defineProperty(target, 'scrollIntoView', { configurable: true, value: scroll });
    try {
      const cancelled = new MouseEvent('click', { button: 0, cancelable: true });
      await fixture.componentInstance.openHistoryEvent(cancelled, event);
      expect(cancelled.defaultPrevented).toBe(true);
      expect(navigate).toHaveBeenCalledTimes(1);
      expect(focus).not.toHaveBeenCalled();
      expect(scroll).not.toHaveBeenCalled();
      navigate.mockImplementation(async () => { currentUrl.mockReturnValue('/portal#another-document'); return true; });
      await fixture.componentInstance.openHistoryEvent(new MouseEvent('click', { button: 0, cancelable: true }), event);
      expect(navigate).toHaveBeenCalledTimes(2);
      expect(focus).not.toHaveBeenCalled();
      expect(scroll).not.toHaveBeenCalled();
    } finally {
      focus.mockRestore(); navigate.mockRestore(); currentUrl.mockRestore(); fixture.destroy();
    }
  });

  it('pages 21 retained events without gaps or duplicates and returns to the same first page', () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalDocuments);
    fixture.detectChanges(); TestBed.tick();
    const items = Array.from({ length: 11 }, (_, index) => {
      const id = String(index + 1).padStart(12, '0');
      const minute = String(index).padStart(2, '0');
      return { ...sharedDocument, reviewId: `aaaaaaaa-aaaa-4aaa-8aaa-${id}`,
        deliverableId: `bbbbbbbb-bbbb-4bbb-8bbb-${id}`, title: `History report ${index + 1}`,
        sharedAt: `2026-10-17T10:${minute}:00Z`,
        acknowledgedAt: index < 10 ? `2026-10-17T10:${minute}:30Z` : null };
    });
    http.expectOne('/api/ui/portal/documents').flush({ ...documents(null, items), bundles: [] });
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const visible = () => historyRows(root).rows.map(row => {
      const event = renderedEvent(root, row);
      return `${event.at}|${event.href}|${event.rowText}`;
    });
    const pageOne = visible();
    expect(pageOne).toHaveLength(20);
    const buttons = () => Array.from(root.querySelectorAll<HTMLButtonElement>(
      "section[aria-label='Client document history'] button"));
    const previous = () => buttons().find(button => button.textContent?.trim() === 'Previous document events')!;
    const next = () => buttons().find(button => button.textContent?.trim() === 'Next document events')!;
    expect(previous().disabled).toBe(true);
    expect(next().disabled).toBe(false);

    next().click(); TestBed.tick(); fixture.detectChanges();
    const pageTwo = visible();
    expect(pageTwo).toHaveLength(1);
    expect(previous().disabled).toBe(false);
    expect(next().disabled).toBe(true);
    expect(new Set([...pageOne, ...pageTwo]).size).toBe(21);

    previous().click(); TestBed.tick(); fixture.detectChanges();
    expect(visible()).toEqual(pageOne);
    expect(previous().disabled).toBe(true);
    expect(next().disabled).toBe(false);
    next().click(); TestBed.tick(); fixture.detectChanges();
    fixture.componentInstance.ws.reload(); TestBed.tick();
    http.expectOne('/api/ui/portal/documents').flush({ ...documents(null,
      items.map((item, index) => index === 10 ? { ...item, acknowledgedAt: '2026-10-17T12:00:00Z' } : item)), bundles: [] });
    TestBed.tick(); fixture.detectChanges();
    expect(previous().disabled).toBe(true);
    expect(visible()).toHaveLength(20);
    expect(visible()[0]).toContain('2026-10-17T12:00:00Z');
    fixture.destroy();
  });
});
