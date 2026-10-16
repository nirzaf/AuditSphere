import { decode } from '../../core/decode';
import { decodeDirectoryBinding, decodeDirectoryResults } from './directory';

describe('Microsoft directory response boundaries', () => {
  const tenant = '00000000-0000-0000-0000-000000000001';
  const objectId = '00000000-0000-0000-0000-000000000002';
  const query = { prefix: 'Member', domain: '', browse: false };
  const user = { tenantId: tenant, objectId, displayName: 'Member', userPrincipalName: 'member@example.test', accountEnabled: true, userType: 'Member' };
  const page = (nextPageToken: string | null) => ({ users: [user], nextPageToken });

  it('accepts opaque protected HTTP cursors beyond the provider raw-token bound, up to 20000 characters', () => {
    for (const length of [2049, 20000]) {
      const token = 'opaque-protected-envelope-'.padEnd(length, 'x');
      expect(decodeDirectoryResults(page(token), tenant, query, null).nextPageToken).toBe(token);
    }
  });

  it('refuses an HTTP envelope longer than 20000 characters', () => {
    expect(() => decodeDirectoryResults(page('x'.repeat(20001)), tenant, query, null)).toThrow();
  });

  it('refuses an exact repeated cursor without interpreting its opaque content', () => {
    const token = 'protected-envelope'.padEnd(4096, 'x');
    expect(() => decodeDirectoryResults(page(token), tenant, query, token)).toThrow();
    expect(decodeDirectoryResults(page(token + 'y'), tenant, query, token).nextPageToken).toBe(token + 'y');
  });

  it('refuses blank and control-containing continuations', () => {
    for (const token of ['', '   ', 'opaque\ncontinuation', 'opaque\u007fcontinuation']) {
      expect(() => decodeDirectoryResults(page(token), tenant, query, null)).toThrow();
    }
  });

  it('rejects foreign tenants, duplicate objects and identities outside the applied browse/domain', () => {
    expect(() => decodeDirectoryResults({ users: [{ ...user, tenantId: objectId }], nextPageToken: null }, tenant, query, null)).toThrow();
    expect(() => decodeDirectoryResults({ users: [user, { ...user }], nextPageToken: null }, tenant, query, null)).toThrow();
    expect(() => decodeDirectoryResults({ users: [{ ...user, accountEnabled: false }], nextPageToken: null }, tenant, { ...query, browse: true }, null)).toThrow();
    expect(() => decodeDirectoryResults(page(null), tenant, { ...query, domain: 'other.test' }, null)).toThrow();
    expect(decodeDirectoryResults({ users: [], nextPageToken: null }, tenant, query, null).users).toEqual([]);
  });

  it('requires consistent local binding identity fields without treating absence as a receipt', () => {
    const absent = { tenantId: tenant, objectId, bound: false, userId: null, userKind: null, localDisabled: null,
      verificationStartedAt: null, directoryEnabledState: null, directoryUserType: null };
    expect(decode(decodeDirectoryBinding, absent).bound).toBe(false);
    expect(() => decode(decodeDirectoryBinding, { ...absent, bound: true })).toThrow();
    expect(() => decode(decodeDirectoryBinding, { ...absent, userId: tenant })).toThrow();
    expect(() => decode(decodeDirectoryBinding, { ...absent, localDisabled: false })).toThrow();
    expect(decode(decodeDirectoryBinding, { ...absent, bound: true, userId: tenant, userKind: 'Staff', localDisabled: false }).userId).toBe(tenant);
  });
});

import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, NavigationStart } from '@angular/router';
import { Subject } from 'rxjs';
import { vi, beforeEach, afterEach } from 'vitest';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { MicrosoftDirectory } from './directory';

