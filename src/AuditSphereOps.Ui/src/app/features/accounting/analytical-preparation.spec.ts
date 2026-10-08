import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, ParamMap, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AnalyticalPreparation } from './analytical-preparation';
import { analyticalEditableFields, decodeAnalyticalLookup, decodeAnalyticalPreview, decodeAnalyticalState } from './analytical-preparation-contracts';

const id='11111111-1111-4111-8111-111111111111',user='22222222-2222-4222-8222-222222222222',period='33333333-3333-4333-8333-333333333333';
const base=`/api/ui/engagements/${id}/analytical-preparation`,basis='a'.repeat(64);
const context={engagementId:id,clientId:id,clientName:'Synthetic client',engagementName:'FY26 audit',inputGeneration:1,
  periods:[{id:period,code:'FY26',startDate:'2026-01-01',endDate:'2026-12-31',currency:'QAR',status:'ACTIVE',revision:1,canPrepare:true,blockers:[]}]};
const state={engagementId:id,clientId:id,periodId:period,comparisonPeriodId:null,currency:'QAR',inputGeneration:1,reviewBasis:basis,canPrepare:true,blockers:[]};
const fields={area:'Revenue',measure:'Annual movement',currentAmount:'120.123456',priorAmount:'100.000000',budgetAmount:'',denominatorBasis:'prior-year total',
  formulaVersion:'synthetic.v1',explanation:'Synthetic rationale.',seasonalityExplanation:'',reason:'Synthetic preparation reason.',evidenceReference:'SYNTHETIC-REF'};
const preview={engagementId:id,clientId:id,periodId:period,comparisonPeriodId:null,requestId:user,reviewBasis:basis,requestHash:'b'.repeat(64),area:'REVENUE',measure:'Annual movement',
  currentAmount:'120.123456',priorAmount:'100.000000',budgetAmount:null,varianceRatio:'0.201235',currency:'QAR',resultStatus:'DRAFT',canProceed:true,blockers:[]};
const receipt=(requestId:string)=>({id:user,requestId,requestHash:preview.requestHash,evidenceId:user,actorId:user,reason:fields.reason,evidenceReference:fields.evidenceReference,
  createdAt:'2026-10-04T10:00:00Z',result:{evidenceId:user,kind:'ANALYTICAL',status:'DRAFT',area:'REVENUE',measure:fields.measure,currentAmount:fields.currentAmount,
    priorAmount:fields.priorAmount,varianceRatio:preview.varianceRatio,actorId:user,createdAt:'2026-10-04T10:00:00Z'}});

