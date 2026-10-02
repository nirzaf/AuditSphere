import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from './session';

describe('session invalidation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });
  const first = { userId: '00000000-0000-0000-0000-000000000001', firmId: '00000000-0000-0000-0000-000000000010', generation: '1', staff: true };
  it('coalesces checks and keeps unchanged presentation state stable', async () => {
    const session = TestBed.inject(SessionService),
      http = TestBed.inject(HttpTestingController);
    const a = session.refresh(),
      b = session.refresh();
    http.expectOne('/api/ui/session').flush(first);
    expect(await a).toBe(true);
    expect(await b).toBe(true);
    const current = session.current();
    const c = session.refresh();
    http.expectOne('/api/ui/session').flush(first);
    await c;
    expect(session.current()).toBe(current);
  });
  it('invalidates prior user state and denies revoked sessions', async () => {
    const session = TestBed.inject(SessionService),
      http = TestBed.inject(HttpTestingController);
    let request = session.refresh();
    http.expectOne('/api/ui/session').flush(first);
    await request;
    request = session.refresh();
    http.expectOne('/api/ui/session').flush({ ...first, userId: '00000000-0000-0000-0000-000000000002' });
    await request;
    expect(session.invalidation()).toBe(1);
    request = session.refresh();
    http.expectOne('/api/ui/session').flush({}, { status: 401, statusText: 'Unauthorized' });
    await request;
    expect(session.current()).toBeNull();
    expect(session.invalidation()).toBe(2);
  });
  it('cannot refill state from a response started before invalidation', async () => {
    const session = TestBed.inject(SessionService),
      http = TestBed.inject(HttpTestingController);
    const request = session.refresh();
    session.clear();
    http.expectOne('/api/ui/session').flush(first);
    expect(await request).toBe(false);
    expect(session.current()).toBeNull();
  });
});

describe('sign-out outcome', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('clears immediately and blocks automatic identity restoration after an unknown outcome', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const session = TestBed.inject(SessionService),
      http = TestBed.inject(HttpTestingController);
    const logout = session.signOut();
    expect(session.current()).toBeNull();
    http.expectOne('/api/ui/sign-out').flush({}, { status: 503, statusText: 'Unavailable' });
    await expect(logout).rejects.toBeTruthy();
    expect(await session.refresh()).toBe(false);
    http.expectNone('/api/ui/session');
    http.verify();
  });
});
