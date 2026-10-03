import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { decodeReconciliation, ReconciliationReview } from './reconciliation';

const id='11111111-1111-4111-8111-111111111111', other='22222222-2222-4222-8222-222222222222';
const view={id,clientId:id,clientName:'Scoped client',engagementId:id,engagementName:'Scoped audit',periodId:id,periodCode:'FY26',
  bookId:id,bookCode:'STAT',basis:'IFRS',currency:'QAR',area:'CASH',accountSelection:'1000',asOfDate:'2026-12-31',agingBasis:'',agingBucketRuleVersion:'',
  status:'APPROVED',revision:1,supersedesReconciliationId:null,createdByUserId:id,createdAt:'2026-10-03T00:00:00Z',
  reviewedByUserId:other,reviewedAt:'2026-10-03T00:00:00Z',sourceKind:'TRIAL_BALANCE',sourceId:id,sourceHash:'d'.repeat(64),currentSourceHash:'d'.repeat(64),
  sourceAvailable:true,inputGeneration:1,currentGeneration:1,isStale:false,canReuseApprovedEvidence:true,sourceTotal:'100.123456',glTotal:'100.123456',residual:'0.000000',
  latestProof:{id,formulaVersion:'reconciliation-proof.v1',sourceTotal:'100.123456',glTotal:'100.123456',itemsSignedTotal:'0.000000',residual:'0.000000',
    isReconciled:true,itemCount:0,itemManifestDigest:'a'.repeat(64),sourceHash:'d'.repeat(64),inputGeneration:1,createdByUserId:id,createdAt:'2026-10-03T00:00:00Z',matchesCurrentInputs:true},
  blockers:[],items:[],itemCount:0,page:0,hasMore:false,reviewBasis:'b'.repeat(64)};

describe('native reconciliation evidence',()=>{
  let http:HttpTestingController;
  function setup(){const params=new BehaviorSubject(convertToParamMap({id}));TestBed.configureTestingModule({imports:[ReconciliationReview],providers:[
    provideHttpClient(),provideHttpClientTesting(),provideRouter([]),{provide:ActivatedRoute,useValue:{paramMap:params}}]});
    http=TestBed.inject(HttpTestingController);TestBed.inject(SessionService).current.set({userId:other,firmId:id,generation:'1',staff:true});return params;}
  afterEach(()=>{http?.verify({ignoreCancelled:true});TestBed.resetTestingModule();});
  it('validates complete item counts, exact money and current proof eligibility',()=>{
    expect(decodeReconciliation(view).sourceTotal).toBe('100.123456');
    expect(()=>decodeReconciliation({...view,sourceTotal:100.123456})).toThrow();
    expect(()=>decodeReconciliation({...view,itemCount:1})).toThrow();
    expect(()=>decodeReconciliation({...view,currentSourceHash:'e'.repeat(64)})).toThrow();
    expect(()=>decodeReconciliation({...view,latestProof:{...view.latestProof,residual:'1.000000'}})).toThrow();
    expect(()=>decodeReconciliation({...view,reviewedByUserId:id})).toThrow();
  });
  it('keeps retained approved amounts visible beside explicit stale blockers',()=>{
    setup();const f=TestBed.createComponent(ReconciliationReview);f.detectChanges();http.expectOne(`/api/ui/accounting/reconciliations/${id}?page=0`).flush({
      ...view,isStale:true,canReuseApprovedEvidence:false,currentGeneration:2,latestProof:{...view.latestProof,matchesCurrentInputs:false},blockers:['Generation changed; new review required.']});
    f.detectChanges();expect(f.nativeElement.textContent).toContain('100.123456');expect(f.nativeElement.textContent).toContain('Evidence reuse blocked');
    expect(f.nativeElement.textContent).toContain('Does not match current inputs');expect(f.nativeElement.querySelector('form')).toBeNull();
  });
  it('clears old evidence on same-component navigation and refuses a mismatched target',()=>{
    const params=setup();const f=TestBed.createComponent(ReconciliationReview);f.detectChanges();http.expectOne(`/api/ui/accounting/reconciliations/${id}?page=0`).flush(view);f.detectChanges();
    expect(f.nativeElement.textContent).toContain('100.123456');params.next(convertToParamMap({id:other}));f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('100.123456');http.expectOne(`/api/ui/accounting/reconciliations/${other}?page=0`).flush(view);f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('100.123456');expect(f.componentInstance.data.error()).toBeTruthy();
  });
  it('cancels protected inspection when the session ends',()=>{
    setup();const f=TestBed.createComponent(ReconciliationReview);f.detectChanges();const request=http.expectOne(`/api/ui/accounting/reconciliations/${id}?page=0`);
    TestBed.inject(SessionService).clear();f.detectChanges();expect(request.cancelled).toBe(true);expect(f.nativeElement.textContent).not.toContain('100.123456');
  });
  it('renders missing proof and generation explicitly without an inferred zero result',()=>{
    setup();const f=TestBed.createComponent(ReconciliationReview);f.detectChanges();http.expectOne(`/api/ui/accounting/reconciliations/${id}?page=0`).flush({
      ...view,latestProof:null,currentGeneration:null,isStale:true,canReuseApprovedEvidence:false,blockers:['Current generation unavailable.']});f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No calculated proof is retained');expect(f.nativeElement.textContent).toContain('Unavailable');
  });
});
