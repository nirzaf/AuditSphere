import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { NavigationEnd, NavigationSkipped, NavigationSkippedCode, Router, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { map } from 'rxjs';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { GlobalSearch } from './features/search/search';
import { PendingOutcomesBanner } from './core/pending-outcomes';
import { SessionService } from './core/session';
import { Drafts } from './core/drafts';
import { TabDrafts } from './core/tab-drafts';
import { presentationBase, workspaceSignInHref } from './core/navigation';
import { WorkspaceNavigation } from './core/workspace-navigation';
import type { WorkspaceNavigationDialog } from './core/workspace-navigation-dialog';
@Component({
  selector: 'app-root',
  imports: [GlobalSearch, WorkspaceNavigation, RouterOutlet, MatToolbarModule, MatButtonModule, PendingOutcomesBanner],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly dialogs = inject(MatDialog);
  readonly compact = toSignal(inject(BreakpointObserver).observe('(max-width: 700px)').pipe(map(x => x.matches)), { initialValue: false });
  private readonly currentRoute = toSignal(this.router.events.pipe(map(() => this.router.url)), { initialValue: this.router.url });
  readonly publicSetup = computed(() => this.currentRoute().split(/[?#]/)[0] === '/setup/microsoft365');
  readonly session = inject(SessionService);
  private readonly drafts = inject(Drafts);
  private readonly tabDrafts = inject(TabDrafts);
  private readonly navigationDialog = signal<MatDialogRef<WorkspaceNavigationDialog> | null>(null);
  readonly navigationOpen = computed(() => this.navigationDialog() !== null);
  readonly navigationLoading = signal(false);
  readonly navigationFailure = signal('');
  private navigationOwner = '';
  private destroyed = false;
  readonly signInHref = workspaceSignInHref(location.pathname, location.search, presentationBase());
  readonly signOutFailure = signal('');
  private owner(): string {
    const s = this.session.current();
    return s ? `${s.firmId}:${s.userId}:${s.generation}:${s.staff}:${this.session.invalidation()}` : '';
  }
  async openNavigation(): Promise<void> {
    if (!this.compact() || !this.session.current() || this.publicSetup() || this.navigationOpen() || this.navigationLoading()) return;
    const owner = this.owner();
    const trigger = document.activeElement;
    this.navigationLoading.set(true);
    this.navigationFailure.set('');
    try {
      const { WorkspaceNavigationDialog } = await import('./core/workspace-navigation-dialog');
      if (this.destroyed || owner !== this.owner() || !this.compact() || this.publicSetup()) return;
      this.navigationOwner = owner;
      const ref = this.dialogs.open(WorkspaceNavigationDialog, {
        ariaLabel: 'Workspace navigation', autoFocus: 'first-tabbable', restoreFocus: true,
        width: '22rem', maxWidth: 'calc(100vw - 2rem)', maxHeight: 'calc(100dvh - 2rem)',
      });
      this.navigationDialog.set(ref);
      ref.afterClosed().pipe(takeUntilDestroyed(this.destroy)).subscribe(reason => {
        if (this.navigationDialog() !== ref) return;
        this.navigationDialog.set(null);
        if (reason === 'navigated' || reason === 'layout' || reason === 'identity') document.getElementById('main')?.focus();
        else if (this.compact() && this.owner() === owner && !this.publicSetup()) document.querySelector<HTMLButtonElement>('.navigation-toggle')?.focus();
      });
    } catch {
      if (!this.destroyed && owner === this.owner()) this.navigationFailure.set('Navigation could not load. Retry to open the menu.');
    } finally {
      if (!this.destroyed) {
        this.navigationLoading.set(false);
        // A resize may hide the trigger before the lazy chunk arrives. Do not leave focus
        // on a hidden control, or take it away from a field the user has since reached.
        if (owner === this.owner() && !this.compact() && !this.navigationOpen() &&
          (document.activeElement === trigger || document.activeElement === document.body))
          document.getElementById('main')?.focus();
      }
    }
  }
  skipToMain(event: Event): void {
    event.preventDefault();
    const main = document.getElementById('main');
    main?.focus();
    main?.scrollIntoView({ block: 'start' });
  }
  async signOut(): Promise<void> {
    this.signOutFailure.set('');
    this.drafts.clearAll();
    this.tabDrafts.clearAll();
    try {
      await this.session.signOut();
    } catch {
      this.signOutFailure.set(
        'Sign-out could not be confirmed. Your workspace is cleared. Retry to confirm server sign-out.',
      );
    }
  }
  constructor() {
    effect(() => {
      const ref = this.navigationDialog();
      if (ref && this.owner() !== this.navigationOwner) ref.close('identity');
      else if (ref && (!this.compact() || this.publicSetup())) ref.close('layout');
    });
    this.router.events.pipe(takeUntilDestroyed(this.destroy)).subscribe(event => {
      if (event instanceof NavigationEnd || (event instanceof NavigationSkipped && event.code === NavigationSkippedCode.IgnoredSameUrlNavigation))
        this.navigationDialog()?.close('navigated');
    });
    void this.session.refresh();
    const timer = setInterval(() => void this.session.refresh(), 5000);
    const focus = () => void this.session.refresh();
    window.addEventListener('focus', focus);
    const visible = () => {
      if (document.visibilityState === 'visible') focus();
    };
    document.addEventListener('visibilitychange', visible);
    this.destroy.onDestroy(() => {
      this.destroyed = true;
      this.navigationDialog()?.close();
      clearInterval(timer);
      window.removeEventListener('focus', focus);
      document.removeEventListener('visibilitychange', visible);
    });
  }
}
