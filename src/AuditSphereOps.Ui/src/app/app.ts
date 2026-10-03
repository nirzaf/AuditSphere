import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { NAVIGATION } from './navigation';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { GlobalSearch } from './features/search/search';
import { SessionService } from './core/session';
import { Drafts } from './core/drafts';
import { presentationBase, workspaceSignInHref } from './core/navigation';
@Component({
  selector: 'app-root',
  imports: [GlobalSearch, RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);
  private readonly currentRoute = toSignal(this.router.events.pipe(map(() => this.router.url)), { initialValue: this.router.url });
  readonly publicSetup = computed(() => this.currentRoute().split(/[?#]/)[0] === '/setup/microsoft365');
  readonly session = inject(SessionService);
  private readonly drafts = inject(Drafts);
  readonly nav = NAVIGATION;
  readonly signInHref = workspaceSignInHref(location.pathname, location.search, presentationBase());
  readonly signOutFailure = signal('');
  skipToMain(event: Event): void {
    event.preventDefault();
    const main = document.getElementById('main');
    main?.focus();
    main?.scrollIntoView({ block: 'start' });
  }
  async signOut(): Promise<void> {
    this.signOutFailure.set('');
    this.drafts.clearAll();
    try {
      await this.session.signOut();
    } catch {
      this.signOutFailure.set(
        'Sign-out could not be confirmed. Your workspace is cleared. Retry to confirm server sign-out.',
      );
    }
  }
  constructor() {
    void this.session.refresh();
    const timer = setInterval(() => void this.session.refresh(), 5000);
    const focus = () => void this.session.refresh();
    window.addEventListener('focus', focus);
    const visible = () => {
      if (document.visibilityState === 'visible') focus();
    };
    document.addEventListener('visibilitychange', visible);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      window.removeEventListener('focus', focus);
      document.removeEventListener('visibilitychange', visible);
    });
  }
}
