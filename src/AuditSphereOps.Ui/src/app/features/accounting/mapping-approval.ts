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
import { disabled, form, FormField } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import {
  bool,
  date,
  guid,
  instant,
  nat,
  nullable,
  obj,
  sha256,
  str,
  decode,
} from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';

const shape = obj({
  mappingId: guid,
  clientId: guid,
  engagementId: guid,
  datasetId: guid,
  version: nat,
  status: str(30),
  mappingGeneration: nat,
  inputGeneration: nat,
  taxonomyVersion: str(100),
  chartVersionId: nullable(guid),
  periodStart: date,
  periodEnd: date,
  sourceAccountCount: nat,
  allocationCount: nat,
  preparerId: guid,
  reviewerId: nullable(guid),
  reviewedAt: nullable(instant),
  revision: sha256,
  canApprove: bool,
  blocker: nullable(str(2000)),
});
export function decodeMappingApproval(raw: unknown, path = 'response') {
  const p = shape(raw, path);
  if (
    p.version < 1 ||
    p.sourceAccountCount > 20000 ||
    p.allocationCount > 5000 ||
    (p.canApprove &&
      (p.status !== 'DRAFT' ||
        p.blocker ||
        p.reviewerId ||
        p.reviewedAt ||
        p.mappingGeneration !== p.inputGeneration ||
        p.allocationCount < 1)) ||
    (!p.canApprove && !p.blocker) ||
    (p.status === 'APPROVED' && (!p.reviewerId || !p.reviewedAt))
  )
    throw new Error('Unsupported mapping review state');
  return p;
}
export function mappingReviewCheckpoint(raw: unknown): { mappingId: string } | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const p = raw as Record<string, unknown>;
  return Object.keys(p).length === 1 &&
    typeof p['mappingId'] === 'string' &&
    /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(p['mappingId'])
    ? { mappingId: p['mappingId'] }
    : null;
}

@Component({
  selector: 'audit-mapping-approval',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './mapping-approval.html',
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      p,
      dd,
      code {
        overflow-wrap: anywhere;
      }
      dd {
        margin: 0;
      }
      dl {
        display: grid;
        grid-template-columns: minmax(100px, 180px) minmax(0, 1fr);
        gap: 0.6rem;
      }
      label {
        display: block;
        margin-block: 1rem;
      }
      .actions {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
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
export class MappingApproval implements NavigationProtected {
  readonly id = routeGuid();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly review = this.api.resource(
    () => (this.id() ? this.url() : null),
    (raw) => {
      const p = decodeMappingApproval(raw);
      if (p.mappingId !== this.id()) throw new Error('Wrong mapping');
      return p;
    },
  );
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciled = signal(false);
  readonly message = signal('');
  readonly resetGeneration = signal(0);
  readonly model = signal({ reviewed: false });
  readonly canApprove = computed(
    () => !!this.review.data()?.canApprove && !this.busy() && !this.uncertain(),
  );
  readonly fields = form(this.model, (p) => disabled(p.reviewed, () => !this.canApprove()));
  private assentRevision = '';
  private readRequested = false;
  private request = 0;
  private destroyed = false;
  private context = '';
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
        this.busy.set(false);
        this.uncertain.set(false);
        this.reconciled.set(false);
        this.readRequested = false;
        this.message.set('');
        this.clearAssent();
      });
    });
    let previous = '';
    effect(() => {
      const p = this.review.data();
      if (!p) {
        untracked(() => this.clearAssent());
        return;
      }
      if (p.revision === previous && !this.readRequested) return;
      previous = p.revision;
      untracked(() => {
        this.clearAssent();
        const saved = this.drafts.read(
          { entity: this.entity(), baseRevision: p.revision },
          mappingReviewCheckpoint,
        );
        if (saved.state === 'stale' || (saved.state === 'ready' && saved.draft.submissionPending)) {
          this.uncertain.set(true);
          this.message.set(
            'An earlier review checkpoint needs persisted-state verification. No approval will be repeated automatically.',
          );
        }
        if (this.readRequested && this.uncertain()) this.reconciled.set(true);
        this.readRequested = false;
      });
    });
  }
  private url() {
    return `/api/ui/accounting/mappings/${this.id()}/approval`;
  }
  private entity() {
    return `mapping-approval/${this.id()}`;
  }
  private clearAssent() {
    this.fields.reviewed().reset(false);
    this.assentRevision = '';
    this.resetGeneration.update((n) => n + 1);
  }
  assent(e: Event) {
    this.assentRevision = (e.target as HTMLInputElement).checked
      ? (this.review.data()?.revision ?? '')
      : '';
  }
  refresh() {
    if (this.busy()) return;
    this.clearAssent();
    this.reconciled.set(false);
    this.readRequested = true;
    this.review.reload();
  }
  acknowledge() {
    if (!this.reconciled() || !this.review.data() || this.busy()) return;
    if (!this.drafts.clear(this.entity())) {
      this.message.set('Cannot clear the checkpoint. Keep the write fence and retry.');
      return;
    }
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.clearAssent();
    this.message.set('Persisted mapping reviewed. Any permitted new action requires fresh assent.');
  }
  async approve() {
    const p = this.review.data(),
      s = this.session.current();
    if (
      !p ||
      !s ||
      !this.canApprove() ||
      !this.model().reviewed ||
      this.assentRevision !== p.revision
    )
      return;
    if (
      !this.drafts.save(
        { entity: this.entity(), baseRevision: p.revision },
        { mappingId: p.mappingId },
        mappingReviewCheckpoint,
        true,
      )
    ) {
      this.message.set('Tab storage is unavailable. No approval was sent.');
      return;
    }
    const n = ++this.request,
      g = this.session.invalidation();
    this.busy.set(true);
    this.clearAssent();
    this.message.set('');
    const result = await this.api.command(this.url(), { revision: p.revision, reviewed: true });
    if (
      this.destroyed ||
      this.request !== n ||
      this.session.invalidation() !== g ||
      this.id() !== p.mappingId
    )
      return;
    this.busy.set(false);
    if (!result.ok) {
      this.uncertain.set(result.unknown);
      if (!result.unknown) this.drafts.clear(this.entity());
      this.message.set(result.message);
      this.review.reload();
      return;
    }
    try {
      const value = decode(decodeMappingApproval, result.value);
      if (
        value.mappingId !== p.mappingId ||
        value.datasetId !== p.datasetId ||
        value.clientId !== p.clientId ||
        value.engagementId !== p.engagementId ||
        value.status !== 'APPROVED' ||
        value.reviewerId !== s.userId ||
        !this.drafts.clear(this.entity())
      )
        throw new Error('Wrong retained review');
      this.message.set('Independent approval retained for this exact mapping version.');
      this.review.reload();
    } catch {
      this.uncertain.set(true);
      this.reconciled.set(false);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set(
        'Read and acknowledge the persisted mapping before leaving an unconfirmed approval.',
      );
      return false;
    }
    // This read-only review contains no editable business fields. Assent is never a draft.
    this.clearAssent();
    return true;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
}
