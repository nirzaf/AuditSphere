import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { PracticeRateCards, decodeRateCards } from './rate-cards';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const other = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const version = (overrides: Record<string, unknown> = {}) => ({
  id,
  version: 1,
  ratePerHour: '1000.00',
  status: 'APPROVED',
  preparedByUserId: other,
  approvedByUserId: other,
  approvedAt: '2026-10-06T10:00:00Z',
  createdAt: '2026-10-06T09:00:00Z',
  canApprove: false,
  ...overrides,
});
const baseline = [
  { role: 'Engagement Partner', ratePerHour: '1000.00', state: 'APPROVED', cardId: id, approvedByUserId: other },
  { role: 'Audit Manager', ratePerHour: '750.00', state: 'MISSING', cardId: null, approvedByUserId: null },
];
const workspace = {
  canRevise: true,
  slots: [{ role: 'Engagement Partner', activity: 'General', currency: 'QAR', approved: version(), draft: null }],
  steBaseline: baseline,
};

const decode = (value: unknown) => decodeRateCards(value, 'rate cards');

describe('Charge-out rate contract (STE 4.5.1)', () => {
  it('keeps rates as exact decimal strings and the server-computed approval flag', () => {
    const decoded = decode(workspace);
    expect(decoded.slots[0].approved?.ratePerHour).toBe('1000.00');
    expect(decoded.canRevise).toBe(true);
    expect(() =>
      decode({ ...workspace, slots: [{ ...workspace.slots[0], approved: version({ ratePerHour: 1000 }) }] }),
    ).toThrow();
    expect(() =>
      decode({ ...workspace, slots: [{ ...workspace.slots[0], draft: version({ canApprove: 'yes' }) }] }),
    ).toThrow();
  });
});

describe('Charge-out rate screen', () => {
  function setup(): HttpTestingController {
    TestBed.configureTestingModule({
      imports: [PracticeRateCards],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    return TestBed.inject(HttpTestingController);
  }
  function render(payload: object) {
    const http = setup();
    const f = TestBed.createComponent(PracticeRateCards);
    f.detectChanges();
    http.expectOne('/api/ui/practice/rate-cards').flush(payload);
    f.detectChanges();
    return f;
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('offers the approve action only where the server says the actor may approve', () => {
    const draft = version({ id: other, version: 2, ratePerHour: '1100.00', status: 'DRAFT', approvedByUserId: null, approvedAt: null, canApprove: true });
    const f = render({ ...workspace, slots: [{ ...workspace.slots[0], draft }] });
    const text = (f.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Approve draft');
    expect(text).toContain('1,100.00');
  });

  it('shows the preparer that a different approver must act', () => {
    const draft = version({ id: other, version: 2, ratePerHour: '1100.00', status: 'DRAFT', approvedByUserId: null, approvedAt: null, canApprove: false });
    const f = render({ ...workspace, slots: [{ ...workspace.slots[0], draft }] });
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Awaiting a different approver');
    expect([...host.querySelectorAll('button')].some((b) => b.textContent?.includes('Approve draft'))).toBe(false);
  });

  it('shows the revision form when the server grants revision', () => {
    const f = render(workspace);
    expect((f.nativeElement as HTMLElement).querySelector('input[name="rateRole"]')).not.toBeNull();
  });

  it('hides the revision form when the server does not grant revision', () => {
    const f = render({ ...workspace, canRevise: false });
    expect((f.nativeElement as HTMLElement).querySelector('input[name="rateRole"]')).toBeNull();
  });

  it('offers the baseline action while a baseline role is missing', () => {
    const f = render(workspace);
    expect((f.nativeElement as HTMLElement).textContent).toContain('Create missing STE baseline drafts');
  });

  it('hides the baseline action once every baseline role has a card', () => {
    const complete = baseline.map((line) => ({ ...line, state: 'APPROVED', cardId: id, approvedByUserId: other }));
    const f = render({ ...workspace, steBaseline: complete });
    expect((f.nativeElement as HTMLElement).textContent).not.toContain('Create missing STE baseline drafts');
  });
});
