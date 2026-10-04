import { arr, bool, dec, date, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';

const period = obj({ id:guid, code:str(100), startDate:date, endDate:date, currency:str(3), basis:str(40), status:str(30), revision:nat });
const source = obj({ id:guid, kind:oneOf('TRIAL_BALANCE','GENERAL_LEDGER'), periodId:guid, periodCode:str(100),
  bookId:nullable(guid), currency:str(3), basis:str(40), rowCount:NumberDecoder, sourceHash:str(64), importedAt:instant });
function NumberDecoder(v:unknown,p:string):number { return typeof v==='number'&&Number.isSafeInteger(v)&&v>=0?v:(()=>{throw new Error(`${p}: expected count`)})(); }
export const decodeReconciliationContext=obj({ engagementId:guid, clientId:guid, clientName:str(300), engagementName:str(300),
  periods:arr(period,100), sources:arr(source,200) });
export const decodeReconciliationState=obj({ engagementId:guid, clientId:guid, clientName:str(300), engagementName:str(300),
  periodId:guid, periodCode:str(100), bookId:nullable(guid), currency:str(3), basis:str(40),
  sourceKind:oneOf('TRIAL_BALANCE','GENERAL_LEDGER'), sourceId:guid, sourceHash:sha256, inputGeneration:nat,
  reviewBasis:sha256, canPrepare:bool, blockers:arr(str(2000),25) });
export const decodeReconciliationPreview=obj({ engagementId:guid, clientId:guid, periodId:guid, requestId:guid,
  reviewBasis:sha256, requestHash:sha256, sourceKind:oneOf('TRIAL_BALANCE','GENERAL_LEDGER'), sourceId:guid,
  sourceHash:sha256, area:str(80), accountSelection:str(2000), sourceTotal:dec, glTotal:dec, residual:dec,
  currency:str(3), resultStatus:oneOf('RECONCILED','UNRECONCILED'), canProceed:bool, blockers:arr(str(2000),25) });
export const decodeReconciliationReceipt=obj({ id:guid, requestId:guid, requestHash:sha256, reconciliationId:guid,
  actorId:guid, reason:str(4000), evidenceReference:str(2000), createdAt:instant,
  result:obj({ reconciliationId:guid, revision:nat, supersedesId:nullable(guid), status:oneOf('RECONCILED','UNRECONCILED'),
    sourceTotal:dec, glTotal:dec, residual:dec, sourceHash:sha256, actorId:guid, createdAt:instant }) });
export const decodeReconciliationLookup=obj({ found:bool, receipt:nullable(decodeReconciliationReceipt) });

const specialistPeriod=obj({ id:guid, code:str(100), startDate:date, endDate:date, currency:str(3), status:str(30), revision:nat,
  canPrepare:bool, blockers:arr(str(2000),25) });
const specialistSchedule=obj({ id:guid, periodId:guid, periodCode:str(100), area:str(80), revision:nat,
  supersedesScheduleId:nullable(guid), methodologyVersion:str(100), status:str(30), calculatedAmount:dec, createdAt:instant });
export const decodeSpecialistContext=obj({ engagementId:guid, clientId:guid, clientName:str(300), engagementName:str(300),
  periods:arr(specialistPeriod,100), schedules:arr(specialistSchedule,100) });
export const decodeSpecialistState=obj({ engagementId:guid, clientId:guid, periodId:guid, periodCode:str(100), currency:str(3),
  inputGeneration:nat, area:str(80), currentScheduleId:nullable(guid), nextRevision:nat, reviewBasis:sha256,
  canPrepare:bool, blockers:arr(str(2000),25) });
export const decodeSpecialistPreview=obj({ engagementId:guid, clientId:guid, periodId:guid, area:str(80), requestId:guid,
  reviewBasis:sha256, requestHash:sha256, supersedesScheduleId:nullable(guid), revision:nat, currency:str(3),
  calculatedAmount:dec, managementAmount:dec, difference:dec, canProceed:bool, blockers:arr(str(2000),25) });
export const decodeSpecialistReceipt=obj({ id:guid, requestId:guid, requestHash:sha256, scheduleId:guid, actorId:guid,
  reason:str(4000), createdAt:instant, result:obj({ scheduleId:guid, area:str(80), revision:nat,
    supersedesScheduleId:nullable(guid), status:str(30), calculatedAmount:dec, managementAmount:dec,
    difference:dec, actorId:guid, createdAt:instant }) });
export const decodeSpecialistLookup=obj({ found:bool, receipt:nullable(decodeSpecialistReceipt) });

export type ReconciliationContext=ReturnType<typeof decodeReconciliationContext>;
export type ReconciliationSource=ReconciliationContext['sources'][number];
export type ReconciliationState=ReturnType<typeof decodeReconciliationState>;
export type ReconciliationPreview=ReturnType<typeof decodeReconciliationPreview>;
export type SpecialistContext=ReturnType<typeof decodeSpecialistContext>;
export type SpecialistState=ReturnType<typeof decodeSpecialistState>;
export type SpecialistPreview=ReturnType<typeof decodeSpecialistPreview>;
