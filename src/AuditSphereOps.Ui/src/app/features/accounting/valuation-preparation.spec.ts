import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { ValuationPreparation } from './valuation-preparation';
import { decodeValuationLookup, decodeValuationPreparation, decodeValuationPreview, valuationEditableFields } from './valuation-preparation-contracts';
const id='11111111-1111-4111-8111-111111111111',other='22222222-2222-4222-8222-222222222222',root='/api/ui/accounting/reconciliations/'+id+'/valuation-preparation/ECL';
const view={id,clientId:id,clientName:'Scoped client',engagementId:id,engagementName:'Scoped audit',periodId:id,periodCode:'FY26',
  bookId:id,bookCode:'STAT',basis:'IFRS',currency:'QAR',area:'CASH',accountSelection:'1000',asOfDate:'2026-12-31',agingBasis:'',agingBucketRuleVersion:'',
  status:'RECONCILED',revision:1,supersedesReconciliationId:null,createdByUserId:id,createdAt:'2026-10-03T00:00:00Z',
  reviewedByUserId:null,reviewedAt:null,sourceKind:'TRIAL_BALANCE',sourceId:id,sourceHash:'d'.repeat(64),currentSourceHash:'d'.repeat(64),
  sourceAvailable:true,inputGeneration:1,currentGeneration:1,isStale:false,canReuseApprovedEvidence:false,sourceTotal:'100.123456',glTotal:'100.123456',residual:'0.000000',
  latestProof:{id,formulaVersion:'reconciliation-proof.v1',sourceTotal:'100.123456',glTotal:'100.123456',itemsSignedTotal:'0.000000',residual:'0.000000',
    isReconciled:true,itemCount:0,itemManifestDigest:'a'.repeat(64),sourceHash:'d'.repeat(64),inputGeneration:1,createdByUserId:id,createdAt:'2026-10-03T00:00:00Z',matchesCurrentInputs:true},
  blockers:[],items:[],itemCount:0,page:0,hasMore:false,reviewBasis:'b'.repeat(64)};
const state={kind:'ECL',context:view,reviewBasis:'f'.repeat(64),canPrepare:true,blockers:[]};
const fields={method:'PROVISION_MATRIX_V1',methodologyVersion:'synthetic.ecl.v1',assumptionsHash:'a'.repeat(64),probabilityOfDefault:'0.100000',lossGivenDefault:'0.500000',
  managementOverlay:'2.000000',managementExpectedLoss:'8.000000',bookedAmount:'8.000000',quantity:'',unitCost:'',nrvPerUnit:'',obsolescenceReserve:'',bookAmount:'',reason:'Synthetic source rationale',evidenceReference:'Synthetic evidence'};