describe('native analytical preparation',()=>{
  let http:HttpTestingController;let params:BehaviorSubject<ParamMap>;let routeMock:{paramMap:BehaviorSubject<ParamMap>;snapshot:{queryParamMap:ParamMap}};
  beforeEach(()=>{sessionStorage.clear();params=new BehaviorSubject(convertToParamMap({id}));routeMock={paramMap:params,snapshot:{queryParamMap:convertToParamMap({})}};TestBed.configureTestingModule({imports:[AnalyticalPreparation],
    providers:[provideHttpClient(),provideHttpClientTesting(),provideRouter([]),{provide:ActivatedRoute,useValue:routeMock}]});
    http=TestBed.inject(HttpTestingController);TestBed.inject(SessionService).current.set({userId:user,firmId:id,generation:'1',staff:true});});
  afterEach(()=>{try{http.verify({ignoreCancelled:true});}finally{TestBed.resetTestingModule();sessionStorage.clear();}});
  async function open(){const fixture=TestBed.createComponent(AnalyticalPreparation);fixture.detectChanges();TestBed.tick();http.expectOne(base).flush(context);TestBed.tick();
    http.expectOne(base+`/state?periodId=${period}`).flush(state);await Promise.resolve();TestBed.tick();return{fixture,component:fixture.componentInstance};}
  async function prepare(c:AnalyticalPreparation){c.model.set({...fields});TestBed.tick();const task=c.prepare();http.expectOne(base+'/preview').flush({value:preview});await task;TestBed.tick();}

  it('requires an authorized basis, preview and fresh explicit assent for one preparation',async()=>{
    const {component:c}=await open();expect(c.basis()?.canPrepare).toBe(true);expect(c.valid()).toBe(false);await c.execute();http.expectNone(base);
    await prepare(c);c.reviewed.set(true);const task=c.execute();const post=http.expectOne(base);expect(post.request.body.reviewed).toBe(true);
    expect(post.request.body.fields.periodId).toBe(period);post.flush({value:receipt(post.request.body.requestId)});await task;
    expect(c.receipt()?.result.kind).toBe('ANALYTICAL');expect(c.uncertain()).toBe(false);expect(c.message()).toContain('Independent review remains required');
    http.expectOne(base).flush(context);TestBed.tick();http.expectOne(base+`/state?periodId=${period}`).flush(state);TestBed.tick();
  });
  it('keeps unknown outcomes fenced until actor-owned receipt lookup resolves them',async()=>{
    const {component:c}=await open();await prepare(c);c.reviewed.set(true);const task=c.execute();const post=http.expectOne(base);const requestId=post.request.body.requestId;
    post.flush({}, {status:503,statusText:'Unavailable'});await task;expect(c.uncertain()).toBe(true);expect(await c.confirmNavigation()).toBe(false);await c.execute();http.expectNone(base);
    const check=c.reconcile();http.expectOne(base+`/receipts/${requestId}?requestHash=${preview.requestHash}`).flush({found:true,receipt:receipt(requestId)});await check;
    expect(c.receipt()).toBeTruthy();c.acknowledge();expect(c.uncertain()).toBe(false);http.expectOne(base).flush(context);TestBed.tick();
    http.expectOne(base+`/state?periodId=${period}`).flush(state);TestBed.tick();expect(c.model().currentAmount).toBe('');
  });
  it('invalidates the preview and assent after any input change',async()=>{
    const {component:c}=await open();await prepare(c);c.reviewed.set(true);c.setField('currentAmount','121.000000');expect(c.preview()).toBeNull();expect(c.reviewed()).toBe(false);
    expect(c.saveDraft()).toBe(true);expect(sessionStorage.getItem(sessionStorage.key(0)!)).not.toContain('reviewed');
    expect(analyticalEditableFields({...fields,reviewed:true})).toBeNull();
  });
  it('clears exposed amounts when the signed-in session is invalidated',async()=>{
    const {fixture,component:c}=await open();c.model.set({...fields});TestBed.inject(SessionService).clear();TestBed.tick();
    expect(c.data.data()).toBeNull();expect(c.model().currentAmount).toBe('');expect(fixture.nativeElement.textContent).not.toContain('120.123456');
  });
  it('explains how to request access when the server conceals an unavailable scope',()=>{
    const fixture=TestBed.createComponent(AnalyticalPreparation);fixture.detectChanges();TestBed.tick();
    http.expectOne(base).flush({code:'scope.denied'},{status:403,statusText:'Forbidden'});TestBed.tick();
    expect(fixture.nativeElement.textContent).toContain('Ask your firm administrator to confirm your AuditSphere role and client or engagement scope.');
  });
  it('rejects malformed period state, preview and mismatched receipt contracts',()=>{
    expect(()=>decodeAnalyticalState({...state,reviewBasis:'bad'},'state')).toThrow();expect(()=>decodeAnalyticalPreview({...preview,periodId:'bad'},'preview')).toThrow();
    expect(()=>decodeAnalyticalLookup({found:true,receipt:null})).toThrow();expect(analyticalEditableFields({...fields,hidden:'value'})).toBeNull();
  });

  it('pre-fills area and measure from a statement link when the statement revision still matches',async()=>{
    const statement='c'.repeat(64);
    routeMock.snapshot={queryParamMap:convertToParamMap({area:'Revenue',line:'REV-100',periodStart:'2026-01-01',periodEnd:'2026-12-31',revision:statement,returnUrl:`/app/engagements/${id}/statements`})};
    const fixture=TestBed.createComponent(AnalyticalPreparation);fixture.detectChanges();TestBed.tick();
    http.expectOne(base).flush(context);TestBed.tick();
    http.expectOne(base+`/state?periodId=${period}`).flush(state);
    http.expectOne(`/api/ui/engagements/${id}/statements/workspace`).flush({basis:{revision:statement}});
    await new Promise((resolve)=>setTimeout(resolve,0));TestBed.tick();
    const c=fixture.componentInstance;
    expect(c.model().area).toBe('Revenue');expect(c.model().measure).toBe('REV-100');expect(c.periodId()).toBe(period);
    expect(c.returnTarget()).toBe(`/app/engagements/${id}/statements`);
  });
  it('refuses a stale statement link and fills nothing',async()=>{
    routeMock.snapshot={queryParamMap:convertToParamMap({area:'Revenue',line:'REV-100',periodStart:'2026-01-01',periodEnd:'2026-12-31',revision:'c'.repeat(64)})};
    const fixture=TestBed.createComponent(AnalyticalPreparation);fixture.detectChanges();TestBed.tick();
    http.expectOne(base).flush(context);TestBed.tick();
    http.expectOne(base+`/state?periodId=${period}`).flush(state);
    http.expectOne(`/api/ui/engagements/${id}/statements/workspace`).flush({basis:{revision:'d'.repeat(64)}});
    await new Promise((resolve)=>setTimeout(resolve,0));TestBed.tick();
    const c=fixture.componentInstance;
    expect(c.model().area).toBe('');expect(c.message()).toContain('earlier version');
  });
});
