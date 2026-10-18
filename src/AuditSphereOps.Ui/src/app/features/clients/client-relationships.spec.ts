import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import { SessionService } from '../../core/session';
import { ClientRelationships } from './client-relationships';
import {
  decodeClientHierarchy,
  decodeClientRoutings,
} from './client-relationships-contracts';

const clientId = '11111111-1111-4111-8111-111111111111';
const parentId = '22222222-2222-4222-8222-222222222222';
const subId = '33333333-3333-4333-8333-333333333333';
const contactId = '44444444-4444-4444-8444-444444444444';
const relId1 = '55555555-5555-4555-8555-555555555555';
const relId2 = '66666666-6666-4666-8666-666666666666';
const routeId = '77777777-7777-4777-8777-777777777777';

const mockHierarchy = {
  clientId,
  legalName: 'Apex Holding Group Q.P.S.C.',
  taxRegistrationNumber: 'QA-TIN-987654321',
  entityType: 'Q.P.S.C.',
  parents: [
    {
      id: relId1,
      primaryClientId: parentId,
      primaryClientName: 'Global Supreme Corp',
      relatedClientId: clientId,
      relatedClientName: 'Apex Holding Group Q.P.S.C.',
      relationshipKind: 'PARENT',
      ownershipPercentage: '51.00',
      effectiveFrom: '2024-01-01',
      effectiveTo: null,
      notes: 'Majority controlling interest',
      createdAt: '2024-01-01T00:00:00Z',
      isActive: true,
      effectiveStatus: 'ACTIVE' as const,
    },
  ],
  subsidiaries: [
    {
      id: relId2,
      primaryClientId: clientId,
      primaryClientName: 'Apex Holding Group Q.P.S.C.',
      relatedClientId: subId,
      relatedClientName: 'Apex Logistics W.L.L.',
      relationshipKind: 'PARENT',
      ownershipPercentage: '100.00',
      effectiveFrom: '2025-01-01',
      effectiveTo: null,
      notes: 'Wholly owned subsidiary',
      createdAt: '2025-01-01T00:00:00Z',
      isActive: true,
      effectiveStatus: 'ACTIVE' as const,
    },
  ],
  affiliates: [
    {
      id: '88888888-8888-4888-8888-888888888888',
      primaryClientId: clientId,
      primaryClientName: 'Apex Holding Group Q.P.S.C.',
      relatedClientId: '99999999-9999-4999-8999-999999999999',
      relatedClientName: 'Future Affiliate',
      relationshipKind: 'AFFILIATE',
      ownershipPercentage: null,
      effectiveFrom: '2099-01-01',
      effectiveTo: null,
      notes: null,
      createdAt: '2026-01-01T00:00:00Z',
      isActive: true,
      effectiveStatus: 'FUTURE' as const,
    },
    {
      id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      primaryClientId: clientId,
      primaryClientName: 'Apex Holding Group Q.P.S.C.',
      relatedClientId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
      relatedClientName: 'Former Affiliate',
      relationshipKind: 'AFFILIATE',
      ownershipPercentage: null,
      effectiveFrom: null,
      effectiveTo: '2020-01-01',
      notes: null,
      createdAt: '2020-01-01T00:00:00Z',
      isActive: true,
      effectiveStatus: 'EXPIRED' as const,
    },
    {
      id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
      primaryClientId: clientId,
      primaryClientName: 'Apex Holding Group Q.P.S.C.',
      relatedClientId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
      relatedClientName: 'Revoked Affiliate',
      relationshipKind: 'AFFILIATE',
      ownershipPercentage: null,
      effectiveFrom: null,
      effectiveTo: null,
      notes: null,
      createdAt: '2020-01-01T00:00:00Z',
      isActive: false,
      effectiveStatus: 'REVOKED' as const,
    },
  ],
};

