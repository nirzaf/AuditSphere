import { Component, ElementRef, effect, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionService } from './session';

@Component({
  selector: 'audit-user-account',
  imports: [RouterLink],
  template: `
    @if (session.current(); as account) {
      <details #panel tabindex="-1" (keydown.escape)="close($event)">
        <summary aria-label="Your account and profile">
          <span class="avatar" aria-hidden="true">{{ (account.displayName || account.email || '?').slice(0, 1).toUpperCase() }}</span>
          <span class="identity"><span class="name">{{ account.displayName || account.email || 'Your account' }}</span>
            <small>{{ account.email || 'Signed in' }}</small></span>
          <span aria-hidden="true">▾</span>
        </summary>
        <section aria-label="Your profile" class="profile">
          <h2>Your profile</h2>
          <p class="account-name">{{ account.displayName || 'Name unavailable' }}</p>
          <p>{{ account.email || 'Email unavailable' }}</p>
          <p>{{ account.staff ? 'Firm workspace' : 'Client portal' }}</p>
          @if (account.staff) {
            <a routerLink="/app/profile" (click)="dismiss()">View and update your profile</a>
          }
          @if (account.microsoftAccount) {
            <p>Your identity is managed by your Microsoft organization. Use the same account shown above.</p>
            <a href="https://myaccount.microsoft.com/personal-info" target="_blank" rel="noopener noreferrer">
              Manage Microsoft profile <span class="sr-only">(opens in a new tab)</span>
            </a>
          } @else {
            <p>For changes to this account’s details, contact your AuditSphere administrator.</p>
          }
        </section>
      </details>
    }
  `,
  styles: `
    :host { display: block; flex: 0 1 18rem; min-width: 0; max-width: 18rem; }
    details { position: relative; }
    summary { display: flex; align-items: center; gap: .5rem; min-height: 44px; cursor: pointer;
      border: 1px solid #64748b; border-radius: .5rem; padding: .3rem .6rem; box-sizing: border-box; }
    summary::-webkit-details-marker { display: none; }
    summary:focus-visible { outline: 3px solid white; outline-offset: 2px; }
    .avatar { display: grid; place-items: center; flex: none; width: 2rem; height: 2rem;
      border-radius: 50%; background: #1d4ed8; color: white; font-weight: 700; }
    .identity { display: grid; min-width: 0; }
    .identity > * { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    small { color: #cbd5e1; }
    .profile { position: absolute; inset-inline-end: 0; top: calc(100% + .5rem); z-index: 100;
      box-sizing: border-box; width: min(22rem, calc(100vw - 1.5rem)); max-height: 70vh; overflow: auto;
      padding: 1rem; border: 1px solid #64748b; border-radius: .5rem; background: white; color: #12385d;
      box-shadow: 0 6px 20px #0003; overflow-wrap: anywhere; }
    h2 { margin: 0 0 .75rem; font-size: 1.125rem; }
    p { margin: .5rem 0; }
    .account-name { font-weight: 700; }
    a { display: inline-flex; align-items: center; min-height: 44px; color: #0b5cab; }
    a:focus-visible { outline: 3px solid #075a9c; outline-offset: 2px; }
    @media (max-width: 700px) { :host { flex: 0 0 auto; } .identity { display: none; } }
  `,
})
export class UserAccount {
  readonly session = inject(SessionService);
  private readonly panel = viewChild<ElementRef<HTMLDetailsElement>>('panel');
  constructor() {
    effect(() => {
      this.session.current();
      this.session.invalidation();
      // Never leave another identity's account panel open after session changes.
      const panel = this.panel()?.nativeElement;
      if (panel) panel.open = false;
    });
  }
  dismiss(): void {
    const panel = this.panel()?.nativeElement;
    if (panel) panel.open = false;
  }
  close(event: Event): void {
    const panel = this.panel()?.nativeElement;
    if (!panel?.open) return;
    event.preventDefault(); event.stopPropagation();
    panel.open = false;
    panel.querySelector('summary')?.focus();
  }
}