describe('Microsoft directory lifecycle and recovery', () => {
  const tenant = '00000000-0000-0000-0000-000000000001', objectId = '00000000-0000-0000-0000-000000000002';
  const user = { tenantId: tenant, objectId, displayName: 'Member', userPrincipalName: 'member@example.test', accountEnabled: true, userType: 'Member' };
  const initial = { directoryConfigured: true, configuredTenantId: tenant };
  let settings: WritableSignal<typeof initial | null>, command: ReturnType<typeof vi.fn>, get: ReturnType<typeof vi.fn>;
  let session: { current: WritableSignal<{ userId: string; firmId: string; generation: string; staff: boolean } | null>; invalidation: WritableSignal<number> };
  let events: Subject<NavigationStart>, marker: unknown, dialog: { open: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    settings = signal<typeof initial | null>(initial); command = vi.fn(); get = vi.fn(); marker = null;
    events = new Subject(); dialog = { open: vi.fn() };
    session = { current: signal<{ userId: string; firmId: string; generation: string; staff: boolean } | null>({ userId: objectId, firmId: tenant, generation: '1', staff: true }), invalidation: signal(0) };
    TestBed.configureTestingModule({ providers: [
      { provide: Api, useValue: { resource: () => ({ data: settings, loading: signal(false), error: signal(null), reload: vi.fn() }), command, get } },
      { provide: SessionService, useValue: session }, { provide: Router, useValue: { events } }, { provide: MatDialog, useValue: dialog },
      { provide: TabDrafts, useValue: { save: (_scope: unknown, value: unknown) => { marker = value; return true; },
        read: () => marker ? { state: 'ready', draft: { value: marker, submissionPending: true } } : { state: 'absent' }, clear: () => { marker = null; return true; } } }
    ] });
    TestBed.overrideComponent(MicrosoftDirectory, { set: { template: '', imports: [] } });
  });
  afterEach(() => TestBed.resetTestingModule());
  function open() { const fixture = TestBed.createComponent(MicrosoftDirectory); fixture.detectChanges(); return fixture; }
  function select(c: MicrosoftDirectory) { c.page.set({ users: [user], nextPageToken: null }); c.select(user); c.reviewed.setValue(true); }
  function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
  const status = (bound = true) => ({ tenantId: tenant, objectId, bound, userId: bound ? tenant : null, userKind: bound ? 'Staff' : null,
    localDisabled: bound ? false : null, verificationStartedAt: null, directoryEnabledState: bound ? 'ENABLED' : null, directoryUserType: bound ? 'Member' : null });

  it('retries a failed next page with the identical opaque cursor and clears everything on denial', async () => {
    const c = open().componentInstance, token = 'protected'.padEnd(3000, 'x');
    command.mockResolvedValueOnce({ ok: true, value: { users: [user], nextPageToken: token } });
    await c.search(true); c.select(c.page()!.users[0]); c.reviewed.setValue(true);
    command.mockResolvedValueOnce({ ok: false, unknown: true, status: 503, message: 'Unavailable' }); await c.next();
    expect(c.page()!.users).toHaveLength(1); expect(c.selected()).toBeNull(); expect(c.reviewed.value).toBe(false);
    const attempt = command.mock.calls[1][1];
    command.mockResolvedValueOnce({ ok: true, value: { users: [], nextPageToken: null } }); await c.retryPage();
    expect(command.mock.calls[2][1]).toEqual(attempt); expect(c.page()!.users).toEqual([]); await c.next(); expect(command).toHaveBeenCalledTimes(3);
    command.mockResolvedValueOnce({ ok: false, unknown: false, status: 403, message: 'Denied' }); await c.search(true);
    expect(c.page()).toBeNull(); expect(c.retryAttempt()).toBeNull(); expect(c.selected()).toBeNull();
  });

  for (const error of [false, true]) it(`discards late ${error ? 'error' : 'success'} after input A-B-A without clearing a newer busy operation`, async () => {
    const c = open().componentInstance, old = deferred<unknown>(), fresh = deferred<unknown>();
    command.mockReturnValueOnce(old.promise).mockReturnValueOnce(fresh.promise);
    const first = c.search(true); c.form.controls.prefix.setValue('B'); c.form.controls.prefix.setValue('');
    const second = c.search(true);
    if (error) old.reject(new Error('late')); else old.resolve({ ok: true, value: { users: [user], nextPageToken: null } });
    await first; expect(c.page()).toBeNull(); expect(c.message()).toBe(''); expect(c.busy()).toBe(true);
    fresh.resolve({ ok: true, value: { users: [], nextPageToken: null } }); await second; expect(c.busy()).toBe(false);
  });

  for (const fence of ['configuration', 'session', 'navigation', 'destroy']) it(`fences a dispatched binding across ${fence} changes and retains its pending identity`, async () => {
    const f = open(), c = f.componentInstance, pending = deferred<unknown>(); select(c); command.mockReturnValueOnce(pending.promise);
    const saving = c.bind(); expect(command).toHaveBeenCalledTimes(1);
    if (fence === 'configuration') { settings.set({ ...initial, configuredTenantId: objectId }); f.detectChanges(); settings.set(initial); f.detectChanges(); }
    if (fence === 'session') { session.invalidation.update(x => x + 1); f.detectChanges(); }
    if (fence === 'navigation') events.next(new NavigationStart(1, '/elsewhere'));
    if (fence === 'destroy') f.destroy();
    pending.resolve({ ok: true, value: tenant }); await saving;
    expect(c.boundId()).toBeNull(); expect(get).not.toHaveBeenCalled(); expect(marker).toEqual({ tenantId: tenant, objectId, guest: false });
  });

  it('accepts an exact acknowledged binding, refreshes the roster, and refuses a known denied binding without uncertainty', async () => {
    const c = open().componentInstance, changed = vi.fn(); c.changed.subscribe(changed); select(c);
    command.mockResolvedValueOnce({ ok: true, value: tenant }); get.mockResolvedValueOnce(status()); await c.bind();
    expect(c.boundId()).toBe(tenant); expect(c.uncertain()).toBe(false); expect(changed).toHaveBeenCalledTimes(1); expect(marker).toBeNull();
    select(c); command.mockResolvedValueOnce({ ok: false, unknown: false, status: 403, message: 'Denied' }); await c.bind();
    expect(c.uncertain()).toBe(false); expect(c.page()).toBeNull(); expect(c.reviewed.value).toBe(false);
  });

  it('retains only identity after a lost response and reload; present or absent inspection never dispatches a retry', async () => {
    const f = open(), c = f.componentInstance; select(c);
    command.mockResolvedValueOnce({ ok: false, unknown: true, status: 0, message: 'Lost response' }); await c.bind(); await c.bind();
    expect(command).toHaveBeenCalledTimes(1); expect(Object.keys(marker as object).sort()).toEqual(['guest', 'objectId', 'tenantId']); f.destroy();
    const restored = open().componentInstance; expect(restored.uncertain()).toBe(true); expect(restored.reviewed.value).toBe(false);
    for (const bound of [true, false]) { get.mockResolvedValueOnce(status(bound)); await restored.inspectBinding();
      expect(restored.inspection()?.bound).toBe(bound); expect(restored.uncertain()).toBe(true); await restored.bind(); }
    expect(command).toHaveBeenCalledTimes(1); expect(get.mock.calls.every(call => call[0] === `/api/ui/administration/directory/binding?tenantId=${tenant}&objectId=${objectId}`)).toBe(true);
  });

  it('refuses a mismatched binding acknowledgement and any obsolete or disabled selection', async () => {
    const c = open().componentInstance; select(c); command.mockResolvedValueOnce({ ok: true, value: tenant });
    get.mockResolvedValueOnce({ ...status(), objectId: tenant }); await c.bind(); expect(c.uncertain()).toBe(true); expect(c.boundId()).toBeNull();
    await c.bind(); expect(command).toHaveBeenCalledTimes(1);
    c.selected.set(null); c.select({ ...user }); expect(c.selected()).toBeNull();
  });

  it('resets assent on input or selection changes and refuses noncurrent or disabled identities', async () => {
    const c = open().componentInstance; select(c);
    c.select({ ...user }); expect(c.selected()).toBe(user);
    const disabled = { ...user, accountEnabled: false }; c.page.set({ users: [disabled], nextPageToken: null });
    c.selected.set(null); c.select(disabled); expect(c.selected()).toBeNull();
    select(c); c.form.controls.domain.setValue('example.test');
    expect(c.reviewed.value).toBe(false); expect(c.selected()).toBeNull(); await c.bind(); expect(command).not.toHaveBeenCalled();
  });

  it('blocks duplicate programmatic binding while dispatched and preserves uncertainty on a failed inspection', async () => {
    const c = open().componentInstance, pending = deferred<unknown>(); select(c); command.mockReturnValueOnce(pending.promise);
    const saving = c.bind(); c.reviewed.setValue(true); await c.bind(); expect(command).toHaveBeenCalledTimes(1);
    pending.resolve({ ok: false, unknown: true, status: 0, message: 'Lost' }); await saving;
    get.mockRejectedValueOnce(new Error('Denied')); await c.inspectBinding();
    expect(c.page()).toBeNull(); expect(c.selected()).toBeNull(); expect(c.uncertain()).toBe(true); expect(marker).not.toBeNull();
    await c.bind(); expect(command).toHaveBeenCalledTimes(1);
  });

  for (const failure of [false, true]) it(`does not open a role dialog after a late access ${failure ? 'failure' : 'success'} in another lifetime`, async () => {
    const f = open(), c = f.componentInstance; select(c); command.mockResolvedValueOnce({ ok: true, value: tenant });
    get.mockResolvedValueOnce(status()); await c.bind();
    const later = deferred<unknown>(); get.mockReturnValueOnce(later.promise); const assigning = c.assign(tenant); f.destroy();
    if (failure) later.reject(new Error('late')); else later.resolve({ users: [], disabledOrRevoked: [] });
    await assigning; expect(dialog.open).not.toHaveBeenCalled(); expect(c.message()).toBe(''); expect(c.boundId()).toBeNull();
  });

});
