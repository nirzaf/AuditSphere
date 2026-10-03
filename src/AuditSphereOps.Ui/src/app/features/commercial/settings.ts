import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
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
}
interface Rule {
  id: string;
  version: string;
  kind: string;
  threshold: string | null;
  role: string;
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
  if (v['profile'] !== null) {
    const p = v['profile'] as Record<string, unknown>;
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
      ].every((k) => typeof p[k] === 'string')
    )
      throw new Error('Invalid profile');
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
  return v as unknown as Workspace;
}
export interface SettingsDraft {
  profile: Omit<Profile, 'version'>;
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
});
@Component({
  selector: 'audit-commercial-settings',
  imports: [RouterLink, FormsModule, MatButtonModule, MatProgressBarModule],
  template: `
    <a routerLink="/app/practice/leads">Leads and opportunities</a>
    <h1>Commercial settings</h1>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading commercial settings" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
    <button matButton [disabled]="busy()" (click)="refresh()">Refresh settings</button>
    @if (data(); as w) {
      @if (!w.canEdit) {
        <p>
          Settings are read-only. A current firm-wide Administrator or Partner is required to edit.
        </p>
      }
      @if(w.canEdit) {
        <p>Unsubmitted drafts stay in this tab for up to four hours and require the same identity and current settings revision. Review confirmations are never saved.</p>
        <button matButton [disabled]="busy() || uncertain() || !draftScope()" (click)="saveTabDraft()">Save settings tab draft</button>
        <button matButton [disabled]="busy() || uncertain() || !draftScope()" (click)="recoverTabDraft()">Recover settings tab draft</button>
      }
      <h2>Firm letterhead</h2>
      <p>
        Version {{ w.profile?.version ?? 'Not configured' }}. Changes create new profile versions;
        generated documents retain their original version.
      </p>
      <form (ngSubmit)="saveProfile()">
        <fieldset [disabled]="!w.canEdit || busy() || uncertain()">
          <legend>Reviewed firm profile</legend>
          <label
            >Legal name<input
              name="legalName"
              [(ngModel)]="profile.legalName" (ngModelChange)="reviewed = false"
              required
              maxlength="200"
          /></label>
          <label
            >Address<textarea
              name="address"
              [(ngModel)]="profile.address" (ngModelChange)="reviewed = false"
              maxlength="500"
            ></textarea>
          </label>
          <label
            >Contact email<input
              name="email"
              type="email"
              [(ngModel)]="profile.email" (ngModelChange)="reviewed = false"
              maxlength="200"
          /></label>
          <label
            >Contact phone<input name="phone" [(ngModel)]="profile.phone" (ngModelChange)="reviewed = false" maxlength="60"
          /></label>
          <label
            >Accent colour (#RRGGBB)<input
              name="accent"
              [(ngModel)]="profile.accent" (ngModelChange)="reviewed = false"
              required
              maxlength="7"
          /></label>
          <label
            >Closing text<textarea
              name="closing"
              [(ngModel)]="profile.closing" (ngModelChange)="reviewed = false"
              maxlength="4000"
            ></textarea>
          </label>
          <label
            >Approved firm history and commercial registrations<textarea
              name="history"
              [(ngModel)]="profile.history" (ngModelChange)="reviewed = false"
              maxlength="16000"
            ></textarea>
          </label>
          <label
            >Approved industry credentials and portfolio evidence<textarea
              name="credentials"
              [(ngModel)]="profile.credentials" (ngModelChange)="reviewed = false"
              maxlength="16000"
            ></textarea>
          </label>
          <label
            >Approved audit methodology overview<textarea
              name="methodology"
              [(ngModel)]="profile.methodology" (ngModelChange)="reviewed = false"
              maxlength="16000"
            ></textarea>
          </label>
          <label
            ><input type="checkbox" name="reviewed" [(ngModel)]="reviewed" />I reviewed this firm
            profile and confirm the new version.</label
          >
          <button matButton type="submit" [disabled]="!reviewed">Save letterhead version</button>
        </fieldset>
      </form>
      <h2>Approval matrix</h2>
      <p>
        The highest exceeded discount band applies. Non-standard terms require every configured
        role. With no active rules, the default is {{ w.defaultRole }} for discounts over
        {{ w.defaultDiscountThreshold }}% and non-standard terms. Existing quotation versions retain
        their recorded requirements.
      </p>
      <div class="table-scroll">
        <table>
          <caption>
            Active approval rules
          </caption>
          <thead>
            <tr>
              <th>Kind</th>
              <th>Threshold</th>
              <th>Role</th>
              <th>Version</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (r of w.rules; track r.id) {
              <tr>
                <td>{{ r.kind }}</td>
                <td>{{ r.threshold ?? 'Not applicable' }}</td>
                <td>{{ r.role }}</td>
                <td>{{ r.version }}</td>
                <td>
                  @if (w.canEdit) {
                    <button
                      matButton
                      [disabled]="busy() || uncertain()"
                      (click)="deactivation.set(r)"
                    >
                      Review deactivation
                    </button>
                  }
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="5">No configured rules; the safe default applies.</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (w.canEdit) {
        <form (ngSubmit)="saveRule()">
          <fieldset [disabled]="busy() || uncertain()">
            <legend>Add or change approval rule</legend>
            <label
              >Rule<select name="kind" [(ngModel)]="kind" (ngModelChange)="ruleReviewed = false">
                <option value="DISCOUNT_OVER_PERCENT">Discount over threshold</option>
                <option value="NON_STANDARD_TERMS">Non-standard terms</option>
              </select></label
            >
            @if (kind === 'DISCOUNT_OVER_PERCENT') {
              <label
                >Threshold %<input name="threshold" [(ngModel)]="threshold" (ngModelChange)="ruleReviewed = false" inputmode="decimal"
              /></label>
            }
            <label
              >Required approver role<select name="role" [(ngModel)]="role" (ngModelChange)="ruleReviewed = false">
                @for (r of w.approverRoles; track r) {
                  <option [value]="r">{{ r }}</option>
                }
              </select></label
            >
            <label
              ><input type="checkbox" name="ruleReviewed" [(ngModel)]="ruleReviewed" />I reviewed
              the effect on future quotation approvals.</label
            >
            <button matButton type="submit" [disabled]="!ruleReviewed">Save approval rule</button>
          </fieldset>
        </form>
      }
      @if (deactivation(); as r) {
        <section role="alert">
          <h3>Deactivate {{ r.kind }} · {{ r.role }} · version {{ r.version }}?</h3>
          <p>
            Future quotations use remaining rules or the safe default. Existing approvals retain
            their original requirements.
          </p>
          <button matButton [disabled]="busy() || uncertain()" (click)="deactivate(r)">
            Confirm rule deactivation</button
          ><button matButton [disabled]="busy()" (click)="deactivation.set(null)">Cancel</button>
        </section>
      }
      @if (uncertain()) {
        <button matButton [disabled]="busy()" (click)="acknowledge()">
          I reviewed the persisted settings outcome
        </button>
      }
    }
    <p role="status">{{ message() }}</p>
  `,
})
export class CommercialSettings {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly draftScope = signal<DraftScope | null>(null);
  private baseline = '';
  private readVersion = 0;
  private fieldsDraft(): SettingsDraft {
    const {version: _version, ...profile} = this.profile;
    return {profile,kind:this.kind,threshold:this.threshold,role:this.role};
  }
  dirty(): boolean { return !!this.baseline && JSON.stringify(this.fieldsDraft()) !== this.baseline; }
  saveTabDraft(): boolean {
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
    this.profile={...d.profile,version:w.profile?.version ?? '0'};this.kind=d.kind;this.threshold=d.threshold;this.role=d.role;
    this.reviewed=this.ruleReviewed=false;this.deactivation.set(null);this.message.set('Draft fields recovered. Review the exact changes before submitting.');
  }
  async confirmNavigation(): Promise<boolean> {
    if(this.busy() || this.uncertain()) return false;
    if(!this.dirty()) return true;
    const fence=this.fence;
    const choice=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if(fence!==this.fence)return true;
    if(choice==='save') return this.saveTabDraft();
    if(choice==='discard') {const scope=this.draftScope();return !scope || this.drafts.clear(scope.entity);}
    return false;
  }
  async refresh(): Promise<void> {
    if(this.busy()) return;
    // Unknown outcomes must be read before acknowledgment; never automatically resubmit.
    if(this.uncertain() || await this.confirmNavigation()) this.load();
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
  readonly deactivation = signal<Rule | null>(null);
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
        this.read?.unsubscribe();
        this.write?.unsubscribe();
        this.data.set(null);
        this.profile = blank();
        this.reviewed = this.ruleReviewed = false;
        this.deactivation.set(null);
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
      window.removeEventListener('beforeunload',unload);++this.readVersion;
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
    if (this.reviewed) this.command('/profile', { ...this.profile, reviewed: true });
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
            const persisted=w.profile ? { ...w.profile } : blank();
            this.profile=preserve === 'profile' ? {...previous.profile,version:persisted.version} : persisted;
            this.kind=preserve === 'rule' ? previous.kind : 'DISCOUNT_OVER_PERCENT';
            this.threshold=preserve === 'rule' ? previous.threshold : w.defaultDiscountThreshold;
            this.role=preserve === 'rule' ? previous.role : w.defaultRole;
            const {version:_version,...baseProfile}=persisted;
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
