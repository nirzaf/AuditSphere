import { Component, signal } from '@angular/core';
import { APP_BASE_HREF } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { WorkspaceAvailability } from '../../core/workspace-availability';
import { PortfolioProgress } from '../practice/engagement-progress';
import { Portfolio, decodePortfolio } from './portfolio';
import { decodeContextPage } from './portfolio-contracts';
describe('portfolio contract', () => {
  it('decodes bounded exact-count rows', () => {
    expect(
      decodePortfolio({
        items: [{ id: '00000000-0000-0000-0000-000000000001', name: 'Client A', engagements: 1, isArchived: true }],
        total: 1,
        page: 0,
        pageSize: 25,
      }).total,
    ).toBe(1);
  });
  it('refuses unsupported counts and identities', () => {
    expect(() => decodePortfolio({items: [], total: 0, page: 0, pageSize: 0})).toThrow();
    expect(() => decodePortfolio({ items: [], total: '1', page: 0, pageSize: 25 })).toThrow();
    expect(() =>
      decodePortfolio({
        items: [{ id: 'email@example.test', name: 'Client', engagements: 1, isArchived: false }],
        total: 1,
        page: 0,
        pageSize: 25,
      }),
    ).toThrow();
  });
});

const client = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const engagement = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const other = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const choice = (id = engagement) => ({ engagementId: id, serviceRoute: 'Audit', periodStart: '2026-01-01', periodEnd: '2026-12-31', status: 'Active' });
const contextPage = (extra: object = {}) => ({ clientId: client, clientName: 'Client A', canOpenClient: false,
  pageSize: 25, items: [choice()], hasMore: false, nextCursor: null, validForMs: 30000, ...extra });
const workspace = () => ({ search: '', clients: { items: [{ id: client, name: 'Client A', engagements: 1, isArchived: false }], total: 1, page: 0, pageSize: 25 },
  metrics: { clients: 1, engagements: 1, openHolds: 0, pendingOperations: 0, readyCandidates: 0, issuedReleases: 0 },
  hasActiveMicrosoftConfiguration: true, recentLimit: 25, candidateTotal: 0, packageTotal: 0, candidates: [], packages: [] });

@Component({ selector: 'audit-portfolio-progress', template: '' })
class StubPortfolioProgress {}

