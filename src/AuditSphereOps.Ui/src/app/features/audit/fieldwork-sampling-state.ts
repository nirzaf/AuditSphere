import { signal } from '@angular/core';
import { Api, CommandState, type Resource } from '../../core/api';
import { decimalInput, decode } from '../../core/decode';
import { type Fieldwork, decodeSampleSet, samplingPreviewDecoder, type SampleItem, type SampleSet, type SampleTestDraft, type SamplingPreview } from './fieldwork-contracts';

export class FieldworkSamplingState {
  readonly samplingPreview = signal<SamplingPreview | null>(null);
  readonly samplingBusy = signal(false);
  readonly sampleSet = signal<SampleSet | null>(null);
  readonly sampleSetLoading = signal(false);
  readonly sampleActionBusy = signal(false);
  readonly sampleSetError = signal('');
  selectionReviewComment = '';
  private readonly sampleDrafts = new Map<string, SampleTestDraft>();
  private previewInputFingerprint = '';
  private generation = 0;
  private previewOperation = 0;
  private sampleReadOperation = 0;
  private actionOperation = 0;

  readonly id: () => string | null;
  readonly api: Api;
  readonly ws: Resource<Fieldwork>;
  readonly cmd: CommandState;
  private readonly captureOwner: () => () => boolean;
  s = { procedure: '', schedule: '', method: 'MUS', interval: '', key: '', size: null as number | null, seed: null as number | null,
    attributeFields: [] as string[], rationale: '' };
  readonly attributeFields = [
    { value: 'ACCOUNT', label: 'Account code' }, { value: 'CURRENCY', label: 'Currency' },
    { value: 'DIRECTION', label: 'Debit / credit / zero direction' }, { value: 'MONTH', label: 'Transaction or posting month' },
  ];
  readonly countBased = () => ['RANDOM', 'SYSTEMATIC', 'STRATIFIED', 'ATTRIBUTE_STRATA'].includes(this.s.method);

  constructor(context: {
    id: () => string | null; api: Api; ws: Resource<Fieldwork>; cmd: CommandState;
    captureOwner: () => () => boolean;
  }) {
    this.id = context.id; this.api = context.api; this.ws = context.ws; this.cmd = context.cmd;
    this.captureOwner = context.captureOwner;
  }

