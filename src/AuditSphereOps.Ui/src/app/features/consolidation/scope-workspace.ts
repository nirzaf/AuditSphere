import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { ConsolidationOwnership } from './ownership';
import { ConsolidationComponentCorrection } from './component-correction';
import { ConsolidationExternalPacks } from './external-packs';
import { decodeScopeWorkspace, type ScopeWorkspace, type ConsolidationComponent, type IntercompanyException, type IntercompanyExceptionsPage, type IntercompanyMatchOptions } from './scope-contracts';
import { formatOwnershipPercent, isValidOwnershipPercent } from './scope-amounts';
import { ScopeIntercompanyState, type IntercompanyForm } from './scope-intercompany-state';
import { ScopeJournalState, type NewJournalLine } from './scope-journal-state';

export { formatOwnershipPercent } from './scope-amounts';

@Component({
  selector: 'audit-consolidation-scope-workspace',
  imports: [FormsModule, RouterLink, MatButtonModule, ConsolidationOwnership, ConsolidationComponentCorrection, ConsolidationExternalPacks, ...SHARED],
  templateUrl: './scope-workspace.html',
})
export class ConsolidationScopeWorkspace {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private destroyed = false;
  readonly id = routeGuid();
  readonly ownerKey = computed(() => {
    const scopeId = this.id()?.toLowerCase() ?? '';
    const session = this.session.current();
    return [scopeId, this.session.invalidation(), session?.firmId ?? '', session?.userId ?? '', session?.generation ?? ''].join(':');
  });
  readonly componentsPageNumber = signal(1);
  readonly readinessPageNumber = signal(1);
  readonly eligiblePackagesPageNumber = signal(1);
  readonly journalsPageNumber = signal(1);
  readonly runsPageNumber = signal(1);
  readonly selectedComponentCorrection = signal<{ scopeId: string; componentId: string } | null>(null);
  readonly externalPacksScopeId = signal<string | null>(null);
  readonly Math = Math;
  readonly ws = this.api.resource(
    () => this.id()
      ? `/api/ui/consolidation/scopes/${this.id()}?eligiblePackagesPage=${this.eligiblePackagesPageNumber()}&journalsPage=${this.journalsPageNumber()}&runsPage=${this.runsPageNumber()}&componentsPage=${this.componentsPageNumber()}&readinessPage=${this.readinessPageNumber()}`
      : null,
    (value, path) => {
      const workspace = decodeScopeWorkspace(value, path);
      if (workspace.scope.id.toLowerCase() !== this.id()?.toLowerCase() ||
          workspace.componentPaging.page !== this.componentsPageNumber() ||
          (workspace.readinessPaging !== null && workspace.readinessPaging.page !== this.readinessPageNumber()) ||
          workspace.eligiblePackages.page !== this.eligiblePackagesPageNumber() ||
          workspace.journals.page !== this.journalsPageNumber() || workspace.runs.page !== this.runsPageNumber())
        throw new Error('Consolidation history belongs to a different scope or page.');
      return workspace;
    },
    'The requested consolidation scope is not available in the current firm and group grant.',
  );
  readonly cmd = new CommandState(this.api);
  readonly intercompany = new ScopeIntercompanyState(
    this.api,
    () => this.id(),
    () => this.ws.data(),
    () => this.ownerKey(),
    (owner, url, body, success, after) => this.runScopedCommand(owner, url, body, success, after),
    () => this.ws.reload(),
  );
  readonly journal = new ScopeJournalState(
    () => this.ownerKey(),
    (owner, url, body, success, after, settled) => this.runScopedCommand(owner, url, body, success, after, settled),
    () => this.ws.data(),
    (page) => this.journalsPageNumber.set(page),
    () => this.ws.reload(),
  );

  private previousOwnerKey: string | null = null;
  private readonly resetPageStateOnOwnerChange = effect(() => {
    const owner = this.ownerKey();
    untracked(() => {
      if (owner === this.previousOwnerKey) return;
      this.previousOwnerKey = owner;
      this.componentsPageNumber.set(1);
      this.readinessPageNumber.set(1);
      this.eligiblePackagesPageNumber.set(1);
      this.journalsPageNumber.set(1);
      this.runsPageNumber.set(1);
      this.selectedComponentCorrection.set(null);
      this.externalPacksScopeId.set(null);
      this.selectedPackageId = '';
      this.journal.resetForOwnerChange();
    });
  });

  constructor() {
    this.destroyRef.onDestroy(() => { this.destroyed = true; });
  }

