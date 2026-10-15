import { Component, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { bool, guid, nullable, obj, sha256, str } from '../../core/decode';

const version = (value: unknown, path: string): string => {
  const decoded = str(19)(value, path);
  if (!/^\d{1,19}$/.test(decoded)) throw new Error(`${path}: expected a version string`);
  return decoded;
};
const legalName = (value: unknown, path: string): string => {
  const decoded = str(200)(value, path);
  if (!decoded.trim()) throw new Error(`${path}: expected a legal name`);
  return decoded;
};
const accent = (value: unknown, path: string): string => {
  const decoded = str(7)(value, path);
  if (!/^#[\da-fA-F]{6}$/.test(decoded)) throw new Error(`${path}: expected a six-digit accent color`);
  return decoded;
};
const brandingShape = obj({
  profileId: nullable(guid), version, legalName, accentColorHex: accent,
  hasLogo: bool, logoSha256Hex: nullable(sha256),
});

export const decodePortalBranding = (value: unknown, path = '') => {
  const decoded = brandingShape(value, path);
  const validLogo = decoded.profileId !== null && decoded.logoSha256Hex !== null;
  if (decoded.hasLogo !== validLogo || (decoded.profileId === null && decoded.version !== '0'))
    throw new Error(`${path || 'branding'}: inconsistent branding metadata`);
  return decoded;
};

@Component({
  selector: 'audit-portal-branding',
  imports: [MatButtonModule],
  template: `
    @if (branding.loading()) {
      <p class="branding-status" role="status">Loading firm branding…</p>
    } @else if (branding.data(); as current) {
      <section class="portal-branding" aria-label="Firm branding" [style.border-top-color]="current.accentColorHex">
        @if (current.hasLogo && !logoUnavailable()) {
          <div class="logo-frame" [style.border-color]="current.accentColorHex">
            <img [src]="logoUrl()" [alt]="'Logo of ' + current.legalName" (error)="logoUnavailable.set(true)" />
          </div>
        } @else if (!current.hasLogo) {
          <div class="brand-mark" aria-hidden="true" [style.border-color]="current.accentColorHex">{{ initials() }}</div>
        }
        <div class="brand-copy">
          <h2>{{ current.legalName }}</h2>
          <p>Client document and reporting workspace</p>
          @if (current.hasLogo && logoUnavailable()) {
            <p role="status">Firm logo unavailable. Refresh branding to retry.</p>
            <button matButton type="button" (click)="refresh()">Refresh branding</button>
          }
        </div>
      </section>
    } @else {
      <section class="portal-branding" aria-label="Firm branding">
        <div class="brand-mark" aria-hidden="true">A</div>
        <div class="brand-copy">
          <h2>AuditSphere</h2>
          <p>Client document and reporting workspace</p>
          <p role="status">Firm branding is unavailable.</p>
          <button matButton type="button" (click)="refresh()">Refresh branding</button>
        </div>
      </section>
    }
  `,
  styles: `
    :host { display: block; color: var(--as-ink); }
    .portal-branding { display: flex; align-items: center; gap: 1rem; min-width: 0; border-top: 4px solid; border-radius: .25rem; padding: 1rem; }
    .logo-frame, .brand-mark { display: grid; flex: 0 0 auto; place-items: center; width: 160px; max-width: 38vw; height: 72px; border: 1px solid; border-radius: .25rem; }
    .logo-frame img { display: block; max-width: 100%; max-height: 64px; width: auto; height: auto; object-fit: contain; }
    .brand-mark { width: 64px; height: 64px; font-size: 1.25rem; font-weight: 700; }
    .brand-copy { min-width: 0; }
    h2 { margin: 0; overflow-wrap: anywhere; color: var(--as-ink); }
    p { margin: .25rem 0 0; color: var(--as-text-muted); }
    .branding-status { color: var(--as-text-muted); }
    @media (max-width: 390px) { .portal-branding { align-items: flex-start; gap: .75rem; padding: .75rem; } .logo-frame { width: 120px; } }
  `,
})
export class PortalBranding {
  private readonly api = inject(Api);
  readonly branding = this.api.resource(() => '/api/ui/portal/branding', decodePortalBranding,
    'Firm branding is unavailable.');
  readonly logoUnavailable = signal(false);
  readonly initials = computed(() => {
    const name = this.branding.data()?.legalName ?? 'AuditSphere';
    return name.trim().split(/\s+/u).filter(Boolean).slice(0, 2)
      .map(word => Array.from(word)[0] ?? '').join('');
  });
  readonly logoUrl = computed(() => {
    const value = this.branding.data();
    return value?.hasLogo && value.profileId
      ? `/api/ui/portal/branding/${value.profileId}/logo`
      : '';
  });

  private previousAssetIdentity = '';
  constructor() {
    effect(() => {
      const value = this.branding.data();
      const identity = value?.hasLogo ? `${value.profileId}:${value.logoSha256Hex}` : '';
      if (identity !== this.previousAssetIdentity) {
        this.previousAssetIdentity = identity;
        this.logoUnavailable.set(false);
      }
      if (!value) this.logoUnavailable.set(false);
    });
  }

  refresh(): void {
    this.logoUnavailable.set(false);
    this.branding.reload();
  }
}
