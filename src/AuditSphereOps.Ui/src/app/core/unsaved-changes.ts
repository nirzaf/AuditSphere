import { Component, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface NavigationProtected {
  confirmNavigation(): boolean | Promise<boolean>;
}
export const unsavedChangesGuard: CanDeactivateFn<NavigationProtected> = (component) =>
  component.confirmNavigation();

@Component({
  selector: 'audit-unsaved-changes',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Unsubmitted confirmation edits</h2>
    <mat-dialog-content
      ><p>
        Keep editing, save a draft in this browser tab, or discard the edits before continuing.
      </p>
      <p>
        A tab draft expires after four hours and is not saved evidence. Recovery requires the same
        identity and current record revision, followed by fresh review. Closing this tab can remove
        it.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions
      ><button matButton (click)="dialog.close('keep')">Keep editing</button>
      <button matButton (click)="dialog.close('save')">Save draft and continue</button>
      <button matButton (click)="dialog.close('discard')">
        Discard edits and continue
      </button></mat-dialog-actions
    >
  `,
})
export class UnsavedChangesDialog {
  readonly dialog = inject(MatDialogRef<UnsavedChangesDialog>);
}