  get intercompanyNatures() { return this.intercompany.intercompanyNatures; }
  get intercompanyMembersPageNumber() { return this.intercompany.intercompanyMembersPageNumber; }
  get intercompanyMemberSearch() { return this.intercompany.intercompanyMemberSearch; }
  get intercompanyMatchOptions() { return this.intercompany.intercompanyMatchOptions; }
  get intercompanyUnresolvedOnly() { return this.intercompany.intercompanyUnresolvedOnly; }
  get intercompanyPageNumber() { return this.intercompany.intercompanyPageNumber; }
  get intercompanyMatches() { return this.intercompany.intercompanyMatches; }
  get intercompanyForm(): IntercompanyForm { return this.intercompany.intercompanyForm; }
  set intercompanyForm(value: IntercompanyForm) { this.intercompany.intercompanyForm = value; }
  get journalNumber() { return this.journal.journalNumber; }
  set journalNumber(value: string) { this.journal.journalNumber = value; }
  get journalType() { return this.journal.journalType; }
  set journalType(value: string) { this.journal.journalType = value; }
  get journalCurrency() { return this.journal.journalCurrency; }
  set journalCurrency(value: string) { this.journal.journalCurrency = value; }
  get journalEvidence() { return this.journal.journalEvidence; }
  set journalEvidence(value: string) { this.journal.journalEvidence = value; }
  get journalLines() { return this.journal.journalLines; }
  get journalAmountsValid() { return this.journal.journalAmountsValid; }
  get totalDebits() { return this.journal.totalDebits; }
  get totalCredits() { return this.journal.totalCredits; }
  get isJournalBalanced() { return this.journal.isJournalBalanced; }
  get journalDifference() { return this.journal.journalDifference; }

  private async runScopedCommand<T>(
    owner: string,
    url: string,
    body: unknown,
    success: string,
    after?: (value: T) => void,
    settled?: () => void,
  ): Promise<boolean> {
    if (this.cmd.busy() || this.cmd.uncertain()) return false;
    this.cmd.busy.set(true);
    this.cmd.message.set('');
    try {
      const outcome = await this.api.command<T>(url, body, 'POST');
      const ownerIsCurrent = !this.destroyed && owner === this.ownerKey();
      if (!ownerIsCurrent) {
        if (!outcome.ok && outcome.unknown) {
          this.cmd.failed.set(true);
          this.cmd.code.set(outcome.code);
          this.cmd.uncertain.set(true);
          this.cmd.message.set('A previous-scope action has an unconfirmed outcome. Reopen that scope and refresh before repeating it.');
        }
        return false;
      }
      this.cmd.failed.set(!outcome.ok);
      this.cmd.code.set(outcome.ok ? '' : outcome.code);
      if (!outcome.ok && outcome.unknown) this.cmd.uncertain.set(true);
      this.cmd.message.set(outcome.ok ? success : outcome.message);
      if (outcome.ok) after?.(outcome.value);
      return outcome.ok;
    } finally {
      this.cmd.busy.set(false);
      if (!this.destroyed && owner === this.ownerKey()) settled?.();
    }
  }

  intercompanyCountLabel(workspace: ScopeWorkspace): string { return this.intercompany.countLabel(workspace); }
  selectIntercompanyMember(side: 'SELLER' | 'BUYER', clientId: string): void { this.intercompany.selectMember(side, clientId); }
  changeIntercompanyOutside(field: 'enabled' | 'side' | 'clientId', value: string | boolean): void { this.intercompany.changeOutside(field, value); }
  searchIntercompanyMembers(search: string): void { this.intercompany.searchMembers(search); }
  previousIntercompanyMembersPage(): void { this.intercompany.previousMembersPage(); }
  nextIntercompanyMembersPage(page: NonNullable<IntercompanyMatchOptions['memberPaging']>): void { this.intercompany.nextMembersPage(page); }
  intercompanyMemberOptions(options: IntercompanyMatchOptions): IntercompanyMatchOptions['members'] { return this.intercompany.memberOptions(options); }
  isCurrentIntercompanyRevision(workspace: ScopeWorkspace, options: IntercompanyMatchOptions): boolean { return this.intercompany.isCurrentRevision(workspace, options); }
  isCurrentIntercompanyPageRevision(workspace: ScopeWorkspace, page: IntercompanyExceptionsPage): boolean { return this.intercompany.isCurrentPageRevision(workspace, page); }
  canSubmitIntercompanyMatch(workspace: ScopeWorkspace, options: IntercompanyMatchOptions): boolean { return this.intercompany.canSubmit(workspace, options); }
  intercompanyDifferenceReasonRequired(): boolean { return this.intercompany.differenceReasonRequired(); }
  submitIntercompanyMatch(workspace: ScopeWorkspace, options: IntercompanyMatchOptions): void { this.intercompany.submit(workspace, options); }
  showIntercompanyHistory(): void { this.intercompany.showHistory(); }
  showIntercompanyExceptions(): void { this.intercompany.showExceptions(); }
  previousIntercompanyPage(): void { this.intercompany.previousPage(); }
  nextIntercompanyPage(page: IntercompanyExceptionsPage): void { this.intercompany.nextPage(page); }
  isIntercompanyApprovalReviewed(matchId: string): boolean { return this.intercompany.isApprovalReviewed(matchId); }
  setIntercompanyApprovalReviewed(matchId: string, reviewed: boolean): void { this.intercompany.setApprovalReviewed(matchId, reviewed); }
  canApproveIntercompanyMatch(workspace: ScopeWorkspace, item: IntercompanyException, page: IntercompanyExceptionsPage): boolean { return this.intercompany.canApprove(workspace, item, page); }
  approveIntercompanyMatch(workspace: ScopeWorkspace, item: IntercompanyException, page: IntercompanyExceptionsPage): void { this.intercompany.approve(workspace, item, page); }
  invalidateIntercompanyReview(): void { this.intercompany.invalidateReview(); }

