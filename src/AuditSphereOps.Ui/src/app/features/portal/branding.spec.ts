import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';
import { decodePortalBranding, PortalBranding } from './branding';
import { SessionService } from '../../core/session';

const identityA = '11111111-1111-4111-8111-111111111111';
const identityB = '22222222-2222-4222-8222-222222222222';
const profileA = '33333333-3333-4333-8333-333333333333';
const profileB = '44444444-4444-4444-8444-444444444444';
const hash = 'a'.repeat(64);
const metadata = (profileId = profileA, legal = 'Easy Guide Audit', logo = true, sha = hash) => ({
  profileId, version: '7', legalName: legal, accentColorHex: '#1A2B3C', hasLogo: logo, logoSha256Hex: logo ? sha : null,
});

function open() {
  TestBed.configureTestingModule({
    imports: [PortalBranding],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const current = signal({ userId: identityA, firmId: identityA, generation: 'a', staff: false });
  const invalidation = signal(0);
  const refresh = vi.fn();
  TestBed.overrideProvider(SessionService, { useValue: { current, invalidation, refresh } });
  const fixture = TestBed.createComponent(PortalBranding);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  TestBed.tick();
  return { fixture, http, current, invalidation };
}

describe('client portal firm branding', () => {
  it('constructs only a protected GUID asset URL and rejects injected or inconsistent metadata', () => {
    expect(decodePortalBranding(metadata()).profileId).toBe(profileA);
    expect(() => decodePortalBranding({ ...metadata(), accentColorHex: 'red; background:url(https://evil)' })).toThrow();
    expect(() => decodePortalBranding({ ...metadata(), legalName: '' })).toThrow();
    expect(() => decodePortalBranding({ ...metadata(), version: '1e3' })).toThrow();
    expect(() => decodePortalBranding({ ...metadata(), hasLogo: false })).toThrow();
    expect(() => decodePortalBranding({ ...metadata(), profileId: null })).toThrow();
  });

  it('renders a same-origin protected logo with accessible text and a Unicode text mark when no logo exists', () => {
    const { fixture, http } = open();
    try {
      http.expectOne('/api/ui/portal/branding').flush(metadata());
      TestBed.tick(); fixture.detectChanges();
      const heading = fixture.nativeElement.querySelector('h2') as HTMLHeadingElement;
      const image = fixture.nativeElement.querySelector('img') as HTMLImageElement;
      expect(heading.textContent?.trim()).toBe('Easy Guide Audit');
      expect(image.getAttribute('src')).toBe(`/api/ui/portal/branding/${profileA}/logo`);
      expect(image.getAttribute('alt')).toBe('Logo of Easy Guide Audit');
      expect(fixture.nativeElement.textContent).toContain('Client document and reporting workspace');

      fixture.componentInstance.branding.reload(); TestBed.tick();
      http.expectOne('/api/ui/portal/branding').flush(metadata(profileB, 'Étoile Áudit', false, ''));
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('img')).toBeNull();
      expect(fixture.nativeElement.querySelector('.brand-mark')?.textContent?.trim()).toBe('ÉÁ');
      expect(fixture.nativeElement.querySelector('.brand-mark')?.getAttribute('aria-hidden')).toBe('true');
      http.expectNone(request => request.method === 'POST');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('hides a broken image, retries only on the operator action, and never displays backend errors', () => {
    const { fixture, http } = open();
    try {
      http.expectOne('/api/ui/portal/branding').flush(metadata());
      TestBed.tick(); fixture.detectChanges();
      (fixture.nativeElement.querySelector('img') as HTMLImageElement).dispatchEvent(new Event('error'));
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('img')).toBeNull();
      expect(fixture.nativeElement.textContent).toContain('Firm logo unavailable. Refresh branding to retry.');
      expect(fixture.nativeElement.textContent).not.toContain('scope.denied');
      http.expectNone('/api/ui/portal/branding');

      (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
      TestBed.tick();
      http.expectOne('/api/ui/portal/branding').flush(metadata());
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('img')?.getAttribute('src'))
        .toBe(`/api/ui/portal/branding/${profileA}/logo`);
      http.expectNone(request => request.method === 'POST');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('clears the previous identity immediately and discards its in-flight metadata after session change', () => {
    const { fixture, http, current, invalidation } = open();
    try {
      const first = http.expectOne('/api/ui/portal/branding');
      first.flush(metadata(profileA, 'Firm A'));
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('h2')?.textContent?.trim()).toBe('Firm A');

      fixture.componentInstance.branding.reload(); TestBed.tick();
      const delayed = http.expectOne('/api/ui/portal/branding');
      current.set({ userId: identityB, firmId: identityB, generation: 'b', staff: false });
      invalidation.update(value => value + 1);
      TestBed.tick(); fixture.detectChanges();
      expect(delayed.cancelled).toBe(true);
      expect(fixture.nativeElement.textContent).not.toContain('Firm A');
      const next = http.expectOne('/api/ui/portal/branding');
      next.flush(metadata(profileB, 'Firm B'));
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('h2')?.textContent?.trim()).toBe('Firm B');
      http.expectNone(request => request.method === 'POST');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });
});
