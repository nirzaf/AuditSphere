import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { PortalDocuments } from './documents';

const userId = '11111111-1111-4111-8111-111111111111';
const firmId = '22222222-2222-4222-8222-222222222222';
const bundleId = '33333333-3333-4333-8333-333333333333';
const receiptId = '44444444-4444-4444-8444-444444444444';
const contentSha256 = 'a'.repeat(64);
const manifestSha256 = 'b'.repeat(64);
const documentReviewId = '55555555-5555-4555-8555-555555555555';
const deliverableId = '66666666-6666-4666-8666-666666666666';
const bundle = (acknowledgedAt: string | null = null) => ({
  id: bundleId, sha256: contentSha256, manifestSha256, assembledAt: '2026-10-15T10:00:00Z', acknowledgedAt,
});
const sharedDocument = { reviewId: documentReviewId, deliverableId, kind: 'AUDIT_REPORT', title: 'Audited financial statements',
  version: '3', sha256: contentSha256, acknowledgedAt: null, comments: [] };
const documents = (acknowledgedAt: string | null = null, items: Array<typeof sharedDocument> = []) => ({
  documents: items, signedLetters: [], bundles: [bundle(acknowledgedAt)],
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

    fixture.componentInstance.select(documentReviewId);
    fixture.componentInstance.review.controls.body.setValue('Please clarify this statement.');
    fixture.componentInstance.review.controls.body.markAsDirty();
    const sent = fixture.componentInstance.comment(documentReviewId);
    const accepted = http.expectOne({ method: 'POST', url: `/api/ui/portal/documents/${documentReviewId}/comment` });
    expect(accepted.request.body).toEqual({ body: 'Please clarify this statement.' });
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
    http.expectOne('/api/ui/portal/documents').flush(documents());
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const button = () => Array.from(root.querySelectorAll('button')).find(x => x.textContent?.includes('Record receipt acknowledgment'))!;
    expect(button().disabled).toBe(true);
    expect(root.textContent).toContain(contentSha256);
    expect(root.textContent).toContain(manifestSha256);

    const checkbox = root.querySelector('input[type="checkbox"]') as HTMLInputElement;
    checkbox.checked = true; checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(button().disabled).toBe(false);
    const action = fixture.componentInstance.acknowledgeBundle(bundle());
    const post = http.expectOne({ method: 'POST', url: `/api/ui/portal/bundles/${bundleId}/acknowledge` });
    expect(post.request.body).toEqual({ contentSha256, manifestSha256, received: true });
    post.flush({ value: receiptId });
    await action; fixture.detectChanges();

    http.expectOne('/api/ui/portal/documents').flush(documents('2026-10-15T10:05:00Z'));
    await fixture.whenStable(); TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).toContain('Receipt acknowledged by your client account');
    expect(root.textContent).toContain('not proof of email delivery');
    expect(root.querySelector('input[type="checkbox"]')).toBeNull();
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
