import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, ElementRef, computed, effect, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Api, Resource } from '../../core/api';
import { arr, bool, date, DecodeError, Decoder, guid, instant, int, nat, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const REQUIREMENTS = ['MANDATORY', 'CONDITIONAL', 'OPTIONAL'] as const;
const STATES = ['COMPLETED', 'PENDING', 'BLOCKED', 'NOT_APPLICABLE'] as const;
const step = obj({
  taskId: guid, title: str(300), phase: str(40), ordinal: int, requirement: oneOf(...REQUIREMENTS), state: oneOf(...STATES),
  condition: nullable(str(300)), assigneeUserId: nullable(guid), assigneeName: nullable(str(200)), dueDate: nullable(date),
  applicabilityCondition: nullable(str(500)),
});
const progress = obj({
  engagementId: guid, clientId: guid, clientName: str(300), serviceRoute: str(100), periodStart: str(20), periodEnd: str(20),
  lifecycleRoute: bool, phase: str(80), stageLabel: nullable(str(120)), stageIndex: nullable(int),
  percentComplete: nullable(int), applicableRequired: nat, completedRequired: nat, pendingRequired: nat, blockedRequired: nat,
  notApplicable: nat, optionalApplicable: nat, optionalCompleted: nat, gateOpen: bool, gates: arr(text, 50),
  disclosure: nullable(text), responsibleName: nullable(str(200)), responsibleRole: nullable(str(80)), nextDueDate: nullable(date),
  nextAction: nullable(text), nextActionLink: nullable(str(300)), steps: arr(step, 500), evaluatedAtUtc: instant,
});
export type EngagementProgress = ReturnType<typeof progress>;
const list = obj({ items: arr(progress, 50), total: nat, page: nat, pageSize: nat });

/** Refuses figures that do not add up, a 100% that hides an open gate, or a next-action link that leaves the app. */
export function checkProgress(p: EngagementProgress): EngagementProgress {
  const percent = p.percentComplete;
  const ceiling = p.applicableRequired === 0 ? null : Math.floor(p.completedRequired * 100 / p.applicableRequired);
  if (p.completedRequired + p.pendingRequired + p.blockedRequired !== p.applicableRequired ||
      p.optionalCompleted > p.optionalApplicable || (percent === null) !== (ceiling === null) ||
      (percent !== null && (percent < 0 || percent > 100 || percent > ceiling! || (percent === 100 && p.gateOpen))) ||
      p.gateOpen !== p.gates.length > 0 || (p.nextActionLink !== null && !/^\/app\/[A-Za-z0-9/_?&=.-]*$/.test(p.nextActionLink)))
    throw new DecodeError('Unsupported engagement progress.');
  return p;
}
export const decodeEngagementProgress = (value: unknown, path: string) => checkProgress(progress(value, path));
export function decodeProgressList(value: unknown, path: string) {
  const result = list(value, path);
  result.items.forEach(checkProgress);
  if (result.pageSize < 1 || result.items.length > result.pageSize) throw new DecodeError('Unsupported progress page.');
  return result;
}

/** Splits a server-provided in-app link into a router path and its query parameters. */
export function appLink(link: string | null): { path: string; query: Record<string, string> } | null {
  if (!link || !link.startsWith('/app/')) return null;
  const [path, search = ''] = link.split('?', 2);
  return { path, query: Object.fromEntries(new URLSearchParams(search)) };
}

const REQUIREMENT_LABELS: Record<string, string> = { MANDATORY: 'Mandatory', CONDITIONAL: 'Conditional', OPTIONAL: 'Optional' };

/** Keep mounted trackers current without replacing focused/expanded content during a background read. */
function progressResource<T>(url: () => string | null, decoder: Decoder<T>, unavailable: string): Resource<T> {
  const document = inject(DOCUMENT);
  const session = inject(SessionService);
  const visible = signal(document.visibilityState !== 'hidden');
  const context = computed(() => {
    const target = url(), actor = session.current();
    return target && actor?.staff
      ? JSON.stringify([target, actor.userId, actor.firmId, actor.generation, session.invalidation()]) : null;
  });
  let requestedContext: string | null = null;
  const source = inject(Api).resource(() => {
    requestedContext = context();
    // A hidden page also cancels any in-flight read through the existing resource lifecycle.
    return visible() && requestedContext ? url() : null;
  }, decoder, unavailable);
  const data = linkedSignal<{ key: string | null; value: T | null; error: string }, T | null>({
    source: () => ({ key: context(), value: source.data(), error: source.error() }),
    computation: ({ key, value, error }, previous) => {
      if (!key || error) return null;
      if (value !== null && key === requestedContext) return value;
      // Never retain a result across records, pages, actors, firms or session generations.
      return previous?.source.key === key ? previous.value : null;
    },
  });
  const refresh = () => {
    if (visible() && document.visibilityState !== 'hidden' && context() &&
        context() === requestedContext && !source.loading()) source.reload();
  };
  effect(onCleanup => {
    if (!visible() || !context()) return;
    const timer = setInterval(refresh, 30_000);
    onCleanup(() => clearInterval(timer));
  });
  const returned = () => {
    const wasVisible = visible();
    visible.set(document.visibilityState !== 'hidden');
    // Hidden -> visible changes the resource URL; focus on an already visible page needs a reload.
    if (wasVisible && visible()) refresh();
  };
  document.addEventListener('visibilitychange', returned);
  document.defaultView?.addEventListener('focus', returned);
  inject(DestroyRef).onDestroy(() => {
    document.removeEventListener('visibilitychange', returned);
    document.defaultView?.removeEventListener('focus', returned);
  });
  return { data, error: source.error, loading: source.loading, reload: refresh };
}

/** Shared row presentation for client and portfolio progress summaries. */
@Component({
  selector: 'audit-progress-summary',
  imports: [RouterLink, MatProgressBarModule],
  styles: [`
    .progress-cell { display: grid; gap: .25rem; min-width: 8rem; }
    .progress-cell strong { font-variant-numeric: tabular-nums; }
  `],
  template: `
    <div class="table-scroll"><table>
      <caption>{{ caption() }}</caption>
      <thead><tr><th scope="col">Engagement</th><th scope="col">Phase</th><th scope="col">Progress</th><th scope="col" class="number">Blocked</th>
        <th scope="col">Next due</th><th scope="col">Next action</th><th scope="col">Responsible</th></tr></thead>
      <tbody>@for (p of items(); track p.engagementId) {
        <tr>
          <th scope="row"><a [routerLink]="['/app/engagements', p.engagementId]">{{ p.serviceRoute || 'Engagement' }}</a>
            <small>{{ p.clientName }}@if (p.periodStart && p.periodEnd) { · {{ p.periodStart }} to {{ p.periodEnd }} }</small></th>
          <td>{{ p.phase }}@if (p.gateOpen) { <small>{{ p.gates.length }} open gate(s)</small> }</td>
          <td><div class="progress-cell">
            <strong>{{ p.percentComplete === null ? 'No task plan' : p.percentComplete + '%' }}</strong>
            @if (p.percentComplete !== null) {
              <mat-progress-bar mode="determinate" [value]="p.percentComplete"
                [attr.aria-label]="'Progress for ' + (p.serviceRoute || 'engagement') + ' ' + p.periodEnd" />
              <small>{{ p.completedRequired }} of {{ p.applicableRequired }} required steps</small>
            }
          </div></td>
          <td class="number">{{ p.blockedRequired }}</td>
          <td class="nowrap">{{ p.nextDueDate ?? '—' }}</td>
          <td>@if (link(p.nextActionLink); as l) { <a [routerLink]="l.path" [queryParams]="l.query">{{ p.nextAction }}</a> }
            @else { {{ p.nextAction ?? 'Nothing outstanding' }} }</td>
          <td>{{ p.responsibleName ?? p.responsibleRole ?? 'Unassigned' }}</td>
        </tr>
      } @empty { <tr><td colspan="7">No engagements in your current scope.</td></tr> }</tbody>
    </table></div>
  `,
})
export class ProgressSummary {
  readonly items = input.required<EngagementProgress[]>();
  readonly caption = input('Engagement progress');
  readonly link = appLink;
}

/** The prominent tracker at the top of an engagement workspace. */
@Component({
  selector: 'audit-engagement-progress',
  imports: [RouterLink, MatButtonModule, MatProgressBarModule, ...SHARED],
  styles: [`
    .tracker-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: .5rem 1rem; }
    .tracker-head h2 { margin: 0; }
    .tracker-figure { display: flex; align-items: baseline; gap: .75rem; margin: .75rem 0 .5rem; }
    .tracker-figure strong { font-size: 2rem; line-height: 1; color: var(--as-ink); font-variant-numeric: tabular-nums; }
    .tracker-counts { display: flex; flex-wrap: wrap; gap: .375rem 1.25rem; margin: .75rem 0; padding: 0; list-style: none; }
    .tracker-counts strong { font-variant-numeric: tabular-nums; }
    .tracker-gates { margin-top: .75rem; padding: .75rem 1rem; border-inline-start: 4px solid var(--as-warning-accent);
      border-radius: var(--as-radius-sm); background: var(--as-surface-sunken); }
    .tracker-gates h3 { margin: 0 0 .25rem; font-size: .9375rem; }
    .tracker-gates ul { margin: 0; padding-inline-start: 1.25rem; }
    details { margin-top: 1rem; }
    summary { cursor: pointer; font-weight: 600; color: var(--as-ink); }
  `],
  template: `
    <section class="panel" aria-labelledby="engagement-progress-heading">
      <div class="tracker-head">
        <h2 id="engagement-progress-heading">Engagement progress</h2>
        @if (view.data(); as p) { <span class="hint">Evaluated {{ p.evaluatedAtUtc.slice(0, 16).replace('T', ' ') }} UTC</span> }
      </div>
      <audit-state [loading]="view.loading()" [error]="view.error()" label="engagement progress" />
      @if (view.data(); as p) {
        <div class="tracker-figure">
          <strong>{{ p.percentComplete === null ? '—' : p.percentComplete + '%' }}</strong>
          <span>{{ p.percentComplete === null ? 'No task plan adopted yet; adopt a template to track progress.'
            : p.gateOpen && p.completedRequired === p.applicableRequired
              ? 'All required steps are complete; open gates prevent completion.' : 'of applicable required steps complete' }}</span>
        </div>
        @if (p.percentComplete !== null) {
          <mat-progress-bar mode="determinate" [value]="p.percentComplete" aria-label="Engagement progress" />
        }
        <ul class="tracker-counts" aria-label="Required step counts">
          <li><strong>{{ p.completedRequired }}</strong> completed</li>
          <li><strong>{{ p.pendingRequired }}</strong> pending</li>
          <li><strong>{{ p.blockedRequired }}</strong> blocked</li>
          <li><strong>{{ p.notApplicable }}</strong> not applicable</li>
          <li>Optional: <strong>{{ p.optionalCompleted }}</strong> of {{ p.optionalApplicable }}</li>
        </ul>
        <dl class="facts">
          <dt>Current phase</dt><dd>{{ p.phase }}@if (p.stageLabel) { · stage {{ p.stageIndex }} of 11, {{ p.stageLabel }} }</dd>
          <dt>Responsible</dt><dd>{{ p.responsibleName ?? 'Unassigned' }}@if (p.responsibleRole) { · {{ p.responsibleRole }} }</dd>
          <dt>Next due</dt><dd>{{ p.nextDueDate ?? 'No due date recorded' }}</dd>
          <dt>Next action</dt><dd>@if (link(p.nextActionLink); as l) { <a [routerLink]="l.path" [queryParams]="l.query">{{ p.nextAction }}</a> }
            @else { {{ p.nextAction ?? 'Nothing outstanding' }} }</dd>
        </dl>
        @if (p.gates.length) {
          <div class="tracker-gates" role="note">
            <h3>{{ p.percentComplete === 99 ? 'Required steps are done; these gates keep progress below 100%' : 'Open gates' }}</h3>
            <ul>@for (g of p.gates; track $index) { <li>{{ g }}</li> }</ul>
          </div>
        }
        @if (p.disclosure) { <p class="hint">{{ p.disclosure }}</p> }
        <p class="hint">Progress counts required steps completed through their dependency and evidence checks; a completed task plan
          is not approval for release.</p>
        @if (p.steps.length) {
          <details>
            <summary>Steps ({{ p.steps.length }})</summary>
            <div class="table-scroll"><table>
              <caption>Engagement steps in plan order</caption>
              <thead><tr><th scope="col">Step</th><th scope="col">Requirement</th><th scope="col">State</th><th scope="col">Assignee</th>
                <th scope="col">Due</th></tr></thead>
              <tbody>@for (s of p.steps; track s.taskId) {
                <tr><th scope="row">{{ s.title }}@if (s.applicabilityCondition) { <small>Applies when: {{ s.applicabilityCondition }}</small> }</th>
                  <td>{{ requirementLabel(s.requirement) }}</td>
                  <td><audit-status [value]="s.state" />@if (s.condition) { <small>{{ s.condition }}</small> }</td>
                  <td>{{ s.assigneeName ?? 'Unassigned' }}</td><td class="nowrap">{{ s.dueDate ?? '—' }}</td></tr>
              }</tbody>
            </table></div>
          </details>
        }
      }
    </section>
  `,
})
export class EngagementProgressTracker {
  readonly engagementId = input.required<string>();
  readonly view = progressResource(() => this.engagementId() ? `/api/ui/engagements/${this.engagementId()}/progress` : null,
    decodeEngagementProgress, 'Engagement progress is not available in your current scope.');
  readonly link = appLink;
  requirementLabel(value: string): string { return REQUIREMENT_LABELS[value] ?? value; }
}

/** Progress for each engagement of one client, shown at the top of the client workspace. */
@Component({
  selector: 'audit-client-progress',
  imports: [MatButtonModule, ProgressSummary, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="client-progress-heading">
      <h2 #progressHeading id="client-progress-heading" tabindex="-1">Progress by engagement</h2>
      <p class="hint">Each engagement and reporting period is tracked separately from its own task plan and gates.</p>
      <audit-state [loading]="view.loading()" [error]="view.error()" label="client progress" />
      @if (view.data(); as v) {
        <audit-progress-summary [items]="v.items" caption="Engagement progress for this client" />
        <nav aria-label="Client engagement progress pages">
          <button matButton type="button" [disabled]="v.page === 0 || view.loading()" (click)="changePage(-1)">Previous progress page</button>
          <span>Page {{ v.page + 1 }} · {{ v.total }} {{ v.total === 1 ? 'engagement' : 'engagements' }} in scope</span>
          <button matButton type="button" [disabled]="(v.page + 1) * v.pageSize >= v.total || view.loading()" (click)="changePage(1)">Next progress page</button>
        </nav>
      }
    </section>
  `,
})
export class ClientProgress {
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('progressHeading');
  readonly clientId = input.required<string>();
  readonly page = linkedSignal({ source: this.clientId, computation: () => 0 });
  readonly view = progressResource(() => this.clientId()
    ? `/api/ui/clients/${this.clientId()}/progress` + (this.page() > 0 ? '?page=' + this.page() : '') : null,
    decodeProgressList, 'Client progress is not available in your current scope.');

  changePage(direction: number) {
    const current = this.view.data(), next = current ? current.page + direction : -1;
    if (!current || this.view.loading() || next < 0 || next * current.pageSize >= current.total) return;
    // The old page's buttons are removed while the next authorized page loads.
    this.heading().nativeElement.focus();
    this.page.set(next);
  }
}

/** A bounded, paged summary of engagement progress across the portfolio. */
@Component({
  selector: 'audit-portfolio-progress',
  imports: [MatButtonModule, ProgressSummary, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="portfolio-progress-heading">
      <h2 id="portfolio-progress-heading">Engagement progress</h2>
      <p class="hint">Newest periods first. Each engagement's progress counts its own required steps; nothing is averaged across engagements.</p>
      <audit-state [loading]="view.loading()" [error]="view.error()" label="portfolio progress" />
      @if (view.data(); as v) {
        <audit-progress-summary [items]="v.items" caption="Engagement progress across your portfolio" />
        <nav aria-label="Engagement progress pages">
          <button matButton type="button" [disabled]="v.page === 0 || view.loading()" (click)="page.set(v.page - 1)">Previous progress page</button>
          <span>Page {{ v.page + 1 }} · {{ v.total }} {{ v.total === 1 ? 'engagement' : 'engagements' }} in scope</span>
          <button matButton type="button" [disabled]="(v.page + 1) * v.pageSize >= v.total || view.loading()" (click)="page.set(v.page + 1)">Next progress page</button>
        </nav>
      }
    </section>
  `,
})
export class PortfolioProgress {
  readonly page = signal(0);
  readonly view = progressResource(() => '/api/ui/portfolio/progress' + (this.page() > 0 ? '?page=' + this.page() : ''),
    decodeProgressList, 'Portfolio progress is not available in your current scope.');
}
