import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Api, CommandState } from '../../core/api';
import { arr, date, decode, guid, instant, nat, nullable, obj, oneOf, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import type { NavigationProtected } from '../../core/unsaved-changes';

export const GENDERS = [['FEMALE', 'Female'], ['MALE', 'Male'], ['OTHER', 'Other'], ['PREFER_NOT_TO_SAY', 'Prefer not to say']] as const;
export const decodeUserProfile = obj({
  userId: guid, userType: oneOf('Staff', 'Client'), roles: arr(str(80), 50), fullName: str(300), email: str(320),
  position: nullable(str(120)), gender: nullable(oneOf('FEMALE', 'MALE', 'OTHER', 'PREFER_NOT_TO_SAY')),
  dateOfBirth: nullable(date), mobileNumber: nullable(str(24)), joinedDate: nullable(date), revision: nat,
  updatedAt: nullable(instant),
});
export type UserProfile = ReturnType<typeof decodeUserProfile>;
type ProfileForm = { position: string; gender: string; dateOfBirth: string; mobileNumber: string; joinedDate: string };

const blankForm = (): ProfileForm => ({ position: '', gender: '', dateOfBirth: '', mobileNumber: '', joinedDate: '' });
function formFrom(p: UserProfile): ProfileForm {
  return { position: p.position ?? '', gender: p.gender ?? '', dateOfBirth: p.dateOfBirth ?? '',
    mobileNumber: p.mobileNumber ?? '', joinedDate: p.joinedDate ?? '' };
}

/** The signed-in user's own profile: identity details from the sign-in provider, and optional self-maintained details. */
@Component({
  selector: 'audit-user-profile-page',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, ...SHARED],
  styles: [`
    .profile-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: .75rem 1rem; margin: 1rem 0; }
    .required-tag { margin-inline-start: .375rem; font-size: .75rem; font-weight: 600; color: var(--as-accent-ink, #1e4fa8); }
    .actions { display: flex; flex-wrap: wrap; gap: .5rem; }
    a.external { display: inline-flex; align-items: center; min-height: 44px; }
  `],
  template: `
    <audit-page-header title="My profile" eyebrow="Your account"
      description="Your sign-in identity, and the personal and employment details you keep up to date yourself." />
    <audit-state [loading]="view.loading()" [error]="view.error()" label="your profile" />
    @if (mismatch()) {
      <p role="alert">This profile does not match the signed-in account. Reload the page.</p>
    } @else if (current(); as p) {
      <section class="panel" aria-labelledby="profile-identity-heading">
        <h2 id="profile-identity-heading">Account</h2>
        <dl class="facts">
          <dt>User type</dt><dd>{{ p.userType === 'Staff' ? 'Firm staff' : 'Client user' }}</dd>
          <dt>Access roles</dt><dd>{{ p.roles.length ? p.roles.join(', ') : 'No current role grants' }}</dd>
          <dt>Full name<span class="required-tag">Required</span></dt><dd>{{ p.fullName || 'Not yet provided by your sign-in account' }}</dd>
          <dt>Email<span class="required-tag">Required</span></dt><dd>{{ p.email }}</dd>
        </dl>
        @if (microsoft()) {
          <p class="hint">Your full name and email come from your Microsoft 365 account and refresh each time you sign in.
            Change them in Microsoft.</p>
          <a class="external" href="https://myaccount.microsoft.com/personal-info" target="_blank" rel="noopener noreferrer">
            Manage Microsoft profile <span class="sr-only">(opens in a new tab)</span></a>
        } @else {
          <p class="hint">Your full name and email come from your sign-in account. Ask your AuditSphere administrator to change them.</p>
        }
      </section>
      <form class="panel" aria-labelledby="profile-details-heading" (ngSubmit)="save()">
        <h2 id="profile-details-heading">Personal and employment details</h2>
        <p class="hint">All optional. Update them whenever they change; AuditSphere shows them only to you.</p>
        <div class="profile-grid">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Position</mat-label>
            <input matInput name="position" [(ngModel)]="form.position" maxlength="120" autocomplete="organization-title" />
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Gender</mat-label>
            <select matNativeControl name="gender" [(ngModel)]="form.gender">
              <option value="">Not provided</option>
              @for (g of genders; track g[0]) { <option [value]="g[0]">{{ g[1] }}</option> }
            </select>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Date of birth</mat-label>
            <input matInput type="date" name="dateOfBirth" [(ngModel)]="form.dateOfBirth" min="1900-01-01" [max]="today" autocomplete="bday" />
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Mobile number</mat-label>
            <input matInput type="tel" name="mobileNumber" [(ngModel)]="form.mobileNumber" maxlength="24" autocomplete="tel" />
            <mat-hint>Include the country code, for example +974 5555 1234.</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Joined date</mat-label>
            <input matInput type="date" name="joinedDate" [(ngModel)]="form.joinedDate" min="1900-01-01" [max]="today" />
          </mat-form-field>
        </div>
        <div class="actions">
          <button matButton="filled" type="submit" [disabled]="!dirty() || cmd.busy() || cmd.uncertain()">
            {{ cmd.busy() ? 'Saving…' : 'Save profile' }}</button>
          <button matButton type="button" (click)="discard()" [disabled]="!dirty() || cmd.busy()">Discard changes</button>
          @if (cmd.uncertain() || cmd.code() === 'revision.stale') {
            <button matButton type="button" (click)="reload()">Reload profile</button>
          }
        </div>
        <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
        <p class="hint">{{ p.updatedAt ? 'Last updated ' + p.updatedAt.slice(0, 16).replace('T', ' ') + ' UTC.' : 'Not saved yet.' }}</p>
      </form>
    }
  `,
})
export class UserProfilePage implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly cmd = new CommandState(this.api);
  readonly genders = GENDERS;
  readonly today = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
  readonly view = this.api.resource(() => '/api/ui/me/profile', decodeUserProfile, 'Your profile is unavailable.');
  private readonly saved = signal<UserProfile | null>(null);
  readonly current = computed(() => this.saved() ?? this.view.data());
  readonly microsoft = computed(() => this.session.current()?.microsoftAccount === true);
  readonly mismatch = computed(() => {
    const profile = this.current(), account = this.session.current();
    return !!profile && !!account && profile.userId !== account.userId;
  });
  form: ProfileForm = blankForm();
  private baseline = JSON.stringify(this.form);
  /** Revision the current edits started from; a save is fenced to it, never to a later background read. */
  private editRevision = 0;

  constructor() {
    // A different identity clears the saved copy and any edits.
    effect(() => { this.session.invalidation(); untracked(() => { this.saved.set(null); this.clearForm(); }); });
    // Session refreshes reload the profile; a reload must never overwrite edits in progress.
    effect(() => {
      const profile = this.current();
      untracked(() => { if (profile && !this.dirty()) this.resetForm(profile); });
    });
  }

  dirty(): boolean { return JSON.stringify(this.form) !== this.baseline; }

  async save(): Promise<void> {
    const profile = this.current();
    if (!profile || !this.dirty()) return;
    const f = this.form;
    await this.cmd.run<unknown>('/api/ui/me/profile', {
      position: f.position.trim() || null, gender: f.gender || null, dateOfBirth: f.dateOfBirth || null,
      mobileNumber: f.mobileNumber.trim() || null, joinedDate: f.joinedDate || null, expectedRevision: this.editRevision,
    }, 'Profile saved.', value => {
      try { const updated = decode(decodeUserProfile, value); this.resetForm(updated); this.saved.set(updated); }
      catch { this.cmd.message.set('Saved, but the response could not be read. Reload your profile.'); this.cmd.failed.set(true); }
    }, 'PUT');
  }

  discard(): void { const profile = this.current(); if (profile) this.resetForm(profile); }

  reload(): void {
    this.clearForm();
    this.saved.set(null);
    this.cmd.uncertain.set(false);
    this.cmd.message.set('');
    this.view.reload();
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.cmd.busy() || this.cmd.uncertain() || this.dirty()) { event.preventDefault(); event.returnValue = ''; }
  }

  confirmNavigation(): boolean {
    return !this.dirty() || window.confirm('Discard your unsaved profile changes?');
  }

  private resetForm(profile: UserProfile): void {
    this.form = formFrom(profile);
    this.baseline = JSON.stringify(this.form);
    this.editRevision = profile.revision;
  }

  private clearForm(): void {
    this.form = blankForm();
    this.baseline = JSON.stringify(this.form);
  }
}
