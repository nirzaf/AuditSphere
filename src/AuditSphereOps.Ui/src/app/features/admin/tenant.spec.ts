import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TenantConnection } from './tenant';
import { SharePointAdministration } from './sharepoint';
import { WorkspaceAdministration } from './workspaces';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import { tenantWorkspace } from './tenant-contracts';

const id = '11111111-1111-4111-8111-111111111111';
const snapshot = { consentConfigured:false, directoryConfigured:false, simulation:false,
  preparationConfigured:false, configuredTenantId:id, mailAuthorizationEnrollmentAvailable:false, workspace:{ connection:{ draftId:id, expectedTenantId:id,
    draftState:'DRAFT', connectionState:null, consentState:null, lastAttemptState:null, lastAttemptAt:null,
    directoryCapabilityState:'NOT_CONFIGURED', directoryLastCheckedAt:null }, capabilities:[], setupProgress:null,
    consentingAdministrator:null, draftRevision:'1', setupMetadata:{tenantDisplayName:'Synthetic tenant label',mailState:'CONFIGURED',recordsState:'NOT_CONFIGURED'} } };

describe('Tenant saved setup metadata', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({imports:[TenantConnection],providers:[provideRouter([]),provideHttpClient(),provideHttpClientTesting()]});
    TestBed.overrideComponent(TenantConnection,{remove:{imports:[SharePointAdministration,WorkspaceAdministration]},add:{schemas:[NO_ERRORS_SCHEMA]}});
    TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'0',staff:true});
  });
  afterEach(() => {TestBed.inject(HttpTestingController).verify({ignoreCancelled:true});TestBed.resetTestingModule();});
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
    const payload=new File([payloadBytes],'authorization.json',{type:'application/json'});
    const signature=new File([signatureBytes],'authorization.p7s',{type:'application/pkcs7-signature'});
    Object.defineProperty(payload,'arrayBuffer',{value:()=>Promise.resolve(payloadBytes.buffer)});
    Object.defineProperty(signature,'arrayBuffer',{value:()=>Promise.resolve(signatureBytes.buffer)});
    const payloadInput=document.createElement('input');Object.defineProperty(payloadInput,'files',{value:[payload]});
    const signatureInput=document.createElement('input');Object.defineProperty(signatureInput,'files',{value:[signature]});
    c.selectMailAuthorizationFile({currentTarget:payloadInput} as unknown as Event,'payload');
    c.selectMailAuthorizationFile({currentTarget:signatureInput} as unknown as Event,'signature');
    await c.enrollMailAuthorizationEvidence();
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    await Promise.resolve();await Promise.resolve();
    const enroll=http.expectOne('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    const base64=(bytes:Uint8Array)=>btoa(Array.from(bytes,value=>String.fromCharCode(value)).join(''));
    expect(enroll.request.body).toEqual({canonicalPayloadBase64:base64(payloadBytes),detachedCmsBase64:base64(signatureBytes),reviewed:true});
    enroll.flush({value:id});await pending;
    const evidence={receiptId:id,model:'LEGACY_APPLICATION_ACCESS_POLICY',attestationSha256:'a'.repeat(64),bindingSha256:'b'.repeat(64),expiresAtUtc:'2026-10-24T00:00:00Z'};
    const persisted={...snapshot,mailAuthorizationEnrollmentAvailable:true,workspace:{...snapshot.workspace,capabilities:[{
      capability:'OUTBOUND_MAIL',displayName:'Outbound mail',permission:'Mail.Send',enabled:true,state:'VERIFIED',lastVerifiedAt:'2026-10-23T00:00:00Z',
      stale:false,diagnosticCode:'mail-authorization-evidence-current',authorizationEvidence:evidence
    }]}};
    http.expectOne('/api/ui/administration/microsoft365').flush(persisted);
    TestBed.tick();fixture.detectChanges();
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
    const payload=new File([payloadBytes],'authorization.json',{type:'application/json'});
    const signature=new File([signatureBytes],'authorization.p7s',{type:'application/pkcs7-signature'});
    Object.defineProperty(payload,'arrayBuffer',{value:()=>Promise.resolve(payloadBytes.buffer)});
    Object.defineProperty(signature,'arrayBuffer',{value:()=>Promise.resolve(signatureBytes.buffer)});
    const payloadInput=document.createElement('input');Object.defineProperty(payloadInput,'files',{value:[payload]});
    const signatureInput=document.createElement('input');Object.defineProperty(signatureInput,'files',{value:[signature]});
    c.selectMailAuthorizationFile({currentTarget:payloadInput} as unknown as Event,'payload');
    c.selectMailAuthorizationFile({currentTarget:signatureInput} as unknown as Event,'signature');
    c.mailAuthorizationReviewed.set(true);
    const pending=c.enrollMailAuthorizationEvidence();
    await Promise.resolve();await Promise.resolve();
    http.expectOne('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll').flush({value:'not-a-guid'});
    await pending;TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    expect(c.mailAuthorizationReviewed()).toBe(true);
    expect(c.mailEvidenceMessage()).toContain('outcome is unknown');
    expect(c.mailEvidenceMessage()).not.toContain('acknowledged');expect(c.mailEvidenceMessage()).not.toContain('recorded');
    http.expectOne('/api/ui/administration/microsoft365').flush({...snapshot,mailAuthorizationEnrollmentAvailable:true});
    TestBed.tick();
    expect(c.mailPayloadFile()).toBe(payload);expect(c.mailSignatureFile()).toBe(signature);
    await c.enrollMailAuthorizationEvidence();
    http.expectNone('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll');
    fixture.destroy();
  });
});
