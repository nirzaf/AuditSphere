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
import { guid } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { TrialBalanceSource } from '../engagements/tb-source';
import {
  decodeJournalPreview,
  decodeJournalReceipt,
  decodeJournalLookup,
} from './journal-contracts';
import {
  decodeJournalCreation,
  JournalCreationFields,
  journalCreationFields,
} from './journal-creation-contracts';

const empty = (): JournalCreationFields => ({
  journalNumber: '',
  purpose: 'REPORTING_ADJUSTMENT',
  origin: 'AUDIT_PROPOSED',
  reason: '',
  evidenceReference: '',
  supersedesId: '',
  supersedesRevision: '',
  lines: [
    { accountCode: '', debit: '0', credit: '0' },
    { accountCode: '', debit: '0', credit: '0' },
  ],
});
type Intent = Omit<JournalCreationFields, 'supersedesId' | 'supersedesRevision'> & {
  requestId: string;
  reviewBasis: string;
  supersedesId: string | null;
  supersedesRevision: number | null;
  reviewed: boolean;
};
@Component({
  selector: 'audit-journal-create',
  imports: [RouterLink, FormField, MatButtonModule, TrialBalanceSource, ...SHARED],
  templateUrl: './journal-create.html',
  styleUrl: './journal.scss',
})
export class JournalCreate implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly context = this.api.resource(
    () => (this.id() ? this.base() : null),
    (raw, path) => {
      const c = decodeJournalCreation(raw, path);
      if (c.datasetId !== this.id()) throw new Error('Wrong source');
      return c;
    },
  );
  readonly model = signal(empty());
  readonly fields = form(this.model, (p) => {
    required(p.journalNumber);
    maxLength(p.journalNumber, 32);
    required(p.reason);
    maxLength(p.reason, 4000);
    required(p.evidenceReference);
    maxLength(p.evidenceReference, 2000);
    maxLength(p.supersedesId, 36);
    maxLength(p.supersedesRevision, 12);
  });
  readonly preview = signal<ReturnType<typeof decodeJournalPreview> | null>(null);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly receipt = signal<ReturnType<typeof decodeJournalReceipt> | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly draftAvailable = signal(false);
  readonly sourceOpen = signal(false);
  private readonly baseline = signal(JSON.stringify(empty()));
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  private generation = 0;
  private request: Intent | null = null;
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
        this.busy.set(false);
        this.uncertain.set(false);
        this.receipt.set(null);
        this.pending.set(null);
        this.request = null;
        this.checkedAbsent = false;
        this.sourceOpen.set(false);
        this.draftAvailable.set(false);
        this.message.set('');
        this.failed.set(false);
      });
    });
    effect(() => {
      const c = this.context.data();
      if (!c) return;
      untracked(() => {
        this.preview.set(null);
        this.reviewed.set(false);
        this.sourceOpen.set(false);
        this.draftAvailable.set(
          this.drafts.read(this.scope(), journalCreationFields).state === 'ready',
        );
        const p = this.drafts.readPendingRequest(this.scope(true));
        if (p.state === 'ready') {
          this.pending.set(p.draft.value);
          this.uncertain.set(true);
          this.message.set(UNKNOWN_OUTCOME);
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
    return '/api/ui/datasets/' + this.id() + '/journal-drafts';
  }
  private scope(pending = false) {
    return {
      entity: (pending ? 'journal-create-request/' : 'journal-create-fields/') + this.id(),
      baseRevision: this.context.data()?.reviewBasis ?? '',
    };
  }
  private intent(requestId: string, reviewBasis: string): Intent {
    const m = structuredClone(this.model());
    let supersedesId: string | null = null,
      supersedesRevision: number | null = null;
    if (m.supersedesId || m.supersedesRevision) {
      supersedesId = guid(m.supersedesId, 'supersedesId');
      supersedesRevision = Number(m.supersedesRevision);
      if (
        !/^[1-9][0-9]{0,11}$/.test(m.supersedesRevision) ||
        !Number.isSafeInteger(supersedesRevision)
      )
        throw new Error('Enter the exact prior journal identity and positive revision together.');
    }
    return { ...m, supersedesId, supersedesRevision, requestId, reviewBasis, reviewed: false };
  }
  editLine(i: number, key: 'accountCode' | 'debit' | 'credit', e: Event) {
    if (!this.busy() && !this.uncertain())
      this.model.update((m) => ({
        ...m,
        lines: m.lines.map((l, n) =>
          n === i ? { ...l, [key]: (e.target as HTMLInputElement).value } : l,
        ),
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
    const c = this.context.data(),
      generation = this.generation;
    if (
      !c?.canCreate ||
      this.busy() ||
      this.receipt() ||
      !this.fields().valid() ||
      (this.uncertain() && !this.checkedAbsent)
    )
      return;
    this.preview.set(null);
    this.reviewed.set(false);
    this.failed.set(false);
    try {
      const r = this.intent(this.pending()?.requestId ?? crypto.randomUUID(), c.reviewBasis);
      this.busy.set(true);
      const result = await this.api.command(this.base() + '/preview', r);
      if (generation !== this.generation) return;
      if (!result.ok) {
        this.message.set(result.message);
        this.failed.set(true);
        return;
      }
      const p = decodeJournalPreview(result.value);
      if (
        p.action !== 'CREATE' ||
        p.reviewBasis !== r.reviewBasis ||
        this.context.data()?.reviewBasis !== r.reviewBasis ||
        (this.pending() && p.requestHash !== this.pending()!.requestHash)
      )
        throw new Error(
          'The pending intent or source review changed. Keep its reference and inspect the receipt.',
        );
      this.request = r;
      this.preview.set(p);
      this.message.set('');
    } catch (e) {
      if (generation === this.generation) {
        this.message.set(e instanceof Error ? e.message : 'Unsupported creation preview.');
        this.failed.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async execute() {
    const c = this.context.data(),
      p = this.preview(),
      r = this.request,
      generation = this.generation;
    if (
      !c?.canCreate ||
      !p?.canProceed ||
      !r ||
      !this.reviewed() ||
      this.busy() ||
      this.receipt() ||
      r.reviewBasis !== c.reviewBasis
    )
      return;
    try {
      if (JSON.stringify(this.intent(r.requestId, r.reviewBasis)) !== JSON.stringify(r)) return;
    } catch {
      return;
    }
    this.busy.set(true);
    this.reviewed.set(false);
    this.failed.set(false);
    const pending = { requestId: r.requestId, requestHash: p.requestHash };
    this.pending.set(pending);
    this.drafts.save(this.scope(), this.model(), journalCreationFields, true);
    this.drafts.save(this.scope(true), pending, pendingRequestReference, true);
    const result = await this.api.command(this.base(), { ...r, reviewed: true });
    if (generation !== this.generation) return;
    this.busy.set(false);
    this.preview.set(null);
    if (!result.ok) {
      this.message.set(result.message);
      this.failed.set(true);
      if (result.unknown) {
        this.uncertain.set(true);
        this.checkedAbsent = false;
      } else this.clearPending();
      return;
    }
    try {
      const receipt = decodeJournalReceipt(result.value, 'receipt');
      if (!this.matches(receipt, pending)) throw new Error('Unconfirmed creation');
      this.receipt.set(receipt);
      this.clearPending();
      this.drafts.clear(this.scope().entity);
      this.baseline.set(JSON.stringify(this.model()));
      this.message.set(
        'The new draft and its original exact lines are retained. Independent review and application are separate steps.',
      );
    } catch {
      this.uncertain.set(true);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  private matches(r: ReturnType<typeof decodeJournalReceipt>, p: PendingRequestReference) {
    return (
      r.action === 'CREATE' &&
      r.oldStatus === 'NOT_CREATED' &&
      r.oldRevision === 0 &&
      r.newStatus === 'Draft' &&
      r.newRevision === 1 &&
      r.journalId === r.resultJournalId &&
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
    if (!p || this.busy() || !this.context.data()) return;
    this.busy.set(true);
    this.preview.set(null);
    this.reviewed.set(false);
    try {
      const l = await this.api.get(
        this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
        decodeJournalLookup,
      );
      if (generation !== this.generation) return;
      if (l.found && l.receipt && this.matches(l.receipt, p)) {
        this.receipt.set(l.receipt);
        this.checkedAbsent = false;
        this.message.set(
          'The retained creation receipt confirms the draft. Acknowledge it before leaving.',
        );
      } else if (!l.found && !l.receipt) {
        this.checkedAbsent = true;
        this.message.set(
          'No receipt is currently retained. Restore the exact original fields, preview and explicitly confirm only the identical request. A changed intent cannot reuse it.',
        );
      } else throw new Error('Unsupported creation receipt');
    } catch {
      if (generation === this.generation)
        this.message.set(
          'Receipt verification is unavailable. Keep the request reference and check again before another creation.',
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
    this.baseline.set(JSON.stringify(this.model()));
  }
  saveDraft() {
    const ok =
      !this.busy() &&
      !this.uncertain() &&
      !this.receipt() &&
      this.drafts.save(this.scope(), this.model(), journalCreationFields);
    this.message.set(
      ok
        ? 'Editable journal fields saved in this tab. Review assent is excluded.'
        : 'The tab draft could not be saved.',
    );
    return ok;
  }
  restoreDraft() {
    if (this.busy() || this.receipt()) return;
    const d = this.drafts.read(this.scope(), journalCreationFields);
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
    if (this.busy() || this.uncertain() || this.receipt()) return false;
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
    this.sourceOpen.set(false);
    this.context.reload();
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set('Resolve the creation receipt before leaving an unconfirmed request.');
      return false;
    }
    if (!this.dirty()) return true;
    const d = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    return d === 'save' ? this.saveDraft() : d === 'discard' ? this.discardDraft() : false;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
}
