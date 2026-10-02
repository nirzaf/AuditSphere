import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
interface Question {
  code: string;
  section: string;
  prompt: string;
  category: string;
  answerType: string;
  requiresEvidence: boolean;
  adverse: boolean;
  answer: string | null;
  evidence: string | null;
  revision: string;
  priorAnswer: string | null;
  answeredBy: string | null;
}
interface Clearance {
  id: string;
  area: string;
  specialist: string;
  status: string;
  evidence: string | null;
  conditions: string | null;
}
interface Checklist {
  clientId: string;
  generation: string;
  path: string;
  currentDecision: string | null;
  priorDecision: string | null;
  ready: boolean;
  canEdit: boolean;
  canReview: boolean;
  canDecide: boolean;
  canStartContinuance: boolean;
  questions: Question[];
  clearances: Clearance[];
  blockers: { kind: string; message: string; questionCode: string | null }[];
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function decodeChecklist(value: unknown): Checklist {
  if (!value || typeof value !== 'object') throw new Error('Invalid checklist');
  const v = value as Record<string, unknown>;
  if (
    typeof v['clientId'] !== 'string' ||
    !guid.test(v['clientId']) ||
    typeof v['generation'] !== 'string' ||
    !/^\d{1,19}$/.test(v['generation']) ||
    !['NEW_CLIENT', 'CONTINUANCE'].includes(v['path'] as string) ||
    typeof v['ready'] !== 'boolean' ||
    !['canEdit', 'canReview', 'canDecide', 'canStartContinuance'].every(
      (k) => typeof v[k] === 'boolean',
    )
  )
    throw new Error('Invalid context');
  for (const key of ['questions', 'clearances', 'blockers'])
    if (!Array.isArray(v[key]) || v[key].length > 1000) throw new Error('Invalid checklist bounds');
  for (const q of v['questions'] as Record<string, unknown>[]) {
    if (
      !q ||
      !['code', 'section', 'prompt', 'category', 'answerType', 'revision'].every(
        (k) => typeof q[k] === 'string',
      ) ||
      !/^\d{1,19}$/.test(q['revision'] as string) ||
      typeof q['requiresEvidence'] !== 'boolean' ||
      typeof q['adverse'] !== 'boolean' ||
      !['answer', 'evidence', 'priorAnswer', 'answeredBy'].every(
        (k) => q[k] === null || typeof q[k] === 'string',
      )
    )
      throw new Error('Invalid question');
  }
  for (const b of v['blockers'] as Record<string, unknown>[])
    if (
      !b ||
      typeof b['kind'] !== 'string' ||
      typeof b['message'] !== 'string' ||
      (b['questionCode'] !== null && typeof b['questionCode'] !== 'string')
    )
      throw new Error('Invalid blocker');
  for (const c of v['clearances'] as Record<string, unknown>[])
    if (
      !c ||
      typeof c['id'] !== 'string' ||
      !guid.test(c['id']) ||
      !['area', 'specialist', 'status'].every((k) => typeof c[k] === 'string') ||
      !['evidence', 'conditions'].every((k) => c[k] === null || typeof c[k] === 'string')
    )
      throw new Error('Invalid clearance');
  for (const k of ['currentDecision', 'priorDecision'])
    if (v[k] !== null && typeof v[k] !== 'string') throw new Error('Invalid decision');
  return v as unknown as Checklist;
}
@Component({
  selector: 'audit-acceptance-checklist',
  imports: [RouterLink, FormsModule, MatButtonModule, MatProgressBarModule],
  template: `
    <a routerLink="/app">Portfolio</a>
    <h1>Client acceptance checklist</h1>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading acceptance" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
      <button matButton (click)="load()">Retry</button>
    }
    @if (data(); as checklist) {
      <p>{{ checklist.path }} · Evaluation {{ checklist.generation }}</p>
      <p>
        Current decision: {{ checklist.currentDecision ?? 'Not recorded' }} · Prior decision:
        {{ checklist.priorDecision ?? 'None' }}
      </p>
      <p role="status">
        {{ checklist.ready ? 'Checklist ready for human decision' : 'Checklist blocked' }}
      </p>
      <ul>
        @for (blocker of checklist.blockers; track $index) {
          <li>{{ blocker.message }}</li>
        }
      </ul>
      @for (question of checklist.questions; track question.code) {
        <section>
          <h2>{{ question.code }} · {{ question.section }}</h2>
          <p>{{ question.prompt }}</p>
          @if (question.priorAnswer) {
            <p>Prior-cycle answer: {{ question.priorAnswer }}</p>
          }
          @if (question.answer) {
            <p>
              Recorded: {{ question.answer }} · {{ question.evidence }} · Revision
              {{ question.revision }} · {{ question.answeredBy }}
            </p>
          }
          @if (question.adverse) {
            <p>Adverse answer: specialist clearance is required.</p>
          }
          @if (checklist.canEdit) {
            <form (ngSubmit)="save(question)">
              <label
                >Answer for {{ question.code }}
                @if (question.answerType === 'BOOLEAN') {
                  <select
                    [name]="question.code"
                    [(ngModel)]="answers[question.code]"
                    [disabled]="busy()"
                  >
                    <option value="">Choose an answer</option>
                    <option value="Yes">Yes</option>
                    <option value="No">No</option>
                  </select>
                } @else {
                  <textarea
                    [name]="question.code"
                    [(ngModel)]="answers[question.code]"
                    maxlength="2000"
                    [disabled]="busy()"
                  ></textarea>
                }
              </label>
              <label
                >Evidence reference {{ question.requiresEvidence ? '(required)' : '(optional)'
                }}<input
                  [name]="question.code + '-evidence'"
                  [(ngModel)]="evidence[question.code]"
                  maxlength="500"
                  [required]="question.requiresEvidence"
                  [disabled]="busy()"
              /></label>
              <button
                matButton
                type="submit"
                [disabled]="busy() || uncertain() || !answers[question.code]"
              >
                Save answer for {{ question.code }}
              </button>
            </form>
          }
        </section>
      }
      <h2>Specialist reviews</h2>
      @for (clearance of checklist.clearances; track clearance.id) {
        <p>
          {{ clearance.area }} · {{ clearance.specialist }} · {{ clearance.status }} ·
          {{ clearance.evidence }} · {{ clearance.conditions }}
        </p>
        @if (checklist.canReview && clearance.status !== 'CLEARED') {
          <label
            >Review evidence for {{ clearance.area
            }}<input [(ngModel)]="reviewEvidence[clearance.id]" maxlength="500" [disabled]="busy()"
          /></label>
          <label
            >Review conditions<input
              [(ngModel)]="reviewConditions[clearance.id]"
              maxlength="2000"
              [disabled]="busy()"
          /></label>
          <button
            matButton
            [disabled]="busy() || uncertain()"
            (click)="recordReview(clearance, 'CLEARED')"
          >
            Clear {{ clearance.area }}
          </button>
          <button
            matButton
            [disabled]="busy() || uncertain()"
            (click)="recordReview(clearance, 'HOLD')"
          >
            Hold {{ clearance.area }}
          </button>
          <button
            matButton
            [disabled]="busy() || uncertain()"
            (click)="recordReview(clearance, 'CONDITIONS')"
          >
            Record conditions for {{ clearance.area }}
          </button>
        }
      } @empty {
        <p>No specialist reviews recorded.</p>
      }
      @if (checklist.canEdit) {
        <form (ngSubmit)="requestReview()">
          <h2>Request specialist review</h2>
          <label
            >Review area<input
              name="reviewArea"
              [(ngModel)]="reviewArea"
              minlength="2"
              maxlength="100"
              required
              [disabled]="busy()"
          /></label>
          <label
            >Specialist<input
              name="specialist"
              [(ngModel)]="specialist"
              minlength="2"
              maxlength="200"
              required
              [disabled]="busy()"
          /></label>
          <button matButton type="submit" [disabled]="busy() || uncertain()">Request review</button>
        </form>
      }
      @if (checklist.canDecide) {
        <form (ngSubmit)="decide()">
          <h2>Partner decision</h2>
          <p>
            This is your professional decision. Checklist readiness does not accept the client
            automatically.
          </p>
          <label
            >Service route<input
              name="decisionService"
              [(ngModel)]="decisionService"
              required
              maxlength="50"
              [disabled]="busy()"
          /></label>
          <label
            >Decision<select name="decision" [(ngModel)]="decision" [disabled]="busy()">
              <option value="Accepted">Accepted</option>
              <option value="AcceptedWithConditions">Accepted with conditions</option>
              <option value="Declined">Declined</option>
              <option value="Deferred">Deferred</option>
            </select></label
          >
          <label
            >Rationale<textarea
              name="rationale"
              [(ngModel)]="rationale"
              required
              maxlength="2000"
              [disabled]="busy()"
            ></textarea>
          </label>
          <label
            >Conditions<textarea
              name="conditions"
              [(ngModel)]="conditions"
              maxlength="2000"
              [disabled]="busy()"
            ></textarea>
          </label>
          <label
            ><input
              type="checkbox"
              name="decisionReviewed"
              [(ngModel)]="decisionReviewed"
              [disabled]="busy()"
            />
            I reviewed this evaluation and confirm my Partner decision.</label
          >
          <button matButton type="submit" [disabled]="busy() || uncertain() || !decisionReviewed">
            Record Partner decision
          </button>
        </form>
      }
      @if (checklist.canStartContinuance) {
        <section>
          <h2>Continuance review</h2>
          <p>
            Opening the next evaluation advances the generation and requires fresh delta answers.
          </p>
          <label
            ><input type="checkbox" [(ngModel)]="continuanceReviewed" [disabled]="busy()" /> Start
            the next review cycle for this client.</label
          >
          <button
            matButton
            [disabled]="busy() || uncertain() || !continuanceReviewed"
            (click)="startContinuance()"
          >
            Start continuance review
          </button>
        </section>
      }
      <p role="status">{{ commandStatus() }}</p>
      <a [href]="'/app/clients/' + checklist.clientId + '/assessment'"
        >Open full client assessment workbench</a
      >
    }
  `,
})
export class AcceptanceChecklist {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);
  private id = '';
  private request?: Subscription;
  readonly data = signal<Checklist | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  reviewArea = '';
  specialist = '';
  decisionService = '';
  decision = 'Accepted';
  rationale = '';
  conditions = '';
  decisionReviewed = false;
  continuanceReviewed = false;
  reviewEvidence: Record<string, string> = {};
  reviewConditions: Record<string, string> = {};
  answers: Record<string, string> = {};
  evidence: Record<string, string> = {};
  constructor() {
    const route = this.route.paramMap.subscribe((p) => {
      this.reset();
      this.id = p.get('id') ?? '';
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.reset();
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      route.unsubscribe();
      this.request?.unsubscribe();
    });
  }
  private reset(): void {
    this.reviewArea = '';
    this.specialist = '';
    this.decisionService = '';
    this.decision = 'Accepted';
    this.rationale = '';
    this.conditions = '';
    this.decisionReviewed = false;
    this.continuanceReviewed = false;
    this.reviewEvidence = {};
    this.reviewConditions = {};
    this.request?.unsubscribe();
    this.data.set(null);
    this.answers = {};
    this.evidence = {};
    this.commandStatus.set('');
    this.uncertain.set(false);
  }
  requestReview(): void {
    if (this.data()?.canEdit)
      this.command('reviews', { area: this.reviewArea, specialist: this.specialist });
  }
  recordReview(review: Clearance, status: string): void {
    if (this.data()?.canReview)
      this.command('reviews/' + review.id, {
        status,
        evidence: this.reviewEvidence[review.id] ?? '',
        conditions: this.reviewConditions[review.id] ?? '',
        expectedStatus: review.status,
      });
  }
  decide(): void {
    if (this.data()?.canDecide && this.decisionReviewed)
      this.command('decision', {
        serviceRoute: this.decisionService,
        decision: this.decision,
        rationale: this.rationale,
        conditions: this.conditions,
      });
  }
  startContinuance(): void {
    if (this.data()?.canStartContinuance && this.continuanceReviewed)
      this.command('continuance', {});
  }
  private command(path: string, body: object): void {
    const c = this.data();
    if (!c || this.busy() || this.uncertain()) return;
    const id = this.id;
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.commandStatus.set('Saving acceptance action…');
    this.http
      .post('/api/ui/clients/' + id + '/acceptance/' + path, { ...body, generation: c.generation })
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.decisionReviewed = false;
          this.continuanceReviewed = false;
          this.commandStatus.set('Acceptance action recorded.');
          this.load();
        },
        error: (failure) => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Action refused. Review current generation, checklist blockers, scope and required evidence.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Outcome unconfirmed. Refresh and review recorded state before another action.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  save(question: Question): void {
    const c = this.data();
    if (!c?.canEdit || this.busy() || this.uncertain()) return;
    const id = this.id;
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.commandStatus.set('Saving answer…');
    this.http
      .post('/api/ui/clients/' + id + '/acceptance/answers/' + encodeURIComponent(question.code), {
        answer: this.answers[question.code],
        evidence: this.evidence[question.code],
        generation: c.generation,
        revision: question.revision,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          delete this.answers[question.code];
          delete this.evidence[question.code];
          this.commandStatus.set('Answer recorded.');
          this.load();
        },
        error: (failure) => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Answer refused. Review the current generation, revision, evidence and access.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Outcome unconfirmed. Refresh and review recorded answers before resubmitting.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    if (!guid.test(this.id) || !this.session.current()?.staff) return;
    this.loading.set(true);
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>('/api/ui/clients/' + this.id + '/acceptance')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            const c = decodeChecklist(value);
            this.data.set(c);
            this.answers = Object.fromEntries(
              c.questions.map((q) => [q.code, this.answers[q.code] ?? q.answer ?? '']),
            );
            this.evidence = Object.fromEntries(
              c.questions.map((q) => [q.code, this.evidence[q.code] ?? q.evidence ?? '']),
            );
          } catch {
            this.error.set('Acceptance returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set('Acceptance unavailable. Check your client scope or retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
