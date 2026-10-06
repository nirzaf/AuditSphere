import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ClientAccountRoles, decodeAccountRoles } from './account-roles';
import { SessionService } from '../../core/session';
const client = '11111111-1111-4111-8111-111111111111', id = '22222222-2222-4222-8222-222222222222', maker = '33333333-3333-4333-8333-333333333333', reviewer = '44444444-4444-4444-8444-444444444444';
const proposal = { id,chartVersionId:id,accountId:id,role:'AR',effectiveFrom:'2026-01-01',effectiveTo:null,reason:'Receivable control',proposedByUserId:maker,decision:'APPROVE',decisionReason:'Independent check',reviewedByUserId:reviewer };
const response = { clientId:client,bookkeepingActive:true,canReview:true,page:0,hasMore:false,hasMoreAccounts:false,configurations:[proposal],accounts:[{id,chartVersionId:id,accountCode:'1100',accountName:'Receivables',accountType:'ASSET'}] };
describe('Reviewed client account roles', () => {
  it('accepts scoped independent historical decisions and account identities', () => { expect(decodeAccountRoles(response,client,0).configurations[0].decision).toBe('APPROVE'); });
  it('rejects mismatched clients/pages, malformed grants, self review and duplicate identities', () => {
    for (const change of [{clientId:id},{page:1},{canReview:'yes'},{configurations:[{...proposal,reviewedByUserId:maker}]},{configurations:[proposal,proposal]},{accounts:[{...response.accounts[0],id:'foreign'}]}]) expect(() => decodeAccountRoles({...response,...change},client,0)).toThrow();
  });
  let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({imports:[ClientAccountRoles],providers:[provideHttpClient(),provideHttpClientTesting()]});http=TestBed.inject(HttpTestingController);TestBed.inject(SessionService).current.set({userId:maker,firmId:id,generation:'1',staff:true}); });
  afterEach(() => {http.verify({ignoreCancelled:true});TestBed.resetTestingModule();});
  function setup() {const f=TestBed.createComponent(ClientAccountRoles);f.componentRef.setInput('clientId',client);f.detectChanges();const c=f.componentInstance;c.load(0);http.expectOne(r=>r.url.endsWith('/account-roles')).flush(response);c.accountId=id;c.role='AR';c.from='2026-01-01';c.reason='Receivable control';c.assent.set(true);return {f,c};}
  it('locks an unknown proposal outcome until fresh validated history and explicit preparation', () => {
    const {c}=setup();c.propose();http.expectOne(r=>r.method==='POST').error(new ProgressEvent('lost'));expect(c.unknown()).toBe(true);c.assent.set(true);c.propose();http.expectNone(r=>r.method==='POST');c.load(0);http.expectOne(r=>r.method==='GET').flush(response);expect(c.unknown()).toBe(false);expect(c.accountId).toBe('');expect(c.assent()).toBe(false);
  });
  it('cancels pending commands and clears another client’s private role data', () => {
    const {f,c}=setup();c.propose();const old=http.expectOne(r=>r.method==='POST');f.componentRef.setInput('clientId',id);f.detectChanges();expect(old.cancelled).toBe(true);expect(c.workspace()).toBeNull();expect(c.selected()).toBeNull();expect(c.accountId).toBe('');expect(c.unknown()).toBe(false);
  });
});
