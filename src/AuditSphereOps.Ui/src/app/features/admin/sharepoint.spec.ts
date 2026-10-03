import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SharePointAdministration } from './sharepoint';
import { SessionService } from '../../core/session';
import { currentVerification, editableResource, selectedResources } from './sharepoint-contracts';
import { decode } from '../../core/decode';
import { TabDrafts } from '../../core/tab-drafts';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

const id = '11111111-1111-4111-8111-111111111111';
const manifest = '{"nodes":[]}';
const snapshot = { probeConfigured: true, workspace: { draft: { id, revision:'4', state:'VALIDATING',tenantId:id,
  siteUrl:'https://synthetic.sharepoint.com/sites/working',siteId:'site-1',driveId:'drive-1',rootFolderId:'root-1',
  accessProfile:'APP_MEDIATED',connectionRevisionId:id,connectionState:'VERIFIED',consentState:'VERIFIED' },
  templates:[{id,purpose:'CLIENT_WORKSPACE',version:'2',manifestJson:manifest,manifestDigest:'a'.repeat(64),createdAt:'2026-10-02T00:00:00Z',approvedAt:null}],
  clientDefaultManifest:manifest,engagementDefaultManifest:manifest,clientSteManifest:manifest,engagementSteManifest:manifest,verification:null } };
const prefix = '/api/ui/administration/microsoft365/';

