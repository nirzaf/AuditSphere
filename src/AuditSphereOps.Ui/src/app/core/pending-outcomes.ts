import { Component, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SessionService } from './session';
import { pendingRequestReference } from './tab-drafts';

const prefix = 'auditsphere-tab-draft-v1:';
const segmentPattern = /^[a-zA-Z0-9:_-]{1,200}$/;
const linkPattern = /^\/app\/[a-zA-Z0-9:_/-]+$/;

interface PendingRoute {
  label: string;
  /** Route template under the presentation base. {n} substitutes the n-th entity path segment. */
  template: string;
}

/** Every persisted pending-fence scope with its owning staff workspace. A scope without an entry
 * still surfaces, but without a link: the banner never guesses a destination. */
export const PENDING_OUTCOME_ROUTES: Record<string, PendingRoute> = {
  'contact-request': { label: 'Client contact creation', template: '/app/clients/{0}/contacts/new' },
  'engagement-creation-request': { label: 'Engagement creation', template: '/app/clients/{0}/engagements/new' },
  'client-conversion-request': { label: 'Prospect conversion', template: '/app/practice/proposals/{0}/client-conversion' },
  'assessment-request': { label: 'Client acceptance review', template: '/app/clients/{0}/assessment' },
  'activation-request': { label: 'Engagement activation review', template: '/app/engagements/{0}/activation' },
  'engagement-budget-request': { label: 'Engagement budget preparation', template: '/app/engagements/{0}' },
  'engagement-budget-approval-request': { label: 'Engagement budget approval', template: '/app/engagements/{0}' },
  'engagement-staffing-request': { label: 'Engagement staffing change', template: '/app/engagements/{0}' },
  'journal-create-request': { label: 'Adjustment journal preparation', template: '/app/accounting/sources/{0}/journal-draft' },
  'journal-request': { label: 'Adjustment journal submission', template: '/app/accounting/journals/{0}' },
  'evidence-request': { label: 'Accounting evidence action', template: '/app/accounting/evidence/{0}/{1}/actions' },
  'valuation-request': { label: 'Currency valuation preparation', template: '/app/accounting/reconciliations/{1}/prepare/{0}' },
  'analytical-request': { label: 'Analytical review preparation', template: '/app/engagements/{0}/analysis/new' },
};

export interface PendingOutcome {
  label: string;
  /** Owning workspace deep link, or null when the scope has no verified destination. */
  link: string | null;
}

/** Shell-level view over this tab's persisted unknown command outcomes. Read-only: the banner
 * surfaces fences so a practitioner can verify each retained receipt in its owning workspace;
 * verification and clearing stay owned by the feature that created the fence. */
@Injectable({ providedIn: 'root' })
export class PendingOutcomes {
  private readonly session = inject(SessionService);
  readonly items = signal<PendingOutcome[]>([]);

  refresh(): void {
    const s = this.session.current();
    if (!s || !s.staff) {
      this.items.set([]);
      return;
    }
    const identity = `${prefix}${s.firmId}:${s.userId}:${s.generation}:`;
    const found: PendingOutcome[] = [];
    try {
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (!key?.startsWith(identity)) continue;
        const entity = key.slice(identity.length);
        let entry: unknown;
        try {
          entry = JSON.parse(sessionStorage.getItem(key) ?? 'null');
        } catch {
          continue;
        }
        if (
          !entry ||
          typeof entry !== 'object' ||
          (entry as Record<string, unknown>)['submissionPending'] !== true ||
          (entry as Record<string, unknown>)['entity'] !== entity
        )
          continue;
        if (!pendingRequestReference((entry as Record<string, unknown>)['value'])) continue;
        found.push(describe(entity));
      }
    } catch {
      /* storage unavailable: surface nothing rather than a wrong claim */
    }
    this.items.set(found);
  }
}

function describe(entity: string): PendingOutcome {
  const slash = entity.indexOf('/');
  const name = slash === -1 ? entity : entity.slice(0, slash);
  const segments = slash === -1 ? [] : entity.slice(slash + 1).split('/');
  const route = PENDING_OUTCOME_ROUTES[name];
  if (!route) return { label: 'Unverified command outcome', link: null };
  let link: string;
  try {
    link = route.template.replace(/\{(\d+)\}/g, (_, index) => {
      const value = segments[Number(index)];
      if (value === undefined || !segmentPattern.test(value)) throw new Error('Invalid fence segment');
      return value;
    });
  } catch {
    return { label: route.label, link: null };
  }
  return linkPattern.test(link) ? { label: route.label, link } : { label: route.label, link: null };
}

@Component({
  selector: 'audit-pending-outcomes',
  imports: [RouterLink],
  template: `
    @if (items().length > 0) {
      <section class="pending-outcomes" aria-label="Unverified command outcomes">
        <h2>Unverified command outcomes</h2>
        <p>
          This tab retains {{ items().length }} command outcome{{ items().length === 1 ? '' : 's' }}
          whose result was never confirmed. Verification is manual in each owning workspace; nothing
          is retried automatically.
        </p>
        <ul>
          @for (item of items(); track item.label + (item.link ?? '')) {
            <li>
              @if (item.link) {
                <a [routerLink]="item.link">{{ item.label }} — open the workspace and verify the retained receipt.</a>
              } @else {
                <span>{{ item.label }} — open its workspace from the navigation menu and verify the retained receipt.</span>
              }
            </li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .pending-outcomes {
      margin: 0 1rem 0.75rem;
      padding: 0.75rem 1rem;
      border: 1px solid #b36b00;
      border-radius: 4px;
      background: #fff7e6;
      color: #4a3000;
    }
    .pending-outcomes h2 { font-size: 0.9rem; margin: 0 0 0.25rem; }
    .pending-outcomes p { margin: 0 0 0.5rem; font-size: 0.875rem; }
    .pending-outcomes ul { margin: 0; padding-inline-start: 1.25rem; font-size: 0.875rem; }
  `,
})
export class PendingOutcomesBanner {
  private readonly outcomes = inject(PendingOutcomes);
  readonly items = this.outcomes.items;
  constructor() {
    const router = inject(Router);
    const destroy = inject(DestroyRef);
    this.outcomes.refresh();
    router.events
      .pipe(takeUntilDestroyed(destroy))
      .subscribe((event) => {
        if (event instanceof NavigationEnd) this.outcomes.refresh();
      });
    const timer = setInterval(() => this.outcomes.refresh(), 5000);
    destroy.onDestroy(() => clearInterval(timer));
  }
}
