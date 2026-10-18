import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { Api, CommandState, routeGuid } from '../../core/api';
import { decodeFieldwork, decodeManagerReview, decodeProgramOptions, focusRisks, type Fieldwork } from './fieldwork-contracts';
import { SessionService } from '../../core/session';
import { PlanningCommandState } from './planning-command-state';
import { PlanningCommandReview } from './planning-command-review';
import { SHARED } from '../../core/ui';
import { TabDrafts } from '../../core/tab-drafts';
import { FieldworkProcedureState } from './fieldwork-procedure-state';
import { FieldworkSamplingState } from './fieldwork-sampling-state';

@Component({
  selector: 'audit-fieldwork',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatTabsModule, PlanningCommandReview, ...SHARED],
  templateUrl: './fieldwork.html',
})
export class AuditFieldwork {
  private readonly api = inject(Api);
  private readonly tabDrafts = inject(TabDrafts);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
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
  readonly cmd = new CommandState(this.api);
  readonly riskLink = new PlanningCommandState(() => this.id(), () => this.ws.data(),
    () => !!this.ws.data()?.canManageFieldwork && !this.ws.loading() && !this.ws.error(),
    () => this.cmd.busy() || this.cmd.uncertain(), () => this.ws.reload(), ['LINK_PROCEDURE_RISK']);
  readonly section = signal<number | null>(null);
  readonly toolTab = signal(0);
  readonly procedure: FieldworkProcedureState;
  readonly sampling: FieldworkSamplingState;
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
      captureOwner: () => this.captureOwner(), destroyRef: this.destroyRef,
    };
    this.procedure = new FieldworkProcedureState(context);
    this.sampling = new FieldworkSamplingState({ ...context });
    this.destroyRef.onDestroy(() => { this.destroyed = true; ++this.ownerGeneration; ++this.commandGeneration; });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation();
      untracked(() => this.observeOwner());
    });
    effect(() => {
      this.ws.data();
      untracked(() => this.procedure.workspaceChanged());
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
    this.procedure.ownerChanged(); this.sampling.ownerChanged();
    this.cmd.busy.set(false); this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.code.set('');
    this.refreshPriorScopeNotice();
  }
  private captureOwner(): () => boolean {
    const engagement = this.id(), actor = this.riskLinkSession.current(), epoch = this.riskLinkSession.invalidation(), generation = this.ownerGeneration;
    return () => !this.destroyed && generation === this.ownerGeneration && engagement === this.id() &&
      actor === this.riskLinkSession.current() && epoch === this.riskLinkSession.invalidation();
  }
  private refreshPriorScopeNotice(): void {
    this.previousScopeOutcomeUncertain.set(this.priorScopeCommandUnknown || this.pendingPriorScopeCommands.size > 0);
  }

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
    if (this.cmd.busy() || this.cmd.uncertain()) return Promise.resolve(false);
    const owner = this.captureOwner(), operation = ++this.commandGeneration;
    this.cmd.busy.set(true); this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.code.set('');
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
      this.cmd.busy.set(false);
      if (owner()) this.ws.reload();
    });
  }
  aggregate(w: Fieldwork): void { void this.send(`/api/ui/engagements/${this.id()}/fieldwork/aggregate`, { conclusion: this.aggregateConclusion }, 'Aggregate conclusion recorded.'); }

}
