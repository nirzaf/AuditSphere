import { DestroyRef, signal } from '@angular/core';
import { Api, CommandState, type Resource } from '../../core/api';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { decodeFieldwork, decodeProcedureCommandLookup, decodeProcedureCommandPreview, decodeProcedureCommandReceipt, decodeProcedureCommandState, decodeProcedureReview, decodeProgramOptions,
  decodeTailorProcedureCommand, type Fieldwork, type TailorProcedureCommand } from './fieldwork-contracts';

export type FieldworkSend = (url: string, body: unknown, ok: string, after?: () => void) => Promise<boolean>;
type PendingProcedureCommand = { engagementId: string; actorUserId: string; actorGeneration: string; procedureId: string; requestId: string; requestHash: string };

export class FieldworkProcedureState {
  readonly selectedProgramVersionId = signal('');
  readonly review = signal<ReturnType<typeof decodeProcedureReview> | null>(null);
  readonly reviewError = signal('');
  readonly procedureSelectedId = signal('');
  readonly procedureCommandState = signal<ReturnType<typeof decodeProcedureCommandState> | null>(null);
  readonly procedureCommandPreview = signal<ReturnType<typeof decodeProcedureCommandPreview> | null>(null);
  readonly procedureCommandReceipt = signal<ReturnType<typeof decodeProcedureCommandReceipt> | null>(null);
  readonly procedureCommandError = signal('');
  readonly procedureCommandBusy = signal(false);
  readonly procedureUnknown = signal<PendingProcedureCommand | null>(null);
  private readonly procedureUnknowns = signal<PendingProcedureCommand[]>([]);
  readonly tailoringProcedureId = signal('');
  readonly tailoringPending = signal<{ procedureId: string; command: TailorProcedureCommand } | null>(null);
  readonly tailoringBusy = signal(false);
  readonly tailoringUncertain = signal(false);
  readonly tailoringFailed = signal(false);
  readonly tailoringMessage = signal('');
  tailoringTitle = '';
  tailoringWording = '';
  tailoringRationale = '';
  procedureWork = '';
  procedureConclusion = '';
  procedureDecision = 'REVIEWED';
  procedureComment = '';
  procedureManualReference = '';
  procedureObservation = '';
  procedureLegacyReferences = '';
  procedureAssent = false;
  procedureSelectedLinks = new Set<string>();
  procedureSelectedPhysical = new Set<string>();
  private procedurePendingRequest: Record<string, unknown> | null = null;
  private readonly applicabilityReasons = signal<Record<string, string>>({});
  private reviewRequest = 0;
  private reviewDestroyed = false;
  private reviewProcedureId = '';
  private noteContextKey = '';
  private selectionGeneration = 0;
  private commandGeneration = 0;
  private previewGeneration = 0;
  private tailoringGeneration = 0;

  readonly api: Api;
  readonly tabDrafts: TabDrafts;
  readonly id: () => string | null;
  readonly ws: Resource<Fieldwork>;
  readonly cmd: CommandState;
  readonly riskLinkSession: SessionService;
  private readonly sendCommand: FieldworkSend;
  private readonly captureOwner: () => () => boolean;
  private readonly destroyRef: DestroyRef;

  e = { procedure: '', upload: '', note: '' };
  ph = { index: '', box: '', description: '', location: '', linkTarget: '', moveTo: '' };
  ah = { title: '', wording: '', reason: '', section: '' };
  n = { field: 'WORK_PERFORMED', excerpt: '', body: '', reply: '' };

  constructor(context: {
    api: Api; tabDrafts: TabDrafts; id: () => string | null; ws: Resource<Fieldwork>; cmd: CommandState;
    session: SessionService; send: FieldworkSend; captureOwner: () => () => boolean; destroyRef: DestroyRef;
  }) {
    this.api = context.api; this.tabDrafts = context.tabDrafts; this.id = context.id; this.ws = context.ws;
    this.cmd = context.cmd; this.riskLinkSession = context.session; this.sendCommand = context.send;
    this.captureOwner = context.captureOwner; this.destroyRef = context.destroyRef;
    this.destroyRef.onDestroy(() => {
      this.reviewDestroyed = true; ++this.reviewRequest; ++this.selectionGeneration; ++this.commandGeneration; ++this.previewGeneration; ++this.tailoringGeneration;
      this.review.set(null); this.reviewError.set(''); this.procedureCommandBusy.set(false); this.tailoringBusy.set(false);
    });
  }