  previousComponentsPage(): void {
    this.componentsPageNumber.update(page => Math.max(1, page - 1));
    this.selectedComponentCorrection.set(null);
  }
  nextComponentsPage(page: ScopeWorkspace['componentPaging']): void {
    if (page.page * page.pageSize < page.totalCount) {
      this.componentsPageNumber.set(page.page + 1);
      this.selectedComponentCorrection.set(null);
    }
  }
  previousReadinessPage(): void { this.readinessPageNumber.update(page => Math.max(1, page - 1)); }
  nextReadinessPage(page: NonNullable<ScopeWorkspace['readinessPaging']>): void {
    if (page.page * page.pageSize < page.totalCount) this.readinessPageNumber.set(page.page + 1);
  }
  previousEligiblePackagesPage(): void {
    this.eligiblePackagesPageNumber.update(page => Math.max(1, page - 1));
    this.selectedPackageId = '';
  }
  nextEligiblePackagesPage(page: ScopeWorkspace['eligiblePackages']): void {
    if (page.page * page.pageSize < page.totalCount) {
      this.eligiblePackagesPageNumber.set(page.page + 1);
      this.selectedPackageId = '';
    }
  }
  previousJournalsPage(): void { this.journalsPageNumber.update(page => Math.max(1, page - 1)); }
  nextJournalsPage(page: ScopeWorkspace['journals']): void {
    if (page.page * page.pageSize < page.totalCount) this.journalsPageNumber.set(page.page + 1);
  }
  previousRunsPage(): void { this.runsPageNumber.update(page => Math.max(1, page - 1)); }
  nextRunsPage(page: ScopeWorkspace['runs']): void {
    if (page.page * page.pageSize < page.totalCount) this.runsPageNumber.set(page.page + 1);
  }

  selectedPackageId = '';
  ownershipPercent = '100';
  controlMethod = 'CONTROLLED';
  isValidOwnershipPercent(value: string): boolean { return isValidOwnershipPercent(value); }
  ownershipPercentDisplay(value: string): string { return formatOwnershipPercent(value); }

  private currentWorkspace(): ScopeWorkspace | null {
    const workspace = this.ws.data();
    return workspace && workspace.scope.id.toLowerCase() === this.id()?.toLowerCase() ? workspace : null;
  }

