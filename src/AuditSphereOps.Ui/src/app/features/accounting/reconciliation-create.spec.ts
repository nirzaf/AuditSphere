import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogModule } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { ReconciliationCreate } from './reconciliation-create';
import { SessionService } from '../../core/session';

const engagementId = '11111111-1111-4111-8111-111111111111';
const clientId = '22222222-2222-4222-8222-222222222222';
const periodId = '33333333-3333-4333-8333-333333333333';
const sourceId = '44444444-4444-4444-8444-444444444444';
const actorId = '55555555-5555-4555-8555-555555555555';
const basis = 'a'.repeat(64);
const sourceHash = 'b'.repeat(64);
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
  sourceId, sourceHash, inputGeneration: 1, reviewBasis: basis, canPrepare: false,
  blockers: ['A distinct independently evidenced control population is missing; a selected TB or GL source cannot reconcile itself.'],
};

describe('native generic reconciliation preparation', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(() => {
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
  });

  it('shows the missing independent source and never requests a source-only preview', async () => {
    const fixture = TestBed.createComponent(ReconciliationCreate);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(root).flush(context);
    TestBed.tick();
    http.expectOne(`${root}/state?sourceKind=TRIAL_BALANCE&sourceId=${sourceId}`).flush(state);
    TestBed.tick();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.state.data()?.canPrepare).toBe(false);
    expect(component.valid()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('distinct independently evidenced control population');
    expect(fixture.nativeElement.textContent).toContain('Native AR/AP and typed bank workflows remain separately available.');
    expect(fixture.nativeElement.textContent).not.toContain('General ledger total');
    expect(fixture.nativeElement.textContent).not.toContain('Review before saving');

    await component.prepare();
    http.expectNone(`${root}/preview`);
    expect(component.preview()).toBeNull();
  });
});
