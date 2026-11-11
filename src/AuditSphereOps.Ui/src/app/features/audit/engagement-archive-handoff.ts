import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, decode, guid, int, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

export const decodeArchiveHandoff = obj({
  engagementId: guid, clientId: guid,
  archives: arr(obj({ archiveId: guid, profileId: text, profileVersion: int, status: text }), 10000),
  profiles: arr(obj({ profileId: text, profileVersion: int }), 10000),
  canCreate: bool, blockedReason: nullable(text),
});
const decodeCreated = obj({ archiveId: guid, created: bool });

@Component({
  selector: 'audit-engagement-archive-handoff',
  imports: [...SHARED, RouterLink, MatButtonModule],
  template: `
    <audit-page-header title="Engagement archive" eyebrow="Records evidence"
      description="Open an existing archive or explicitly prepare one using an approved records profile." />
    <button matButton="outlined" (click)="refresh()" [disabled]="busy()">Refresh</button>
    <audit-state [loading]="rec.loading()" [error]="rec.error()" label="engagement archive" />
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (view(); as v) {
      @if (v.archives.length > 1) { <p>Select the archive to open.</p> }
      @for (a of v.archives; track a.archiveId) {
        <p><a [routerLink]="['/app/records/archives', a.archiveId]">Open archive {{ a.profileId }} version {{ a.profileVersion }}</a> ({{ a.status }})</p>
      }
      @if (v.blockedReason) { <p role="status">{{ v.blockedReason }}</p> }
      @if (v.canCreate && !v.archives.length) {
        <p>Select the approved profile for this archive. Preparing an archive does not approve a profile or complete the engagement.</p>
        @for (p of v.profiles; track p.profileId + ':' + p.profileVersion) {
          <button matButton="outlined" [disabled]="busy() || unresolved()" (click)="create(p)">Prepare {{ p.profileId }} version {{ p.profileVersion }}</button>
        }
      }
    }
  `,
})
export class EngagementArchiveHandoff {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  readonly id = routeGuid();
  private readonly active = signal(true);
  private readonly readActor = signal<ReturnType<SessionService['current']>>(null);
  private readonly readEpoch = signal(-1);
  private sequence = 0;
  private destroyed = false;
  readonly busy = signal(false);
  readonly unresolved = signal(false);
  readonly message = signal('');
  readonly rec = this.api.resource(() => this.active() && this.id() ? `/api/ui/engagements/${this.id()}/archive` : null,
    (raw, path) => {
      const value = decodeArchiveHandoff(raw, path);
      if (value.engagementId !== this.id()) throw new Error('Engagement archive identity mismatch');
      this.readActor.set(this.session.current()); this.readEpoch.set(this.session.invalidation());
      return value;
    }, 'The engagement archive is not available in the current scope.');
  readonly view = computed(() => !this.destroyed && this.active() && this.session.current()?.staff &&
    this.readActor() === this.session.current() && this.readEpoch() === this.session.invalidation() &&
    !this.rec.loading() && !this.rec.error() && this.rec.data()?.engagementId === this.id() ? this.rec.data() : null);

  constructor() {
    const events = this.router.events.subscribe(event => {
      if (event instanceof NavigationStart) { this.active.set(false); ++this.sequence; }
      else if (event instanceof NavigationCancel || event instanceof NavigationError) this.active.set(true);
      else if (event instanceof NavigationEnd) this.active.set(/^\/(?:ui\/)?app\/engagements\/[^/]+\/archive$/.test(event.urlAfterRedirects.split(/[?#]/)[0]));
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; ++this.sequence; events.unsubscribe(); });
  }
  refresh(): void { this.rec.reload(); }
  async create(profile: { profileId: string; profileVersion: number }): Promise<void> {
    const view = this.view();
    if (!view || !view.canCreate || view.archives.length || this.busy() || this.unresolved() ||
      !view.profiles.some(p => p.profileId === profile.profileId && p.profileVersion === profile.profileVersion)) return;
    const owner = ++this.sequence;
    const actor = this.session.current(), epoch = this.session.invalidation(), id = this.id();
    this.busy.set(true); this.message.set('');
    const outcome = await this.api.command('/api/ui/records/archives', {
      clientId: view.clientId, engagementId: view.engagementId, ...profile,
    });
    this.busy.set(false);
    if (this.destroyed || !this.active() || owner !== this.sequence || id !== this.id() ||
      actor !== this.session.current() || epoch !== this.session.invalidation()) return;
    if (!outcome.ok) {
      this.unresolved.set(outcome.unknown); this.message.set(outcome.message);
      return;
    }
    try {
      const archive = decode(decodeCreated, outcome.value);
      await this.router.navigate(['/app/records/archives', archive.archiveId]);
    } catch {
      this.unresolved.set(true);
      this.message.set('The archive result could not be verified. Refresh and open the persisted archive before preparing another.');
    }
  }
}
