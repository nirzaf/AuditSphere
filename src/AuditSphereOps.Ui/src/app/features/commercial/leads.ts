import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { Drafts } from '../../core/drafts';
interface Lead {
  id: string;
  name: string;
  source: string;
  primaryContactName: string | null;
  primaryContactEmail: string | null;
  status: string;
  createdAt: string;
}
interface LeadPage {
  items: Lead[];
  total: number;
  page: number;
  pageSize: number;
}
export function decodeLeads(value: unknown): LeadPage {
  if (!value || typeof value !== 'object') throw new Error('Invalid leads');
  const v = value as Record<string, unknown>;
  if (
    !Array.isArray(v['items']) ||
    !['total', 'page', 'pageSize'].every((k) => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0) ||
    Number(v['page']) > 10000 ||
    Number(v['pageSize']) < 1 ||
    Number(v['pageSize']) > 100 ||
    v['items'].length > Number(v['pageSize'])
  )
    throw new Error('Invalid pagination');
  for (const l of v['items'])
    if (
      !l ||
      typeof l.id !== 'string' ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(l.id) ||
      !['name', 'source', 'status', 'createdAt'].every((k) => typeof l[k] === 'string') ||
      !['primaryContactName', 'primaryContactEmail'].every(
        (k) => l[k] === null || typeof l[k] === 'string',
      )
    )
      throw new Error('Invalid lead');
  return v as unknown as LeadPage;
}
@Component({
  selector: 'audit-leads',
  imports: [
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  template: `
    <h1>Practice leads</h1>
    <a routerLink="/app/practice/commercial-settings">Commercial settings</a>
    <p>Commercial qualification remains separate from client acceptance and credit decisions.</p>
    <form (ngSubmit)="load()">
      <mat-form-field
        ><mat-label>Search lead name</mat-label
        ><input matInput name="search" [(ngModel)]="search" maxlength="100" /></mat-form-field
      ><button matButton type="submit">Search</button>
    </form>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading leads" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
      <button matButton (click)="load()">Retry</button>
    }
    @if (data(); as leads) {
      <section>
        <h2>Record lead</h2>
        <form (ngSubmit)="create()">
          <mat-form-field
            ><mat-label>Lead name</mat-label
            ><input
              matInput
              name="name"
              [(ngModel)]="name"
              (ngModelChange)="touchDraft()"
              required
              maxlength="200"
              [disabled]="busy()"
          /></mat-form-field>
          <mat-form-field
            ><mat-label>Source</mat-label
            ><input
              matInput
              name="source"
              [(ngModel)]="source"
              (ngModelChange)="touchDraft()"
              required
              maxlength="200"
              [disabled]="busy()"
          /></mat-form-field>
          <mat-form-field
            ><mat-label>Contact name</mat-label
            ><input
              matInput
              name="contactName"
              [(ngModel)]="contactName"
              (ngModelChange)="touchDraft()"
              maxlength="200"
              [disabled]="busy()"
          /></mat-form-field>
          <mat-form-field
            ><mat-label>Contact email</mat-label
            ><input
              matInput
              type="email"
              name="contactEmail"
              [(ngModel)]="contactEmail"
              (ngModelChange)="touchDraft()"
              maxlength="254"
              [disabled]="busy()"
          /></mat-form-field>
          <button matButton="filled" type="submit" [disabled]="busy() || uncertain()">
            Record lead
          </button>
        </form>
      </section>
      <p role="status">{{ leads.total }} leads in this search</p>
      <div class="table-scroll">
        <table>
          <caption>
            Commercial leads
          </caption>
          <thead>
            <tr>
              <th>Name</th>
              <th>Source</th>
              <th>Contact</th>
              <th>Status</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (lead of leads.items; track lead.id) {
              <tr>
                <td>
                  <a [routerLink]="['/app/practice/leads', lead.id]">{{ lead.name }}</a>
                </td>
                <td>{{ lead.source }}</td>
                <td>{{ lead.primaryContactName }} · {{ lead.primaryContactEmail }}</td>
                <td>{{ lead.status }}</td>
                <td>
                  @if (lead.status === 'NEW') {
                    <button
                      matButton
                      [disabled]="busy() || uncertain()"
                      (click)="qualification.set(lead)"
                    >
                      Qualify {{ lead.name }}
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (!leads.items.length) {
        <p>No leads match this search.</p>
      }
      <nav aria-label="Lead pages">
        <button matButton [disabled]="leads.page === 0" (click)="load(leads.page - 1)">
          Previous</button
        ><span>Page {{ leads.page + 1 }}</span
        ><button
          matButton
          [disabled]="(leads.page + 1) * leads.pageSize >= leads.total"
          (click)="load(leads.page + 1)"
        >
          Next
        </button>
      </nav>
      @if (qualification(); as lead) {
        <section role="alert">
          <h2>Qualify {{ lead.name }}?</h2>
          <p>This does not grant professional acceptance or client access.</p>
          <button matButton [disabled]="busy() || uncertain()" (click)="qualify(lead.id)">
            Confirm qualification</button
          ><button matButton [disabled]="busy()" (click)="qualification.set(null)">Cancel</button>
        </section>
      }
    }
    <p role="status">{{ commandStatus() }}</p>
  `,
})
export class Leads {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  search = '';
  name = '';
  source = '';
  contactName = '';
  contactEmail = '';
  private readonly drafts = inject(Drafts);
  private draftTimer?: ReturnType<typeof setTimeout>;
  private static validDraft(value: unknown): { name: string; source: string; contactName: string; contactEmail: string } | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    const str = (k: string, max: number) => typeof v[k] === 'string' && (v[k] as string).length <= max ? v[k] as string : null;
    return str('name', 200) !== null && str('source', 200) !== null && str('contactName', 200) !== null && str('contactEmail', 254) !== null
      ? { name: str('name', 200)!, source: str('source', 200)!, contactName: str('contactName', 200)!, contactEmail: str('contactEmail', 254)! }
      : null;
  }
  touchDraft(): void {
    clearTimeout(this.draftTimer);
    this.draftTimer = setTimeout(
      () => this.drafts.save('commercial-lead-draft', { name: this.name, source: this.source, contactName: this.contactName, contactEmail: this.contactEmail }), 800);
  }
  readonly data = signal<LeadPage | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  readonly qualification = signal<Lead | null>(null);
  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.draftTimer));
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.data.set(null);
        this.search = this.name = this.source = this.contactName = this.contactEmail = '';
        this.qualification.set(null);
        this.commandStatus.set('');
        this.uncertain.set(false);
        if (staff) {
          const saved = this.drafts.load('commercial-lead-draft', Leads.validDraft);
          if (saved) { this.name = saved.name; this.source = saved.source; this.contactName = saved.contactName; this.contactEmail = saved.contactEmail; }
          this.load();
        }
      });
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }
  create(): void {
    this.command('', {
      name: this.name,
      source: this.source,
      contactName: this.contactName || null,
      contactEmail: this.contactEmail || null,
    });
  }
  qualify(id: string): void {
    this.command('/' + id + '/qualify', {});
  }
  private command(path: string, body: object): void {
    if (!this.data() || this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.commandStatus.set('Saving commercial change…');
    this.http
      .post('/api/ui/leads' + path, body)
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.busy.set(false);
          if (generation !== this.session.invalidation()) return;
          this.name = this.source = this.contactName = this.contactEmail = '';
          this.drafts.clear('commercial-lead-draft');
          this.qualification.set(null);
          this.commandStatus.set('Commercial change recorded.');
          this.load();
        },
        error: (failure) => {
          this.busy.set(false);
          if (generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Change refused. Check current access, required fields and existing leads.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Outcome unconfirmed. Review current leads before resubmitting.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(page = 0): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    if (!this.session.current()?.staff) return;
    const generation = this.session.invalidation();
    this.loading.set(true);
    this.request = this.http
      .get<unknown>('/api/ui/leads', { params: { search: this.search, page } })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            this.data.set(decodeLeads(value));
          } catch {
            this.error.set('Leads returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set(
            'Leads unavailable. A current firm-wide commercial assignment is required.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
