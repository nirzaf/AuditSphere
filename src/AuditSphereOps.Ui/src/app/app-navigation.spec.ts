import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter, Router } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { App } from './app';
import { SessionService } from './core/session';
import { unsavedChangesGuard } from './core/unsaved-changes.guard';
import { provideWorkspaceAvailabilityForTests } from './core/workspace-availability.testing';
import type { WorkspaceAvailabilityView } from './core/workspace-availability';
import { NAVIGATION } from './navigation';

const contextId = (value: number) => `00000000-0000-4000-8000-${String(value).padStart(12, '0')}`;
const engagementContextBody = (id: string, clientId: string, clientName: string, serviceRoute: string) => ({
  id, clientId, clientName, serviceRoute, serviceProfileId: 'Annual audit',
  periodStart: '2025-01-01', periodEnd: '2025-12-31', canViewClientProfile: true,
});
const contextChoicesBody = (clientId: string, clientName: string, engagements: readonly { id: string; service: string }[]) => ({
  clientId, clientName, canOpenClient: true, pageSize: 100,
  items: engagements.map(item => ({ engagementId: item.id, serviceRoute: item.service,
    periodStart: '2025-01-01', periodEnd: '2025-12-31', status: 'ACTIVE' })),
  hasMore: false, nextCursor: null, validForMs: 30000,
});

class NavigationState { dirty = false; draft = 'Saved draft'; }

@Component({ template: '<textarea aria-label="Workpaper notes" [value]="state.draft" (input)="updateDraft($event)"></textarea>' })
class TestWorkspace {
  readonly state = inject(NavigationState);
  confirmNavigation(): boolean { return !this.state.dirty; }
  updateDraft(event: Event): void { this.state.draft = (event.target as HTMLTextAreaElement).value; this.state.dirty = true; }
}