const preview={kind:'ECL',reconciliationId:id,reviewBasis:state.reviewBasis,requestHash:'e'.repeat(64),method:'PROVISION_MATRIX_V1',eligibleExposure:'100.123456',calculatedAmount:'7.006173',bookedAmount:'8.000000',difference:'-0.993827',currency:'QAR',canProceed:true,blockers:[]};
const receipt=(requestId:string)=>({id,requestId,requestHash:preview.requestHash,reconciliationId:id,kind:'ECL',evidenceId:id,actorId:other,reason:fields.reason,evidenceReference:fields.evidenceReference,createdAt:'2026-10-03T00:00:00Z'});
describe('reviewed native valuation preparation',()=>{
  let http:HttpTestingController;let params:BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(()=>{sessionStorage.clear();params=new BehaviorSubject(convertToParamMap({id,kind:'ECL'}));TestBed.configureTestingModule({imports:[ValuationPreparation],providers:[provideHttpClient(),provideHttpClientTesting(),provideRouter([]),{provide:ActivatedRoute,useValue:{paramMap:params}}]});
    http=TestBed.inject(HttpTestingController);TestBed.inject(SessionService).current.set({userId:other,firmId:id,generation:'1',staff:true});});
  afterEach(()=>{try{http.verify({ignoreCancelled:true});}finally{TestBed.resetTestingModule();sessionStorage.clear();}});
  function open(){const f=TestBed.createComponent(ValuationPreparation);f.detectChanges();TestBed.tick();http.expectOne(root).flush(state);TestBed.tick();return{f,c:f.componentInstance};}
  async function prepare(c:ValuationPreparation){c.model.set({...fields});TestBed.tick();const task=c.prepare();http.expectOne(root+'/preview').flush({value:preview});await task;TestBed.tick();}
  it('has no zero defaults and requires preview plus fresh assent for exactly one write',async()=>{
    const {c}=open();expect(c.valid()).toBe(false);expect(c.model().managementOverlay).toBe('');await c.execute();http.expectNone(r=>r.method==='POST');
    await prepare(c);await c.execute();http.expectNone(root);c.reviewed.set(true);const task=c.execute();await c.execute();const r=http.expectOne(root);
    expect(r.request.body.fields.bookedAmount).toBe('8.000000');expect(r.request.body.fields.quantity).toBeNull();expect(r.request.body.reviewed).toBe(true);
    r.flush({value:receipt(r.request.body.requestId)});await task;http.expectOne(root).flush(state);TestBed.tick();expect(c.receipt()).toBeTruthy();expect(c.uncertain()).toBe(false);
  });
  it('invalidates assent and preview after an edited input and stores editable fields only',async()=>{
    const {c}=open();await prepare(c);c.reviewed.set(true);c.model.update(m=>({...m,managementOverlay:'3.000000'}));TestBed.tick();expect(c.preview()).toBeNull();expect(c.reviewed()).toBe(false);
    expect(c.saveDraft()).toBe(true);expect(sessionStorage.getItem(sessionStorage.key(0)!)).not.toContain('"reviewed"');expect(valuationEditableFields({...fields,reviewed:true})).toBeNull();
  });
  it('fences an unknown outcome until the actor-owned preparation receipt is acknowledged',async()=>{
    const {c}=open();await prepare(c);c.reviewed.set(true);const task=c.execute();const r=http.expectOne(root);const key=r.request.body.requestId;r.flush({}, {status:503,statusText:'Unavailable'});await task;
    expect(c.uncertain()).toBe(true);expect(await c.confirmNavigation()).toBe(false);await c.execute();http.expectNone(root);
    const check=c.reconcile();http.expectOne(root+'/receipts/'+key+'?requestHash='+preview.requestHash).flush({found:true,receipt:receipt(key)});await check;expect(c.uncertain()).toBe(true);
    c.acknowledge();http.expectOne(root).flush(state);TestBed.tick();expect(c.uncertain()).toBe(false);expect(c.model().bookedAmount).toBe('');
  });
  it('refuses dispatch when the recovery checkpoint cannot be stored',async()=>{
    const {c}=open();await prepare(c);c.reviewed.set(true);const storage=vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new Error('Unavailable');});await c.execute();storage.mockRestore();
    http.expectNone(root);expect(c.message()).toContain('No valuation was sent');
  });
  it('ignores a late receipt after in-place route navigation',async()=>{
    const {c}=open();await prepare(c);c.reviewed.set(true);const task=c.execute();const r=http.expectOne(root);params.next(convertToParamMap({id:other,kind:'INVENTORY'}));TestBed.tick();
    r.flush({value:receipt(r.request.body.requestId)});await task;expect(c.receipt()).toBeNull();expect(c.preview()).toBeNull();
    http.expectOne('/api/ui/accounting/reconciliations/'+other+'/valuation-preparation/INVENTORY').flush({}, {status:403,statusText:'Denied'});
  });
  it('clears source amounts and editable fields after session invalidation',()=>{
    const {f,c}=open();c.model.set(fields);TestBed.inject(SessionService).clear();TestBed.tick();expect(c.data.data()).toBeNull();expect(c.model().reason).toBe('');expect(f.nativeElement.textContent).not.toContain('100.123456');
  });
  it('rejects stale preparation claims, unsupported methods and incoherent receipt states',()=>{
    expect(()=>decodeValuationPreparation({...state,context:{...view,isStale:true}})).toThrow();expect(()=>decodeValuationPreview({...preview,method:'UNSUPPORTED'})).toThrow();
    expect(()=>decodeValuationLookup({found:true,receipt:null})).toThrow();expect(()=>decodeValuationPreview({...preview,calculatedAmount:7.006173})).toThrow();
  });
});
