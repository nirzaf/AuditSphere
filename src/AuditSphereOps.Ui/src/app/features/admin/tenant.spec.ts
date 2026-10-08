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
  preparationConfigured:false, configuredTenantId:id, workspace:{ connection:{ draftId:id, expectedTenantId:id,
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
    TestBed.inject(SessionService).current.set(null);TestBed.tick();fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic tenant label');fixture.destroy();
  });
  it('handles absent metadata without inventing setup states and rejects unsupported verification claims', () => {
    expect(decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,setupMetadata:null}}).workspace.setupMetadata).toBeNull();
    expect(() => decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,setupMetadata:{...snapshot.workspace.setupMetadata,mailState:'VERIFIED'}}})).toThrow();
    expect(() => decode(tenantWorkspace,{...snapshot,workspace:{...snapshot.workspace,setupMetadata:{...snapshot.workspace.setupMetadata,tenantDisplayName:'x'.repeat(301)}}})).toThrow();
  });
});
