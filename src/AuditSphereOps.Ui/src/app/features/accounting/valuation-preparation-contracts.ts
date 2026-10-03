import { arr, bool, dec, guid, instant, obj, oneOf, sha256, str } from '../../core/decode';
import { decodeReconciliation } from './reconciliation';

export const eclAmounts = ['probabilityOfDefault','lossGivenDefault','managementOverlay','managementExpectedLoss','bookedAmount'] as const;
export const inventoryAmounts = ['quantity','unitCost','nrvPerUnit','obsolescenceReserve','bookAmount'] as const;
export const allValuationAmounts = [...eclAmounts,...inventoryAmounts] as const;
export const valuationLabels: Record<typeof allValuationAmounts[number],string> = {
  probabilityOfDefault:'Probability of default', lossGivenDefault:'Loss given default', managementOverlay:'Management overlay',
  managementExpectedLoss:'Management expected loss', bookedAmount:'Booked amount', quantity:'Quantity', unitCost:'Unit cost',
  nrvPerUnit:'NRV per unit', obsolescenceReserve:'Obsolescence reserve', bookAmount:'Book amount',
};
const fields = obj({ method:str(100), methodologyVersion:str(100), assumptionsHash:str(64), probabilityOfDefault:str(21), lossGivenDefault:str(21),
  managementOverlay:str(21), managementExpectedLoss:str(21), bookedAmount:str(21), quantity:str(21), unitCost:str(21), nrvPerUnit:str(21),
  obsolescenceReserve:str(21), bookAmount:str(21), reason:str(4000), evidenceReference:str(2000) });
export type ValuationEditableFields = ReturnType<typeof fields>;
export function valuationEditableFields(raw:unknown):ValuationEditableFields|null {
  try { if(!raw || typeof raw!=='object' || Array.isArray(raw) || Object.keys(raw).length!==15)return null;
    return fields(raw,'fields'); } catch { return null; }
}
const state = obj({ kind:oneOf('ECL','INVENTORY'), context:decodeReconciliation, reviewBasis:sha256,
  canPrepare:bool, blockers:arr(str(2000),25) });
export function decodeValuationPreparation(raw:unknown,path='response') {
  const s=state(raw,path),c=s.context;
  if(c.page!==0 || (s.canPrepare && (s.blockers.length || c.blockers.length || c.status!=='RECONCILED' || c.isStale ||
    !c.latestProof?.matchesCurrentInputs || !c.latestProof.isReconciled || c.currentGeneration!==c.inputGeneration))) throw new Error('Unsupported valuation context');
  return s;
}
const preview=obj({ kind:oneOf('ECL','INVENTORY'), reconciliationId:guid, reviewBasis:sha256, requestHash:sha256,
  method:oneOf('PROVISION_MATRIX_V1','LOWER_COST_NRV_V1'), eligibleExposure:dec, calculatedAmount:dec, bookedAmount:dec,
  difference:dec, currency:str(3), canProceed:bool, blockers:arr(str(2000),25) });
export function decodeValuationPreview(raw:unknown,path='response') {
  const p=preview(raw,path);
  if((p.kind==='ECL' ? p.method!=='PROVISION_MATRIX_V1' : p.method!=='LOWER_COST_NRV_V1') || (p.canProceed && p.blockers.length)) throw new Error('Unsupported valuation preview');
  return p;
}
export const decodeValuationReceipt=obj({ id:guid, requestId:guid, requestHash:sha256, reconciliationId:guid, kind:oneOf('ECL','INVENTORY'),
  evidenceId:guid, actorId:guid, reason:str(4000), evidenceReference:str(2000), createdAt:instant });
const lookup=obj({ found:bool, receipt:(raw:unknown,path:string)=>raw===null?null:decodeValuationReceipt(raw,path) });
export function decodeValuationLookup(raw:unknown,path='response') { const p=lookup(raw,path);if(p.found!==(p.receipt!==null))throw new Error('Unsupported receipt state');return p; }
