import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { TabDrafts, DraftScope } from '../../core/tab-drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { firstValueFrom, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { exactDecimal, guidPattern } from '../../core/contracts';
interface Profile {
  version: string;
  legalName: string;
  address: string;
  email: string;
  phone: string;
  accent: string;
  closing: string;
  history: string;
  credentials: string;
  methodology: string;
  hasLogo: boolean;
  logoSha256Hex: string | null;
}
type EditableProfile = Pick<Profile,
  'legalName' | 'address' | 'email' | 'phone' | 'accent' | 'closing' | 'history' | 'credentials' | 'methodology'>;
type LogoAction = 'KEEP' | 'REPLACE' | 'REMOVE';
interface Rule {
  id: string;
  version: string;
  kind: string;
  threshold: string | null;
  role: string;
}
interface Policy {
  id: string;
  version: string;
  currency: string;
  minimumFee: string | null;
  maximumFee: string | null;
  maxDiscountPercent: string | null;
  validityDays: string;
  status: string;
  current: boolean;
  canSubmit: boolean;
  canApprove: boolean;
}
const policyState = (v: unknown): v is string =>
  typeof v === 'string' && ['DRAFT', 'PENDING_APPROVAL', 'APPROVED'].includes(v);
export function decodePolicies(value: unknown): Policy[] {
  if (!Array.isArray(value) || value.length > 100) throw new Error('Invalid pricing policies');
  return value.map((entry) => {
    const p = entry as Record<string, unknown>;
    if (
      !p ||
      typeof p['id'] !== 'string' ||
      !guidPattern.test(p['id']) ||
      !revision(p['version']) ||
      typeof p['currency'] !== 'string' ||
      !/^[A-Z]{3}$/.test(p['currency']) ||
      !(p['minimumFee'] === null || exactDecimal(p['minimumFee'])) ||
      !(p['maximumFee'] === null || exactDecimal(p['maximumFee'])) ||
      !(p['maxDiscountPercent'] === null || exactDecimal(p['maxDiscountPercent'])) ||
      !revision(p['validityDays']) ||
      !policyState(p['status']) ||
      !['current', 'canSubmit', 'canApprove'].every((k) => typeof p[k] === 'boolean')
    )
      throw new Error('Invalid pricing policy');
    return p as unknown as Policy;
  });
}
interface Workspace {
  canEdit: boolean;
  profile: Profile | null;
  rulesRevision: string;
  defaultDiscountThreshold: string;
  defaultRole: string;
  approverRoles: string[];
  rules: Rule[];
}
const revision = (v: unknown): v is string => typeof v === 'string' && /^\d{1,19}$/.test(v);
export function decodeSettings(value: unknown): Workspace {
  if (!value || typeof value !== 'object') throw new Error('Invalid settings');
  const v = value as Record<string, unknown>;
  if (
    typeof v['canEdit'] !== 'boolean' ||
    typeof v['rulesRevision'] !== 'string' ||
    !/^[0-9a-f]{64}$/.test(v['rulesRevision']) ||
    !exactDecimal(v['defaultDiscountThreshold']) ||
    typeof v['defaultRole'] !== 'string' ||
    !Array.isArray(v['approverRoles']) ||
    v['approverRoles'].length > 20 ||
    !v['approverRoles'].every((r) => typeof r === 'string') ||
    !Array.isArray(v['rules']) ||
    v['rules'].length > 100
  )
    throw new Error('Invalid settings');
  let profile: Profile | null = null;
  if (v['profile'] !== null) {
    const rawProfile = v['profile'];
    if (!rawProfile || typeof rawProfile !== 'object' || Array.isArray(rawProfile))
      throw new Error('Invalid profile');
    const p = rawProfile as Record<string, unknown>;
    const hasLegacyLogoMetadata = !Object.prototype.hasOwnProperty.call(p, 'hasLogo') &&
      !Object.prototype.hasOwnProperty.call(p, 'logoSha256Hex');
    const hasLogo = hasLegacyLogoMetadata ? false : p['hasLogo'];
    const logoSha256Hex = hasLegacyLogoMetadata ? null : p['logoSha256Hex'];
    if (
      !p ||
      !revision(p['version']) ||
      ![
        'legalName',
        'address',
        'email',
        'phone',
        'accent',
        'closing',
        'history',
        'credentials',
        'methodology',
      ].every((k) => typeof p[k] === 'string') ||
      typeof hasLogo !== 'boolean' ||
      !(logoSha256Hex === null || typeof logoSha256Hex === 'string' && /^[0-9a-f]{64}$/.test(logoSha256Hex)) ||
      hasLogo !== (logoSha256Hex !== null)
    )
      throw new Error('Invalid profile');
    profile = {
      version: p['version'] as string,
      legalName: p['legalName'] as string,
      address: p['address'] as string,
      email: p['email'] as string,
      phone: p['phone'] as string,
      accent: p['accent'] as string,
      closing: p['closing'] as string,
      history: p['history'] as string,
      credentials: p['credentials'] as string,
      methodology: p['methodology'] as string,
      hasLogo,
      logoSha256Hex: logoSha256Hex as string | null,
    };
  }
  for (const r of v['rules'])
    if (
      !r ||
      typeof r.id !== 'string' ||
      !guidPattern.test(r.id) ||
      !revision(r.version) ||
      typeof r.kind !== 'string' ||
      typeof r.role !== 'string' ||
      !(r.threshold === null || exactDecimal(r.threshold))
    )
      throw new Error('Invalid rule');
  return { ...v, profile } as unknown as Workspace;
}
export interface SettingsDraft {
  profile: EditableProfile;
  kind: string;
  threshold: string;
  role: string;
}
export function settingsDraft(value: unknown): SettingsDraft | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const v = value as Record<string, unknown>, p = v['profile'];
  if (!p || typeof p !== 'object' || Array.isArray(p)) return null;
  const fields = p as Record<string, unknown>;
  const limits = {legalName:200,address:500,email:200,phone:60,accent:7,closing:4000,history:16000,credentials:16000,methodology:16000};
  const clean: Record<string,string> = {};
  for (const [key, limit] of Object.entries(limits)) {
    if (typeof fields[key] !== 'string' || fields[key].length > limit) return null;
    clean[key] = fields[key];
  }
  if (typeof v['kind'] !== 'string' || !['DISCOUNT_OVER_PERCENT','NON_STANDARD_TERMS'].includes(v['kind']) ||
      typeof v['threshold'] !== 'string' || v['threshold'].length > 40 ||
      typeof v['role'] !== 'string' || v['role'].length > 100) return null;
  return {profile:clean as unknown as SettingsDraft['profile'],kind:v['kind'],threshold:v['threshold'],role:v['role']};
}
const blank = (): Profile => ({
  version: '0',
  legalName: '',
  address: '',
  email: '',
  phone: '',
  accent: '#2B6CB0',
  closing: '',
  history: '',
  credentials: '',
  methodology: '',
  hasLogo: false,
  logoSha256Hex: null,
});
@Component({
  selector: 'audit-commercial-settings',
  imports: [RouterLink, FormsModule, MatButtonModule, MatProgressBarModule],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class CommercialSettings {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly draftScope = signal<DraftScope | null>(null);
  private baseline = '';
  private readVersion = 0;
  private logoReadVersion = 0;
  private logoFileInput?: HTMLInputElement;
  private fieldsDraft(): SettingsDraft {
    return {profile:this.editableProfile(),kind:this.kind,threshold:this.threshold,role:this.role};
  }
  private editableProfile(profile: Profile = this.profile): EditableProfile {
    return {
      legalName: profile.legalName,
      address: profile.address,
      email: profile.email,
      phone: profile.phone,
      accent: profile.accent,
      closing: profile.closing,
      history: profile.history,
      credentials: profile.credentials,
      methodology: profile.methodology,
    };
  }
  dirty(): boolean {
    return this.logoAction !== 'KEEP' || this.logoReading() ||
      !!this.baseline && JSON.stringify(this.fieldsDraft()) !== this.baseline;
  }
  saveTabDraft(): boolean {
    if (this.logoAction !== 'KEEP' || this.logoReading()) {
      this.message.set('Logo changes are not saved in tab drafts. Save the reviewed profile or discard the logo change before leaving.');
      return false;
    }
    const scope=this.draftScope();
    if(!scope || !this.data()?.canEdit || this.busy() || this.uncertain()) return false;
    const saved=this.drafts.save(scope,this.fieldsDraft(),settingsDraft);
    this.message.set(saved ? 'Unsubmitted settings tab draft saved without review confirmation.' : 'Tab draft storage is unavailable. Keep this page open to retain edits.');
    return saved;
  }
  recoverTabDraft(): void {
    const scope=this.draftScope(),w=this.data();if(!scope || !w?.canEdit || this.busy() || this.uncertain()) return;
    const recovered=this.drafts.read(scope,settingsDraft);
    if(recovered.state !== 'ready' || recovered.draft.submissionPending) {this.message.set('No compatible unsubmitted settings draft is available.');return;}
    const d=recovered.draft.value;
    if(!w.approverRoles.includes(d.role)) {this.message.set('The saved approver role is no longer available. Review current settings.');return;}
    this.resetLogoIntent();
    this.profile={...blank(),...d.profile,version:w.profile?.version ?? '0',
      hasLogo:w.profile?.hasLogo ?? false,logoSha256Hex:w.profile?.logoSha256Hex ?? null};
    this.kind=d.kind;this.threshold=d.threshold;this.role=d.role;
    this.reviewed=this.ruleReviewed=false;this.deactivation.set(null);this.message.set('Draft fields recovered. Review the exact changes before submitting.');
  }
  async confirmNavigation(): Promise<boolean> {
    if(this.busy() || this.uncertain()) return false;
    if(!this.dirty()) return true;
    const fence=this.fence;
    const choice=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if(fence!==this.fence)return true;
    if(choice==='save') return this.saveTabDraft();
    if(choice==='discard') {const scope=this.draftScope();this.resetLogoIntent();return !scope || this.drafts.clear(scope.entity);}
    return false;
  }
  async refresh(): Promise<void> {
    if(this.busy()) return;
    // Unknown outcomes must be read before acknowledgment; never automatically resubmit.
    if(this.uncertain()) { this.resetLogoIntent(); this.load(); return; }
    if(await this.confirmNavigation()) this.load();
  }
  private read?: Subscription;
  private write?: Subscription;
  private fence = 0;
  readonly data = signal<Workspace | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  logoAction: LogoAction = 'KEEP';
  logoBase64: string | null = null;
  logoSha256: string | null = null;
  logoFileName = '';
  logoByteCount = 0;
  readonly logoReading = signal(false);
  readonly logoMessage = signal('');
  readonly deactivation = signal<Rule | null>(null);
  readonly policies = signal<Policy[]>([]);
  readonly policyReviewed = signal(false);
  readonly canApproveAnyPolicy = computed(() => this.policies().some((x) => x.canApprove));
  policyCurrency = 'QAR';
  policyMinimumFee = '';
  policyMaximumFee = '';
  policyMaxDiscount = '';
  policyValidityDays = '30';
  policyApprovalReason = '';
  policyBusy = signal(false);
  profile = blank();
  reviewed = false;
  ruleReviewed = false;
  kind = 'DISCOUNT_OVER_PERCENT';
  threshold = '10';
  role = 'Partner';
  constructor() {
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.fence++;
        ++this.readVersion;this.baseline='';this.draftScope.set(null);
        this.resetLogoIntent();
        this.read?.unsubscribe();
        this.write?.unsubscribe();
        this.data.set(null);
        this.profile = blank();
        this.reviewed = this.ruleReviewed = false;
        this.deactivation.set(null);
        this.policies.set([]);
        this.policyReviewed.set(false);
        this.policyBusy.set(false);
        this.kind = 'DISCOUNT_OVER_PERCENT';
        this.threshold = '10';
        this.role = 'Partner';
        this.busy.set(false);
        this.uncertain.set(false);
        this.message.set('');
        if (staff) this.load();
      });
    });
    const unload=(event:BeforeUnloadEvent)=>{if(this.dirty() || this.busy() || this.uncertain()){event.preventDefault();event.returnValue='';}};
    window.addEventListener('beforeunload',unload);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('beforeunload',unload);++this.readVersion;this.resetLogoIntent();
      this.fence++;
      this.read?.unsubscribe();
      this.write?.unsubscribe();
    });
  }
  acknowledge(): void {
    if (this.data() && !this.busy()) {
      this.uncertain.set(false);
      this.reviewed = this.ruleReviewed = false;
      this.message.set('Persisted settings reviewed. Review the current change before proceeding.');
    }
  }
  saveProfile(): void {
    const workspace = this.data();
    if (!workspace?.canEdit || !this.reviewed || this.busy() || this.uncertain() || this.logoReading() ||
        this.logoAction === 'REPLACE' && !this.logoBase64) return;
    const fields = this.editableProfile();
    this.command('/profile', {
      version: this.profile.version,
      ...fields,
      reviewed: true,
      logoPngBase64: this.logoAction === 'REPLACE' ? this.logoBase64 : null,
      removeLogo: this.logoAction === 'REMOVE',
    });
  }
  async selectLogo(event: Event): Promise<void> {
    const input = event.currentTarget as HTMLInputElement;
    this.logoFileInput = input;
    const files = input.files;
    const readVersion = ++this.logoReadVersion;
    const fence = this.fence;
    const generation = this.session.invalidation();
    this.logoAction = 'KEEP';
    this.logoBase64 = null;
    this.logoSha256 = null;
    this.logoFileName = '';
    this.logoByteCount = 0;
    this.logoReading.set(false);
    this.reviewed = false;
    this.logoMessage.set('');
    if (!files || files.length !== 1) {
      this.logoMessage.set('Upload a PNG logo of at most 128 KiB.');
      input.value = '';
      return;
    }
    const file = files[0];
    if (!file || !file.name.toLowerCase().endsWith('.png') || file.size < 1 || file.size > 131072 ||
        file.type !== '' && file.type !== 'image/png') {
      this.logoMessage.set('Upload a PNG logo of at most 128 KiB.');
      input.value = '';
      return;
    }
    this.logoReading.set(true);
    this.logoMessage.set('Reading the selected PNG…');
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      if (!this.logoReadIsCurrent(readVersion, fence, generation)) return;
      if (bytes.byteLength < 1 || bytes.byteLength > 131072) {
        this.logoMessage.set('Upload a PNG logo of at most 128 KiB.');
        return;
      }
      const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
      if (!this.logoReadIsCurrent(readVersion, fence, generation)) return;
      const hash = Array.from(digest, value => value.toString(16).padStart(2, '0')).join('');
      let binary = '';
      for (let offset = 0; offset < bytes.length; offset += 0x8000)
        binary += String.fromCharCode(...bytes.subarray(offset, Math.min(offset + 0x8000, bytes.length)));
      this.reviewed = false;
      this.logoAction = 'REPLACE';
      this.logoBase64 = btoa(binary);
      this.logoSha256 = hash;
      this.logoFileName = file.name;
      this.logoByteCount = bytes.byteLength;
      this.logoMessage.set(`${file.name} · ${bytes.byteLength} bytes · SHA-256 ${hash}. Review the profile before saving.`);
    } catch {
      if (this.logoReadIsCurrent(readVersion, fence, generation))
        this.logoMessage.set('Upload a PNG logo of at most 128 KiB.');
    } finally {
      if (this.logoReadIsCurrent(readVersion, fence, generation)) {
        this.logoReading.set(false);
        input.value = '';
      }
    }
  }
  keepCurrentLogo(): void {
    this.resetLogoIntent('Keeping the current firm logo. Review the profile before saving.');
    this.reviewed = false;
  }
  removeCurrentLogo(): void {
    if (!this.profile.hasLogo) return;
    this.resetLogoIntent();
    this.logoAction = 'REMOVE';
    this.logoMessage.set('Logo removal selected. Review the profile before saving.');
    this.reviewed = false;
  }
  private resetLogoIntent(message = ''): void {
    ++this.logoReadVersion;
    this.logoAction = 'KEEP';
    this.logoBase64 = null;
    this.logoSha256 = null;
    this.logoFileName = '';
    this.logoByteCount = 0;
    this.logoReading.set(false);
    this.logoMessage.set(message);
    if (this.logoFileInput) this.logoFileInput.value = '';
  }
  private logoReadIsCurrent(readVersion: number, fence: number, generation: number): boolean {
    return readVersion === this.logoReadVersion && fence === this.fence && generation === this.session.invalidation();
  }
  saveRule(): void {
    const w = this.data();
    if (!w || !this.ruleReviewed || !w.approverRoles.includes(this.role)) return;
    if (this.kind === 'DISCOUNT_OVER_PERCENT' && !exactDecimal(this.threshold)) {
      this.message.set('Enter an exact decimal threshold.');
      return;
    }
    this.command('/rules', {
      kind: this.kind,
      threshold: this.kind === 'DISCOUNT_OVER_PERCENT' ? this.threshold : null,
      role: this.role,
      rulesRevision: w.rulesRevision,
      reviewed: true,
    });
  }
  deactivate(r: Rule): void {
    const w = this.data();
    if (w?.rules.some((x) => x.id === r.id))
      this.command(
        '/rules/' + r.id + '/deactivate',
        { rulesRevision: w.rulesRevision, reviewed: true },
        true,
      );
  }
  savePolicy(): void {
    if (!this.policyReviewed() || !this.policyValidity('saving')) return;
    this.policyCommand('', {
      currency: this.policyCurrency.trim().toUpperCase(),
      minimumFee: this.policyMinimumFee.trim() || null,
      maximumFee: this.policyMaximumFee.trim() || null,
      maxDiscount: this.policyMaxDiscount.trim() || null,
      validityDays: this.policyValidityDays.trim(),
      expectedRevision: null,
      reviewed: true,
    });
  }
  submitPolicy(p: Policy): void {
    if (!p.canSubmit || !this.policies().some((x) => x.id === p.id)) return;
    this.policyCommand('/' + p.id + '/submit', {}, true);
  }
  approvePolicy(p: Policy, reason: string): void {
    const trimmed = reason.trim();
    if (!p.canApprove || !this.policies().some((x) => x.id === p.id)) return;
    if (trimmed.length < 5 || trimmed.length > 1000) {
      this.message.set('Record an approval reason of 5 to 1000 characters before approving.');
      return;
    }
    this.policyCommand('/' + p.id + '/approve', { reason: trimmed, reviewed: true }, true);
  }
  private policyValidity(action: string): boolean {
    if (!/^[A-Za-z]{3}$/.test(this.policyCurrency.trim())) {
      this.message.set('Enter a three-letter policy currency before ' + action + '.');
      return false;
    }
    for (const value of [this.policyMinimumFee, this.policyMaximumFee, this.policyMaxDiscount]) {
      if (value.trim() && !exactDecimal(value.trim())) {
        this.message.set('Policy limits must be exact decimals or left empty.');
        return false;
      }
    }
    const days = this.policyValidityDays.trim();
    if (!/^\d{1,3}$/.test(days) || Number(days) < 1 || Number(days) > 365) {
      this.message.set('Enter a validity window of 1 to 365 days.');
      return false;
    }
    return true;
  }
  private policyCommand(path: string, body: object, noContent = false): void {
    if (!this.data()?.canEdit || this.policyBusy() || this.busy() || this.uncertain()) return;
    const fence = this.fence;
    this.policyBusy.set(true);
    this.message.set('Saving reviewed pricing policy…');
    this.http
      .post<unknown>('/api/ui/commercial-settings/pricing-policy' + path, body)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.policyBusy.set(false);
          const r = value as { id?: unknown };
          if (!noContent && (typeof r?.id !== 'string' || !guidPattern.test(r.id))) {
            this.uncertain.set(true);
            this.data.set(null);
            this.message.set('Pricing policy outcome unconfirmed. Refresh persisted settings.');
            return;
          }
          this.policyReviewed.set(false);
          this.policyApprovalReason = '';
          this.message.set(
            'Pricing policy recorded. Existing quotations and documents retain their original terms.',
          );
          this.loadPolicies();
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.policyBusy.set(false);
          this.policyReviewed.set(false);
          this.uncertain.set(!(failure.status >= 400 && failure.status < 500));
          this.data.set(null);
          this.message.set(
            this.uncertain()
              ? 'Pricing policy outcome unconfirmed. Refresh before another command.'
              : 'Pricing policy refused. Check current authority, the reviewed confirmation and field limits.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  loadPolicies(): void {
    const fence = this.fence;
    this.http
      .get<unknown>('/api/ui/commercial-settings/pricing-policy')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            this.policies.set(decodePolicies(value));
          } catch {
            this.policies.set([]);
            this.message.set('Unsupported pricing policy response; refresh.');
          }
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  private command(path: string, body: object, noContent = false): void {
    if (!this.data()?.canEdit || this.busy() || this.uncertain()) return;
    const fence = this.fence;
    this.busy.set(true);
    this.message.set('Saving reviewed settings…');
    this.write = this.http
      .post<unknown>('/api/ui/commercial-settings' + path, body)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          const r = value as { id?: unknown };
          if (!noContent && (typeof r?.id !== 'string' || !guidPattern.test(r.id))) {
            this.uncertain.set(true);
            this.data.set(null);
            this.message.set('Outcome unconfirmed. Refresh persisted settings.');
            return;
          }
          this.reviewed = this.ruleReviewed = false;
          this.deactivation.set(null);
          this.message.set(
            'Settings recorded. Existing documents and quotation approvals retain their original identities.',
          );
          const scope=this.draftScope();if(scope)this.drafts.clear(scope.entity);
          if (path === '/profile') this.resetLogoIntent();
          this.load(path === '/profile' ? 'rule' : 'profile');
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.reviewed = this.ruleReviewed = false;
          this.uncertain.set(!(failure.status >= 400 && failure.status < 500));
          this.data.set(null);
          this.message.set(
            this.uncertain()
              ? 'Outcome unconfirmed. Refresh and review persisted settings before another command.'
              : 'Settings refused. Refresh and check current authority, reviewed revision and field limits.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(preserve?: 'profile' | 'rule'): void {
    const previous=this.fieldsDraft();
    const previousBase=this.baseline ? settingsDraft(JSON.parse(this.baseline)) : null;
    const previousProfileVersion=this.profile.version;
    const pendingLogo=this.logoAction !== 'KEEP' || this.logoReading();
    const readVersion=++this.readVersion;this.draftScope.set(null);
    this.read?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.reviewed = this.ruleReviewed = false;
    this.deactivation.set(null);
    if (!this.session.current()?.staff) return;
    const fence = this.fence;
    this.loading.set(true);
    this.read = this.http
      .get<unknown>('/api/ui/commercial-settings')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            const w = decodeSettings(value);
            this.data.set(w);
            this.loadPolicies();
            const persisted=w.profile ? { ...w.profile } : blank();
            this.profile=preserve === 'profile'
              ? {...persisted,...previous.profile,version:persisted.version,hasLogo:persisted.hasLogo,logoSha256Hex:persisted.logoSha256Hex}
              : persisted;
            if (previousProfileVersion !== persisted.version)
              this.resetLogoIntent(pendingLogo ? 'Firm profile changed; select the logo again after review.' : '');
            this.kind=preserve === 'rule' ? previous.kind : 'DISCOUNT_OVER_PERCENT';
            this.threshold=preserve === 'rule' ? previous.threshold : w.defaultDiscountThreshold;
            this.role=preserve === 'rule' ? previous.role : w.defaultRole;
            const baseProfile=this.editableProfile(persisted);
            this.baseline=JSON.stringify({profile:baseProfile,
              kind:preserve === 'rule' ? previousBase?.kind ?? 'DISCOUNT_OVER_PERCENT' : this.kind,
              threshold:preserve === 'rule' ? previousBase?.threshold ?? w.defaultDiscountThreshold : this.threshold,
              role:preserve === 'rule' ? previousBase?.role ?? w.defaultRole : this.role});
            const generation=this.session.invalidation();
            void crypto.subtle.digest('SHA-256',new TextEncoder().encode(JSON.stringify([w.profile?.version ?? '0',w.rulesRevision]))).then(bytes=>{
              if(readVersion!==this.readVersion || fence!==this.fence || generation!==this.session.invalidation())return;
              const baseRevision=Array.from(new Uint8Array(bytes),x=>x.toString(16).padStart(2,'0')).join('');
              this.draftScope.set({entity:'commercial-settings',baseRevision});
            });
          } catch {
            this.error.set('Unsupported settings response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.loading.set(false);
          this.error.set(
            'Commercial settings unavailable. Current firm-wide commercial access is required.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
