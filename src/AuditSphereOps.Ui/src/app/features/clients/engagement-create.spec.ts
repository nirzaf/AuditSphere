import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { EngagementCreate } from './engagement-create';
import {
  engagementFields,
  decodeEngagementCreationLookup,
  decodeEngagementCreationPreview,
  decodeEngagementCreationState,
  validEngagement,
  decodeAnnualPreparation,
} from './engagement-create-contracts';

const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222',
  third = '33333333-3333-4333-8333-333333333333';
const basis = 'a'.repeat(64),
  hash = 'b'.repeat(64),
  url = '/api/ui/clients/' + id + '/engagement-creation';
const state = {
  clientId: id,
  clientName: 'Synthetic client',
  clientGeneration: '9007199254740993',
  reviewBasis: basis,
};
const fields = {
  serviceRoute: 'AccountingOnly',
  serviceProfile: 'SYNTHETIC',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
};
describe('Reviewed client engagement creation contracts', () => {
  it('refuses restricted, duplicated or wrong-period annual mapping references', () => {
    const reference = { id: other, version: '9007199254740993', taxonomyVersion: 'synthetic', periodStart: fields.periodStart, periodEnd: fields.periodEnd };
    const annual = { clientId: id, sourceEngagementId: other, sourcePeriodStart: fields.periodStart,
      sourcePeriodEnd: fields.periodEnd, fields: { ...fields, annualSourceEngagementId: other },
      copiedReferences: [], requiresFreshWork: [], contactSuggestions: [], historicalMappingState: 'AVAILABLE',
      historicalMappings: [reference], taskTemplateSuggestions: [] };
    expect(decodeAnnualPreparation(annual).historicalMappings[0].version).toBe('9007199254740993');
    expect(() => decodeAnnualPreparation({ ...annual, historicalMappingState: 'RESTRICTED' })).toThrow();
    expect(() => decodeAnnualPreparation({ ...annual, historicalMappings: [reference, reference] })).toThrow();
    expect(() => decodeAnnualPreparation({ ...annual, historicalMappings: [{ ...reference, periodEnd: '2025-12-31' }] })).toThrow();
  });
  it('decodes bounded milestone suggestions and defaults legacy or restricted references safely', () => {
    const annual = { clientId: id, sourceEngagementId: other, sourcePeriodStart: fields.periodStart,
      sourcePeriodEnd: fields.periodEnd, fields: { ...fields, annualSourceEngagementId: other },
      copiedReferences: [], requiresFreshWork: [], contactSuggestions: [], historicalMappingState: 'AVAILABLE',
      historicalMappings: [], taskTemplateSuggestions: [] };
    const fieldwork = { kind: 'FIELDWORK_START', sourceDate: '2026-01-07', offsetDaysFromSourcePeriodEnd: 7,
      suggestedDate: '2027-01-07', sourcePlanRevision: 2,
      notice: 'Suggested internal target only. Confirm current-year commitments in the planning workspace.' };
    const current = decodeAnnualPreparation({ ...annual, milestoneSuggestions: [fieldwork], milestoneSuggestionState: 'AVAILABLE' });
    expect(current.milestoneSuggestions).toEqual([fieldwork]);
    expect(decodeAnnualPreparation(annual).milestoneSuggestionState).toBe('MISSING');
    expect(decodeAnnualPreparation(annual).milestoneSuggestions).toEqual([]);
    const restricted = decodeAnnualPreparation({ ...annual, milestoneSuggestions: [], milestoneSuggestionState: 'RESTRICTED' });
    expect(restricted.milestoneSuggestionState).toBe('RESTRICTED');
    expect(() => decodeAnnualPreparation({ ...annual, milestoneSuggestions: [fieldwork], milestoneSuggestionState: 'RESTRICTED' })).toThrow();
    expect(() => decodeAnnualPreparation({ ...annual, milestoneSuggestions: [fieldwork], milestoneSuggestionState: 'FUTURE' })).toThrow();
    expect(() => decodeAnnualPreparation({ ...annual, milestoneSuggestions: Array(5).fill(fieldwork), milestoneSuggestionState: 'AVAILABLE' })).toThrow();
    expect(() => decodeAnnualPreparation({ ...annual, milestoneSuggestions: [{ ...fieldwork, sourcePlanRevision: 0 }], milestoneSuggestionState: 'AVAILABLE' })).toThrow();
  });
  it('allowlists bounded fields and exact generations without retaining assent', () => {
    expect(engagementFields({ ...fields, annualTemplateRevisionId: other })).toBeNull();
    expect(engagementFields({ ...fields, reviewed: true })).toBeNull();
    expect(engagementFields({ ...fields, serviceProfile: 'x'.repeat(101) })).toBeNull();
    expect(engagementFields({ ...fields, serviceProfile: 'Finance\n' })).toBeNull();
    expect(validEngagement({ ...fields, periodEnd: '2025-12-31' })).toBe(false);
    expect(decodeEngagementCreationState(state, '').clientGeneration).toBe('9007199254740993');
    expect(() =>
      decodeEngagementCreationState({ ...state, clientGeneration: 9007199254740993 }, ''),
    ).toThrow();
    expect(() => decodeEngagementCreationLookup({ found: true, receipt: null })).toThrow();
    expect(() =>
      decodeEngagementCreationState(
        { ...state, clientId: '00000000-0000-0000-0000-000000000000' },
        '',
      ),
    ).toThrow();
  });
});
describe('Native engagement creation review, tab recovery and ownership', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let queries: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear();
    ids = new BehaviorSubject(convertToParamMap({ id }));
    queries = new BehaviorSubject(convertToParamMap({ contactPage: '2', contactPageSize: '25' }));
    TestBed.configureTestingModule({
      imports: [EngagementCreate],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: ids,
            queryParamMap: queries,
            snapshot: {
              paramMap: ids.value,
              queryParamMap: queries.value,
            },
          },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(EngagementCreate);
    f.detectChanges();
    TestBed.tick();
    return f;
  }
  function read(value = state) {
    const requests = TestBed.inject(HttpTestingController)
      .match((r) => r.method === 'GET' && !r.url.includes('/receipts/') && !r.url.includes('/annual-engagement/'))
      .filter((r) => !r.cancelled);
    expect(requests).toHaveLength(1);
    requests[0].flush(value);
    TestBed.tick();
  }
  function annualPreparation(sourceId: string) {
    return {
      clientId: id,
      sourceEngagementId: sourceId,
      sourcePeriodStart: '2026-01-01',
      sourcePeriodEnd: '2026-12-31',
      fields: { ...fields, periodStart: '2027-01-01', periodEnd: '2027-12-31', annualSourceEngagementId: sourceId },
      copiedReferences: [],
      requiresFreshWork: [],
      contactSuggestions: [],
      historicalMappingState: 'AVAILABLE',
      historicalMappings: [],
      taskTemplateSuggestions: [],
    };
  }
  async function preview(c: EngagementCreate, existingEngagementId: string | null = null) {
    c.model.set({ ...fields });
    TestBed.tick();
    const pending = c.prepare();
    const r = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    const body = r.request.body;
    r.flush({
      value: {
        clientId: id,
        requestId: body.requestId,
        reviewBasis: basis,
        requestHash: hash,
        fields,
        existingEngagementId,
      },
    });
    await pending;
    TestBed.tick();
    return body;
  }
  function receipt(requestId: string, receiptFields = fields) {
    return {
      id: other,
      clientId: id,
      engagementId: other,
      actorId: id,
      requestId,
      requestHash: hash,
      reviewBasis: basis,
      clientGeneration: '9007199254740993',
      engagementGeneration: '1',
      fields: receiptFields,
      createdAt: '2026-10-03T12:30:00Z',
    };
  }
  it('copies a bounded annual reference only after explicit selection and keeps approvals separate', async () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    const f = open(); read(); const c = f.componentInstance;
    const proposed = { ...fields, periodStart: '2027-01-01', periodEnd: '2027-12-31', annualSourceEngagementId: other, annualTemplateRevisionId: id };
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + other).flush({
      clientId: id, sourceEngagementId: other, sourcePeriodStart: '2026-01-01', sourcePeriodEnd: '2026-12-31',
      fields: proposed, copiedReferences: ['Service route', 'Prior period reference'], requiresFreshWork: ['Partner continuance', 'Materiality'],
      contactSuggestions: [{ id: other, name: 'Synthetic annual liaison', role: 'Liaison', primary: true }],
      historicalMappingState: 'RESTRICTED', historicalMappings: [],
      taskTemplateSuggestions: [{ templateId: other, revisionId: id, version: 1, name: 'Synthetic annual plan', contentDigest: hash }],
    });
    TestBed.tick(); expect(c.model().annualSourceEngagementId).toBeUndefined();
    c.useAnnualSuggestion(); f.detectChanges(); TestBed.tick();
    expect(c.model()).toEqual(proposed); expect(c.valid()).toBe(true);
    c.chooseAnnualTemplate('unavailable'); expect(c.model()).toEqual(proposed);
    c.chooseAnnualTemplate(''); expect(c.model().annualTemplateRevisionId).toBeUndefined();
    c.chooseAnnualTemplate(id); expect(c.model()).toEqual(proposed);
    f.detectChanges(); TestBed.tick();
    expect(f.nativeElement.textContent).toContain('Requires fresh current-period work');
    expect(f.nativeElement.textContent).toContain('No prior milestone plan is available for reference.');
    expect(f.nativeElement.textContent).toContain('Synthetic annual liaison');
    expect(f.nativeElement.textContent).toContain('Mapping references require accounting read access');
    const pending = c.prepare(); const request = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    expect(request.request.body.fields.annualSourceEngagementId).toBe(other);
    request.flush({ value: { clientId: id, requestId: request.request.body.requestId, reviewBasis: basis,
      requestHash: hash, fields: proposed, existingEngagementId: null } });
    await pending; TestBed.tick(); expect(c.preview()?.fields.annualSourceEngagementId).toBe(other);
    expect(c.reviewed()).toBe(false);
  });
  it('fences a late annual preview and unflushed confirmation across source changes without discarding fields', async () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    const f = open(); read(); const c = f.componentInstance;
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + other)
      .flush(annualPreparation(other));
    TestBed.tick();
    const firstFields = annualPreparation(other).fields;
    c.useAnnualSuggestion(); TestBed.tick();

    const latePreview = c.prepare();
    const firstPreviewRequest = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    queries.next(convertToParamMap({ annualSourceEngagementId: third }));
    firstPreviewRequest.flush({ value: { clientId: id, requestId: firstPreviewRequest.request.body.requestId,
      reviewBasis: basis, requestHash: hash, fields: firstFields, existingEngagementId: null } });
    await latePreview;
    expect(c.preview()).toBeNull();
    expect(c.model()).toEqual(firstFields);

    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + third)
      .flush(annualPreparation(third));
    TestBed.tick();
    expect(c.model()).toEqual(firstFields);
    expect(c.valid()).toBe(false);
    expect(c.discardDraft()).toBe(true);
    c.useAnnualSuggestion(); TestBed.tick();
    const currentFields = annualPreparation(third).fields;
    const currentPreview = c.prepare();
    const currentPreviewRequest = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    currentPreviewRequest.flush({ value: { clientId: id, requestId: currentPreviewRequest.request.body.requestId,
      reviewBasis: basis, requestHash: hash, fields: currentFields, existingEngagementId: null } });
    await currentPreview; TestBed.tick();
    c.reviewed.set(true);

    // The computed route context changes synchronously; do not wait for effects before enforcing it.
    queries.next(convertToParamMap({ contactPage: '2' }));
    expect(c.valid()).toBe(false);
    const staleConfirmation = c.execute();
    const posted = TestBed.inject(HttpTestingController).match((request) => request.method === 'POST' && request.url === url);
    if (posted.length) posted[0].flush({ code: 'generation.stale' }, { status: 409, statusText: 'Conflict' });
    await staleConfirmation;
    expect(posted).toHaveLength(0);
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
    expect(c.model()).toEqual(currentFields);
    f.detectChanges();
    const returnToAnnual = Array.from(
      (f.nativeElement as HTMLElement).querySelectorAll('a') as NodeListOf<HTMLAnchorElement>,
    ).find((link) => link.textContent?.trim() === 'Return to the retained annual source to review again');
    expect(returnToAnnual?.getAttribute('href'))
      .toBe('/app/clients/' + id + '/engagements/new?annualSourceEngagementId=' + third);
  });
  it('invalidates annual preview and confirmation when the source changes away and back before effects flush', async () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    const f = open(); read(); const c = f.componentInstance;
    const proposed = annualPreparation(other).fields;
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + other)
      .flush(annualPreparation(other));
    TestBed.tick();
    c.useAnnualSuggestion(); TestBed.tick();

    const acceptedPreview = c.prepare();
    const acceptedRequest = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    acceptedRequest.flush({ value: { clientId: id, requestId: acceptedRequest.request.body.requestId,
      reviewBasis: basis, requestHash: hash, fields: proposed, existingEngagementId: null } });
    await acceptedPreview; TestBed.tick();
    c.reviewed.set(true);

    queries.next(convertToParamMap({ annualSourceEngagementId: third }));
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    expect(c.canConfirmCreation()).toBe(false);
    const unflushedConfirmation = c.execute();
    const posted = TestBed.inject(HttpTestingController)
      .match((request) => request.method === 'POST' && request.url === url);
    expect(posted).toHaveLength(0);
    await unflushedConfirmation;
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
    expect(c.model()).toEqual(proposed);

    const latePreview = c.prepare();
    const lateRequest = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    queries.next(convertToParamMap({ annualSourceEngagementId: third }));
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    lateRequest.flush({ value: { clientId: id, requestId: lateRequest.request.body.requestId,
      reviewBasis: basis, requestHash: hash, fields: proposed, existingEngagementId: null } });
    await latePreview; TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
    expect(c.model()).toEqual(proposed);
  });
  it('keeps an in-flight annual creation recoverable after query-context change', async () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    const f = open(); read(); const c = f.componentInstance;
    const proposed = annualPreparation(other).fields;
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + other)
      .flush(annualPreparation(other));
    TestBed.tick();
    c.useAnnualSuggestion(); TestBed.tick();
    const pendingPreview = c.prepare();
    const previewRequest = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    previewRequest.flush({ value: { clientId: id, requestId: previewRequest.request.body.requestId,
      reviewBasis: basis, requestHash: hash, fields: proposed, existingEngagementId: null } });
    await pendingPreview; TestBed.tick();
    c.reviewed.set(true);

    const inFlightCreation = c.execute();
    const createRequest = TestBed.inject(HttpTestingController).expectOne(url);
    const requestId = createRequest.request.body.requestId as string;
    queries.next(convertToParamMap({ contactPage: '2' }));
    TestBed.tick();
    expect(c.model()).toEqual(proposed);
    expect(c.pending()?.requestId).toBe(requestId);
    expect(c.uncertain()).toBe(true);
    const storedValues = Array.from({ length: sessionStorage.length }, (_, index) =>
      sessionStorage.getItem(sessionStorage.key(index) ?? ''),
    ).join('\n');
    expect(storedValues).toContain(requestId);

    createRequest.flush({ value: receipt(requestId, proposed) });
    await inFlightCreation; TestBed.tick();
    expect(c.receipt()).toBeNull();
    expect(c.pending()?.requestId).toBe(requestId);
    expect(c.uncertain()).toBe(true);

    const recovery = c.reconcile();
    TestBed.inject(HttpTestingController).expectOne(url + '/receipts/' + requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: receipt(requestId, proposed) });
    await recovery; TestBed.tick();
    expect(c.receipt()?.fields).toEqual(proposed);
    expect(c.pending()?.requestId).toBe(requestId);
  });
  it('renders read-only renewal milestones and links only to the created engagement', async () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    const f = open(); read(); const c = f.componentInstance;
    const createdEngagementId = '33333333-3333-4333-8333-333333333333';
    const proposed = { ...fields, periodStart: '2027-01-01', periodEnd: '2027-12-31',
      annualSourceEngagementId: other, annualTemplateRevisionId: id };
    const suggestionRows = [
      { kind: 'FIELDWORK_START', sourceDate: '2026-01-07', offsetDaysFromSourcePeriodEnd: 7,
        suggestedDate: '2027-01-07', sourcePlanRevision: 2,
        notice: 'Suggested internal target only. Confirm current-year commitments in the planning workspace.' },
      { kind: 'DRAFT_REPORT', sourceDate: '2026-02-15', offsetDaysFromSourcePeriodEnd: 46,
        suggestedDate: '2027-02-15', sourcePlanRevision: 2,
        notice: 'Suggested internal target only. Confirm current-year commitments in the planning workspace.' },
      { kind: 'FINAL_REPORT', sourceDate: '2026-03-15', offsetDaysFromSourcePeriodEnd: 74,
        suggestedDate: '2027-03-15', sourcePlanRevision: 2,
        notice: 'Suggested internal target only. Confirm current-year commitments in the planning workspace.' },
      { kind: 'STATUTORY_CUTOFF', sourceDate: '2026-04-30', offsetDaysFromSourcePeriodEnd: 120,
        suggestedDate: null, sourcePlanRevision: 2,
        notice: 'Enter a separately verified current-year statutory cutoff; the previous deadline is not reused.' },
    ];
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + other).flush({
      clientId: id, sourceEngagementId: other, sourcePeriodStart: '2026-01-01', sourcePeriodEnd: '2026-12-31',
      fields: proposed, copiedReferences: [], requiresFreshWork: ['Partner continuance'], contactSuggestions: [],
      historicalMappingState: 'AVAILABLE', historicalMappings: [], taskTemplateSuggestions: [
        { templateId: other, revisionId: id, version: 1, name: 'Synthetic annual plan', contentDigest: hash },
      ], milestoneSuggestions: suggestionRows, milestoneSuggestionState: 'AVAILABLE',
    });
    TestBed.tick(); f.detectChanges();
    const annual = f.nativeElement.querySelector('[aria-label="Annual engagement preparation"]') as HTMLElement;
    expect(annual.querySelector('[aria-label="Suggested internal milestones"]')).not.toBeNull();
    expect(annual.textContent).toContain('2026-01-07');
    expect(annual.textContent).toContain('2027-01-07');
    expect(annual.textContent).toContain('Source plan revision');
    expect(annual.querySelector('tbody tr td:nth-of-type(3)')?.textContent?.trim()).toBe('2');
    expect(annual.textContent).toContain('Requires current-year input');
    expect(annual.textContent).toContain('These are suggestions, not a saved plan. Creating an engagement does not schedule, activate or approve it.');

    c.useAnnualSuggestion(); f.detectChanges(); TestBed.tick();
    expect(c.model()).toEqual(proposed);
    const pendingPreview = c.prepare();
    const previewRequest = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    expect(previewRequest.request.body.fields).toEqual(proposed);
    expect(JSON.stringify(previewRequest.request.body).toLowerCase()).not.toContain('milestone');
    expect(JSON.stringify(previewRequest.request.body).toLowerCase()).not.toContain('approval');
    previewRequest.flush({ value: { clientId: id, requestId: previewRequest.request.body.requestId,
      reviewBasis: basis, requestHash: hash, fields: proposed, existingEngagementId: null } });
    await pendingPreview; TestBed.tick();
    c.reviewed.set(true); TestBed.tick();
    const pendingCreate = c.execute();
    const createRequest = TestBed.inject(HttpTestingController).expectOne(url);
    expect(createRequest.request.body.fields).toEqual(proposed);
    expect(JSON.stringify(createRequest.request.body).toLowerCase()).not.toContain('milestone');
    expect(JSON.stringify(createRequest.request.body).toLowerCase()).not.toContain('approval');
    createRequest.flush({ value: {
      id: '44444444-4444-4444-8444-444444444444', clientId: id, engagementId: createdEngagementId,
      actorId: id, requestId: createRequest.request.body.requestId, requestHash: hash, reviewBasis: basis,
      clientGeneration: '9007199254740993', engagementGeneration: '1', fields: proposed,
      createdAt: '2026-10-03T12:30:00Z',
    } });
    await pendingCreate; TestBed.tick();
    read(); f.detectChanges();
    const milestoneLink = f.nativeElement.querySelector('a[aria-label="Review current-year milestone plan"]') as HTMLAnchorElement | null;
    const anchorCandidates = Array.from((f.nativeElement as HTMLElement).querySelectorAll('a') as NodeListOf<HTMLAnchorElement>);
    const link = milestoneLink ?? anchorCandidates
      .find((candidate) => candidate.textContent?.trim() === 'Review current-year milestone plan');
    expect(link?.getAttribute('href')).toBe('/app/engagements/' + createdEngagementId + '/audit-plan');
    expect(link?.getAttribute('href')).not.toContain(other);
  });
  it('hides all prior milestone dates when the annual reference is restricted', () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: other }));
    const f = open(); read();
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/' + id + '/annual-engagement/' + other).flush({
      clientId: id, sourceEngagementId: other, sourcePeriodStart: '2026-01-01', sourcePeriodEnd: '2026-12-31',
      fields: { ...fields, periodStart: '2027-01-01', periodEnd: '2027-12-31', annualSourceEngagementId: other },
      copiedReferences: [], requiresFreshWork: [], contactSuggestions: [], historicalMappingState: 'RESTRICTED',
      historicalMappings: [], taskTemplateSuggestions: [], milestoneSuggestions: [], milestoneSuggestionState: 'RESTRICTED',
    });
    TestBed.tick(); f.detectChanges();
    const annual = f.nativeElement.querySelector('[aria-label="Annual engagement preparation"]') as HTMLElement;
    expect(annual.textContent).toContain('Source milestone references require planning access.');
    expect(annual.querySelector('[aria-label="Suggested internal milestones"]')).toBeNull();
    expect(annual.textContent).not.toContain('2026-08-21');
  });
  it('does not silently turn a malformed annual source into manual creation', () => {
    queries.next(convertToParamMap({ annualSourceEngagementId: 'invalid' }));
    const f = open(); read(); const c = f.componentInstance; c.model.set({ ...fields }); TestBed.tick();
    expect(c.valid()).toBe(false);
    TestBed.inject(HttpTestingController).expectNone((r) => r.url.includes('/annual-engagement/'));
  });
  it('shows exact blocked-shell intent, requires assent and invalidates review on edit', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    expect(c.returnParams.contactPage).toBe(2);
    expect(c.returnParams.contactPageSize).toBe(25);
    expect(
      f.nativeElement.querySelector('a[href*="/app/clients/"]').getAttribute('href'),
    ).toContain('contactPage=2');
    c.model.set({ ...fields, periodStart: 'bad' });
    TestBed.tick();
    await c.prepare();
    expect(c.preview()).toBeNull();
    await preview(c);
    expect(f.nativeElement.textContent).toContain('SYNTHETIC');
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    c.reviewed.set(true);
    c.model.update((v) => ({ ...v, serviceProfile: 'Changed' }));
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
  });
  it('shows and links an existing matching shell without offering duplicate creation', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c, other);
    expect(c.message()).toContain('already exists');
    expect(f.nativeElement.textContent).toContain('Inspect the existing shell');
    const link = f.nativeElement.querySelector('a[href="/app/engagements/' + other + '"]');
    expect(link?.textContent.trim()).toBe('Inspect existing engagement');
    c.reviewed.set(true);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    expect(f.nativeElement.textContent).not.toContain('Confirm blocked creation');
    expect(
      decodeEngagementCreationPreview({
        clientId: id,
        requestId: other,
        reviewBasis: basis,
        requestHash: hash,
        fields,
        existingEngagementId: other,
      }, '').existingEngagementId,
    ).toBe(other);
    expect(
      decodeEngagementCreationPreview({
        clientId: id,
        requestId: other,
        reviewBasis: basis,
        requestHash: hash,
        fields,
        existingEngagementId: null,
      }, '').existingEngagementId,
    ).toBeNull();
  });
  it('dispatches once, blocks unknown resubmission and reconciles its actor-owned receipt', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const result = c.execute();
    const post = TestBed.inject(HttpTestingController).expectOne(url);
    expect(post.request.body.expectedRequestHash).toBe(hash);
    post.flush({}, { status: 503, statusText: 'Unavailable' });
    await result;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    await c.prepare();
    TestBed.inject(HttpTestingController).expectNone(url + '/preview');
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: receipt(r.requestId) });
    await check;
    TestBed.tick();
    expect(c.receipt()?.requestId).toBe(r.requestId);
    c.acknowledge();
    read({ ...state, reviewBasis: 'c'.repeat(64) });
    expect(c.uncertain()).toBe(false);
    expect(c.model().serviceProfile).toBe('');
    expect(c.pending()).toBeNull();
  });
  it('recovers only the request across changed revisions, never old editable fields or assent', async () => {
    const first = open();
    read();
    const c = first.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const dispatch = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 503, statusText: 'Unknown' });
    await dispatch;
    TestBed.tick();
    first.destroy();
    const next = open();
    read({ ...state, reviewBasis: 'c'.repeat(64) });
    const restored = next.componentInstance;
    expect(restored.uncertain()).toBe(true);
    expect(restored.pending()?.requestId).toBe(r.requestId);
    expect(restored.model().serviceProfile).toBe('');
    expect(restored.reviewed()).toBe(false);
    expect(restored.draftAvailable()).toBe(false);
    const check = restored.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: false, receipt: null });
    await check;
    TestBed.tick();
    expect(restored.absent()).toBe(true);
    restored.model.set({ ...fields, serviceProfile: 'Different' });
    TestBed.tick();
    const prepare = restored.prepare();
    const p = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    expect(p.request.body.requestId).toBe(r.requestId);
    p.flush({
      value: {
        clientId: id,
        requestId: r.requestId,
        reviewBasis: 'c'.repeat(64),
        requestHash: 'd'.repeat(64),
        fields: { ...fields, serviceProfile: 'Different' },
      },
    });
    await prepare;
    TestBed.tick();
    expect(restored.preview()).toBeNull();
    expect(restored.pending()).not.toBeNull();
  });
  it('restores matching tab fields explicitly and excludes review assent', () => {
    const f = open();
    read();
    const c = f.componentInstance;
    c.model.set(fields);
    TestBed.tick();
    c.reviewed.set(true);
    expect(c.saveDraft()).toBe(true);
    f.destroy();
    const next = open();
    read();
    expect(next.componentInstance.draftAvailable()).toBe(true);
    expect(next.componentInstance.model().serviceProfile).toBe('');
    next.componentInstance.restoreDraft();
    TestBed.tick();
    expect(next.componentInstance.model()).toEqual(fields);
    expect(next.componentInstance.reviewed()).toBe(false);
    expect(
      Array.from({ length: sessionStorage.length }, (_, i) =>
        sessionStorage.getItem(sessionStorage.key(i)!),
      ).join(),
    ).not.toContain('reviewed');
  });
  it('fences old route callbacks and removes local fields and receipts on session loss', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const dispatch = c.execute();
    const pending = TestBed.inject(HttpTestingController).expectOne(url);
    ids.next(convertToParamMap({ id: other }));
    TestBed.tick();
    read({ ...state, clientId: other });
    ids.next(convertToParamMap({ id }));
    TestBed.tick();
    read();
    c.model.set({ ...fields, serviceProfile: 'New route' });
    TestBed.tick();
    pending.flush({ value: receipt(r.requestId) });
    await dispatch;
    TestBed.tick();
    expect(c.model().serviceProfile).toBe('New route');
    expect(c.receipt()).toBeNull();
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(c.model().serviceProfile).toBe('');
    expect(c.state.data()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });
  it('keeps malformed successful acknowledgments unknown and fences destroyed callbacks', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c);
    c.reviewed.set(true);
    const dispatched = c.execute();
    TestBed.inject(HttpTestingController).expectOne(url).flush({ value: {} });
    await dispatched;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.model()).toEqual(fields);
    expect(c.receipt()).toBeNull();
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    const check = c.reconcile();
    const reply = TestBed.inject(HttpTestingController).expectOne((r) =>
      r.url.includes('/receipts/'),
    );
    f.destroy();
    reply.flush({ found: false, receipt: null });
    await check;
    expect(c.absent()).toBe(false);
  });
  it('requires a fresh review for an absent identical retry and clears protected fields on scope refusal', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const dispatched = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 503, statusText: 'Unknown' });
    await dispatched;
    TestBed.tick();
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: false, receipt: null });
    await check;
    TestBed.tick();
    expect(c.absent()).toBe(true);
    const review = c.prepare();
    const p = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    expect(p.request.body.requestId).toBe(r.requestId);
    p.flush({
      value: {
        clientId: id,
        requestId: r.requestId,
        reviewBasis: basis,
        requestHash: hash,
        fields,
        existingEngagementId: null,
      },
    });
    await review;
    TestBed.tick();
    expect(c.reviewed()).toBe(false);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    c.reviewed.set(true);
    const retry = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({ value: receipt(r.requestId) });
    await retry;
    TestBed.tick();
    read();
    expect(c.uncertain()).toBe(false);
    expect(c.receipt()).not.toBeNull();
    c.state.reload();
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(c.model().serviceProfile).toBe('');
    expect(c.preview()).toBeNull();
    expect(c.receipt()).toBeNull();
  });
  it('removes protected context immediately when reviewed creation is denied', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c);
    c.reviewed.set(true);
    const dispatched = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    await dispatched;
    TestBed.tick();
    expect(c.state.data()).toBeNull();
    expect(c.model().serviceProfile).toBe('');
    expect(c.preview()).toBeNull();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(c.receipt()).toBeNull();
  });
});
