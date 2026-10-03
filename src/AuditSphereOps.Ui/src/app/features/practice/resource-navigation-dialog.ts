import { Component, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
@Component({
  imports: [MatDialogModule, MatButtonModule],
  template: `<h2 mat-dialog-title>Unsubmitted planning edits</h2>
    <mat-dialog-content
      ><p>
        Planning edits stay in memory. Leaving or refreshing this workspace discards them.
      </p></mat-dialog-content
    ><mat-dialog-actions
      ><button matButton (click)="dialog.close(false)">Keep editing</button
      ><button matButton (click)="dialog.close(true)">
        Discard edits and continue
      </button></mat-dialog-actions
    >`,
})
export class ResourceNavigationDialog {
  readonly dialog = inject(MatDialogRef<ResourceNavigationDialog>);
}
