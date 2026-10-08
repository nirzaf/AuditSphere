import { TestBed } from '@angular/core/testing';
import { APP_BASE_HREF } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { WorkspaceAdministration } from './workspaces';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import { clientSites, siteLink, workspacePage } from './workspace-contracts';

const id='11111111-1111-4111-8111-111111111111', prefix='/api/ui/administration/microsoft365/';
const page={providerConfigured:true,workspace:{page:0,hasMore:false,clients:[{clientId:id,clientName:'Synthetic client',workspaceState:'WAITING_FOR_INTEGRATION',lastErrorCode:null,lastVerifiedAt:null,engagements:[]}]}};
const review={targetId:id,kind:'CLIENT',name:'Synthetic client',currentState:'WAITING_FOR_INTEGRATION',reviewToken:'a'.repeat(64),eligible:true,requiredAction:'Review',tenantId:id,siteId:'site',driveId:'drive',rootFolderId:'root',clientTemplateId:id,engagementTemplateId:null};

describe('Workspace provisioning review',()=>{
  beforeEach(()=>{ TestBed.configureTestingModule({imports:[WorkspaceAdministration],providers:[{provide:APP_BASE_HREF,useValue:"/ui/"},provideRouter([]),provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'0',staff:true}); });
  afterEach(()=>{ TestBed.inject(HttpTestingController).verify({ignoreCancelled:true}); TestBed.resetTestingModule(); });
  function open(){ const fixture=TestBed.createComponent(WorkspaceAdministration),http=TestBed.inject(HttpTestingController);fixture.detectChanges();
    http.expectOne(prefix+'workspaces?page=0').flush(page);http.expectOne(prefix+'client-sites').flush([]);TestBed.tick();
    expect(fixture.nativeElement.querySelector('h3 a').getAttribute('href')).toBe('/ui/app/clients/'+id);
    expect(fixture.nativeElement.querySelector('a[href*=operations]').getAttribute('href')).toBe('/ui/app/operations');
    return {fixture,c:fixture.componentInstance,http}; }
  function select(c:WorkspaceAdministration,http:HttpTestingController){c.select(id,false);TestBed.tick();http.expectOne(prefix+'workspaces/clients/'+id+'/review').flush(review);TestBed.tick();}
  it('requires exact current target review and a reason and distinguishes failed capability from HTTP success',async()=>{
    const {fixture,c,http}=open();select(c,http);expect(c.canProvision()).toBe(false);c.form.setValue({reason:'Approved folder setup',reviewed:true});
    const save=c.provision(),request=http.expectOne(prefix+'workspaces/clients/provision');expect(request.request.body.reviewToken).toBe(review.reviewToken);
    expect(request.request.body.tenantId).toBeUndefined();request.flush({value:{workspaceOrBindingId:id,state:'FAILED',message:'Read-back failed',diagnosticCode:'synthetic'}});await save;
    expect(c.failed()).toBe(true);expect(c.message()).toContain('FAILED');http.expectOne(prefix+'workspaces?page=0').flush(page);http.expectOne(prefix+'client-sites').flush([]);fixture.destroy();
  });
  it('sends an unknown mutation once and requires persisted refresh and a fresh target review',async()=>{
    const {fixture,c,http}=open();select(c,http);c.form.setValue({reason:'Approved setup',reviewed:true});const save=c.provision();
    http.expectOne(prefix+'workspaces/clients/provision').flush({}, {status:503,statusText:'Unavailable'});await save;
    expect(c.uncertain()).toBe(true);await c.provision();http.expectNone(prefix+'workspaces/clients/provision');c.refresh();TestBed.tick();
    http.expectOne(prefix+'workspaces?page=0').flush(page);http.expectOne(prefix+'client-sites').flush([]);TestBed.tick();
    expect(c.uncertain()).toBe(false);expect(c.review.data()).toBeNull();expect(c.form.controls.reviewed.value).toBe(false);fixture.destroy();
  });
  it('clears review on changed target metadata and blocks unavailable provider or eligibility',()=>{
    const {fixture,c,http}=open();select(c,http);c.form.setValue({reason:'Approved setup',reviewed:true});expect(c.canProvision()).toBe(true);
    c.select(id,false);TestBed.tick();http.expectOne(prefix+'workspaces/clients/'+id+'/review').flush({...review,eligible:false,reviewToken:'b'.repeat(64)});TestBed.tick();
    expect(c.form.controls.reviewed.value).toBe(false);expect(c.canProvision()).toBe(false);fixture.destroy();
  });
  it('bounds responses and refuses unverified or unsafe SharePoint links',()=>{
    expect(()=>decode(workspacePage,{...page,workspace:{...page.workspace,clients:Array(26).fill(page.workspace.clients[0])}})).toThrow();
    expect(()=>decode(clientSites,Array(101).fill({}))).toThrow();expect(siteLink('READY','https://synthetic.sharepoint.com/sites/client')).toContain('/sites/client');
    for(const url of ['javascript:alert(1)','https://synthetic.sharepoint.com/sites/a?token=secret','https://synthetic.sharepoint.com.evil.test/','https://user:pass@synthetic.sharepoint.com/']) expect(siteLink('READY',url)).toBeNull();
    expect(siteLink('BLOCKED_EXTERNAL','https://synthetic.sharepoint.com/sites/client')).toBeNull();
  });
});
