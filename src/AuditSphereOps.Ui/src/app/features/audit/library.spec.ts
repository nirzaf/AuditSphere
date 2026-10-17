import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AuditProgramLibrary, decodeProgramLibrary, decodeSectionPage } from './library';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const hash64 = 'c'.repeat(64);

describe('Audit Program Library Contracts', () => {
  it('decodes a valid program library payload', () => {
    const raw = {
      versions: [
        {
          programVersionId: id1,
          programCode: 'CORE-AUDIT',
          version: '2026.1',
          status: 'PUBLISHED',
          sourceHash: hash64,
          contentDigest: hash64,
          procedureCount: 150,
          sectionCount: 12,
          createdByUserId: id1,
          approvedByUserId: id2,
          publicationReviewReference: 'review-2026-01',
          createdAt: '2026-09-15T08:00:00Z',
          approvedAt: '2026-09-15T09:00:00Z',
        },
      ],
      selectedVersion: {
        programVersionId: id1,
        programCode: 'CORE-AUDIT',
        version: '2026.1',
        status: 'PUBLISHED',
        sourceHash: hash64,
        contentDigest: hash64,
        procedureCount: 150,
        sectionCount: 12,
        createdByUserId: id1,
        approvedByUserId: id2,
        publicationReviewReference: 'review-2026-01',
        createdAt: '2026-09-15T08:00:00Z',
        approvedAt: '2026-09-15T09:00:00Z',
      },
      canPrepare: false,
      canPublish: false,
      sections: [
        {
          sectionNumber: 1,
          sectionTitle: 'Planning and Risk Assessment',
          procedureCount: 15,
          assertions: ['Completeness', 'Accuracy'],
        },
      ],
    };

    const decoded = decodeProgramLibrary(raw, 'library');
    expect(decoded.versions.length).toBe(1);
    expect(decoded.versions[0].programCode).toBe('CORE-AUDIT');
    expect(decoded.selectedVersion?.version).toBe('2026.1');
    expect(decoded.sections[0].sectionNumber).toBe(1);
    expect(decoded.sections[0].assertions).toContain('Completeness');
  });

  it('decodes a valid section procedures page payload', () => {
    const raw = {
      sectionNumber: 1,
      sectionTitle: 'Planning and Risk Assessment',
      totalCount: 1,
      page: 1,
      pageSize: 50,
      items: [
        {
          procedureId: id2,
          sourceProcedureId: 'AWP-01',
          sectionNumber: 1,
          sectionTitle: 'Planning and Risk Assessment',
          ordinal: 1,
          sourceWording: 'Inquire with management regarding business risks and operational changes.',
          applicabilityCondition: 'Always applicable',
          expectedEvidence: 'Meeting minutes and notes of inquiry',
        },
      ],
    };

    const decoded = decodeSectionPage(raw, 'sectionPage');
    expect(decoded.sectionNumber).toBe(1);
    expect(decoded.items.length).toBe(1);
    expect(decoded.items[0].sourceProcedureId).toBe('AWP-01');
    expect(decoded.items[0].sourceWording).toContain('Inquire with management');
  });

  it('rejects invalid library payloads', () => {
    expect(() => decodeProgramLibrary(null, 'library')).toThrow();
    expect(() => decodeProgramLibrary({}, 'library')).toThrow();
    expect(() => decodeSectionPage(null, 'sectionPage')).toThrow();
  });
});


const summary = (id = id1, version = '2026.1') => ({ programVersionId: id, programCode: 'CORE-AUDIT', version, status: 'PUBLISHED', sourceHash: hash64,
  contentDigest: hash64, procedureCount: 2, sectionCount: 2, createdByUserId: id2, approvedByUserId: id1,
  publicationReviewReference: 'review-2026-01', createdAt: '2026-09-15T08:00:00Z', approvedAt: '2026-09-15T09:00:00Z' });
const library = (id = id1) => ({ canPrepare: false, canPublish: false, versions: [summary(), summary(id2, '2026.2')], selectedVersion: id === id1 ? summary() : summary(id2, '2026.2'),
  sections: [{ sectionNumber: 1, sectionTitle: 'Planning', procedureCount: 1, assertions: [] }, { sectionNumber: 2, sectionTitle: 'Evidence', procedureCount: 1, assertions: [] }] });
const procedurePage = (sectionNumber = 1, wording = 'Exact source procedure', count = 1) => ({ sectionNumber, sectionTitle: sectionNumber === 1 ? 'Planning' : 'Evidence',
  totalCount: count, page: 1, pageSize: 100, items: count ? [{ procedureId: id2, sourceProcedureId: 'AWP-01-01', sectionNumber,
    sectionTitle: sectionNumber === 1 ? 'Planning' : 'Evidence', ordinal: 1, sourceWording: wording, applicabilityCondition: 'Always applicable', expectedEvidence: 'Signed evidence' }] : [] });

