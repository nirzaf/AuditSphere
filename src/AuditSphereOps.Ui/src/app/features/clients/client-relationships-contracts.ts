import { arr, bool, decode, DecodeError, Decoder, guid, instant, nullable, obj, str } from '../../core/decode';

const percentageDecoder: Decoder<string | null> = (v, p) => {
  if (v === null || v === undefined) return null;
  if (typeof v === 'number' && Number.isFinite(v)) return v.toFixed(2);
  if (typeof v === 'string' && !isNaN(Number(v))) return v;
  throw new DecodeError(`${p || 'response'}: expected percentage`);
};

export const clientRelationshipDecoder = obj({
  id: guid,
  primaryClientId: guid,
  primaryClientName: str(500),
  relatedClientId: guid,
  relatedClientName: str(500),
  relationshipKind: str(50),
  ownershipPercentage: percentageDecoder,
  effectiveFrom: nullable(str(50)),
  effectiveTo: nullable(str(50)),
  notes: nullable(str(2000)),
  createdAt: instant,
  isActive: bool,
});

export const clientHierarchyDecoder = obj({
  clientId: guid,
  legalName: str(500),
  taxRegistrationNumber: nullable(str(100)),
  entityType: nullable(str(100)),
  parents: arr(clientRelationshipDecoder, 50),
  subsidiaries: arr(clientRelationshipDecoder, 100),
  affiliates: arr(clientRelationshipDecoder, 100),
});

export const clientContactRoutingDecoder = obj({
  id: guid,
  clientContactId: guid,
  contactName: str(500),
  contactEmail: str(254),
  contactPhone: nullable(str(50)),
  contactRole: nullable(str(100)),
  contactTitle: nullable(str(100)),
  signatoryAuthority: nullable(str(100)),
  purpose: str(50),
  effectiveFrom: nullable(str(50)),
  effectiveTo: nullable(str(50)),
  isPrimaryForPurpose: bool,
  createdAt: instant,
  isActive: bool,
});

export const clientRoutingsListDecoder = arr(clientContactRoutingDecoder, 100);

export function decodeClientHierarchy(value: unknown) {
  return decode(clientHierarchyDecoder, value);
}

export function decodeClientRoutings(value: unknown) {
  return decode(clientRoutingsListDecoder, value);
}

export type ClientOrganizationHierarchy = ReturnType<typeof decodeClientHierarchy>;
export type ClientRelationship = ReturnType<typeof clientRelationshipDecoder>;
export type ClientContactRouting = ReturnType<typeof clientContactRoutingDecoder>;
