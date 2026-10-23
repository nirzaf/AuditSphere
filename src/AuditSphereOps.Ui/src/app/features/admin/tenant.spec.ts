import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { TenantConnection } from './tenant';
import { SharePointAdministration } from './sharepoint';
import { WorkspaceAdministration } from './workspaces';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import { tenantWorkspace } from './tenant-contracts';

const id = '11111111-1111-4111-8111-111111111111';
const siblingId = '22222222-2222-4222-8222-222222222222';
const exactEnvelopeDigest = '2fb1b78b680d81bd3a63f3c6340973ab665a9d819c95fd1a04175b63b155ce85';
const snapshot = { consentConfigured:false, directoryConfigured:false, simulation:false,
  preparationConfigured:false, configuredTenantId:id, mailAuthorizationEnrollmentAvailable:false, workspace:{ connection:{ draftId:id, expectedTenantId:id,
    draftState:'DRAFT', connectionState:null, consentState:null, lastAttemptState:null, lastAttemptAt:null,
    directoryCapabilityState:'NOT_CONFIGURED', directoryLastCheckedAt:null }, capabilities:[], setupProgress:null,
    consentingAdministrator:null, draftRevision:'1', setupMetadata:{tenantDisplayName:'Synthetic tenant label',mailState:'CONFIGURED',recordsState:'NOT_CONFIGURED'} } };

function mailEvidence(receiptId: string, attestationSha256 = exactEnvelopeDigest) {
  return {receiptId,model:'LEGACY_APPLICATION_ACCESS_POLICY',attestationSha256,bindingSha256:'b'.repeat(64),expiresAtUtc:'2026-10-24T00:00:00Z'};
}
function workspaceWithMailEvidence(evidence: ReturnType<typeof mailEvidence>, overrides: {
  enrollmentAvailable?: boolean; enabled?: boolean; state?: string; stale?: boolean
} = {}) {
  return {...snapshot,mailAuthorizationEnrollmentAvailable:overrides.enrollmentAvailable ?? true,workspace:{...snapshot.workspace,capabilities:[{
    capability:'OUTBOUND_MAIL',displayName:'Outbound mail',permission:'Mail.Send',enabled:overrides.enabled ?? true,state:overrides.state ?? 'VERIFIED',lastVerifiedAt:'2026-10-23T00:00:00Z',
    stale:overrides.stale ?? false,diagnosticCode:'mail-authorization-evidence-current',authorizationEvidence:evidence
  }]}};
}
function selectMailEvidence(c: TenantConnection, payloadBytes: Uint8Array, signatureBytes: Uint8Array) {
  const payloadBuffer=new ArrayBuffer(payloadBytes.byteLength),signatureBuffer=new ArrayBuffer(signatureBytes.byteLength);
  new Uint8Array(payloadBuffer).set(payloadBytes);new Uint8Array(signatureBuffer).set(signatureBytes);
  const payload=new File([payloadBuffer],'authorization.json',{type:'application/json'});
  const signature=new File([signatureBuffer],'authorization.p7s',{type:'application/pkcs7-signature'});
  Object.defineProperty(payload,'arrayBuffer',{value:()=>Promise.resolve(payloadBuffer)});
  Object.defineProperty(signature,'arrayBuffer',{value:()=>Promise.resolve(signatureBuffer)});
  const payloadInput=document.createElement('input');Object.defineProperty(payloadInput,'files',{value:[payload]});
  const signatureInput=document.createElement('input');Object.defineProperty(signatureInput,'files',{value:[signature]});
  c.selectMailAuthorizationFile({currentTarget:payloadInput} as unknown as Event,'payload');
  c.selectMailAuthorizationFile({currentTarget:signatureInput} as unknown as Event,'signature');
  return {payload,signature};
}
async function waitForRequest(http: HttpTestingController, url: string) {
  for (let attempt=0;attempt<50;attempt++) {
    const requests=http.match(url);
    if (requests.length) { expect(requests).toHaveLength(1); return requests[0]!; }
    await new Promise(resolve=>setTimeout(resolve,1));
  }
  throw new Error(`No request was sent to ${url}`);
}

