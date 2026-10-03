import {
  Component,
  DestroyRef,
  HostListener,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { disabled, form, FormField } from '@angular/forms/signals';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { clientUtcTime } from '../clients/client-contracts';
import { engagementLocation } from './engagement-contracts';
import {
  decodeActivationLookup,
  decodeActivationPreview,
  decodeActivationReceipt,
  decodeActivationState,
} from './activation-contracts';

type Request = {
  requestId: string;
  reviewBasis: string;
  reviewed: boolean;
  expectedRequestHash?: string;
};
@Component({
  selector: 'audit-engagement-activation',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './activation.html',
  styleUrl: './activation.scss',
})
export class EngagementActivationReview implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly route = inject(ActivatedRoute);
  readonly id = routeGuid();
  readonly utcTime = clientUtcTime;
  readonly returnParams = engagementLocation(
    (k) => this.route.snapshot?.queryParamMap?.get(k) ?? null,
  ) ?? { holdPage: 0, holdPageSize: 10 };
  readonly model = signal({ reviewed: false });
  readonly fields = form(this.model, (p) =>
    disabled(p.reviewed, () => this.busy() || this.uncertain()),
  );
  readonly state = this.api.resource(
    () => this.base(),
    (raw, path) => {
      const s = decodeActivationState(raw, path);
      if (s.engagementId !== this.id()) throw new Error('Wrong engagement');
      return s;
    },
  );
  readonly preview = signal<ReturnType<typeof decodeActivationPreview> | null>(null);
  readonly receipt = signal<ReturnType<typeof decodeActivationReceipt> | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly absent = signal(false);
  readonly message = signal('');
  private lifetime = 0;
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
      untracked(() => this.clear());
    });
    effect(() => {
      const s = this.state.data();
      const error = this.state.error();
      untracked(() => {
        this.model.set({ reviewed: false });
        this.preview.set(null);
        this.request = null;
        if (error) {
          this.lifetime++;
          this.busy.set(false);
          this.receipt.set(null);
          return;
        }
        if (!s) return;
        const p = this.drafts.readPendingRequest(this.scope());
        if (p.state === 'ready') {
          this.pending.set(p.draft.value);
          this.uncertain.set(!this.retryRequested);
          this.absent.set(false);
          this.message.set(
            this.retryRequested
              ? 'Fresh review is required before retrying the same activation request.'
              : UNKNOWN_OUTCOME,
          );
          this.retryRequested = false;
        }
      });
    });
    inject(DestroyRef).onDestroy(() => this.clear());
  }
  private clear() {
    this.lifetime++;
    this.retryRequested = false;
    this.model.set({ reviewed: false });
    this.preview.set(null);
    this.receipt.set(null);
    this.pending.set(null);
    this.busy.set(false);
    this.uncertain.set(false);
    this.absent.set(false);
    this.message.set('');
    this.request = null;
  }
  private base() {
    return this.id() ? '/api/ui/engagements/' + this.id() + '/activation-review' : null;
  }
  private scope() {
    return {
      entity: 'activation-request/' + this.id(),
      baseRevision: this.state.data()?.reviewBasis ?? '',
    };
  }
  private matches(r: ReturnType<typeof decodeActivationReceipt>, p: PendingRequestReference) {
    return (
      r.engagementId === this.id() &&
      r.actorId === this.session.current()?.userId &&
      r.clientId === this.state.data()?.clientId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async prepare() {
    const s = this.state.data(),
      g = this.lifetime,
      pending = this.pending();
    if (!s || !s.eligible || this.busy() || this.uncertain()) return;
    const r: Request = {
      requestId: pending?.requestId ?? crypto.randomUUID(),
      reviewBasis: s.reviewBasis,
      reviewed: false,
    };
    this.busy.set(true);
    this.model.set({ reviewed: false });
    this.preview.set(null);
    const result = await this.api.command(this.base() + '/preview', r);
    if (g !== this.lifetime) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      return;
    }
    try {
      const p = decodeActivationPreview(result.value, 'preview');
      if (
        p.engagementId !== this.id() ||
        p.requestId !== r.requestId ||
        p.reviewBasis !== r.reviewBasis ||
        this.state.data()?.reviewBasis !== r.reviewBasis ||
        (pending && p.requestHash !== pending.requestHash)
      )
        throw new Error('Wrong preview');
      this.request = r;
      this.preview.set(p);
      this.message.set('Review this exact engagement and current acceptance before confirming.');
    } catch {
      this.message.set('Review returned an unsupported response. Refresh current context.');
    }
  }
  async execute() {
    const s = this.state.data(),
      p = this.preview(),
      r = this.request,
      g = this.lifetime;
    if (
      !s ||
      !s.eligible ||
      !p ||
      !r ||
      !this.model().reviewed ||
      this.busy() ||
      this.uncertain() ||
      s.reviewBasis !== r.reviewBasis
    )
      return;
    const ref = { requestId: r.requestId, requestHash: p.requestHash };
    if (!this.drafts.save(this.scope(), ref, pendingRequestReference, true)) {
      this.message.set('Recovery storage is unavailable in this tab. No activation was sent.');
      return;
    }
    this.pending.set(ref);
    this.busy.set(true);
    this.model.set({ reviewed: false });
    const result = await this.api.command(this.base()!, {
      ...r,
      reviewed: true,
      expectedRequestHash: p.requestHash,
    });
    if (g !== this.lifetime) return;
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
        this.state.reload();
      }
      return;
    }
    try {
      const value = decodeActivationReceipt(result.value, 'receipt');
      if (!this.matches(value, ref)) throw new Error('Wrong receipt');
      this.receipt.set(value);
      this.clearPending();
      this.message.set('Activation recorded with immutable acceptance and request evidence.');
      this.state.reload();
    } catch {
      this.uncertain.set(true);
      this.absent.set(false);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  private clearPending() {
    this.drafts.clear(this.scope().entity);
    this.pending.set(null);
    this.uncertain.set(false);
    this.absent.set(false);
  }
  async reconcile() {
    const p = this.pending(),
      g = this.lifetime;
    if (!p || !this.state.data() || this.busy()) return;
    this.busy.set(true);
    this.model.set({ reviewed: false });
    try {
      const l = await this.api.get(
        this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
        decodeActivationLookup,
      );
      if (g !== this.lifetime) return;
      if (l.found && l.receipt && this.matches(l.receipt, p)) {
        this.receipt.set(l.receipt);
        this.absent.set(false);
        this.message.set(
          'The retained receipt confirms this activation. Acknowledge the result before continuing.',
        );
      } else if (!l.found) {
        this.absent.set(true);
        this.message.set(
          'No receipt is currently retained. Inspect fresh prerequisites before reviewing the same request again.',
        );
      } else throw new Error('Wrong receipt');
    } catch {
      if (g === this.lifetime)
        this.message.set(
          'Receipt verification is unavailable. Keep the reference and check again.',
        );
    } finally {
      if (g === this.lifetime) this.busy.set(false);
    }
  }
  reviewAgain() {
    if (!this.absent() || this.busy()) return;
    this.retryRequested = true;
    this.uncertain.set(false);
    this.absent.set(false);
    this.preview.set(null);
    this.model.set({ reviewed: false });
    this.state.reload();
  }
  acknowledge() {
    const r = this.receipt(),
      p = this.pending();
    if (!r || !p || !this.matches(r, p) || this.busy()) return;
    this.clearPending();
    this.model.set({ reviewed: false });
    this.state.reload();
  }
  refresh() {
    if (this.busy()) return;
    this.model.set({ reviewed: false });
    this.preview.set(null);
    this.state.reload();
  }
  confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set('Verify the retained activation request before leaving.');
      return false;
    }
    return true;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
}