const mockRoutings = [
  {
    id: routeId,
    clientContactId: contactId,
    contactName: 'Nasser Al-Thani',
    contactEmail: 'nasser@apex.qa',
    contactPhone: '+974 4400 1122',
    contactRole: 'Chief Financial Officer',
    contactTitle: 'CFO & Managing Director',
    signatoryAuthority: 'FULL_COMMERCIAL_AND_AUDIT',
    purpose: 'FINANCE',
    effectiveFrom: '2026-01-01',
    effectiveTo: null,
    isPrimaryForPurpose: true,
    createdAt: '2026-01-01T00:00:00Z',
    isActive: true,
  },
];

const mockClientWorkspace = {
  id: clientId, name: 'Apex Holding Group Q.P.S.C.', status: 'Active',
  commercialName: null, registrationNumber: 'CR-2026-0042', jurisdiction: 'Qatar',
  createdAt: '2026-01-01T00:00:00Z', safetyGeneration: '1', canManageContacts: true, canCreateEngagement: false,
  metrics: { engagements: 0, workBlocked: 0, contacts: 1 },
  paging: { engagementPage: 0, engagementPageSize: 10, contactPage: 0, contactPageSize: 50 },
  engagements: [],
  contacts: [{ id: contactId, name: 'Nasser Al-Thani', email: 'nasser@apex.qa', role: 'Chief Financial Officer', primary: true }],
  portalIntent: null,
};
const mockCandidates = {
  items: [{ id: parentId, name: 'Global Supreme Corp' }, { id: subId, name: 'Apex Logistics W.L.L.' }],
  total: 2, page: 0, pageSize: 50,
};
const mockResolution = {
  status: 'Resolved', clientContactId: contactId, fullName: 'Nasser Al-Thani',
  email: 'nasser@apex.qa', purpose: 'COMMERCIAL', message: 'Recipient resolved successfully.',
};

function flushWorkspace(httpMock: HttpTestingController, fixture: ComponentFixture<ClientRelationships>, routings: Record<string, unknown>[] = mockRoutings,
  resolutionPurpose = 'COMMERCIAL'): void {
  httpMock.expectOne(`/api/ui/clients/${clientId}/hierarchy`).flush(mockHierarchy);
  httpMock.expectOne(`/api/ui/clients/${clientId}/routings`).flush(routings);
  httpMock.expectOne(`/api/ui/clients/${clientId}?contactPage=0&contactPageSize=50`).flush(mockClientWorkspace);
  httpMock.expectOne(`/api/ui/clients/${clientId}/relationship-candidates?page=0&pageSize=50`).flush(mockCandidates);
  httpMock.expectOne(request => request.urlWithParams.startsWith(
    `/api/ui/clients/${clientId}/routings/resolve?purpose=${resolutionPurpose}&asOfDate=`)).flush(mockResolution);
  fixture.detectChanges();
}

describe('Client Relationships Contracts', () => {
  it('decodes valid hierarchy and routing payloads correctly', () => {
    const h = decodeClientHierarchy(mockHierarchy);
    expect(h.clientId).toBe(clientId);
    expect(h.legalName).toBe('Apex Holding Group Q.P.S.C.');
    expect(h.parents.length).toBe(1);
    expect(h.parents[0].primaryClientName).toBe('Global Supreme Corp');
    expect(h.subsidiaries.length).toBe(1);
    expect(h.subsidiaries[0].relatedClientName).toBe('Apex Logistics W.L.L.');
    expect(h.subsidiaries[0].ownershipPercentage).toBe('100.00');
    expect(h.affiliates.map(x => x.effectiveStatus)).toEqual(['FUTURE', 'EXPIRED', 'REVOKED']);

    const r = decodeClientRoutings(mockRoutings);
    expect(r.length).toBe(1);
    expect(r[0].contactName).toBe('Nasser Al-Thani');
    expect(r[0].purpose).toBe('FINANCE');
    expect(r[0].isPrimaryForPurpose).toBe(true);
  });

  it('rejects malformed hierarchy payloads', () => {
    expect(() => decodeClientHierarchy(null)).toThrow();
    expect(() => decodeClientHierarchy({})).toThrow();
    expect(() => decodeClientHierarchy({ ...mockHierarchy, clientId: 'bad-guid' })).toThrow();
  });
});

