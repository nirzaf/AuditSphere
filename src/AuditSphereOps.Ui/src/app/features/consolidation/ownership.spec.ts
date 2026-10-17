import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { ConsolidationOwnership, decodeOwnershipWorkspace } from './ownership';

const scopeId = '11111111-1111-4111-8111-111111111111';
const groupId = '22222222-2222-4222-8222-222222222222';
const parentId = '33333333-3333-4333-8333-333333333333';
const childId = '44444444-4444-4444-8444-444444444444';
const edgeId = '55555555-5555-4555-8555-555555555555';
function payload() {
  return { scopeId, groupId, groupName: 'Synthetic group', scopeVersion: 1, groupRevision: 7, currentGroupRevision: 7,
    scopeStatus: 'DRAFT', scopeMethod: 'RESTRICTED', canAddOwnership: true, page: 0, pageSize: 25, total: 0, interests: [],
    eligibleMembers: [{ clientId: parentId, legalName: 'Synthetic parent', membershipEffectiveFrom: '2026-01-01' },
      { clientId: childId, legalName: 'Synthetic child', membershipEffectiveFrom: '2026-01-01' }] };
}
describe('consolidation ownership evidence', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('sends exact percentage points and current revision, then fences an unknown outcome until exact saved recovery', async () => {
    const data = signal(decodeOwnershipWorkspace(payload()));
    const resource = { data, loading: signal(false), error: signal(''), reload: vi.fn() };
    const api = { resource: vi.fn(() => resource),
      command: vi.fn().mockResolvedValue({ ok: false, unknown: true, code: 'outcome.unknown', status: 0, message: 'Unknown outcome.' }),
      get: vi.fn() };
    TestBed.configureTestingModule({ imports: [ConsolidationOwnership], providers: [
      { provide: Api, useValue: api }, { provide: SessionService, useValue: { invalidation: signal(0) } },
    ] });
    const fixture = TestBed.createComponent(ConsolidationOwnership);
    fixture.componentRef.setInput('scopeId', scopeId);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.parentClientId = parentId; c.childClientId = childId; c.effectiveFrom = '2026-01-01';
    c.ownershipPercent = '73.123456'; c.economicInterestPercent = '66.500000';
    c.controlAssessment = 'CONTROLLED'; c.method = 'EVIDENCED-METHOD'; c.evidenceReference = 'OWNERSHIP-01'; c.reviewed = true;
    await c.addOwnership();
    expect(api.command).toHaveBeenCalledWith(`/api/ui/consolidation/scopes/${scopeId}/ownership-interests`,
      expect.objectContaining({ parentClientId: parentId, childClientId: childId, ownershipPercent: '73.123456',
        economicInterestPercent: '66.500000', expectedGroupRevision: 7, reviewed: true }), 'POST');
    c.reviewed = true;
    await c.addOwnership();
    expect(api.command).toHaveBeenCalledTimes(1);
    const saved = decodeOwnershipWorkspace({ ...payload(), total: 1, interests: [{
      id: edgeId, parentClientId: parentId, parentLegalName: 'Synthetic parent', childClientId: childId,
      childLegalName: 'Synthetic child', effectiveFrom: '2026-01-01', effectiveTo: null,
      ownershipPercent: '73.123456', economicInterestPercent: '66.5', controlAssessment: 'CONTROLLED',
      method: 'EVIDENCED-METHOD', evidenceReference: 'OWNERSHIP-01', status: 'APPROVED',
    }] });
    api.get.mockResolvedValue(saved);
    await c.recover();
    expect(c.cmd().uncertain()).toBe(false);
    expect(api.command).toHaveBeenCalledTimes(1);
    data.set(saved);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('73.123456%');
    expect(fixture.nativeElement.textContent).toContain('66.5%');
  });
  it('rejects a mutation projection for a stale scope and binary numeric percentages', () => {
    expect(() => decodeOwnershipWorkspace({ ...payload(), currentGroupRevision: 8 })).toThrow();
    expect(() => decodeOwnershipWorkspace({ ...payload(), interests: [{ id: edgeId,
      parentClientId: parentId, parentLegalName: 'Parent', childClientId: childId, childLegalName: 'Child',
      effectiveFrom: '2026-01-01', effectiveTo: null, ownershipPercent: 73.123456, economicInterestPercent: '66.5',
      controlAssessment: 'CONTROLLED', method: 'METHOD', evidenceReference: 'REF', status: 'APPROVED' }] })).toThrow();
  });
});
