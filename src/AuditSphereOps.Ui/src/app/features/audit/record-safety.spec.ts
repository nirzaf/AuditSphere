import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { FindingRecord, PopulationRecord } from './records';
import { findingTestPreview, findingTestReceipt, findingTestState } from './finding-command-test-data';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const finding = (n = 1) => ({ id: id(n), engagementId: id(20), findingType: 'Synthetic deficiency', impactDescription: 'Synthetic impact', monetaryAmount: '10.00', corrected: true,
  managementResponse: `Response ${n}`, status: 'EVALUATED', createdAt: '2026-10-16T00:00:00Z', professionalWorkBlocked: false, letterDesignatedAt: null, letterRecommendation: null, revision: '1', canRecordResponse: true, canDesignate: true });
const population = (n = 1) => ({ id: id(n), engagementId: id(20), purpose: 'Synthetic population', assertion: 'Existence', sourceReceiptReference: 'Synthetic receipt', extractionParameters: 'All rows', rowCount: 1,
  monetaryControlTotal: '10.00', currency: 'QAR', exclusions: null, status: 'PENDING_APPROVAL', createdAt: '2026-10-16T00:00:00Z', selections: [], evidence: [] });

describe('audit record route, draft and command safety', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    params = new BehaviorSubject(convertToParamMap({ id: id(1) }));
    TestBed.configureTestingModule({ imports: [FindingRecord, PopulationRecord], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
  });
  afterEach(() => { try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); sessionStorage.clear(); } });
  async function settle() { for (let i = 0; i < 8; ++i) await Promise.resolve(); TestBed.tick(); }
  const url = (n = 1) => `/api/ui/findings/${id(n)}`;
  let lastPreview: ReturnType<typeof findingTestPreview>;
  async function dispatch(page: FindingRecord) {
    await settle(); http.expectOne(url() + '/commands/state').flush(findingTestState()); await settle();
    const previewRequest = http.expectOne(url() + '/commands/preview');
    lastPreview = findingTestPreview(previewRequest.request.body); previewRequest.flush(lastPreview); await settle();
    page.flow.reviewed.set(true); void page.flow.execute();
    return http.expectOne(url() + '/commands/actions');
  }
  function openFinding() {
    const fixture = TestBed.createComponent(FindingRecord); fixture.detectChanges(); TestBed.tick();
    http.expectOne(url()).flush(finding()); TestBed.tick(); return fixture;
  }
  it.each([8001, 20000])('previews a %i-character response through the complete supported editor range', async length => {
    const fixture = openFinding(), page = fixture.componentInstance;
    const editor = fixture.nativeElement.querySelector('textarea[name="resp"]') as HTMLTextAreaElement;
    expect(editor.maxLength).toBe(20000);
    page.response = 'r'.repeat(length); page.respond(id(1)); await settle();
    http.expectOne(url() + '/commands/state').flush(findingTestState()); await settle();
    const request = http.expectOne(url() + '/commands/preview');
    expect(request.request.body.fields.response.managementResponse).toHaveLength(length);
    request.flush(findingTestPreview(request.request.body)); await settle();
    expect(page.flow.preview()?.canonicalInput.response?.managementResponse).toHaveLength(length);
    expect(page.flow.reviewed()).toBe(false); http.expectNone(r => r.url.endsWith('/commands/actions'));
  });

  it('initializes recorded response and correction even without a letter recommendation', () => {
    const fixture = openFinding();
    expect(fixture.componentInstance.response).toBe('Response 1');
    expect(fixture.componentInstance.corrected).toBe(true);
    expect(fixture.componentInstance.designatedFlag).toBe(false);
  });
  it('uses server capabilities and refuses programmatic unavailable actions', () => {
    const fixture = TestBed.createComponent(FindingRecord); fixture.detectChanges(); TestBed.tick();
    http.expectOne(url()).flush({ ...finding(), canRecordResponse: false, canDesignate: false }); TestBed.tick();
    const page = fixture.componentInstance; page.response = 'Draft'; page.designation = 'Draft recommendation'; page.designatedFlag = true;
    page.respond(id(1)); page.designate(id(1), true); http.expectNone(r => r.method === 'POST' || r.url.endsWith('/commands/state'));
    expect(fixture.nativeElement.textContent).not.toContain('Preview management response');
    expect(fixture.nativeElement.textContent).not.toContain('Preview letter designation');
  });
  it.each(['response', 'corrected', 'designation', 'designatedFlag'] as const)('clears prior assent when %s changes A-B-A', (field) => {
    const fixture = openFinding(), page = fixture.componentInstance;
    page.flow.reviewed.set(true);
    if (field === 'response') { page.response = 'B'; page.response = 'Response 1'; }
    else if (field === 'corrected') { page.corrected = false; page.corrected = true; }
    else if (field === 'designation') { page.designation = 'B'; page.designation = ''; }
    else { page.designatedFlag = true; page.designatedFlag = false; }
    expect(page.flow.reviewed()).toBe(false); expect(page.flow.preview()).toBeNull(); http.expectNone(r => r.method === 'POST');
  });
  it('clears private drafts and prevents old-record commands on route change', () => {
    const fixture = openFinding(), page = fixture.componentInstance;
    page.response = 'Unsaved private response'; page.designation = 'Unsaved recommendation';
    params.next(convertToParamMap({ id: id(2) })); TestBed.tick();
    expect(page.response).toBe(''); expect(page.designation).toBe(''); expect(page.corrected).toBe(false);
    page.respond(id(1)); page.designate(id(1), true);
    http.expectNone(r => r.method === 'POST');
    http.expectOne(url(2)).flush(finding(2)); TestBed.tick(); expect(page.response).toBe('Response 2');
  });
  it('rejects a well-shaped finding response with the wrong route identity', () => {
    const fixture = TestBed.createComponent(FindingRecord); fixture.detectChanges(); TestBed.tick();
    http.expectOne(url()).flush(finding(2)); TestBed.tick();
    expect(fixture.componentInstance.rec.data()).toBeNull(); expect(fixture.nativeElement.textContent).not.toContain('Response 2');
  });
  it('cancels previous A and B reads before returning to A', () => {
    const fixture = TestBed.createComponent(FindingRecord); fixture.detectChanges(); TestBed.tick(); const first = http.expectOne(url());
    params.next(convertToParamMap({ id: id(2) })); TestBed.tick(); const second = http.expectOne(url(2));
    params.next(convertToParamMap({ id: id(1) })); TestBed.tick(); const last = http.expectOne(url());
    expect(first.cancelled).toBe(true); expect(second.cancelled).toBe(true); last.flush(finding()); TestBed.tick();
    expect(fixture.componentInstance.response).toBe('Response 1');
  });
  it('clears record and response after denied refresh', () => {
    const fixture = openFinding(); fixture.componentInstance.response = 'Private draft'; fixture.componentInstance.rec.reload(); TestBed.tick();
    http.expectOne(url()).flush({}, {status:403,statusText:'Forbidden'}); TestBed.tick();
    expect(fixture.componentInstance.response).toBe(''); expect(fixture.componentInstance.rec.data()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Private draft');
  });
  it('ignores late successful command completion after moving to another record', async () => {
    const fixture = openFinding(), page = fixture.componentInstance; page.response = 'Synthetic update'; page.respond(id(1));
    const command = await dispatch(page);
    params.next(convertToParamMap({ id: id(2) })); TestBed.tick(); http.expectOne(url(2)).flush(finding(2)); TestBed.tick();
    command.flush(findingTestReceipt(lastPreview)); await settle();
    expect(page.response).toBe('Response 2'); expect(page.flow.message()).toBe(''); http.expectNone(url(2));
  });
  it('retains uncertainty for the original finding without blocking unrelated records', async () => {
    const fixture = openFinding(), page = fixture.componentInstance; page.respond(id(1)); const command = await dispatch(page);
    params.next(convertToParamMap({ id:id(2) })); TestBed.tick(); http.expectOne(url(2)).flush(finding(2)); TestBed.tick();
    command.flush({}, {status:500,statusText:'Unknown'}); await settle();
    expect(page.flow.uncertain()).toBe(false);
    params.next(convertToParamMap({id:id(1)})); TestBed.tick(); http.expectOne(url()).flush(finding()); TestBed.tick();
    expect(page.flow.uncertain()).toBe(true); page.respond(id(1)); http.expectNone(r => r.method === 'POST');
  });
  it('preserves both dirty editors after refused same-record submission and authorized refresh', async () => {
    const fixture = openFinding(), page = fixture.componentInstance;
    page.response = 'Correct this response'; page.designation = 'Retain unrelated recommendation'; page.designatedFlag = true;
    page.respond(id(1)); (await dispatch(page)).flush({message:'Refused'}, {status:400,statusText:'Refused'});
    await settle(); page.rec.reload(); http.expectOne(url()).flush(finding()); TestBed.tick();
    expect(page.response).toBe('Correct this response'); expect(page.designation).toBe('Retain unrelated recommendation'); expect(page.designatedFlag).toBe(true);
  });
  it('refreshes the successful response without discarding an unsaved recommendation', async () => {
    const fixture = openFinding(), page = fixture.componentInstance;
    page.response = 'Saved response'; page.designation = 'Unsaved recommendation'; page.respond(id(1)); (await dispatch(page)).flush(findingTestReceipt(lastPreview));
    await settle(); http.expectOne(url()).flush({...finding(),managementResponse:'Saved response'}); TestBed.tick();
    expect(page.response).toBe('Saved response'); expect(page.designation).toBe('Unsaved recommendation');
  });
  it.each(['response', 'designation'] as const)('keeps %s edits made during a successful command', async (editor) => {
    const fixture = openFinding(), page = fixture.componentInstance;
    if (editor === 'response') { page.response = 'Submitted A'; page.respond(id(1)); }
    else { page.designation = 'Submitted A'; page.designatedFlag = true; page.designate(id(1), true); }
    const command = await dispatch(page);
    if (editor === 'response') { page.response = 'Edited B'; page.corrected = false; }
    else { page.designation = 'Edited B'; page.designatedFlag = false; }
    command.flush(findingTestReceipt(lastPreview)); await settle();
    expect(page.flow.receipt()).toBeNull(); expect(page.flow.uncertain()).toBe(true);
    page.rec.reload(); http.expectOne(url()).flush({...finding(),managementResponse:'Submitted A',letterRecommendation:'Submitted A',letterDesignatedAt:'2026-10-16T00:00:00Z'}); TestBed.tick();
    expect(editor === 'response' ? page.response : page.designation).toBe('Edited B');
    expect(editor === 'response' ? page.corrected : page.designatedFlag).toBe(false);
  });
  it('preserves dirty editors across an equivalent authenticated session refresh', () => {
    const fixture = openFinding(), page = fixture.componentInstance; page.response = 'Private draft';
    const session = TestBed.inject(SessionService); session.current.set({...session.current()!}); TestBed.tick();
    http.expectOne(url()).flush(finding()); TestBed.tick(); expect(page.response).toBe('Private draft');
  });
  it('does not reload a destroyed finding after a late command result', async () => {
    const fixture = openFinding(), page = fixture.componentInstance; page.respond(id(1)); const command = await dispatch(page);
    fixture.destroy(); command.flush(findingTestReceipt(lastPreview)); await settle(); http.expectNone(url());
  });
  it('clears protected drafts on session loss', () => {
    const fixture = openFinding(); fixture.componentInstance.response = 'Private draft';
    TestBed.inject(SessionService).current.set(null); TestBed.tick();
    expect(fixture.componentInstance.response).toBe(''); expect(fixture.componentInstance.rec.data()).toBeNull(); http.expectNone(r => r.method === 'POST');
  });
  it('rejects mismatched population identity and supports explicit refresh', () => {
    const fixture = TestBed.createComponent(PopulationRecord); fixture.detectChanges(); TestBed.tick();
    const endpoint = `/api/ui/audit/populations/${id(1)}`; http.expectOne(endpoint).flush(population(2)); TestBed.tick();
    expect(fixture.componentInstance.rec.data()).toBeNull(); fixture.componentInstance.rec.reload();
    http.expectOne(endpoint).flush(population()); TestBed.tick(); expect(fixture.componentInstance.rec.data()?.id).toBe(id(1));
    expect(fixture.nativeElement.textContent).toContain('No selection has been recorded');
    expect(fixture.nativeElement.textContent).toContain('No source receipts');
  });
});
