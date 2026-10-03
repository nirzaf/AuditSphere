import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { ContactCreate } from './contact-create';
import { contactFields, decodeContactLookup, decodeContactState, validContact } from './contact-create-contracts';

const id='11111111-1111-4111-8111-111111111111',other='22222222-2222-4222-8222-222222222222';
const basis='a'.repeat(64),hash='b'.repeat(64),url='/api/ui/clients/'+id+'/contact-creation';
const state={clientId:id,clientName:'Synthetic client',safetyGeneration:'9007199254740993',reviewBasis:basis,currentPrimary:[{id:other,name:'Original primary'}]};
const fields={name:'Synthetic contact',email:'contact@example.test',role:'Finance',primary:true};
describe('Reviewed client contact contracts',()=>{
  it('allowlists bounded fields and exact generations without retaining assent',()=>{
    expect(contactFields({...fields,reviewed:true})).toBeNull();expect(contactFields({...fields,name:'x'.repeat(201)})).toBeNull();
    expect(contactFields({...fields,role:'Finance\n'})).toBeNull();expect(validContact({...fields,email:'invalid'})).toBe(false);
    expect(decodeContactState(state,'').safetyGeneration).toBe('9007199254740993');
    expect(()=>decodeContactState({...state,safetyGeneration:9007199254740993},'')).toThrow();
    expect(()=>decodeContactLookup({found:true,receipt:null})).toThrow();
  });
});
describe('Native contact review, tab recovery and ownership',()=>{
  let ids:BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(()=>{sessionStorage.clear();ids=new BehaviorSubject(convertToParamMap({id}));
    TestBed.configureTestingModule({imports:[ContactCreate],providers:[provideRouter([]),provideHttpClient(),provideHttpClientTesting(),
      {provide:ActivatedRoute,useValue:{paramMap:ids,snapshot:{paramMap:ids.value,queryParamMap:convertToParamMap({contactPage:"2",contactPageSize:"25"})}}}]});
    TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'1',staff:true});});
  afterEach(()=>{TestBed.inject(HttpTestingController).verify({ignoreCancelled:true});TestBed.resetTestingModule();sessionStorage.clear();});
  function open(){const f=TestBed.createComponent(ContactCreate);f.detectChanges();TestBed.tick();return f;}
  function read(value=state){const requests=TestBed.inject(HttpTestingController).match(r=>r.method==='GET'&&!r.url.includes('/receipts/')).filter(r=>!r.cancelled);expect(requests).toHaveLength(1);requests[0].flush(value);TestBed.tick();}
  async function preview(c:ContactCreate){c.model.set({...fields});TestBed.tick();const pending=c.prepare();const r=TestBed.inject(HttpTestingController).expectOne(url+'/preview');
    const body=r.request.body;r.flush({value:{clientId:id,requestId:body.requestId,reviewBasis:basis,requestHash:hash,fields,replacedPrimary:state.currentPrimary}});await pending;TestBed.tick();return body;}
  function receipt(requestId:string){return {id:other,clientId:id,contactId:other,actorId:id,requestId,requestHash:hash,reviewBasis:basis,resultGeneration:'9007199254740994',createdAt:'2026-10-03T12:30:00Z'};}
  it('shows exact primary replacement, requires assent and invalidates review on edit',async()=>{
    const f=open();read();const c=f.componentInstance;
    expect(c.returnParams.contactPage).toBe(2);expect(c.returnParams.contactPageSize).toBe(25);
    expect(f.nativeElement.querySelector('a[href*="/app/clients/"]').getAttribute('href')).toContain('contactPage=2');
    c.model.set({...fields,email:'bad'});TestBed.tick();await c.prepare();expect(c.preview()).toBeNull();
    await preview(c);expect(f.nativeElement.textContent).toContain('Original primary');
    await c.execute();TestBed.inject(HttpTestingController).expectNone(url);
    c.reviewed.set(true);c.model.update(v=>({...v,name:'Changed'}));TestBed.tick();expect(c.preview()).toBeNull();expect(c.reviewed()).toBe(false);
  });
  it('dispatches once, blocks unknown resubmission and reconciles its actor-owned receipt',async()=>{
    const f=open();read();const c=f.componentInstance,r=await preview(c);c.reviewed.set(true);
    const result=c.execute();const post=TestBed.inject(HttpTestingController).expectOne(url);expect(post.request.body.expectedRequestHash).toBe(hash);
    post.flush({}, {status:503,statusText:'Unavailable'});await result;TestBed.tick();expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);await c.prepare();TestBed.inject(HttpTestingController).expectNone(url+'/preview');
    const check=c.reconcile();TestBed.inject(HttpTestingController).expectOne(url+'/receipts/'+r.requestId+'?requestHash='+hash).flush({found:true,receipt:receipt(r.requestId)});
    await check;TestBed.tick();expect(c.receipt()?.requestId).toBe(r.requestId);c.acknowledge();read({...state,reviewBasis:'c'.repeat(64)});
    expect(c.uncertain()).toBe(false);expect(c.model().name).toBe('');expect(c.pending()).toBeNull();
  });
  it('recovers only the request across changed revisions, never old editable fields or assent',async()=>{
    const first=open();read();const c=first.componentInstance,r=await preview(c);c.reviewed.set(true);
    const dispatch=c.execute();TestBed.inject(HttpTestingController).expectOne(url).flush({}, {status:503,statusText:'Unknown'});await dispatch;TestBed.tick();first.destroy();
    const next=open();read({...state,reviewBasis:'c'.repeat(64)});const restored=next.componentInstance;
    expect(restored.uncertain()).toBe(true);expect(restored.pending()?.requestId).toBe(r.requestId);expect(restored.model().name).toBe('');expect(restored.reviewed()).toBe(false);
    expect(restored.draftAvailable()).toBe(false);
    const check=restored.reconcile();TestBed.inject(HttpTestingController).expectOne(url+'/receipts/'+r.requestId+'?requestHash='+hash).flush({found:false,receipt:null});await check;TestBed.tick();
    expect(restored.absent()).toBe(true);restored.model.set({...fields,name:'Different'});TestBed.tick();
    const prepare=restored.prepare();const p=TestBed.inject(HttpTestingController).expectOne(url+'/preview');expect(p.request.body.requestId).toBe(r.requestId);
    p.flush({value:{clientId:id,requestId:r.requestId,reviewBasis:'c'.repeat(64),requestHash:'d'.repeat(64),fields:{...fields,name:'Different'},replacedPrimary:[]}});
    await prepare;TestBed.tick();expect(restored.preview()).toBeNull();expect(restored.pending()).not.toBeNull();
  });
  it('restores matching tab fields explicitly and excludes review assent',()=>{
    const f=open();read();const c=f.componentInstance;c.model.set(fields);TestBed.tick();c.reviewed.set(true);expect(c.saveDraft()).toBe(true);f.destroy();
    const next=open();read();expect(next.componentInstance.draftAvailable()).toBe(true);expect(next.componentInstance.model().name).toBe('');
    next.componentInstance.restoreDraft();TestBed.tick();expect(next.componentInstance.model()).toEqual(fields);expect(next.componentInstance.reviewed()).toBe(false);
    expect(Array.from({length:sessionStorage.length},(_,i)=>sessionStorage.getItem(sessionStorage.key(i)!)).join()).not.toContain('reviewed');
  });
  it('fences old route callbacks and removes local fields and receipts on session loss',async()=>{
    const f=open();read();const c=f.componentInstance,r=await preview(c);c.reviewed.set(true);const dispatch=c.execute();const pending=TestBed.inject(HttpTestingController).expectOne(url);
    ids.next(convertToParamMap({id:other}));TestBed.tick();read({...state,clientId:other});
    c.model.set({...fields,name:'New route'});TestBed.tick();pending.flush({value:receipt(r.requestId)});await dispatch;TestBed.tick();
    expect(c.model().name).toBe('New route');expect(c.receipt()).toBeNull();
    TestBed.inject(SessionService).clear();TestBed.tick();expect(c.model().name).toBe('');expect(c.state.data()).toBeNull();expect(sessionStorage.length).toBe(0);
  });
});
