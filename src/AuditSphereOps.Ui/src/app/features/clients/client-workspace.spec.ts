import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { ClientDetail } from './client';
import { clientLocation, clientUtcTime, decodeClient, defaultClientLocation, portalIntentExplanation } from './client-contracts';
const id = '11111111-1111-4111-8111-111111111111', other = '22222222-2222-4222-8222-222222222222';
const payload = { id, name: 'Scoped synthetic client', status: 'PROSPECT', commercialName: 'Synthetic trading', registrationNumber: 'SYNTHETIC-REG',
  jurisdiction: 'QA', createdAt: '2026-01-02T12:30:00Z', safetyGeneration: '9007199254740993', canManageContacts: true, canCreateEngagement: false,
  metrics: { engagements: 27, workBlocked: 13, contacts: 27 }, paging: { ...defaultClientLocation },
  engagements: [{ id, serviceRoute: 'Audit', status: 'Draft', periodStart: '2026-01-01', periodEnd: '2026-12-31', professionalWorkBlocked: true }],
  contacts: [{ id, name: 'Synthetic contact', email: 'client@example.test', role: 'Finance', primary: true }],
  portalIntent: { state: 'AWAITING_ACCEPTANCE', recipientEmail: 'client@example.test', updatedAt: '2026-01-02T12:30:00Z', contactHasClientAccess: false },
};
describe('Native client profile bounds and metadata', () => {
  it('preserves exact generation and UTC metadata while excluding unrequested profiles', () => {
    expect(decodeClient({ ...payload, restrictedProfile: 'PRIVATE' }).safetyGeneration).toBe('9007199254740993');
    expect(decodeClient({ ...payload, restrictedProfile: 'PRIVATE' })).not.toHaveProperty('restrictedProfile');
    expect(clientUtcTime(payload.createdAt)).toBe('2026-01-02 12:30 UTC');
    expect(clientUtcTime('bad')).toBe('Unsupported timestamp');
    expect(portalIntentExplanation('INVITED')).toContain('checked separately');
  });
  it('rejects broken counts, response page bounds, duplicate rows and unsupported portal states', () => {
    for (const v of [
      { ...payload, safetyGeneration: 9007199254740993 }, { ...payload, safetyGeneration: '9223372036854775808' },
      { ...payload, paging: { ...payload.paging, contactPageSize: 100 } },
      { ...payload, metrics: { ...payload.metrics, workBlocked: 28 } },
      { ...payload, contacts: [payload.contacts[0], payload.contacts[0]] },
      { ...payload, engagements: Array(51).fill(payload.engagements[0]) },
      { ...payload, portalIntent: { ...payload.portalIntent, state: 'MICROSOFT_VERIFIED' } },
    ]) expect(() => decodeClient(v)).toThrow();
  });
  it('allows only bounded independent URL pages and preserved page sizes', () => {
    expect(clientLocation(k => ({contactPage:'2',contactPageSize:'25'} as Record<string,string>)[k] ?? null)).toEqual({ ...defaultClientLocation, contactPage: 2, contactPageSize: 25 });
    for (const values of [{contactPage:'-1'},{engagementPage:'10001'},{engagementPage:'01'},{contactPageSize:'100'}])
      expect(clientLocation(k => (values as Record<string,string | undefined>)[k] ?? null)).toBeNull();
  });
});
describe('Client profile request and command ownership', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>, queries: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    ids = new BehaviorSubject(convertToParamMap({ id })); queries = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({ imports: [ClientDetail], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: ids, queryParamMap: queries, snapshot: { queryParamMap: queries.value } } }] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function open() { const f = TestBed.createComponent(ClientDetail); f.detectChanges(); TestBed.tick(); return f; }
  function read() {
    const active = TestBed.inject(HttpTestingController).match(r => r.method === 'GET' && r.url.startsWith('/api/ui/clients/')).filter(r => !r.cancelled);
    expect(active).toHaveLength(1); return active[0];
  }
  it('renders source metadata, summaries, UTC date and separate bounded tables', () => {
    const f = open(); read().flush(payload); TestBed.tick();
    expect(f.nativeElement.textContent).toContain('Trading as: Synthetic trading');
    expect(f.nativeElement.textContent).toContain('9007199254740993');
    expect(f.nativeElement.textContent).toContain('2026-01-02 12:30 UTC');
    expect(f.nativeElement.querySelectorAll('tbody tr')).toHaveLength(2);
    const nav = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    f.componentInstance.page('contact', 1);
    expect(nav).toHaveBeenCalledWith([], expect.objectContaining({queryParams:{...defaultClientLocation,contactPage:1}}));
    f.componentInstance.size('engagement','25');
    expect(nav).toHaveBeenCalledWith([], expect.objectContaining({queryParams:{...defaultClientLocation,engagementPageSize:25}}));
  });
  it('cancels superseded reads and rejects mismatched client or page responses', () => {
    const f = open(), old = read(); ids.next(convertToParamMap({id:other})); TestBed.tick();
    expect(old.cancelled).toBe(true); read().flush(payload); TestBed.tick();
    expect(f.componentInstance.data()).toBeNull(); expect(f.componentInstance.error()).toContain('unsupported');
    queries.next(convertToParamMap({contactPage:'2'})); TestBed.tick(); read().flush({...payload,id:other}); TestBed.tick();
    expect(f.componentInstance.data()).toBeNull();
  });
  it('clears protected metadata when the session ends', () => {
    const f = open(); read().flush(payload); TestBed.tick();
    TestBed.inject(SessionService).clear(); TestBed.tick();
    expect(f.componentInstance.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('SYNTHETIC-REG');
  });
  it('links authorized creation to separate native route segments and preserves source pages',()=>{
    const f=open();read().flush({...payload,canCreateEngagement:true});TestBed.tick();
    const link=f.nativeElement.querySelector('a[href*="/engagements/new"]');expect(link).not.toBeNull();
    expect(link.getAttribute('href')).toContain('/app/clients/'+id+'/engagements/new?');
    expect(link.getAttribute('href')).not.toContain('%2F');
    expect(f.nativeElement.querySelector('input[name="serviceRoute"]')).toBeNull();
  });

});
