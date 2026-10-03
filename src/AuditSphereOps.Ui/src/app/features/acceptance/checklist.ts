import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { combineLatest, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import {
  arr,
  bool,
  decode,
  guid as decodeGuid,
  instant,
  nat,
  nullable,
  obj,
  str,
} from '../../core/decode';
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
const exactCounter = (v: unknown, minimum: bigint): v is string =>
  typeof v === 'string' &&
  /^(0|[1-9]\d{0,18})$/.test(v) &&
  BigInt(v) >= minimum &&
  BigInt(v) <= 9223372036854775807n;
const bounded = (v: unknown, max: number): v is string => typeof v === 'string' && v.length <= max;
const decisions = ['Pending', 'Accepted', 'AcceptedWithConditions', 'Declined', 'Deferred'];
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function decodeChecklist(value: unknown): Checklist {
  if (!value || typeof value !== 'object') throw new Error('Invalid checklist');
  const v = value as Record<string, unknown>;
  if (
    typeof v['clientId'] !== 'string' ||
    !guid.test(v['clientId']) ||
    typeof v['generation'] !== 'string' ||
    !exactCounter(v['generation'], 1n) ||
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
      !exactCounter(q['revision'], 0n) ||
      !['BOOLEAN', 'TEXT'].includes(q['answerType'] as string) ||
      !bounded(q['code'], 100) ||
      !bounded(q['section'], 300) ||
      !bounded(q['prompt'], 20000) ||
      !bounded(q['category'], 300) ||
      typeof q['requiresEvidence'] !== 'boolean' ||
      typeof q['adverse'] !== 'boolean' ||
      !['answer', 'evidence', 'priorAnswer', 'answeredBy'].every(
        (k) =>
          q[k] === null || bounded(q[k], k === 'evidence' ? 500 : k === 'answeredBy' ? 300 : 2000),
      )
    )
      throw new Error('Invalid question');
  }
  for (const b of v['blockers'] as Record<string, unknown>[])
    if (
      !b ||
      !bounded(b['kind'], 100) ||
      !bounded(b['message'], 20000) ||
      (b['questionCode'] !== null && !bounded(b['questionCode'], 100))
    )
      throw new Error('Invalid blocker');
  for (const c of v['clearances'] as Record<string, unknown>[])
    if (
      !c ||
      typeof c['id'] !== 'string' ||
      !guid.test(c['id']) ||
      !['area', 'specialist', 'status'].every((k) => typeof c[k] === 'string') ||
      !['PENDING', 'CLEARED', 'HOLD', 'CONDITIONS'].includes(c['status'] as string) ||
      !bounded(c['area'], 100) ||
      !bounded(c['specialist'], 200) ||
      !['evidence', 'conditions'].every(
        (k) => c[k] === null || bounded(c[k], k === 'evidence' ? 500 : 2000),
      )
    )
      throw new Error('Invalid clearance');
  for (const k of ['currentDecision', 'priorDecision'])
    if (v[k] !== null && !decisions.includes(v[k] as string)) throw new Error('Invalid decision');
  if (
    new Set((v['questions'] as Question[]).map((q) => q.code.toUpperCase())).size !==
      (v['questions'] as Question[]).length ||
    new Set((v['clearances'] as Clearance[]).map((c) => c.id.toLowerCase())).size !==
      (v['clearances'] as Clearance[]).length
  )
    throw new Error('Duplicate assessment identity');
  return v as unknown as Checklist;
}
const decodeAssessmentMetadata = obj({
  client: obj({
    id: decodeGuid,
    legalName: str(300),
    registrationNumber: nullable(str(200)),
    status: str(100),
  }),
  selectedDecision: nullable(
    obj({
      id: decodeGuid,
      engagementId: nullable(decodeGuid),
      generation: str(19),
      decision: str(40),
      serviceRoute: str(100),
      rationale: str(20000),
      conditions: nullable(str(20000)),
      path: str(40),
      priorDecisionId: nullable(decodeGuid),
      decidedBy: nullable(str(300)),
      decidedAt: nullable(instant),
    }),
  ),
  historical: bool,
  repository: nullable(
    obj({ logicalKey: str(500), state: str(100), lastVerifiedAt: nullable(instant) }),
  ),
  answered: nat,
  total: nat,
  clearedReviews: nat,
  totalReviews: nat,
  sections: arr(obj({ section: str(300), answered: nat, total: nat }), 1000),
});
export function decodeAssessment(value: unknown) {
  const metadata = decode(decodeAssessmentMetadata, value);
  const checklist = decodeChecklist((value as Record<string, unknown>)['checklist']);
  if (
    metadata.selectedDecision &&
    (!exactCounter(metadata.selectedDecision.generation, 1n) ||
      !decisions.includes(metadata.selectedDecision.decision))
  )
    throw new Error('Invalid recorded decision');
  if (
    metadata.client.id !== checklist.clientId ||
    metadata.answered > metadata.total ||
    metadata.clearedReviews > metadata.totalReviews ||
    metadata.totalReviews !== checklist.clearances.length ||
    metadata.clearedReviews !== checklist.clearances.filter((c) => c.status === 'CLEARED').length ||
    metadata.answered !== checklist.questions.filter((q) => !!q.answer?.trim()).length ||
    new Set(metadata.sections.map((s) => s.section)).size !== metadata.sections.length ||
    metadata.total !== checklist.questions.length ||
    metadata.sections.some((s) => s.answered > s.total) ||
    metadata.sections.reduce((n, s) => n + s.total, 0) !== metadata.total ||
    metadata.sections.reduce((n, s) => n + s.answered, 0) !== metadata.answered ||
    metadata.historical !==
      !!(metadata.selectedDecision && metadata.selectedDecision.generation !== checklist.generation)
  )
    throw new Error('Invalid assessment context');
  if (
    (metadata.historical || metadata.selectedDecision?.engagementId) &&
    (checklist.canEdit ||
      checklist.canReview ||
      checklist.canDecide ||
      checklist.canStartContinuance)
  )
    throw new Error('Historical assessment is read only');
  return { ...metadata, checklist };
}
type Assessment = ReturnType<typeof decodeAssessment>;
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
      @if (assessment(); as workspace) {
        <section aria-label="Client assessment profile">
          <h2>{{ workspace.client.legalName }}</h2>
          <p>
            Registration: {{ workspace.client.registrationNumber ?? 'Not recorded' }} ·
            Relationship: {{ workspace.client.status }}
          </p>
        </section>
        @if (workspace.selectedDecision; as selected) {
          <section
            [attr.aria-label]="
              selected.decision === 'Pending' ? 'Decision status' : 'Recorded professional decision'
            "
          >
            <h2>
              {{ selected.decision === 'Pending' ? 'Decision pending' : 'Recorded decision' }}
            </h2>
            <p>
              {{ selected.decision }} · Evaluation {{ selected.generation }} ·
              {{ selected.serviceRoute }}
            </p>
            <p>
              Decision path: {{ selected.path }} · Prior decision:
              {{ selected.priorDecisionId ?? 'None' }}
            </p>
            <p>{{ selected.rationale }}</p>
            @if (selected.conditions) {
              <p>Conditions: {{ selected.conditions }}</p>
            }
            <p>
              Recorded by {{ selected.decidedBy ?? 'Not recorded' }} ·
              {{ selected.decidedAt ?? 'Not recorded' }}
            </p>
          </section>
        }
        @if (workspace.historical || workspace.selectedDecision?.engagementId) {
          <p role="status">
            {{
              workspace.historical
                ? 'You are viewing a historical professional decision.'
                : 'You are viewing an engagement-specific professional decision.'
            }}
            The questionnaire below describes the current client evaluation; this view is read-only.
          </p>
          <a [routerLink]="['/app/clients', checklist.clientId, 'assessment']"
            >Open current evaluation</a
          >
        }
        <section aria-label="Client workspace status">
          <h2>Client workspace</h2>
          @if (workspace.repository; as repository) {
            <p>{{ repository.logicalKey }} · {{ repository.state }}</p>
            <p>Last verification: {{ repository.lastVerifiedAt ?? 'Not verified' }}</p>
            @if (repository.state !== 'READY') {
              <p role="status">
                Workspace access is unavailable until acceptance, configuration and verification
                gates are satisfied. Ask an administrator to review workspace status.
              </p>
            }
          } @else {
            <p>No client workspace has been recorded.</p>
          }
          <p>Document access remains application-mediated and separately authorized.</p>
        </section>
        <section aria-label="Evaluation progress">
          <h2>Evaluation progress</h2>
          <p>
            {{ workspace.answered }} of {{ workspace.total }} questions answered ·
            {{ workspace.clearedReviews }} of {{ workspace.totalReviews }} specialist reviews
            cleared
          </p>
          @if (workspace.total > 0) {
            <mat-progress-bar
              mode="determinate"
              [value]="(100 * workspace.answered) / workspace.total"
              aria-label="Questions answered"
            />
          }
          @for (section of workspace.sections; track section.section) {
            <p>{{ section.section }}: {{ section.answered }} of {{ section.total }}</p>
          }
        </section>
      }
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
              (ngModelChange)="decisionReviewed = false"
              required
              maxlength="50"
              [disabled]="busy()"
          /></label>
          <label for="assessment-professional-decision">Decision</label>
          <select
            id="assessment-professional-decision"
            name="decision"
            [(ngModel)]="decision"
            (ngModelChange)="decisionReviewed = false"
            [disabled]="busy()"
          >
            <option value="Accepted">Accepted</option>
            <option value="AcceptedWithConditions">Accepted with conditions</option>
            <option value="Declined">Declined</option>
            <option value="Deferred">Deferred</option>
          </select>
          <label
            >Rationale<textarea
              name="rationale"
              [(ngModel)]="rationale"
              (ngModelChange)="decisionReviewed = false"
              required
              maxlength="2000"
              [disabled]="busy()"
            ></textarea>
          </label>
          <label
            >Conditions<textarea
              name="conditions"
              [(ngModel)]="conditions"
              (ngModelChange)="decisionReviewed = false"
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
      <a [routerLink]="['/app/clients', checklist.clientId]">View client profile</a>
    }
  `,
})
export class AcceptanceChecklist {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);
  private id = '';
  private decisionId: string | null = null;
  readonly assessment = signal<Assessment | null>(null);
  private request?: Subscription;
  readonly data = signal<Checklist | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  reviewArea = '';
  specialist = '';
  decisionService = 'FinancialStatementAudit';
  decision = 'Accepted';
  rationale = '';
  conditions = '';
  private decisionReviewBasis = '';
  private continuanceReviewBasis = '';
  private decisionBasis(): string {
    return JSON.stringify([
      this.id,
      this.decisionId,
      this.session.invalidation(),
      this.data(),
      this.decisionService,
      this.decision,
      this.rationale,
      this.conditions,
    ]);
  }
  private continuanceBasis(): string {
    return JSON.stringify([this.id, this.decisionId, this.session.invalidation(), this.data()]);
  }
  get decisionReviewed(): boolean {
    return !!this.decisionReviewBasis && this.decisionReviewBasis === this.decisionBasis();
  }
  set decisionReviewed(value: boolean) {
    this.decisionReviewBasis = value ? this.decisionBasis() : '';
  }
  get continuanceReviewed(): boolean {
    return !!this.continuanceReviewBasis && this.continuanceReviewBasis === this.continuanceBasis();
  }
  set continuanceReviewed(value: boolean) {
    this.continuanceReviewBasis = value ? this.continuanceBasis() : '';
  }
  reviewEvidence: Record<string, string> = {};
  reviewConditions: Record<string, string> = {};
  answers: Record<string, string> = {};
  evidence: Record<string, string> = {};
  constructor() {
    const route = combineLatest([this.route.paramMap, this.route.queryParamMap]).subscribe(
      ([p, query]) => {
        this.reset();
        this.id = p.get('id') ?? '';
        this.decisionId = query.get('decisionId');
        this.load();
      },
    );
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
    this.decisionService = 'FinancialStatementAudit';
    this.decision = 'Accepted';
    this.rationale = '';
    this.conditions = '';
    this.decisionReviewed = false;
    this.continuanceReviewed = false;
    this.reviewEvidence = {};
    this.reviewConditions = {};
    this.request?.unsubscribe();
    this.data.set(null);
    this.assessment.set(null);
    this.answers = {};
    this.evidence = {};
    this.commandStatus.set('');
    this.uncertain.set(false);
    this.busy.set(false);
    this.loading.set(false);
    this.error.set('');
  }
  private clearEdits(): void {
    this.assessment.set(null);
    this.data.set(null);
    this.answers = {};
    this.evidence = {};
    this.reviewEvidence = {};
    this.reviewConditions = {};
    this.reviewArea = '';
    this.specialist = '';
    this.decisionService = 'FinancialStatementAudit';
    this.decision = 'Accepted';
    this.rationale = '';
    this.conditions = '';
    this.decisionReviewed = false;
    this.continuanceReviewed = false;
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
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.busy.set(false);
          this.decisionReviewed = false;
          this.continuanceReviewed = false;
          this.commandStatus.set('Acceptance action recorded.');
          this.load();
        },
        error: (failure) => {
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.busy.set(false);
          if (path === 'decision' && failure.status === 409) {
            this.decisionService = 'FinancialStatementAudit';
            this.decision = 'Accepted';
            this.rationale = '';
            this.conditions = '';
            this.decisionReviewed = false;
            this.commandStatus.set(
              'The client state changed since this decision was prepared. Review the current state and record the decision again.',
            );
          } else if (path === 'decision' && failure.status === 403) {
            this.commandStatus.set(
              'Access is unavailable for this decision. Sign in with an authorized Partner identity for the current firm.',
            );
          } else if (failure.status >= 400 && failure.status < 500)
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
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.busy.set(false);
          delete this.answers[question.code];
          delete this.evidence[question.code];
          this.commandStatus.set('Answer recorded.');
          this.load();
        },
        error: (failure) => {
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.busy.set(false);
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
    this.assessment.set(null);
    this.error.set('');
    this.decisionReviewed = false;
    this.continuanceReviewed = false;
    if (
      !guid.test(this.id) ||
      (this.decisionId !== null && !guid.test(this.decisionId)) ||
      !this.session.current()?.staff
    )
      return;
    this.loading.set(true);
    const generation = this.session.invalidation();
    const id = this.id;
    const decisionId = this.decisionId;
    this.request = this.http
      .get<unknown>(
        '/api/ui/clients/' +
          id +
          '/assessment' +
          (decisionId ? '?decisionId=' + encodeURIComponent(decisionId) : ''),
      )
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (
            id !== this.id ||
            decisionId !== this.decisionId ||
            generation !== this.session.invalidation()
          )
            return;
          try {
            const workspace = decodeAssessment(value);
            const c = workspace.checklist;
            if (c.clientId !== id || (decisionId && workspace.selectedDecision?.id !== decisionId))
              throw new Error('Wrong assessment identity');
            this.assessment.set(workspace);
            this.data.set(c);
            this.answers = Object.fromEntries(
              c.questions.map((q) => [q.code, this.answers[q.code] ?? q.answer ?? '']),
            );
            this.evidence = Object.fromEntries(
              c.questions.map((q) => [q.code, this.evidence[q.code] ?? q.evidence ?? '']),
            );
          } catch {
            this.clearEdits();
            this.error.set('Acceptance returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (
            id !== this.id ||
            decisionId !== this.decisionId ||
            generation !== this.session.invalidation()
          )
            return;
          this.loading.set(false);
          this.clearEdits();
          this.error.set('Acceptance unavailable. Check your client scope or retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
