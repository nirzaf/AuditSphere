import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';

export interface ClientRow {
  id: string;
  name: string;
  engagements: number;
}
export interface PortfolioPage {
  items: ClientRow[];
  total: number;
  page: number;
  pageSize: number;
}
export function decodePortfolio(value: unknown): PortfolioPage {
  if (!value || typeof value !== 'object') throw new Error('Invalid portfolio');
  const v = value as Record<string, unknown>;
  if (
    !Array.isArray(v['items']) ||
    !['total', 'page', 'pageSize'].every((k) => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0)
  )
    throw new Error('Invalid pagination');
  if (Number(v['page']) > 10000 || Number(v['pageSize']) < 1 || Number(v['pageSize']) > 100 || v['items'].length > Number(v['pageSize'])) throw new Error('Invalid page bounds');
  for (const row of v['items']) {
    if (
      !row ||
      typeof row.id !== 'string' ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(row.id) ||
      typeof row.name !== 'string' ||
      !Number.isSafeInteger(row.engagements) ||
      row.engagements < 0
    )
      throw new Error('Invalid client');
  }
  return v as unknown as PortfolioPage;
}
@Component({
  selector: 'audit-portfolio',
  imports: [RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  template: `
    <header>
      <p class="eyebrow">Practice overview · Current explicit scope</p>
      <h1>Portfolio</h1>
      <p>Clients and engagement counts visible under your current AuditSphere assignments.</p>
    </header>
    <form (submit)="search($event, filter.value)">
      <mat-form-field
        ><mat-label>Search client name or ID</mat-label><input matInput #filter maxlength="100"
      /></mat-form-field>
      <button matButton="filled" type="submit">Search</button>
    </form>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading portfolio" />
    }
    @if (error()) {
      <section role="alert">
        <h2>Portfolio unavailable</h2>
        <p>{{ error() }}</p>
        <button matButton (click)="load()">Retry</button>
      </section>
    }
    @if (data(); as result) {
      <p role="status">{{ result.total }} clients in this search</p>
      @if (!result.items.length) {
        <p>No clients match this search in your current scope.</p>
      } @else {
        <div class="table-scroll">
          <table>
            <caption>
              Authorized clients
            </caption>
            <thead>
              <tr>
                <th>Client</th>
                <th>Visible engagements</th>
              </tr>
            </thead>
            <tbody>
              @for (client of result.items; track client.id) {
                <tr>
                  <td>
                    <strong><a [routerLink]="['/app/clients', client.id]">{{ client.name }}</a></strong
                    ><small>{{ client.id }}</small>
                  </td>
                  <td class="number">{{ client.engagements }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
      <nav aria-label="Client pages">
        <button matButton [disabled]="result.page === 0" (click)="load(result.page - 1)">
          Previous
        </button>
        <span>Page {{ result.page + 1 }}</span
        ><button
          matButton
          [disabled]="(result.page + 1) * result.pageSize >= result.total"
          (click)="load(result.page + 1)"
        >
          Next
        </button>
      </nav>
    }
  `,
})
export class Portfolio {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private query = '';
  readonly data = signal<PortfolioPage | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.data.set(null);
        this.request?.unsubscribe();
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }
  search(event: Event, value: string): void {
    event.preventDefault();
    this.query = value.trim();
    this.load();
  }
  load(page = 0): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.loading.set(true);
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>('/api/ui/portfolio', { params: { search: this.query, page, pageSize: 25 } })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            this.data.set(decodePortfolio(value));
          } catch {
            this.error.set('The server returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set('Refresh your session or ask an administrator to check your assignment.');
          if (failure.status === 401 || failure.status === 403) this.session.clear();
        },
      });
  }
}
