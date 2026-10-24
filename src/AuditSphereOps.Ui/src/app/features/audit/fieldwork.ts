import { DatePipe } from '@angular/common';
import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, HostListener, inject, Injector, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { Api, CommandState, routeGuid } from '../../core/api';
import { decodeBankReconciliationDetail, decodeBankReconciliationPage, decodeFieldwork, decodeManagerReview, decodeProgramOptions, focusRisks, type Fieldwork } from './fieldwork-contracts';
import { SessionService } from '../../core/session';
import { PlanningCommandState } from './planning-command-state';
import { PlanningCommandReview } from './planning-command-review';
import { SHARED } from '../../core/ui';
import { TabDrafts } from '../../core/tab-drafts';
import { FieldworkProcedureState } from './fieldwork-procedure-state';
import { ProcedureSubmittedEvidence } from './procedure-submitted-evidence';
import { FieldworkSamplingState } from './fieldwork-sampling-state';
import { FieldworkSourceIntakeState } from './fieldwork-source-intake-state';
import type { FieldworkCommandAcquire, FieldworkCommandRecovery } from './fieldwork-command-context';

@Component({
  selector: 'audit-fieldwork',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatTabsModule, PlanningCommandReview, ProcedureSubmittedEvidence, ...SHARED],
  templateUrl: './fieldwork.html',
  styles: '.submitted-evidence-json { white-space:pre-wrap; overflow-wrap:anywhere; max-inline-size:100%; }',
})
export class AuditFieldwork {
  private readonly api = inject(Api);
  private readonly tabDrafts = inject(TabDrafts);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly samplingPreviewTrigger = viewChild<unknown, ElementRef<HTMLButtonElement>>('samplingPreviewTrigger', { read: ElementRef });
  private readonly renderInjector = inject(Injector);
  private readonly samplingPreviewHeading = viewChild<ElementRef<HTMLElement>>('samplingPreviewHeading');
  private readonly procedurePanel = viewChild<ElementRef<HTMLElement>>('procedurePanel');
  private readonly procedurePreviewHeading = viewChild<ElementRef<HTMLElement>>('procedurePreviewHeading');
  private focusInteraction = 0;
  readonly id = routeGuid();
  readonly statementFocus = { line: this.route.snapshot?.queryParamMap.get('line') ?? null, area: this.route.snapshot?.queryParamMap.get('area') ?? null };
  readonly focusedRisks = computed(() => focusRisks(this.ws.data()?.risks ?? [], this.statementFocus.line));
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/fieldwork` : null), decodeFieldwork,
    'Fieldwork unavailable: an authorized internal engagement scope is required.');
  readonly programOptions = this.api.resource(() => this.id() && this.ws.data()?.canManageFieldwork
    ? `/api/ui/engagements/${this.id()}/fieldwork/program-options` : null, decodeProgramOptions,
    'Published programme options unavailable for this authorized engagement.');
  readonly managerReview = this.api.resource(() => this.id() && this.ws.data()?.canReviewSelections
    ? `/api/ui/engagements/${this.id()}/manager-review` : null, decodeManagerReview,
    'Manager review summary unavailable: an authorized reviewer scope is required.');
  readonly bankInspectionOpen = signal(false);
  readonly bankOffset = signal(0);
  readonly bankSelection = signal<string | null>(null);
  readonly bankItemOffset = signal(0);
  private readonly bankInspectionHeading = viewChild<ElementRef<HTMLElement>>('bankInspectionHeading');
  private bankFocusRequest: { current: () => boolean; interaction: number; origin: HTMLElement; version: number } | null = null;
  private bankFocusVersion = 0;
  readonly bankList = this.api.resource(() => this.bankInspectionOpen() && this.id() && this.ws.data()?.canReviewSelections && this.managerReview.data()
    ? `/api/ui/engagements/${this.id()}/fieldwork/bank-reconciliations?offset=${this.bankOffset()}&limit=25` : null,
    (raw, path) => {
      const page = decodeBankReconciliationPage(raw, path);
      if (page.engagementId !== this.id() || page.offset !== this.bankOffset() || page.limit !== 25)
        throw new Error('Bank evidence does not match this engagement or page.');
      return page;
    }, 'Bank reconciliation inspection is unavailable in your current reviewer scope.');
  readonly bankDetail = this.api.resource(() => this.bankList.data()?.rows.some(row => row.id === this.bankSelection())
    ? `/api/ui/engagements/${this.id()}/fieldwork/bank-reconciliations/${this.bankSelection()}?offset=${this.bankItemOffset()}&limit=100` : null,
    (raw, path) => {
      const detail = decodeBankReconciliationDetail(raw, path);
      if (detail.engagementId !== this.id() || detail.reconciliation.id !== this.bankSelection() ||
          detail.offset !== this.bankItemOffset() || detail.limit !== 100)
        throw new Error('Bank evidence does not match the selected record or page.');
      return detail;
    }, 'The selected bank reconciliation is unavailable in your current reviewer scope.');
  readonly cmd = new CommandState(this.api);
  readonly riskLink = new PlanningCommandState(() => this.id(), () => this.ws.data(),
    () => !!this.ws.data()?.canManageFieldwork && !this.ws.loading() && !this.ws.error(),
    () => this.otherFieldworkCommandBlocked(), () => this.ws.reload(), ['LINK_PROCEDURE_RISK']);
  readonly section = signal<number | null>(null);
  readonly toolTab = signal(0);
  private readonly samplingToolTabIndex = 1;
  readonly procedure: FieldworkProcedureState;
  readonly sampling: FieldworkSamplingState;
  readonly sourceIntake: FieldworkSourceIntakeState;
  private activeEngagementId: string | null = null;
  private readonly riskLinkSelections = new Map<string, string>();
  private readonly riskLinkBases = new Map<string, string | null>();
  private readonly riskLinkSession = inject(SessionService);
  aggregateConclusion = '';
  readonly previousScopeOutcomeUncertain = signal(false);
  private ownerGeneration = 0;
  private commandGeneration = 0;
  private observedOwner = '';
  private destroyed = false;
  private readonly pendingPriorScopeCommands = new Set<number>();
  private priorScopeCommandUnknown = false;

  constructor() {
    const context = {
      api: this.api, tabDrafts: this.tabDrafts, id: this.id, ws: this.ws, cmd: this.cmd,
      session: this.riskLinkSession, send: (url: string, body: unknown, ok: string, after?: () => void) => this.send(url, body, ok, after),
      acquireCommand: (recovery?: FieldworkCommandRecovery) => this.acquireFieldworkCommand(recovery),
      captureOwner: () => this.captureOwner(), destroyRef: this.destroyRef,
    };
    this.procedure = new FieldworkProcedureState(context);
    this.sampling = new FieldworkSamplingState({ ...context });
    this.sourceIntake = new FieldworkSourceIntakeState({
      id: this.id, api: this.api, session: this.riskLinkSession, drafts: this.tabDrafts,
      canManage: () => !!this.ws.data()?.canManageFieldwork && !this.ws.loading() && !this.ws.error(),
      canReview: () => !!this.ws.data()?.canReviewSelections && !this.ws.loading() && !this.ws.error(),
      acquireCommand: this.acquireFieldworkCommand, captureOwner: () => this.captureOwner(), reload: () => this.ws.reload(),
    });
    this.destroyRef.onDestroy(() => { this.destroyed = true; ++this.ownerGeneration; ++this.commandGeneration; });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation();
      untracked(() => this.observeOwner());
    });
    effect(() => {
      this.ws.data();
      untracked(() => { this.procedure.workspaceChanged(); this.sourceIntake.workspaceChanged(); });
    });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation();
      untracked(() => { this.riskLinkSelections.clear(); this.riskLinkBases.clear(); });
    });
    effect(() => {
      const w = this.ws.data();
      untracked(() => {
        if (!w) return;
        if (!w.canManageFieldwork) this.riskLinkSelections.clear();
        for (const [id] of this.riskLinkSelections) if (!w.procedures.some(p => p.id === id)) this.riskLinkSelections.delete(id);
        for (const item of w.procedures) {
          if (this.riskLinkBases.has(item.id) && this.riskLinkBases.get(item.id) !== item.riskId) this.riskLinkSelections.delete(item.id);
          this.riskLinkBases.set(item.id, item.riskId);
        }
      });
    });
    effect(() => {
      this.bankList.data(); this.bankList.error(); const listLoading = this.bankList.loading();
      this.bankDetail.data(); this.bankDetail.error(); const detailLoading = this.bankDetail.loading();
      untracked(() => {
        const request = this.bankFocusRequest;
        if (!request || listLoading || detailLoading || !this.bankInspectionOpen()) return;
        this.bankFocusRequest = null;
        afterNextRender(() => {
          const doc = request.origin.ownerDocument;
          if (!request.current() || request.version !== this.bankFocusVersion || request.interaction !== this.focusInteraction ||
              !this.bankInspectionOpen() || (doc.activeElement !== request.origin && doc.activeElement !== doc.body)) return;
          this.bankInspectionHeading()?.nativeElement.focus();
        }, { injector: this.renderInjector });
      });
    });
    // Aggregate conclusion presentation stays in the host; sampling history is owned by its state object.
    effect(() => {
      const w = this.ws.data();
      this.ws.loading(); this.ws.error();
      const engagementId = this.id();
      untracked(() => {
        if (engagementId !== this.activeEngagementId) {
          this.activeEngagementId = engagementId;
          this.riskLinkSelections.clear();
        }
        this.aggregateConclusion = w?.aggregate?.conclusion ?? '';
        this.sampling.workspaceUnavailable();
        this.sourceIntake.workspaceUnavailable();
      });
    });
  }
  private observeOwner(): void {
    const actor = this.riskLinkSession.current();
    const owner = `${this.id() ?? ''}:${actor?.userId ?? ''}:${actor?.generation ?? ''}:${this.riskLinkSession.invalidation()}`;
    if (owner === this.observedOwner) return;
    this.observedOwner = owner;
    const wasBusy = this.cmd.busy();
    if (wasBusy) this.pendingPriorScopeCommands.add(this.commandGeneration);
    ++this.ownerGeneration; ++this.commandGeneration;
    this.procedure.ownerChanged(); this.sampling.ownerChanged(); this.sourceIntake.ownerChanged();
    this.bankInspectionOpen.set(false); this.bankSelection.set(null); this.bankOffset.set(0); this.bankItemOffset.set(0);
    this.bankFocusRequest = null; ++this.bankFocusVersion;
    this.cmd.busy.set(false); this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.code.set('');
    this.refreshPriorScopeNotice();
  }
  private requestBankFocus(trigger: EventTarget | null): void {
    ++this.bankFocusVersion;
    this.bankFocusRequest = trigger instanceof HTMLElement && trigger.ownerDocument.activeElement === trigger
      ? { origin: trigger, interaction: this.focusInteraction, current: this.captureOwner(), version: this.bankFocusVersion } : null;
  }
  openBankInspection(trigger: EventTarget | null): void {
    if (!this.ws.data()?.canReviewSelections || !this.managerReview.data()) return;
    if (this.bankInspectionOpen()) { this.bankInspectionHeading()?.nativeElement.focus(); return; }
    this.requestBankFocus(trigger);
    this.bankInspectionOpen.set(true);
  }
  closeBankInspection(trigger: EventTarget | null): void {
    const ownsFocus = trigger instanceof HTMLElement && trigger.ownerDocument.activeElement === trigger;
    // The count trigger remains mounted: return focus before removing the inspection panel.
    if (ownsFocus) this.bankInspectionTrigger()?.nativeElement.focus();
    this.bankInspectionOpen.set(false); this.bankSelection.set(null); this.bankFocusRequest = null; ++this.bankFocusVersion;
  }
  private readonly bankInspectionTrigger = viewChild<unknown, ElementRef<HTMLButtonElement>>('bankInspectionTrigger', { read: ElementRef });
  bankPage(offset: number, trigger: EventTarget | null): void {
    if (this.bankList.loading() || !this.bankList.data() || offset < 0) return;
    this.requestBankFocus(trigger); this.bankSelection.set(null); this.bankItemOffset.set(0); this.bankOffset.set(offset);
  }
  inspectBank(id: string, trigger: EventTarget | null): void {
    if (!this.bankList.data()?.rows.some(row => row.id === id) || this.bankDetail.loading()) return;
    this.requestBankFocus(trigger); this.bankItemOffset.set(0); this.bankSelection.set(id);
  }
  bankItemsPage(offset: number, trigger: EventTarget | null): void {
    if (this.bankDetail.loading() || !this.bankDetail.data() || offset < 0) return;
    this.requestBankFocus(trigger); this.bankItemOffset.set(offset);
  }
  refreshBankInspection(trigger: EventTarget | null): void {
    if (this.bankList.loading() || this.bankDetail.loading()) return;
    this.requestBankFocus(trigger); this.bankList.reload();
  }
  bankGenerationLabel(state: string): string {
    return state === 'CURRENT_GENERATION' ? 'Matches current input generation' : state === 'STALE_GENERATION'
      ? 'Retained evidence: input generation changed' : 'Current input generation unavailable';
  }
  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction(): void { this.focusInteraction++; }

  async previewSampling(): Promise<void> {
    const trigger = this.samplingPreviewTrigger()?.nativeElement;
    const document = trigger?.ownerDocument;
    const origin = document?.activeElement;
    const ownsFocus = !!origin && !!trigger?.form?.contains(origin);
    const interaction = this.focusInteraction, current = this.captureOwner();
    await this.sampling.sample();
    const preview = this.sampling.samplingPreview();
    if (!document || !ownsFocus || !preview) return;
    afterNextRender(() => {
      if (!current() || interaction !== this.focusInteraction || this.toolTab() !== this.samplingToolTabIndex ||
          this.sampling.samplingPreview() !== preview || !this.sampling.previewMatchesInputs() ||
          (document.activeElement !== origin && document.activeElement !== document.body)) return;
      this.samplingPreviewHeading()?.nativeElement.focus();
    }, { injector: this.renderInjector });
  }

  async previewProcedure(action: 'SUBMIT_RESULT' | 'REVIEW_RESULT'): Promise<void> {
    const panel = this.procedurePanel()?.nativeElement;
    const document = panel?.ownerDocument, origin = document?.activeElement;
    const ownsFocus = !!origin && !!panel?.contains(origin);
    const interaction = this.focusInteraction, current = this.captureOwner();
    await this.procedure.prepareProcedureCommand(action);
    const preview = this.procedure.procedureCommandPreview();
    if (!document || !ownsFocus || !preview) return;
    afterNextRender(() => {
      if (!current() || interaction !== this.focusInteraction ||
          this.procedure.procedureCommandPreview() !== preview ||
          (document.activeElement !== origin && document.activeElement !== document.body)) return;
      this.procedurePreviewHeading()?.nativeElement.focus();
    }, { injector: this.renderInjector });
  }

  private captureOwner(): () => boolean {
    const engagement = this.id(), actor = this.riskLinkSession.current(), epoch = this.riskLinkSession.invalidation(), generation = this.ownerGeneration;
    return () => !this.destroyed && generation === this.ownerGeneration && engagement === this.id() &&
      actor === this.riskLinkSession.current() && epoch === this.riskLinkSession.invalidation();
  }
  private refreshPriorScopeNotice(): void {
    this.previousScopeOutcomeUncertain.set(this.priorScopeCommandUnknown || this.pendingPriorScopeCommands.size > 0);
  }

  private commandBlocked(recovery?: FieldworkCommandRecovery): boolean {
    return this.cmd.uncertain() || this.riskLink.busy() || this.riskLink.uncertain() ||
      this.procedure.hasUnresolvedCommand(recovery) || this.sourceIntake.hasUnresolvedCommand(recovery) ||
      this.sampling.samplingBusy() || this.sampling.sampleActionBusy();
  }
  private otherFieldworkCommandBlocked(): boolean {
    return this.cmd.busy() || this.cmd.uncertain() || this.procedure.hasUnresolvedCommand() || this.sourceIntake.hasUnresolvedCommand() ||
      this.sampling.samplingBusy() || this.sampling.sampleActionBusy();
  }
  fieldworkCommandBlocked(recovery?: FieldworkCommandRecovery): boolean {
    return this.cmd.busy() || this.commandBlocked(recovery);
  }
  private acquireFieldworkCommand: FieldworkCommandAcquire = (recovery) => {
    if (this.fieldworkCommandBlocked(recovery)) return null;
    const owner = this.captureOwner(), operation = ++this.commandGeneration;
    this.cmd.busy.set(true);
    return () => {
      if (operation === this.commandGeneration && owner()) this.cmd.busy.set(false);
    };
  };

  count(w: Fieldwork, status: string): number { return w.procedures.filter((p) => p.applicabilityStatus === status).length; }
  reviewed(w: Fieldwork): number { return w.procedures.filter((p) => p.evidenceState === 'REVIEWED_CURRENT').length; }
  riskSelection(p: Fieldwork['procedures'][number]): string { return this.riskLinkSelections.get(p.id) ?? p.riskId ?? ''; }
  setRiskSelection(procedureId: string, riskId: string): void { this.riskLink.inputChanged(); this.riskLinkSelections.set(procedureId, riskId); }
  riskLabel(w: Fieldwork, riskId: string | null): string {
    if (!riskId) return 'No risk linked';
    const risk = w.risks.find((x) => x.id === riskId);
    return risk ? `${risk.area} · ${risk.effectiveBand ?? risk.band ?? 'Not assessed'}${risk.significanceDecision === 'SIGNIFICANT' ? ' · Significant' : ''}` : 'Linked risk is outside this list';
  }
  riskFor(w: Fieldwork, riskId: string | null) { return riskId ? w.risks.find((x) => x.id === riskId) : undefined; }
  async saveRiskLink(procedureId: string): Promise<void> {
    const w = this.ws.data(), p = w?.procedures.find(p => p.id === procedureId);
    if (!w?.canManageFieldwork || !p || this.cmd.busy() || this.cmd.uncertain()) return;
    const riskId = this.riskSelection(p);
    if (riskId === (p.riskId ?? '') || (riskId && !w.risks.some(r => r.id === riskId))) return;
    await this.riskLink.prepare(() => ({ action: 'LINK_PROCEDURE_RISK', linkProcedureRisk: { procedureId,
      riskId: this.riskSelection(p) || null, expectedRiskId: p.riskId } }));
  }
  applicable(w: Fieldwork) { return w.procedures.filter((p) => p.applicabilityStatus === 'APPLICABLE'); }
  reviewable(w: Fieldwork) { return w.canManageFieldwork ? this.applicable(w) : w.procedures; }
  sections(w: Fieldwork) {
    const map = new Map<number, { key: number; title: string; count: number }>();
    for (const p of w.procedures) {
      const key = p.sourceSectionNumber ?? -2;
      const g = map.get(key) ?? { key, title: p.sourceSectionTitle ?? '', count: 0 };
      g.count++; map.set(key, g);
    }
    return [...map.values()].sort((a, b) => a.key - b.key);
  }
  firstSection(w: Fieldwork): number { return w.procedures.length ? (w.procedures[0].sourceSectionNumber ?? -2) : 0; }
  visible(w: Fieldwork) {
    const s = this.section() ?? this.firstSection(w);
    return s === 0 ? w.procedures : w.procedures.filter((p) => (p.sourceSectionNumber ?? -2) === s);
  }
  send(url: string, body: unknown, ok: string, after?: () => void): Promise<boolean> {
    const release = this.acquireFieldworkCommand();
    if (!release) return Promise.resolve(false);
    const owner = this.captureOwner(), operation = this.commandGeneration;
    this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.code.set('');
    return this.api.command(url, body).then(result => {
      if (!owner() || operation !== this.commandGeneration) {
        if (!result.ok && result.unknown) {
          this.priorScopeCommandUnknown = true;
          this.cmd.uncertain.set(true);
        }
        return false;
      }
      this.cmd.failed.set(!result.ok); this.cmd.code.set(result.ok ? '' : result.code);
      if (!result.ok && result.unknown) this.cmd.uncertain.set(true);
      this.cmd.message.set(result.ok ? ok : result.message);
      if (result.ok) after?.();
      return result.ok;
    }).finally(() => {
      if (operation !== this.commandGeneration) {
        this.pendingPriorScopeCommands.delete(operation);
        this.refreshPriorScopeNotice();
        return;
      }
      release();
      if (owner()) this.ws.reload();
    });
  }
  aggregate(w: Fieldwork): void { void this.send(`/api/ui/engagements/${this.id()}/fieldwork/aggregate`, { conclusion: this.aggregateConclusion }, 'Aggregate conclusion recorded.'); }

}
