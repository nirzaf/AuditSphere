import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Api, CommandState } from './api';
import { obj, text } from './decode';
import { SessionService } from './session';

describe('API session and unknown outcome fences', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });
  // The shared response matrix every page inherits: a command is confirmed, refused (safe to correct and resend) or
  // unknown (never resent until its persisted state is checked); a 401 also refreshes the session.
  it.each([
    [200, 'confirmed', false], [400, 'refused', false], [403, 'refused', false], [404, 'refused', false],
    [409, 'refused', false], [422, 'refused', false], [401, 'refused', true], [500, 'unknown', false],
    [503, 'unknown', false], [0, 'unknown', false],
  ] as const)('classifies a command answered with status %i as %s', async (status, expected, refreshes) => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController), session = TestBed.inject(SessionService);
    const refresh = vi.spyOn(session, 'refresh').mockResolvedValue(true as never);
    const request = api.command('/api/ui/example', { reviewed: true });
    const pending = http.expectOne('/api/ui/example');
    if (status === 200) pending.flush({ value: 'accepted' });
    else if (status === 0) pending.error(new ProgressEvent('error'));
    else pending.flush({ code: 'synthetic.code', message: 'Synthetic refusal.' }, { status, statusText: 'Synthetic' });
    const result = await request;
    expect(result.ok ? 'confirmed' : result.unknown ? 'unknown' : 'refused').toBe(expected);
    expect(refresh).toHaveBeenCalledTimes(refreshes ? 1 : 0);
    http.expectNone('/api/ui/example');
  });
  it.each([
    [401, 'Your session ended. Sign in again.', true], [404, 'Synthetic refusal.', false], [403, 'Synthetic refusal.', false],
    [500, 'Temporarily unavailable. Retry shortly.', false], [0, 'Temporarily unavailable. Retry shortly.', false],
  ] as const)('explains an on-demand read answered with status %i', async (status, message, refreshes) => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController), session = TestBed.inject(SessionService);
    const refresh = vi.spyOn(session, 'refresh').mockResolvedValue(true as never);
    const request = api.get('/api/ui/example', obj({ name: text }));
    const refused = expect(request).rejects.toThrow(message);
    const pending = http.expectOne('/api/ui/example');
    if (status === 0) pending.error(new ProgressEvent('error'));
    else pending.flush({ code: 'synthetic.code', message: 'Synthetic refusal.' }, { status, statusText: 'Synthetic' });
    await refused;
    expect(refresh).toHaveBeenCalledTimes(refreshes ? 1 : 0);
  });
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
  it('maps a typed duplicate library code conflict to a safe corrective message', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController);
    const request = api.command('/api/ui/library', { code: 'DUPLICATE' });
    http.expectOne('/api/ui/library').flush(
      { code: 'library.duplicate-code', message: 'server implementation detail must not be shown' },
      { status: 409, statusText: 'Conflict' });
    const result = await request;
    expect(result).toEqual({
      ok: false, unknown: false, code: 'library.duplicate-code', status: 409,
      message: 'That library code already exists. Choose another code or open the entry to prepare a new version.',
    });
  });
  it.each([
    ['idempotency.conflict', 'This request identity has already been used. Check the original request\'s receipt or persisted state before continuing.'],
    ['revision.stale', 'This record changed since you loaded it. Refresh and review the current revision.'],
    ['generation.stale', 'This record changed since you loaded it. Refresh and review the current revision.'],
    ['unrecognized.conflict', 'The request was not accepted. Check the current state before continuing.'],
  ] as const)('explains a 409 %s without changing the request or exposing server detail', async (code, message) => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController);
    const body = { idempotencyKey: '11111111-1111-4111-8111-111111111111', revision: '4', reviewed: true };
    const request = api.command('/api/ui/example', body);
    const pending = http.expectOne('/api/ui/example');
    expect(pending.request.body).toEqual(body);
    pending.flush({ code, message: '<script>private server detail</script>' }, { status: 409, statusText: 'Conflict' });
    expect(await request).toEqual({ ok: false, unknown: false, code, status: 409, message });
    http.expectNone('/api/ui/example');
  });
  it('explains a period overlap without presenting it as a stale revision or exposing server detail', async () => {
    const request = TestBed.inject(Api).command('/api/ui/clients/synthetic/engagement-creation/preview', {});
    TestBed.inject(HttpTestingController).expectOne('/api/ui/clients/synthetic/engagement-creation/preview').flush(
      { code: 'engagement.period-overlap', message: 'private server detail' }, { status: 409, statusText: 'Conflict' });
    const result = await request;
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.message).toContain('overlaps the requested period');
      expect(result.message).not.toContain('private server detail');
      expect(result.message).not.toContain('record changed');
    }
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
  it('surfaces a rate-limited command as a typed refusal and never retries it', async () => {
    const api = TestBed.inject(Api), http = TestBed.inject(HttpTestingController);
    const request = api.command('/api/ui/example', { reviewed: true });
    http.expectOne('/api/ui/example').flush(
      { code: 'request.throttled', message: 'Too many requests. Wait briefly and try again.', correlationId: 'synthetic' },
      { status: 429, statusText: 'Too Many Requests' });
    const result = await request;
    expect(result).toEqual({ ok: false, unknown: false, code: 'request.throttled', status: 429,
      message: 'Too many requests. Wait briefly and try again.' });
    http.expectNone('/api/ui/example');
  });
});
