import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const person = obj({ id: guid, name: text, email: nullable(text) });
export const decodePbcInbox = obj({ engagementId: guid, serviceRoute: text, periodStart: text, periodEnd: text,
  requests: arr(obj({ id: guid, area: text, objective: text, dueDate: text, state: text, revision: nat,
    intents: arr(obj({ id: guid, fileName: text, receivedByteCount: nat, declaredByteCount: nat, state: text, transferState: nullable(text), declaredSha256Hex: text }), 1000),
    timeline: arr(obj({ at: instant, label: text, body: text }), 5000), canRequestMore: bool }), 2000),
  clientOwners: arr(person, 500), reviewers: arr(person, 500) });

@Component({
  selector: 'audit-pbc-inbox',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="PBC location"><a routerLink="/app">Portfolio</a> / <span>PBC requests</span></nav>
    <audit-page-header title="Prepared-by-client requests" eyebrow="Engagement documents"
      description="Only request metadata, upload receipts and durable transfer state are shown. A received file is not accepted evidence until the assigned reviewer completes suitability review." />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="the scoped PBC inbox" />
    @if (ws.data(); as w) {
      <p class="actions" aria-label="PBC navigation"><a [routerLink]="['/app/engagements', w.engagementId]">Engagement overview</a>
        <a [routerLink]="['/app/engagements', w.engagementId, 'audit-fieldwork']">Audit fieldwork</a></p>
      <p role="status">{{ w.requests.length }} requests · {{ intents(w) }} upload intents · {{ received(w) }} received uploads</p>
      <section class="panel" aria-labelledby="new-request-heading">
        <h2 id="new-request-heading">Request files from the client</h2>
        <p>The client receives a minimal email notification with this description and an authenticated portal upload link.</p>
        <form class="inline-form" (submit)="$event.preventDefault(); create()">
          <label>Files required <textarea name="desc" [(ngModel)]="f.description" maxlength="2000" required></textarea></label>
          <label>Client recipient <select name="owner" [(ngModel)]="f.owner" required><option value="">Select client user</option>
            @for (u of w.clientOwners; track u.id) { <option [value]="u.id">{{ u.name }} ({{ u.email }})</option> }</select></label>
          <label>Reviewer <select name="reviewer" [(ngModel)]="f.reviewer" required><option value="">Select reviewer</option>
            @for (u of w.reviewers; track u.id) { <option [value]="u.id">{{ u.name }}</option> }</select></label>
          <label>Due date <input type="date" name="due" [(ngModel)]="f.due" required /></label>
          <label>Preferred format (optional) <input name="format" [(ngModel)]="f.format" maxlength="200" /></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy() || !w.clientOwners.length || !w.reviewers.length">{{ cmd.busy() ? 'Sending…' : 'Send file request' }}</button>
        </form>
      </section>
      @for (r of w.requests; track r.id) {
        <section class="panel" [attr.aria-labelledby]="'pbc-' + r.id">
          <h2 [id]="'pbc-' + r.id">{{ r.area }} <audit-status [value]="r.state" /></h2>
          <p><small>{{ r.objective }} · due {{ r.dueDate }} · revision {{ r.revision }}</small></p>
          @if (r.intents.length) {
            <div class="table-scroll"><table>
              <caption class="sr-only">Upload intents for request {{ r.area }}</caption>
              <thead><tr><th scope="col">File</th><th scope="col">Bytes</th><th scope="col">Intent state</th><th scope="col">Transfer state</th><th scope="col">Action</th></tr></thead>
              <tbody>@for (i of r.intents; track i.id) {
                <tr><td>{{ i.fileName }}</td><td>{{ i.receivedByteCount }} / {{ i.declaredByteCount }}</td><td><audit-status [value]="i.state" /></td>
                  <td><audit-status [value]="i.transferState ?? '—'" /></td>
                  <td>@if (i.state === 'CHUNKING' && i.receivedByteCount === i.declaredByteCount) {
                      <button matButton="filled" (click)="complete(i.id, i.declaredSha256Hex)" [disabled]="cmd.busy()">Complete staged transfer</button>
                    } @else if (i.state === 'STAGED' || i.state === 'RECEIVED') { <a matButton="filled" [href]="'/api/pbc/uploads/' + i.id + '/download'">Download</a> }</td></tr>
              }</tbody>
            </table></div>
          } @else { <p>No upload intents are recorded for this request.</p> }
          <h3>Conversation timeline</h3>
          <ol>@for (t of r.timeline; track $index) { <li><strong>{{ t.label }}</strong> <small>{{ t.at.slice(0, 16).replace('T', ' ') }}</small><p>{{ t.body }}</p></li> }</ol>
          @if (r.canRequestMore) {
            <label>Request more files <textarea [(ngModel)]="more[r.id]" [name]="'more-' + r.id" maxlength="3000" required></textarea></label>
            <button matButton="filled" (click)="requestMore(r.id)" [disabled]="cmd.busy() || !(more[r.id] ?? '').trim()">Request more files</button>
          }
        </section>
      } @empty {
        <section class="panel"><h2>{{ w.serviceRoute }} — {{ w.periodStart }} to {{ w.periodEnd }}</h2><p>No PBC requests are recorded for this engagement.</p></section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class PbcInbox {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/pbc` : null), decodePbcInbox,
    'The engagement is not available in your current access scope.');
  readonly cmd = new CommandState(this.api);
  f = { description: '', owner: '', reviewer: '', due: new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10), format: '' };
  more: Record<string, string> = {};

  intents(w: ReturnType<typeof decodePbcInbox>): number { return w.requests.reduce((n, r) => n + r.intents.length, 0); }
  received(w: ReturnType<typeof decodePbcInbox>): number { return w.requests.reduce((n, r) => n + r.intents.filter((i) => i.state === 'RECEIVED').length, 0); }
  private reload = () => this.ws.reload();
  create(): void {
    const w = this.ws.data();
    const owner = this.f.owner || (w?.clientOwners.length === 1 ? w.clientOwners[0].id : '');
    const reviewer = this.f.reviewer || (w?.reviewers.length === 1 ? w.reviewers[0].id : '');
    if (!owner || !reviewer) { this.cmd.failed.set(true); this.cmd.message.set('Choose the client recipient and the reviewer.'); return; }
    void this.cmd.run(`/api/ui/engagements/${this.id()}/pbc`, { description: this.f.description, clientOwnerId: owner, reviewerId: reviewer, dueDate: this.f.due,
      requestedFormat: this.f.format || null }, 'Request opened and client email queued.', () => (this.f = { ...this.f, description: '', format: '' })).finally(this.reload);
  }
  requestMore(requestId: string): void {
    void this.cmd.run(`/api/ui/pbc-requests/${requestId}/more-files`, { body: this.more[requestId] }, 'Additional request recorded and email queued.',
      () => (this.more[requestId] = '')).finally(this.reload);
  }
  complete(intentId: string, sha: string): void {
    void this.cmd.run(`/api/ui/pbc-uploads/${intentId}/complete`, { declaredSha256: sha },
      'Staged bytes verified; a durable provider transfer is queued. The request is not received until that transfer completes.').finally(this.reload);
  }
}
