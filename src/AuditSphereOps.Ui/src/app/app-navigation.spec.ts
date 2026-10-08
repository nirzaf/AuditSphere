import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter, Router } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { App } from './app';
import { SessionService } from './core/session';

@Component({ template: '<h1>Test workspace</h1>' })
class TestWorkspace {}

describe('responsive workspace navigation', () => {
  afterEach(() => TestBed.resetTestingModule());
  async function setup() {
    const layout = new BehaviorSubject({ matches: true, breakpoints: {} });
    let allow = true;
    TestBed.configureTestingModule({ imports: [App], providers: [
      provideHttpClient(), provideHttpClientTesting(),
      provideRouter([{path: 'app', component: TestWorkspace}, {path: 'other', component: TestWorkspace, canActivate: [() => allow]}]),
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
    return {fixture, app: fixture.componentInstance, layout, closed, ref, open, session: TestBed.inject(SessionService), router: TestBed.inject(Router), deny: () => {allow = false;}};
  }
  it('opens one labelled focus-restoring dialog and leaves cancellation to Material', async () => {
    const s = await setup();
    await s.app.openNavigation(); await s.app.openNavigation();
    expect(s.open).toHaveBeenCalledOnce();
    expect(s.open.mock.calls[0][1]).toEqual(expect.objectContaining({ariaLabel: 'Workspace navigation', autoFocus: 'first-tabbable', restoreFocus: true}));
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
});