describe('Selected SharePoint resource and template review',() => {
  beforeEach(() => {
    TestBed.configureTestingModule({imports:[SharePointAdministration],providers:[provideHttpClient(),provideHttpClientTesting()]});
    TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'0',staff:true});
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ignoreCancelled:true}); TestBed.resetTestingModule(); });
  function open() { const fixture=TestBed.createComponent(SharePointAdministration), http=TestBed.inject(HttpTestingController);
    fixture.detectChanges(); http.expectOne(prefix+'resources').flush(snapshot); TestBed.tick();
    return {fixture,c:fixture.componentInstance,http}; }
  it('clears review when exact resource IDs change and sends only the reviewed revision',async () => {
    const {fixture,c,http}=open(); c.resourceForm.controls.reviewed.setValue(true); c.resourceForm.controls.driveId.setValue('drive-2');
    expect(c.resourceForm.controls.reviewed.value).toBe(false); await c.saveResource(); http.expectNone(prefix+'resources/save');
    c.resourceForm.controls.reviewed.setValue(true); const save=c.saveResource(); await Promise.resolve(); await Promise.resolve(); const request=http.expectOne(prefix+'resources/save');
    expect(request.request.body.expectedRevision).toBe('4'); expect(request.request.body.draftId).toBe(id); expect(request.request.body.driveId).toBe('drive-2');
    expect(request.request.body.tenantId).toBeUndefined();
    request.flush({code:'revision.stale',message:'Changed'}, {status:409,statusText:'Conflict'}); await save;
    expect(c.uncertain()).toBe(false); expect(c.message()).toContain('Refresh and review'); fixture.destroy();
  });
  it('requires review of the exact immutable template before approval',async () => {
    const {fixture,c,http}=open(); c.reviewTemplate(id); await c.approveTemplate(); http.expectNone(prefix+'templates/approve');
    c.approveReviewed.setValue(true); const saving=c.approveTemplate(); await Promise.resolve(); await Promise.resolve(); const request=http.expectOne(prefix+'templates/approve');
    expect(request.request.body.expectedDigest).toBe('a'.repeat(64)); request.flush({value:true}); await saving;
    http.expectOne(prefix+'resources').flush(snapshot); TestBed.tick(); expect(c.reviewedTemplate()).toBeNull(); fixture.destroy();
  });
  it('fences an unknown write until persisted state is explicitly refreshed and reviewed again',async () => {
    const {fixture,c,http}=open(); c.templateForm.controls.reviewed.setValue(true); const save=c.saveTemplate();
    await Promise.resolve(); await Promise.resolve(); const request=http.expectOne(prefix+'templates/save'); expect(request.request.body.expectedVersion).toBe('2');
    request.flush({}, {status:503,statusText:'Unavailable'}); await save; expect(c.uncertain()).toBe(true);
    await c.saveTemplate(); http.expectNone(prefix+'templates/save'); const refresh=c.refresh(); await Promise.resolve(); await Promise.resolve(); http.expectOne(prefix+'resources').flush(snapshot); await refresh; TestBed.tick();
    expect(c.uncertain()).toBe(false); expect(c.templateForm.controls.reviewed.value).toBe(false);
    await c.saveTemplate(); http.expectNone(prefix+'templates/save'); fixture.destroy();
  });
  it('recovers only allowlisted fields for the same persisted revision and never restores assent',() => {
    const first=open(),resourceScope={entity:`sharepoint-resource:${id}`,baseRevision:'a'.repeat(64)},templateScope={entity:`sharepoint-template:${id}`,baseRevision:'b'.repeat(64)};
    first.c.resourceScope.set(resourceScope);first.c.templateScope.set(templateScope);
    first.c.resourceForm.patchValue({siteUrl:'https://synthetic.sharepoint.com/sites/recovered',siteId:'site-recovered',reviewed:true});
    first.c.templateForm.patchValue({manifestJson:'{"nodes":[{"key":"root","name":"Recovered"}]}',reviewed:true});
    expect(first.c.saveResourceDraft()).toBe(true);expect(first.c.saveTemplateDraft()).toBe(true);first.fixture.destroy();

    const second=open();second.c.resourceScope.set(resourceScope);second.c.templateScope.set(templateScope);
    second.c.recoverResourceDraft();second.c.recoverTemplateDraft();
    expect(second.c.resourceForm.getRawValue()).toMatchObject({siteUrl:'https://synthetic.sharepoint.com/sites/recovered',siteId:'site-recovered',reviewed:false});
    expect(second.c.templateForm.getRawValue()).toMatchObject({manifestJson:'{"nodes":[{"key":"root","name":"Recovered"}]}',reviewed:false});second.fixture.destroy();

    const third=open();third.c.resourceScope.set({...resourceScope,baseRevision:'c'.repeat(64)});third.c.templateScope.set({...templateScope,baseRevision:'d'.repeat(64)});
    third.c.recoverResourceDraft();third.c.recoverTemplateDraft();
    expect(third.c.resourceForm.controls.siteUrl.value).toBe('https://synthetic.sharepoint.com/sites/working');
    expect(third.c.templateForm.controls.manifestJson.value).toBe(manifest);third.fixture.destroy();
    const drafts=TestBed.inject(TabDrafts);drafts.clear(resourceScope.entity);drafts.clear(templateScope.entity);
  });
  it('guards navigation and saves dirty resource and template fields without their review assent',async()=>{
    const {fixture,c}=open(),resourceScope={entity:`sharepoint-resource:${id}`,baseRevision:'e'.repeat(64)},templateScope={entity:`sharepoint-template:${id}`,baseRevision:'f'.repeat(64)};
    c.resourceScope.set(resourceScope);c.templateScope.set(templateScope);
    c.resourceForm.controls.siteId.setValue('unsaved-site');c.templateForm.controls.manifestJson.setValue('{"nodes":[{"key":"draft"}]}');
    vi.spyOn(TestBed.inject(MatDialog),'open').mockReturnValue({afterClosed:()=>of('save')} as any);
    expect(await c.confirmNavigation()).toBe(true);
    const drafts=TestBed.inject(TabDrafts);
    expect(drafts.read(resourceScope,v=>v as {siteId:string}).state).toBe('ready');
    expect(drafts.read(templateScope,v=>v as {manifestJson:string}).state).toBe('ready');
    expect(c.resourceForm.controls.reviewed.value).toBe(false);expect(c.templateForm.controls.reviewed.value).toBe(false);
    drafts.clear(resourceScope.entity);drafts.clear(templateScope.entity);fixture.destroy();
  });
  it('treats local drafts and stale or future verification as unverified',() => {
    const now=Date.parse('2026-10-02T12:00:00Z'); expect(currentVerification('VERIFIED','2026-10-02T00:00:00Z',now)).toBe(true);
    expect(currentVerification('VERIFIED','2026-09-30T00:00:00Z',now)).toBe(false);
    expect(currentVerification('VERIFIED','2026-10-03T00:00:00Z',now)).toBe(false); expect(editableResource('ACTIVE')).toBe(false);
    expect(() => decode(selectedResources,{...snapshot,workspace:{...snapshot.workspace,templates:Array(101).fill(snapshot.workspace.templates[0])}})).toThrow();
    expect(() => decode(selectedResources,{...snapshot,workspace:{...snapshot.workspace,draft:{...snapshot.workspace.draft,tenantId:'email@example.test'}}})).toThrow();
  });
});