  ownerChanged(): void {
    const tailoringWasBusy = this.tailoringBusy();
    ++this.reviewRequest; ++this.selectionGeneration; ++this.commandGeneration; ++this.previewGeneration; ++this.tailoringGeneration;
    this.review.set(null); this.reviewError.set('');
    this.reviewProcedureId = ''; this.noteContextKey = '';
    this.procedureCommandState.set(null); this.procedureCommandPreview.set(null); this.procedureCommandReceipt.set(null); this.procedureCommandError.set('');
    this.procedureAssent = false; this.procedureSelectedId.set(''); this.procedurePendingRequest = null;
    this.procedureWork = ''; this.procedureConclusion = ''; this.procedureDecision = 'REVIEWED'; this.procedureComment = '';
    this.procedureManualReference = ''; this.procedureObservation = ''; this.procedureLegacyReferences = '';
    this.e = { procedure: '', upload: '', note: '' };
    this.n = { field: 'WORK_PERFORMED', excerpt: '', body: '', reply: '' };
    this.procedureSelectedLinks.clear(); this.procedureSelectedPhysical.clear();
    this.procedureCommandBusy.set(false); this.tailoringBusy.set(false);
    this.selectedProgramVersionId.set('');
    if (!this.tailoringPending()) {
      this.tailoringProcedureId.set(''); this.tailoringTitle = ''; this.tailoringWording = ''; this.tailoringRationale = '';
      this.tailoringUncertain.set(false); this.tailoringFailed.set(false); this.tailoringMessage.set('');
    } else if (tailoringWasBusy) {
      this.tailoringUncertain.set(true);
      this.tailoringMessage.set('The previous tailoring request is still unresolved. Return to its original engagement and reconcile the exact request before continuing.');
    }
    this.applicabilityReasons.set({});
  }

  workspaceChanged(): void {
    ++this.reviewRequest; this.review.set(null); this.reviewError.set('');
    const workspace = this.ws.data();
    if (workspace && this.e.procedure && workspace.procedures.some(p => p.id === this.e.procedure)) void this.selectProcedure(this.e.procedure);
    if (workspace) this.restorePendingTailoring(workspace);
  }

