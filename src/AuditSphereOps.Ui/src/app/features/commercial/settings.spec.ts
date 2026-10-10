import { describe, expect, it } from 'vitest';
import { decodePolicies, decodeSettings } from './settings';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const rule = {
  id,
  version: '1',
  kind: 'DISCOUNT_OVER_PERCENT',
  threshold: '5.25',
  role: 'Manager',
};
const workspace = {
  canEdit: false,
  profile: null,
  rulesRevision: 'a'.repeat(64),
  defaultDiscountThreshold: '10',
  defaultRole: 'Partner',
  approverRoles: ['Partner', 'Manager', 'Administrator'],
  rules: [rule],
};
describe('Commercial settings contract', () => {
  it('retains read-only authority, exact threshold and matrix fingerprint', () => {
    const w = decodeSettings(workspace);
    expect(w.canEdit).toBe(false);
    expect(w.rules[0].threshold).toBe('5.25');
    expect(w.rulesRevision).toHaveLength(64);
  });
  it('rejects numeric thresholds/revisions and forged fingerprints', () => {
    expect(() => decodeSettings({ ...workspace, rules: [{ ...rule, threshold: 5.25 }] })).toThrow();
    expect(() => decodeSettings({ ...workspace, rules: [{ ...rule, version: 1 }] })).toThrow();
    expect(() => decodeSettings({ ...workspace, rulesRevision: 'short' })).toThrow();
  });
  it('bounds the active matrix', () => {
    expect(() => decodeSettings({ ...workspace, rules: Array(101).fill(rule) })).toThrow();
  });
});
const policy = {
  id,
  version: '2',
  currency: 'QAR',
  minimumFee: '1000',
  maximumFee: '50000',
  maxDiscountPercent: null,
  validityDays: '30',
  status: 'PENDING_APPROVAL',
  current: false,
  canSubmit: false,
  canApprove: true,
};
describe('Pricing policy contract', () => {
  it('keeps exact limits, an optional discount cap and the server-computed actions', () => {
    const p = decodePolicies([policy])[0];
    expect([p.minimumFee, p.maxDiscountPercent, p.canApprove, p.canSubmit]).toEqual(['1000', null, true, false]);
  });
  it('rejects numeric limits, unknown states and missing action flags', () => {
    expect(() => decodePolicies([{ ...policy, minimumFee: 1000 }])).toThrow();
    expect(() => decodePolicies([{ ...policy, version: 2 }])).toThrow();
    expect(() => decodePolicies([{ ...policy, status: 'ACTIVE' }])).toThrow();
    expect(() => decodePolicies([{ ...policy, canApprove: undefined }])).toThrow();
    expect(() => decodePolicies(Array(101).fill(policy))).toThrow();
  });
});

