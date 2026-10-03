import { Component, effect, inject, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, routeGuid } from '../../core/api';
import { guid, nullable, obj } from '../../core/decode';
import { SHARED } from '../../core/ui';

const decodeRoute = obj({ clientId: guid, decisionId: nullable(guid) });

/** Legacy /app/assessments/{client-or-decision-id} links resolve server-side to the client acceptance workspace. */
@Component({
  selector: 'audit-assessment-route',
  imports: [RouterLink, ...SHARED],
  template: `
    <audit-page-header title="Client acceptance" eyebrow="Acceptance and continuance" />
    @if (error()) {
      <section role="alert">
        <p>{{ error() }}</p>
        <a routerLink="/app">Back to portfolio</a>
      </section>
    } @else {
      <p role="status">Opening the client acceptance workspace…</p>
    }
  `,
})
export class AssessmentRoute {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly id = routeGuid();
  readonly error = signal('');
  constructor() {
    effect(() => {
      const id = this.id();
      this.error.set('');
      untracked(async () => {
        if (!id) return;
        try {
          const r = await this.api.get(`/api/ui/assessments/${id}`, decodeRoute);
          if (this.id() === id)
            await this.router.navigate(['/app/clients', r.clientId, 'assessment'], {
              replaceUrl: true,
              queryParams: r.decisionId ? { decisionId: r.decisionId } : {},
            });
        } catch (e) {
          if (this.id() !== id) return;
          this.error.set(
            (e as Error).message === 'Not available in your current scope.'
              ? 'The assessment is unavailable in your current scope.'
              : (e as Error).message,
          );
        }
      });
    });
  }
}
