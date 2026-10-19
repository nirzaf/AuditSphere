import { afterNextRender, Component, DestroyRef, ElementRef, Injector, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { PortfolioNavigation } from '../portfolio/portfolio-contracts';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { guidPattern } from '../../core/contracts';
import { Client, ClientLocation, clientLocation, decodeClient, decodeClientArchiveState, clientUtcTime, portalIntentExplanation } from './client-contracts';
import { ClientRelationships } from './client-relationships';
export { decodeClient } from './client-contracts';
type ArchiveFocus = { owner: string; interaction: number; origin: HTMLElement; isArchived: boolean; revision: string };

@Component({
  selector: 'audit-client',
  host: {
    '(document:pointerdown)': 'recordFocusInteraction()',
    '(document:keydown)': 'recordFocusInteraction()',
  },
  imports: [
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
    ClientRelationships,
    ...SHARED,
  ],
  templateUrl: './client.html',
  styleUrl: './client.scss',
})
export class ClientDetail {
  readonly portfolioNavigation = inject(PortfolioNavigation);
  private readonly http = inject(HttpClient);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderInjector = inject(Injector);
  private focusInteraction = 0;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly utcTime = clientUtcTime;
  readonly portalExplanation = portalIntentExplanation;
  readonly location = signal<ClientLocation | null>(clientLocation(k => this.route.snapshot.queryParamMap.get(k)));
  private viewRevision = 0;
  private routeRevision = 0;
  private destroyed = false;
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private archiveRequest?: Subscription;
  private id = '';
  readonly data = signal<Client | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly archiveBusy = signal(false);
  readonly archiveMessage = signal('');
  readonly archiveFailed = signal(false);
  constructor() {
    const routeSubscription = this.route.paramMap.subscribe((p) => {
      this.routeRevision++;
      this.id = (p.get('id') ?? '').toLowerCase();
      this.archiveBusy.set(false);
      this.archiveMessage.set('');
      this.archiveFailed.set(false);
      this.load();
    });
    const querySubscription = this.route.queryParamMap.subscribe(p => {
      this.location.set(clientLocation(k => p.get(k)));
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.routeRevision++;
          this.request?.unsubscribe();
        this.archiveRequest?.unsubscribe();
        this.archiveBusy.set(false);
        this.data.set(null);
          if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.routeRevision++;
      this.viewRevision++;
      routeSubscription.unsubscribe();
      querySubscription.unsubscribe();
      this.request?.unsubscribe();
      this.archiveRequest?.unsubscribe();
    });
  }
  page(kind: 'engagement' | 'contact', page: number): void {
    const location = this.location(), data = this.data();
    if (!location || !data || !Number.isSafeInteger(page) || page < 0 || page > 10000) return;
    const total = kind === 'engagement' ? data.metrics.engagements : data.metrics.contacts;
    const size = kind === 'engagement' ? location.engagementPageSize : location.contactPageSize;
    if (page > Math.max(0, Math.ceil(total / size) - 1)) return;
    this.navigate({ ...location, [kind + 'Page']: page });
  }
  size(kind: 'engagement' | 'contact', value: string): void {
    const location = this.location();
    if (!location || !['10', '25', '50'].includes(value)) return;
    this.navigate({ ...location, [kind + 'Page']: 0, [kind + 'PageSize']: Number(value) });
  }
  private navigate(location: ClientLocation): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: location });
  }
  recordFocusInteraction(): void { this.focusInteraction++; }
  setArchived(isArchived: boolean): void {
    const client = this.data();
    if (!client || !client.canManageArchive || this.archiveBusy() || client.isArchived === isArchived) return;
    const id = this.id, owner = this.owner();
    const document = this.hostElement.nativeElement.ownerDocument;
    const active = document.activeElement;
    const origin = active instanceof HTMLElement && active.matches('button[data-client-archive]') &&
      this.hostElement.nativeElement.contains(active) ? active : null;
    const interaction = this.focusInteraction;
    this.archiveBusy.set(true); this.archiveMessage.set(''); this.archiveFailed.set(false);
    this.archiveRequest = this.http.put<unknown>(`/api/ui/clients/${id}/archive-state`, {
      isArchived, expectedRevision: client.lifecycleRevision,
    }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (owner !== this.owner() || this.destroyed) return;
        this.archiveBusy.set(false);
        try {
          const state = decodeClientArchiveState(value);
          if (state.isArchived !== isArchived) throw new Error('Unexpected client archive state');
          this.archiveMessage.set(isArchived
            ? 'Client archived. Its profile and authorized engagement history remain searchable.'
            : 'Client restored. Its existing history and authorization remain unchanged.');
          this.load(origin ? { owner, interaction, origin, isArchived, revision: state.revision } : undefined);
        } catch {
          this.archiveFailed.set(true); this.archiveMessage.set('The server returned an unsupported archive response. Refresh to review the current state.');
        }
      },
      error: failure => {
        if (owner !== this.owner() || this.destroyed) return;
        this.archiveBusy.set(false); this.archiveFailed.set(true);
        this.archiveMessage.set(failure.status === 409
          ? 'The client archive state changed elsewhere. Refresh the profile before retrying.'
          : 'Client archive state was not confirmed. Refresh to review the current state.');
        if (failure.status === 401) this.session.clear();
      },
    });
  }
  private owner(): string {
    const s = this.session.current();
    return s?.staff ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}:${this.id}:${this.routeRevision}` : '';
  }
  load(archiveFocus?: ArchiveFocus): void {
    const revision = ++this.viewRevision;
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.loading.set(false);
    if (this.destroyed || !this.session.current()?.staff) return;
    const id = this.id, location = this.location(), owner = this.owner();
    if (!guidPattern.test(id) || !location) {
      this.error.set('The client link or page parameters are invalid.');
      return;
    }
    this.loading.set(true);
    const query = new URLSearchParams(Object.entries(location).map(([k,v]) => [k, String(v)]));
    this.request = this.http.get<unknown>('/api/ui/clients/' + id + '?' + query).pipe(timeout(15000)).subscribe({
      next: (value) => {
        if (revision !== this.viewRevision || owner !== this.owner() || this.destroyed) return;
        try {
          const decoded = decodeClient(value);
          if (decoded.id.toLowerCase() !== id || Object.entries(location).some(([key, n]) => decoded.paging[key as keyof ClientLocation] !== n))
            throw new Error('Wrong client context');
          this.data.set(decoded);
          if (archiveFocus && decoded.isArchived === archiveFocus.isArchived && decoded.lifecycleRevision === archiveFocus.revision)
            this.restoreArchiveFocus(archiveFocus, revision);
        } catch { this.error.set('The server returned an unsupported response.'); }
        this.loading.set(false);
      },
      error: (failure) => {
        if (revision !== this.viewRevision || owner !== this.owner() || this.destroyed) return;
        this.loading.set(false);
        this.error.set('Check your access or retry shortly.');
        if (failure.status === 401) this.session.clear();
      },
    });
  }
  private restoreArchiveFocus(context: ArchiveFocus, revision: number): void {
    afterNextRender(() => {
      const host = this.hostElement.nativeElement;
      const active = host.ownerDocument.activeElement;
      const client = this.data();
      if (this.destroyed || context.owner !== this.owner() || revision !== this.viewRevision ||
          context.interaction !== this.focusInteraction || client?.lifecycleRevision !== context.revision ||
          client.isArchived !== context.isArchived || (active !== context.origin && active !== host.ownerDocument.body)) return;
      const target = host.querySelector<HTMLButtonElement>('button[data-client-archive]') ??
        host.querySelector<HTMLButtonElement>('button[data-client-refresh]');
      target?.focus();
    }, { injector: this.renderInjector });
  }

}