  private send(url: string, body: unknown, ok: string, after?: () => void): Promise<boolean> {
    return this.sendCommand(url, body, ok, after);
  }
  private invalid(message: string): void { this.cmd.failed.set(true); this.cmd.message.set(message); }
  private base(): string { return `/api/ui/engagements/${this.id()}/fieldwork`; }
  selectedProgram(options: ReturnType<typeof decodeProgramOptions>) {
    const selectedId = this.selectedProgramVersionId();
    return options.options.find(x => x.programVersionId === selectedId) ?? options.options.find(x => x.eligible) ?? null;
  }
  selectedProgramVersion(options: ReturnType<typeof decodeProgramOptions>): string {
    return this.selectedProgram(options)?.programVersionId ?? '';
  }
  adoptProgram(options: ReturnType<typeof decodeProgramOptions>): void {
    const selected = this.selectedProgram(options);
    if (!selected?.eligible || options.currentAdoptionId || options.currentAdoptionBlocker) return;
    void this.send(`/api/ui/engagements/${this.id()}/fieldwork/program`, {
      programVersionId: selected.programVersionId, sourceHash: selected.sourceHash,
      contentDigest: selected.contentDigest, expectedAdoptionId: options.currentAdoptionId,
    }, 'Exact published programme adopted.');
  }
  applicabilityReason(procedureId: string): string { return this.applicabilityReasons()[procedureId] ?? ''; }
  setApplicabilityReason(procedureId: string, value: string): void {
    this.applicabilityReasons.update(current => ({ ...current, [procedureId]: value }));
  }
  private tailoringScope(procedureId: string) {
    return { entity: `audit-procedure-tailoring/${procedureId}`, baseRevision: '0'.repeat(64) };
  }
  private restorePendingTailoring(workspace: Fieldwork): void {
    if (this.tailoringPending() || !workspace.canManageFieldwork) return;
    for (const procedure of workspace.procedures) {
      if (!procedure.originalWording || !procedure.definitionRevision) continue;
      const saved = this.tabDrafts.read(this.tailoringScope(procedure.id), decodeTailorProcedureCommand);
      if (saved.state !== 'ready' || !saved.draft.submissionPending) continue;
      this.tailoringPending.set({ procedureId: procedure.id, command: saved.draft.value });
      this.tailoringProcedureId.set(procedure.id);
      this.tailoringTitle = saved.draft.value.title;
      this.tailoringWording = saved.draft.value.customWording;
      this.tailoringRationale = saved.draft.value.rationale;
      this.tailoringUncertain.set(true);
      this.tailoringMessage.set('A previous tailoring request needs reconciliation. Retry the retained request before making another change.');
      return;
    }
  }
  beginTailoring(procedure: Fieldwork['procedures'][number]): void {
    if (!this.ws.data()?.canManageFieldwork || !procedure.originalWording || !procedure.definitionRevision ||
        this.tailoringBusy() || this.tailoringUncertain() || this.tailoringPending()) return;
    this.tailoringProcedureId.set(procedure.id);
    this.tailoringTitle = procedure.title;
    this.tailoringWording = procedure.effectiveWording ?? procedure.title;
    this.tailoringRationale = '';
    this.tailoringMessage.set('');
    this.tailoringFailed.set(false);
  }
  cancelTailoring(): void {
    if (this.tailoringBusy() || this.tailoringUncertain()) return;
    this.tailoringProcedureId.set('');
    this.tailoringTitle = ''; this.tailoringWording = ''; this.tailoringRationale = '';
    this.tailoringMessage.set(''); this.tailoringFailed.set(false);
  }
  async submitTailoring(): Promise<void> {
    if (this.tailoringBusy() || this.tailoringUncertain() || this.tailoringPending()) return;
    const procedureId = this.tailoringProcedureId(), workspace = this.ws.data();
    const procedure = workspace?.procedures.find(item => item.id === procedureId);
    const title = this.tailoringTitle.trim(), customWording = this.tailoringWording.trim(), rationale = this.tailoringRationale.trim();
    if (!workspace?.canManageFieldwork || !procedure?.originalWording || !procedure.definitionRevision ||
        !title || title.length > 500 || !customWording || customWording.length > 2000 || !rationale || rationale.length > 1000) {
      this.tailoringFailed.set(true);
      this.tailoringMessage.set('Enter a bounded effective title, instructions and rationale for the current controlled definition.');
      return;
    }
    const command: TailorProcedureCommand = { title, customWording, rationale,
      expectedDefinitionRevision: procedure.definitionRevision, commandId: globalThis.crypto.randomUUID() };
    await this.sendTailoring(procedureId, command);
  }
  async recoverTailoring(): Promise<void> {
    const pending = this.tailoringPending();
    if (!pending || !this.tailoringUncertain() || this.tailoringBusy()) return;
    await this.sendTailoring(pending.procedureId, pending.command);
  }
  private async sendTailoring(procedureId: string, command: TailorProcedureCommand): Promise<void> {
    const generation = ++this.tailoringGeneration;
    const owner = this.captureOwner();
    const current = () => owner() && generation === this.tailoringGeneration && !this.reviewDestroyed;
    const scope = this.tailoringScope(procedureId);
    this.tailoringFailed.set(false);
    this.tailoringMessage.set('');
    this.tailoringBusy.set(true);
    if (!this.tabDrafts.save(scope, command, decodeTailorProcedureCommand, true)) {
      this.tailoringBusy.set(false);
      this.tailoringFailed.set(true);
      this.tailoringMessage.set('The request could not be safely retained in this tab, so it was not sent.');
      return;
    }
    this.tailoringPending.set({ procedureId, command });
    try {
      const result = await this.api.command(`/api/ui/procedures/${procedureId}/tailor`, command);
      if (!current()) return;
      if (result.ok) {
        this.tabDrafts.clear(scope.entity);
        this.tailoringPending.set(null);
        this.tailoringUncertain.set(false);
        this.tailoringProcedureId.set('');
        this.tailoringMessage.set('A new effective wording revision was appended. The original controlled definition is unchanged.');
        this.ws.reload();
      } else if (result.unknown) {
        this.tailoringUncertain.set(true);
        this.tailoringMessage.set('The outcome is unknown. Retry the exact retained request to reconcile it before starting another tailoring change.');
      } else {
        this.tabDrafts.clear(scope.entity);
        this.tailoringPending.set(null);
        this.tailoringUncertain.set(false);
        this.tailoringProcedureId.set('');
        this.tailoringFailed.set(true);
        this.tailoringMessage.set(result.code === 'generation.stale'
          ? 'The definition revision changed. The current procedure is refreshing; review its source and effective wording before trying again.'
          : result.message);
        this.ws.reload();
      }
    } catch {
      if (!current()) return;
      this.tailoringUncertain.set(true);
      this.tailoringMessage.set('The outcome could not be verified. Retry the exact retained request to reconcile it before starting another tailoring change.');
    } finally { if (generation === this.tailoringGeneration) this.tailoringBusy.set(false); }
  }
  decide(procedure: ReturnType<typeof decodeFieldwork>['procedures'][number], decision: string): void {
    const rationale = this.applicabilityReason(procedure.id).trim();
    void this.send(`/api/ui/procedures/${procedure.id}/applicability`, {
      decision, rationale: rationale || null, expectedApplicabilityRevision: procedure.applicabilityRevision,
      expectedDefinitionRevision: procedure.definitionRevision ?? 0, commandId: globalThis.crypto.randomUUID(),
      proposalDecisionId: procedure.pendingApplicabilityProposalId,
    }, 'Applicability decision recorded.');
  }
  async selectProcedure(procedureId: string): Promise<void> {
    if (procedureId !== this.reviewProcedureId) {
      this.reviewProcedureId = procedureId;
      this.e = { procedure: procedureId, upload: '', note: '' };
      this.n = { field: 'WORK_PERFORMED', excerpt: '', body: '', reply: '' };
      this.noteContextKey = '';
      if (this.procedureSelectedId() && this.procedureSelectedId() !== procedureId) {
        ++this.selectionGeneration; ++this.commandGeneration; ++this.previewGeneration;
        this.procedureSelectedId.set(''); this.procedureCommandState.set(null); this.procedureCommandPreview.set(null);
        this.procedureCommandReceipt.set(null); this.procedurePendingRequest = null; this.procedureAssent = false; this.procedureCommandError.set('');
        this.procedureCommandBusy.set(false);
      }
      this.procedureSelectedLinks.clear(); this.procedureSelectedPhysical.clear();
    }
    const request = ++this.reviewRequest, engagement = this.id(), workspace = this.ws.data(),
      actor = this.riskLinkSession.current(), epoch = this.riskLinkSession.invalidation();
    const owner = this.captureOwner();
    this.review.set(null); this.reviewError.set('');
    if (this.reviewDestroyed || !actor?.staff || !procedureId || !workspace?.procedures.some(p => p.id === procedureId)) return;
    const current = () => owner() && !this.reviewDestroyed && request === this.reviewRequest && engagement === this.id() &&
      workspace === this.ws.data() && actor === this.riskLinkSession.current() && epoch === this.riskLinkSession.invalidation();
    try {
      const review = await this.api.get(`/api/ui/procedures/${procedureId}/review`, decodeProcedureReview);
      if (!current()) return;
      if (review.procedureId !== procedureId) throw new Error('Procedure review unavailable.');
      this.bindNoteContext(`${procedureId}:${review.currentResult?.id ?? ''}`);
      this.review.set(review);
    } catch (e) { if (current()) this.reviewError.set((e as Error).message); }
  }

