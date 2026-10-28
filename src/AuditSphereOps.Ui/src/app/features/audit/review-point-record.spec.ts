import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { ReviewPointRecord } from './records';
import { reviewPointTestId as id, reviewPointTestRecord as record, reviewPointTestState as state, reviewPointTestPreview as preview, reviewPointTestReceipt as receipt } from './review-point-command-test-data';

describe('Review point detail scope, refresh and disposition controls', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>, http: HttpTestingController;
  const url = (n = 1) => `/api/ui/reviews/${id(n)}`;
  beforeEach(() => {
    sessionStorage.clear(); params = new BehaviorSubject(convertToParamMap({ id: id(1) }));
    TestBed.configureTestingModule({ imports: [ReviewPointRecord], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true }); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); sessionStorage.clear(); } });
  function open(payload = record()) {
    const fixture = TestBed.createComponent(ReviewPointRecord); fixture.detectChanges(); TestBed.tick(); http.expectOne(url()).flush(payload); TestBed.tick(); return fixture;
  }
  async function settle() { for (let i = 0; i < 8; ++i) await Promise.resolve(); TestBed.tick(); }
  it('renders separate immutable target and own disposition revisions without independence claims', () => {
    const f = open(); expect(f.nativeElement.textContent).toContain('Target revision'); expect(f.nativeElement.textContent).toContain('Disposition revision');
    expect(f.componentInstance.rec.data()?.targetRevision).toBe('7'); expect(f.componentInstance.rec.data()?.dispositionRevision).toBe('11');
    expect(f.nativeElement.textContent).toContain('recorded disposition'); expect(f.nativeElement.textContent).not.toContain('independent clearing decision');
  });
  it('rejects a well-shaped returned point with the wrong ID', () => {
    const f = open(record('CLEAR', 2)); expect(f.componentInstance.rec.data()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('Synthetic original review comment');
  });
  it('uses canChangeDisposition to hide and block unavailable actions, including programmatic calls', () => {
    const f = open({ ...record(), canChangeDisposition: false }); f.componentInstance.set(id(1), true);
    expect(f.nativeElement.textContent).not.toContain('Preview clearing review point'); http.expectNone(r => r.method === 'POST' || r.url.endsWith('/commands/state'));
  });
  it('supports explicit refresh, invalidates assent on source change, and clears denied content', async () => {
    const f = open(), page = f.componentInstance; page.set(id(1), true);
    http.expectOne(url() + '/commands/state').flush(state()); await settle(); const request = http.expectOne(url() + '/commands/preview'); request.flush(preview(request.request.body)); await settle();
    page.flow.reviewed.set(true); page.rec.reload(); TestBed.tick(); http.expectOne(url()).flush({ ...record(), dispositionRevision: '12' }); TestBed.tick();
    expect(page.flow.reviewed()).toBe(false); expect(page.flow.preview()).toBeNull();
    page.rec.reload(); TestBed.tick(); http.expectOne(url()).flush({}, { status: 403, statusText: 'Forbidden' }); TestBed.tick();
    expect(page.rec.data()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('Synthetic original review comment'); http.expectNone(r => r.url.endsWith('/actions'));
  });
  it.each(['success', 'lost-committed'])('keeps the exact verified %s receipt visible during refresh, then hides it on scope denial', async mode => {
    const f = open(record('REOPEN')), page = f.componentInstance;
    page.set(id(1), false); http.expectOne(url() + '/commands/state').flush(state('REOPEN')); await settle();
    const prepare = http.expectOne(url() + '/commands/preview'), p = preview(prepare.request.body);
    prepare.flush(p); await settle(); page.flow.reviewed.set(true); const execute = page.flow.execute();
    const command = http.expectOne(url() + '/commands/actions'), result = receipt(p);
    if (mode === 'success') command.flush(result);
    else command.flush({}, { status: 500, statusText: 'Lost committed response' });
    await execute; await settle();
    if (mode === 'lost-committed') {
      const recover = page.flow.reconcile('REOPEN');
      http.expectOne(url() + `/commands/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: true, receipt: result }); await settle();
      http.expectOne(url() + '/commands/state').flush({ ...state(), reviewPoint: result.dispositionEvent.after });
      await recover; await settle();
    }
    const refresh = http.expectOne(url());
    expect(page.rec.loading()).toBe(true); expect(page.rec.data()).toBeNull();
    expect(page.flow.receipt()?.requestId).toBe(p.requestId);
    const panel = f.nativeElement.querySelector('[aria-label="Verified review point receipt"]');
    expect(panel).not.toBeNull(); expect(panel.textContent).toContain('REOPEN'); expect(panel.textContent).toContain(p.requestId);
    expect(f.nativeElement.querySelector('#context-heading')).toBeNull();
    refresh.flush({ ...record(), dispositionRevision: '12' }); TestBed.tick();
    expect(f.nativeElement.querySelector('[aria-label="Verified review point receipt"]')).toBe(panel);
    page.rec.reload(); TestBed.tick();
    expect(f.nativeElement.querySelector('[aria-label="Verified review point receipt"]')).toBe(panel);
    http.expectOne(url()).flush({}, { status: 403, statusText: 'Forbidden' }); TestBed.tick();
    expect(f.nativeElement.querySelector('[aria-label="Verified review point receipt"]')).toBeNull();
    http.expectNone(r => r.method === 'POST');
  });
  it('cancels previous A/B reads and prevents commands against an old point', () => {
    const f = TestBed.createComponent(ReviewPointRecord); f.detectChanges(); TestBed.tick(); const a = http.expectOne(url());
    params.next(convertToParamMap({ id: id(2) })); TestBed.tick(); const b = http.expectOne(url(2));
    params.next(convertToParamMap({ id: id(1) })); TestBed.tick(); const latest = http.expectOne(url());
    expect(a.cancelled).toBe(true); expect(b.cancelled).toBe(true); latest.flush(record()); TestBed.tick();
    f.componentInstance.set(id(2), true); http.expectNone(r => r.method === 'POST' || r.url.endsWith('/commands/state'));
  });
  it('does not retain protected point content after session loss', () => {
    const f = open(); f.componentInstance.flow.reviewed.set(true); TestBed.inject(SessionService).current.set(null); TestBed.tick();
    expect(f.componentInstance.rec.data()).toBeNull(); expect(f.componentInstance.flow.reviewed()).toBe(false); expect(f.nativeElement.textContent).not.toContain('Synthetic original review comment');
  });
  it('rejects a state with another immutable target before posting a preview', async () => {
    const f = open(); f.componentInstance.set(id(1), true); http.expectOne(url() + '/commands/state').flush({ ...state(), reviewPoint: { ...state().reviewPoint, targetId: id(99) } });
    await settle(); expect(f.componentInstance.flow.preview()).toBeNull(); http.expectNone(r => r.method === 'POST');
  });
});
