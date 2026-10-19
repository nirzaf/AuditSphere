import { afterEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { decodeSearch, migratedHref } from './search';
import { GlobalSearch } from './search';
const identity = '11111111-1111-4111-8111-111111111111';
describe('Authorized navigation search contract', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });
  it('rejects external and unsafe links', () => {
    for (const href of ['https://example.com', '//example.com/app', '/app\\evil', '/app?token=x']) {
      expect(() =>
        decodeSearch({
          term: 'client',
          truncated: false,
          hits: [{ kind: 'Client', title: 'Client', detail: '', href }],
        }),
      ).toThrow();
    }
  });
  it('keeps ownership explicit', () => {
    expect(migratedHref('/app')).toBe('/ui/app');
    expect(migratedHref('/app/clients/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')).toContain(
      '/ui/app/clients/',
    );
    expect(migratedHref('/app/practice/commercial-settings')).toBe(
      '/ui/app/practice/commercial-settings',
    );
    expect(migratedHref('/app/finance')).toBe('/ui/app/finance');
    expect(migratedHref('/app/engagements/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/pbc')).toBe(
      '/ui/app/engagements/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/pbc',
    );
    expect(migratedHref('/app/practice/invoices/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')).toContain('/ui/app/practice/invoices/');
    expect(migratedHref('/app/library/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')).toContain('/ui/app/library/');
    expect(migratedHref('/app/unowned')).toBeNull();
    expect(migratedHref('/app/clients/' + '-'.repeat(36))).toBeNull();
    expect(migratedHref('/app/clients/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?returnUrl=https://example.test')).toBeNull();
    expect(migratedHref('//example.test/app')).toBeNull();
  });
  it('shows a safe retry state when the search request times out', async () => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      imports: [GlobalSearch],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({
      userId: identity,
      firmId: identity,
      generation: '0',
      staff: true,
    });
    const fixture = TestBed.createComponent(GlobalSearch);
    fixture.detectChanges();
    fixture.componentInstance.search(new Event('submit'), 'slow search');
    const request = TestBed.inject(HttpTestingController).expectOne('/api/ui/search?term=slow%20search');

    await vi.advanceTimersByTimeAsync(15000);
    fixture.detectChanges();
    expect(request.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Search unavailable. Check your access or retry.');
    expect(fixture.nativeElement.textContent).not.toContain('private diagnostic');
    expect(fixture.componentInstance.data()).toBeNull();
    fixture.destroy();
  });
  it('cancels an in-flight search and clears its results when the session is invalidated', () => {
    TestBed.configureTestingModule({
      imports: [GlobalSearch],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: identity, firmId: identity, generation: '0', staff: true });
    const fixture = TestBed.createComponent(GlobalSearch);
    fixture.detectChanges();
    fixture.componentInstance.data.set({
      term: 'ZQXPRIVATE',
      hits: [{ kind: 'Client', title: 'ZQXPRIVATE CLIENT', detail: 'Private', href: '/app' }],
      truncated: false,
    });
    fixture.componentInstance.search(new Event('submit'), 'ZQXINFLIGHT');
    const request = TestBed.inject(HttpTestingController).expectOne('/api/ui/search?term=ZQXINFLIGHT');

    session.clear();
    fixture.detectChanges();
    expect(request.cancelled).toBe(true);
    expect(fixture.componentInstance.data()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('ZQXPRIVATE CLIENT');
    fixture.destroy();
  });
  it('detaches already-open suggestions before presenting replacement results and dismissal', async () => {
    TestBed.configureTestingModule({
      imports: [GlobalSearch],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: identity, firmId: identity, generation: '0', staff: true });
    const fixture = TestBed.createComponent(GlobalSearch);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const trigger = fixture.debugElement.query(By.directive(MatAutocompleteTrigger)).injector.get(MatAutocompleteTrigger);
    const result = (term: string) => ({ term, truncated: false,
      hits: [{ kind: 'Page', title: term, detail: 'Authorized workspace page', href: '/app/practice/time' }] });
    component.search(new Event('submit'), 'first');
    http.expectOne('/api/ui/search?term=first').flush(result('first'));
    fixture.detectChanges();
    // Public Material API establishes the residual-open-panel state seen in the browser regression.
    trigger.openPanel();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(trigger.panelOpen).toBe(true);
    component.search(new Event('submit'), 'second');
    http.expectOne('/api/ui/search?term=second').flush(result('second'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(trigger.panelOpen).toBe(false);
    expect(fixture.nativeElement.querySelector('.mat-mdc-autocomplete-panel')).toBeNull();
    expect(fixture.nativeElement.querySelector('.search-panel a')?.textContent).toBe('second');
    trigger.openPanel();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(trigger.panelOpen).toBe(true);
    component.dismiss();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(trigger.panelOpen).toBe(false);
    expect(fixture.nativeElement.querySelector('.search-panel')).toBeNull();
    http.verify();
    fixture.destroy();
  });
});
