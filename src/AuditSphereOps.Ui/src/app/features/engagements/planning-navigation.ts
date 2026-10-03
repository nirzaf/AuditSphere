import { Component, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import type { EngagementDetail } from './engagement';

export const engagementNavigationGuard: CanDeactivateFn<EngagementDetail> = (
  component,
  _route,
  _state,
  next,
) => component.confirmNavigation(next.url);

@Component({
  selector: 'audit-planning-navigation',
  imports: [MatDialogModule, MatButtonModule],
  template: ` <h2 mat-dialog-title>Unsubmitted planning changes</h2>
    <mat-dialog-content>
      <p>Keep editing, save editable budget fields in this tab, or discard edits before leaving.</p>
      <p>
        A tab draft expires after four hours. Recovery requires the same identity and budget
        revision. Review confirmations, staffing choices and approval are never saved in a budget
        draft.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions>
      <button matButton (click)="dialog.close('keep')">Keep editing</button>
      <button matButton (click)="dialog.close('save')">Save budget draft and continue</button>
      <button matButton (click)="dialog.close('discard')">Discard edits and continue</button>
    </mat-dialog-actions>`,
})
export class PlanningNavigationDialog {
  readonly dialog = inject(MatDialogRef<PlanningNavigationDialog>);
}
