import { Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NAVIGATION } from './navigation';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { GlobalSearch } from './features/search/search';
import { SessionService } from './core/session';
import { Drafts } from './core/drafts';
@Component({
  selector: 'app-root',
  imports: [GlobalSearch, RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly session = inject(SessionService);
  private readonly drafts = inject(Drafts);
  readonly nav = NAVIGATION;
  readonly signInHref = '/auth/sign-in?returnUrl=' + encodeURIComponent(
    location.pathname.startsWith('/ui/portal') || location.pathname.startsWith('/ui/app') ? location.pathname + location.search : '/ui/app');
  readonly signOutFailure = signal('');
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
