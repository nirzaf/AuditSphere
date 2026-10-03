import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AnalysisReview, decodeAnalysisReview } from './analysis-review';

const id='11111111-1111-4111-8111-111111111111',other='22222222-2222-4222-8222-222222222222';
const view={id,kind:'ECL',clientId:id,clientName:'Scoped synthetic client',engagementId:id,engagementName:'Scoped audit',periodId:id,periodCode:'FY26',basis:'IFRS',currency:'QAR',
  area:'CASH',status:'APPROVED',version:1,inputGeneration:1,currentGeneration:1,inputsCurrent:true,
  source:{kind:'TRIAL_BALANCE',id,reconciliationId:id,retainedDigest:'a'.repeat(64),currentDigest:'a'.repeat(64),currentChecksPass:true,description:'Exact reconciliation and current source checks.'},
  assumptionsDigest:'b'.repeat(64),replayDigest:null,replayMatchesInputs:null,proposedJournalId:null,createdByUserId:id,createdAt:'2026-10-03T00:00:00Z',reviewedByUserId:other,reviewedAt:'2026-10-03T00:00:00Z',
  amounts:[{key:'CALCULATED',label:'Retained expected loss',value:'7.006173',unit:'REPORTING_CURRENCY'},{key:'PD',label:'Probability of default',value:'0.000000',unit:'RATIO'}],
  details:[{key:'METHOD',label:'Method',value:'PROVISION_MATRIX_V1'}],blockers:[],links:[],linkCount:0,page:0,hasMore:false,hasCurrentReviewedProcedure:false,reviewBasis:'c'.repeat(64)};
describe('native analysis evidence inspection',()=>{
  let http:HttpTestingController;
  function setup(){const params=new BehaviorSubject(convertToParamMap({id,kind:'ECL'}));TestBed.configureTestingModule({imports:[AnalysisReview],providers:[provideHttpClient(),provideHttpClientTesting(),provideRouter([]),
    {provide:ActivatedRoute,useValue:{paramMap:params}}]});http=TestBed.inject(HttpTestingController);TestBed.inject(SessionService).current.set({userId:other,firmId:id,generation:'1',staff:true});return params;}
  afterEach(()=>{http?.verify({ignoreCancelled:true});TestBed.resetTestingModule();});
  it('retains exact money and valid zero inputs and rejects numeric or incoherent responses',()=>{
    expect(decodeAnalysisReview(view).amounts[0].value).toBe('7.006173');expect(decodeAnalysisReview(view).amounts[1].value).toBe('0.000000');
    expect(()=>decodeAnalysisReview({...view,amounts:[{...view.amounts[0],value:7.006173}]})).toThrow();
    expect(()=>decodeAnalysisReview({...view,currentGeneration:2})).toThrow();expect(()=>decodeAnalysisReview({...view,source:{...view.source,currentDigest:'d'.repeat(64)}})).toThrow();
    expect(()=>decodeAnalysisReview({...view,linkCount:1})).toThrow();expect(()=>decodeAnalysisReview({...view,kind:'UNKNOWN'})).toThrow();
  });
  it('renders historical amounts beside stale blockers without an approval control',()=>{
    setup();const f=TestBed.createComponent(AnalysisReview);f.detectChanges();http.expectOne(`/api/ui/accounting/evidence/ECL/${id}?page=0`).flush({...view,inputsCurrent:false,currentGeneration:2,
      source:{...view.source,currentChecksPass:false},blockers:['Source changed; prepare new evidence.']});f.detectChanges();
    expect(f.nativeElement.textContent).toContain('7.006173');expect(f.nativeElement.textContent).toContain('Current verification blockers');expect(f.nativeElement.querySelector('form')).toBeNull();
  });
  it('clears previous detail on kind and identity changes and rejects a mismatched response',()=>{
    const params=setup();const f=TestBed.createComponent(AnalysisReview);f.detectChanges();http.expectOne(`/api/ui/accounting/evidence/ECL/${id}?page=0`).flush(view);f.detectChanges();
    params.next(convertToParamMap({id:other,kind:'INVENTORY'}));f.detectChanges();expect(f.nativeElement.textContent).not.toContain('7.006173');
    http.expectOne(`/api/ui/accounting/evidence/INVENTORY/${other}?page=0`).flush(view);f.detectChanges();expect(f.componentInstance.data.error()).toBeTruthy();expect(f.nativeElement.textContent).not.toContain('7.006173');
  });
  it('cancels protected reads and clears rendered evidence when the session ends',()=>{
    setup();const f=TestBed.createComponent(AnalysisReview);f.detectChanges();const r=http.expectOne(`/api/ui/accounting/evidence/ECL/${id}?page=0`);
    TestBed.inject(SessionService).clear();f.detectChanges();expect(r.cancelled).toBe(true);expect(f.nativeElement.textContent).not.toContain('7.006173');
  });
  it('refuses unsupported kinds before issuing a request',()=>{
    const p=setup();p.next(convertToParamMap({id,kind:'UNKNOWN'}));const f=TestBed.createComponent(AnalysisReview);f.detectChanges();http.expectNone(x=>x.url.includes('/accounting/evidence/'));
    expect(f.nativeElement.textContent).toContain('identity is unsupported');
  });
  it('does not claim source verification for a legacy risk flag',()=>{
    const risk={...view,kind:'JOURNAL_RISK',version:null,inputGeneration:null,inputsCurrent:false,source:{...view.source,kind:'GENERAL_LEDGER',reconciliationId:null,retainedDigest:null,currentChecksPass:false},blockers:['Original digest and generation are not retained.']};
    expect(decodeAnalysisReview(risk).inputsCurrent).toBe(false);expect(()=>decodeAnalysisReview({...risk,inputsCurrent:true})).toThrow();
    expect(()=>decodeAnalysisReview({...risk,source:{...risk.source,currentChecksPass:true}})).toThrow();
  });
  it('shows a missing analytical ratio as unavailable without a fabricated zero',()=>{
    setup();const r={...view,kind:'ANALYTICAL',source:{...view.source,kind:'GENERATION_BOUND',id:null,reconciliationId:null,retainedDigest:null,currentDigest:null},inputsCurrent:false,
      replayDigest:'e'.repeat(64),replayMatchesInputs:true,amounts:[{key:'RATIO',label:'Retained movement ratio',value:null,unit:'RATIO'}],blockers:['No denominator is retained.']};
    expect(decodeAnalysisReview(r).amounts[0].value).toBeNull();expect(()=>decodeAnalysisReview({...r,replayDigest:null})).toThrow();
  });
});