  isComponentCorrectionSelected(workspace: ScopeWorkspace, componentId: string): boolean {
    const selected = this.selectedComponentCorrection();
    return selected?.scopeId === workspace.scope.id.toLowerCase() && selected.componentId === componentId.toLowerCase() &&
      workspace.canPrepare && workspace.components.some(component => component.id.toLowerCase() === componentId.toLowerCase());
  }
  toggleComponentCorrection(workspace: ScopeWorkspace, componentId: string): void {
    const component = workspace.components.find(item => item.id.toLowerCase() === componentId.toLowerCase());
    if (!workspace.canPrepare || workspace.scope.id.toLowerCase() !== this.id()?.toLowerCase() || !component) return;
    if (this.isComponentCorrectionSelected(workspace, component.id)) {
      this.selectedComponentCorrection.set(null);
      return;
    }
    this.selectedComponentCorrection.set({ scopeId: workspace.scope.id.toLowerCase(), componentId: component.id.toLowerCase() });
  }
  selectedComponentCorrectionFor(workspace: ScopeWorkspace): ConsolidationComponent | null {
    const selected = this.selectedComponentCorrection();
    if (!workspace.canPrepare || !selected || selected.scopeId !== workspace.scope.id.toLowerCase()) return null;
    return workspace.components.find(component => component.id.toLowerCase() === selected.componentId) ?? null;
  }
  isExternalPacksOpen(workspace: ScopeWorkspace): boolean {
    return workspace.scope.id.toLowerCase() === this.id()?.toLowerCase() && this.externalPacksScopeId() === workspace.scope.id.toLowerCase();
  }
  toggleExternalPacks(workspace: ScopeWorkspace): void {
    if (workspace.scope.id.toLowerCase() !== this.id()?.toLowerCase()) return;
    this.externalPacksScopeId.set(this.isExternalPacksOpen(workspace) ? null : workspace.scope.id.toLowerCase());
  }

  approveScope(scopeId: string): void {
    if (scopeId.toLowerCase() !== this.id()?.toLowerCase()) return;
    void this.runScopedCommand(this.ownerKey(), `/api/ui/consolidation/scopes/${scopeId}/approve`, {}, 'Perimeter approved.', undefined, () => this.ws.reload());
  }
  submitComponent(workspace: ScopeWorkspace): void {
    if (workspace.scope.id.toLowerCase() !== this.id()?.toLowerCase()) return;
    const pkg = workspace.eligiblePackages.items.find(item => item.packageId === this.selectedPackageId);
    const ownershipPercent = this.ownershipPercent.trim();
    if (!pkg || !this.isValidOwnershipPercent(ownershipPercent)) return;
    void this.runScopedCommand(this.ownerKey(), `/api/ui/consolidation/scopes/${workspace.scope.id}/components`, {
      clientId: pkg.clientId, engagementId: pkg.engagementId, packageId: pkg.packageId, ownershipPercent,
      controlMethod: this.controlMethod, periodBasis: pkg.periodBasis, taxonomyVersion: pkg.taxonomyVersion, mappingVersion: pkg.mappingVersion,
    }, 'Component submitted for independent review.', () => {
      this.selectedPackageId = '';
      this.ownershipPercent = '100';
    }, () => this.ws.reload());
  }
  approveComponent(componentId: string): void {
    const workspace = this.currentWorkspace();
    if (!workspace?.components.some(component => component.id.toLowerCase() === componentId.toLowerCase())) return;
    void this.runScopedCommand(this.ownerKey(), `/api/ui/consolidation/components/${componentId}/approve`, {}, 'Component approved.', undefined, () => this.ws.reload());
  }
  updateJournalLine(index: number, field: keyof NewJournalLine, value: string): void { this.journal.updateLine(index, field, value); }
  addJournalLine(): void { this.journal.addLine(); }
  removeJournalLine(index: number): void { this.journal.removeLine(index); }
  createJournal(scopeId: string): void {
    if (scopeId.toLowerCase() !== this.id()?.toLowerCase() || !this.currentWorkspace()) return;
    this.journal.create(scopeId);
  }
  private hasCurrentJournal(journalId: string): boolean {
    return this.currentWorkspace()?.journals.items.some(journal => journal.id.toLowerCase() === journalId.toLowerCase()) ?? false;
  }
  approveJournal(journalId: string): void { if (this.hasCurrentJournal(journalId)) this.journal.approve(journalId); }
  promptReturnJournal(journalId: string): void { if (this.hasCurrentJournal(journalId)) this.journal.promptReturn(journalId); }
  resubmitJournal(journalId: string): void { if (this.hasCurrentJournal(journalId)) this.journal.resubmit(journalId); }
  runConsolidation(scopeId: string): void {
    if (scopeId.toLowerCase() !== this.id()?.toLowerCase()) return;
    void this.runScopedCommand(this.ownerKey(), `/api/ui/consolidation/scopes/${scopeId}/run`, {}, 'Consolidation run calculated.',
      () => this.runsPageNumber.set(1), () => this.ws.reload());
  }
  approveRun(runId: string): void {
    if (!this.currentWorkspace()?.runs.items.some(run => run.id.toLowerCase() === runId.toLowerCase())) return;
    void this.runScopedCommand(this.ownerKey(), `/api/ui/consolidation/runs/${runId}/approve`, {}, 'Consolidation run approved.', undefined, () => this.ws.reload());
  }
}