describe('ClientRelationships Component', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ClientRelationships],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({
      userId: clientId,
      firmId: clientId,
      generation: '1',
      staff: true,
    });
  });

  afterEach(() => {
    httpMock.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('loads and displays hierarchy, routings and authorized action choices', () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();

    flushWorkspace(httpMock, fixture);

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Apex Holding Group Q.P.S.C.');
    expect(text).toContain('QA-TIN-987654321');
    expect(text).toContain('CR-2026-0042');
    expect(text).toContain('Qatar');
    expect(text).toContain('Global Supreme Corp');
    expect(text).toContain('Apex Logistics W.L.L.');
    expect(text).toContain('Future Affiliate');
    expect(text).toContain('future');
    expect(text).toContain('Former Affiliate');
    expect(text).toContain('expired');
    expect(text).toContain('Revoked Affiliate');
    expect(text).toContain('revoked');
    expect(text).toContain('Nasser Al-Thani');
    expect(text).toContain('FINANCE');
    expect(text).toContain('Assign contact routing');
    expect(text).toContain('Add a client relationship');
    expect(text).toContain('Current recipient resolution');
  });

  it('keeps an unrevoked routing assignment revocable after its contact is deactivated', () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    flushWorkspace(httpMock, fixture, mockRoutings.map(routing => ({ ...routing, isActive: false })));

    const text = fixture.nativeElement.textContent.toLowerCase();
    expect(text).toContain('contact deactivated');
    expect(text).toContain('review revocation');
  });

  it('requires fresh confirmation after either reviewed draft changes', async () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    flushWorkspace(httpMock, fixture);

    const component = fixture.componentInstance;
    Object.assign(component.routingDraft, { clientContactId: contactId, purpose: 'FINANCE' });
    component.reviewRoutingAssignment();
    component.routingConfirmation = true;
    component.routingDraft.purpose = 'COMPLETION';
    component.reviewRoutingAssignment();
    expect(component.routingConfirmation).toBe(false);
    await component.confirmRoutingAssignment();
    httpMock.expectNone(`/api/ui/clients/${clientId}/routings`);

    Object.assign(component.relationshipDraft, { relatedClientId: subId, relationshipKind: 'AFFILIATE' });
    component.reviewRelationshipCreation();
    component.relationshipConfirmation = true;
    component.relationshipDraft.notes = 'Updated after review.';
    component.reviewRelationshipCreation();
    expect(component.relationshipConfirmation).toBe(false);
    await component.confirmRelationshipCreation();
    httpMock.expectNone(`/api/ui/clients/${clientId}/relationships`);
  });

  it('sends reviewed routing assignment and reasoned route-bound revocation payloads', async () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    flushWorkspace(httpMock, fixture);

    const component = fixture.componentInstance;
    Object.assign(component.routingDraft, {
      clientContactId: contactId, purpose: 'FINANCE', effectiveFrom: '2026-02-01', effectiveTo: '2026-12-31', isPrimaryForPurpose: true,
    });
    component.reviewRoutingAssignment();
    expect(component.routingReview()?.contactEmail).toBe('nasser@apex.qa');
    component.routingConfirmation = true;
    component.routingDraft.effectiveTo = '2027-01-31';
    component.reviewRoutingAssignment();
    expect(component.routingConfirmation).toBe(false);
    expect(component.routingReview()?.effectiveTo).toBe('2027-01-31');
    await component.confirmRoutingAssignment();
    httpMock.expectNone(`/api/ui/clients/${clientId}/routings`);
    component.routingConfirmation = true;
    const assignment = component.confirmRoutingAssignment();
    const assignRequest = httpMock.expectOne(`/api/ui/clients/${clientId}/routings`);
    expect(assignRequest.request.method).toBe('POST');
    expect(assignRequest.request.body).toEqual({
      clientContactId: contactId, purpose: 'FINANCE', effectiveFrom: '2026-02-01', effectiveTo: '2027-01-31', isPrimaryForPurpose: true,
    });
    assignRequest.flush({ id: routeId });
    await assignment;
    flushWorkspace(httpMock, fixture, mockRoutings, 'FINANCE');

    component.routingReasons[routeId] = 'Finance contact changed by client.';
    component.routingConfirmation = true;
    component.reviewRoutingRevocation(mockRoutings[0]);
    expect(component.routingRevokeReview()?.reason).toBe('Finance contact changed by client.');
    expect(component.routingConfirmation).toBe(false);
    component.routingConfirmation = true;
    const revocation = component.confirmRoutingRevocation();
    const revokeRequest = httpMock.expectOne(`/api/ui/clients/${clientId}/routings/${routeId}`);
    expect(revokeRequest.request.method).toBe('DELETE');
    expect(revokeRequest.request.body).toEqual({ reason: 'Finance contact changed by client.' });
    revokeRequest.flush({ success: true });
    await revocation;
    flushWorkspace(httpMock, fixture, mockRoutings, 'FINANCE');
    expect(component.routingCommand.message()).toContain('Routing was revoked');
  });

  it('sends reviewed relationship create/revoke payloads and presents domain cycle refusals', async () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    flushWorkspace(httpMock, fixture);

    const component = fixture.componentInstance;
    Object.assign(component.relationshipDraft, {
      relatedClientId: subId, relationshipKind: 'AFFILIATE', ownershipPercentage: '51.25',
      effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', notes: 'Reviewed association.',
    });
    component.reviewRelationshipCreation();
    expect(component.relationshipReview()?.ownershipPercentage).toBe('51.25');
    component.relationshipConfirmation = true;
    const refused = component.confirmRelationshipCreation();
    const refusedRequest = httpMock.expectOne(`/api/ui/clients/${clientId}/relationships`);
    expect(refusedRequest.request.method).toBe('POST');
    expect(refusedRequest.request.body).toEqual({
      relatedClientId: subId, relationshipKind: 'AFFILIATE', ownershipPercentage: 51.25,
      effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', notes: 'Reviewed association.',
    });
    refusedRequest.flush({ code: 'crm.relationship-cycle', message: 'Adding this relationship creates an ownership-tree cycle.' },
      { status: 400, statusText: 'Bad Request' });
    await refused;
    expect(component.relationshipCommand.message()).toContain('ownership-tree cycle');

    const created = component.confirmRelationshipCreation();
    const createRequest = httpMock.expectOne(`/api/ui/clients/${clientId}/relationships`);
    createRequest.flush({ id: relId1 });
    await created;
    flushWorkspace(httpMock, fixture);

    component.relationshipReasons[relId2] = 'Ownership structure changed.';
    component.relationshipConfirmation = true;
    component.reviewRelationshipRevocation(mockHierarchy.subsidiaries[0]);
    expect(component.relationshipRevokeReview()?.relatedClientName).toBe('Apex Logistics W.L.L.');
    expect(component.relationshipConfirmation).toBe(false);
    component.relationshipConfirmation = true;
    const revoked = component.confirmRelationshipRevocation();
    const revokeRequest = httpMock.expectOne(`/api/ui/clients/${clientId}/relationships/${relId2}`);
    expect(revokeRequest.request.method).toBe('DELETE');
    expect(revokeRequest.request.body).toEqual({ reason: 'Ownership structure changed.' });
    revokeRequest.flush({ success: true });
    await revoked;
    flushWorkspace(httpMock, fixture);
    expect(component.relationshipCommand.message()).toContain('relationship was revoked');
  });

  it('handles error states cleanly when requests fail', () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();

    const hierarchyReq = httpMock.expectOne(`/api/ui/clients/${clientId}/hierarchy`);
    const routingsReq = httpMock.expectOne(`/api/ui/clients/${clientId}/routings`);
    httpMock.expectOne(`/api/ui/clients/${clientId}?contactPage=0&contactPageSize=50`);

    routingsReq.flush(mockRoutings);
    hierarchyReq.flush({ message: 'Scope denied' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toContain('Scope denied');
    expect(fixture.nativeElement.textContent).toContain('Client relationships unavailable');
  });
});