import { settingsDraft, CommercialSettings } from './settings';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
const profile={legalName:'Synthetic firm',address:'',email:'',phone:'',accent:'#2B6CB0',closing:'',history:'',credentials:'',methodology:''};
describe('Commercial settings tab drafts',()=>{
  beforeEach(()=>{TestBed.configureTestingModule({imports:[CommercialSettings],providers:[provideRouter([]),provideHttpClient(),provideHttpClientTesting()]});TestBed.inject(SessionService).current.set({userId:id,firmId:id,generation:'0',staff:true});});
  afterEach(()=>{TestBed.inject(HttpTestingController).verify({ignoreCancelled:true});TestBed.resetTestingModule();});
  it('allowlists editable fields without version or review authorization',()=>{
    const r=settingsDraft({profile:{...profile,version:'999'},kind:'NON_STANDARD_TERMS',threshold:'10',role:'Partner',reviewed:true});
    expect(r?.profile).toEqual(profile);expect(r).not.toHaveProperty('reviewed');
    expect(settingsDraft({profile:{...profile,methodology:'x'.repeat(16001)},kind:'DISCOUNT_OVER_PERCENT',threshold:'10',role:'Partner'})).toBeNull();
  });
  it('recovers compatible fields with current version and fresh review',()=>{
    const f=TestBed.createComponent(CommercialSettings),http=TestBed.inject(HttpTestingController);f.detectChanges();
    http.expectOne('/api/ui/commercial-settings').flush({...workspace,canEdit:true,profile:{...profile,version:'3'}});TestBed.tick();
    http.expectOne('/api/ui/commercial-settings/pricing-policy').flush([]);TestBed.tick();
    const c=f.componentInstance;c.draftScope.set({entity:'commercial-settings',baseRevision:'a'.repeat(64)});
    vi.spyOn(TestBed.inject(TabDrafts),'read').mockReturnValue({state:'ready',draft:{submissionPending:false,value:{profile:{...profile,legalName:'Recovered firm'},kind:'DISCOUNT_OVER_PERCENT',threshold:'15',role:'Partner'}}});
    c.reviewed=c.ruleReviewed=true;c.recoverTabDraft();expect(c.profile.legalName).toBe('Recovered firm');expect(c.profile.version).toBe('3');expect(c.reviewed).toBe(false);expect(c.ruleReviewed).toBe(false);expect(c.dirty()).toBe(true);f.destroy();
  });
  it('preserves other unsubmitted rule fields after a confirmed profile command',()=>{
    const f=TestBed.createComponent(CommercialSettings),http=TestBed.inject(HttpTestingController);f.detectChanges();
    http.expectOne('/api/ui/commercial-settings').flush({...workspace,canEdit:true,profile:{...profile,version:'3'}});TestBed.tick();
    http.expectOne('/api/ui/commercial-settings/pricing-policy').flush([]);TestBed.tick();
    const c=f.componentInstance;c.threshold='17';c.profile.legalName='Updated firm';c.reviewed=true;c.saveProfile();
    http.expectOne('/api/ui/commercial-settings/profile').flush({id});
    http.expectOne('/api/ui/commercial-settings').flush({...workspace,canEdit:true,profile:{...profile,legalName:'Updated firm',version:'4'}});
    http.expectOne('/api/ui/commercial-settings/pricing-policy').flush([]);
    expect(c.threshold).toBe('17');expect(c.dirty()).toBe(true);expect(c.ruleReviewed).toBe(false);f.destroy();
  });
  it('offers pricing-policy actions only from server flags and requires an approval reason',()=>{
    const f=TestBed.createComponent(CommercialSettings),http=TestBed.inject(HttpTestingController);f.detectChanges();
    http.expectOne('/api/ui/commercial-settings').flush({...workspace,canEdit:true,profile:{...profile,version:'3'}});TestBed.tick();
    const other={...policy,id:'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',version:'1',status:'APPROVED',current:true,canApprove:false};
    http.expectOne('/api/ui/commercial-settings/pricing-policy').flush([policy,other]);TestBed.tick();f.detectChanges();
    const c=f.componentInstance;expect(c.canApproveAnyPolicy()).toBe(true);
    expect((f.nativeElement as HTMLElement).textContent).toContain('Approved · in force');
    c.approvePolicy(c.policies()[1],'Approved by a second administrator');c.submitPolicy(c.policies()[0]);
    c.approvePolicy(c.policies()[0],'no');expect(c.message()).toContain('approval reason');
    http.expectNone('/api/ui/commercial-settings/pricing-policy/'+id+'/approve');
    c.approvePolicy(c.policies()[0],'Approved by a second administrator');
    const approve=http.expectOne('/api/ui/commercial-settings/pricing-policy/'+id+'/approve');
    expect(approve.request.body).toEqual({reason:'Approved by a second administrator',reviewed:true});
    approve.flush(null,{status:204,statusText:'No Content'});
    http.expectOne('/api/ui/commercial-settings/pricing-policy').flush([{...policy,status:'APPROVED',current:true,canApprove:false}]);
    expect(c.canApproveAnyPolicy()).toBe(false);
    c.policyCurrency='qar';c.policyMinimumFee='1000';c.policyMaximumFee='';c.policyMaxDiscount='';c.policyValidityDays='400';c.policyReviewed.set(true);
    c.savePolicy();expect(c.message()).toContain('1 to 365');
    c.policyValidityDays='45';c.savePolicy();
    const save=http.expectOne('/api/ui/commercial-settings/pricing-policy');
    expect(save.request.body).toEqual({currency:'QAR',minimumFee:'1000',maximumFee:null,maxDiscount:null,validityDays:'45',expectedRevision:null,reviewed:true});
    save.flush({id});http.expectOne('/api/ui/commercial-settings/pricing-policy').flush([]);f.destroy();
  });
});
