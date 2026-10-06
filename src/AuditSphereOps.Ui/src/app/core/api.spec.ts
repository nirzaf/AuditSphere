import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Api, CommandState } from './api';
import { obj, text } from './decode';
import { SessionService } from './session';

describe('API session and unknown outcome fences', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });
  it('cannot return an on-demand read after session invalidation', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController), session = TestBed.inject(SessionService);
    const request = api.get('/api/ui/example', obj({ name: text }));
    const refused = expect(request).rejects.toThrow('Your session changed');
    session.clear(); http.expectOne('/api/ui/example').flush({ name: 'Protected old identity' }); await refused;
  });
  it('does not report a late command success to a changed identity', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController), session = TestBed.inject(SessionService);
    const request = api.command('/api/ui/example', { reviewed: true });
    session.clear(); http.expectOne('/api/ui/example').flush({ value: 'accepted' });
    const result = await request; expect(result.ok).toBe(false); if(!result.ok) expect(result.unknown).toBe(true);
  });
  it('does not save a file whose source metadata failed the caller context fence', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController);
    const request = api.download('/api/ui/source/export', {revision:1}, () => false);
    http.expectOne('/api/ui/source/export').flush(new Blob(['synthetic csv']), {headers:{'Content-Type':'text/csv','Content-Disposition':'attachment; filename=source.csv'}});
    const result = await request; expect(result.ok).toBe(false); if (!result.ok) expect(result.code).toBe('download.context');
  });
  it('preserves the safe typed refusal for a bounded portfolio export', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController);
    const request = api.download('/api/ui/portfolio/export', { search: '' });
    http.expectOne('/api/ui/portfolio/export').flush(new Blob([JSON.stringify({
      code: 'export.limit', message: 'Narrow the search before exporting. The client export limit was exceeded.',
    })], { type: 'application/json' }), { status: 400, statusText: 'Bad Request' });
    const result = await request;
    expect(result).toEqual({ ok: false, unknown: false, code: 'download.refused', status: 400,
      message: 'Narrow the search before exporting. The client export limit was exceeded.' });
  });
  it('does not save a late download after revocation', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController);
    const request = api.download('/api/ui/source/export'); TestBed.inject(SessionService).clear();
    http.expectOne('/api/ui/source/export').flush(new Blob(['synthetic csv']));
    const result = await request; expect(result.ok).toBe(false); if (!result.ok) expect(result.message).toContain('No file was saved');
  });
  it('refreshes and clears the current session when an export reports unauthorized', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController), session = TestBed.inject(SessionService);
    session.current.set({ userId: '11111111-1111-4111-8111-111111111111', firmId: '22222222-2222-4222-8222-222222222222', generation: '1', staff: true });
    const request = api.download('/api/ui/portfolio/export');
    http.expectOne('/api/ui/portfolio/export').flush(new Blob(['refused']), { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    http.expectOne('/api/ui/session').flush({}, { status: 401, statusText: 'Unauthorized' });
    const result = await request;
    expect(result.ok).toBe(false);
    expect(session.current()).toBeNull();
  });
  it('blocks a second page command after a lost response', async () => {
    const state = new CommandState(TestBed.inject(Api)), http = TestBed.inject(HttpTestingController);
    const first = state.run('/api/ui/example', { reviewed: true }, 'Accepted');
    http.expectOne('/api/ui/example').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(await first).toBe(false); expect(state.uncertain()).toBe(true);
    expect(await state.run('/api/ui/example', { reviewed: true }, 'Accepted')).toBe(false); http.expectNone('/api/ui/example');
  });
});