describe('portfolio destination choices', () => {
  afterEach(() => { TestBed.resetTestingModule(); vi.useRealTimers(); vi.restoreAllMocks(); });
  function setup(base = '/') {
    const available = signal(true), revision = signal(1);
    const availability = { current: () => available() ? {} : null, revision,
      invalidate: () => { available.set(false); revision.update(value => value + 1); }, refresh: () => {} };
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: APP_BASE_HREF, useValue: base },
      { provide: WorkspaceAvailability, useValue: availability }] });
    TestBed.overrideComponent(Portfolio, { remove: { imports: [PortfolioProgress] }, add: { imports: [StubPortfolioProgress] } });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: other, firmId: other, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(Portfolio);
    fixture.detectChanges();
    http.expectOne(request => request.url.split('?')[0] === '/api/ui/portfolio/workspace').flush(workspace());
    fixture.detectChanges();
    return { http, fixture, session, available, revision };
  }
  const destinationRequest = (http: HttpTestingController) => http.expectOne(request => request.url.split('?')[0] === '/api/ui/portfolio/context-choices');
  const findLink = (element: HTMLElement, text: string) => [...element.querySelectorAll('a')].find(link => link.textContent?.includes(text));
  const finish = (fixture: ComponentFixture<Portfolio>, http: HttpTestingController) => {
    fixture.destroy(); http.verify({ ignoreCancelled: true });
  };

  it('validates identity, page bounds, duplicate entries, continuation and lifetime', () => {
    expect(decodeContextPage(contextPage(), client, 25).items).toHaveLength(1);
    for (const value of [contextPage({ clientId: other }), contextPage({ pageSize: 101 }),
      contextPage({ items: [choice(), choice()] }), contextPage({ hasMore: true, nextCursor: 'foreign' }),
      contextPage({ hasMore: false, nextCursor: 'pc1.abc' }), contextPage({ validForMs: 0 }),
      contextPage({ validForMs: 30001 })]) expect(() => decodeContextPage(value, client, 25)).toThrow();
  });

  it('shows a direct engagement link without a client link, then independently shows a client link for an empty engagement page', () => {
    const { http, fixture } = setup(), component = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;
    expect(findLink(element, 'Open client details')).toBeUndefined();
    const button = element.querySelector<HTMLButtonElement>(`button[aria-controls="portfolio-choices-${client}"]`)!;
    expect(button.getAttribute('aria-expanded')).toBe('false');
    button.click(); fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(element.textContent).toContain('Loading destinations');
    destinationRequest(http).flush(contextPage()); fixture.detectChanges();
    expect(findLink(element, 'Open Audit')?.getAttribute('href')).toBe(`/app/engagements/${engagement}`);
    expect(findLink(element, 'Open client details')).toBeUndefined();
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ canOpenClient: true, items: [] })); fixture.detectChanges();
    expect(findLink(element, 'Open client details')?.getAttribute('href')).toBe(`/app/clients/${client}`);
    expect(element.textContent).toContain('No engagement destinations available');
    finish(fixture, http);
  });

  it('does not claim a single destination when there is continuation and rejects a duplicate on the next page', () => {
    const { http, fixture } = setup(), component = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ hasMore: true, nextCursor: 'pc1.abc' })); fixture.detectChanges();
    expect(element.textContent).toContain('More destinations are available');
    expect(element.querySelector('ul a')?.getAttribute('href')).toBe(`/app/engagements/${engagement}`);
    component.moreChoices(client);
    const request = destinationRequest(http);
    expect(new URLSearchParams(request.request.url.split('?')[1]).get('cursor')).toBe('pc1.abc');
    request.flush(contextPage()); fixture.detectChanges();
    expect(element.querySelector('ul a')).toBeNull();
    expect(element.textContent).toContain('Destinations are unavailable or changed');
    finish(fixture, http);
  });

  it('hides results synchronously on expiry and rejects an in-flight response after the availability revision changes', () => {
    let clock = 1000;
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    const { http, fixture, revision } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ validForMs: 100 })); fixture.detectChanges();
    expect(component.choiceFor(client)?.page).not.toBeNull();
    clock += 101;
    expect(component.choiceFor(client)).toBeNull();
    component.openChoices(client);
    const pending = destinationRequest(http);
    revision.update(value => value + 1);
    pending.flush(contextPage());
    expect(component.choiceFor(client)).toBeNull();
    fixture.detectChanges();
    expect(findLink(fixture.nativeElement as HTMLElement, 'Open Audit')).toBeUndefined();
    finish(fixture, http);
  });

  it('subtracts the whole request duration and rejects a response which has already expired', () => {
    let clock = 1000;
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    const { http, fixture } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    const pending = destinationRequest(http);
    clock += 100;
    pending.flush(contextPage({ validForMs: 100 })); fixture.detectChanges();
    expect(component.choiceFor(client)?.page).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Destinations are unavailable or changed');
    finish(fixture, http);
  });

  it('stops loading and permits a fresh read if continuation succeeds after the old deadline before its timer runs', () => {
    let clock = 1000;
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    const { http, fixture } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ hasMore: true, nextCursor: 'pc1.abc', validForMs: 100 }));
    component.moreChoices(client);
    const pending = destinationRequest(http);
    clock += 101;
    pending.flush(contextPage({ items: [choice(other)] })); fixture.detectChanges();
    expect(component.choiceFor(client)?.loading).toBe(false);
    expect(component.choiceFor(client)?.page).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Destinations are unavailable or changed');
    expect((fixture.nativeElement as HTMLElement).querySelector('.portfolio-clients ul a')).toBeNull();
    component.openChoices(client);
    const retry = destinationRequest(http);
    expect(new URLSearchParams(retry.request.url.split('?')[1]).has('cursor')).toBe(false);
    retry.flush(contextPage());
    expect(component.choiceFor(client)?.page?.items[0].engagementId).toBe(engagement);
    finish(fixture, http);
  });

  it('hides a previously authorized choice immediately when the current session changes', () => {
    const { http, fixture, session } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush(contextPage()); fixture.detectChanges();
    expect(component.choiceFor(client)?.page).not.toBeNull();
    session.current.set({ userId: engagement, firmId: other, generation: '1', staff: true });
    expect(component.workspaceData()).toBeNull();
    expect(component.choiceFor(client)).toBeNull();
    fixture.detectChanges();
    expect(findLink(fixture.nativeElement as HTMLElement, 'Open Audit')).toBeUndefined();
    finish(fixture, http);
  });

  it('clears a denied page and does not retain the old session after 401', () => {
    const { http, fixture, session } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.current()).toBeNull();
    expect(component.choiceFor(client)).toBeNull();
    finish(fixture, http);
  });

  it('discards previously visible choices when the server rejects a continuation', () => {
    const { http, fixture } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ hasMore: true, nextCursor: 'pc1.abc' })); fixture.detectChanges();
    component.moreChoices(client);
    destinationRequest(http).flush({}, { status: 400, statusText: 'Bad Request' }); fixture.detectChanges();
    expect(component.choiceFor(client)?.page).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('ul a')).toBeNull();
    finish(fixture, http);
  });

  it('invalidates availability on a forbidden choice read', () => {
    const { http, fixture, available } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(available()).toBe(false);
    expect(component.workspaceData()).toBeNull();
    expect(component.choiceFor(client)).toBeNull();
    finish(fixture, http);
  });

  it('keeps normal navigation in the SPA, preserves modified clicks and prevents stale clicks before rerender', () => {
    let clock = 1000;
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    const { http, fixture } = setup(), component = fixture.componentInstance;
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ validForMs: 100 })); fixture.detectChanges();
    const link = findLink(fixture.nativeElement as HTMLElement, 'Open Audit')!;
    expect(link.getAttribute('href')).toBe(`/app/engagements/${engagement}`);
    const prevented: boolean[] = [];
    // Observe the component handler first, then cancel the browser navigation in the test.
    link.addEventListener('click', event => { prevented.push(event.defaultPrevented); event.preventDefault(); });
    link.addEventListener('auxclick', event => { prevented.push(event.defaultPrevented); event.preventDefault(); });
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, metaKey: true }));
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, shiftKey: true }));
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, altKey: true }));
    link.dispatchEvent(new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }));
    expect(prevented).toEqual([true, false, false, false, false, false]);
    expect(navigate).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(`/app/engagements/${engagement}`);
    expect(component.navigation.read()?.selected).toBe(client);
    clock += 101;
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
    link.dispatchEvent(new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }));
    expect(prevented.slice(6)).toEqual([true, true, true]); // No change detection or expiry timer ran.
    expect(navigate).toHaveBeenCalledOnce();
    finish(fixture, http);
  });

  it('uses the same guarded native semantics for a client destination and rejects invalid href input', () => {
    const { http, fixture, revision } = setup(), component = fixture.componentInstance;
    expect(component.choiceHref('client', '//outside.test')).toBeNull();
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ canOpenClient: true, items: [] })); fixture.detectChanges();
    const link = findLink(fixture.nativeElement as HTMLElement, 'Open client details')!;
    expect(link.getAttribute('href')).toBe(`/app/clients/${client}`);
    const prevented: boolean[] = [];
    link.addEventListener('click', event => { prevented.push(event.defaultPrevented); event.preventDefault(); });
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, shiftKey: true }));
    revision.update(value => value + 1);
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    expect(prevented).toEqual([false, true]);
    finish(fixture, http);
  });

  it.each([{ base: '/', prefix: '' }, { base: '/ui/', prefix: '/ui' }])
    ('prepares $base links for native modified clicks while SPA navigation uses an internal route', ({ base, prefix }) => {
      const { http, fixture } = setup(base), component = fixture.componentInstance;
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      component.openChoices(client);
      destinationRequest(http).flush(contextPage({ canOpenClient: true })); fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;
      const link = findLink(element, 'Open Audit')!;
      expect(link.getAttribute('href')).toBe(`${prefix}/app/engagements/${engagement}`);
      expect(findLink(element, 'Open client details')?.getAttribute('href')).toBe(`${prefix}/app/clients/${client}`);
      const prevented: boolean[] = [];
      link.addEventListener('click', event => { prevented.push(event.defaultPrevented); event.preventDefault(); });
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
      expect(prevented).toEqual([false]);
      expect(navigate).not.toHaveBeenCalled();
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      expect(prevented).toEqual([false, true]);
      expect(navigate).toHaveBeenCalledWith(`/app/engagements/${engagement}`);
      finish(fixture, http);
    });

  it.each([401, 403])('processes a same-session %i after the prior choice page expires', status => {
    let clock = 1000;
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    const { http, fixture, session, available } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    destinationRequest(http).flush(contextPage({ hasMore: true, nextCursor: 'pc1.abc', validForMs: 100 }));
    component.moreChoices(client);
    const pending = destinationRequest(http);
    clock += 101;
    vi.advanceTimersByTime(101);
    window.dispatchEvent(new Event('focus'));
    expect(pending.cancelled).toBe(false);
    expect(component.choiceFor(client)?.page).toBeNull();
    pending.flush({}, { status, statusText: 'Denied' });
    if (status === 401) expect(session.current()).toBeNull();
    else expect(available()).toBe(false);
    expect(component.choiceFor(client)).toBeNull();
    finish(fixture, http);
  });

  it('does not apply an old request denial to a replacement session', () => {
    const { http, fixture, session, available } = setup(), component = fixture.componentInstance;
    component.openChoices(client);
    const pending = destinationRequest(http);
    session.current.set({ userId: engagement, firmId: other, generation: '2', staff: true });
    pending.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.current()?.userId).toBe(engagement);
    expect(available()).toBe(true);
    finish(fixture, http);
  });
});
