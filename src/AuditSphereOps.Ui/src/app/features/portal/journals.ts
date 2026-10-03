import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SHARED } from '../../core/ui';
import { decodePortalJournals } from '../accounting/journal-management-contracts';
@Component({
  selector: 'audit-portal-journals',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: ` <section class="panel" aria-label="Client journal management queue">
    <h2>Adjustment journals for management review</h2>
    <button matButton (click)="queue.reload()">Refresh management journals</button
    ><audit-state
      [loading]="queue.loading()"
      [error]="queue.error()"
      label="your management journals"
    />
    @if (queue.data(); as q) {
      @for (j of q.items; track j.id) {
        <article>
          <h3>
            <a [routerLink]="['/portal/accounting/journals', j.id]"
              >{{ j.number }} · revision {{ j.revision }}</a
            >
          </h3>
          <p>{{ j.purpose }} · {{ j.currency }} <audit-status [value]="j.status" /></p>
        </article>
      } @empty {
        <p>No journals are available in your current client or engagement scope.</p>
      }
      <nav class="actions" aria-label="Management journal pages">
        <button
          matButton
          (click)="page.set(page() - 1)"
          [disabled]="page() === 0 || queue.loading()"
        >
          Previous management journals</button
        ><span>Page {{ q.page + 1 }}</span
        ><button
          matButton
          (click)="page.set(page() + 1)"
          [disabled]="!q.hasMore || queue.loading()"
        >
          Next management journals
        </button>
      </nav>
    }
  </section>`,
})
export class PortalJournals {
  private readonly api = inject(Api);
  readonly page = signal(0);
  readonly queue = this.api.resource(
    () => '/api/ui/portal/accounting/journals?page=' + this.page(),
    decodePortalJournals,
  );
}
