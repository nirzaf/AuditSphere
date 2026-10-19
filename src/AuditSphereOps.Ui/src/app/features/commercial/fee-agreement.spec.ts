import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { routes } from '../../app.routes';
import { SessionService } from '../../core/session';
import { unsavedChangesGuard } from '../../core/unsaved-changes.guard';
import { FeeAgreement, advancePreparationStates, decodeFeeAgreement } from './fee-agreement';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const financeAgreementId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const lineageProposalId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const linkedEngagementId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
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
const financeWorkspace = (overrides: Record<string, unknown> = {}) => ({
  ...workspace,
  proposalId: lineageProposalId,
  agreementId: financeAgreementId,
  clientName: 'PBC TEST CLIENT',
  canCreate: false,
  canFinance: true,
  engagements: [],
  ...overrides,
});
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
  function setup(dialogOpen = vi.fn()): HttpTestingController {
    TestBed.configureTestingModule({
      imports: [FeeAgreement],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: MatDialog, useValue: { open: dialogOpen } },
      ],
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

  it('shows the NOT_APPLICABLE message when there is no agreement and so no advance milestone', () => {
    const http = setup();
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('proposalId', id);
    f.detectChanges();
    http.expectOne('/api/ui/proposals/' + id + '/fee-agreement').flush({
      ...workspace,
      agreementId: null,
      engagementId: null,
      milestones: [],
      advancePreparation: { state: 'NOT_APPLICABLE', message: messages['NOT_APPLICABLE'] },
    });
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="advance-preparation"]')?.textContent).toContain(
      messages['NOT_APPLICABLE'],
    );
  });

  it.each(['advance', 'balance'] as const)('identifies the %s milestone in partial and settled payment feedback', async (kind) => {
    const http = setup();
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('agreementId', financeAgreementId);
    f.detectChanges();
    http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(financeWorkspace());
    await f.whenStable();
    for (const paid of [false, true]) {
      const component = f.componentInstance;
      component.amount = '4000';
      component.reference = 'SYN-MILESTONE-' + kind + '-' + paid;
      component.reviewed = true;
      component.payment(kind);
      const request = http.expectOne('/api/ui/fee-agreements/' + financeAgreementId + '/' + kind + '-payment');
      expect(request.request.body.reference).toBe(component.reference);
      request.flush({ id, paid, emailQueued: true, receiptDocumentId: paid ? id : null,
        voucherDocumentId: id, voucherEmailQueued: true });
      const expected = paid
        ? (kind === 'balance' ? 'Final balance paid.' : 'Advance paid.')
        : (kind === 'balance' ? 'Partial final-balance payment recorded.' : 'Partial advance payment recorded.');
      expect(component.message()).toContain(expected);
      if (paid) expect(component.message()).toContain('delivery remains external');
      expect(component.reviewed).toBe(false);
      expect(component.reference).toBe('');
      http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(financeWorkspace());
      await f.whenStable();
    }
    f.destroy();
  });

  it('loads the sanitized finance agreement and recovers only scoped unsubmitted payment fields', async () => {
    const http = setup();
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('agreementId', financeAgreementId);
    f.detectChanges();
    http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(financeWorkspace());
    await f.whenStable();
    f.detectChanges();

    const host = f.nativeElement as HTMLElement;
    expect(host.querySelector('h2')?.textContent).toContain('Fee settlement');
    expect(host.textContent).toContain('PBC TEST CLIENT');
    expect(host.textContent).toContain('Client ID');
    expect(host.textContent).toContain(id);
    expect(host.textContent).toContain('Fee agreement ID');
    expect(host.textContent).toContain(financeAgreementId);
    expect(host.querySelector('[role="region"][aria-label="Fee milestone details"][tabindex="0"]')).not.toBeNull();
    const headers = [...host.querySelectorAll('thead th')];
    expect(headers).toHaveLength(6);
    expect(headers.every((header) => header.getAttribute('scope') === 'col')).toBe(true);
    expect(host.querySelector('tbody tr')?.textContent).toContain('ADVANCE');
    expect(host.querySelector('tbody tr')?.textContent).toContain('Not linked to an invoice');
    expect(host.textContent).not.toContain(lineageProposalId);
    expect(host.textContent).not.toContain('Create fee agreement');
    expect(host.textContent).not.toContain('Link accepted engagement');
    expect(host.textContent).not.toContain('Save fee tab draft');
    expect(host.textContent).toContain('Save fee settlement draft');
    expect(host.textContent).toContain('Recover fee settlement draft');
    expect(host.querySelector('a[href="/app/finance/fee-agreements"]')?.textContent).toContain('Back to fee settlements');

    const component = f.componentInstance;
    await expect.poll(() => component.financeTabDraft.scope()?.entity).toBe(`finance-fee:${financeAgreementId}`);
    component.amount = '4000.00';
    component.reference = 'SYN-FEE-RETRY';
    component.reviewed = true;
    expect(component.saveFinanceTabDraft()).toBe(true);
    expect(component.financeTabDraft.recover()).toEqual({ amount: '4000.00', reference: 'SYN-FEE-RETRY' });
    f.destroy();

    const recovered = TestBed.createComponent(FeeAgreement);
    recovered.componentRef.setInput('agreementId', financeAgreementId);
    recovered.detectChanges();
    http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(financeWorkspace());
    await recovered.whenStable();
    recovered.detectChanges();
    await expect.poll(() => recovered.componentInstance.financeTabDraft.scope()?.entity).toBe(`finance-fee:${financeAgreementId}`);
    recovered.componentInstance.recoverFinanceTabDraft();
    expect(recovered.componentInstance.amount).toBe('4000.00');
    expect(recovered.componentInstance.reference).toBe('SYN-FEE-RETRY');
    expect(recovered.componentInstance.engagementId).toBe('');
    expect(recovered.componentInstance.reviewed).toBe(false);
  });

  it('rejects duplicate and stale deferred navigation choices for a finance draft', async () => {
    const detailRoute = routes.find((route) => route.path === 'app/finance/fee-agreements/:id');
    expect(detailRoute?.canDeactivate).toContain(unsavedChangesGuard);
    const choice = new Subject<'save' | 'discard' | 'keep'>();
    const close = vi.fn();
    const open = vi.fn(() => ({ afterClosed: () => choice.asObservable(), close }));
    const http = setup(open);
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('agreementId', financeAgreementId);
    f.detectChanges();
    http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(financeWorkspace());
    await f.whenStable();
    f.detectChanges();
    await expect.poll(() => f.componentInstance.financeTabDraft.scope()?.entity)
      .toBe(`finance-fee:${financeAgreementId}`);

    const component = f.componentInstance;
    const retainedFields = { amount: '3000.00', reference: 'SYN-PREVIOUS-DRAFT' };
    component.amount = retainedFields.amount;
    component.reference = retainedFields.reference;
    expect(component.saveFinanceTabDraft()).toBe(true);
    component.busy.set(true);
    expect(await component.confirmNavigation()).toBe(false);
    component.busy.set(false);
    component.uncertain.set(true);
    expect(await component.confirmNavigation()).toBe(false);
    component.uncertain.set(false);
    expect(open).not.toHaveBeenCalled();
    component.amount = '4000.00';
    component.reference = 'SYN-FEE-NAVIGATION';
    component.feeFieldsChanged();
    const firstNavigation = component.confirmNavigation();
    expect(open).toHaveBeenCalledTimes(1);
    expect(await component.confirmNavigation()).toBe(false);
    expect(open).toHaveBeenCalledTimes(1);

    component.amount = '6000.00';
    component.feeFieldsChanged();
    expect(close).toHaveBeenCalledTimes(1);
    choice.next('save');
    expect(await firstNavigation).toBe(false);
    expect(component.financeTabDraft.recover()).toEqual(retainedFields);
    expect(component.reviewed).toBe(false);

    const saveNavigation = component.confirmNavigation();
    choice.next('save');
    expect(await saveNavigation).toBe(true);
    expect(component.financeTabDraft.recover()).toEqual({ amount: '6000.00', reference: 'SYN-FEE-NAVIGATION' });
    const discardNavigation = component.confirmNavigation();
    choice.next('discard');
    expect(await discardNavigation).toBe(true);
    expect(component.financeTabDraft.recover()).toBeNull();
  });

  it('fails closed on a mismatched agreement, unsanitized authority, or extra CRM fields', () => {
    const http = setup();
    const invalidResponses = [
      financeWorkspace({ agreementId: id }),
      financeWorkspace({ canCreate: true }),
      financeWorkspace({ creationBlockers: ['not sanitized'] }),
      financeWorkspace({ engagements: [{ id: linkedEngagementId, serviceRoute: 'FinancialStatementAudit', periodStart: '2026-01-01', periodEnd: '2026-12-31' }] }),
      financeWorkspace({ clientName: '' }),
      financeWorkspace({ clientName: 'x'.repeat(301) }),
      { ...financeWorkspace(), clientEmail: 'unexpected@example.invalid' },
    ];
    for (const response of invalidResponses) {
      const f = TestBed.createComponent(FeeAgreement);
      f.componentRef.setInput('agreementId', financeAgreementId);
      f.detectChanges();
      http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(response);
      f.detectChanges();
      expect(f.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
        'Unsupported fee agreement response',
      );
      f.destroy();
    }
  });

  it('requires exactly one valid proposal or agreement identifier', () => {
    const http = setup();
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('proposalId', id);
    f.componentRef.setInput('agreementId', financeAgreementId);
    f.detectChanges();
    http.expectNone('/api/ui/proposals/' + id + '/fee-agreement');
    http.expectNone('/api/ui/finance/fee-agreements/' + financeAgreementId);
    expect(f.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'exactly one fee agreement',
    );
  });

  it('clears finance agreement details when the current session is invalidated', () => {
    const http = setup();
    const session = TestBed.inject(SessionService);
    const f = TestBed.createComponent(FeeAgreement);
    f.componentRef.setInput('agreementId', financeAgreementId);
    f.detectChanges();
    http.expectOne('/api/ui/finance/fee-agreements/' + financeAgreementId).flush(financeWorkspace());
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('12500.00');

    session.clear();
    f.detectChanges();
    expect(f.nativeElement.querySelector('table')).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('12500.00');
  });
});