  private bindNoteContext(key: string): void {
    if (this.noteContextKey === key) return;
    this.noteContextKey = key;
    this.n = { field: 'WORK_PERFORMED', excerpt: '', body: '', reply: '' };
  }

  async openProcedure(procedure: ReturnType<typeof decodeFieldwork>['procedures'][number]): Promise<void> {
    if (this.procedureCommandBusy()) return;
    const generation = ++this.selectionGeneration;
    ++this.commandGeneration;
    ++this.previewGeneration;
    this.e.procedure = procedure.id;
    this.procedureSelectedId.set(procedure.id);
    this.procedureCommandState.set(null);
    this.procedureCommandPreview.set(null);
    this.procedureCommandReceipt.set(null);
    this.procedureCommandError.set('');
    this.procedureCommandBusy.set(false);
    this.procedureAssent = false;
    this.procedureWork = '';
    this.procedureConclusion = '';
    this.procedureDecision = 'REVIEWED';
    this.procedureComment = '';
    this.procedureManualReference = '';
    this.procedureObservation = '';
    this.procedureLegacyReferences = '';
    this.procedureSelectedLinks.clear();
    this.procedureSelectedPhysical.clear();
    this.procedurePendingRequest = null;
    const reviewRead = this.selectProcedure(procedure.id);
    await this.loadProcedureCommandState(procedure.id, generation);
    if (generation === this.selectionGeneration) await reviewRead;
  }
  selectedProcedure(w: Fieldwork) { return this.reviewProcedureId === this.procedureSelectedId()
    ? w.procedures.find(p => p.id === this.procedureSelectedId()) : undefined; }
  procedureUnknownForSelection() {
    const actor = this.riskLinkSession.current();
    return this.procedureUnknowns().find(pending => pending.engagementId === this.id() &&
      pending.actorUserId === actor?.userId && pending.actorGeneration === actor?.generation &&
      pending.procedureId === this.procedureSelectedId()) ?? null;
  }
  private retainUnknown(pending: PendingProcedureCommand): void {
    this.procedureUnknowns.update(current => current.some(item => item.requestId === pending.requestId)
      ? current : [...current, pending]);
    this.procedureUnknown.set(pending);
  }
  private resolveUnknown(requestId: string): void {
    const remaining = this.procedureUnknowns().filter(pending => pending.requestId !== requestId);
    this.procedureUnknowns.set(remaining);
    this.procedureUnknown.set(remaining.at(-1) ?? null);
  }
  procedureInputChanged(): void {
    ++this.previewGeneration;
    this.procedureCommandPreview.set(null);
    this.procedureAssent = false;
    this.procedurePendingRequest = null;
  }
  toggleProcedureLink(linkId: string, checked: boolean): void {
    if (checked) this.procedureSelectedLinks.add(linkId); else this.procedureSelectedLinks.delete(linkId);
    this.procedureInputChanged();
  }
  toggleProcedurePhysical(linkId: string, checked: boolean): void {
    if (checked) this.procedureSelectedPhysical.add(linkId); else this.procedureSelectedPhysical.delete(linkId);
    this.procedureInputChanged();
  }
  private async loadProcedureCommandState(procedureId: string, generation = this.selectionGeneration): Promise<void> {
    const owner = this.captureOwner();
    try {
      const state = await this.api.get(`/api/ui/procedures/${procedureId}/commands/state`, decodeProcedureCommandState);
      if (owner() && generation === this.selectionGeneration && this.procedureSelectedId() === procedureId) this.procedureCommandState.set(state);
    } catch (e) {
      if (owner() && generation === this.selectionGeneration && this.procedureSelectedId() === procedureId) this.procedureCommandError.set((e as Error).message);
    }
  }
  private procedureEvidenceSelections(): unknown[] {
    const selections: Record<string, unknown>[] = [];
    for (const linkId of this.procedureSelectedLinks) selections.push({ kind: 'STORED_LINK', linkId });
    for (const physicalLinkId of this.procedureSelectedPhysical) selections.push({ kind: 'PHYSICAL_LINK', physicalLinkId });
    if (this.procedureManualReference.trim() || this.procedureObservation.trim())
      selections.push({ kind: 'MANUAL_REFERENCE', manualReference: this.procedureManualReference.trim(),
        observation: this.procedureObservation.trim() });
    return selections;
  }
  private procedureActionRequest(action: string): Record<string, unknown> | null {
    if (!this.procedureSelectedId() || this.reviewProcedureId !== this.procedureSelectedId()) return null;
    const state = this.procedureCommandState();
    if (!state) return null;
    const basis = state.basis, review = action === 'REVIEW_RESULT';
    return {
      requestId: globalThis.crypto.randomUUID(), action,
      expectedDefinitionRevision: basis.definitionRevision, expectedInputGeneration: basis.inputGeneration,
      expectedResultRevision: review ? basis.latestResultRevision : basis.currentResultRevision,
      resultId: review ? basis.latestResultId : null,
      workPerformed: review ? null : this.procedureWork.trim(),
      structuredResultJson: review ? null : '{}',
      evidenceReferences: review ? null : this.procedureLegacyReferences.split('\n').map(x => x.trim()).filter(x => x),
      conclusion: review ? null : this.procedureConclusion.trim(),
      decision: review ? this.procedureDecision : null,
      comment: review ? (this.procedureComment.trim() || null) : null,
      evidenceSelections: review ? null : this.procedureEvidenceSelections(),
      reviewed: false, expectedRequestHash: null,
    };
  }
  async prepareProcedureCommand(action: string): Promise<void> {
    const procedureId = this.procedureSelectedId();
    if (this.procedureUnknownForSelection()) {
      this.procedureCommandError.set('Reconcile the exact pending request before preparing another command for this procedure.');
      return;
    }
    if (!procedureId || this.procedureCommandBusy()) return;
    const owner = this.captureOwner(), generation = ++this.commandGeneration, selection = this.selectionGeneration,
      previewGeneration = ++this.previewGeneration;
    const current = () => owner() && generation === this.commandGeneration && selection === this.selectionGeneration &&
      previewGeneration === this.previewGeneration &&
      this.procedureSelectedId() === procedureId && !this.reviewDestroyed;
    const request = this.procedureActionRequest(action);
    if (!request) return;
    this.procedureCommandBusy.set(true);
    this.procedureCommandError.set('');
    this.procedureCommandPreview.set(null);
    this.procedureAssent = false;
    try {
      const outcome = await this.api.command(`/api/ui/procedures/${procedureId}/commands/preview`, request);
      if (!current()) return;
      if (!outcome.ok) { this.procedurePendingRequest = null; this.procedureCommandError.set(outcome.message); return; }
      this.procedurePendingRequest = request;
      this.procedureCommandPreview.set(decode(decodeProcedureCommandPreview, outcome.value));
    } catch {
      if (current()) this.procedureCommandError.set('The procedure command preview could not be verified. Refresh and review again.');
    } finally { if (generation === this.commandGeneration) this.procedureCommandBusy.set(false); }
  }
  async executeProcedureCommand(): Promise<void> {
    const procedureId = this.procedureSelectedId(), preview = this.procedureCommandPreview(), request = this.procedurePendingRequest;
    if (!procedureId || !preview || !request || !this.procedureAssent || this.procedureCommandBusy() || this.procedureUnknownForSelection()) return;
    const owner = this.captureOwner(), generation = ++this.commandGeneration, selection = this.selectionGeneration;
    const actor = this.riskLinkSession.current(), engagementId = this.id();
    const current = () => owner() && generation === this.commandGeneration && selection === this.selectionGeneration &&
      this.procedureSelectedId() === procedureId && !this.reviewDestroyed;
    this.procedureCommandBusy.set(true);
    this.procedureCommandError.set('');
    try {
      const outcome = await this.api.command(`/api/ui/procedures/${procedureId}/commands/actions`,
        { ...request, reviewed: true, expectedRequestHash: preview.requestHash });
      if (!current()) {
        if (!outcome.ok && outcome.unknown && actor && engagementId)
          this.retainUnknown({ engagementId, actorUserId: actor.userId, actorGeneration: actor.generation,
            procedureId, requestId: request['requestId'] as string, requestHash: preview.requestHash });
        return;
      }
      if (outcome.ok) {
        this.procedureCommandReceipt.set(decode(decodeProcedureCommandReceipt, outcome.value));
        this.procedureCommandPreview.set(null);
        this.procedureAssent = false;
        this.procedurePendingRequest = null;
        this.ws.reload();
        await this.loadProcedureCommandState(procedureId);
        await this.selectProcedure(procedureId);
      } else if (outcome.unknown) {
        if (actor && engagementId) this.retainUnknown({ engagementId, actorUserId: actor.userId, actorGeneration: actor.generation,
          procedureId, requestId: request['requestId'] as string, requestHash: preview.requestHash });
        this.procedureCommandError.set(outcome.message);
      } else {
        this.procedureCommandError.set(outcome.message);
      }
    } catch {
      if (actor && engagementId) this.retainUnknown({ engagementId, actorUserId: actor.userId, actorGeneration: actor.generation,
        procedureId, requestId: request['requestId'] as string, requestHash: preview.requestHash });
      if (current()) this.procedureCommandError.set('The outcome could not be verified. Retain the request identity and recover it before retrying.');
    } finally { if (generation === this.commandGeneration) this.procedureCommandBusy.set(false); }
  }
  async recoverProcedureCommand(): Promise<void> {
    const procedureId = this.procedureSelectedId(), pending = this.procedureUnknownForSelection();
    const actor = this.riskLinkSession.current();
    if (!procedureId || !pending || pending.procedureId !== procedureId || pending.engagementId !== this.id() ||
        pending.actorUserId !== actor?.userId || pending.actorGeneration !== actor?.generation || this.procedureCommandBusy()) return;
    const owner = this.captureOwner(), generation = ++this.commandGeneration, selection = this.selectionGeneration;
    const current = () => owner() && generation === this.commandGeneration && selection === this.selectionGeneration &&
      this.procedureSelectedId() === procedureId && !this.reviewDestroyed;
    this.procedureCommandBusy.set(true);
    this.procedureCommandError.set('');
    try {
      const lookup = await this.api.get(`/api/ui/procedures/${procedureId}/commands/receipts/${pending.requestId}?requestHash=${pending.requestHash}`, decodeProcedureCommandLookup);
      if (!current()) return;
      if (lookup.found && lookup.receipt) {
        this.procedureCommandReceipt.set(lookup.receipt);
        this.resolveUnknown(pending.requestId);
        this.ws.reload();
        await this.loadProcedureCommandState(procedureId);
        await this.selectProcedure(procedureId);
      } else {
        this.procedureCommandError.set('The original outcome is not retained yet. Do not resend a new identity; refresh and reconcile persisted state.');
      }
    } catch (e) { if (current()) this.procedureCommandError.set((e as Error).message); }
    finally { if (generation === this.commandGeneration) this.procedureCommandBusy.set(false); }
  }

