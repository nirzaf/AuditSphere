import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormField, disabled, form, validate } from '@angular/forms/signals';
import { MatDialog } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { combineLatest, firstValueFrom, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';

import { Api } from '../../core/api';
import { NavigationProtected } from '../../core/unsaved-changes';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { AssessmentCommandEditor } from './assessment-command-editor';
import { AssessmentNavigationDialog } from './assessment-navigation-dialog';
import {
  AssessmentCommandFields,
  AssessmentPreview,
  AssessmentReceipt,
  assessmentFields,
  decodeAssessmentFields,
  decodeAssessmentPreview,
  decodeAssessmentReceipt,
  decodeAssessmentLookup,
  sameAssessmentFields,
} from './assessment-command-contracts';
import { Assessment, Checklist, decodeAssessment } from './assessment-workspace-contracts';
export { decodeAssessment, decodeChecklist } from './assessment-workspace-contracts';
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
@Component({
  selector: 'audit-acceptance-checklist',
  imports: [RouterLink, FormField, MatButtonModule, MatProgressBarModule, AssessmentCommandEditor],
  templateUrl: './checklist.html',
  styleUrl: './checklist.scss',
})
export class AcceptanceChecklist implements NavigationProtected {
  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  private id = '';
  private decisionId: string | null = null;
  private visit = 0;
  private editRevision = 0;
  private destroyed = false;
  private request?: Subscription;
  readonly assessment = signal<Assessment | null>(null);
  readonly data = signal<Checklist | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly absent = signal(false);
  readonly commandStatus = signal('');
  readonly preview = signal<AssessmentPreview | null>(null);
  readonly receipt = signal<AssessmentReceipt | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly confirmation = signal({ reviewed: false });
  private assent = '';
  private readonly dirtyEditors = signal<Record<string, boolean>>({});
  private readonly editorSeeds = signal<Record<string, AssessmentCommandFields>>({});
  readonly dirty = computed(
    () => !!this.preview() || Object.values(this.dirtyEditors()).some(Boolean),
  );
  readonly locked = computed(() => this.busy() || this.uncertain() || !!this.preview());
  readonly confirmationFields = form(this.confirmation, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(p.reviewed, () => this.busy() || this.uncertain() || !this.preview());
  });
  constructor() {
    let identity = JSON.stringify([this.session.current(), this.session.invalidation()]);
    const route = combineLatest([this.route.paramMap, this.route.queryParamMap]).subscribe(
      ([p, q]) => {
        this.reset();
        this.id = p.get('id') ?? '';
        this.decisionId = q.get('decisionId');
        this.load();
      },
    );
    effect(() => {
      const next = JSON.stringify([this.session.current(), this.session.invalidation()]);
      if (next === identity) return;
      identity = next;
      untracked(() => {
        this.reset();
        this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.visit++;
      route.unsubscribe();
      this.request?.unsubscribe();
    });
  }
  private owner() {
    const s = this.session.current();
    return s?.staff && !this.destroyed
      ? JSON.stringify([
          s.firmId,
          s.userId,
          s.generation,
          this.session.invalidation(),
          this.id,
          this.decisionId,
          this.visit,
        ])
      : '';
  }
  private current(owner: string) {
    return !!owner && owner === this.owner();
  }
  private base() {
    return '/api/ui/clients/' + this.id + '/assessment';
  }
  private scope() {
    return {
      entity: 'assessment-request/' + this.id,
      baseRevision: this.preview()?.reviewBasis ?? '0'.repeat(64),
    };
  }
  private intent() {
    return this.owner() && this.preview() && this.data()
      ? JSON.stringify([this.owner(), this.preview(), this.data(), this.editRevision])
      : '';
  }
  get reviewed() {
    const basis = this.intent();
    return !!basis && this.confirmation().reviewed && this.assent === basis;
  }
  setReviewed(value: boolean) {
    this.confirmation.set({ reviewed: value });
    this.assent = value ? this.intent() : '';
  }
  private clearAssent() {
    this.confirmation.set({ reviewed: false });
    this.assent = '';
  }
  seed(key: string) {
    return this.editorSeeds()[key]!;
  }
  editorChanged(key: string, value: boolean) {
    this.editRevision++;
    if (this.dirtyEditors()[key] !== value)
      this.dirtyEditors.update((m) => ({ ...m, [key]: value }));
    if (!this.uncertain()) {
      this.preview.set(null);
      this.clearAssent();
    }
  }
  private clearContent() {
    this.assessment.set(null);
    this.data.set(null);
    this.editorSeeds.set({});
    this.dirtyEditors.set({});
    this.preview.set(null);
    this.receipt.set(null);
    this.clearAssent();
  }
  private reset() {
    this.visit++;
    this.editRevision++;
    this.request?.unsubscribe();
    this.clearContent();
    this.pending.set(null);
    this.uncertain.set(false);
    this.absent.set(false);
    this.busy.set(false);
    this.loading.set(false);
    this.error.set('');
    this.commandStatus.set('');
  }
  private permitted(f: AssessmentCommandFields) {
    const c = this.data();
    if (!c || c.generation !== f.generation || (this.decisionId && this.assessment()?.historical))
      return false;
    switch (f.kind) {
      case 'ANSWER':
        return (
          c.canEdit &&
          c.questions.some(
            (q) => q.code.toUpperCase() === f.questionCode && q.revision === f.revision,
          )
        );
      case 'REQUEST_REVIEW':
        return c.canEdit;
      case 'RECORD_REVIEW':
        return (
          c.canReview &&
          c.clearances.some((r) => r.id === f.reviewId && r.status === f.expectedStatus)
        );
      case 'DECISION':
        return c.canDecide;
      case 'CONTINUANCE':
        return c.canStartContinuance;
    }
  }
  async prepare(raw: AssessmentCommandFields) {
    if (this.locked() || this.loading()) return;
    let fields: AssessmentCommandFields;
    try {
      fields = decodeAssessmentFields(raw);
    } catch {
      this.commandStatus.set('Enter valid bounded assessment fields before review.');
      return;
    }
    if (!this.permitted(fields)) return;
    const owner = this.owner(),
      state = JSON.stringify(this.data()),
      edits = this.editRevision,
      requestId = crypto.randomUUID();
    this.busy.set(true);
    this.clearAssent();
    this.commandStatus.set('Preparing exact assessment review…');
    const result = await this.api.command(this.base() + '/preview', { requestId, fields });
    if (!this.current(owner)) return;
    this.busy.set(false);
    if (!result.ok) {
      this.commandStatus.set(
        result.unknown ? 'Review unavailable. No assessment action was submitted.' : result.message,
      );
      if (result.status === 401 || result.status === 403) {
        this.clearContent();
        this.load();
      }
      return;
    }
    try {
      const p = decodeAssessmentPreview(result.value);
      if (
        p.clientId !== this.id ||
        p.requestId !== requestId ||
        !sameAssessmentFields(fields, p.fields) ||
        state !== JSON.stringify(this.data()) ||
        edits !== this.editRevision ||
        !this.permitted(fields)
      )
        throw new Error('Changed review');
      this.preview.set(p);
      this.commandStatus.set('Review the exact fields and effect. No action has been submitted.');
      setTimeout(() => {
        if (this.current(owner)) document.getElementById('assessment-action-review')?.focus();
      });
    } catch {
      this.commandStatus.set(
        'Review changed or returned an unsupported reply. Refresh and review again.',
      );
    }
  }
  async execute() {
    const p = this.preview(),
      owner = this.owner();
    if (!p || !this.reviewed || this.busy() || this.uncertain() || !this.permitted(p.fields))
      return;
    const reference = { requestId: p.requestId, requestHash: p.requestHash };
    if (!this.drafts.save(this.scope(), reference, pendingRequestReference, true)) {
      this.commandStatus.set('Recovery storage is unavailable. No assessment action was sent.');
      return;
    }
    this.pending.set(reference);
    this.uncertain.set(true);
    this.absent.set(false);
    this.busy.set(true);
    this.clearAssent();
    this.commandStatus.set('Submitting the reviewed assessment action once…');
    const result = await this.api.command(this.base() + '/commands', {
      requestId: p.requestId,
      fields: p.fields,
      reviewBasis: p.reviewBasis,
      requestHash: p.requestHash,
      reviewed: true,
    });
    if (!this.current(owner)) return;
    this.busy.set(false);
    this.preview.set(null);
    if (!result.ok) {
      this.commandStatus.set(
        result.unknown
          ? 'Outcome unconfirmed. Verify the retained assessment receipt before another action.'
          : result.message,
      );
      // A received refusal is still reconciled when its reference might already have committed.
      // Keep the original request until authorized lookup and explicit acknowledgement.
      if (result.status === 401 || result.status === 403) {
        this.clearContent();
        this.load();
      }
      return;
    }
    try {
      const r = decodeAssessmentReceipt(result.value);
      if (!this.matches(r, reference) || JSON.stringify(r.preview) !== JSON.stringify(p))
        throw new Error('Wrong receipt');
      this.receipt.set(r);
      this.commandStatus.set(
        'Assessment action recorded. Acknowledge its retained receipt before continuing.',
      );
    } catch {
      this.commandStatus.set(
        'Outcome unconfirmed. The reply could not prove the retained request; verify its receipt.',
      );
    }
  }
  private matches(r: AssessmentReceipt, p: PendingRequestReference) {
    return (
      r.clientId === this.id &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcile() {
    const p = this.pending(),
      owner = this.owner();
    if (!p || !this.data() || this.busy()) return;
    this.busy.set(true);
    this.preview.set(null);
    this.clearAssent();
    this.absent.set(false);
    try {
      const lookup = await this.api.get(
        this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
        decodeAssessmentLookup,
      );
      if (!this.current(owner)) return;
      if (lookup.found && lookup.receipt && this.matches(lookup.receipt, p)) {
        this.receipt.set(lookup.receipt);
        this.commandStatus.set(
          'Retained receipt confirms the assessment action. Acknowledge before continuing.',
        );
      } else if (!lookup.found) {
        this.receipt.set(null);
        this.absent.set(true);
        this.commandStatus.set(
          'No committed receipt was found at verification. No action was retried.',
        );
      } else throw new Error('Wrong receipt');
    } catch {
      if (this.current(owner)) {
        this.clearContent();
        this.commandStatus.set(
          'Receipt verification unavailable. Refresh authorized context before checking the retained reference again.',
        );
        this.busy.set(false);
        this.load();
      }
    } finally {
      if (this.current(owner)) this.busy.set(false);
    }
  }
  acknowledge() {
    if (this.receipt() && !this.busy())
      this.closeReference('Assessment receipt acknowledged. Loading current evaluation.');
  }
  acknowledgeAbsent() {
    if (this.absent() && !this.busy())
      this.closeReference(
        'Absent request closed. Review the freshly loaded evaluation before a new action.',
      );
  }
  private closeReference(message: string) {
    if (!this.drafts.clear(this.scope().entity)) {
      this.commandStatus.set('Recovery reference could not be cleared. Retry acknowledgement.');
      return;
    }
    this.pending.set(null);
    this.receipt.set(null);
    this.absent.set(false);
    this.uncertain.set(false);
    this.preview.set(null);
    this.clearAssent();
    this.commandStatus.set(message);
    this.load();
  }
  cancelReview() {
    if (!this.busy() && !this.uncertain()) {
      this.preview.set(null);
      this.clearAssent();
    }
  }
  async refresh() {
    if (this.busy()) return;
    if (this.uncertain()) {
      this.load();
      return;
    }
    if (await this.confirmNavigation()) this.load();
  }
  async confirmNavigation() {
    if (!this.session.current()?.staff) return true;
    if (this.busy() || this.pending() || this.uncertain()) {
      this.commandStatus.set(
        'Verify and acknowledge the retained assessment request before leaving.',
      );
      return false;
    }
    if (!this.dirty()) return true;
    const owner = this.owner();
    const discard = await firstValueFrom(
      this.dialog.open(AssessmentNavigationDialog).afterClosed(),
    );
    if (!this.current(owner)) return false;
    if (discard) {
      this.clearContent();
      return true;
    }
    return false;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.dirty() || this.busy() || this.pending() || this.uncertain()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  private buildSeeds(c: Checklist) {
    const seeds: Record<string, AssessmentCommandFields> = {
      request: assessmentFields('REQUEST_REVIEW', c.generation),
      decision: assessmentFields('DECISION', c.generation),
      continuance: assessmentFields('CONTINUANCE', c.generation),
    };
    for (const q of c.questions)
      seeds['answer-' + q.code] = {
        ...assessmentFields('ANSWER', c.generation),
        questionCode: q.code.toUpperCase(),
        revision: q.revision,
        answer: q.answer,
        evidence: q.evidence,
      };
    for (const r of c.clearances)
      seeds['review-' + r.id] = {
        ...assessmentFields('RECORD_REVIEW', c.generation),
        reviewId: r.id,
        expectedStatus: r.status,
        evidence: r.evidence,
        conditions: r.conditions,
      };
    this.editorSeeds.set(seeds);
  }
  private load() {
    this.request?.unsubscribe();
    this.visit++;
    this.clearContent();
    this.loading.set(false);
    this.error.set('');
    if (
      !guid.test(this.id) ||
      (this.decisionId !== null && !guid.test(this.decisionId)) ||
      !this.session.current()?.staff
    )
      return;
    this.loading.set(true);
    const owner = this.owner(),
      id = this.id,
      decisionId = this.decisionId;
    this.request = this.http
      .get<unknown>(
        this.base() + (decisionId ? '?decisionId=' + encodeURIComponent(decisionId) : ''),
      )
      .pipe(timeout(15000))
      .subscribe({
        next: (raw) => {
          if (!this.current(owner)) return;
          try {
            const workspace = decodeAssessment(raw),
              c = workspace.checklist;
            if (c.clientId !== id || (decisionId && workspace.selectedDecision?.id !== decisionId))
              throw new Error('Wrong assessment');
            this.buildSeeds(c);
            this.assessment.set(workspace);
            this.data.set(c);
            const recovered = this.drafts.readPendingRequest(this.scope());
            if (recovered.state === 'ready') {
              this.pending.set(recovered.draft.value);
              this.uncertain.set(true);
              this.commandStatus.set(
                'Retained request requires receipt verification. No action was retried.',
              );
            } else if (recovered.state === 'unavailable' || recovered.state === 'stale') {
              this.uncertain.set(true);
              this.commandStatus.set(
                'Recovery storage is unavailable or invalid. Restore safe storage before submitting another assessment action.',
              );
            } else if (!this.pending()) {
              // A fresh authorized read and available storage establish there is no retained tab reference.
              // No business command is submitted by this transition.
              this.uncertain.set(false);
            }
          } catch {
            this.clearContent();
            this.error.set('Acceptance returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (!this.current(owner)) return;
          this.loading.set(false);
          this.clearContent();
          this.error.set('Acceptance unavailable. Check your client scope or retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
