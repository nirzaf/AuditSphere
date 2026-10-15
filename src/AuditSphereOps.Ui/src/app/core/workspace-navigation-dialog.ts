import { Component, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { WorkspaceNavigation } from './workspace-navigation';

@Component({
  selector: 'audit-workspace-navigation-dialog',
  imports: [MatDialogModule, MatButtonModule, WorkspaceNavigation],
  template: `
    <h2 mat-dialog-title><span class="brand-mark" aria-hidden="true">AS</span> Workspace navigation</h2>
    <mat-dialog-actions align="end">
      <button matButton (click)="dialog.close()">Close navigation</button>
    </mat-dialog-actions>
    <mat-dialog-content><audit-workspace-navigation /></mat-dialog-content>
  `,
  styles: `
    :host { display: block; background: var(--as-shell-dark, #0f172a); color: var(--as-shell-text, #f8fafc); }
    h2[mat-dialog-title] { display: flex; align-items: center; gap: .75rem; color: var(--as-shell-text, #f8fafc); }
    .brand-mark { display: inline-grid; place-items: center; width: 2rem; height: 2rem; border-radius: .5rem;
      background: #1d4ed8; border: 1px solid #60a5fa; color: white; font-size: .75rem; font-weight: 800; }
    mat-dialog-actions { border-top: 1px solid var(--as-shell-border, #475569); }
    mat-dialog-content { background: var(--as-shell-dark, #0f172a); color: var(--as-shell-text, #f8fafc); padding-block-end: 1rem; }
    button { min-height: 44px; color: var(--as-shell-text, #f8fafc); }
    button:focus-visible { outline: 3px solid white; outline-offset: 2px; }
  `,
})
export class WorkspaceNavigationDialog {
  readonly dialog = inject(MatDialogRef<WorkspaceNavigationDialog>);
}
