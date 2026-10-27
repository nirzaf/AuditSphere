import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { decodeOversightAccess, OversightAccessHistory } from './oversight-access';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const at = '2026-10-27T20:00:00Z';
const event = (n: number) => ({ id: id(n), actorUserId: id(90), sessionEpoch: '9223372036854775807', requestId: id(100 + n),
  operation: 'firm-finance.read', resourceKind: 'FIRM', resourceId: null, authorizedAt: at, outcome: 'DISCLOSURE_AUTHORIZED' });
const page = (count = 25, hasMore = true) => ({ items: Array.from({ length: count }, (_, i) => event(25 - i)), hasMore,
  before: count ? at : null, beforeId: count ? id(26 - count) : null });
const url = '/api/ui/administration/oversight-access';

describe('oversight access history', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [OversightAccessHistory], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  it('preserves exact session epochs and refuses inconsistent scope, cursor or unsupported outcomes', () => {
    expect(decodeOversightAccess(page(), 'p').items[0].sessionEpoch).toBe('9223372036854775807');
    for (const value of [
      { ...page(), beforeId: id(999) },
      { ...page(), items: [{ ...event(1), sessionEpoch: '9223372036854775808' }] },
      { ...page(), items: [{ ...event(1), resourceId: id(99) }] },
      { ...page(), items: [{ ...event(1), outcome: 'RECEIVED_AND_APPROVED' }] },
    ]) expect(() => decodeOversightAccess(value, 'p')).toThrow();
  });
  it('uses the exact server cursor for older/newer pages and exposes no record mutation', () => {
    const f = TestBed.createComponent(OversightAccessHistory); f.detectChanges(); TestBed.tick();
    http.expectOne(url).flush(page()); TestBed.tick(); f.detectChanges();
    expect(f.nativeElement.textContent).toContain('not prove delivery');
    expect(f.nativeElement.textContent).toContain('9223372036854775807');
    f.componentInstance.older(); TestBed.tick();
    http.expectOne(`${url}?before=${encodeURIComponent(at)}&beforeId=${id(1)}`).flush(page(1, false)); TestBed.tick(); f.detectChanges();
    expect(f.componentInstance.previous().length).toBe(1);
    f.componentInstance.newer(); TestBed.tick(); http.expectOne(url).flush(page()); TestBed.tick(); f.detectChanges();
    expect(f.componentInstance.previous()).toEqual([]);
    expect((f.nativeElement as HTMLElement).querySelectorAll('form, input, textarea').length).toBe(0);
    http.expectNone(r => r.method !== 'GET');
  });
  it('removes retained identities when a refresh loses authorization', () => {
    const f = TestBed.createComponent(OversightAccessHistory); f.detectChanges(); TestBed.tick();
    http.expectOne(url).flush(page(1, false)); TestBed.tick(); f.detectChanges();
    expect(f.nativeElement.textContent).toContain(id(90));
    f.componentInstance.newest(); TestBed.tick();
    http.expectOne(url).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' }); TestBed.tick(); f.detectChanges();
    expect(f.componentInstance.events.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain(id(90));
    expect(f.nativeElement.textContent).toContain('Administrator access is required');
  });
});
