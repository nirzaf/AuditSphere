import { Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom, timeout } from 'rxjs';
import { bool, decode, obj } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const bootstrapStatus = obj({ bound: bool, canBootstrap: bool });
export const bootstrapReceipt = obj({ completed: bool, requiresFreshSignIn: bool });
export const ADMINISTRATOR_SIGN_IN = '/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fmicrosoft365%2Ftenant-connection';

@Component({ selector: 'audit-installation-bootstrap', imports: [ReactiveFormsModule, MatButtonModule, ...SHARED], template: `
  <audit-page-header title="Initial administrator setup" description="Only the deployment-approved Microsoft identity can bind the first AuditSphere administrator. This local role does not assign a Microsoft Entra administrator role." />
  <audit-state [loading]="loading()" [error]="error()" label="initial administrator setup" />
  @if (status(); as s) {
    @if (s.canBootstrap) {
      <section class="panel"><h2>Bind your approved identity</h2><p>Microsoft has authenticated this account. Enter the installation proof supplied privately by your deployment operator. AuditSphere never asks for your Microsoft password.</p>
        <form [formGroup]="form" (ngSubmit)="complete()">
          <label for="installation-proof">Installation proof</label><input id="installation-proof" type="password" formControlName="proof" autocomplete="off" maxlength="4096" />
          <label><input type="checkbox" formControlName="reviewed" /> I reviewed this initial firm-wide AuditSphere administrator assignment.</label>
          <button matButton="filled" [disabled]="form.invalid || busy() || uncertain()">Bind initial administrator</button>
        </form>
      </section>
    } @else { <p role="status">Initial setup is already closed. Sign in again to check your current application access, or contact an existing administrator.</p> }
  }
  <audit-command-message [message]="message()" [failed]="failed()" />
  <a matButton [href]="status()?.bound ? administratorSignIn : signIn">Sign in with Microsoft</a>
` })
export class InstallationBootstrap {
  private readonly http = inject(HttpClient);
  readonly form = inject(FormBuilder).nonNullable.group({ proof: ['', [Validators.required, Validators.maxLength(4096)]], reviewed: [false, Validators.requiredTrue] });
  readonly status = signal<ReturnType<typeof bootstrapStatus> | null>(null);
  readonly loading = signal(true); readonly error = signal(''); readonly busy = signal(false); readonly uncertain = signal(false); readonly failed = signal(false); readonly message = signal('');
  readonly signIn = '/auth/sign-in?returnUrl=%2Fui%2Fsetup%2Fmicrosoft365';
  readonly administratorSignIn = ADMINISTRATOR_SIGN_IN;
  private disposed = false;
  constructor() { inject(DestroyRef).onDestroy(() => { this.disposed = true; this.form.reset(); this.status.set(null); }); void this.load(); }
  private async load(): Promise<void> {
    try {
      const value = await firstValueFrom(this.http.get<unknown>('/api/setup/session').pipe(timeout(15000)));
      if (!this.disposed) this.status.set(decode(bootstrapStatus, value));
    } catch (error) {
      if (!this.disposed) this.error.set(error instanceof HttpErrorResponse && error.status === 401 ? 'Sign in with the deployment-approved Microsoft account to start setup.' : 'Initial administrator setup is not available to this identity. Contact your deployment operator if you need access.');
    } finally { if (!this.disposed) this.loading.set(false); }
  }
  async complete(): Promise<void> {
    if (this.form.invalid || !this.status()?.canBootstrap || this.busy() || this.uncertain() || this.disposed) return;
    const request = this.form.getRawValue(); this.form.controls.proof.reset(); this.busy.set(true); this.message.set('');
    try {
      const value = await firstValueFrom(this.http.post<unknown>('/api/setup/bootstrap', request).pipe(timeout(30000)));
      if (this.disposed) return;
      const receipt = decode(bootstrapReceipt, value);
      if (!receipt.completed || !receipt.requiresFreshSignIn) throw new Error('Unsupported bootstrap receipt');
      location.assign(ADMINISTRATOR_SIGN_IN);
    } catch (error) {
      if (this.disposed) return;
      this.failed.set(true);
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
        this.message.set('Setup was not accepted. Check the approved identity and installation proof, or sign in again to review current access.');
      } else {
        this.uncertain.set(true); this.message.set('Setup could not be confirmed. Sign in again to check the recorded access before repeating the operation.');
      }
    } finally { if (!this.disposed) this.busy.set(false); }
  }
}
