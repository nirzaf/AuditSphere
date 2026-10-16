import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { describe, expect, it } from 'vitest';
import { decodeCatalogue, decodeEntry, decodeHits, TechnicalLibrary } from './library';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const hash64 = 'a'.repeat(64);

describe('Technical Library Contracts', () => {
  it('decodes a valid technical library catalogue', () => {
    const raw = [
      {
        id: id1,
        code: 'IFRS-15',
        title: 'Revenue from Contracts with Customers',
        category: 'IFRS',
        audience: 'FIRM_WIDE',
        publishedVersion: 2,
        hasDraft: false,
      },
    ];

    const decoded = decodeCatalogue(raw, 'catalogue');
    expect(decoded.length).toBe(1);
    expect(decoded[0].code).toBe('IFRS-15');
    expect(decoded[0].publishedVersion).toBe(2);
  });

  it('decodes a valid technical library entry', () => {
    const raw = {
      id: id1,
      code: 'IFRS-15',
      title: 'Revenue from Contracts with Customers',
      category: 'IFRS',
      version: {
        id: id2,
        version: 2,
        status: 'PUBLISHED',
        effectiveFrom: '2026-01-01',
        sourceReference: 'IASB Official Standard 2026',
        contentSha256: hash64,
        body: 'Full standard content...',
      },
      history: [
        {
          id: id2,
          version: 2,
          status: 'PUBLISHED',
          effectiveFrom: '2026-01-01',
          sourceReference: 'IASB Official Standard 2026',
          contentSha256: hash64,
          body: 'Full standard content...',
        },
      ],
    };

    const decoded = decodeEntry(raw, 'entry');
    expect(decoded.id).toBe(id1);
    expect(decoded.version.version).toBe(2);
    expect(decoded.history.length).toBe(1);
  });

  it('decodes search hits', () => {
    const raw = [
      {
        documentId: id1,
        code: 'IFRS-15',
        title: 'Revenue from Contracts with Customers',
        category: 'IFRS',
        version: 2,
        effectiveFrom: '2026-01-01',
        snippet: '...performance obligations satisfied over time...',
      },
    ];

    const decoded = decodeHits(raw, 'hits');
    expect(decoded.length).toBe(1);
    expect(decoded[0].code).toBe('IFRS-15');
  });

  it('rejects invalid payloads', () => {
    expect(() => decodeCatalogue(null, 'catalogue')).toThrow();
    expect(() => decodeEntry(null, 'entry')).toThrow();
  });
});

const entryPayload = (body = 'Published body', id = id1, selected = 1) => ({ id, code: 'SYN-001', title: 'Synthetic guidance', category: 'ISA',
  version: { id: id2, version: selected, status: 'PUBLISHED', effectiveFrom: '2026-01-01', sourceReference: 'Synthetic source', contentSha256: hash64, body },
  history: [{ id: id2, version: selected, status: 'DRAFT', effectiveFrom: '2026-01-01', sourceReference: 'Synthetic source', contentSha256: hash64, body: null }] });
const hit = (title: string) => [{ documentId: id1, code: 'SYN-001', title, category: 'ISA', version: 1, effectiveFrom: '2026-01-01', snippet: title }];

