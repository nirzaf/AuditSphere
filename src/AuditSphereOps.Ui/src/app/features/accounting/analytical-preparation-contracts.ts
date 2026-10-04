import { arr, bool, dec, guid, instant, obj, sha256, str } from '../../core/decode';

const period = obj({ id: guid, code: str(100), startDate: str(10), endDate: str(10), currency: str(3), status: str(30),
  revision: (raw: unknown, path: string) => { if (typeof raw !== 'number' || !Number.isSafeInteger(raw) || raw < 0) throw new Error(`Invalid ${path}`); return raw; },
  canPrepare: bool, blockers: arr(str(500), 20) });
export type AnalyticalPeriod = ReturnType<typeof period>;
const context = obj({ engagementId: guid, clientId: guid, clientName: str(200), engagementName: str(200), inputGeneration: (raw: unknown,path: string) => {
  if (typeof raw !== 'number' || !Number.isSafeInteger(raw) || raw < 1) throw new Error(`Invalid ${path}`); return raw;
}, periods: arr(period, 100) });
export const decodeAnalyticalContext = context;
const state = obj({ engagementId: guid, clientId: guid, periodId: guid, comparisonPeriodId: (raw: unknown,path:string) => raw===null?null:guid(raw,path),
  currency: str(3), inputGeneration: (raw:unknown,path:string)=>{if(typeof raw!=='number'||!Number.isSafeInteger(raw)||raw<1)throw new Error(`Invalid ${path}`);return raw;},
  reviewBasis: sha256, canPrepare: bool, blockers: arr(str(500), 20) });
export const decodeAnalyticalState = state;
const preview = obj({ engagementId: guid, clientId: guid, periodId: guid,
  comparisonPeriodId: (raw: unknown,path:string) => raw===null?null:guid(raw,path), requestId: guid, reviewBasis: sha256,
  requestHash: sha256, area: str(80), measure: str(100), currentAmount: dec, priorAmount: dec,
  budgetAmount: (raw:unknown,path:string)=>raw===null?null:dec(raw,path), varianceRatio: (raw:unknown,path:string)=>raw===null?null:dec(raw,path),
  currency: str(3), resultStatus: str(30), canProceed: bool, blockers: arr(str(500), 20) });
export const decodeAnalyticalPreview = preview;
const result = obj({ evidenceId: guid, kind: (raw:unknown,path:string)=>{if(raw!=='ANALYTICAL')throw new Error(`Invalid ${path}`);return raw;},
  status: str(30), area: str(80), measure: str(100), currentAmount: dec, priorAmount: dec,
  varianceRatio: (raw:unknown,path:string)=>raw===null?null:dec(raw,path), actorId: guid, createdAt: instant });
const receipt = obj({ id: guid, requestId: guid, requestHash: sha256, evidenceId: guid, actorId: guid,
  reason: str(4000), evidenceReference: str(2000), createdAt: instant, result });
export const decodeAnalyticalReceipt = receipt;
const lookup = obj({ found: bool, receipt: (raw:unknown,path:string)=>raw===null?null:receipt(raw,path) });
export function decodeAnalyticalLookup(raw:unknown,path='response') { const r=lookup(raw,path); if(r.found!==(r.receipt!==null))throw new Error('Invalid receipt state'); return r; }
export const analyticalEditableFields = (raw:unknown) => {
  try {
    if(!raw||typeof raw!=='object'||Array.isArray(raw)||Object.keys(raw).length!==11)return null;
    const f=obj({area:str(80),measure:str(100),currentAmount:str(21),priorAmount:str(21),budgetAmount:str(21),denominatorBasis:str(200),
      formulaVersion:str(100),explanation:str(4000),seasonalityExplanation:str(2000),reason:str(4000),evidenceReference:str(2000)})(raw,'fields');
    return f;
  } catch { return null; }
};
export type AnalyticalEditableFields = NonNullable<ReturnType<typeof analyticalEditableFields>>;
