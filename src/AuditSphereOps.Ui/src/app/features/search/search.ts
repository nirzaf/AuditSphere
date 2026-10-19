import {
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { MatAutocompleteModule, MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { filter, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { workspaceRoute } from '../../core/navigation';
export { migratedHref } from '../../core/navigation';
interface Hit {
  kind: string;
  title: string;
  detail: string;
  href: string;
}
interface SearchResult {
  term: string;
  hits: Hit[];
  truncated: boolean;
}
export function decodeSearch(value: unknown): SearchResult {
  if (!value || typeof value !== 'object') throw new Error('Invalid search');
  const v = value as Record<string, unknown>;
  if (
    typeof v['term'] !== 'string' ||
    v['term'].length > 100 ||
    typeof v['truncated'] !== 'boolean' ||
    !Array.isArray(v['hits']) ||
    v['hits'].length > 100
  )
    throw new Error('Invalid search');
  for (const h of v['hits']) {
    if (
      !h ||
      !['kind', 'title', 'detail', 'href'].every((k) => typeof h[k] === 'string') ||
      !/^\/app(?:\/|$)/.test(h.href) ||
      /[\\\s?#]/.test(h.href)
    )
      throw new Error('Invalid search link');
  }
  return v as unknown as SearchResult;
}
@Component({
  selector: 'audit-global-search',
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    RouterLink,
  ],
  template: `
    <section aria-label="Global search">
      <form (submit)="search($event, term.value)">
        <mat-form-field
          ><mat-label>Search your workspace</mat-label
          ><input
            matInput
            #term
            maxlength="100"
            minlength="2"
            [matAutocomplete]="suggestions"
            [matAutocompleteDisabled]="loading() || !!data() || !!error()"
            (input)="typing(term.value)"
            (keydown.escape)="dismiss()"
            aria-describedby="search-help"
        /></mat-form-field>
        <button matButton type="submit">Search</button>
      </form>
      <mat-autocomplete #suggestions="matAutocomplete" (optionSelected)="open($event.option.value)">
        @for (hit of data()?.hits ?? []; track $index) {
          <mat-option [value]="hit.href" [disabled]="!route(hit.href)">{{ hit.title }} · {{ hit.kind }}</mat-option>
        }
      </mat-autocomplete>
      <p id="search-help" class="sr-only">
        Press slash outside fields and dialogs to focus search. Search authorized clients,
        engagements, PBC requests, leads, invoices, library and pages. Documents and emails are not
        searched.
      </p>
      @if (loading() || error() || data()) {
        <div class="search-panel" role="region" aria-label="Search results">
          @if (loading()) {
            <mat-progress-bar mode="indeterminate" aria-label="Searching workspace" />
          }
          @if (error()) {
            <p role="alert">{{ error() }}</p>
          }
          @if (data(); as result) {
            <p role="status">
              {{ result.hits.length }} results{{
                result.truncated ? ' · Refine your search for more specific results' : ''
              }}
            </p>
            <ul>
              @for (hit of result.hits; track $index) {
                <li>
                  @if (route(hit.href); as destination) {
                    <a [routerLink]="destination">{{ hit.title }}</a>
                  } @else {
                    <span>{{ hit.title }}</span><small>This page is not available in this workspace.</small>
                  }
                  <p>{{ hit.kind }} · {{ hit.detail }}</p>
                </li>
              }
            </ul>
            <button matButton (click)="dismiss()">Close search results</button>
          }
        </div>
      }
    </section>
  `,
  styles: `
    :host { display: block; position: relative; flex: 0 1 26rem; min-width: 12rem; max-width: 34rem; }
    section { position: relative; }
    form { display: flex; align-items: center; gap: .5rem; margin: 0; }
    mat-form-field { flex: 1 1 auto; min-width: 0; margin-block-end: -1.25rem; }
    form > button { min-height: 44px; color: var(--as-shell-text, #f8fafc); }
    .search-panel { position: absolute; inset-block-start: calc(100% + .375rem); inset-inline-end: 0;
      z-index: 100; box-sizing: border-box; width: min(42rem, calc(100vw - 2rem));
      max-height: min(65vh, 34rem); overflow: auto; padding: .75rem 1rem;
      border: 1px solid var(--as-line, #d8e1eb); border-radius: .625rem;
      background: white; color: var(--as-ink, #12385d); box-shadow: var(--as-shadow-2, 0 2px 4px rgb(16 42 67 / 8%)); }
    .search-panel ul { margin: .5rem 0; padding-inline-start: 1.25rem; }
    .search-panel li { padding-block: .375rem; border-block-end: 1px solid var(--as-line, #d8e1eb); }
    .search-panel li p { margin: .2rem 0 0; color: var(--as-text-muted, #455468); font-size: .875rem; }
    .search-panel a { color: var(--as-link, #0b5cab); font-weight: 600; }
    .search-panel a:focus-visible, .search-panel button:focus-visible { outline: 3px solid var(--as-focus, #075a9c); outline-offset: 2px; }
    @media (max-width: 700px) {
      :host { flex: 1 0 100%; max-width: none; min-width: 0; }
      .search-panel { inset-inline-start: 0; inset-inline-end: auto; width: min(42rem, calc(100vw - 1rem)); }
    }
  `,
})
export class GlobalSearch {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private request?: Subscription;
  private debounce?: ReturnType<typeof setTimeout>;
  private lastRequestedTerm: string | null = null;
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('term');
  private readonly suggestionsTrigger = viewChild(MatAutocompleteTrigger);
  readonly data = signal<SearchResult | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly route = workspaceRoute;
  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        this.dismiss();
        const field = this.field();
        if (field) field.nativeElement.value = '';
      });
    effect(() => {
      this.session.invalidation();
      untracked(() => {
        this.dismiss();
        const field = this.field();
        if (field) field.nativeElement.value = '';
      });
    });
    const shortcut = (event: KeyboardEvent) => {
      const target = event.target instanceof HTMLElement ? event.target : null;
      if (
        event.key !== '/' ||
        event.altKey ||
        event.ctrlKey ||
        event.metaKey ||
        target?.closest('input,textarea,select,[contenteditable="true"],[role="textbox"]') ||
        document.querySelector('[role="dialog"],dialog[open]')
      )
        return;
      event.preventDefault();
      this.field()?.nativeElement.focus();
    };
    document.addEventListener('keydown', shortcut);
    inject(DestroyRef).onDestroy(() => {
      this.request?.unsubscribe();
      if (this.debounce) clearTimeout(this.debounce);
      document.removeEventListener('keydown', shortcut);
    });
  }
  dismiss(): void {
    // Disabling the trigger does not detach a panel that was already open.
    this.suggestionsTrigger()?.closePanel();
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = undefined;
    this.request?.unsubscribe();
    this.lastRequestedTerm = null;
    this.data.set(null);
    this.error.set('');
    this.loading.set(false);
  }
  typing(value: string): void {
    const term = value.trim();
    if (term && this.lastRequestedTerm === term) return;
    this.dismiss();
    if (term) this.debounce = setTimeout(() => this.run(value), 250);
  }
  open(href: unknown): void {
    if (typeof href !== 'string' || !this.data()?.hits.some((h) => h.href === href)) return;
    const destination = workspaceRoute(href);
    if (destination) void this.router.navigateByUrl(destination);
  }
  search(event: Event, value: string): void {
    event.preventDefault();
    // Cancel the typeahead synchronously so Enter submits only one request.
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = undefined;
    this.run(value);
  }
  private run(value: string): void {
    if (!this.session.current()?.staff) return;
    const term = value.trim();
    if (term.length < 2 || term.length > 100) {
      this.dismiss();
      this.error.set('Enter between 2 and 100 characters.');
      return;
    }
    if (this.lastRequestedTerm === term) return;
    this.dismiss();
    this.lastRequestedTerm = term;
    this.loading.set(true);
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>('/api/ui/search', { params: { term } })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          this.suggestionsTrigger()?.closePanel();
          try {
            this.data.set(decodeSearch(value));
          } catch {
            this.lastRequestedTerm = null;
            this.error.set('Search returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.suggestionsTrigger()?.closePanel();
          this.lastRequestedTerm = null;
          this.loading.set(false);
          this.error.set('Search unavailable. Check your access or retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
