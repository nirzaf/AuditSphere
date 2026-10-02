import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SharePointAdministration } from './sharepoint';
import { SessionService } from '../../core/session';
import { currentVerification, editableResource, selectedResources } from './sharepoint-contracts';
import { decode } from '../../core/decode';

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
    c.resourceForm.controls.reviewed.setValue(true); const save=c.saveResource(); const request=http.expectOne(prefix+'resources/save');
    expect(request.request.body.expectedRevision).toBe('4'); expect(request.request.body.draftId).toBe(id); expect(request.request.body.driveId).toBe('drive-2');
    expect(request.request.body.tenantId).toBeUndefined();
    request.flush({code:'revision.stale',message:'Changed'}, {status:409,statusText:'Conflict'}); await save;
    expect(c.uncertain()).toBe(false); expect(c.message()).toContain('Refresh and review'); fixture.destroy();
  });
  it('requires review of the exact immutable template before approval',async () => {
    const {fixture,c,http}=open(); c.reviewTemplate(id); await c.approveTemplate(); http.expectNone(prefix+'templates/approve');
    c.approveReviewed.setValue(true); const saving=c.approveTemplate(); const request=http.expectOne(prefix+'templates/approve');
    expect(request.request.body.expectedDigest).toBe('a'.repeat(64)); request.flush({value:true}); await saving;
    http.expectOne(prefix+'resources').flush(snapshot); TestBed.tick(); expect(c.reviewedTemplate()).toBeNull(); fixture.destroy();
  });
  it('fences an unknown write until persisted state is explicitly refreshed and reviewed again',async () => {
    const {fixture,c,http}=open(); c.templateForm.controls.reviewed.setValue(true); const save=c.saveTemplate();
    const request=http.expectOne(prefix+'templates/save'); expect(request.request.body.expectedVersion).toBe('2');
    request.flush({}, {status:503,statusText:'Unavailable'}); await save; expect(c.uncertain()).toBe(true);
    await c.saveTemplate(); http.expectNone(prefix+'templates/save'); c.refresh(); http.expectOne(prefix+'resources').flush(snapshot); TestBed.tick();
    expect(c.uncertain()).toBe(false); expect(c.templateForm.controls.reviewed.value).toBe(false);
    await c.saveTemplate(); http.expectNone(prefix+'templates/save'); fixture.destroy();
  });
  it('treats local drafts and stale or future verification as unverified',() => {
    const now=Date.parse('2026-10-02T12:00:00Z'); expect(currentVerification('VERIFIED','2026-10-02T00:00:00Z',now)).toBe(true);
    expect(currentVerification('VERIFIED','2026-09-30T00:00:00Z',now)).toBe(false);
    expect(currentVerification('VERIFIED','2026-10-03T00:00:00Z',now)).toBe(false); expect(editableResource('ACTIVE')).toBe(false);
    expect(() => decode(selectedResources,{...snapshot,workspace:{...snapshot.workspace,templates:Array(101).fill(snapshot.workspace.templates[0])}})).toThrow();
    expect(() => decode(selectedResources,{...snapshot,workspace:{...snapshot.workspace,draft:{...snapshot.workspace.draft,tenantId:'email@example.test'}}})).toThrow();
  });
});