  ownerChanged(): void {
    ++this.generation; ++this.previewOperation; ++this.sampleReadOperation; ++this.actionOperation;
    this.samplingBusy.set(false); this.sampleSetLoading.set(false); this.sampleActionBusy.set(false);
    this.samplingPreview.set(null); this.previewInputFingerprint = '';
    this.sampleSet.set(null); this.sampleSetError.set(''); this.sampleDrafts.clear();
    this.s = { procedure: '', schedule: '', method: 'MUS', interval: '', key: '', size: null, seed: null, attributeFields: [], rationale: '' };
    this.selectionReviewComment = '';
  }
  inputsChanged(): void {
    ++this.previewOperation;
    this.samplingBusy.set(false);
    this.samplingPreview.set(null);
    this.previewInputFingerprint = '';
  }
  workspaceUnavailable(): void {
    if (!this.ws.error()) return;
    ++this.previewOperation; ++this.sampleReadOperation; ++this.actionOperation;
    this.samplingBusy.set(false); this.samplingPreview.set(null); this.previewInputFingerprint = '';
    this.sampleSet.set(null); this.sampleSetError.set(''); this.sampleDrafts.clear();
  }
  private base(): string { return `/api/ui/engagements/${this.id()}/fieldwork`; }
  private invalid(message: string): void { this.cmd.failed.set(true); this.cmd.message.set(message); }
  parameters(r: { interval: string | null; keyItemThreshold: string | null; sampleSize: number | null; seed: number | null; sourceDigest: string }): string {
    return [r.interval ? `interval ${r.interval}` : null, r.keyItemThreshold ? `key items ≥ ${r.keyItemThreshold}` : null, r.sampleSize !== null ? `size ${r.sampleSize}` : null,
      r.seed !== null ? `seed ${r.seed}` : null, `source ${r.sourceDigest.slice(0, 10)}`].filter((x) => x).join(', ');
  }
  itemDraft(item: SampleItem): SampleTestDraft {
    let draft = this.sampleDrafts.get(item.selectionItemId);
    if (!draft) {
      draft = { workPerformed: item.testWorkPerformed ?? '', evidenceReferences: item.evidenceReferences.join('\n'), result: item.testResult === 'PENDING' ? 'PASS' : item.testResult,
        exceptionAmount: item.exceptionAmount ?? '', contradictoryEvidence: item.contradictoryEvidence ?? '', followUp: item.followUp ?? '', reviewerComment: '' };
      this.sampleDrafts.set(item.selectionItemId, draft);
    }
    return draft;
  }
  pageStart(set: SampleSet): number { return set.totalCount ? (set.page - 1) * set.pageSize + 1 : 0; }
  pageEnd(set: SampleSet): number { return Math.min(set.page * set.pageSize, set.totalCount); }
  methodLabel(method: string): string {
    return ({ MUS: 'Monetary unit (MUS)', KEY_ITEM: 'Key item threshold', RANDOM: 'Seeded random', SYSTEMATIC: 'Systematic random',
      STRATIFIED: 'Key items plus random', ATTRIBUTE_STRATA: 'Attribute strata' } as Record<string, string>)[method] ?? method;
  }
  toggleAttributeField(field: string, checked: boolean): void {
    this.s.attributeFields = checked
      ? [...new Set([...this.s.attributeFields, field])]
      : this.s.attributeFields.filter(x => x !== field);
    this.inputsChanged();
  }
  previewMatchesInputs(): boolean {
    return this.samplingPreview() !== null && this.previewInputFingerprint === this.samplingFingerprint();
  }
  private samplingFingerprint(): string {
    const s = this.s;
    return JSON.stringify({ procedureId: s.procedure, scheduleId: s.schedule, method: s.method.trim().toUpperCase(),
      interval: s.method === 'MUS' ? (s.interval.trim() ? decimalInput(s.interval, 6) : null) : null,
      keyItemThreshold: ['KEY_ITEM', 'STRATIFIED'].includes(s.method) ? (s.key.trim() ? decimalInput(s.key, 6) : null) : null,
      sampleSize: this.countBased() ? s.size : null, seed: this.countBased() ? s.seed : null,
      rationale: s.rationale.trim(), attributeFields: s.method === 'ATTRIBUTE_STRATA' ? [...s.attributeFields].sort() : null });
  }
  private samplingInput(): Record<string, unknown> | null {
    const s = this.s;
    const interval = s.method === 'MUS' && s.interval.trim() ? decimalInput(s.interval, 6) : null;
    const keyItemThreshold = ['KEY_ITEM', 'STRATIFIED'].includes(s.method) && s.key.trim() ? decimalInput(s.key, 6) : null;
    if (!s.procedure || !s.schedule || !s.rationale.trim()) {
      this.invalid('Choose a procedure and approved schedule, and enter a rationale.'); return null;
    }
    if (s.method === 'MUS' && (!s.interval.trim() || interval === null)) {
      this.invalid('Enter a valid monetary-unit interval.'); return null;
    }
    if (['KEY_ITEM', 'STRATIFIED'].includes(s.method) && (!s.key.trim() || keyItemThreshold === null)) {
      this.invalid('Enter a valid key-item threshold.'); return null;
    }
    if (this.countBased() && (!Number.isInteger(s.size) || (s.size ?? 0) < 1 || !Number.isInteger(s.seed))) {
      this.invalid('Enter a positive whole-number sample size and an integer seed.'); return null;
    }
    if (s.method === 'ATTRIBUTE_STRATA' && s.attributeFields.length === 0) {
      this.invalid('Choose at least one attribute field for stratified attribute sampling.'); return null;
    }
    return { procedureId: s.procedure, scheduleId: s.schedule, method: s.method, interval, keyItemThreshold,
      sampleSize: this.countBased() ? s.size : null, seed: this.countBased() ? s.seed : null, rationale: s.rationale.trim(),
      attributeFields: s.method === 'ATTRIBUTE_STRATA' ? [...s.attributeFields].sort() : null };
  }
  async sample(): Promise<void> {
    const input = this.samplingInput();
    if (!input) return;
    const owner = this.captureOwner(), generation = this.generation, operation = ++this.previewOperation;
    const inputFingerprint = this.samplingFingerprint();
    const current = () => owner() && generation === this.generation && operation === this.previewOperation;
    this.samplingPreview.set(null);
    this.previewInputFingerprint = '';
    this.samplingBusy.set(true);
    this.cmd.failed.set(false);
    this.cmd.message.set('');
    try {
      const result = await this.api.command<unknown>(this.base() + '/sampling/preview', input);
      if (!current()) return;
      if (!result.ok) { this.cmd.failed.set(true); this.cmd.message.set(result.message); return; }
      const preview = decode(samplingPreviewDecoder, result.value);
      if (inputFingerprint !== this.samplingFingerprint()) {
        this.cmd.message.set('Sampling inputs changed while the preview was loading. Review the current inputs and preview again.');
        return;
      }
      this.samplingPreview.set(preview);
      this.previewInputFingerprint = this.samplingFingerprint();
      this.cmd.message.set('Preview ready. Review the selected row IDs and strata before recording.');
    } catch {
      if (current()) {
        this.cmd.failed.set(true);
        this.cmd.message.set('The preview response could not be verified. Refresh the workspace and try again.');
      }
    } finally { if (operation === this.previewOperation) this.samplingBusy.set(false); }
  }
  async recordSampling(): Promise<void> {
    if (!this.previewMatchesInputs()) return this.invalid('Sampling inputs changed. Preview and review the selection again.');
    const input = this.samplingInput();
    const preview = this.samplingPreview();
    if (!input || !preview) return;
    const owner = this.captureOwner(), generation = this.generation, operation = ++this.actionOperation;
    const current = () => owner() && generation === this.generation && operation === this.actionOperation;
    this.samplingBusy.set(true);
    this.cmd.failed.set(false);
    this.cmd.message.set('');
    try {
      const result = await this.api.command<{ selectedCount: number; populationCount: number; coveragePercent: string; selectionId: string }>(
        this.base() + '/sampling', { ...input, expectedPreviewDigest: preview.previewDigest });
      if (!current()) return;
      if (result.ok) {
        this.cmd.message.set(`Recorded ${result.value.selectedCount} of ${result.value.populationCount} selected rows. The persisted selection is awaiting independent review.`);
        this.samplingPreview.set(null);
        this.previewInputFingerprint = '';
        this.sampleSet.set(null);
        this.ws.reload();
        await this.openSampleSet(result.value.selectionId);
      } else {
        this.cmd.failed.set(true);
        this.cmd.message.set(result.unknown
          ? `${result.message} Repeating the unchanged preview is idempotent.` : result.message);
        if (result.code === 'generation.stale') this.samplingPreview.set(null);
      }
    } finally { if (operation === this.actionOperation) this.samplingBusy.set(false); }
  }
  async openSampleSet(selectionId: string): Promise<void> { await this.loadSampleSet(selectionId, 1); }
  async loadSampleSet(selectionId: string, page = 1): Promise<void> {
    const owner = this.captureOwner(), generation = this.generation, operation = ++this.sampleReadOperation;
    const current = () => owner() && generation === this.generation && operation === this.sampleReadOperation;
    this.sampleSetLoading.set(true); this.sampleSetError.set('');
    try {
      const set = await this.api.get(`/api/ui/selections/${selectionId}/sample-set?page=${page}&pageSize=100`, decodeSampleSet);
      if (!current() || set.selectionId !== selectionId || set.page !== page) return;
      this.sampleSet.set(set);
      for (const item of set.items) this.itemDraft(item);
    } catch (error) {
      if (current()) { this.sampleSet.set(null); this.sampleSetError.set((error as Error).message || 'The sample set is unavailable.'); }
    } finally { if (operation === this.sampleReadOperation) this.sampleSetLoading.set(false); }
  }
  async reviewSelection(selectionId: string, decision: 'REVIEWED' | 'CHANGES_REQUIRED'): Promise<void> {
    const set = this.sampleSet();
    if (!set || set.selectionId !== selectionId || this.sampleActionBusy()) return;
    const comment = this.selectionReviewComment.trim();
    if (!comment) return this.invalid('Enter the independent review note before deciding this sample.');
    const owner = this.captureOwner(), generation = this.generation, operation = ++this.actionOperation;
    const page = set.page;
    const current = () => owner() && generation === this.generation && operation === this.actionOperation &&
      this.sampleSet()?.selectionId === selectionId && this.sampleSet()?.page === page;
    this.sampleActionBusy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    try {
      const result = await this.api.command(this.baseSelection(selectionId) + '/review', { decision, comment });
      if (!current()) return;
      if (result.ok) {
        this.cmd.message.set(decision === 'REVIEWED' ? 'The exact sample is approved for execution.' : 'The sample was returned with the recorded review note.');
        this.selectionReviewComment = '';
        this.ws.reload();
      } else {
        this.cmd.failed.set(true);
        this.cmd.message.set(result.unknown ? `${result.message} Refresh the sample status before deciding again.` : result.message);
      }
      await this.loadSampleSet(selectionId, page);
    } finally { if (operation === this.actionOperation) this.sampleActionBusy.set(false); }
  }
  async recordItemTest(item: SampleItem): Promise<void> {
    const draft = this.itemDraft(item);
    const evidenceReferences = draft.evidenceReferences.split(/\r?\n/).map(x => x.trim()).filter(Boolean);
    if (!draft.workPerformed.trim()) return this.invalid('Describe the work performed for this selected item.');
    if (['PASS', 'EXCEPTION'].includes(draft.result) && evidenceReferences.length === 0)
      return this.invalid('Pass and exception results require at least one evidence reference.');
    if (['EXCEPTION', 'LIMITATION'].includes(draft.result) && !draft.followUp.trim())
      return this.invalid('Exceptions and limitations require documented follow-up.');
    const enteredExceptionAmount = draft.exceptionAmount.trim();
    const exceptionAmount = enteredExceptionAmount ? decimalInput(enteredExceptionAmount, 6) : null;
    if (enteredExceptionAmount && exceptionAmount === null)
      return this.invalid('Enter a valid exception amount with no more than six decimal places.');
    const set = this.sampleSet();
    if (!set || !set.items.some(current => current.selectionItemId === item.selectionItemId) || this.sampleActionBusy()) return;
    const owner = this.captureOwner(), generation = this.generation, operation = ++this.actionOperation;
    const page = set.page;
    const current = () => owner() && generation === this.generation && operation === this.actionOperation &&
      this.sampleSet()?.selectionId === set.selectionId && this.sampleSet()?.page === page &&
      this.sampleSet()?.items.some(value => value.selectionItemId === item.selectionItemId) === true;
    this.sampleActionBusy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    try {
      const result = await this.api.command(this.baseItem(item.selectionItemId) + '/tests', {
        workPerformed: draft.workPerformed.trim(), evidenceReferences, result: draft.result, exceptionAmount,
        contradictoryEvidence: draft.contradictoryEvidence.trim() || null, followUp: draft.followUp.trim() || null,
      });
      if (!current()) return;
      if (result.ok) {
        this.cmd.message.set(`Item test recorded for ${item.stableRowId}. The current result and evidence are now in the sample history.`);
        this.sampleDrafts.delete(item.selectionItemId);
      } else {
        this.cmd.failed.set(true);
        this.cmd.message.set(result.unknown ? `${result.message} Refresh the item history before recording another revision.` : result.message);
      }
      await this.loadSampleSet(set.selectionId, page);
    } finally { if (operation === this.actionOperation) this.sampleActionBusy.set(false); }
  }
  async reviewItemTest(testId: string, decision: 'REVIEWED' | 'CHANGES_REQUIRED', selectionItemId: string): Promise<void> {
    const draft = this.sampleDrafts.get(selectionItemId);
    const comment = draft?.reviewerComment.trim() ?? '';
    if (decision === 'CHANGES_REQUIRED' && !comment) return this.invalid('Enter a reason when returning an item test for changes.');
    const set = this.sampleSet();
    if (!set || !set.items.some(item => item.auditItemTestId === testId && item.selectionItemId === selectionItemId) || this.sampleActionBusy()) return;
    const owner = this.captureOwner(), generation = this.generation, operation = ++this.actionOperation;
    const page = set.page;
    const current = () => owner() && generation === this.generation && operation === this.actionOperation &&
      this.sampleSet()?.selectionId === set.selectionId && this.sampleSet()?.page === page;
    this.sampleActionBusy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    try {
      const result = await this.api.command(this.baseItemTest(testId) + '/review', { decision, comment: comment || null });
      if (!current()) return;
      if (result.ok) this.cmd.message.set(decision === 'REVIEWED' ? 'Item-test result independently reviewed.' : 'Item-test result returned with the reviewer note.');
      else {
        this.cmd.failed.set(true);
        this.cmd.message.set(result.unknown ? `${result.message} Refresh the current review state before deciding again.` : result.message);
      }
      await this.loadSampleSet(set.selectionId, page);
    } finally { if (operation === this.actionOperation) this.sampleActionBusy.set(false); }
  }
  private baseSelection(selectionId: string): string { return `/api/ui/selections/${selectionId}`; }
  private baseItem(itemId: string): string { return `/api/ui/selection-items/${itemId}`; }
  private baseItemTest(testId: string): string { return `/api/ui/item-tests/${testId}`; }

}
