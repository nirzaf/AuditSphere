import type { Injector } from '@angular/core';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { WorkspaceNavigationDialog } from './workspace-navigation-dialog';

/** Loaded only when compact-screen navigation is opened. */
export function openWorkspaceNavigation(injector: Injector): MatDialogRef<WorkspaceNavigationDialog> {
  return injector.get(MatDialog).open(WorkspaceNavigationDialog, {
    ariaLabel: 'Workspace navigation',
    autoFocus: 'first-tabbable',
    restoreFocus: true,
    width: '22rem',
    maxWidth: 'calc(100vw - 2rem)',
    maxHeight: 'calc(100dvh - 2rem)',
  });
}
