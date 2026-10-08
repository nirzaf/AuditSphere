import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PendingOutcomes, PENDING_OUTCOME_ROUTES } from './pending-outcomes';
import { SessionService } from './session';

const id = '11111111-1111-4111-8111-111111111111';
const hash = 'a'.repeat(64);
const prefix = 'auditsphere-tab-draft-v1:';
const key = (entity: string, user = id, generation = '0') =>
  `${prefix}${id}:${user}:${generation}:${entity}`;
const pendingValue = () => ({ requestId: id, requestHash: hash });

describe('Global pending outcome reconciliation', () => {
  let outcomes: PendingOutcomes, session: SessionService;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    session = TestBed.inject(SessionService);
    session.current.set({ userId: id, firmId: id, generation: '0', staff: true });
    outcomes = TestBed.inject(PendingOutcomes);
    TestBed.tick();
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  const seed = (entity: string, value: unknown, submissionPending = true, user = id, generation = '0') => {
    sessionStorage.setItem(key(entity, user, generation), JSON.stringify({
      schemaVersion: 1, firmId: id, userId: user, generation, entity,
      baseRevision: hash, savedAt: Date.now(), expiresAt: Date.now() + 4 * 60 * 60 * 1000,
      submissionPending, value,
    }));
  };

  it('surfaces a persisted unknown outcome for the current identity with its owning workspace link', () => {
    seed('contact-request/' + id, pendingValue());
    outcomes.refresh();
    expect(outcomes.items()).toEqual([{
      label: 'Client contact creation',
      link: '/app/clients/' + id + '/contacts/new',
    }]);
  });

  it('substitutes multi-segment fences in the owning workspace order', () => {
    seed('valuation-request/GL/' + id, pendingValue());
    seed('evidence-request/ECL/' + id, pendingValue());
    outcomes.refresh();
    expect(outcomes.items()).toContainEqual({
      label: 'Currency valuation preparation',
      link: '/app/accounting/reconciliations/' + id + '/prepare/GL',
    });
    expect(outcomes.items()).toContainEqual({
      label: 'Accounting evidence action',
      link: '/app/accounting/evidence/ECL/' + id + '/actions',
    });
  });

  it('ignores confirmed drafts, other identities, other generations and malformed entries', () => {
    seed('contact-request/' + id, pendingValue(), false);
    seed('contact-request/' + id, pendingValue(), true, '22222222-2222-4222-8222-222222222222');
    seed('contact-request/' + id, pendingValue(), true, id, '1');
    sessionStorage.setItem(key('journal-request/' + id), '{not json');
    sessionStorage.setItem(key('journal-create-request/' + id), JSON.stringify({
      schemaVersion: 1, submissionPending: true, value: { requestId: 'not-a-uuid', requestHash: hash },
    }));
    outcomes.refresh();
    expect(outcomes.items()).toEqual([]);
  });

  it('surfaces an unmapped fence without inventing a destination', () => {
    seed('future-request/' + id, pendingValue());
    outcomes.refresh();
    expect(outcomes.items()).toEqual([{ label: 'Unverified command outcome', link: null }]);
  });

  it('drops a mapped fence whose segments would forge a link outside the staff workspace', () => {
    seed('contact-request/..%2Fportal', pendingValue());
    outcomes.refresh();
    expect(outcomes.items()).toEqual([{ label: 'Client contact creation', link: null }]);
  });

  it('maps every known pending-fence scope and clears with the identity', () => {
    const known = [
      'contact-request', 'engagement-creation-request', 'client-conversion-request',
      'assessment-request', 'activation-request', 'engagement-budget-request',
      'engagement-budget-approval-request', 'engagement-staffing-request',
      'journal-create-request', 'journal-request', 'evidence-request',
      'valuation-request', 'analytical-request',
    ];
    for (const name of known) expect(PENDING_OUTCOME_ROUTES[name].template).toMatch(/^\/app\//);
    session.clear();
    seed('contact-request/' + id, pendingValue());
    outcomes.refresh();
    expect(outcomes.items()).toEqual([]);
  });
});
