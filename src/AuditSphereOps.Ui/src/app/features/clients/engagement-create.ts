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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { disabled, form, FormField, maxLength, readonly, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import {
  EngagementFields,
  engagementFields,
  decodeEngagementCreationLookup,
  decodeEngagementCreationPreview,
  decodeEngagementCreationReceipt,
  decodeEngagementCreationState,
  emptyEngagement,
  validEngagement,
} from './engagement-create-contracts';
import { clientLocation, clientUtcTime, defaultClientLocation } from './client-contracts';
import { PortfolioNavigation } from '../portfolio/portfolio-contracts';

type Request = {
  requestId: string;
  reviewBasis: string;
  fields: EngagementFields;
  reviewed: boolean;
  expectedRequestHash?: string;
};
@Component({
  selector: 'audit-engagement-create',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './engagement-create.html',
  styleUrl: './engagement-create.scss',
})
export class EngagementCreate implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly utcTime = clientUtcTime;
  readonly portfolioNavigation = inject(PortfolioNavigation);
  private readonly route = inject(ActivatedRoute);
  readonly returnParams =
    clientLocation((k) => this.route.snapshot?.queryParamMap?.get(k) ?? null) ??
    defaultClientLocation;
  readonly model = signal<EngagementFields>(emptyEngagement());
  readonly fields = form(this.model, (p) => {
    required(p.serviceRoute);
    maxLength(p.serviceRoute, 50);
    required(p.serviceProfile);
    maxLength(p.serviceProfile, 100);
    required(p.periodStart);
    required(p.periodEnd);
    readonly(p.serviceRoute, () => this.locked());
    readonly(p.serviceProfile, () => this.locked());
    readonly(p.periodStart, () => this.locked());
    readonly(p.periodEnd, () => this.locked());
  });
  readonly state = this.api.resource(
    () => this.base(),
    (raw, path) => {
      const s = decodeEngagementCreationState(raw, path);
      if (s.clientId !== this.id()) throw new Error('Wrong client');
      return s;
    },
  );
  readonly preview = signal<ReturnType<typeof decodeEngagementCreationPreview> | null>(null);
  readonly receipt = signal<ReturnType<typeof decodeEngagementCreationReceipt> | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly absent = signal(false);
  readonly message = signal('');
  readonly draftAvailable = signal(false);
  readonly valid = computed(() => this.fields().valid() && validEngagement(this.model()));
  readonly locked = computed(() => this.busy() || (this.uncertain() && !this.absent()));
  private readonly baseline = signal(JSON.stringify(emptyEngagement()));
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  private generation = 0;
  private request: Request | null = null;
  private retryRequested = false;
  constructor() {
    effect(() => {
      this.id();
      this.session.invalidation();
      const s = this.session.current();
      s?.firmId;
      s?.userId;
      s?.generation;
      untracked(() => {
        this.generation++;
        this.model.set(emptyEngagement());
        this.baseline.set(JSON.stringify(emptyEngagement()));
        this.preview.set(null);
        this.receipt.set(null);
        this.reviewed.set(false);
        this.pending.set(null);
        this.busy.set(false);
        this.uncertain.set(false);
        this.absent.set(false);
        this.retryRequested = false;
        this.message.set('');
        this.draftAvailable.set(false);
        this.request = null;
      });
    });
    effect(() => {
      const s = this.state.data();
      if (!s) return;
      untracked(() => {
        this.preview.set(null);
        this.reviewed.set(false);
        this.request = null;
        this.draftAvailable.set(this.drafts.read(this.scope(), engagementFields).state === 'ready');
        const p = this.drafts.readPendingRequest(this.scope(true));
        if (p.state === 'ready' && !this.retryRequested) {
          this.pending.set(p.draft.value);
          this.uncertain.set(true);
          this.absent.set(false);
          this.message.set(UNKNOWN_OUTCOME);
        }
      });
    });
    effect(() => {
      const error = this.state.error();
      if (!error) return;
      untracked(() => {
        this.generation++;
        this.model.set(emptyEngagement());
        this.baseline.set(JSON.stringify(emptyEngagement()));
        this.preview.set(null);
        this.receipt.set(null);
        this.reviewed.set(false);
        this.pending.set(null);
        this.busy.set(false);
        this.uncertain.set(false);
        this.absent.set(false);
        this.draftAvailable.set(false);
        this.request = null;
        this.retryRequested = false;
      });
    });
    effect(() => {
      this.model();
      untracked(() => {
        this.preview.set(null);
        this.reviewed.set(false);
        this.request = null;
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.generation++;
    });
  }
  private base() {
    return this.id() ? '/api/ui/clients/' + this.id() + '/engagement-creation' : null;
  }
  private scope(pending = false) {
    return {
      entity:
        (pending ? 'engagement-creation-request/' : 'engagement-creation-fields/') + this.id(),
      baseRevision: this.state.data()?.reviewBasis ?? '',
    };
  }
  private matches(
    r: ReturnType<typeof decodeEngagementCreationReceipt>,
    p: PendingRequestReference,
  ) {
    return (
      r.clientId === this.id() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async prepare() {
    const s = this.state.data(),
      g = this.generation,
      pending = this.pending();
    if (!s || !this.valid() || this.locked()) return;
    const r: Request = {
      requestId: pending?.requestId ?? crypto.randomUUID(),
      reviewBasis: s.reviewBasis,
      fields: structuredClone(this.model()),
      reviewed: false,
    };
    this.busy.set(true);
    this.preview.set(null);
    this.reviewed.set(false);
    try {
      const result = await this.api.command(this.base() + '/preview', r);
      if (g !== this.generation) return;
      if (!result.ok) {
        this.message.set(result.message);
        if (result.status === 401 || result.status === 403) this.revalidateProtectedContext();
        return;
      }
      const p = decodeEngagementCreationPreview(result.value, 'preview');
      if (
        p.clientId !== this.id() ||
        p.requestId !== r.requestId ||
        p.reviewBasis !== r.reviewBasis ||
        this.state.data()?.reviewBasis !== r.reviewBasis ||
        JSON.stringify(p.fields) !==
          JSON.stringify({
            serviceRoute: r.fields.serviceRoute.trim(),
            serviceProfile: r.fields.serviceProfile.trim(),
            periodStart: r.fields.periodStart.trim(),
            periodEnd: r.fields.periodEnd.trim(),
          }) ||
        (pending && p.requestHash !== pending.requestHash)
      )
        throw new Error(
          'The exact engagement intent changed. Restore its original fields or verify the retained receipt.',
        );
      this.request = r;
      this.preview.set(p);
      this.message.set(
        p.existingEngagementId
          ? 'A matching service-period engagement already exists. Inspect it before continuing.'
          : 'Review the exact service, profile and period. This creates a blocked draft only.',
      );
    } catch {
      if (g === this.generation)
        this.message.set(
          'Review unavailable or the exact intent changed. Restore the original fields or verify the retained receipt.',
        );
    } finally {
      if (g === this.generation) this.busy.set(false);
    }
  }
  async execute() {
    const s = this.state.data(),
      p = this.preview(),
      r = this.request,
      g = this.generation;
    if (
      !s ||
      !p ||
      !r ||
      p.existingEngagementId !== null ||
      !this.reviewed() ||
      this.busy() ||
      s.reviewBasis !== r.reviewBasis ||
      JSON.stringify(this.model()) !== JSON.stringify(r.fields)
    )
      return;
    const ref = { requestId: r.requestId, requestHash: p.requestHash };
    if (
      !this.drafts.save(this.scope(), this.model(), engagementFields, true) ||
      !this.drafts.save(this.scope(true), ref, pendingRequestReference, true)
    ) {
      this.message.set('Recovery storage is unavailable in this tab. No engagement was sent.');
      return;
    }
    this.retryRequested = false;
    this.pending.set(ref);
    this.busy.set(true);
    this.reviewed.set(false);
    const result = await this.api.command(this.base()!, {
      ...r,
      reviewed: true,
      expectedRequestHash: p.requestHash,
    });
    if (g !== this.generation) return;
    this.busy.set(false);
    this.preview.set(null);
    this.request = null;
    if (!result.ok) {
      this.message.set(result.message);
      if (result.unknown) {
        this.uncertain.set(true);
        this.absent.set(false);
      } else {
        this.clearPending();
        if (result.status === 401 || result.status === 403) this.revalidateProtectedContext();
      }
      return;
    }
    try {
      const receipt = decodeEngagementCreationReceipt(result.value, 'receipt');
      if (
        !this.matches(receipt, ref) ||
        receipt.reviewBasis !== r.reviewBasis ||
        receipt.clientGeneration !== s.clientGeneration ||
        JSON.stringify(receipt.fields) !== JSON.stringify(p.fields)
      )
        throw new Error('Wrong receipt');
      this.receipt.set(receipt);
      this.clearPending();
      this.drafts.clear(this.scope().entity);
      this.model.set(emptyEngagement());
      this.baseline.set(JSON.stringify(emptyEngagement()));
      this.message.set(
        'Blocked engagement recorded with an immutable receipt. Partner activation remains separate.',
      );
      this.state.reload();
    } catch {
      this.uncertain.set(true);
      this.absent.set(false);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  private revalidateProtectedContext() {
    this.generation++;
    this.drafts.clear(this.scope().entity);
    this.model.set(emptyEngagement());
    this.baseline.set(JSON.stringify(emptyEngagement()));
    this.preview.set(null);
    this.receipt.set(null);
    this.reviewed.set(false);
    this.request = null;
    this.busy.set(false);
    this.absent.set(false);
    this.retryRequested = false;
    this.state.reload();
  }
  private clearPending() {
    this.drafts.clear(this.scope(true).entity);
    this.pending.set(null);
    this.uncertain.set(false);
    this.absent.set(false);
    this.request = null;
    this.retryRequested = false;
  }
  async reconcile() {
    const p = this.pending(),
      g = this.generation;
    if (!p || !this.state.data() || this.busy()) return;
    this.busy.set(true);
    this.preview.set(null);
    this.reviewed.set(false);
    try {
      const lookup = await this.api.get(
        this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
        decodeEngagementCreationLookup,
      );
      if (g !== this.generation) return;
      if (lookup.found && lookup.receipt && this.matches(lookup.receipt, p)) {
        this.receipt.set(lookup.receipt);
        this.absent.set(false);
        this.message.set(
          'The retained receipt confirms this blocked engagement. Acknowledge the result before continuing.',
        );
      } else if (!lookup.found) {
        this.retryRequested = true;
        this.absent.set(true);
        this.message.set(
          'No receipt is currently retained. Restore or enter the identical original fields, then obtain fresh review to retry the same request. Changed fields cannot reuse it.',
        );
      } else throw new Error('Wrong receipt');
    } catch {
      if (g === this.generation)
        this.message.set(
          'Receipt verification is unavailable. Keep the reference and check again.',
        );
    } finally {
      if (g === this.generation) this.busy.set(false);
    }
  }
  acknowledge() {
    const r = this.receipt(),
      p = this.pending();
    if (!r || !p || !this.matches(r, p)) return;
    this.clearPending();
    this.drafts.clear(this.scope().entity);
    this.model.set(emptyEngagement());
    this.baseline.set(JSON.stringify(emptyEngagement()));
    this.state.reload();
  }
  saveDraft() {
    const ok =
      !this.busy() &&
      !this.uncertain() &&
      this.drafts.save(this.scope(), this.model(), engagementFields);
    this.message.set(
      ok
        ? 'Editable fields saved in this tab for four hours. Review assent is excluded.'
        : 'Draft unavailable.',
    );
    return ok;
  }
  restoreDraft() {
    if (this.locked()) return;
    const d = this.drafts.read(this.scope(), engagementFields);
    if (d.state !== 'ready') {
      this.message.set('Saved fields are stale or unavailable.');
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
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set('Verify the retained engagement creation request before leaving.');
      return false;
    }
    if (!this.dirty()) return true;
    const d = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    return d === 'save' ? this.saveDraft() : d === 'discard' ? this.discardDraft() : false;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.dirty() || this.busy() || this.uncertain()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  async refresh() {
    if (this.busy()) return;
    if (!this.uncertain() && this.dirty() && !(await this.confirmNavigation())) return;
    this.preview.set(null);
    this.reviewed.set(false);
    this.state.reload();
  }
}
