import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { WorkpaperCommandState } from './workpaper-command-state';

@Component({ selector: 'audit-workpaper-command-review', imports: [FormsModule, MatDialogModule, MatButtonModule], template: `
  <h2 mat-dialog-title>Review exact workpaper action</h2>
  <mat-dialog-content>
    @if (flow.preview(); as p) {
      <section aria-label="Exact workpaper preview">
        <p>{{ p.summary }}</p><p>{{ p.canonicalInput.action }} · {{ p.before.index }} · {{ p.before.title }}</p>
        <dl><dt>Workpaper</dt><dd>{{ p.before.workpaperId }}</dd><dt>Client / engagement</dt><dd>{{ p.before.clientId }} / {{ p.before.engagementId }}</dd>
          <dt>Target revision / input / policy</dt><dd>{{ p.before.revision }} / {{ p.before.inputGeneration }} / {{ p.before.policyGeneration }}</dd>
          <dt>Owned draft / revision / acknowledged save</dt><dd>{{ p.before.draft?.id }} / {{ p.before.draft?.draftRevision }} / {{ p.before.draft?.lastSaveId }}</dd></dl>
        <div class="table-scroll"><table aria-label="Workpaper before and proposed after"><thead><tr><th>Field</th><th>Persisted before</th><th>Proposed after</th></tr></thead><tbody>
          <tr><th scope="row">Workpaper revision</th><td>{{ p.before.revision }}</td><td>{{ proposedRevision() }}</td></tr>
          <tr><th scope="row">Draft revision</th><td>{{ p.before.draft?.draftRevision }}</td><td>{{ p.before.draft?.draftRevision }}</td></tr>
          <tr><th scope="row">Status</th><td>{{ p.before.status }}</td><td>{{ p.canonicalInput.submit ? 'SUBMITTED_SNAPSHOT' : p.before.status }}</td></tr>
          <tr><th scope="row">Draft lifecycle</th><td>{{ p.before.draft?.lifecycle }}</td><td>{{ p.canonicalInput.submit ? 'CONSUMED' : 'DISCARDED' }}</td></tr>
          <tr><th scope="row">Work performed</th><td>{{ p.before.workPerformed ?? 'No frozen content' }}</td><td>{{ p.canonicalInput.submit?.workPerformed ?? p.before.workPerformed ?? 'No frozen content' }}</td></tr>
          <tr><th scope="row">Conclusion</th><td>{{ p.before.conclusion ?? 'No frozen conclusion' }}</td><td>{{ p.canonicalInput.submit?.conclusion ?? p.before.conclusion ?? 'No frozen conclusion' }}</td></tr>
        </tbody></table></div>
        <h3>Exact acknowledged draft retained in history</h3><p class="content">{{ p.before.draft?.workPerformed }}</p><p class="content">{{ p.before.draft?.conclusion }}</p>
        @if (p.canonicalInput.discard) { <p>Discard marks this saved draft revision discarded. Its original content remains retained; unsaved editor changes are not saved by this action.</p> }
        <p>Request {{ p.requestId }} · <code>{{ p.requestHash }}</code></p><p>Reviewed basis <code>{{ p.reviewBasis }}</code></p>
        @for (blocker of p.blockers; track blocker) { <p role="alert">{{ blocker }}</p> }
        <label><input type="checkbox" [ngModel]="flow.reviewed()" (ngModelChange)="flow.reviewed.set($event)" [disabled]="flow.busy() || !p.canProceed" /> I reviewed this exact workpaper, acknowledged draft and proposed effect.</label>
      </section>
    }
    @if (flow.message()) { <p [attr.role]="flow.failed() || flow.uncertain() ? 'alert' : 'status'">{{ flow.message() }}</p> }
  </mat-dialog-content>
  <mat-dialog-actions><button matButton="outlined" (click)="close()" [disabled]="flow.busy()">Stay on workpaper</button>
    <button matButton="filled" (click)="confirm()" [disabled]="flow.busy() || !flow.preview()?.canProceed || !flow.reviewed()">Confirm reviewed workpaper action</button>
  </mat-dialog-actions>
`, styles: ['p, dd, td, code { overflow-wrap: anywhere; } .content { white-space: pre-wrap; }'] })
export class WorkpaperCommandReview {
  readonly flow = inject<{ flow: WorkpaperCommandState }>(MAT_DIALOG_DATA).flow;
  readonly proposedRevision = computed(() => {
    const p = this.flow.preview();
    return p ? p.canonicalInput.submit ? (BigInt(p.before.revision) + 1n).toString() : p.before.revision : '';
  });
  private readonly dialog = inject(MatDialogRef<WorkpaperCommandReview, boolean>);
  async confirm(): Promise<void> {
    const receipt = await this.flow.execute();
    if (receipt) this.dialog.close(true);
    else if (!this.flow.preview()) this.dialog.close(false);
  }
  close(): void { this.flow.resetReview(); this.dialog.close(false); }
}
