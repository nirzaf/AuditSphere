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
  jurisdiction: 'QA', createdAt: '2026-01-02T12:30:00Z', isArchived: false, lastArchivedAt: null, lifecycleRevision: '0',
  safetyGeneration: '9007199254740993', canManageContacts: true, canManageArchive: true, canCreateEngagement: false,
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
  afterEach(() => {
    const http = TestBed.inject(HttpTestingController);
    // The progress panel loads its own projection (covered by engagement-progress.spec); this spec owns the client read.
    for (const panel of http.match(r => r.url.startsWith('/api/ui/clients/') && r.url.endsWith('/progress'))) expect(panel.request.method).toBe('GET');
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() { const f = TestBed.createComponent(ClientDetail); f.detectChanges(); TestBed.tick(); return f; }
  function read() {
    const active = TestBed.inject(HttpTestingController).match(r => r.method === 'GET' && r.url.startsWith('/api/ui/clients/') && !r.url.endsWith('/progress')).filter(r => !r.cancelled);
    expect(active).toHaveLength(1); return active[0];
  }
  it('renders source metadata, summaries, UTC date and separate bounded tables', () => {
    const f = open(); read().flush(payload); TestBed.tick();
    expect(f.nativeElement.textContent).toContain('Trading as: Synthetic trading');
    expect(f.nativeElement.textContent).toContain('9007199254740993');
    expect(f.nativeElement.textContent).toContain('2026-01-02 12:30 UTC');
    expect(f.nativeElement.querySelectorAll('tbody tr')).toHaveLength(2);
    // jsdom does not establish viewport visibility: keep the real viewport defer pending.
    expect(f.nativeElement.textContent).toContain('Organization hierarchy and correspondence routing');
    expect(f.nativeElement.querySelector('audit-client-relationships')).toBeNull();
    const nav = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    f.componentInstance.page('contact', 1);
    expect(nav).toHaveBeenCalledWith([], expect.objectContaining({queryParams:{...defaultClientLocation,contactPage:1}}));
    f.componentInstance.size('engagement','25');
    expect(nav).toHaveBeenCalledWith([], expect.objectContaining({queryParams:{...defaultClientLocation,engagementPageSize:25}}));
  });
  it('archives and restores with the exact lifecycle revision while keeping history searchable', () => {
    const f = open(); read().flush(payload); TestBed.tick();
    const archiveButton = f.nativeElement.querySelector('[data-client-archive]') as HTMLButtonElement;
    archiveButton.focus();
    expect(document.activeElement).toBe(archiveButton);
    f.componentInstance.setArchived(true);
    const command = TestBed.inject(HttpTestingController).expectOne(`/api/ui/clients/${id}/archive-state`);
    expect(command.request.method).toBe('PUT');
    expect(command.request.body).toEqual({ isArchived: true, expectedRevision: '0' });
    command.flush({ isArchived: true, lastArchivedAt: '2026-10-18T10:00:00Z', revision: '1' }); TestBed.tick();
    read().flush({ ...payload, isArchived: true, lastArchivedAt: '2026-10-18T10:00:00Z', lifecycleRevision: '1' }); TestBed.tick();
    expect(document.activeElement).toBe(f.nativeElement.querySelector('[data-client-archive]'));
    expect(document.activeElement?.textContent).toContain('Restore client');
    expect(f.nativeElement.textContent).toContain('Archived client record');
    expect(f.nativeElement.textContent).toContain('authorized engagement history');
    expect(f.componentInstance.data()?.metrics.engagements).toBe(27);
    f.componentInstance.setArchived(false);
    const restore = TestBed.inject(HttpTestingController).expectOne(`/api/ui/clients/${id}/archive-state`);
    expect(restore.request.body).toEqual({ isArchived: false, expectedRevision: '1' });
    restore.flush({ isArchived: false, lastArchivedAt: '2026-10-18T10:00:00Z', revision: '2' }); TestBed.tick();
    read().flush({ ...payload, lastArchivedAt: '2026-10-18T10:00:00Z', lifecycleRevision: '2' }); TestBed.tick();
    expect(document.activeElement).toBe(f.nativeElement.querySelector('[data-client-archive]'));
    expect(document.activeElement?.textContent).toContain('Archive client');
    expect(f.componentInstance.data()?.isArchived).toBe(false);
    expect(f.componentInstance.data()?.lastArchivedAt).toBe('2026-10-18T10:00:00Z');
    ids.next(convertToParamMap({ id: other })); TestBed.tick();
    expect(f.componentInstance.archiveMessage()).toBe('');
    expect(f.componentInstance.archiveFailed()).toBe(false);
    read().flush({ ...payload, id: other }); TestBed.tick();
  });
  it.each([
    { eventType: 'pointerdown', moveToBreadcrumb: false },
    { eventType: 'keydown', moveToBreadcrumb: true },
  ])('does not steal focus after $eventType during archive refresh', ({ eventType, moveToBreadcrumb }) => {
    const f = open(); read().flush(payload); TestBed.tick();
    (f.nativeElement.querySelector('[data-client-archive]') as HTMLButtonElement).focus();
    f.componentInstance.setArchived(true);
    TestBed.inject(HttpTestingController).expectOne(`/api/ui/clients/${id}/archive-state`)
      .flush({ isArchived: true, lastArchivedAt: '2026-10-18T10:00:00Z', revision: '1' });
    TestBed.tick();
    document.dispatchEvent(new Event(eventType, { bubbles: true }));
    const breadcrumb = f.nativeElement.querySelector('nav[aria-label="Breadcrumb"] a') as HTMLAnchorElement;
    if (moveToBreadcrumb) breadcrumb.focus();
    const expectedFocus = document.activeElement;
    expect(expectedFocus).toBe(moveToBreadcrumb ? breadcrumb : document.body);
    read().flush({ ...payload, isArchived: true, lastArchivedAt: '2026-10-18T10:00:00Z', lifecycleRevision: '1' });
    TestBed.tick();
    expect(document.activeElement).toBe(expectedFocus);
    expect(f.componentInstance.data()?.isArchived).toBe(true);
  });
  it('does not restore archive focus after query-only navigation before acknowledgement', () => {
    const f = open(); read().flush(payload); TestBed.tick();
    (f.nativeElement.querySelector('[data-client-archive]') as HTMLButtonElement).focus();
    f.componentInstance.setArchived(true);
    const command = TestBed.inject(HttpTestingController).expectOne(`/api/ui/clients/${id}/archive-state`);
    queries.next(convertToParamMap({ contactPage: '1' })); TestBed.tick();
    command.flush({ isArchived: true, lastArchivedAt: '2026-10-18T10:00:00Z', revision: '1' }); TestBed.tick();
    read().flush({ ...payload, isArchived: true, lastArchivedAt: '2026-10-18T10:00:00Z', lifecycleRevision: '1',
      paging: { ...defaultClientLocation, contactPage: 1 } }); TestBed.tick();
    expect(f.componentInstance.location()?.contactPage).toBe(1);
    expect(f.componentInstance.data()?.isArchived).toBe(true);
    expect(document.activeElement).not.toBe(f.nativeElement.querySelector('[data-client-archive]'));
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
