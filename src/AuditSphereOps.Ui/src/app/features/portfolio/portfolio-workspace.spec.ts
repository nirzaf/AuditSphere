import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { Portfolio } from './portfolio';
import { PortfolioNavigation, decodePortfolioWorkspace, portfolioLocation } from './portfolio-contracts';
import { SessionService } from '../../core/session';
import { Api, CommandOutcome } from '../../core/api';
const id = '11111111-1111-4111-8111-111111111111';
const payload = {
  search: '', clients: { items: [{ id, name: 'Scoped client', engagements: 1 }], total: 1, page: 0, pageSize: 25 },
  metrics: { clients: 1, engagements: 1, openHolds: 1, pendingOperations: 1, readyCandidates: 1, issuedReleases: 0 },
  hasActiveMicrosoftConfiguration: false, recentLimit: 25, candidateTotal: 1, packageTotal: 0,
  candidates: [{ id, clientId: id, engagementId: id, clientName: 'Scoped client', targetKind: 'WORKPAPER', targetRevision: '9007199254740993', status: 'READY', createdAt: '2026-10-01T12:00:00Z' }], packages: [],
};

describe('Portfolio workspace and navigation contracts', () => {
  it('preserves exact revisions and rejects invalid or unbounded projections', () => {
    expect(decodePortfolioWorkspace(payload).candidates[0].targetRevision).toBe('9007199254740993');
    expect(() => decodePortfolioWorkspace({ ...payload, recentLimit: 1000 })).toThrow();
    expect(() => decodePortfolioWorkspace({ ...payload, candidates: Array(26).fill(payload.candidates[0]) })).toThrow();
    expect(() => decodePortfolioWorkspace({ ...payload, metrics: { ...payload.metrics, clients: 0 } })).toThrow();
    expect(() => decodePortfolioWorkspace({ ...payload, candidates: [{ ...payload.candidates[0], targetRevision: 9007199254740992 }] })).toThrow();
  });
  it('bounds URL filters, pages and selected identities', () => {
    expect(portfolioLocation(k => ({ search: '  Name ', page: '2', selected: id } as Record<string,string>)[k] ?? null)).toEqual({ search: 'Name', page: 2, pageSize: 25, selected: id });
    for (const values of [{ page: '-1' }, { page: '10001' }, { page: '01' }, { selected: 'email@test' }, { search: 'a'.repeat(101) }])
      expect(portfolioLocation(k => (values as Record<string,string | undefined>)[k] ?? null)).toBeNull();
  });
});

describe('Native portfolio reads and export fences', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({ imports: [Portfolio], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { queryParamMap: params, snapshot: { queryParamMap: params.value } } }] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function open() {
    const f = TestBed.createComponent(Portfolio); f.detectChanges(); TestBed.tick();
    return f;
  }
  function read() { return TestBed.inject(HttpTestingController).expectOne(r => r.url.startsWith('/api/ui/portfolio/workspace?')); }
  it('renders scoped summary and panels without fabricating external verification', () => {
    const f = open(); read().flush(payload); TestBed.tick();
    const text = f.nativeElement.textContent;
    expect(text).toContain('Open holds'); expect(text).toContain('9007199254740993');
    expect(text).toContain('No active Microsoft 365 configuration'); expect(text).toContain('No financial packages');
    expect(f.nativeElement.querySelectorAll('.portfolio-clients tbody tr').length).toBe(1);
  });
  it('cancels old reads and refuses mismatched search/page responses', () => {
    const f = open(), old = read(); params.next(convertToParamMap({ search: 'NEXT', page: '1' })); TestBed.tick();
    expect(old.cancelled).toBe(true); read().flush(payload); TestBed.tick();
    expect(f.componentInstance.view.data()).toBeNull(); expect(f.componentInstance.view.error()).toContain('Unsupported response');
  });
  it('removes protected content and the typed filter when the session ends', () => {
    params.next(convertToParamMap({ search: 'private' })); const f = open();
    read().flush({ ...payload, search: 'private' }); TestBed.tick();
    const session = TestBed.inject(SessionService); session.clear(); TestBed.tick();
    expect(f.componentInstance.view.data()).toBeNull(); expect(f.componentInstance.model().search).toBe('');
    expect(f.nativeElement.textContent).not.toContain('Scoped client');
  });
  it('retains only location under the exact identity and epoch, never portfolio data', () => {
    const navigation = TestBed.inject(PortfolioNavigation); navigation.remember({ search: 'client', page: 2, pageSize: 25, selected: id });
    expect(navigation.params()).toEqual({ search: 'client', page: 2, pageSize: 25, selected: id });
    TestBed.inject(SessionService).invalidation.update(n => n + 1);
    expect(navigation.read()).toBeNull();
    navigation.remember({ search: 'new', page: 0, pageSize: 25, selected: null });
    TestBed.inject(SessionService).current.update(s => s && ({ ...s, userId: '22222222-2222-4222-8222-222222222222' }));
    expect(navigation.read()).toBeNull();
  });
  it('supports only reviewed client page sizes without losing the search', () => {
    params.next(convertToParamMap({ search: 'client', pageSize: '10' }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const f = open(); read().flush({ ...payload, search: 'client', clients: { ...payload.clients, pageSize: 10 } }); TestBed.tick();
    expect(f.componentInstance.view.data()?.clients.pageSize).toBe(10);
    f.componentInstance.resizePage('25');
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { search: 'client', page: 0, pageSize: 25 } }));
    expect(f.componentInstance.createdAt('bad')).toBe('Unsupported timestamp');
  });
  it('restores an explicit client-list return without reusing protected results', async () => {
    TestBed.inject(PortfolioNavigation).remember({ search: 'restored', page: 2, pageSize: 25, selected: id });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    open(); read().flush(payload); TestBed.tick();
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { search: 'restored', page: 2, pageSize: 25, selected: id }, replaceUrl: true }));
  });
  it('fences a late export after filter change or component destruction', async () => {
    const f = open(); read().flush(payload); TestBed.tick();
    let finish!: (v: CommandOutcome<{ fileName: string; headers: Record<string,string> }>) => void;
    let accept!: (metadata: { fileName: string; headers: Record<string,string>; byteCount: number; contentType: string }) => boolean;
    vi.spyOn(TestBed.inject(Api), 'download').mockImplementation((_url, _body, callback) => {
      accept = callback!; return new Promise(resolve => { finish = resolve; });
    });
    const pending = f.componentInstance.download();
    const metadata = { fileName: 'auditsphere-portfolio.csv', headers: {}, byteCount: 100, contentType: 'text/csv; charset=utf-8' };
    expect(accept(metadata)).toBe(true); expect(accept({ ...metadata, fileName: 'unexpected.html' })).toBe(false);
    params.next(convertToParamMap({ search: 'changed' })); TestBed.tick(); read().flush({ ...payload, search: 'changed' }); TestBed.tick();
    expect(accept(metadata)).toBe(false); f.destroy();
    finish({ ok: true, value: { fileName: metadata.fileName, headers: {} } }); await pending;
    expect(f.componentInstance.message()).toBe('');
  });
});
