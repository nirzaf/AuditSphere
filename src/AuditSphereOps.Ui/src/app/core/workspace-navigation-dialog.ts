import { Component, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { WorkspaceNavigation } from './workspace-navigation';

@Component({
  selector: 'audit-workspace-navigation-dialog',
  imports: [MatDialogModule, MatButtonModule, WorkspaceNavigation],
  template: `
    <h2 mat-dialog-title>Workspace navigation</h2>
    <mat-dialog-actions align="end">
      <button matButton (click)="dialog.close()">Close navigation</button>
    </mat-dialog-actions>
    <mat-dialog-content><audit-workspace-navigation /></mat-dialog-content>
  `,
  styles: `
    mat-dialog-content { background: #edf3f8; padding-block-end: 1rem; }
    button { min-height: 44px; }
  `,
})
export class WorkspaceNavigationDialog {
  readonly dialog = inject(MatDialogRef<WorkspaceNavigationDialog>);
}