describe('Tenant saved setup metadata', () => {
  beforeEach(() => {
    const nodeWebCrypto=(globalThis as unknown as {process:{getBuiltinModule(name:string):{webcrypto:Crypto}}})
      .process.getBuiltinModule('crypto').webcrypto;
    vi.stubGlobal('crypto',nodeWebCrypto);
    TestBed.configureTestingModule({imports:[TenantConnection],providers:[provideRouter([]),provideHttpClient(),provideHttpClientTesting()]});
    TestBed.overrideComponent(TenantConnection,{remove:{imports:[SharePointAdministration,WorkspaceAdministration]},add:{schemas:[NO_ERRORS_SCHEMA]}});
    TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'0',staff:true});
  });
  afterEach(() => {TestBed.inject(HttpTestingController).verify({ignoreCancelled:true});TestBed.resetTestingModule();vi.unstubAllGlobals();});
  it('renders saved labels/states without claiming live verification and clears them on session loss', () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();http.expectOne('/api/ui/administration/microsoft365').flush(snapshot);TestBed.tick();fixture.detectChanges();
    const rendered=fixture.nativeElement.textContent as string;
    expect(rendered).toContain('Synthetic tenant label');expect(rendered).toContain('Friendly setup label only');
    expect(rendered).toContain('Mail setup');expect(rendered).toContain('Records setup');expect(rendered).toContain('not verification results');
    expect(rendered).not.toContain('Enroll signed mail authorization evidence');
    TestBed.inject(SessionService).current.set(null);TestBed.tick();fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic tenant label');fixture.destroy();
  });
  it('handles absent metadata without inventing setup states and rejects unsupported verification claims', () => {
    expect(decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,setupMetadata:null}}).workspace.setupMetadata).toBeNull();
    expect(() => decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,setupMetadata:{...snapshot.workspace.setupMetadata,mailState:'VERIFIED'}}})).toThrow();
    expect(() => decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,setupMetadata:{...snapshot.workspace.setupMetadata,tenantDisplayName:'x'.repeat(301)}}})).toThrow();
    expect(() => decode(tenantWorkspace,{...snapshot,mailAuthorizationEnrollmentAvailable:'yes'})).toThrow();
    const capability={capability:'OUTBOUND_MAIL',displayName:'Outbound mail',permission:'Mail.Send',enabled:true,state:'NOT_VERIFIED',lastVerifiedAt:null,stale:false,diagnosticCode:'mail-authorization-bound-evidence-required'};
    const withoutTypedEvidence=decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,capabilities:[capability]}});
    expect(withoutTypedEvidence.workspace.capabilities[0].authorizationEvidence).toBeNull();
    const evidence={receiptId:id,model:'LEGACY_APPLICATION_ACCESS_POLICY',attestationSha256:'a'.repeat(64),bindingSha256:'b'.repeat(64),expiresAtUtc:'2026-10-24T00:00:00Z'};
    expect(() => decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,capabilities:[{...capability,authorizationEvidence:{...evidence,model:'LEGACY_AAP_GRAPH'}}]}})).toThrow();
  });
  it('uploads exact selected receipt bytes only after review when the server enables enrollment', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('not proof of a live send or delivery');
    const payloadBytes=Uint8Array.from([0x7b,0x0a,0x22,0x61,0x22,0x3a,0x31,0x7d]);
    const signatureBytes=Uint8Array.from([0x30,0x82,0x00,0xff]);
    const {payload,signature}=selectMailEvidence(c,payloadBytes,signatureBytes);
    await c.enrollMailAuthorizationEvidence();
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    const enroll=await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    const base64=(bytes:Uint8Array)=>btoa(Array.from(bytes,value=>String.fromCharCode(value)).join(''));
    expect(enroll.request.body).toEqual({canonicalPayloadBase64:base64(payloadBytes),detachedCmsBase64:base64(signatureBytes),reviewed:true});
    enroll.flush({value:id});
    const evidence=mailEvidence(id);
    const persisted=workspaceWithMailEvidence(evidence);
    const receiptReadback=await waitForRequest(http,'/api/ui/administration/microsoft365');
    receiptReadback.flush(persisted);
    await pending;
    TestBed.tick();fixture.detectChanges();
    expect(c.uncertain()).toBe(false);
    expect(c.mailPayloadFile()).toBeNull();expect(c.mailSignatureFile()).toBeNull();
    expect(c.mailEvidenceMessage()).toContain('persisted for the exact submitted files');
    expect(fixture.nativeElement.textContent).toContain('Persisted mail authorization evidence');
    expect(fixture.nativeElement.textContent).toContain('LEGACY_APPLICATION_ACCESS_POLICY');
    expect(fixture.nativeElement.textContent).toContain(evidence.attestationSha256);
    expect(fixture.nativeElement.textContent).toContain(evidence.bindingSha256);
    expect(fixture.nativeElement.textContent).toContain(evidence.expiresAtUtc);
    expect(fixture.nativeElement.textContent).toContain('not proof of a live send or delivery');
    expect(fixture.nativeElement.textContent).toContain('Enroll signed mail authorization evidence');
    fixture.destroy();
  });
  it('keeps selected files unknown and blocks retry when a successful response has no valid receipt id', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    const payloadBytes=Uint8Array.from([0x7b,0x7d]),signatureBytes=Uint8Array.from([0x30,0x01]);
    const {payload,signature}=selectMailEvidence(c,payloadBytes,signatureBytes);
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    (await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll')).flush({value:'not-a-guid'});
    (await waitForRequest(http,'/api/ui/administration/microsoft365')).flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    await pending;TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    expect(c.mailAuthorizationReviewed()).toBe(true);
    expect(c.mailEvidenceMessage()).toContain('outcome remains unknown');
    expect(c.mailEvidenceMessage()).not.toContain('acknowledged');expect(c.mailEvidenceMessage()).not.toContain('recorded');
    await c.enrollMailAuthorizationEvidence();
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    fixture.destroy();
  });
  it('recovers a lost enrollment acknowledgment only from the exact current envelope receipt', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    const payloadBytes=Uint8Array.from([0x7b,0x0a,0x22,0x61,0x22,0x3a,0x31,0x7d]);
    const signatureBytes=Uint8Array.from([0x30,0x82,0x00,0xff]);
    const {payload,signature}=selectMailEvidence(c,payloadBytes,signatureBytes);
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    const enroll=await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    enroll.error(new ProgressEvent('error'));
    (await waitForRequest(http,'/api/ui/administration/microsoft365')).flush(workspaceWithMailEvidence(mailEvidence(siblingId)));
    await pending;TestBed.tick();
    expect(c.uncertain()).toBe(false);
    expect(c.mailPayloadFile()).toBeNull();expect(c.mailSignatureFile()).toBeNull();
    expect(c.mailAuthorizationReviewed()).toBe(false);
    expect(c.mailEvidenceMessage()).toContain('after the acknowledgment was lost');
    expect(c.mailEvidenceMessage()).toContain('No second enrollment was sent');
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    fixture.destroy();
  });
  it('keeps uncertainty for a sibling receipt or a changed envelope and never reposts', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    const payloadBytes=Uint8Array.from([0x7b,0x0a,0x22,0x61,0x22,0x3a,0x31,0x7d]);
    const signatureBytes=Uint8Array.from([0x30,0x82,0x00,0xff]);
    const {payload,signature}=selectMailEvidence(c,payloadBytes,signatureBytes);
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    (await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll')).flush({value:id});
    (await waitForRequest(http,'/api/ui/administration/microsoft365')).flush(workspaceWithMailEvidence(mailEvidence(siblingId)));
    await pending;TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    expect(c.mailAuthorizationReviewed()).toBe(true);

    const refresh=c.refresh();
    (await waitForRequest(http,'/api/ui/administration/microsoft365')).flush(workspaceWithMailEvidence(mailEvidence(id,'c'.repeat(64))));
    await refresh;await pending;TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.mailEvidenceMessage()).toContain('outcome remains unknown');
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    fixture.destroy();
  });
  it('keeps uncertainty when an exact receipt is unavailable, disabled, unverified, or stale', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    const {payload,signature}=selectMailEvidence(c,Uint8Array.from([0x7b,0x0a,0x22,0x61,0x22,0x3a,0x31,0x7d]),Uint8Array.from([0x30,0x82,0x00,0xff]));
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    (await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll')).flush({value:id});
    const invalidCurrentReceipts=[
      {enrollmentAvailable:false},
      {enabled:false},
      {state:'NOT_VERIFIED'},
      {stale:true}
    ];
    let index=0;
    for (const overrides of invalidCurrentReceipts) {
      const refresh=index===0 ? null : c.refresh();
      (await waitForRequest(http,'/api/ui/administration/microsoft365'))
        .flush(workspaceWithMailEvidence(mailEvidence(id),overrides));
      if (refresh) await refresh;
      if (index===0) await pending;
      TestBed.tick();fixture.detectChanges();
      expect(c.uncertain()).toBe(true);
      expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
      expect(c.mailEvidenceMessage()).toContain('outcome remains unknown');
      index++;
    }
    // Identical bytes and ID become recoverable only when all current capability guards pass.
    const current=c.refresh();
    (await waitForRequest(http,'/api/ui/administration/microsoft365')).flush(workspaceWithMailEvidence(mailEvidence(id)));
    await current;TestBed.tick();
    expect(c.uncertain()).toBe(false);
    expect(c.mailPayloadFile()).toBeNull();expect(c.mailSignatureFile()).toBeNull();
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    fixture.destroy();
  });
  it('hides prior receipt data and keeps a generic notice after a denied recovery read', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    const {payload,signature}=selectMailEvidence(c,Uint8Array.from([0x7b,0x7d]),Uint8Array.from([0x30,0x01]));
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    (await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll')).flush({value:id});
    (await waitForRequest(http,'/api/ui/administration/microsoft365')).flush(workspaceWithMailEvidence(mailEvidence(siblingId)));
    await pending;TestBed.tick();fixture.detectChanges();
    expect(c.uncertain()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain(siblingId);

    const refresh=c.refresh();
    (await waitForRequest(http,'/api/ui/administration/microsoft365'))
      .flush({code:'scope.denied',message:'Not available.'},{status:403,statusText:'Forbidden'});
    await refresh;TestBed.tick();fixture.detectChanges();
    expect(c.ws.data()).toBeNull();
    expect(c.workspaceSnapshot()).toBeNull();
    expect(c.uncertain()).toBe(true);
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    expect(fixture.nativeElement.textContent).not.toContain(siblingId);
    expect(fixture.nativeElement.textContent).toContain('Persisted status could not be confirmed.');
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(false);

    (await waitForRequest(http,'/api/ui/administration/microsoft365'))
      .flush({}, {status:403,statusText:'Forbidden'});
    TestBed.tick();fixture.detectChanges();
    expect(c.workspaceSnapshot()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(siblingId);
    expect(fixture.nativeElement.textContent).toContain('Persisted status could not be confirmed.');
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    fixture.destroy();
  });
  it('keeps a definite enrollment refusal distinct from an unknown outcome', async () => {
    const fixture=TestBed.createComponent(TenantConnection),http=TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();fixture.detectChanges();
    const c=fixture.componentInstance;
    const payloadBytes=Uint8Array.from([0x7b,0x0a,0x22,0x61,0x22,0x3a,0x31,0x7d]);
    const signatureBytes=Uint8Array.from([0x30,0x82,0x00,0xff]);
    const {payload,signature}=selectMailEvidence(c,payloadBytes,signatureBytes);
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    (await waitForRequest(http,'/api/ui/administration/microsoft365/mail-authorization-evidence/enroll'))
      .flush({code:'request.invalid',message:'The signed evidence was refused.'},{status:400,statusText:'Bad Request'});
    await pending;
    expect(c.uncertain()).toBe(false);
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    expect(c.mailAuthorizationReviewed()).toBe(true);
    expect(c.mailEvidenceMessage()).toContain('refused');
    http.expectNone('/api/ui/administration/microsoft365');
    fixture.destroy();
  });
});
