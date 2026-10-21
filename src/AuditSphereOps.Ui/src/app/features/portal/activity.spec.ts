import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { PortalActivity, portalActivityPage } from './activity';

const id = (n: number) => `40000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const at = '2026-10-21T05:00:00.123456Z';
const item = (n = 1) => ({ key: `event-${n}`, at, kind: 'PBC_MESSAGE', label: 'Request communication recorded',
  title: `Synthetic request ${n}`, actorLabel: 'Audit team', context: { clientLegalName: 'Synthetic client',
    serviceRoute: 'AUDIT', periodStart: '2026-01-01', periodEnd: '2026-12-31' }, targetKind: 'pbc-request', targetId: id(n) });
const page = (count = 1, nextCursor: string | null = null) => ({ snapshotAtUtc: at, pageSize: 25,
  items: Array.from({ length: count }, (_, index) => item(index + 1)), hasMore: nextCursor !== null, nextCursor });
const start = () => {
  TestBed.configureTestingModule({ imports: [PortalActivity],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const session = TestBed.inject(SessionService);
  session.current.set({ userId: id(100), firmId: id(101), generation: '1', staff: false });
  const fixture = TestBed.createComponent(PortalActivity);
  fixture.detectChanges(); TestBed.tick();
  return { fixture, component: fixture.componentInstance, session, http: TestBed.inject(HttpTestingController) };
};

describe('client activity', () => {
  it('rejects unsafe destinations, duplicate identities and inconsistent continuation without inventing source state', () => {
    expect(decode(portalActivityPage, page()).items[0].at).toBe(at);
    expect(() => decode(portalActivityPage, { ...page(), items: [{ ...item(), targetKind: 'external-url' }] })).toThrow();
    expect(() => decode(portalActivityPage, { ...page(), items: [item(), item()] })).toThrow();
    expect(() => decode(portalActivityPage, page(1, 'more'))).toThrow();
    expect(() => decode(portalActivityPage, { ...page(), nextCursor: 'unexpected' })).toThrow();
  });

  it('pages using the exact server cursor, clears prior rows and refuses repeated cursors or a changed cutoff', () => {
    const { fixture, component, http, session } = start();
    try {
      http.expectOne('/api/ui/portal/activity?pageSize=25').flush(page(25, 'version:cursor/+='));
      fixture.detectChanges(); TestBed.tick();
      expect(fixture.nativeElement.textContent).toContain('Synthetic request 25');
      component.next(new Event('click')); fixture.detectChanges(); TestBed.tick();
      const next = http.expectOne('/api/ui/portal/activity?pageSize=25&cursor=version%3Acursor%2F%2B%3D');
      expect(fixture.nativeElement.textContent).not.toContain('Synthetic request 25');
      next.flush(page(25, 'version:cursor/+=')); fixture.detectChanges(); TestBed.tick();
      expect(fixture.nativeElement.textContent).toContain('continuation repeated');
      session.current.set({ ...session.current()! }); fixture.detectChanges(); TestBed.tick();
      expect(component.cursors()).toEqual([null, 'version:cursor/+=']);
      http.expectOne('/api/ui/portal/activity?pageSize=25&cursor=version%3Acursor%2F%2B%3D').flush(page());
      fixture.detectChanges(); TestBed.tick();
      component.refresh(new Event('click')); fixture.detectChanges(); TestBed.tick();
      http.expectOne('/api/ui/portal/activity?pageSize=25').flush(page(25, 'next-page'));
      fixture.detectChanges(); TestBed.tick();
      component.next(new Event('click')); fixture.detectChanges(); TestBed.tick();
      http.expectOne('/api/ui/portal/activity?pageSize=25&cursor=next-page')
        .flush({ ...page(), snapshotAtUtc: '2026-10-21T06:00:00Z' }); fixture.detectChanges(); TestBed.tick();
      expect(fixture.nativeElement.textContent).toContain('continuation changed');
      expect(fixture.nativeElement.textContent).not.toContain('Synthetic request 1');
      http.verify();
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('renders semantic UTC and request links, then removes private events when current access is denied', () => {
    const { fixture, component, http, session } = start();
    try {
      http.expectOne('/api/ui/portal/activity?pageSize=25').flush(page()); fixture.detectChanges(); TestBed.tick();
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('li time')?.getAttribute('datetime')).toBe(at);
      expect(root.querySelector('li a')?.getAttribute('href')).toBe(`/portal/requests/${id(1)}`);
      expect(root.textContent).toContain('does not prove delivery');
      component.ws.reload(); fixture.detectChanges();
      http.expectOne('/api/ui/portal/activity?pageSize=25').flush({}, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges(); TestBed.tick();
      expect(root.textContent).not.toContain('Synthetic client');
      expect(root.textContent).toContain('unavailable in your current scope');
      session.clear(); fixture.detectChanges(); TestBed.tick();
      expect(component.cursors()).toEqual([null]);
      expect(root.textContent).not.toContain('Synthetic request');
      http.verify();
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