describe('Technical library scoped reads and honest unknown-action recovery', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id: id1 })); query = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({ imports: [TechnicalLibrary], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params, queryParamMap: query } }] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id1, firmId: id1, generation: '1', staff: true });
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function open(payload = entryPayload()) {
    const f = TestBed.createComponent(TechnicalLibrary); f.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/library').flush([]); http.expectOne('/api/ui/library/' + id1).flush(payload); TestBed.tick(); return f;
  }
  async function settle() { for (let i = 0; i < 8; i++) await Promise.resolve(); TestBed.tick(); }
  function refreshReads(payload = entryPayload()) {
    http.match(r => r.method === 'GET' && r.url === '/api/ui/library').forEach(r => r.flush([]));
    http.match(r => r.method === 'GET' && r.url === '/api/ui/library/' + id1).forEach(r => r.flush(payload));
    TestBed.tick();
  }
  it.each([20001, 200000])('renders a backend-valid %s-character body without truncation', size => {
    const body = 'x'.repeat(size), f = open(entryPayload(body));
    expect(f.nativeElement.querySelector('.library-body').textContent).toBe(body);
  });
  it('rejects a body beyond the supported 200000-character bound', () => {
    expect(() => decodeEntry(entryPayload('x'.repeat(200001)), '')).toThrow();
  });
  it('refuses wrong document and requested-version responses before exposing content/actions', () => {
    const f = open(entryPayload('PRIVATE wrong document', id2));
    expect(f.componentInstance.entry.data()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('PRIVATE wrong document');
    query.next(convertToParamMap({ v: '2' })); TestBed.tick();
    http.expectOne('/api/ui/library/' + id1 + '?v=2').flush(entryPayload('PRIVATE wrong version', id1, 1)); TestBed.tick();
    expect(f.componentInstance.entry.data()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('PRIVATE wrong version');
    expect(f.nativeElement.querySelector('section[aria-labelledby="entry-heading"]')).toBeNull();
    http.expectNone(r => r.method !== 'GET');
  });
  it('keeps only the newest search response and ignores older failures', async () => {
    const f = open(), c = f.componentInstance;
    c.term = 'old'; const old = c.search(); const oldRead = http.expectOne('/api/ui/library/search?term=old');
    c.term = 'new'; const latest = c.search(); http.expectOne('/api/ui/library/search?term=new').flush(hit('Latest guidance')); await latest;
    oldRead.flush({}, { status: 503, statusText: 'Unavailable' }); await old;
    expect(c.hits()?.[0].title).toBe('Latest guidance'); expect(c.searchError()).toBe('');
  });
  it.each(['route', 'query', 'session', 'destroy'] as const)('fences late search replies after %s changes', async change => {
    const f = open(), c = f.componentInstance; c.term = 'private'; const searching = c.search();
    const read = http.expectOne('/api/ui/library/search?term=private');
    if (change === 'route') { params.next(convertToParamMap({ id: id2 })); params.next(convertToParamMap({ id: id1 })); }
    if (change === 'query') { query.next(convertToParamMap({ v: '2' })); query.next(convertToParamMap({})); }
    if (change === 'session') TestBed.inject(SessionService).clear();
    if (change === 'destroy') f.destroy();
    TestBed.tick(); read.flush(hit('PRIVATE late hit')); await searching; refreshReads();
    expect(c.hits()).toBeNull(); expect(c.searchError()).toBe('');
  });
  it('clears already-rendered protected search hits on session loss', async () => {
    const f = open(), c = f.componentInstance; c.term = 'leadership'; const searching = c.search();
    http.expectOne('/api/ui/library/search?term=leadership').flush(hit('PRIVATE leadership guidance')); await searching; TestBed.tick();
    expect(f.nativeElement.textContent).toContain('PRIVATE leadership guidance');
    TestBed.inject(SessionService).clear(); TestBed.tick();
    expect(c.hits()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('PRIVATE leadership guidance');
  });
  it.each(['create', 'draft', 'publish'] as const)('keeps unknown %s disabled and refreshes without resending or claiming an original receipt', async action => {
    const f = open(), c = f.componentInstance;
    c.fresh = { ...c.fresh, code: 'SYN-NEW', title: 'Private local title', body: 'Private local body', source: 'Internal source' };
    c.next = { ...c.next, body: 'Private local draft', source: 'Internal source' };
    if (action === 'create') c.create(); else if (action === 'draft') c.draft(id1); else c.publish(id2);
    const post = http.expectOne(r => r.method === 'POST'); post.error(new ProgressEvent('error')); await settle(); refreshReads();
    expect(c.cmd.uncertain()).toBe(true); expect(c.commandsBlocked()).toBe(true);
    c.create(); c.draft(id1); c.publish(id2); http.expectNone(r => r.method === 'POST');
    expect(f.nativeElement.querySelector('button[type="submit"][disabled]')).not.toBeNull();
    const refreshing = c.reconcile(); refreshReads(); await settle(); refreshReads(); await refreshing;
    // The final awaited entry read resumes reconcile after refreshReads' last render tick.
    // Flush that continuation's signal writes before asserting the rendered recovery controls.
    TestBed.tick(); f.detectChanges();
    expect(c.catalogue.loading()).toBe(false); expect(c.catalogue.error()).toBe('');
    expect(c.entry.loading()).toBe(false); expect(c.entry.error()).toBe('');
    expect(c.entry.data()?.id).toBe(id1);
    expect(Array.from(f.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes('Discard local command fields'))).toBeDefined();
    expect(c.reconciliationLoaded()).toBe(true); expect(c.cmd.uncertain()).toBe(true);
    expect(f.nativeElement.textContent).toContain('original outcome remains unconfirmed');
    http.expectNone(r => r.method === 'POST');
    c.acknowledgeUnconfirmed(); TestBed.tick();
    expect(c.cmd.uncertain()).toBe(false); expect(c.next.body).toBe(''); expect(c.fresh.body).toBe(''); expect(c.fresh.code).toBe('');
    http.expectNone(r => r.method === 'POST');
  });
  it('does not apply late mutation success to a reused A-B-A view or resend after navigation', async () => {
    const f = open(), c = f.componentInstance; c.create(); const post = http.expectOne(r => r.method === 'POST');
    params.next(convertToParamMap({ id: id2 })); params.next(convertToParamMap({ id: id1 })); TestBed.tick();
    post.flush({ value: id1 }); await settle();
    expect(c.cmd.uncertain()).toBe(true); expect(c.cmd.busy()).toBe(false);
    expect(c.cmd.message()).not.toContain('Entry created as a draft');
    c.create(); http.expectNone(r => r.method === 'POST');
  });
  it('keeps edits typed during successful draft publication', async () => {
    const f = open(), c = f.componentInstance; c.next = { ...c.next, body: 'Sent draft', source: 'Sent source' };
    c.draft(id1); const post = http.expectOne(r => r.method === 'POST');
    c.next = { ...c.next, body: 'Newer local draft' }; post.flush({ value: id2 }); await settle(); refreshReads();
    expect(c.next.body).toBe('Newer local draft'); expect(c.cmd.uncertain()).toBe(false);
  });
  it('does not acknowledge uncertainty when persisted-state refresh fails', async () => {
    const f = open(), c = f.componentInstance; c.create(); http.expectOne(r => r.method === 'POST').error(new ProgressEvent('error'));
    await settle(); refreshReads();
    const refreshing = c.reconcile();
    http.match(r => r.method === 'GET' && r.url === '/api/ui/library').forEach(r => r.flush({}, { status: 503, statusText: 'Unavailable' }));
    http.match(r => r.method === 'GET' && r.url === '/api/ui/library/' + id1).forEach(r => r.flush(entryPayload()));
    await refreshing; TestBed.tick(); c.acknowledgeUnconfirmed();
    expect(c.cmd.uncertain()).toBe(true); expect(c.reconciliationLoaded()).toBe(false); http.expectNone(r => r.method === 'POST');
  });
});
