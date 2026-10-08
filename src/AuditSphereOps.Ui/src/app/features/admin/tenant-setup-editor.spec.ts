import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TenantSetupEditor } from './tenant-setup-editor';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';

const id='11111111-1111-4111-8111-111111111111';
const initial={tenantDisplayName:'Synthetic',mailState:'NOT_CONFIGURED',recordsState:'NOT_CONFIGURED'} as const;
describe('tenant setup review and recovery',()=>{
  beforeEach(()=>{TestBed.configureTestingModule({imports:[TenantSetupEditor],providers:[provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'0',staff:true}); });
  afterEach(()=>{TestBed.inject(HttpTestingController).verify({ignoreCancelled:true});TestBed.resetTestingModule();});
  function create(){const f=TestBed.createComponent(TenantSetupEditor);f.componentRef.setInput('draftId',id);f.componentRef.setInput('revision','1');f.componentRef.setInput('initial',initial);f.componentRef.setInput('editable',true);f.detectChanges();return f;}
  it('requires review confirmation and invalidates review when fields change',async()=>{
    const f=create(),c=f.componentInstance,http=TestBed.inject(HttpTestingController);
    c.model.set({name:'New label',mail:'CONFIGURED',records:'NOT_CONFIGURED'});TestBed.tick();
    const reviewed=c.review();const req=http.expectOne('/api/ui/administration/microsoft365/setup/preview');
    req.flush({value:{...req.request.body,requestHash:'a'.repeat(64),reviewBasis:'b'.repeat(64),before:initial}});await reviewed;
    expect(c.preview()).not.toBeNull();await c.save();http.expectNone('/api/ui/administration/microsoft365/setup/commands');
    c.assentModel.set({reviewed:true});c.model.update(x=>({...x,name:'Changed again'}));TestBed.tick();
    expect(c.preview()).toBeNull();expect(c.assentModel().reviewed).toBe(false);f.destroy();
  });
  it('refuses dispatch if the pending reference cannot be saved',async()=>{
    const f=create(),c=f.componentInstance,http=TestBed.inject(HttpTestingController);
    c.scope.set({entity:`tenant-setup:${id}`,baseRevision:'a'.repeat(64)});
    c.preview.set({requestId:id,draftId:id,expectedRevision:'1',requestHash:'a'.repeat(64),reviewBasis:'b'.repeat(64),before:initial,fields:initial});
    c.assentModel.set({reviewed:true});vi.spyOn(TestBed.inject(TabDrafts),'save').mockReturnValue(false);
    await c.save();http.expectNone('/api/ui/administration/microsoft365/setup/commands');expect(c.pending()).toBeNull();expect(c.failed()).toBe(true);f.destroy();
  });
  it('ignores late preview results after the component is destroyed',async()=>{
    const f=create(),c=f.componentInstance,http=TestBed.inject(HttpTestingController);
    const promise=c.review();const req=http.expectOne('/api/ui/administration/microsoft365/setup/preview');f.destroy();
    req.flush({value:{...req.request.body,requestHash:'a'.repeat(64),reviewBasis:'b'.repeat(64),before:initial}});await promise;expect(c.preview()).toBeNull();
  });
  it('retains only a pending reference after a lost response and reconciles without resending',async()=>{
    const f=create(),c=f.componentInstance,http=TestBed.inject(HttpTestingController);
    c.scope.set({entity:`tenant-setup:${id}`,baseRevision:'a'.repeat(64)});
    c.preview.set({requestId:id,draftId:id,expectedRevision:'1',requestHash:'a'.repeat(64),reviewBasis:'b'.repeat(64),before:initial,fields:initial});
    c.assentModel.set({reviewed:true});
    vi.spyOn(TestBed.inject(TabDrafts),'save').mockReturnValue(true);
    const save=c.save();http.expectOne('/api/ui/administration/microsoft365/setup/commands').flush({}, {status:503,statusText:'Unavailable'});await save;
    expect(c.pending()).toEqual({requestId:id,requestHash:'a'.repeat(64)});
    await c.save();http.expectNone('/api/ui/administration/microsoft365/setup/commands');
    const check=c.lookup();http.expectOne(`/api/ui/administration/microsoft365/setup/receipts/${id}?requestHash=${'a'.repeat(64)}`).flush({found:true,receipt:{id,requestId:id,draftId:id,requestHash:'a'.repeat(64),appliedRevision:'2',previousFingerprint:'b'.repeat(64),appliedFingerprint:'c'.repeat(64),recordedAt:'2026-10-03T15:00:00Z'}});await check;
    expect(c.receipt()?.appliedRevision).toBe('2');expect(c.assentModel().reviewed).toBe(false);f.destroy();
  });

});
