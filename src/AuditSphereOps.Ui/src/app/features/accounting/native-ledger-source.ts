import {
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormField, form, maxLength, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Api, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { nativeSourceBase } from './native-source-api';
import { decodeNativeSource } from './native-source-contracts';
import { NativeLedgerSourceApi, nativeLedgerBase } from './native-ledger-source-api';
import {
  NativeLedgerIntent,
  NativeLedgerPreview,
  NativeLedgerSource,
  decodeNativeLedgerSource,
  nativeLedgerPendingIntent,
} from './native-ledger-source-contracts';

@Component({
  selector: 'audit-native-ledger-source',
  imports: [RouterLink, FormField, MatButtonModule, MatPaginatorModule, ...SHARED],
  templateUrl: './native-ledger-source.html',
  styles: [
    ':host{display:block} label{display:block;margin-block:1rem} code,dd{overflow-wrap:anywhere} .table-scroll{overflow:auto}',
  ],
})
export class NativeLedgerSourcePage implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly native = inject(NativeLedgerSourceApi);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly clientId = routeGuid('clientId');
  readonly datasetId = routeGuid('datasetId');
  readonly importBatchId = routeGuid('importBatchId');
  readonly trialBalance = this.api.resource(
    () =>
      this.clientId() && this.datasetId() && !this.importBatchId()
        ? nativeSourceBase(this.clientId()!) + '/' + this.datasetId()
        : null,
    (raw) => decodeNativeSource(raw, this.clientId()!, this.datasetId()!),
  );
  readonly source = this.api.resource(
    () =>
      this.clientId() && this.importBatchId()
        ? nativeLedgerBase(this.clientId()!) + '/' + this.importBatchId()
        : null,
    (raw) => decodeNativeLedgerSource(raw, this.clientId()!, this.importBatchId()!),
  );
  readonly model = signal({ evidenceReference: '' });
  readonly fields = form(this.model, (p) => {
    required(p.evidenceReference);
    maxLength(p.evidenceReference, 300);
  });
  readonly wholePeriod = signal(false);
  readonly reviewed = signal(false);
  readonly preview = signal<NativeLedgerPreview | null>(null);
  readonly rowPage = signal(0);
  readonly visibleRows = computed(
    () => this.preview()?.rows.slice(this.rowPage() * 100, (this.rowPage() + 1) * 100) ?? [],
  );
  readonly sealed = signal<NativeLedgerSource | null>(null);
  readonly displaySource = computed(() => this.sealed() ?? this.source.data());
  readonly pending = signal<NativeLedgerIntent | null>(null);
  readonly uncertain = signal(false);
  readonly blockedPending = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly ready = computed(() => {
    const tb = this.trialBalance.data(),
      evidence = this.model().evidenceReference;
    return (
      !!this.session.current()?.staff &&
      !!tb &&
      tb.isCurrent &&
      !tb.activityOnly &&
      tb.validationStatus === 'Accepted' &&
      evidence.length > 0 &&
      evidence.length <= 300 &&
      evidence.trim() === evidence &&
      this.wholePeriod() &&
      !this.busy() &&
      !this.blockedPending() &&
      !this.displaySource()
    );
  });
  readonly canSeal = computed(
    () =>
      !!this.session.current()?.staff &&
      this.wholePeriod() &&
      !this.busy() &&
      !this.blockedPending() &&
      !this.displaySource() &&
      (this.pending() ? !!this.trialBalance.data() : this.ready() && !!this.preview()),
  );
  private generation = 0;
  private intent: NativeLedgerIntent | null = null;

  constructor() {
    effect(() => {
      this.clientId();
      this.datasetId();
      this.importBatchId();
      this.session.invalidation();
      untracked(() => {
        this.invalidatePreview();
        this.model.set({ evidenceReference: '' });
        this.wholePeriod.set(false);
        this.sealed.set(null);
        this.pending.set(null);
        this.uncertain.set(false);
        this.blockedPending.set(false);
        this.message.set('');
      });
    });
    effect(() => {
      const tb = this.trialBalance.data();
      if (!tb) return;
      untracked(() => {
        this.invalidatePreview();
        const saved = this.drafts.readPendingSubmission(
          this.draftScope(),
          nativeLedgerPendingIntent,
        );
        if (
          saved.state === 'ready' &&
          saved.draft.value.clientId === tb.clientId &&
          saved.draft.value.engagementId === tb.engagementId &&
          saved.draft.value.trialBalanceDatasetId === tb.datasetId
        ) {
          this.pending.set(saved.draft.value);
          this.uncertain.set(true);
          this.wholePeriod.set(false);
          this.model.set({ evidenceReference: saved.draft.value.evidenceReference });
          this.message.set(
            'A prior native GL seal remains unresolved. Explicit recovery uses its original reviewed request; no new capture is started.',
          );
        } else if (saved.state !== 'absent') {
          this.blockedPending.set(true);
          this.message.set(
            'The original request identity is unavailable or unsupported. Reconcile the retained outcome before starting another capture.',
          );
        }
      });
    });
    effect(() => {
      this.model();
      untracked(() => this.invalidatePreview());
    });
    effect(() => {
      if (!this.wholePeriod()) untracked(() => this.invalidatePreview());
    });
  }
  private invalidatePreview() {
    this.generation++;
    this.preview.set(null);
    this.intent = null;
    this.reviewed.set(false);
    this.busy.set(false);
    this.rowPage.set(0);
  }
  private draftScope() {
    return {
      entity: `native-gl-source-request/${this.clientId()}/${this.datasetId()}`,
      baseRevision: this.preview()?.reviewBasis ?? '0'.repeat(64),
    };
  }
  async makePreview() {
    if (!this.ready() || this.pending() || this.uncertain()) return;
    const tb = this.trialBalance.data()!,
      generation = this.generation;
    const request: NativeLedgerIntent = {
      requestId: crypto.randomUUID(),
      clientId: tb.clientId,
      engagementId: tb.engagementId,
      trialBalanceDatasetId: tb.datasetId,
      expectedTrialBalanceSourceHash: tb.sourceIdentityHash,
      evidenceReference: this.model().evidenceReference,
      reviewBasis: '',
    };
    this.preview.set(null);
    this.rowPage.set(0);
    this.reviewed.set(false);
    this.busy.set(true);
    this.message.set('');
    try {
      const p = await this.native.preview(request, tb);
      if (generation !== this.generation) return;
      this.intent = { ...request, reviewBasis: p.reviewBasis };
      this.preview.set(p);
    } catch (e) {
      if (generation === this.generation)
        this.message.set(e instanceof Error ? e.message : 'Native GL preview unavailable.');
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async seal() {
    const r = this.pending() ?? this.intent,
      tb = this.trialBalance.data();
    if (
      !r ||
      !tb ||
      !this.canSeal() ||
      !this.reviewed() ||
      r.clientId !== this.clientId() ||
      r.trialBalanceDatasetId !== tb.datasetId ||
      r.engagementId !== tb.engagementId ||
      r.evidenceReference !== this.model().evidenceReference ||
      (!this.pending() && r.expectedTrialBalanceSourceHash !== tb.sourceIdentityHash)
    )
      return;
    if (
      !this.drafts.save(
        { ...this.draftScope(), baseRevision: r.reviewBasis },
        r,
        nativeLedgerPendingIntent,
        true,
      )
    ) {
      this.message.set('The original pending request could not be retained. No seal was sent.');
      return;
    }
    const generation = this.generation;
    this.pending.set(r);
    this.busy.set(true);
    this.reviewed.set(false);
    this.message.set('');
    const result = await this.native.seal(r);
    if (generation !== this.generation) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      if (result.unknown) this.uncertain.set(true);
      else {
        this.drafts.clear(this.draftScope().entity);
        this.pending.set(null);
        this.uncertain.set(false);
        this.invalidatePreview();
      }
      return;
    }
    try {
      const s = decodeNativeLedgerSource(result.value, r.clientId),
        p = this.preview();
      if (
        s.trialBalanceDatasetId !== r.trialBalanceDatasetId ||
        s.engagementId !== r.engagementId ||
        s.periodId !== tb.periodId ||
        s.asOfDate !== tb.asOfDate ||
        s.legalEntityKey !== tb.legalEntityKey ||
        (p &&
          (s.sourceIdentityHash !== p.sourceIdentityHash ||
            s.manifestHash !== p.manifestHash ||
            s.generatedRowsHash !== p.generatedRowsHash ||
            s.lineCount !== p.lineCount ||
            s.transactionCount !== p.transactionCount))
      )
        throw new Error('Native GL seal identity mismatch');
      this.sealed.set(s);
      this.drafts.clear(this.draftScope().entity);
      this.pending.set(null);
      this.uncertain.set(false);
      this.invalidatePreview();
      this.message.set(
        'Native generated GL source sealed. Independent GL acceptance and downstream completeness remain separate workflows.',
      );
    } catch {
      this.uncertain.set(true);
      this.message.set(
        'The seal response identity could not be verified. Retain and recover its original request; do not start another capture.',
      );
    }
  }
  async download(kind: 'rows' | 'manifest') {
    const s = this.displaySource();
    if (!s || this.busy()) return;
    const generation = this.generation;
    this.busy.set(true);
    try {
      const a = await this.native.artifact(s);
      if (generation !== this.generation || s.importBatchId !== this.displaySource()?.importBatchId)
        return;
      const text = kind === 'rows' ? a.generatedRowsJson : a.manifestJson;
      const url = URL.createObjectURL(new Blob([text], { type: a.contentType })),
        link = document.createElement('a');
      link.href = url;
      link.download = kind === 'rows' ? a.fileName : a.fileName.replace('.json', '-manifest.json');
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      this.message.set(
        a.isCurrent
          ? 'Exact retained generated bytes saved. This is not an uploaded file or independent acceptance.'
          : `Historical generated bytes saved. ${a.currentnessBlocker ?? ''}`,
      );
    } catch (e) {
      if (generation === this.generation)
        this.message.set(e instanceof Error ? e.message : 'Generated artifact unavailable.');
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  refresh() {
    if (this.busy()) return;
    this.invalidatePreview();
    this.wholePeriod.set(false);
    this.trialBalance.reload();
    this.source.reload();
  }
  confirmNavigation() {
    if (this.busy() || (this.preview() && !this.pending())) {
      this.message.set(
        'Finish the action or refresh to discard the unsealed preview before leaving.',
      );
      return false;
    }
    return true;
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.busy() || this.preview() || this.pending()) event.preventDefault();
  }
}
