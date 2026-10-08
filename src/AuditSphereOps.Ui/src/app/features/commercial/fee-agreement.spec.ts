import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { FeeAgreement, advancePreparationStates, decodeFeeAgreement } from './fee-agreement';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const milestone = {
  id,
  kind: 'ADVANCE',
  amount: '12500.00',
  state: 'PENDING',
  invoiceId: null,
  invoiceNumber: null,
  invoiceStatus: null,
  allocated: '0',
  outstanding: '12500.00',
  paidAt: null,
};
/** Server messages, one per advance preparation state (AdvanceInvoicePreparationStates in FeeAgreementWorkspaceQuery). */
const messages: Record<string, string> = {
  NOT_APPLICABLE: 'No advance milestone exists until the Engagement Letter is generated.',
  AWAITING_ENGAGEMENT_LETTER:
    'Generate the current approved Engagement Letter before the advance invoice can be prepared.',
  PENDING_AUTOMATION_DISABLED:
    'Advance-invoice preparation is pending: automatic drafting is disabled, so Finance must prepare the draft manually.',
  AWAITING_AUTOMATION: 'Automatic drafting is enabled and will create one advance invoice draft.',
  QUEUED: 'The advance invoice draft is queued. Queued is not yet a draft.',
  BLOCKED:
    'Automatic drafting was blocked. Finance must resolve the blocker before the draft can be retried.',
  DRAFT_CREATED:
    'The advance invoice draft exists. Finance review and posting are still required before it is official.',
  CANCELLED:
    'The advance invoice was cancelled. Finance must prepare a replacement through the normal review.',
  OFFICIAL_INVOICE: 'The advance invoice is POSTED. Delivery is recorded separately from posting.',
};
const workspace = {
  proposalId: id,
  agreementId: id,
  clientId: id,
  engagementId: null,
  fee: '25000.00',
  currency: 'QAR',
  advancePercent: '50',
  canCreate: false,
  canFinance: false,
  releaseRecorded: false,
  creationBlockers: [],
  milestones: [milestone, { ...milestone, kind: 'BALANCE' }],
  engagements: [],
  advancePreparation: {
    state: 'PENDING_AUTOMATION_DISABLED',
    message: messages['PENDING_AUTOMATION_DISABLED'],
  },
};
describe('Fee agreement contract', () => {
  it('preserves exact amounts and server finance authority', () => {
    const w = decodeFeeAgreement(workspace);
    expect(w.fee).toBe('25000.00');
    expect(w.advancePercent).toBe('50');
    expect(w.canFinance).toBe(false);
  });
  it('rejects numeric money and forged references', () => {
    expect(() => decodeFeeAgreement({ ...workspace, fee: 25000 })).toThrow();
    expect(() =>
      decodeFeeAgreement({
        ...workspace,
        milestones: [{ ...milestone, invoiceId: 'guess' }, workspace.milestones[1]],
      }),
    ).toThrow();
    expect(() => decodeFeeAgreement({ ...workspace, canFinance: 'yes' })).toThrow();
  });
  it('requires both exact milestone kinds and bounds candidates', () => {
    expect(() =>
      decodeFeeAgreement({ ...workspace, milestones: [milestone, milestone] }),
    ).toThrow();
    expect(() => decodeFeeAgreement({ ...workspace, milestones: [] })).toThrow();
    expect(() =>
      decodeFeeAgreement({
        ...workspace,
        engagements: Array(101).fill({ id, serviceRoute: 'Audit', periodStart: '', periodEnd: '' }),
      }),
    ).toThrow();
  });
  it('supports a guarded pre-agreement state', () => {
    expect(
      decodeFeeAgreement({
        ...workspace,
        agreementId: null,
        milestones: [],
        advancePreparation: { state: 'NOT_APPLICABLE', message: messages['NOT_APPLICABLE'] },
        creationBlockers: ['Client acceptance required.'],
      }).creationBlockers,
    ).toHaveLength(1);
  });
  it('constrains the advance preparation to the nine server states', () => {
    expect([...advancePreparationStates].sort()).toEqual(Object.keys(messages).sort());
    for (const state of Object.keys(messages)) {
      const w = decodeFeeAgreement({
        ...workspace,
        advancePreparation: { state, message: messages[state] },
      });
      expect(w.advancePreparation.message).toBe(messages[state]);
    }
    expect(() =>
      decodeFeeAgreement({ ...workspace, advancePreparation: { state: 'PAID', message: 'x' } }),
    ).toThrow();
    expect(() =>
      decodeFeeAgreement({ ...workspace, advancePreparation: { state: 'QUEUED', message: '' } }),
    ).toThrow();
    expect(() =>
      decodeFeeAgreement({
        ...workspace,
        advancePreparation: { state: 'QUEUED', message: 'x'.repeat(501) },
      }),
    ).toThrow();
  });
});
describe('Advance invoice preparation display', () => {
  function setup(): HttpTestingController {
    TestBed.configureTestingModule({
      imports: [FeeAgreement],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    return TestBed.inject(HttpTestingController);
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  for (const state of Object.keys(messages)) {
    it(`renders ${state} beside the advance milestone with its server message`, () => {
      const http = setup();
      const f = TestBed.createComponent(FeeAgreement);
      f.componentRef.setInput('proposalId', id);
      f.detectChanges();
      http
        .expectOne('/api/ui/proposals/' + id + '/fee-agreement')
        .flush({ ...workspace, advancePreparation: { state, message: messages[state] } });
      f.detectChanges();
      const host = f.nativeElement as HTMLElement;
      const advanceRow = [...host.querySelectorAll('tbody tr')].find((row) =>
        row.textContent?.includes('ADVANCE'),
      );
      expect(advanceRow?.querySelector(`[data-status="${state}"]`)).not.toBeNull();
      expect(advanceRow?.querySelector('[data-testid="advance-preparation"]')?.textContent).toContain(
        messages[state],
      );
    });
  }
  it('shows the standard decode error when the advance state is outside the nine states', () => {
    const http = setup();
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('proposalId', id);
    f.detectChanges();
    http
      .expectOne('/api/ui/proposals/' + id + '/fee-agreement')
      .flush({ ...workspace, advancePreparation: { state: 'PAID', message: 'Unexpected.' } });
    f.detectChanges();
    expect(f.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Unsupported fee agreement response',
    );
    expect(f.nativeElement.querySelector('[data-testid="advance-preparation"]')).toBeNull();
  });
});
