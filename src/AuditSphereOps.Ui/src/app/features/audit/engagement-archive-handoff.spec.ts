import { afterEach, describe, expect, it, vi } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router, NavigationStart, type Event } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import { decodeArchiveHandoff, EngagementArchiveHandoff } from './engagement-archive-handoff';
import { workbenchLinksFor } from '../engagements/engagement-workflow-contracts';

const engagementId = '11111111-2222-3333-4444-555555555555';
const archiveId = '66666666-7777-8888-9999-000000000000';
const clientId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

describe('Engagement archive handoff identity', () => {
  it('uses an engagement handoff, retaining the independently returned archive identity', () => {
    const link = workbenchLinksFor('ARCHIVE_CLOSE', engagementId, clientId).find(x => x.key === 'records');
    expect(link?.href).toBe(`/app/engagements/${engagementId}/archive`);
    const result = decode(decodeArchiveHandoff, { engagementId, clientId,
      archives: [{ archiveId, profileId: 'TEST', profileVersion: 1, status: 'ISSUED' }],
      profiles: [], canCreate: false, blockedReason: null });
    expect(result.archives[0].archiveId).toBe(archiveId);
    expect(result.archives[0].archiveId).not.toBe(result.engagementId);
  });
  it('retains a blocked result without inventing a profile or archive', () => {
    const result = decode(decodeArchiveHandoff, { engagementId, clientId, archives: [], profiles: [],
      canCreate: false, blockedReason: 'No approved records profile is available for this engagement route.' });
    expect(result.canCreate).toBe(false);
    expect(result.archives).toEqual([]);
    expect(result.profiles).toEqual([]);
  });
});


describe('Engagement archive handoff interaction', () => {
  afterEach(() => TestBed.resetTestingModule());
  function setup(archives: { archiveId: string; profileId: string; profileVersion: number; status: string }[] = []) {
    const data = signal<ReturnType<typeof decodeArchiveHandoff> | null>(null);
    const resource = { data, loading: signal(false), error: signal(''), reload: vi.fn() };
    let decoder!: typeof decodeArchiveHandoff;
    const api = { resource: vi.fn((_url: unknown, d: typeof decoder) => { decoder = d; return resource; }),
      command: vi.fn().mockResolvedValue({ ok: true, value: { archiveId, created: true } }) };
    const session = { current: signal({ userId: clientId, firmId: clientId, staff: true }), invalidation: signal(0) };
    TestBed.configureTestingModule({ imports: [EngagementArchiveHandoff], providers: [provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: engagementId })) } },
      { provide: Api, useValue: api }, { provide: SessionService, useValue: session }] });
    const events = new Subject<Event>();
    vi.spyOn(TestBed.inject(Router), 'events', 'get').mockReturnValue(events);
    const fixture = TestBed.createComponent(EngagementArchiveHandoff);
    data.set(decoder({ engagementId, clientId, archives,
      profiles: archives.length ? [] : [{ profileId: 'TEST', profileVersion: 1 }],
      canCreate: !archives.length, blockedReason: null }, 'handoff'));
    return { fixture, api, session, resource, events, read: (raw: unknown) => data.set(decoder(raw, 'handoff')) };
  }
  it('shows explicit archive choices and hides them immediately on session invalidation', async () => {
    const { fixture, api, session } = setup([
      { archiveId, profileId: 'TEST', profileVersion: 1, status: 'ISSUED' },
      { archiveId: clientId, profileId: 'TEST', profileVersion: 2, status: 'ISSUED' },
    ]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('a').length).toBe(2);
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(`/app/records/archives/${archiveId}`);
    expect(api.command).not.toHaveBeenCalled();
    session.invalidation.set(1); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('a').length).toBe(0);
  });
  it('explicitly prepares using the selected profile and navigates only with returned archive ID', async () => {
    const { fixture, api } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await fixture.componentInstance.create({ profileId: 'TEST', profileVersion: 1 });
    expect(api.command).toHaveBeenCalledWith('/api/ui/records/archives', {
      clientId, engagementId, profileId: 'TEST', profileVersion: 1,
    });
    expect(navigate).toHaveBeenCalledWith(['/app/records/archives', archiveId]);
  });
  it('ignores a successful late creation response after session replacement', async () => {
    const { fixture, api, session } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    api.command.mockImplementation(async () => {
      session.invalidation.set(1);
      return { ok: true, value: { archiveId, created: true } };
    });
    await fixture.componentInstance.create({ profileId: 'TEST', profileVersion: 1 });
    expect(navigate).not.toHaveBeenCalled();
    expect(api.command).toHaveBeenCalledTimes(1);
  });
  it('fences genuinely unknown creation and discovers the committed archive through explicit refresh', async () => {
    const { fixture, api, resource, read } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    api.command.mockResolvedValue({ ok: false, unknown: true, code: 'outcome.unknown', status: 0,
      message: 'The outcome is not confirmed. Refresh to see the persisted state before repeating this action.' });
    await fixture.componentInstance.create({ profileId: 'TEST', profileVersion: 1 });
    expect(fixture.componentInstance.unresolved()).toBe(true);
    await fixture.componentInstance.create({ profileId: 'TEST', profileVersion: 1 });
    await fixture.componentInstance.create({ profileId: 'OTHER', profileVersion: 2 });
    expect(api.command).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
    resource.reload.mockImplementation(() => read({ engagementId, clientId,
      archives: [{ archiveId, profileId: 'TEST', profileVersion: 1, status: 'ISSUED' }],
      profiles: [], canCreate: false, blockedReason: null }));
    fixture.componentInstance.refresh();
    await fixture.whenStable();
    expect(resource.reload).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(`/app/records/archives/${archiveId}`);
    expect(fixture.nativeElement.textContent).not.toContain('Prepare TEST');
    expect(api.command).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
  });
  it('hides prior choices during navigation and ignores the late creation response', async () => {
    const { fixture, api, events, read } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    let finish!: (value: unknown) => void;
    api.command.mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const pending = fixture.componentInstance.create({ profileId: 'TEST', profileVersion: 1 });
    events.next(new NavigationStart(2, `/app/engagements/${clientId}/archive`));
    read({ engagementId, clientId,
      archives: [{ archiveId, profileId: 'TEST', profileVersion: 1, status: 'ISSUED' }],
      profiles: [], canCreate: false, blockedReason: null });
    finish({ ok: true, value: { archiveId, created: true } });
    await pending;
    fixture.detectChanges();
    expect(fixture.componentInstance.view()).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('a').length).toBe(0);
    expect(fixture.nativeElement.textContent).not.toContain('Prepare TEST');
    await fixture.componentInstance.create({ profileId: 'TEST', profileVersion: 1 });
    expect(api.command).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalled();
  });

});