describe('Audit library read lifecycle', () => {
  let fixture: ComponentFixture<AuditProgramLibrary>, page: AuditProgramLibrary, http: HttpTestingController;
  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [AuditProgramLibrary], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: id1, firmId: id1, generation: '0', staff: true });
    fixture = TestBed.createComponent(AuditProgramLibrary); page = fixture.componentInstance; http = TestBed.inject(HttpTestingController);
    TestBed.tick(); http.expectOne('/api/ui/audit/library').flush(library()); await fixture.whenStable();
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  it('clears populated metadata and procedures after failed library root refresh and retries successfully', async () => {
    const browse = page.open(id1, 1, '');
    http.expectOne('/api/ui/audit/library/' + id1 + '/sections/1').flush(procedurePage());
    await browse; await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Exact source procedure');
    expect(fixture.nativeElement.textContent).toContain(hash64);
    page.refresh(); TestBed.tick();
    http.expectOne('/api/ui/audit/library').flush({ message: 'Root unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.library()).toBeNull(); expect(page.section()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(hash64);
    expect(fixture.nativeElement.textContent).not.toContain('Exact source procedure');
    expect(fixture.nativeElement.querySelector('select[name="version"]')).toBeNull();
    page.refresh(); TestBed.tick(); http.expectOne('/api/ui/audit/library').flush(library()); await fixture.whenStable();
    expect(page.library()?.selectedVersion?.programVersionId).toBe(id1);
    expect(fixture.nativeElement.textContent).toContain(hash64);
    const retry = page.open(id1, 1, '');
    http.expectOne('/api/ui/audit/library/' + id1 + '/sections/1').flush(procedurePage());
    await retry; await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Exact source procedure');
  });
  it('submits search terms longer than 100 characters without truncation', async () => {
    const term = 'source wording '.repeat(10);
    const read = page.open(id1, 1, term);
    http.expectOne('/api/ui/audit/library/' + id1 + '/sections/1?search=' + encodeURIComponent(term.trim())).flush(procedurePage(1, '', 0));
    await read; expect(page.appliedSearch()).toBe(term.trim());
  });
  it('shows empty library and empty selected-version sections honestly', async () => {
    page.refresh(); TestBed.tick(); http.expectOne('/api/ui/audit/library').flush({ canPrepare: false, canPublish: false, versions: [], selectedVersion: null, sections: [] }); await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No library version');
    page.refresh(); TestBed.tick(); http.expectOne('/api/ui/audit/library').flush({ ...library(), sections: [] }); await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No sections in the selected version');
  });
  it('clears source rows during reads, renders no matches and clears the actual search term', async () => {
    const browse = page.open(id1, 1, ''); http.expectOne('/api/ui/audit/library/' + id1 + '/sections/1').flush(procedurePage()); await browse; await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Exact source procedure');
    expect(fixture.nativeElement.textContent).toContain('Signed evidence');
    page.search = 'missing'; const search = page.searchSection();
    expect(page.section()).toBeNull(); http.expectOne('/api/ui/audit/library/' + id1 + '/sections/1?search=missing').flush(procedurePage(1, '', 0)); await search; await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No procedures in this section match');
    const clear = page.clearSearch(); expect(page.search).toBe('');
    http.expectOne('/api/ui/audit/library/' + id1 + '/sections/1').flush(procedurePage()); await clear;
    expect(page.appliedSearch()).toBe(''); expect(page.section()?.totalCount).toBe(1);
  });
  for (const failure of [false, true]) {
    it(`discards A-B-A late search ${failure ? 'error' : 'success'} without changing latest busy state`, async () => {
      const first = page.open(id1, 1, 'A'), firstRequest = http.expectOne(r => r.urlWithParams.endsWith('?search=A'));
      const middle = page.open(id1, 1, 'B'), middleRequest = http.expectOne(r => r.urlWithParams.endsWith('?search=B'));
      const last = page.open(id1, 1, 'A'), lastRequest = http.expectOne(r => r.urlWithParams.endsWith('?search=A'));
      if (failure) firstRequest.flush({ message: 'obsolete denial' }, { status: 403, statusText: 'Forbidden' }); else firstRequest.flush(procedurePage(1, 'obsolete'));
      await first; expect(page.busy()).toBe(true); expect(page.sectionError()).toBe('');
      middleRequest.flush(procedurePage(1, 'middle')); await middle; expect(page.busy()).toBe(true);
      lastRequest.flush(procedurePage(1, 'latest')); await last; expect(page.section()?.items[0].sourceWording).toBe('latest'); expect(page.blocked()).toBe(false);
    });
    it(`drops section ${failure ? 'error' : 'success'} after version A-B-A selection`, async () => {
      const old = page.open(id1, 1, ''), oldRequest = http.expectOne(r => r.url.endsWith('/sections/1'));
      page.selectVersion(id2); TestBed.tick(); http.expectOne('/api/ui/audit/library?version=2026.2').flush(library(id2)); await Promise.resolve(); TestBed.tick();
      page.selectVersion(id1); TestBed.tick(); http.expectOne('/api/ui/audit/library?version=2026.1').flush(library()); TestBed.tick();
      if (failure) oldRequest.flush({}, { status: 500, statusText: 'Unavailable' }); else oldRequest.flush(procedurePage());
      await old; expect(page.section()).toBeNull(); expect(page.sectionError()).toBe(''); expect(page.library()?.selectedVersion?.programVersionId).toBe(id1);
    });
  }
  it('drops older section completion after selecting a different section', async () => {
    const one = page.open(id1, 1, ''), old = http.expectOne(r => r.url.endsWith('/sections/1'));
    const two = page.open(id1, 2, ''), current = http.expectOne(r => r.url.endsWith('/sections/2'));
    current.flush(procedurePage(2)); await two; old.flush(procedurePage(1)); await one;
    expect(page.section()?.sectionNumber).toBe(2);
  });
  it('hides the protected library after failed search and recovers only through a fresh library read', async () => {
    const search = page.open(id1, 1, 'denied'); http.expectOne(r => r.url.includes('/sections/1')).flush({ message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await search; await fixture.whenStable(); expect(page.library()).toBeNull(); expect(page.section()).toBeNull(); expect(page.blocked()).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain(hash64); expect(fixture.nativeElement.textContent).toContain('Access denied.');
    page.refresh(); expect(page.library()).toBeNull(); TestBed.tick(); http.expectOne('/api/ui/audit/library').flush(library()); await fixture.whenStable();
    expect(page.library()).not.toBeNull(); expect(page.sectionError()).toBe(''); expect(page.blocked()).toBe(false);
  });
  it('refresh fences pending reads even when the same version returns', async () => {
    const old = page.open(id1, 1, ''), pending = http.expectOne(r => r.url.endsWith('/sections/1'));
    page.refresh(); expect(page.section()).toBeNull(); TestBed.tick(); http.expectOne('/api/ui/audit/library').flush(library()); TestBed.tick();
    pending.flush(procedurePage()); await old; expect(page.section()).toBeNull(); expect(page.search).toBe('');
  });
  it('rejects mismatched section payloads and unavailable-version fallback', async () => {
    const loading = page.open(id1, 1, ''); http.expectOne(r => r.url.endsWith('/sections/1')).flush(procedurePage(2)); await loading;
    expect(page.blocked()).toBe(true); expect(page.section()).toBeNull();
    page.refresh(); TestBed.tick(); http.expectOne('/api/ui/audit/library').flush(library()); TestBed.tick();
    page.selectVersion(id2); TestBed.tick(); http.expectOne('/api/ui/audit/library?version=2026.2').flush(library()); await fixture.whenStable();
    expect(page.library()).toBeNull(); expect(page.lib.error()).toContain('Unsupported response');
  });
  it('fences session invalidation and destroyed lifetime without accepting late responses', async () => {
    const old = page.open(id1, 1, ''), pending = http.expectOne(r => r.url.endsWith('/sections/1'));
    TestBed.inject(SessionService).clear(); TestBed.tick(); pending.flush(procedurePage()); await old;
    expect(page.library()).toBeNull(); expect(page.section()).toBeNull();
    TestBed.inject(SessionService).current.set({ userId: id1, firmId: id1, generation: '1', staff: true }); TestBed.tick();
    http.expectOne('/api/ui/audit/library').flush(library()); TestBed.tick();
    const later = page.open(id1, 1, ''), lateRequest = http.expectOne(r => r.url.endsWith('/sections/1')); fixture.destroy();
    lateRequest.flush(procedurePage()); await later; expect(page.section()).toBeNull();
  });
  it('does not dispatch arbitrary version or section requests', async () => {
    page.selectVersion('unknown'); await page.open(id2, 1, ''); await page.open(id1, 20, ''); http.expectNone(r => r.url.includes('/sections/'));
  });
});