  private refreshReview = () => { void this.selectProcedure(this.e.procedure); };
  linkEvidence(): void {
    if (!this.e.procedure || !this.e.upload) return this.invalid('Choose a procedure and a received upload.');
    void this.send(`/api/ui/procedures/${this.e.procedure}/evidence`, { uploadIntentId: this.e.upload, note: this.e.note || null }, 'Evidence linked.', this.refreshReview);
  }
  register(): void {
    const index = this.ph.index;
    void this.send(this.base() + '/physical', { fileIndex: index, boxReference: this.ph.box, description: this.ph.description, location: this.ph.location },
      `Registered ${index.trim().toUpperCase()}.`, () => (this.ph = { ...this.ph, index: '', box: '', description: '', location: '' }));
  }
  linkPhysical(itemId: string): void { void this.send(`/api/ui/physical-items/${itemId}/link`, { procedureId: this.ph.linkTarget }, 'Physical file linked.'); }
  move(itemId: string): void { void this.send(`/api/ui/physical-items/${itemId}/move`, { toLocation: this.ph.moveTo }, 'Movement recorded.', () => (this.ph.moveTo = '')); }
  adHoc(): void {
    void this.send(this.base() + '/ad-hoc', { title: this.ah.title, wording: this.ah.wording, reason: this.ah.reason, sectionTitle: this.ah.section || null },
      'Ad hoc step inserted; it must be performed and reviewed before completion.', () => (this.ah = { title: '', wording: '', reason: '', section: '' }));
  }
  addNote(resultId: string): void {
    if (this.reviewProcedureId !== this.e.procedure || this.noteContextKey !== `${this.e.procedure}:${resultId}` ||
        this.review()?.currentResult?.id !== resultId) return this.invalid('The selected procedure result changed. Refresh its review before adding a note.');
    void this.send('/api/ui/review-notes', { resultId, field: this.n.field, excerpt: this.n.excerpt, body: this.n.body }, 'Note added.',
      () => { this.n = { ...this.n, excerpt: '', body: '' }; this.refreshReview(); });
  }
  noteEvent(noteId: string, kind: 'RESPONSE' | 'RESOLVED'): void {
    if (this.reviewProcedureId !== this.e.procedure || !this.review()?.notes.some(note => note.noteId === noteId))
      return this.invalid('The selected review note is no longer current. Refresh the procedure review before responding.');
    void this.send(`/api/ui/review-notes/${noteId}/events`, { kind, body: this.n.reply }, kind === 'RESOLVED' ? 'Note resolved.' : 'Response added.',
      () => { this.n.reply = ''; this.refreshReview(); });
  }
  history(m: { toLocation: string; movedAt: string }[]): string { return m.map((x) => `${x.toLocation} (${x.movedAt.slice(0, 10)})`).join(' → '); }
}
