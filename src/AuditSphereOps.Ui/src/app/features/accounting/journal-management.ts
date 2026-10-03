import {
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map, firstValueFrom } from 'rxjs';
import { form, FormField, required, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts, pendingRequestReference, PendingRequestReference } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import {
  decodeJournalManagement,
  decodeManagementPreview,
  decodeManagementReceipt,
  decodeManagementLookup,
  managementFields,
  ManagementFields,
} from './journal-management-contracts';
const empty = (): ManagementFields => ({ decision: 'ACCEPTED', reason: '', evidenceReference: '' });
type Intent = ManagementFields & { requestId: string; reviewBasis: string; reviewed: boolean };
@Component({
  selector: 'audit-client-decision-navigation',
  imports: [MatButtonModule, MatDialogModule],
  template: ` <h2 mat-dialog-title>Unsubmitted management response</h2>
    <mat-dialog-content
      ><p>
        Keep editing or discard the unsubmitted response before leaving. Client response fields are
        not stored in this browser tab.
      </p></mat-dialog-content
    >
    <mat-dialog-actions
      ><button matButton (click)="dialog.close('keep')">Keep editing</button
      ><button matButton (click)="dialog.close('discard')">
        Discard response and continue
      </button></mat-dialog-actions
    >`,
})
export class ClientDecisionNavigation {
  readonly dialog = inject(MatDialogRef<ClientDecisionNavigation>);
}
@Component({
  selector: 'audit-journal-management',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './journal-management.html',
  styleUrl: './journal.scss',
})
export class JournalManagement implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly client = toSignal(inject(ActivatedRoute).data.pipe(map((d) => d['client'] === true)), {
    initialValue: false,
  });
  readonly ws = this.api.resource(
    () => (this.id() ? this.base() : null),
    (raw, path) => {
      const v = decodeJournalManagement(raw, path);
      if (v.journalId !== this.id() || v.evidenceMode !== (this.client() ? 'SIGNED_IN' : 'OFFLINE'))
        throw new Error('Wrong management context');
      return v;
    },
  );
  readonly model = signal(empty());
  readonly fields = form(this.model, (p) => {
    required(p.reason);
    maxLength(p.reason, 4000);
    required(p.evidenceReference);
    maxLength(p.evidenceReference, 2000);
  });
  readonly preview = signal<ReturnType<typeof decodeManagementPreview> | null>(null);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly receipt = signal<ReturnType<typeof decodeManagementReceipt> | null>(null);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly draftAvailable = signal(false);
  private generation = 0;
  private request: Intent | null = null;
  private checkedAbsent = false;
  private baseline = signal(JSON.stringify(empty()));
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  constructor() {
    effect(() => {
      this.id();
      this.client();
      this.session.invalidation();
      untracked(() => {
        this.generation++;
        this.model.set(empty());
        this.baseline.set(JSON.stringify(empty()));
        this.preview.set(null);
        this.reviewed.set(false);
        this.busy.set(false);
        this.uncertain.set(false);
        this.pending.set(null);
        this.receipt.set(null);
        this.request = null;
        this.checkedAbsent = false;
        this.draftAvailable.set(false);
        this.message.set('');
        this.failed.set(false);
      });
    });
    effect(() => {
      const v = this.ws.data();
      untracked(() => {
        this.preview.set(null);
        this.reviewed.set(false);
        if (v && !this.client()) {
          this.draftAvailable.set(
            this.drafts.read(this.scope(), managementFields).state === 'ready',
          );
          if (!this.pending()) {
            const saved = this.drafts.readPendingRequest(this.scope(true));
            if (saved.state === 'ready') {
              this.pending.set(saved.draft.value);
              this.uncertain.set(true);
              this.message.set('Check and acknowledge the retained receipt before another action.');
            }
          }
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
    return this.client()
      ? `/api/ui/portal/accounting/journals/${this.id()}`
      : `/api/ui/accounting/journals/${this.id()}/management`;
  }
  private scope(pending = false) {
    return {
      entity: `journal-management${pending ? '-pending' : ''}:${this.id()}`,
      baseRevision: this.ws.data()?.reviewBasis ?? '',
    };
  }
  private intent(id: string, basis: string): Intent {
    return { ...this.model(), requestId: id, reviewBasis: basis, reviewed: false };
  }
  saveDraft() {
    const ok =
      !this.client() &&
      !this.busy() &&
      !this.uncertain() &&
      this.drafts.save(this.scope(), this.model(), managementFields);
    this.message.set(
      ok
        ? 'Editable response fields saved in this tab; assent is excluded.'
        : 'Fields remain in memory only.',
    );
    this.draftAvailable.set(ok);
    return ok;
  }
  restoreDraft() {
    if (this.busy() || this.uncertain() || this.client()) return;
    const r = this.drafts.read(this.scope(), managementFields);
    if (r.state === 'ready' && !r.draft.submissionPending) {
      this.model.set(r.draft.value);
      this.reviewed.set(false);
      this.message.set('Restored fields require a fresh preview and review.');
    }
  }
  discard() {
    if (this.busy() || this.uncertain()) return;
    this.model.set(empty());
    this.baseline.set(JSON.stringify(empty()));
    this.drafts.clear(this.scope().entity);
    this.draftAvailable.set(false);
    this.preview.set(null);
    this.reviewed.set(false);
  }
  refresh() {
    if (!this.busy()) {
      this.preview.set(null);
      this.reviewed.set(false);
      this.ws.reload();
    }
  }
  async prepare() {
    const v = this.ws.data(),
      g = this.generation;
    if (
      !v?.canDecide ||
      this.receipt() ||
      this.busy() ||
      !this.fields().valid() ||
      (this.uncertain() && !this.checkedAbsent)
    )
      return;
    this.preview.set(null);
    this.reviewed.set(false);
    this.busy.set(true);
    try {
      const r = this.intent(this.pending()?.requestId ?? crypto.randomUUID(), v.reviewBasis);
      const result = await this.api.command(this.base() + '/preview', r);
      if (g !== this.generation) return;
      if (JSON.stringify(this.intent(r.requestId, r.reviewBasis)) !== JSON.stringify(r)) return;
      if (!result.ok) {
        this.message.set(result.message);
        this.failed.set(true);
        return;
      }
      const p = decodeManagementPreview(result.value, 'preview');
      if (
        p.reviewBasis !== r.reviewBasis ||
        p.evidenceMode !== v.evidenceMode ||
        p.decision !== r.decision ||
        p.reason !== r.reason.trim() ||
        p.evidenceReference !== r.evidenceReference.trim() ||
        this.ws.data()?.reviewBasis !== r.reviewBasis ||
        (this.pending() && p.requestHash !== this.pending()!.requestHash)
      )
        throw new Error(
          'The reviewed intent changed. Inspect its retained receipt before retrying.',
        );
      this.request = r;
      this.preview.set(p);
      this.message.set('');
      this.failed.set(false);
    } catch (e) {
      if (g === this.generation) {
        this.message.set(e instanceof Error ? e.message : 'Unsupported preview.');
        this.failed.set(true);
      }
    } finally {
      if (g === this.generation) this.busy.set(false);
    }
  }
  private matches(r: ReturnType<typeof decodeManagementReceipt>, p: PendingRequestReference) {
    const a = r.action,
      m = r.management;
    return (
      a.action === 'MANAGEMENT' &&
      a.journalId === this.id() &&
      a.resultJournalId === this.id() &&
      a.oldStatus === 'Draft' &&
      a.newStatus === 'Draft' &&
      a.oldRevision === a.newRevision &&
      a.oldRevision === this.ws.data()?.revision &&
      a.oldRevision === m.journalRevision &&
      a.actorId === this.session.current()?.userId &&
      a.requestId === p.requestId &&
      a.requestHash === p.requestHash &&
      m.evidenceMode === (this.client() ? 'SIGNED_IN' : 'OFFLINE') &&
      (this.client() ? m.decidedByUserId === a.actorId : m.decidedByUserId === null)
    );
  }
  private clearPending() {
    this.drafts.clear(this.scope(true).entity);
    this.pending.set(null);
    this.uncertain.set(false);
    this.checkedAbsent = false;
    this.request = null;
  }
  async execute() {
    const v = this.ws.data(),
      p = this.preview(),
      r = this.request,
      g = this.generation;
    if (
      !v?.canDecide ||
      this.receipt() ||
      !p?.canProceed ||
      !r ||
      !this.reviewed() ||
      this.busy() ||
      r.reviewBasis !== v.reviewBasis ||
      JSON.stringify(this.intent(r.requestId, r.reviewBasis)) !== JSON.stringify(r)
    )
      return;
    this.busy.set(true);
    this.reviewed.set(false);
    const pending = { requestId: r.requestId, requestHash: p.requestHash };
    this.pending.set(pending);
    if (!this.client()) {
      this.drafts.save(this.scope(), this.model(), managementFields, true);
      this.drafts.save(this.scope(true), pending, pendingRequestReference, true);
    }
    const result = await this.api.command(this.base(), { ...r, reviewed: true });
    if (g !== this.generation) return;
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
      const receipt = decodeManagementReceipt(result.value, 'receipt');
      if (
        !this.matches(receipt, pending) ||
        receipt.management.decision !== r.decision ||
        receipt.action.reason !== r.reason.trim() ||
        receipt.management.evidenceReference !== r.evidenceReference.trim()
      )
        throw new Error('Unconfirmed response');
      this.receipt.set(receipt);
      this.clearPending();
      this.baseline.set(JSON.stringify(this.model()));
      this.drafts.clear(this.scope().entity);
      this.message.set(
        'The management disposition is retained for this exact revision. Technical posting and application remain separate.',
      );
      this.failed.set(false);
      this.ws.reload();
    } catch {
      this.uncertain.set(true);
      this.message.set(UNKNOWN_OUTCOME);
      this.failed.set(true);
    }
  }
  async reconcile() {
    const p = this.pending(),
      g = this.generation;
    if (!p || this.busy() || !this.ws.data()) return;
    this.busy.set(true);
    try {
      const r = await this.api.get(
        this.base() + `/receipts/${p.requestId}?requestHash=${p.requestHash}`,
        decodeManagementLookup,
      );
      if (g !== this.generation) return;
      if (r.found && r.receipt && this.matches(r.receipt, p)) {
        this.receipt.set(r.receipt);
        this.message.set('Acknowledge this retained response before continuing.');
        this.failed.set(false);
      } else if (!r.found && !r.receipt) {
        this.checkedAbsent = true;
        this.message.set(
          'No receipt found. Only a fresh preview of the identical intent may retry this request.',
        );
      } else throw new Error('Receipt does not match this response.');
    } catch (e) {
      if (g === this.generation) {
        this.message.set(e instanceof Error ? e.message : 'Receipt unavailable.');
        this.failed.set(true);
      }
    } finally {
      if (g === this.generation) this.busy.set(false);
    }
  }
  acknowledge() {
    if (
      this.busy() ||
      !this.receipt() ||
      !this.pending() ||
      !this.matches(this.receipt()!, this.pending()!)
    )
      return;
    this.clearPending();
    this.baseline.set(JSON.stringify(this.model()));
    this.drafts.clear(this.scope().entity);
    this.ws.reload();
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set('Resolve the pending management response before leaving.');
      return false;
    }
    if (!this.dirty()) return true;
    const dialog = this.client()
      ? this.dialog.open(ClientDecisionNavigation)
      : this.dialog.open(UnsavedChangesDialog);
    const choice = await firstValueFrom(dialog.afterClosed());
    if (choice === 'save') return this.saveDraft();
    if (choice === 'discard') {
      this.discard();
      return true;
    }
    return false;
  }
}
