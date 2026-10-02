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
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid } from '../../core/api';
import { bool, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';

const receipt = obj({
  id: guid,
  firmId: guid,
  clientId: guid,
  engagementId: guid,
  sourceKind: oneOf('TB'),
  trialBalanceDatasetId: guid,
  importBatchId: nullable(guid),
  sourceIdentityHash: sha256,
  decision: oneOf('ACCEPTED'),
  evidenceReference: str(2000),
  acceptedByUserId: guid,
  createdAt: instant,
});
const selected = obj({
  decisionId: guid,
  sourceKind: oneOf('TB'),
  trialBalanceDatasetId: guid,
  importBatchId: nullable(guid),
  sourceIdentityHash: sha256,
  acceptedByUserId: guid,
  acceptedAt: instant,
  inputGeneration: nat,
});
const shape = obj({
  datasetId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: nullable(guid),
  periodCode: nullable(str(100)),
  sourceRevision: nat,
  currency: str(10),
  entity: str(500),
  importState: oneOf('SEALED'),
  validationStatus: oneOf('Accepted', 'Pending', 'Rejected'),
  balanced: bool,
  sourceHash: str(100),
  importedByUserId: guid,
  importedAt: instant,
  inputGeneration: nat,
  receipt: nullable(receipt),
  selected: nullable(selected),
  revision: sha256,
  canAccept: bool,
  blocker: nullable(str(2000)),
});
const glReceipt = obj({
  id: guid,
  firmId: guid,
  clientId: guid,
  engagementId: guid,
  sourceKind: oneOf('GL'),
  trialBalanceDatasetId: nullable(guid),
  importBatchId: guid,
  sourceIdentityHash: sha256,
  decision: oneOf('ACCEPTED'),
  evidenceReference: str(2000),
  acceptedByUserId: guid,
  createdAt: instant,
});
const glSelected = obj({
  decisionId: guid,
  sourceKind: oneOf('GL'),
  trialBalanceDatasetId: nullable(guid),
  importBatchId: guid,
  sourceIdentityHash: sha256,
  acceptedByUserId: guid,
  acceptedAt: instant,
  inputGeneration: nat,
});
const glShape = obj({
  importBatchId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: guid,
  periodCode: str(100),
  bookId: nullable(guid),
  currency: str(10),
  entity: str(500),
  importState: oneOf('SEALED'),
  rowCount: nat,
  rawFileSha256: str(100),
  sourceHash: str(100),
  profileVersion: str(200),
  parserVersion: str(200),
  importedByUserId: guid,
  importedAt: instant,
  inputGeneration: nat,
  receipt: nullable(glReceipt),
  selected: nullable(glSelected),
  revision: sha256,
  canAccept: bool,
  blocker: nullable(str(2000)),
});
type Review =
  (ReturnType<typeof shape> & { kind: 'TB' }) | (ReturnType<typeof glShape> & { kind: 'GL' });
export function decodeGeneralLedgerAcceptance(
  raw: unknown,
  path: string,
): ReturnType<typeof glShape> & { kind: 'GL' } {
  const v = glShape(raw, path);
  if (
    (v.receipt &&
      (v.receipt.importBatchId !== v.importBatchId ||
        v.receipt.trialBalanceDatasetId !== null ||
        v.receipt.clientId !== v.clientId ||
        v.receipt.engagementId !== v.engagementId ||
        v.receipt.sourceIdentityHash !== v.sourceHash)) ||
    (v.selected &&
      (v.selected.trialBalanceDatasetId !== null ||
        v.selected.inputGeneration !== v.inputGeneration ||
        (v.selected.importBatchId === v.importBatchId &&
          v.selected.sourceIdentityHash !== v.sourceHash))) ||
    (v.canAccept &&
      (v.receipt !== null ||
        v.blocker !== null ||
        !/^[a-f0-9]{64}$/i.test(v.sourceHash) ||
        !/^[a-f0-9]{64}$/i.test(v.rawFileSha256) ||
        !v.periodCode)) ||
    (!v.canAccept && !v.blocker)
  )
    throw new Error('Unsupported general ledger acceptance');
  return { ...v, kind: 'GL' };
}
export function decodeAcceptance(
  raw: unknown,
  path: string,
): ReturnType<typeof shape> & { kind: 'TB' } {
  const v = shape(raw, path);
  if (
    v.sourceRevision < 1 ||
    (v.receipt &&
      (v.receipt.trialBalanceDatasetId !== v.datasetId ||
        v.receipt.clientId !== v.clientId ||
        v.receipt.engagementId !== v.engagementId ||
        v.receipt.importBatchId !== null ||
        v.receipt.sourceIdentityHash !== v.sourceHash)) ||
    (v.selected &&
      (v.selected.inputGeneration !== v.inputGeneration || v.selected.importBatchId !== null)) ||
    (v.canAccept &&
      (v.receipt !== null ||
        v.blocker !== null ||
        v.validationStatus !== 'Accepted' ||
        !v.balanced ||
        !/^[a-f0-9]{64}$/i.test(v.sourceHash))) ||
    (!v.canAccept && !v.blocker)
  )
    throw new Error('Unsupported source acceptance');
  return { ...v, kind: 'TB' };
}
export function decodeAcceptanceDraft(raw: unknown): { evidenceReference: string } | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const v = raw as Record<string, unknown>;
  return Object.keys(v).length === 1 &&
    typeof v['evidenceReference'] === 'string' &&
    v['evidenceReference'].length <= 2000
    ? { evidenceReference: v['evidenceReference'] }
    : null;
}

