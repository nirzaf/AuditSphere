import { describe, expect, it } from 'vitest';
import { decodeSettings } from './settings';
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
    const c=f.componentInstance;c.draftScope.set({entity:'commercial-settings',baseRevision:'a'.repeat(64)});
    vi.spyOn(TestBed.inject(TabDrafts),'read').mockReturnValue({state:'ready',draft:{submissionPending:false,value:{profile:{...profile,legalName:'Recovered firm'},kind:'DISCOUNT_OVER_PERCENT',threshold:'15',role:'Partner'}}});
    c.reviewed=c.ruleReviewed=true;c.recoverTabDraft();expect(c.profile.legalName).toBe('Recovered firm');expect(c.profile.version).toBe('3');expect(c.reviewed).toBe(false);expect(c.ruleReviewed).toBe(false);expect(c.dirty()).toBe(true);f.destroy();
  });
  it('preserves other unsubmitted rule fields after a confirmed profile command',()=>{
    const f=TestBed.createComponent(CommercialSettings),http=TestBed.inject(HttpTestingController);f.detectChanges();
    http.expectOne('/api/ui/commercial-settings').flush({...workspace,canEdit:true,profile:{...profile,version:'3'}});TestBed.tick();
    const c=f.componentInstance;c.threshold='17';c.profile.legalName='Updated firm';c.reviewed=true;c.saveProfile();
    http.expectOne('/api/ui/commercial-settings/profile').flush({id});
    http.expectOne('/api/ui/commercial-settings').flush({...workspace,canEdit:true,profile:{...profile,legalName:'Updated firm',version:'4'}});
    expect(c.threshold).toBe('17');expect(c.dirty()).toBe(true);expect(c.ruleReviewed).toBe(false);f.destroy();
  });
});
