import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { ConsolidationGroupMembership, percentagePoints } from './group-membership';

const groupId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const clientId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const actorId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const firmId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const baseUrl = `/api/ui/consolidation/groups/${groupId}/members`;

function workspace(members: object[] = [], revision = 3) {
  return { groupId, groupCode: 'HOLDCO-01', groupName: 'Holding Company Group', groupRevision: revision,
    page: 0, pageSize: 25, total: members.length, members,
    eligibleClients: [{ id: clientId, legalName: 'Synthetic Legal Entity' }], canAddMember: true };
}

function member(overrides: Record<string, unknown> = {}) {
  return { id: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', clientId, clientName: 'Synthetic Legal Entity',
    effectiveFrom: '2026-01-01', effectiveTo: null, controlMethod: 'CONTROLLED', ownershipPercent: '91.123456',
    economicInterestPercent: '78.654321', evidenceReference: 'PERIMETER-APPROVAL-01', status: 'APPROVED', revision: 1,
    ...overrides };
}

describe('consolidation group membership', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [ConsolidationGroupMembership],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); sessionStorage.clear(); vi.restoreAllMocks(); }
  });

  it('accepts exact percentage points from 0 through 100 and rejects out-of-range or over-precision input', () => {
    expect(percentagePoints('0')).toBe('0');
    expect(percentagePoints('100')).toBe('100');
    expect(percentagePoints('91.123456')).toBe('91.123456');
    expect(percentagePoints('100.000001')).toBeNull();
    expect(percentagePoints('-0.000001')).toBeNull();
    expect(percentagePoints('91.1234567')).toBeNull();
    expect(percentagePoints('1e2')).toBeNull();
  });

  async function open() {
    const fixture = TestBed.createComponent(ConsolidationGroupMembership);
    fixture.componentRef.setInput('groupId', groupId);
    fixture.detectChanges();
    http.expectOne(`${baseUrl}?page=0&pageSize=25`).flush(workspace());
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

  it('posts the exact decimal strings once and reloads the retained perimeter after success', async () => {
    const { fixture, component } = await open();
    component.clientId = clientId;
    component.effectiveFrom = '2026-01-01';
    component.effectiveTo = '';
    component.controlMethod = ' controlled ';
    component.ownershipPercent = '91.123456';
    component.economicInterestPercent = '78.654321';
    component.evidenceReference = ' PERIMETER-APPROVAL-01 ';

    const adding = component.addMember();
    const request = http.expectOne({ method: 'POST', url: baseUrl });
    expect(request.request.body).toEqual({ clientId, effectiveFrom: '2026-01-01', effectiveTo: null,
      controlMethod: 'controlled', ownershipPercent: '91.123456', economicInterestPercent: '78.654321',
      evidenceReference: 'PERIMETER-APPROVAL-01' });
    request.flush({ value: null });
    await adding;

    http.expectOne(`${baseUrl}?page=0&pageSize=25`).flush(workspace([member()], 4));
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Group revision 4');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('91.123456%');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('78.654321%');
  });

  it('reconciles an uncertain submission from the exact scoped membership and never resends it', async () => {
    const { component } = await open();
    component.clientId = clientId;
    component.effectiveFrom = '2026-01-01';
    component.effectiveTo = '';
    component.controlMethod = 'CONTROLLED';
    component.ownershipPercent = '91.123456';
    component.economicInterestPercent = '78.654321';
    component.evidenceReference = 'PERIMETER-APPROVAL-01';

    const adding = component.addMember();
    http.expectOne({ method: 'POST', url: baseUrl }).flush({}, { status: 503, statusText: 'Unavailable' });
    await adding;
    expect(component.cmd.uncertain()).toBe(true);

    const recovering = component.recoverMembership();
    const lookupUrl = `${baseUrl}?page=0&pageSize=25&clientId=${clientId}&effectiveFrom=2026-01-01`;
    http.expectOne({ method: 'GET', url: lookupUrl }).flush(workspace([member()]));
    await recovering;
    http.expectOne(`${baseUrl}?page=0&pageSize=25`).flush(workspace([member()], 4));
    expect(component.cmd.uncertain()).toBe(false);
    expect(component.pendingMembership()).toBeNull();
    http.expectNone({ method: 'POST', url: baseUrl });
  });
});
