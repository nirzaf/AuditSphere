import { Component, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

@Component({
  imports: [MatDialogModule, MatButtonModule],
  template: `<h2 mat-dialog-title>Unsubmitted assessment edits</h2>
    <mat-dialog-content
      ><p>
        Answers, evidence and professional notes stay in memory until submitted. Leaving or
        refreshing this evaluation discards these edits.
      </p></mat-dialog-content
    >
    <mat-dialog-actions
      ><button matButton (click)="dialog.close(false)">Keep editing</button>
      <button matButton (click)="dialog.close(true)">
        Discard edits and continue
      </button></mat-dialog-actions
    >`,
})
export class AssessmentNavigationDialog {
  readonly dialog = inject(MatDialogRef<AssessmentNavigationDialog>);
}
