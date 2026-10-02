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
  it('blocks a second page command after a lost response', async () => {
    const state = new CommandState(TestBed.inject(Api)), http = TestBed.inject(HttpTestingController);
    const first = state.run('/api/ui/example', { reviewed: true }, 'Accepted');
    http.expectOne('/api/ui/example').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(await first).toBe(false); expect(state.uncertain()).toBe(true);
    expect(await state.run('/api/ui/example', { reviewed: true }, 'Accepted')).toBe(false); http.expectNone('/api/ui/example');
  });
});
