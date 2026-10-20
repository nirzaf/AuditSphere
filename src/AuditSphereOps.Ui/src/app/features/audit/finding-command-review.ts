import { Component, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { SessionService } from '../../core/session';
import { FindingCommandState } from './finding-command-state';
import { FindingPreview, FindingSnapshot } from './finding-command-contracts';

@Component({ selector: 'audit-finding-command-review', imports: [FormsModule, MatButtonModule], template: `
  @if (flow().preview(); as p) {
    <section class="panel" aria-label="Exact finding preview">
      <h2>Review finding amendment</h2><p>{{ p.summary }}</p>
      <p>{{ p.canonicalInput.action }} · finding {{ p.before.findingId }} · engagement {{ p.before.engagementId }} · client {{ p.before.clientId }}</p>
      <p>{{ p.before.findingType }} · {{ p.before.impactDescription }} · amount {{ p.before.monetaryAmount ?? 'Not quantified' }}</p>
      <div class="table-scroll"><table aria-label="Finding before and proposed after"><thead><tr><th>Field</th><th>Persisted before</th><th>Proposed after</th></tr></thead>
        <tbody>@for (row of rows(p); track row.label) { <tr><th scope="row">{{ row.label }}</th><td>{{ row.before }}</td><td>{{ row.after }}</td></tr> }</tbody></table></div>
      <p>Reviewed source <code>{{ p.reviewBasis }}</code> · exact request <code>{{ p.requestHash }}</code></p>
      <p>This records the current operator's amendment. It does not establish an independent review or audit conclusion.</p>
      @for (blocker of p.blockers; track blocker) { <p role="alert">{{ blocker }}</p> }
      <label><input type="checkbox" [ngModel]="flow().reviewed()" (ngModelChange)="flow().reviewed.set($event)" [disabled]="flow().busy() || !p.canProceed" /> I reviewed this exact finding amendment and its before and after effects.</label>
      <button matButton="filled" (click)="flow().execute()" [disabled]="flow().busy() || !p.canProceed || !flow().reviewed()">Confirm reviewed finding action</button>
    </section>
  }
  @if (flow().uncertain()) {
    <section class="panel" aria-label="Exact finding request recovery"><h2>Recover finding request</h2>
      <p role="alert">The finding command outcome is not confirmed. Matching history is not an actor-owned receipt. Do not repeat blindly.</p>
      @for (p of flow().pending(); track p.findingId + p.action) {
        <p>{{ p.action }} · finding {{ p.findingId }} · request {{ p.reference.requestId }} · <code>{{ p.reference.requestHash }}</code></p>
        <button matButton="outlined" (click)="flow().reconcile(p.action)" [disabled]="!flow().canReconcile(p.action)">Look up exact finding receipt</button>
        @if (flow().absent().includes(p.action)) { <p>No receipt was found at lookup time. A delayed command may still commit. Re-enter the exact original intent and prepare a fresh preview of this same request before explicitly submitting again.</p> }
      }
    </section>
  }
  @if (flow().receipt(); as r) {
    <section class="panel" aria-label="Verified finding receipt"><h2>Verified finding amendment receipt</h2>
      <p>{{ r.action }} · finding {{ r.findingId }} · amendment {{ r.amendment.id }}</p>
      <p>Actor {{ r.actorId }} · exact session {{ r.actorEpoch }} · {{ r.createdAt }}</p>
      <p>Request {{ r.requestId }} · <code>{{ r.requestHash }}</code></p>
      <div class="table-scroll"><table aria-label="Persisted finding amendment"><thead><tr><th>Field</th><th>Before</th><th>After</th></tr></thead><tbody>
        @for (row of amendmentRows(r.amendment.before, r.amendment.after); track row.label) { <tr><th scope="row">{{ row.label }}</th><td>{{ row.before }}</td><td>{{ row.after }}</td></tr> }
      </tbody></table></div>
    </section>
  }
  @if (flow().message()) { <p [attr.role]="flow().failed() || flow().uncertain() ? 'alert' : 'status'">{{ flow().message() }}</p> }
`, styles: ['td, code, p { overflow-wrap: anywhere; }'] })
export class FindingCommandReview {
  readonly flow = input.required<FindingCommandState>();
  private readonly session = inject(SessionService);
  rows(p: FindingPreview) {
    const f = p.canonicalInput, before = p.before;
    return [
      { label: 'Revision', before: before.revision, after: p.nextRevision },
      { label: 'Finding status', before: before.status, after: f.response ? f.response.corrected ? 'CORRECTED' : 'EVALUATED' : before.status },
      { label: 'Designation recorded by', before: before.letterDesignatedByUserId ?? 'None', after: f.action === 'RECORD_RESPONSE' ? before.letterDesignatedByUserId ?? 'None' : f.designation ? this.session.current()?.userId ?? 'Current operator' : 'None' },
      { label: 'Management response', before: before.managementResponse ?? 'Not recorded', after: f.response?.managementResponse ?? before.managementResponse ?? 'Not recorded' },
      { label: 'Corrected in audited period', before: before.corrected ? 'Yes' : 'No', after: (f.response?.corrected ?? before.corrected) ? 'Yes' : 'No' },
      { label: 'Management letter', before: before.letterDesignatedAt ? 'Designated' : 'Internal only', after: f.action === 'RECORD_RESPONSE' ? before.letterDesignatedAt ? 'Designated' : 'Internal only' : f.designation ? 'Designated by the executing operator at commit' : 'Internal only' },
      { label: 'Letter recommendation', before: before.letterRecommendation ?? 'None', after: f.action === 'RECORD_RESPONSE' ? before.letterRecommendation ?? 'None' : f.designation?.recommendation ?? 'None' },
    ];
  }
  amendmentRows(before: FindingSnapshot, after: FindingSnapshot) {
    return (['revision', 'managementResponse', 'corrected', 'status', 'letterRecommendation', 'letterDesignatedAt', 'letterDesignatedByUserId'] as const)
      .map(key => ({ label: key.replace(/([a-z])([A-Z])/g, '$1 $2'), before: String(before[key] ?? 'None'), after: String(after[key] ?? 'None') }));
  }
}
