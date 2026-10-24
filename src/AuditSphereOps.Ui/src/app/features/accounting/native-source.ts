import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Api, queryParam, routeGuid } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { NativeSourceApi, nativeSourceBase } from './native-source-api';
import { NativePreview, NativeSource, NativeSourceIntent, decodeNativeContext, decodeNativeSource, nativePendingIntent } from './native-source-contracts';

const empty = () => ({ periodId: '', engagementId: '', asOfDate: '', legalEntityKey: '', evidenceReference: '', allowActivityOnly: false });
@Component({
  selector: 'audit-native-source',
  imports: [RouterLink, FormField, MatButtonModule, MatPaginatorModule, ...SHARED],
  templateUrl: './native-source.html',
  styles: [':host{display:block} fieldset{display:grid;gap:1rem} label{display:block} code,dd{overflow-wrap:anywhere} .table-scroll{overflow:auto}'],
})
export class NativeSourcePage implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly native = inject(NativeSourceApi);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly clientId = routeGuid('clientId');
  readonly datasetId = routeGuid('datasetId');
  private readonly initialPeriod = queryParam('periodId');
  readonly context = this.api.resource(() => this.clientId() && !this.datasetId()
    ? `/api/ui/accounting/clients/${this.clientId()}` : null, raw => decodeNativeContext(raw, this.clientId()!));
  readonly source = this.api.resource(() => this.clientId() && this.datasetId()
    ? nativeSourceBase(this.clientId()!) + '/' + this.datasetId() : null,
    raw => decodeNativeSource(raw, this.clientId()!, this.datasetId()!));
  readonly model = signal(empty());
  readonly fields = form(this.model, p => {
    required(p.periodId); required(p.engagementId); maxLength(p.engagementId, 36); required(p.asOfDate);
    required(p.legalEntityKey); maxLength(p.legalEntityKey, 200);
    required(p.evidenceReference); maxLength(p.evidenceReference, 2000);
  });
  readonly wholePeriod = signal(false);
  readonly reviewed = signal(false);
  readonly preview = signal<NativePreview | null>(null);
  readonly rowPage = signal(0);
  readonly visibleRows = computed(() => this.preview()?.trialBalance.rows.slice(this.rowPage() * 100, (this.rowPage() + 1) * 100) ?? []);
  readonly sealed = signal<NativeSource | null>(null);
  readonly pending = signal<NativeSourceIntent | null>(null);
  readonly uncertain = signal(false);
  readonly blockedPending = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly selectedPeriod = computed(() => this.context.data()?.periods.find(p => p.id === this.model().periodId) ?? null);
  readonly displaySource = computed(() => this.sealed() ?? this.source.data());
  readonly ready = computed(() => {
    const m = this.model(), p = this.selectedPeriod();
    return !!this.session.current()?.staff && !!p && ['ACTIVE', 'DRAFT'].includes(p.status) &&
      guidPattern.test(m.engagementId) && /^\d{4}-\d{2}-\d{2}$/.test(m.asOfDate) && m.asOfDate >= p.start && m.asOfDate <= p.end &&
      !!m.legalEntityKey.trim() && m.legalEntityKey === m.legalEntityKey.trim() && m.legalEntityKey.length <= 200 &&
      !!m.evidenceReference.trim() && m.evidenceReference === m.evidenceReference.trim() && m.evidenceReference.length <= 2000 &&
      this.wholePeriod() && !this.busy() && !this.blockedPending() && !this.displaySource();
  });
  readonly canSeal = computed(() => !!this.session.current()?.staff && this.wholePeriod() && !this.busy() &&
    !this.blockedPending() && !this.displaySource() && (this.pending() ? !!this.context.data() : this.ready() && !!this.preview()));
  private generation = 0;
  private intent: NativeSourceIntent | null = null;
  constructor() {
    effect(() => {
      this.clientId(); this.datasetId(); this.session.invalidation();
      untracked(() => {
        this.generation++; this.model.set(empty()); this.preview.set(null); this.intent = null;
        this.reviewed.set(false); this.wholePeriod.set(false); this.busy.set(false); this.message.set('');
        this.sealed.set(null); this.pending.set(null); this.uncertain.set(false); this.blockedPending.set(false);
      });
    });
    effect(() => {
      const c = this.context.data(); const initialPeriod = this.initialPeriod();
      if (!c) return;
      untracked(() => {
        this.generation++;
        const saved = this.drafts.readPendingSubmission(this.draftScope(), nativePendingIntent);
        if (saved.state === 'ready' && saved.draft.value.clientId === c.clientId) {
          const r = saved.draft.value;
          this.pending.set(r); this.uncertain.set(true); this.reviewed.set(false); this.wholePeriod.set(false);
          this.model.set({ periodId: r.periodId, engagementId: r.engagementId, asOfDate: r.asOfDate,
            legalEntityKey: r.legalEntityKey, evidenceReference: r.evidenceReference, allowActivityOnly: r.allowActivityOnly });
          this.message.set('An earlier seal remains unresolved. Review its retained intent before explicitly retrying that same request.');
        } else if (saved.state === 'stale' || saved.state === 'unavailable' || saved.state === 'ready') {
          this.blockedPending.set(true);
          this.message.set('The prior request identity is unavailable or unsupported. Sealing stays blocked; reconcile the retained source before continuing.');
        } else {
          const p = c.periods.find(p => p.id === initialPeriod);
          this.model.set({ ...empty(), periodId: p?.id ?? '', asOfDate: p?.end ?? '' });
        }
      });
    });
    effect(() => {
      this.model();
      untracked(() => { this.generation++; this.preview.set(null); this.intent = null; this.reviewed.set(false); this.busy.set(false); });
    });
    effect(() => {
      if (!this.wholePeriod()) untracked(() => { this.preview.set(null); this.reviewed.set(false); this.intent = null; });
    });
  }
  private draftScope() {
    // Pending submission recovery is intentionally independent of a changed preview base.
    return { entity: 'native-source-request/' + this.clientId(), baseRevision: this.preview()?.reviewBasis ?? '0'.repeat(64) };
  }
  choosePeriod(event: Event) {
    if (this.busy() || this.uncertain()) return;
    const id = (event.target as HTMLSelectElement).value, p = this.context.data()?.periods.find(p => p.id === id);
    this.model.update(m => ({ ...m, periodId: p?.id ?? '', asOfDate: p?.end ?? '' }));
  }
  async makePreview() {
    if (!this.ready() || this.pending() || this.uncertain()) return;
    const p = this.selectedPeriod()!, m = this.model(), generation = this.generation;
    const request: NativeSourceIntent = { ...m, clientId: this.clientId()!, requestId: crypto.randomUUID(),
      expectedPeriodRevision: p.revision, reviewBasis: '' };
    this.preview.set(null); this.rowPage.set(0); this.reviewed.set(false); this.busy.set(true); this.message.set('');
    try {
      const value = await this.native.preview(request, p);
      if (generation !== this.generation) return;
      this.intent = { ...request, reviewBasis: value.reviewBasis }; this.preview.set(value);
    } catch (e) {
      if (generation === this.generation) this.message.set(e instanceof Error ? e.message : 'Preview unavailable.');
    } finally { if (generation === this.generation) this.busy.set(false); }
  }
  async seal() {
    const r = this.pending() ?? this.intent;
    if (!r || !this.canSeal() || !this.reviewed()) return;
    const m = this.model();
    if (r.engagementId !== m.engagementId || r.periodId !== m.periodId || r.asOfDate !== m.asOfDate ||
        r.legalEntityKey !== m.legalEntityKey || r.evidenceReference !== m.evidenceReference || r.allowActivityOnly !== m.allowActivityOnly ||
        !this.pending() && r.expectedPeriodRevision !== this.selectedPeriod()?.revision) return;
    if (r.clientId !== this.clientId()) return;
    if (!this.drafts.save({ ...this.draftScope(), baseRevision: r.reviewBasis }, r, nativePendingIntent, true)) {
      this.message.set('The pending request could not be retained in this tab. No seal was sent.'); return;
    }
    const generation = this.generation;
    this.pending.set(r); this.busy.set(true); this.reviewed.set(false); this.message.set('');
    const result = await this.native.seal(r);
    if (generation !== this.generation) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      if (result.unknown) this.uncertain.set(true);
      else { this.drafts.clear(this.draftScope().entity); this.pending.set(null); this.uncertain.set(false); this.preview.set(null); this.intent = null; }
      return;
    }
    try {
      const s = decodeNativeSource(result.value, r.clientId);
      if (s.engagementId !== r.engagementId || s.periodId !== r.periodId || s.asOfDate !== r.asOfDate ||
          s.legalEntityKey !== r.legalEntityKey || !r.allowActivityOnly && s.activityOnly ||
          this.preview() && (s.sourceIdentityHash !== this.preview()!.sourceIdentityHash ||
            s.manifestHash !== this.preview()!.manifestHash || s.generatedRowsHash !== this.preview()!.generatedRowsHash ||
            s.activityOnly !== this.preview()!.activityOnly || s.rowCount !== this.preview()!.rowCount)) throw new Error('Seal identity mismatch');
      this.sealed.set(s); this.preview.set(null); this.intent = null;
      this.pending.set(null); this.uncertain.set(false); this.drafts.clear(this.draftScope().entity);
      this.message.set('Native generated source sealed. Technical validation and independent acceptance remain separate.');
    } catch {
      this.uncertain.set(true); this.message.set('The seal response could not be verified. Its original request identity remains retained; do not start another capture.');
    }
  }
  async download(kind: 'rows' | 'manifest') {
    const s = this.displaySource(); if (!s || this.busy()) return;
    const generation = this.generation; this.busy.set(true);
    try {
      const a = await this.native.artifact(s);
      if (generation !== this.generation || s.datasetId !== this.displaySource()?.datasetId) return;
      const text = kind === 'rows' ? a.generatedRowsJson : a.manifestJson;
      const url = URL.createObjectURL(new Blob([text], { type: a.contentType })), link = document.createElement('a');
      link.href = url; link.download = kind === 'rows' ? a.fileName : a.fileName.replace('.json', '-manifest.json'); link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      this.message.set(a.isCurrent ? 'Retained generated artifact saved. This is not an uploaded source file or acceptance evidence.' :
        `Historical generated artifact saved. ${a.currentnessBlocker ?? ''}`);
    } catch (e) { if (generation === this.generation) this.message.set(e instanceof Error ? e.message : 'Artifact unavailable.'); }
    finally { if (generation === this.generation) this.busy.set(false); }
  }
  refresh() { if (!this.busy()) { this.preview.set(null); this.reviewed.set(false); this.context.reload(); this.source.reload(); } }
  confirmNavigation() {
    if (this.busy() || this.preview() && !this.pending()) {
      this.message.set('Finish the current action or refresh to discard the unsealed preview before leaving.'); return false;
    }
    return true;
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent) { if (this.busy() || this.preview() || this.pending()) event.preventDefault(); }
}
