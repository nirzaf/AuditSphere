import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogModule } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { ReconciliationCreate, reconciliationEditableFields } from './reconciliation-create';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';

const engagementId = '11111111-1111-4111-8111-111111111111';
const clientId = '22222222-2222-4222-8222-222222222222';
const periodId = '33333333-3333-4333-8333-333333333333';
const sourceId = '44444444-4444-4444-8444-444444444444';
const actorId = '55555555-5555-4555-8555-555555555555';
const basis = 'a'.repeat(64);
const sourceHash = 'b'.repeat(64);
const requestHash = 'c'.repeat(64);
const root = `/api/ui/engagements/${engagementId}/reconciliation-preparation`;

const context = {
  engagementId, clientId, clientName: 'Synthetic client', engagementName: 'Synthetic audit',
  periods: [{ id: periodId, code: 'FY26', startDate: '2026-01-01', endDate: '2026-12-31',
    currency: 'QAR', basis: 'IFRS', status: 'ACTIVE', revision: 1 }],
  sources: [{ id: sourceId, kind: 'TRIAL_BALANCE', periodId, periodCode: 'FY26', bookId: null,
    currency: 'QAR', basis: 'IFRS', rowCount: 1, sourceHash, importedAt: '2026-10-04T00:00:00Z' }],
};
const state = {
  engagementId, clientId, clientName: 'Synthetic client', engagementName: 'Synthetic audit', periodId,
  periodCode: 'FY26', bookId: null, currency: 'QAR', basis: 'IFRS', sourceKind: 'TRIAL_BALANCE',
  sourceId, sourceHash, inputGeneration: 1, reviewBasis: basis, canPrepare: true, blockers: [],
};

describe('native reviewed reconciliation creation', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(() => {
    sessionStorage.clear();
    params = new BehaviorSubject(convertToParamMap({ id: engagementId }));
    TestBed.configureTestingModule({
      imports: [ReconciliationCreate, MatDialogModule],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), {
        provide: ActivatedRoute,
        useValue: { paramMap: params, snapshot: { queryParamMap: convertToParamMap({ sourceKind: 'TRIAL_BALANCE', sourceId }) } },
      }],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId: clientId, generation: '1', staff: true });
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });

  it('persists both recovery checkpoints before one reviewed, idempotent command', async () => {
    const fixture = TestBed.createComponent(ReconciliationCreate);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(root).flush(context);
    TestBed.tick();
    http.expectOne(`${root}/state?sourceKind=TRIAL_BALANCE&sourceId=${sourceId}`).flush(state);
    TestBed.tick();

    const component = fixture.componentInstance;
    component.model.set({ area: 'CASH', accountCodes: '1000', asOfDate: '2026-12-31', agingBasis: '',
      agingBucketRuleVersion: '', reason: 'Prepare synthetic evidence.', evidenceReference: 'SYNTHETIC-REF' });
    TestBed.tick();
    expect(component.state.data()?.reviewBasis).toBe(basis);
    expect(reconciliationEditableFields(component.model())).not.toBeNull();
    expect(TestBed.inject(SessionService).current()?.staff).toBe(true);
    const scope = (component as unknown as { scope: (pending?: boolean) => { entity: string; baseRevision: string } }).scope();
    expect(scope).toEqual({ entity: `reconciliation-fields/${engagementId}/TRIAL_BALANCE/${sourceId}`, baseRevision: basis });
    expect(TestBed.inject(TabDrafts).save(scope, component.model(), reconciliationEditableFields)).toBe(true);
    expect(component.saveDraft()).toBe(true);
    expect(sessionStorage.length).toBe(1);
    const previewTask = component.prepare();
    http.expectOne(`${root}/preview`).flush({ value: {
      engagementId, clientId, periodId, requestId: '66666666-6666-4666-8666-666666666666', reviewBasis: basis,
      requestHash, sourceKind: 'TRIAL_BALANCE', sourceId, sourceHash, area: 'CASH', accountSelection: '1000',
      sourceTotal: '100.000000', glTotal: '100.000000', residual: '0.000000', currency: 'QAR',
      resultStatus: 'RECONCILED', canProceed: true, blockers: [],
    } });
    await previewTask;
    expect(component.state.data()?.reviewBasis).toBe(basis);
    expect(component.preview()?.requestHash).toBe(requestHash);
    expect(component.preview()?.canProceed).toBe(true);
    component.reviewed.set(true);
    expect(component.reviewed()).toBe(true);
    const intent = (component as unknown as { request: { model: unknown; reviewBasis: string } | null }).request;
    expect(intent).not.toBeNull();
    expect(intent?.reviewBasis).toBe(component.state.data()?.reviewBasis);
    expect(JSON.stringify(intent?.model)).toBe(JSON.stringify(component.model()));
    expect(component.busy()).toBe(false);
    const executeTask = component.execute();
    const command = http.expectOne(root);
    expect(command.request.body.reviewed).toBe(true);
    expect(sessionStorage.length).toBe(2);
    command.flush({ value: {
      id: '77777777-7777-4777-8777-777777777777', requestId: command.request.body.requestId, requestHash,
      reconciliationId: '88888888-8888-4888-8888-888888888888', actorId, reason: 'Prepare synthetic evidence.',
      evidenceReference: 'SYNTHETIC-REF', createdAt: '2026-10-04T00:00:00Z',
      result: { reconciliationId: '88888888-8888-4888-8888-888888888888', revision: 1, supersedesId: null,
        status: 'RECONCILED', sourceTotal: '100.000000', glTotal: '100.000000', residual: '0.000000',
        sourceHash, actorId, createdAt: '2026-10-04T00:00:00Z' },
    } });
    await executeTask;
    TestBed.tick();
    expect(component.receipt()?.requestHash).toBe(requestHash);
    expect(component.message()).toContain('immutable receipt');
    expect(sessionStorage.length).toBe(0);
    http.expectOne(root).flush(context);
  });
});
