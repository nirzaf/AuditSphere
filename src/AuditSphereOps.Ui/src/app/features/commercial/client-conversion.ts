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
import { RouterLink } from '@angular/router';
import { form, FormField, required, maxLength, readonly, validate } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import {
  ClientConversionFields,
  ClientConversionPreview,
  ClientConversionReceipt,
  normalizedClientConversion,
  validClientConversion,
  decodeClientConversionState,
  decodeClientConversionPreview,
  decodeClientConversionReceipt,
  decodeClientConversionLookup,
} from './client-conversion-contracts';

type Editable = { [K in keyof ClientConversionFields]: string };
const empty = (): Editable => ({
  legalName: '',
  commercialName: '',
  registrationNumber: '',
  jurisdiction: '',
  restrictedProfile: '',
});
function editable(raw: unknown): Editable | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const f = raw as Editable;
  if (
    Object.keys(f).sort().join(',') !==
      'commercialName,jurisdiction,legalName,registrationNumber,restrictedProfile' ||
    f.restrictedProfile !== ''
  )
    return null;
  for (const [k, n] of [
    ['legalName', 200],
    ['commercialName', 200],
    ['registrationNumber', 100],
    ['jurisdiction', 100],
  ] as const)
    if (typeof f[k] !== 'string' || f[k].length > n || /[\x00-\x1f\x7f]/.test(f[k])) return null;
  return structuredClone(f);
}
@Component({
  selector: 'audit-client-conversion',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './client-conversion.html',
  styleUrl: './client-conversion.scss',
})
export class ClientConversion implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly model = signal<Editable>(empty());
  readonly baseline = signal(JSON.stringify(empty()));
  readonly state = this.api.resource(
    () => this.base(),
    (raw) => {
      const s = decodeClientConversionState(raw);
      if (s.proposalId !== this.id()) throw new Error('Wrong proposal');
      return s;
    },
  );
  readonly preview = signal<ClientConversionPreview | null>(null);
  readonly receipt = signal<ClientConversionReceipt | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  readonly draftAvailable = signal(false);
  private generation = 0;
  private destroyed = false;
  readonly confirmation = signal({ reviewed: false });
  private readonly assent = signal('');
  readonly locked = computed(
    () => this.busy() || this.uncertain() || !!this.preview() || !this.state.data()?.canConvert,
  );
  readonly dirty = computed(
    () => JSON.stringify(this.model()) !== this.baseline() || !!this.preview(),
  );
  readonly fields = form(this.model, (p) => {
    required(p.legalName);
    maxLength(p.legalName, 200);
    maxLength(p.commercialName, 200);
    maxLength(p.registrationNumber, 100);
    maxLength(p.jurisdiction, 100);
    maxLength(p.restrictedProfile, 2000);
    for (const field of [
      p.legalName,
      p.commercialName,
      p.registrationNumber,
      p.jurisdiction,
      p.restrictedProfile,
    ])
      readonly(field, () => this.locked());
  });
  readonly confirmationFields = form(this.confirmation, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    readonly(p.reviewed, () => this.busy() || this.uncertain() || !this.preview());
  });
  readonly valid = computed(
    () => this.fields().valid() && validClientConversion(normalizedClientConversion(this.model())),
  );
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
        this.model.set(empty());
        this.baseline.set(JSON.stringify(empty()));
        this.preview.set(null);
        this.receipt.set(null);
        this.pending.set(null);
        this.reviewed = false;
        this.busy.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.draftAvailable.set(false);
      });
    });
    effect(() => {
      const s = this.state.data(),
        error = this.state.error();
      if (error) {
        untracked(() => this.clearProtected());
        return;
      }
      if (!s) return;
      untracked(() => {
        this.preview.set(null);
        this.reviewed = false;
        this.draftAvailable.set(
          s.canConvert && this.drafts.read(this.scope(), editable).state === 'ready',
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
        this.reviewed = false;
      });
    });
    effect(() => {
      const changed = this.confirmation().reviewed && !this.reviewed;
      untracked(() => {
        if (changed) {
          this.reviewed = false;
          this.confirmationFields.reviewed().reset(false);
        }
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.generation++;
    });
  }
  private base() {
    return this.id() ? '/api/ui/proposals/' + this.id() + '/client-conversion' : null;
  }
  private owner() {
    const s = this.session.current();
    return s?.staff
      ? JSON.stringify([
          s.firmId,
          s.userId,
          s.generation,
          this.session.invalidation(),
          this.id(),
          this.generation,
        ])
      : '';
  }
  private intent() {
    return this.owner() && this.preview() && this.state.data()?.canConvert
      ? JSON.stringify([this.owner(), this.preview(), this.model(), this.state.data()?.reviewBasis])
      : '';
  }
  get reviewed() {
    const i = this.intent();
    return !!i && this.confirmation().reviewed && this.assent() === i;
  }
  set reviewed(value: boolean) {
    this.confirmation.set({ reviewed: value });
    this.assent.set(value ? this.intent() : '');
  }
  private scope(pending = false) {
    return {
      entity: (pending ? 'client-conversion-request/' : 'client-conversion-fields/') + this.id(),
      baseRevision: this.state.data()?.reviewBasis ?? '0'.repeat(64),
    };
  }
  private clearProtected() {
    this.generation++;
    this.model.set(empty());
    this.baseline.set(JSON.stringify(empty()));
    this.preview.set(null);
    this.receipt.set(null);
    this.pending.set(null);
    this.reviewed = false;
    this.busy.set(false);
    this.uncertain.set(false);
    this.draftAvailable.set(false);
  }
  private current(owner: string) {
    return !this.destroyed && owner === this.owner();
  }
  private matches(r: ClientConversionReceipt, p: PendingRequestReference) {
    return (
      r.proposalId === this.id() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async prepare() {
    const s = this.state.data(),
      owner = this.owner(),
      raw = JSON.stringify(this.model());
    if (!s?.canConvert || !this.valid() || this.locked()) return;
    const requestId = crypto.randomUUID(),
      fields = normalizedClientConversion(this.model());
    this.busy.set(true);
    this.reviewed = false;
    const result = await this.api.command(this.base() + '/preview', {
      requestId,
      fields,
    });
    if (!this.current(owner)) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      if (result.status === 401 || result.status === 403) {
        this.clearProtected();
        this.state.reload();
      }
      return;
    }
    try {
      const p = decodeClientConversionPreview(result.value);
      if (
        p.proposalId !== this.id() ||
        p.requestId !== requestId ||
        p.proposalRevision !== s.proposalRevision ||
        raw !== JSON.stringify(this.model()) ||
        JSON.stringify(p.fields) !== JSON.stringify(fields) ||
        s.reviewBasis !== this.state.data()?.reviewBasis
      )
        throw new Error('Changed conversion');
      this.preview.set(p);
      this.message.set(
        'Review exact legal identity, canonical-client reuse and contact/acceptance effects. No conversion has been submitted.',
      );
    } catch {
      this.message.set(
        'Conversion review changed or returned an unsupported reply. Refresh and review again.',
      );
    }
  }
  async execute() {
    const p = this.preview(),
      owner = this.owner();
    if (!p || !this.reviewed || this.busy() || this.uncertain() || !this.state.data()?.canConvert)
      return;
    const ref = { requestId: p.requestId, requestHash: p.requestHash };
    if (!this.drafts.save(this.scope(true), ref, pendingRequestReference, true)) {
      this.message.set('Recovery storage is unavailable. No conversion was sent.');
      return;
    }
    this.pending.set(ref);
    this.busy.set(true);
    this.reviewed = false;
    const result = await this.api.command(this.base()!, {
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
      this.message.set(result.message);
      if (result.unknown) this.uncertain.set(true);
      else {
        this.drafts.clear(this.scope(true).entity);
        this.pending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.clearProtected();
          this.state.reload();
        }
      }
      return;
    }
    try {
      const r = decodeClientConversionReceipt(result.value);
      if (!this.matches(r, ref) || JSON.stringify(r.preview) !== JSON.stringify(p))
        throw new Error('Wrong receipt');
      this.receipt.set(r);
      this.uncertain.set(true);
      this.message.set(
        'Prospect conversion recorded. Acknowledge its retained receipt before continuing.',
      );
    } catch {
      this.uncertain.set(true);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  async reconcile() {
    const p = this.pending(),
      owner = this.owner();
    if (!p || !this.state.data() || this.busy()) return;
    this.busy.set(true);
    this.preview.set(null);
    this.reviewed = false;
    try {
      const v = await this.api.get(
        this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
        decodeClientConversionLookup,
      );
      if (!this.current(owner)) return;
      if (v.found && v.receipt && this.matches(v.receipt, p)) {
        this.receipt.set(v.receipt);
        this.uncertain.set(true);
        this.message.set(
          'Retained receipt confirms this prospect conversion. Acknowledge before continuing.',
        );
      } else if (!v.found) {
        if (!this.drafts.clear(this.scope(true).entity)) throw new Error('Storage unavailable');
        this.pending.set(null);
        this.uncertain.set(false);
        this.message.set(
          'No committed conversion was found. Refresh and explicitly review before a new request.',
        );
        this.state.reload();
      } else throw new Error('Wrong receipt');
    } catch {
      if (this.current(owner)) {
        this.clearProtected();
        this.state.reload();
        this.message.set(
          'Receipt verification unavailable. No command was retried. Refresh authorized context before verifying the retained reference again.',
        );
      }
    } finally {
      if (this.current(owner)) this.busy.set(false);
    }
  }
  acknowledge() {
    if (!this.receipt() || this.busy()) return;
    if (!this.drafts.clear(this.scope(true).entity)) {
      this.message.set('Recovery reference could not be cleared. Retry acknowledgement.');
      return;
    }
    this.pending.set(null);
    this.uncertain.set(false);
    this.drafts.clear(this.scope().entity);
    this.model.set(empty());
    this.baseline.set(JSON.stringify(empty()));
    this.message.set('Prospect conversion acknowledged. Professional acceptance remains separate.');
    this.state.reload();
  }
  cancelReview() {
    if (this.busy() || this.uncertain()) return;
    this.preview.set(null);
    this.reviewed = false;
  }
  saveDraft() {
    const ok =
      !this.busy() && !this.uncertain() && this.drafts.save(this.scope(), this.model(), editable);
    this.message.set(
      ok
        ? 'Editable legal identity saved in this tab. Assent is excluded.'
        : 'Draft unavailable. Restricted-profile text is memory-only and cannot be saved in a tab draft.',
    );
    if (ok) this.baseline.set(JSON.stringify(this.model()));
    return ok;
  }
  restoreDraft() {
    if (this.locked()) return;
    const d = this.drafts.read(this.scope(), editable);
    if (d.state !== 'ready') {
      this.message.set('Saved fields are stale or unavailable.');
      return;
    }
    this.model.set(d.draft.value);
    this.draftAvailable.set(false);
    this.preview.set(null);
    this.reviewed = false;
  }
  discard() {
    if (this.busy() || this.uncertain()) return false;
    if (!this.drafts.clear(this.scope().entity)) {
      this.message.set('Draft storage could not be cleared. Your current edits are preserved.');
      return false;
    }
    this.model.set(empty());
    this.baseline.set(JSON.stringify(empty()));
    this.preview.set(null);
    this.reviewed = false;
    return true;
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set('Verify and acknowledge the conversion receipt before leaving.');
      return false;
    }
    if (!this.dirty()) return true;
    const owner = this.owner();
    const choice = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if (!this.current(owner)) return false;
    return choice === 'save' ? this.saveDraft() : choice === 'discard' ? this.discard() : false;
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
    this.reviewed = false;
    this.state.reload();
  }
}
