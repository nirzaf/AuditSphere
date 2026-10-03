import {
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts, PendingRequestReference, pendingRequestReference } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { TrialBalanceSource } from '../engagements/tb-source';
import {
  JournalFields,
  JournalAction,
  journalFields,
  decodeJournalReview,
  decodeJournalPreview,
  decodeJournalReceipt,
  decodeJournalLookup,
  decodeJournalHistory,
} from './journal-contracts';

type Request = JournalFields & { requestId: string; reviewBasis: string; reviewed: boolean };
const empty = (): JournalFields => ({
  action: 'UPDATE',
  reason: '',
  evidenceReference: '',
  reversalNumber: '',
  lines: [],
});
@Component({
  selector: 'audit-adjustment-journal',
  imports: [RouterLink, FormField, MatButtonModule, TrialBalanceSource, ...SHARED],
  templateUrl: './journal.html',
  styleUrl: './journal.scss',
})
export class AdjustmentJournal implements NavigationProtected {
  readonly Math = Math;
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly historyPage = signal(1);
  readonly journal = this.api.resource(
    () => (this.id() ? this.base() + '/workspace?historyPage=' + this.historyPage() : null),
    (raw, path) => {
      const v = decodeJournalReview(raw, path);
      if (v.journalId !== this.id() || v.historyPage !== this.historyPage())
        throw new Error('Wrong journal review page');
      return v;
    },
  );
  readonly model = signal<JournalFields>(empty());
  readonly fields = form(this.model, (p) => {
    required(p.reason);
    maxLength(p.reason, 4000);
    required(p.evidenceReference);
    maxLength(p.evidenceReference, 2000);
    maxLength(p.reversalNumber, 32);
  });
  readonly preview = signal<ReturnType<typeof decodeJournalPreview> | null>(null);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly receipt = signal<ReturnType<typeof decodeJournalReceipt> | null>(null);
  readonly draftAvailable = signal(false);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly sourceOpen = signal(false);
  readonly historyId = signal('');
  readonly reflectionState = signal<
    'NOT_REFLECTED' | 'REFLECTED' | 'PARTIALLY_REFLECTED' | 'NOT_APPLICABLE'
  >('NOT_REFLECTED');
  readonly reflectionEvidence = signal('');
  readonly historical = this.api.resource(
    () =>
      this.journal.data()?.history.some((h) => h.id === this.historyId())
        ? this.base() + '/history/' + this.historyId()
        : null,
    (raw, path) => {
      const h = decodeJournalHistory(raw, path),
        r = h.receipt;
      if (
        r.id !== this.historyId() ||
        (r.journalId !== this.id() && r.resultJournalId !== this.id()) ||
        h.before.journalId !== r.journalId ||
        h.after.journalId !== r.resultJournalId ||
        h.before.revision !== r.oldRevision ||
        h.after.revision !== r.newRevision ||
        h.before.status !== r.oldStatus ||
        h.after.status !== r.newStatus
      )
        throw new Error('Mixed retained revision');
      return h;
    },
  );
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  private readonly baseline = signal(JSON.stringify(empty()));
  private generation = 0;
  private request: Request | null = null;
  private checkedAbsent = false;
  constructor() {
    effect(() => {
      this.id();
      this.session.invalidation();
      untracked(() => {
        this.generation++;
        this.model.set(empty());
        this.baseline.set(JSON.stringify(empty()));
        this.preview.set(null);
        this.reviewed.set(false);
        this.receipt.set(null);
        this.pending.set(null);
        this.uncertain.set(false);
        this.busy.set(false);
        this.sourceOpen.set(false);
        this.historyId.set('');
        this.reflectionState.set('NOT_REFLECTED');
        this.reflectionEvidence.set('');
        this.request = null;
        this.checkedAbsent = false;
        this.message.set('');
        this.failed.set(false);
        this.draftAvailable.set(false);
      });
    });
    effect(() => {
      const v = this.journal.data();
      if (!v) return;
      untracked(() => {
        this.reviewed.set(false);
        this.preview.set(null);
        this.historyId.set('');
        this.sourceOpen.set(false);
        if (!this.dirty() && !this.uncertain()) {
          const fields: JournalFields = {
            action: v.canEdit
              ? 'UPDATE'
              : v.canSubmit
                ? 'SUBMIT'
                : v.canPost
                  ? 'POST'
                  : v.canReverse
                    ? 'REVERSE'
                    : 'RETURN',
            reason: v.reason,
            evidenceReference: v.evidenceReference,
            reversalNumber: '',
            lines: v.canEdit ? v.lines.map((l) => ({ ...l })) : [],
          };
          this.model.set(fields);
          this.baseline.set(JSON.stringify(fields));
        }
        const draft = this.drafts.read(this.scope(), journalFields);
        this.draftAvailable.set(draft.state === 'ready');
        const checkpoint = this.drafts.readPendingRequest(this.scope(true));
        if (checkpoint.state === 'ready') {
          this.pending.set(checkpoint.draft.value);
          this.uncertain.set(true);
          this.message.set(UNKNOWN_OUTCOME);
        }
        if (v.sourceReflection && v.sourceReflection.state !== 'UNKNOWN') {
          this.reflectionState.set(v.sourceReflection.state as any);
          this.reflectionEvidence.set(v.sourceReflection.evidence ?? '');
        } else {
          this.reflectionState.set('NOT_REFLECTED');
          this.reflectionEvidence.set('');
        }
      });
    });
    effect(() => {
      this.model();
      untracked(() => {
        this.preview.set(null);
        this.reviewed.set(false);
      });
    });
  }
  private base() {
    return '/api/ui/accounting/journals/' + this.id();
  }
  private scope(pending = false) {
    return {
      entity: (pending ? 'journal-request/' : 'journal-fields/') + this.id(),
      baseRevision: this.journal.data()?.reviewBasis ?? '',
    };
  }
  enabled(action: JournalAction) {
    const v = this.journal.data();
    return (
      !!v &&
      {
        UPDATE: v.canEdit,
        SUBMIT: v.canSubmit,
        RETURN: v.canReturn,
        POST: v.canPost,
        REVERSE: v.canReverse,
      }[action]
    );
  }
  changeAction(event: Event) {
    if (this.busy() || this.uncertain()) return;
    const action = (event.target as HTMLSelectElement).value as JournalAction;
    if (!this.enabled(action)) return;
    this.model.update((m) => ({
      ...m,
      action,
      lines: action === 'UPDATE' ? this.journal.data()!.lines.map((l) => ({ ...l })) : [],
      reversalNumber: '',
    }));
  }
  editLine(i: number, key: 'accountCode' | 'debit' | 'credit', event: Event) {
    if (this.busy() || this.uncertain()) return;
    const value = (event.target as HTMLInputElement).value;
    this.model.update((m) => ({
      ...m,
      lines: m.lines.map((l, n) => (n === i ? { ...l, [key]: value } : l)),
    }));
  }
  addLine() {
    if (!this.busy() && !this.uncertain() && this.model().lines.length < 500)
      this.model.update((m) => ({
        ...m,
        lines: [...m.lines, { accountCode: '', debit: '0', credit: '0' }],
      }));
  }
  removeLine(i: number) {
    if (!this.busy() && !this.uncertain())
      this.model.update((m) => ({ ...m, lines: m.lines.filter((_, n) => n !== i) }));
  }
  async prepare() {
    const v = this.journal.data(),
      generation = this.generation;
    if (
      !v ||
      this.busy() ||
      !this.enabled(this.model().action) ||
      !this.fields().valid() ||
      (this.uncertain() && !this.checkedAbsent)
    )
      return;
    const checkpoint = this.pending();
    const r: Request = {
      ...structuredClone(this.model()),
      requestId: checkpoint?.requestId ?? crypto.randomUUID(),
      reviewBasis: v.reviewBasis,
      reviewed: false,
    };
    this.preview.set(null);
    this.reviewed.set(false);
    this.busy.set(true);
    this.failed.set(false);
    try {
      const result = await this.api.command(this.base() + '/preview', r);
      if (generation !== this.generation) return;
      if (!result.ok) {
        this.message.set(result.message);
        this.failed.set(true);
        return;
      }
      const p = decodeJournalPreview(result.value);
      if (
        p.action !== r.action ||
        p.reviewBasis !== v.reviewBasis ||
        this.journal.data()?.reviewBasis !== v.reviewBasis ||
        (checkpoint && p.requestHash !== checkpoint.requestHash)
      )
        throw new Error(
          'The pending intent or reviewed context changed. Keep the request reference and inspect its receipt.',
        );
      this.request = r;
      this.preview.set(p);
      this.message.set('');
    } catch (e) {
      if (generation === this.generation) {
        this.message.set(e instanceof Error ? e.message : 'Unsupported journal preview.');
        this.failed.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async execute() {
    const v = this.journal.data(),
      p = this.preview(),
      r = this.request,
      generation = this.generation;
    if (
      !v ||
      !p?.canProceed ||
      !r ||
      !this.reviewed() ||
      this.busy() ||
      v.reviewBasis !== r.reviewBasis ||
      this.model().action !== r.action ||
      this.model().reason !== r.reason ||
      this.model().evidenceReference !== r.evidenceReference ||
      this.model().reversalNumber !== r.reversalNumber ||
      JSON.stringify(this.model().lines) !== JSON.stringify(r.lines)
    )
      return;
    this.busy.set(true);
    this.failed.set(false);
    this.reviewed.set(false);
    const pending = { requestId: r.requestId, requestHash: p.requestHash };
    this.pending.set(pending);
    this.drafts.save(this.scope(), this.model(), journalFields, true);
    this.drafts.save(this.scope(true), pending, pendingRequestReference, true);
    const result = await this.api.command(this.base() + '/actions', { ...r, reviewed: true });
    if (generation !== this.generation) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      this.failed.set(true);
      this.preview.set(null);
      if (result.unknown) {
        this.uncertain.set(true);
        this.checkedAbsent = false;
      } else {
        this.clearPending();
      }
      return;
    }
    try {
      const receipt = decodeJournalReceipt(result.value, 'receipt');
      if (!this.matches(receipt, pending)) throw new Error('Unconfirmed journal receipt');
      this.receipt.set(receipt);
      this.clearPending();
      this.drafts.clear(this.scope().entity);
      this.baseline.set(JSON.stringify(this.model()));
      this.message.set('The journal action is retained. Refreshing current state.');
      this.journal.reload();
    } catch {
      this.uncertain.set(true);
      this.preview.set(null);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  private matches(r: ReturnType<typeof decodeJournalReceipt>, p: PendingRequestReference) {
    return (
      r.journalId === this.id() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  private clearPending() {
    this.drafts.clear(this.scope(true).entity);
    this.pending.set(null);
    this.uncertain.set(false);
    this.checkedAbsent = false;
    this.request = null;
  }
  async reconcile() {
    const p = this.pending(),
      generation = this.generation;
    if (!p || this.busy() || !this.journal.data()) return;
    this.busy.set(true);
    this.preview.set(null);
    this.reviewed.set(false);
    try {
      const lookup = await this.api.get(
        this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
        decodeJournalLookup,
      );
      if (generation !== this.generation) return;
      if (lookup.found && lookup.receipt && this.matches(lookup.receipt, p)) {
        this.receipt.set(lookup.receipt);
        this.message.set(
          'The retained receipt confirms the action. Acknowledge it before another action.',
        );
        this.checkedAbsent = false;
      } else if (!lookup.found && !lookup.receipt) {
        this.checkedAbsent = true;
        this.message.set(
          'No receipt is currently retained. Restore the exact original fields if available, then preview and explicitly confirm an idempotent retry. A changed intent cannot reuse this request.',
        );
      } else throw new Error('Unsupported receipt');
    } catch {
      if (generation === this.generation)
        this.message.set(
          'Receipt verification is unavailable. Keep the reference and check again; do not start another action.',
        );
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  acknowledge() {
    const r = this.receipt(),
      p = this.pending();
    if (!r || !p || !this.matches(r, p)) return;
    this.clearPending();
    this.drafts.clear(this.scope().entity);
    this.model.set(empty());
    this.baseline.set(JSON.stringify(empty()));
    this.journal.reload();
  }
  saveDraft() {
    const ok =
      !this.busy() &&
      !this.uncertain() &&
      this.drafts.save(this.scope(), this.model(), journalFields);
    this.message.set(
      ok
        ? 'Editable fields saved in this tab. Review assent is excluded.'
        : 'The tab draft could not be saved.',
    );
    return ok;
  }
  restoreDraft() {
    if (this.busy()) return;
    const d = this.drafts.read(this.scope(), journalFields);
    if (d.state !== 'ready') {
      this.message.set('The saved fields are stale or unavailable. No fields were restored.');
      return;
    }
    this.model.set(d.draft.value);
    this.draftAvailable.set(false);
    this.preview.set(null);
    this.reviewed.set(false);
  }
  discardDraft() {
    if (this.busy() || this.uncertain()) return false;
    this.model.set(JSON.parse(this.baseline()));
    this.preview.set(null);
    this.reviewed.set(false);
    this.drafts.clear(this.scope().entity);
    return true;
  }
  async refresh() {
    if (this.busy()) return;
    if (!this.uncertain() && this.dirty() && !(await this.confirmNavigation())) return;
    this.preview.set(null);
    this.reviewed.set(false);
    this.historyId.set('');
    this.sourceOpen.set(false);
    this.journal.reload();
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set('Resolve the journal request receipt before leaving an unconfirmed action.');
      return false;
    }
    if (!this.dirty()) return true;
    const decision = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    return decision === 'save'
      ? this.saveDraft()
      : decision === 'discard'
        ? this.discardDraft()
        : false;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  async export() {
    const v = this.journal.data(),
      generation = this.generation;
    if (!v || this.busy() || this.uncertain()) return;
    this.busy.set(true);
    const result = await this.api.download(
      this.base() + '/workspace/instructions',
      { reviewBasis: v.reviewBasis },
      (m) =>
        generation === this.generation &&
        this.journal.data()?.reviewBasis === v.reviewBasis &&
        m.headers['x-journal-id'] === v.journalId &&
        m.headers['x-journal-basis'] === v.reviewBasis &&
        m.headers['x-journal-revision'] === String(v.revision) &&
        m.byteCount > 0 &&
        m.byteCount <= 8 * 1024 * 1024 &&
        m.fileName.endsWith('.csv'),
    );
    if (generation !== this.generation) return;
    this.busy.set(false);
    this.failed.set(!result.ok);
    this.message.set(
      result.ok
        ? 'Instructions downloaded. This is not proof of external posting or package application.'
        : result.message,
    );
  }
  async reconcileReflection() {
    const v = this.journal.data(),
      generation = this.generation;
    if (!v || !v.canReconcileReflection || this.busy() || this.uncertain()) return;
    const state = this.reflectionState();
    const evidence = this.reflectionEvidence().trim();
    if ((state === 'REFLECTED' || state === 'PARTIALLY_REFLECTED') && !evidence) {
      this.message.set('Line-level bridge evidence is required for reflected treatments.');
      this.failed.set(true);
      return;
    }
    this.busy.set(true);
    this.failed.set(false);
    try {
      const result = await this.api.command(this.base() + '/source-reflection', {
        requestId: crypto.randomUUID(),
        reviewBasis: v.reviewBasis,
        state,
        evidence,
      });
      if (generation !== this.generation) return;
      if (!result.ok) {
        this.message.set(result.message);
        this.failed.set(true);
        return;
      }
      this.message.set('Source reflection reconciliation recorded.');
      this.journal.reload();
    } catch (e) {
      if (generation === this.generation) {
        this.message.set(e instanceof Error ? e.message : 'Source reflection reconciliation failed.');
        this.failed.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
}
