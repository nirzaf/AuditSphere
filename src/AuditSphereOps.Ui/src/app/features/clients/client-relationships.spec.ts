import { TestBed } from '@angular/core/testing';
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
      ownershipPercentage: 100,
      effectiveFrom: '2025-01-01',
      effectiveTo: null,
      notes: 'Wholly owned subsidiary',
      createdAt: '2025-01-01T00:00:00Z',
      isActive: true,
    },
  ],
  affiliates: [],
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

  it('loads and displays hierarchy and routings successfully', () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();

    const hierarchyReq = httpMock.expectOne(`/api/ui/clients/${clientId}/hierarchy`);
    const routingsReq = httpMock.expectOne(`/api/ui/clients/${clientId}/routings`);

    expect(hierarchyReq.request.method).toBe('GET');
    expect(routingsReq.request.method).toBe('GET');

    hierarchyReq.flush(mockHierarchy);
    routingsReq.flush(mockRoutings);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Apex Holding Group Q.P.S.C.');
    expect(text).toContain('QA-TIN-987654321');
    expect(text).toContain('Global Supreme Corp');
    expect(text).toContain('Apex Logistics W.L.L.');
    expect(text).toContain('Nasser Al-Thani');
    expect(text).toContain('FINANCE');
  });

  it('handles error states cleanly when requests fail', () => {
    const fixture = TestBed.createComponent(ClientRelationships);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();

    const hierarchyReq = httpMock.expectOne(`/api/ui/clients/${clientId}/hierarchy`);
    const routingsReq = httpMock.expectOne(`/api/ui/clients/${clientId}/routings`);

    routingsReq.flush(mockRoutings);
    hierarchyReq.flush({ message: 'Scope denied' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toContain('Scope denied');
    expect(fixture.nativeElement.textContent).toContain('Client relationships unavailable');
  });
});