describe('responsive workspace navigation', () => {
  afterEach(() => TestBed.resetTestingModule());
  async function setup(availability: Partial<WorkspaceAvailabilityView> = {}) {
    const layout = new BehaviorSubject({ matches: true, breakpoints: {} });
    let allow = true;
    const navigationState = new NavigationState();
    TestBed.configureTestingModule({ imports: [App], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(availability),
      { provide: NavigationState, useValue: navigationState },
      provideRouter([
        {path: 'app', component: TestWorkspace},
        {path: 'app/engagements/:id', component: TestWorkspace, canDeactivate: [unsavedChangesGuard]},
        {path: 'app/audit/workpapers/:id', component: TestWorkspace, canDeactivate: [unsavedChangesGuard]},
        {path: 'app/accounting/packages/:id', component: TestWorkspace, canDeactivate: [() => allow]},
        {path: 'app/clients/:id', component: TestWorkspace, canDeactivate: [() => allow]},
        {path: 'app/audit/populations/:id', component: TestWorkspace},
        {path: 'app/findings/:id', component: TestWorkspace},
        {path: 'app/reviews/:id', component: TestWorkspace},
        {path: 'app/records/archives/:id', component: TestWorkspace},
        {path: 'other', component: TestWorkspace, canActivate: [() => allow]},
      ]),
      {provide: BreakpointObserver, useValue: { observe: () => layout }},
    ] });
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController).expectOne('/api/ui/session').flush({
      userId: '11111111-1111-4111-8111-111111111111', firmId: '22222222-2222-4222-8222-222222222222', generation: '1', staff: true,
    });
    await fixture.whenStable();
    const closed = new Subject<string | undefined>();
    const ref = {close: vi.fn(), afterClosed: () => closed};
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue(ref as never);
    return {fixture, app: fixture.componentInstance, layout, closed, ref, open, session: TestBed.inject(SessionService), router: TestBed.inject(Router),
      state: navigationState, deny: () => {allow = false;}};
  }
  it('renders only readable destinations and omits empty groups for an accounting reviewer', async () => {
    const s = await setup({ destinations: ['reviews'], tasks: 'not_authorized', reviews: 'not_authorized' });
    await s.fixture.whenStable();
    const nav = s.fixture.nativeElement.querySelector('nav.workspace-navigation') as HTMLElement;
    expect([...nav.querySelectorAll('a')].map(link => link.textContent?.trim())).toEqual(['Package reviews']);
    expect([...nav.querySelectorAll('h2')].map(heading => heading.textContent?.trim())).toEqual(['Review, Reporting & Close']);
    expect(nav.querySelector('a[href$="/app/work"]')).toBeNull();
    s.fixture.destroy();
  });
  it('opens one labelled focus-restoring dialog and leaves cancellation to Material', async () => {
    const s = await setup();
    await s.app.openNavigation(); await s.app.openNavigation();
    expect(s.open).toHaveBeenCalledOnce();
    expect(s.open.mock.calls[0][1]).toEqual(expect.objectContaining({ariaLabel: 'Workspace navigation', ariaModal: true, autoFocus: 'first-tabbable', restoreFocus: true}));
    expect(s.app.navigationOpen()).toBe(true);
    s.closed.next(undefined); await s.fixture.whenStable();
    expect(s.app.navigationOpen()).toBe(false);
    s.fixture.destroy();
  });
  it('keeps navigation open on guard refusal and closes only after successful navigation', async () => {
    const s = await setup(); await s.router.navigateByUrl('/app'); await s.app.openNavigation();
    s.deny(); expect(await s.router.navigateByUrl('/other')).toBe(false);
    expect(s.ref.close).not.toHaveBeenCalled(); expect(s.app.navigationOpen()).toBe(true);
    await s.router.navigateByUrl('/app');
    expect(s.ref.close).toHaveBeenCalledWith('navigated'); s.fixture.destroy();
  });
  it('moves focus to the main region after a workspace route change without an open dialog', async () => {
    const s = await setup();
    const main = s.fixture.nativeElement.querySelector('#main') as HTMLElement;
    const mainFocus = vi.spyOn(main, 'focus');
    expect(await s.router.navigateByUrl('/app')).toBe(true);
    expect(mainFocus).toHaveBeenCalledOnce();
    s.fixture.destroy();
  });
  it('closes when an open menu no longer belongs to the current session', async () => {
    const s = await setup(); await s.app.openNavigation();
    s.session.clear(); await s.fixture.whenStable();
    expect(s.ref.close).toHaveBeenCalledWith('identity');
    expect(s.fixture.nativeElement.querySelector('.navigation-toggle')).toBeNull(); s.fixture.destroy();
  });
  it('fences a late chunk load after session invalidation', async () => {
    const s = await setup(); const pending = s.app.openNavigation();
    s.session.clear(); await pending; await s.fixture.whenStable();
    expect(s.open).not.toHaveBeenCalled(); expect(s.app.navigationLoading()).toBe(false); s.fixture.destroy();
  });
  it('closes on desktop resize and refuses reopening outside the compact shell', async () => {
    const s = await setup(); await s.app.openNavigation();
    s.layout.next({matches: false, breakpoints: {}}); await s.fixture.whenStable();
    expect(s.ref.close).toHaveBeenCalledWith('layout'); s.closed.next('layout');
    await s.app.openNavigation(); expect(s.open).toHaveBeenCalledOnce(); s.fixture.destroy();
  });
  it('lets staff compact the desktop sidebar without losing accessible route names', async () => {
    const s = await setup();
    s.layout.next({ matches: false, breakpoints: {} });
    await s.fixture.whenStable();
    const toggle = s.fixture.nativeElement.querySelector('.desktop-navigation-toggle') as HTMLButtonElement;
    expect(toggle).toBeTruthy();
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(toggle.getAttribute('aria-label')).toBe('Compact sidebar navigation');
    expect(toggle.getAttribute('title')).toBe('Compact navigation');
    expect(toggle.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    toggle.click();
    await s.fixture.whenStable();
    expect(s.app.navigationCompact()).toBe(true);
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(toggle.getAttribute('aria-label')).toBe('Compact sidebar navigation');
    expect(toggle.getAttribute('title')).toBe('Expand navigation');
    expect(s.fixture.nativeElement.querySelector('.workspace').classList.contains('nav-compact')).toBe(true);
    expect(s.fixture.nativeElement.querySelector('.workspace-navigation.compact-navigation')).toBeTruthy();
    expect(s.fixture.nativeElement.querySelector('a[aria-label="Portfolio"]')).toBeTruthy();
    expect(s.fixture.nativeElement.querySelector('a[title="Portfolio"]')).toBeTruthy();
    s.fixture.destroy();
  });
  it('draws one decorative icon per staff destination and group, leaving labels as the accessible names', async () => {
    const s = await setup();
    const nav = s.fixture.nativeElement.querySelector('nav.workspace-navigation') as HTMLElement;
    const links = nav.querySelectorAll('a');
    expect(links.length).toBe(NAVIGATION.reduce((count, group) => count + group.items.length, 0));
    for (const link of links) expect(link.querySelector('svg[aria-hidden="true"] path')).toBeTruthy();
    for (const heading of nav.querySelectorAll('h2')) expect(heading.querySelector('svg[aria-hidden="true"]')).toBeTruthy();
    expect(nav.querySelector('a[href$="/app/finance/books"]')?.textContent?.trim()).toBe('Firm books');
    expect(Array.from(nav.querySelectorAll('h3')).map(heading => heading.textContent?.trim())).toEqual([
      'Client Accounting', 'Group Consolidation',
    ]);
    s.fixture.destroy();
  });
  it('reports a safe load failure and allows retry', async () => {
    const s = await setup(); s.open.mockImplementationOnce(() => {throw new Error('private diagnostics');});
    await s.app.openNavigation(); await s.fixture.whenStable();
    expect(s.fixture.nativeElement.textContent).toContain('Navigation could not load. Retry');
    expect(s.fixture.nativeElement.textContent).not.toContain('private diagnostics');
    await s.app.openNavigation(); expect(s.app.navigationOpen()).toBe(true); s.fixture.destroy();
  });
  it('fences a late chunk after resize and moves focus off the now-hidden trigger', async () => {
    const s = await setup();
    const mainFocus = vi.spyOn(s.fixture.nativeElement.querySelector('#main'), 'focus');
    s.fixture.nativeElement.querySelector('.navigation-toggle').focus();
    const pending = s.app.openNavigation(); s.layout.next({matches:false,breakpoints:{}});
    await pending; expect(s.open).not.toHaveBeenCalled(); expect(mainFocus).toHaveBeenCalledOnce(); s.fixture.destroy();
  });
  it('does not open a late overlay after destruction', async () => {
    const s = await setup(); const pending = s.app.openNavigation(); s.fixture.destroy(); await pending;
    expect(s.open).not.toHaveBeenCalled();
  });

  it('shows the authorized engagement context and switches only to a returned choice', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(11), engagementA = contextId(12), engagementB = contextId(13);
    expect(await s.router.navigateByUrl(`/app/engagements/${engagementA}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${engagementA}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementA, clientId, 'Cedar Audit Client', 'STATUTORY_AUDIT'));
    http.expectOne(`/api/ui/portfolio/context-choices?clientId=${clientId}&pageSize=100`)
      .flush(contextChoicesBody(clientId, 'Cedar Audit Client', [
        { id: engagementA, service: 'STATUTORY_AUDIT' }, { id: engagementB, service: 'STATUTORY_AUDIT' },
      ]));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Cedar Audit Client');
    expect(root.textContent).toContain('Statutory Audit · 2025-01-01 to 2025-12-31');
    const select = root.querySelector('select[aria-label="Switch to an authorized engagement"]') as HTMLSelectElement;
    expect(select.value).toBe(engagementA);
    expect([...select.options].map(option => option.value)).toContain(engagementB);
    select.value = engagementB; select.dispatchEvent(new Event('change'));
    await s.fixture.whenStable();
    expect(s.router.url).toBe(`/app/engagements/${engagementB}`);
    expect(root.textContent).toContain('Loading authorized context');
    expect(root.textContent).not.toContain('Cedar Audit Client');
    s.fixture.destroy();
  });

  it('keeps the authorized current engagement visible when the bounded first choice page continues', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(14), engagementA = contextId(15), engagementB = contextId(16);
    expect(await s.router.navigateByUrl(`/app/engagements/${engagementA}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${engagementA}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementA, clientId, 'Cedar Audit Client', 'FinancialStatementAudit'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush({ ...contextChoicesBody(clientId, 'Cedar Audit Client', [{ id: engagementB, service: 'FinancialStatementAudit' }]),
        hasMore: true, nextCursor: 'pc1.next-page' });
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    const select = root.querySelector('select[aria-label="Switch to an authorized engagement"]') as HTMLSelectElement;
    expect(select.value).toBe(engagementA);
    expect([...select.options].map(option => option.value)).toEqual([engagementA, engagementB]);
    expect(root.textContent).toContain('Financial Statement Audit · 2025-01-01 to 2025-12-31');
    s.fixture.destroy();
  });

  it('resolves an ID-only workpaper parent and keeps the old context when a dirty-route switch is cancelled', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(21), workpaperId = contextId(22), engagementA = contextId(23), engagementB = contextId(24);
    expect(await s.router.navigateByUrl(`/app/audit/workpapers/${workpaperId}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/audit/workpapers/${workpaperId}`).flush({ id: workpaperId, engagementId: engagementA });
    http.expectOne(`/api/ui/engagements/${engagementA}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementA, clientId, 'Cedar Audit Client', 'STATUTORY_AUDIT'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush(contextChoicesBody(clientId, 'Cedar Audit Client', [
        { id: engagementA, service: 'STATUTORY_AUDIT' }, { id: engagementB, service: 'STATUTORY_AUDIT' },
      ]));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    const select = root.querySelector('select[aria-label="Switch to an authorized engagement"]') as HTMLSelectElement;
    expect(select.value).toBe(engagementA);
    const draft = root.querySelector('textarea[aria-label="Workpaper notes"]') as HTMLTextAreaElement;
    draft.value = 'Unsaved workpaper notes'; draft.dispatchEvent(new Event('input'));
    select.value = engagementB; select.dispatchEvent(new Event('change'));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(s.router.url).toBe(`/app/audit/workpapers/${workpaperId}`);
    expect(root.textContent).toContain('Cedar Audit Client');
    expect(select.value).toBe(engagementA);
    expect(draft.value).toBe('Unsaved workpaper notes');
    expect(s.state.dirty).toBe(true);
    s.fixture.destroy();
  });

  it('resolves package context from its authorized parent and cancels stale context reads after A to B to A navigation', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(31), packageId = contextId(32), engagementA = contextId(33), engagementB = contextId(34);
    expect(await s.router.navigateByUrl(`/app/accounting/packages/${packageId}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/packages/${packageId}`).flush({ id: packageId, engagementId: engagementA });
    http.expectOne(`/api/ui/engagements/${engagementA}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementA, clientId, 'Cedar Audit Client', 'STATUTORY_AUDIT'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush(contextChoicesBody(clientId, 'Cedar Audit Client', [
        { id: engagementA, service: 'STATUTORY_AUDIT' }, { id: engagementB, service: 'STATUTORY_AUDIT' },
      ]));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Cedar Audit Client');
    const select = root.querySelector('select[aria-label="Switch to an authorized engagement"]') as HTMLSelectElement;
    select.value = engagementB; select.dispatchEvent(new Event('change'));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(s.router.url).toBe(`/app/engagements/${engagementB}`);
    const staleB = http.expectOne(`/api/ui/engagements/${engagementB}?holdPage=0&holdPageSize=10`);
    expect(root.textContent).not.toContain('Cedar Audit Client');

    expect(await s.router.navigateByUrl(`/app/engagements/${engagementA}`)).toBe(true);
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(staleB.cancelled).toBe(true);
    http.expectOne(`/api/ui/engagements/${engagementA}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementA, clientId, 'Cedar Audit Client', 'STATUTORY_AUDIT'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush(contextChoicesBody(clientId, 'Cedar Audit Client', [
        { id: engagementA, service: 'STATUTORY_AUDIT' }, { id: engagementB, service: 'STATUTORY_AUDIT' },
      ]));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(root.textContent).toContain('Cedar Audit Client');
    expect((root.querySelector('select[aria-label="Switch to an authorized engagement"]') as HTMLSelectElement).value)
      .toBe(engagementA);
    s.fixture.destroy();
  });

  it('resolves an audit finding parent and returns to the neutral firm workspace without stale client context', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(41), findingId = contextId(42), engagementId = contextId(43);
    expect(await s.router.navigateByUrl(`/app/findings/${findingId}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/findings/${findingId}`).flush({ id: findingId, engagementId });
    http.expectOne(`/api/ui/engagements/${engagementId}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementId, clientId, 'Cedar Audit Client', 'FinancialStatementAudit'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush(contextChoicesBody(clientId, 'Cedar Audit Client', [{ id: engagementId, service: 'FinancialStatementAudit' }]));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Cedar Audit Client');
    expect(root.textContent).toContain('Financial Statement Audit · 2025-01-01 to 2025-12-31');
    expect(await s.router.navigateByUrl('/app')).toBe(true);
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(root.querySelector('.workspace-label')?.textContent?.trim()).toBe('Firm workspace');
    expect(root.querySelector('[aria-label="Current workspace context"]')).toBeNull();
    expect(root.textContent).not.toContain('Cedar Audit Client');
    s.fixture.destroy();
  });

  it('clears the current context and choices if authorization changes during a continuation read', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(51), engagementId = contextId(52), nextEngagement = contextId(53);
    expect(await s.router.navigateByUrl(`/app/engagements/${engagementId}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${engagementId}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementId, clientId, 'Cedar Audit Client', 'AUDIT'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush({ ...contextChoicesBody(clientId, 'Cedar Audit Client', [{ id: engagementId, service: 'AUDIT' }]),
        hasMore: true, nextCursor: 'pc1.first-page' });
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Cedar Audit Client');
    [...root.querySelectorAll('button')].find(button => button.textContent?.includes('Load more engagements'))!.click();
    const continuation = http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?') &&
      new URLSearchParams(request.url.split('?')[1]).has('cursor'));
    continuation.flush({}, { status: 403, statusText: 'Forbidden' });
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(root.textContent).not.toContain('Cedar Audit Client');
    expect(root.textContent).toContain('Current context unavailable');
    expect(root.querySelector('select[aria-label="Switch to an authorized engagement"]')).toBeNull();
    expect(s.router.url).toBe(`/app/engagements/${engagementId}`);
    expect(s.router.url).not.toContain(nextEngagement);
    s.fixture.destroy();
  });

  it('refuses a choice after its server-provided context lease expires', async () => {
    let clock = 1000;
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(61), engagementA = contextId(62), engagementB = contextId(63);
    expect(await s.router.navigateByUrl(`/app/engagements/${engagementA}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${engagementA}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementA, clientId, 'Cedar Audit Client', 'AUDIT'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush({ ...contextChoicesBody(clientId, 'Cedar Audit Client', [
        { id: engagementA, service: 'AUDIT' }, { id: engagementB, service: 'AUDIT' },
      ]), validForMs: 100 });
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    const select = root.querySelector('select[aria-label="Switch to an authorized engagement"]') as HTMLSelectElement;
    clock += 101;
    select.value = engagementB; select.dispatchEvent(new Event('change'));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    expect(s.router.url).toBe(`/app/engagements/${engagementA}`);
    expect(root.textContent).not.toContain('Cedar Audit Client');
    expect(root.textContent).toContain('Current context unavailable');
    s.fixture.destroy();
  });

  it('clears protected context synchronously on staff-session invalidation', async () => {
    const s = await setup(), http = TestBed.inject(HttpTestingController);
    const clientId = contextId(71), engagementId = contextId(72);
    expect(await s.router.navigateByUrl(`/app/engagements/${engagementId}`)).toBe(true);
    s.fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${engagementId}?holdPage=0&holdPageSize=10`)
      .flush(engagementContextBody(engagementId, clientId, 'Cedar Audit Client', 'AUDIT'));
    http.expectOne(request => request.url.startsWith('/api/ui/portfolio/context-choices?'))
      .flush(contextChoicesBody(clientId, 'Cedar Audit Client', [{ id: engagementId, service: 'AUDIT' }]));
    await s.fixture.whenStable(); s.fixture.detectChanges();
    const root = s.fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Cedar Audit Client');
    s.session.clear();
    s.fixture.detectChanges();
    expect(root.textContent).not.toContain('Cedar Audit Client');
    expect(root.querySelector('select[aria-label="Switch to an authorized engagement"]')).toBeNull();
    s.fixture.destroy();
  });
});