@Component({
  selector: 'audit-source-acceptance',
  imports: [...SHARED, FormField, MatButtonModule, RouterLink],
  template: `
    <audit-page-header
      title="Source acceptance"
      eyebrow="Client accounting"
      [description]="
        isGeneralLedger
          ? 'Independent human review of one immutable general ledger source.'
          : 'Independent human review of one immutable trial balance source.'
      "
    />
    <audit-state [loading]="loading()" [error]="error()" label="source acceptance" />
    @if (message()) {
      <p role="status" aria-live="polite">{{ message() }}</p>
    }
    @if (uncertain()) {
      <section class="warning" aria-labelledby="reconcile-heading">
        <h2 id="reconcile-heading">Acceptance outcome needs review</h2>
        <p>
          No automatic retry. Read the persisted decision and selected pointer before any further
          submission.
        </p>
        <button matButton [disabled]="busy() || loading()" (click)="refresh(true)">
          Read persisted acceptance
        </button>
        <button matButton [disabled]="!reconciled() || busy() || loading()" (click)="acknowledge()">
          I reviewed the persisted acceptance
        </button>
      </section>
    }

    <button matButton [disabled]="!id() || busy() || loading()" (click)="refresh()">
      Refresh source acceptance
    </button>
    @if (review(); as r) {
      <a
        matButton
        [routerLink]="[
          '/app/engagements',
          r.engagementId,
          isGeneralLedger ? 'general-ledger' : 'tb-intake',
        ]"
        >{{
          isGeneralLedger ? 'Back to general ledger inspection' : 'Back to trial balance intake'
        }}</a
      >
      <section aria-labelledby="source-heading">
        <h2 id="source-heading">Proposed source</h2>
        <p>
          {{ r.entity }} · {{ r.currency }} · {{ r.periodCode ?? 'No linked reporting period' }}
        </p>
        <dl>
          <dt>{{ isGeneralLedger ? 'GL import batch' : 'Dataset' }}</dt>
          <dd>{{ sourceIdentity(r) }}</dd>
          <dt>Client</dt>
          <dd>{{ r.clientId }}</dd>
          <dt>Engagement</dt>
          <dd>{{ r.engagementId }}</dd>
          @if (r.kind === 'TB') {
            <dt>Source revision</dt>
            <dd>{{ r.sourceRevision }}</dd>
          } @else {
            <dt>Book</dt>
            <dd>{{ r.bookId ?? 'No linked book' }}</dd>
            <dt>Imported line count</dt>
            <dd>{{ r.rowCount }}</dd>
            <dt>Raw file digest</dt>
            <dd>{{ r.rawFileSha256 || 'Unavailable' }}</dd>
            <dt>Import profile / parser</dt>
            <dd>{{ r.profileVersion }} / {{ r.parserVersion }}</dd>
          }
          <dt>Source digest</dt>
          <dd>{{ r.sourceHash || 'Unavailable' }}</dd>
          <dt>Imported by</dt>
          <dd>{{ r.importedByUserId }} · {{ r.importedAt }}</dd>
          @if (r.kind === 'TB') {
            <dt>Worker validation</dt>
            <dd>
              {{ r.validationStatus }} · {{ r.importState }} ·
              {{ r.balanced ? 'Balanced' : 'Unbalanced' }}
            </dd>
          } @else {
            <dt>Import state</dt>
            <dd>{{ r.importState }}</dd>
          }
          <dt>Current input generation</dt>
          <dd>{{ r.inputGeneration }}</dd>
        </dl>
        @if (isGeneralLedger) {
          <p>
            Sealing and source acceptance do not establish GL completeness or a professional
            conclusion. Account-exact completeness evidence and its independent review remain
            separate downstream gates.
          </p>
        } @else {
          <p>
            Worker validation and sealing do not replace independent source acceptance or a
            professional conclusion.
          </p>
        }
      </section>
      <section aria-labelledby="selection-heading">
        <h2 id="selection-heading">Current selected source → proposed source</h2>
        @if (r.selected; as s) {
          <p class="identity">
            Current: {{ isGeneralLedger ? s.importBatchId : s.trialBalanceDatasetId }} · decision
            {{ s.decisionId }} ·
            {{ s.sourceIdentityHash }}
          </p>
        } @else {
          <p>
            No {{ isGeneralLedger ? 'general ledger' : 'trial balance' }} source is selected for
            this engagement.
          </p>
        }
        <p class="identity">Proposed: {{ sourceIdentity(r) }} · {{ r.sourceHash }}</p>
        <p>
          Selection is for this engagement and source kind ({{ isGeneralLedger ? 'GL' : 'TB' }}),
          across reporting periods. A different selected source will be superseded. Earlier
          decisions remain retained. Acceptance advances input generation; dependent evidence may
          become stale and needs re-preparation.
        </p>
      </section>
      @if (r.receipt; as a) {
        <section aria-labelledby="receipt-heading">
          <h2 id="receipt-heading">Retained acceptance decision</h2>
          <dl>
            <dt>Decision</dt>
            <dd>{{ a.id }}</dd>
            <dt>Reviewer</dt>
            <dd>{{ a.acceptedByUserId }}</dd>
            <dt>Accepted at</dt>
            <dd>{{ a.createdAt }}</dd>
            <dt>Evidence reference</dt>
            <dd>{{ a.evidenceReference }}</dd>
          </dl>
          <p>
            {{
              r.selected?.decisionId === a.id
                ? 'This is the currently selected source.'
                : 'This historical decision is retained; another source is currently selected.'
            }}
          </p>
        </section>
      }
      @if (r.blocker) {
        <p role="alert">{{ r.blocker }}</p>
      }
      @if (!r.receipt) {
        <form (submit)="$event.preventDefault(); accept()">
          <fieldset [disabled]="busy() || loading() || uncertain() || !r.canAccept">
            <label
              >Acceptance evidence reference<textarea
                [formField]="fields.evidenceReference"
                (input)="invalidateReview()"
                rows="3"
              ></textarea>
            </label>
            <p>
              Reference the evidence for your independent review. Do not enter credentials or
              tokens.
            </p>
            <label
              ><input
                type="checkbox"
                [formField]="fields.reviewed"
                (change)="recordAssent($event)"
              />I independently reviewed this exact source and its effect on the selected
              pointer.</label
            >
          </fieldset>
          <div class="toolbar">
            <button
              matButton
              type="button"
              [disabled]="busy() || loading() || uncertain()"
              (click)="saveDraft()"
            >
              Save acceptance tab draft
            </button>
            <button
              matButton
              type="button"
              [disabled]="busy() || loading() || uncertain()"
              (click)="recoverDraft()"
            >
              Recover acceptance tab draft
            </button>
            <button
              matButton
              type="button"
              [disabled]="busy() || loading() || uncertain()"
              (click)="discardDraft()"
            >
              Discard acceptance draft
            </button>
            <button matButton type="submit" [disabled]="!canSubmit()">
              Accept reviewed source
            </button>
          </div>
        </form>
      }
      <p class="identity">Reviewed workspace revision {{ r.revision }}</p>
    }
  `,
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      section {
        margin-block: 1.5rem;
      }
      dl {
        display: grid;
        grid-template-columns: minmax(100px, 180px) minmax(0, 1fr);
        gap: 0.6rem;
      }
      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
      dt {
        font-weight: 600;
      }
      .identity {
        overflow-wrap: anywhere;
      }
      textarea {
        display: block;
        width: 100%;
        box-sizing: border-box;
        padding: 0.7rem;
      }
      label {
        display: block;
        margin-block: 1rem;
      }
      fieldset {
        border: 0;
        padding: 0;
        min-width: 0;
      }
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
      .warning {
        padding: 1rem;
        border-inline-start: 3px solid var(--mat-sys-error);
      }
      @media (max-width: 500px) {
        dl {
          grid-template-columns: 1fr;
        }
        dd {
          margin-bottom: 0.6rem;
        }
      }
    `,
  ],
})
export class SourceAcceptance implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly isGeneralLedger = inject(ActivatedRoute).snapshot?.data?.['sourceKind'] === 'GL';
  readonly id = routeGuid();
  sourceIdentity(r: Review): string {
    return r.kind === 'GL' ? r.importBatchId : r.datasetId;
  }
  private endpoint(id: string) {
    return this.isGeneralLedger
      ? `/api/ui/gl-sources/${id}/acceptance`
      : `/api/ui/datasets/${id}/source/acceptance`;
  }
  readonly review = signal<Review | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciled = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly model = signal({ evidenceReference: '', reviewed: false });
  readonly fields = form(this.model, (p) => {
    required(p.evidenceReference);
    maxLength(p.evidenceReference, 2000);
  });
  private request = 0;
  private destroyed = false;
  private context = '';
  private saved = '';
  private assent = '';
  readonly canSubmit = computed(
    () =>
      this.review()?.canAccept &&
      !this.busy() &&
      !this.loading() &&
      !this.uncertain() &&
      this.model().evidenceReference.trim().length > 0 &&
      this.model().evidenceReference.length <= 2000 &&
      this.model().reviewed,
  );
  private intent() {
    return { evidenceReference: this.model().evidenceReference };
  }
  private entity() {
    return `${this.isGeneralLedger ? 'gl-source-acceptance' : 'source-acceptance'}:${this.id()}`;
  }
  private scope() {
    return { entity: this.entity(), baseRevision: this.review()?.revision ?? '' };
  }
  private current(generation: number, id: string, request: number) {
    return (
      !this.destroyed &&
      this.session.invalidation() === generation &&
      this.id() === id &&
      this.request === request &&
      this.session.current()?.staff
    );
  }
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request++;
    });
    effect(() => {
      const s = this.session.current(),
        key = `${this.id()}:${s?.firmId}:${s?.userId}:${s?.generation}:${this.session.invalidation()}`;
      if (key === this.context) return;
      this.context = key;
      untracked(() => {
        this.request++;
        this.review.set(null);
        this.model.set({ evidenceReference: '', reviewed: false });
        this.uncertain.set(false);
        this.busy.set(false);
        this.loading.set(false);
        this.reconciled.set(false);
        this.error.set('');
        this.saved = '';
        this.assent = '';
        this.message.set('');
        if (s?.staff && this.id()) void this.refresh();
        else if (s?.staff)
          this.error.set(
            'Choose an available source through the engagement intake or general ledger inspection.',
          );
      });
    });
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  private dirty() {
    const v = this.model().evidenceReference;
    return v.length > 0 && JSON.stringify(this.intent()) !== this.saved;
  }
  invalidateReview() {
    this.assent = '';
    this.model.update((v) => ({ ...v, reviewed: false }));
  }
  recordAssent(e: Event) {
    this.assent =
      (e.target as HTMLInputElement).checked && this.review()
        ? `${this.review()!.revision}:${JSON.stringify(this.intent())}`
        : '';
  }
  async refresh(reconcile = false) {
    if (this.busy()) return;
    const id = this.id();
    if (!id || !this.session.current()?.staff) return;
    const generation = this.session.invalidation(),
      request = ++this.request;
    const previous = this.review()?.revision;
    this.loading.set(true);
    this.review.set(null);
    this.error.set('');
    this.reconciled.set(false);
    this.invalidateReview();
    try {
      const r = await this.api.get<Review>(
        this.endpoint(id),
        this.isGeneralLedger ? decodeGeneralLedgerAcceptance : decodeAcceptance,
      );
      if (!this.current(generation, id, request)) return;
      if (
        this.sourceIdentity(r) !== id ||
        (r.receipt && r.receipt.firmId !== this.session.current()?.firmId) ||
        (r.canAccept && r.importedByUserId === this.session.current()?.userId)
      )
        throw new Error('Unsupported source context.');
      this.review.set(r);
      const draft = this.drafts.read(this.scope(), decodeAcceptanceDraft);
      if (
        !previous &&
        (draft.state === 'stale' || (draft.state === 'ready' && draft.draft.submissionPending))
      ) {
        this.uncertain.set(true);
        this.message.set(
          'A previous tab checkpoint needs persisted-state review before submission.',
        );
      }
      if (previous && previous !== r.revision)
        this.message.set(
          'The persisted source inputs changed. Review the current selection again.',
        );
      if (reconcile) this.reconciled.set(true);
    } catch (e) {
      if (this.current(generation, id, request)) {
        this.review.set(null);
        this.model.set({ evidenceReference: '', reviewed: false });
        this.error.set(e instanceof Error ? e.message : 'Source acceptance is unavailable.');
      }
    } finally {
      if (this.current(generation, id, request)) this.loading.set(false);
    }
  }
  saveDraft(pending = false): boolean {
    if (!this.review() || (!pending && (this.busy() || this.uncertain()))) return false;
    const saved = this.drafts.save(this.scope(), this.intent(), decodeAcceptanceDraft, pending);
    if (saved) {
      this.saved = JSON.stringify(this.intent());
      if (!pending)
        this.message.set('Evidence-reference draft saved in this tab. Review assent is not saved.');
    } else this.message.set('Tab storage is unavailable. No acceptance was sent.');
    return saved;
  }
  recoverDraft() {
    if (this.busy() || this.uncertain() || !this.review()) return;
    const r = this.drafts.read(this.scope(), decodeAcceptanceDraft);
    if (r.state !== 'ready') {
      this.message.set('No recoverable draft for this exact source revision.');
      return;
    }
    if (r.draft.submissionPending) {
      this.uncertain.set(true);
      this.reconciled.set(false);
      return;
    }
    this.model.set({ ...r.draft.value, reviewed: false });
    this.assent = '';
    this.saved = JSON.stringify(this.intent());
    this.message.set('Draft recovered. Review the current source and selection again.');
  }
  discardDraft() {
    if (this.busy() || this.uncertain()) return;
    this.drafts.clear(this.entity());
    this.model.set({ evidenceReference: '', reviewed: false });
    this.saved = '';
    this.assent = '';
  }
  acknowledge() {
    if (!this.uncertain() || !this.reconciled() || !this.review() || this.busy() || this.loading())
      return;
    if (!this.drafts.clear(this.entity())) {
      this.message.set(
        'Cannot clear the pending tab checkpoint. Keep the write fence and retry persisted-state review.',
      );
      return;
    }
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.invalidateReview();
    this.saved = '';
    this.message.set(
      this.review()!.receipt
        ? 'The retained decision is confirmed. No further acceptance is needed.'
        : 'No decision for this source was observed. Review again before submitting.',
    );
  }
  async accept() {
    const r = this.review();
    if (
      !r ||
      !this.canSubmit() ||
      this.assent !== `${r.revision}:${JSON.stringify(this.intent())}` ||
      !this.saveDraft(true)
    )
      return;
    const generation = this.session.invalidation(),
      id = this.sourceIdentity(r),
      request = this.request;
    this.busy.set(true);
    this.invalidateReview();
    this.message.set('');
    const result = await this.api.command<string>(this.endpoint(id), {
      revision: r.revision,
      evidenceReference: this.model().evidenceReference,
      reviewed: true,
    });
    if (!this.current(generation, id, request)) return;
    this.busy.set(false);
    if (result.ok && typeof result.value === 'string' && guidPattern.test(result.value)) {
      this.drafts.clear(this.entity());
      this.model.set({ evidenceReference: '', reviewed: false });
      this.saved = '';
      await this.refresh();
      this.message.set(
        'Source acceptance persisted. The independent decision and selected pointer are shown below.',
      );
    } else if (result.ok || result.unknown) {
      this.uncertain.set(true);
      this.reconciled.set(false);
      this.message.set(
        'The acceptance outcome is not confirmed. No automatic retry. Read the persisted decision.',
      );
    } else {
      this.drafts.clear(this.entity());
      this.review.set(null);
      this.message.set(result.message);
    }
  }
  async confirmNavigation(): Promise<boolean> {
    if (this.busy() || this.uncertain()) {
      this.message.set(
        'Read and acknowledge the persisted acceptance before leaving this pending submission.',
      );
      return false;
    }
    if (!this.dirty()) return true;
    const choice = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if (choice === 'save') return this.saveDraft();
    if (choice === 'discard') {
      this.discardDraft();
      return true;
    }
    return false;
  }
}
