import { arr, date, guid, nat, obj, text, bool } from '../../core/decode';

export const decodeMaintenance = obj({ clients: arr(obj({ id: guid, name: text }), 5000),
  periods: arr(obj({ id: guid, clientId: guid, clientName: text, periodCode: text, startDate: date, endDate: date, status: text, revision: nat, priorPeriodCode: text }), 20000),
  restatements: arr(obj({ id: guid, clientId: guid, clientName: text, periodCode: text, revisedBasis: text, status: text, evidenceReference: text, canReview: bool }), 20000) });
export const decodeClosedPeriods = arr(obj({ id: guid, periodCode: text, startDate: date, endDate: date, basis: text, currency: text, revision: nat,
  packages: arr(obj({ id: guid, currency: text, hashPrefix: text }), 500) }), 2000);
export type ClosedPeriod = ReturnType<typeof decodeClosedPeriods>[number];
