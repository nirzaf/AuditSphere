import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { By } from '@angular/platform-browser';
import { SessionService } from '../../core/session';
import { AcceptanceChecklist } from './checklist';
import { AssessmentCommandEditor } from './assessment-command-editor';
import {
  assessmentFields,
  decodeAssessmentFields,
  decodeAssessmentPreview,
  decodeAssessmentReceipt,
  decodeAssessmentLookup,
} from './assessment-command-contracts';

const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const hash = 'a'.repeat(64),
  basis = 'b'.repeat(64),
  url = '/api/ui/clients/' + id + '/assessment';
const question = {
  code: 'CE-001',
  section: 'Identity',
  prompt: 'Protected assessment question',
  category: 'KYC',
  answerType: 'BOOLEAN',
  requiresEvidence: true,
  adverse: false,
  answer: null,
  evidence: null,
  revision: '0',
  priorAnswer: null,
  answeredBy: null,
};
const workspace = {
  client: {
    id,
    legalName: 'Synthetic assessment client',
    registrationNumber: null,
    status: 'PROSPECT',
  },
  selectedDecision: null,
  historical: false,
  repository: null,
  answered: 0,
  total: 1,
  clearedReviews: 0,
  totalReviews: 0,
  sections: [{ section: 'Identity', answered: 0, total: 1 }],
  specialistTimeline: [],
  checklist: {
    clientId: id,
    generation: '9007199254740993',
    path: 'NEW_CLIENT',
    currentDecision: null,
    priorDecision: null,
    ready: false,
    canEdit: true,
    canReview: true,
    canDecide: true,
    canStartContinuance: false,
    questions: [question],
    clearances: [],
    blockers: [],
  },
};
const fields = {
  ...assessmentFields('ANSWER', workspace.checklist.generation),
  questionCode: 'CE-001',
  revision: '0',
  answer: 'Yes',
  evidence: 'PRIVATE-EVIDENCE',
};
function preview(requestId: string, f = fields) {
  return {
    clientId: id,
    requestId,
    requestHash: hash,
    reviewBasis: basis,
    fields: f,
    before: {
      generation: f.generation,
      path: 'NEW_CLIENT',
      decision: null,
      ready: false,
      answer: null,
      evidence: null,
      revision: f.kind === 'ANSWER' ? f.revision : null,
      reviewStatus: f.expectedStatus,
      reviewConditions: null,
    },
    effect: 'Appends exact assessment evidence; no professional decision is inferred.',
  };
}
function receipt(p: ReturnType<typeof preview>) {
  return {
    id: other,
    clientId: id,
    actorId: id,
    requestId: p.requestId,
    requestHash: hash,
    reviewBasis: basis,
    kind: p.fields.kind,
    generation: p.fields.generation,
    resultGeneration: p.fields.generation,
    resourceId: other,
    preview: p,
    createdAt: '2026-10-03T12:30:00Z',
  };
}
describe('Assessment reviewed command contracts', () => {
  it('preserves exact counters and refuses mixed action shapes', () => {
    expect(decodeAssessmentFields(fields).generation).toBe('9007199254740993');
    for (const f of [
      { ...fields, generation: 9007199254740993 },
      { ...fields, generation: '01' },
      { ...fields, revision: '9223372036854775808' },
      { ...fields, rationale: 'Hidden decision' },
      { ...fields, evidence: 'x'.repeat(501) },
    ])
      expect(() => decodeAssessmentFields(f)).toThrow();
  });
  it('refuses fabricated readiness, contradictory results and unbound receipts', () => {
    const p = preview(id),
      r = receipt(p);
    expect(decodeAssessmentReceipt(r).generation).toBe(fields.generation);
    expect(() => decodeAssessmentReceipt({ ...r, resultGeneration: '9007199254740994' })).toThrow();
    expect(() => decodeAssessmentReceipt({ ...r, requestHash: 'c'.repeat(64) })).toThrow();
    expect(() => decodeAssessmentLookup({ found: true, receipt: null })).toThrow();
    expect(() =>
      decodeAssessmentPreview({
        ...p,
        fields: {
          ...assessmentFields('DECISION', fields.generation),
          serviceRoute: 'Audit',
          decision: 'Accepted',
          rationale: 'Reviewed',
        },
        before: { ...p.before, revision: null },
      }),
    ).toThrow();
  });
});
describe('Assessment Signal Forms and retained request recovery', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>,
    queries: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear();
    ids = new BehaviorSubject(convertToParamMap({ id }));
    queries = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [AcceptanceChecklist],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: ids,
            queryParamMap: queries,
            snapshot: { paramMap: ids.value, queryParamMap: queries.value },
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
    vi.restoreAllMocks();
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function open() {
    const fixture = TestBed.createComponent(AcceptanceChecklist);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }
  function read(value = workspace) {
    const rs = TestBed.inject(HttpTestingController)
      .match((r) => r.method === 'GET' && !r.url.includes('/receipts/'))
      .filter((r) => !r.cancelled);
    expect(rs).toHaveLength(1);
    rs[0].flush(value);
    TestBed.tick();
  }
  async function review(c: AcceptanceChecklist) {
    const task = c.prepare(fields),
      request = TestBed.inject(HttpTestingController).expectOne(url + '/preview'),
      p = preview(request.request.body.requestId);
    request.flush({ value: p });
    await task;
    TestBed.tick();
    return p;
  }
  function answerEditor(f: ReturnType<typeof open>) {
    return f.debugElement.queryAll(By.directive(AssessmentCommandEditor))[0]
      .componentInstance as AssessmentCommandEditor;
  }
  it('validates evidence, bounds and conditional decisions before any review', () => {
    const f = open();
    read();
    const editor = answerEditor(f);
    editor.model.update((m) => ({ ...m, answer: 'Yes', evidence: ' ' }));
    TestBed.tick();
    expect(editor.valid()).toBe(false);
    editor.model.update((m) => ({ ...m, evidence: 'DOC' }));
    TestBed.tick();
    expect(editor.valid()).toBe(true);
    editor.model.update((m) => ({ ...m, evidence: 'x'.repeat(501) }));
    TestBed.tick();
    expect(editor.valid()).toBe(false);
    const decision = f.debugElement
      .queryAll(By.directive(AssessmentCommandEditor))
      .map((d) => d.componentInstance as AssessmentCommandEditor)
      .find((c) => c.seed().kind === 'DECISION')!;
    decision.model.update((m) => ({
      ...m,
      rationale: 'Human review',
      conditions: 'Blocking condition',
    }));
    TestBed.tick();
    expect(decision.valid()).toBe(false);
    decision.model.update((m) => ({ ...m, decision: 'AcceptedWithConditions' }));
    TestBed.tick();
    expect(decision.valid()).toBe(true);
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
  });
  it('renders exact review, requires fresh assent and invalidates it on model changes', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    expect(f.nativeElement.textContent).toContain('Review exact assessment action');
    expect(c.reviewed).toBe(false);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url + '/commands');
    c.setReviewed(true);
    expect(c.reviewed).toBe(true);
    answerEditor(f).model.update((m) => ({ ...m, answer: 'No' }));
    TestBed.tick();
    expect(c.reviewed).toBe(false);
    expect(c.preview()).toBeNull();
    expect(p.fields.evidence).toBe('PRIVATE-EVIDENCE');
  });
  it('stores only a pending identity before dispatch, then recovers without another POST', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    c.setReviewed(true);
    const task = c.execute(),
      post = TestBed.inject(HttpTestingController).expectOne(url + '/commands');
    expect(c.pending()?.requestId).toBe(p.requestId);
    const raw = sessionStorage.getItem(sessionStorage.key(0)!)!;
    expect(raw).not.toContain('PRIVATE-EVIDENCE');
    expect(raw).not.toContain('Protected assessment question');
    expect(Object.keys(JSON.parse(raw).value).sort()).toEqual(['requestHash', 'requestId']);
    post.flush({}, { status: 503, statusText: 'Unconfirmed' });
    await task;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(f.nativeElement.textContent).not.toContain('Protected assessment question');
    const recovery = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + p.requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: receipt(p) });
    await recovery;
    TestBed.tick();
    expect(c.receipt()?.requestId).toBe(p.requestId);
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    read();
    expect(c.pending()).toBeNull();
    expect(sessionStorage.length).toBe(0);
    expect(c.uncertain()).toBe(false);
  });
  it('recovers a pending reference after reload and does not restore professional content or assent', async () => {
    let f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    c.setReviewed(true);
    const task = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/commands')
      .flush({}, { status: 503, statusText: 'Unconfirmed' });
    await task;
    f.destroy();
    f = open();
    read();
    expect(f.componentInstance.pending()?.requestId).toBe(p.requestId);
    expect(f.componentInstance.reviewed).toBe(false);
    expect(f.componentInstance.uncertain()).toBe(true);
    expect(f.nativeElement.textContent).not.toContain('PRIVATE-EVIDENCE');
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
  });
  it('refuses dispatch when recovery storage is unavailable', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await review(c);
    c.setReviewed(true);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Synthetic quota');
    });
    await c.execute();
    expect(c.commandStatus()).toContain('No assessment action was sent');
    TestBed.inject(HttpTestingController).expectNone(url + '/commands');
  });
  it('retains an absent request until explicit acknowledgement and a fresh authorized read', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    c.setReviewed(true);
    const task = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/commands')
      .flush({}, { status: 409, statusText: 'Conflict' });
    await task;
    const lookup = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + p.requestId + '?requestHash=' + hash)
      .flush({ found: false, receipt: null });
    await lookup;
    expect(c.absent()).toBe(true);
    expect(c.pending()).not.toBeNull();
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    c.acknowledgeAbsent();
    read();
    expect(c.uncertain()).toBe(false);
    expect(c.pending()).toBeNull();
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
  });
  it('does not let a late command refill another route visit', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    c.setReviewed(true);
    const task = c.execute(),
      post = TestBed.inject(HttpTestingController).expectOne(url + '/commands');
    ids.next(convertToParamMap({ id: other }));
    TestBed.tick();
    read({
      ...workspace,
      client: { ...workspace.client, id: other },
      checklist: { ...workspace.checklist, clientId: other },
    });
    post.flush({ value: receipt(p) });
    await task;
    TestBed.tick();
    expect(c.data()?.clientId).toBe(other);
    expect(c.receipt()).toBeNull();
    expect(c.busy()).toBe(false);
  });
  it('clears protected content on session loss and ignores its late command', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    c.setReviewed(true);
    const task = c.execute(),
      post = TestBed.inject(HttpTestingController).expectOne(url + '/commands');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    post.flush({ value: receipt(p) });
    await task;
    TestBed.tick();
    expect(c.data()).toBeNull();
    expect(c.receipt()).toBeNull();
    expect(c.preview()).toBeNull();
    expect(c.pending()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Synthetic assessment client');
    expect(sessionStorage.length).toBe(0);
  });
  it('retains the original request after an unsupported success response', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      p = await review(c);
    c.setReviewed(true);
    const task = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/commands')
      .flush({ value: { ...receipt(p), actorId: other } });
    await task;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.receipt()).toBeNull();
    expect(c.pending()?.requestId).toBe(p.requestId);
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
  });
});
