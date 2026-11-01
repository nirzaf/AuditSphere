import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, ElementRef, inject, signal, untracked, viewChildren } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { ConsolidationOwnership } from './ownership';
import { ConsolidationComponentCorrection } from './component-correction';
import { ConsolidationExternalPacks } from './external-packs';
import { decodeGroupDeliverablePage, decodeScopeWorkspace, type ScopeWorkspace, type ConsolidationComponent, type IntercompanyException, type IntercompanyExceptionsPage, type IntercompanyMatchOptions } from './scope-contracts';
import { formatOwnershipPercent, isValidOwnershipPercent } from './scope-amounts';
import { ScopeIntercompanyState, type IntercompanyForm } from './scope-intercompany-state';
import { ScopeJournalState, type NewJournalLine } from './scope-journal-state';
import { decodeDeliverableRecovery, deliverableInputHash, matchesDeliverableReceipt, type DeliverableRecovery } from './deliverable-recovery';

export { formatOwnershipPercent } from './scope-amounts';

@Component({
  selector: 'audit-consolidation-scope-workspace',
  imports: [FormsModule, RouterLink, MatButtonModule, DatePipe, ConsolidationOwnership, ConsolidationComponentCorrection, ConsolidationExternalPacks, ...SHARED],
  templateUrl: './scope-workspace.html',
  styles: [`
    .inline-form { min-width: 0; max-width: 100%; }
    .journal-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .journal-fields label, .journal-fields input, .journal-fields select { min-width: 0; }
    .journal-fields input, .journal-fields select { width: 100%; box-sizing: border-box; }
    .journal-balance { overflow-wrap: anywhere; }
    @media (max-width: 600px) { .journal-fields { grid-template-columns: minmax(0, 1fr); } }
  `],
})
export class ConsolidationScopeWorkspace {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly journalTaxonomyInputs = viewChildren<ElementRef<HTMLInputElement>>('journalTaxonomyInput');
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
  readonly deliverablePageNumber = signal(1);
  readonly selectedComponentCorrection = signal<{ scopeId: string; componentId: string } | null>(null);
  readonly externalPacksScopeId = signal<string | null>(null);
  readonly uncertainScopeId = signal<string | null>(null);
  readonly pendingDeliverable = signal<DeliverableRecovery | null>(null);
  recoveryInput = '';
  private readonly deliverableBuildRequestKeys = new Map<string, string>();
  private readonly deliverableIssueRequestKeys = new Map<string, string>();
  private readonly deliverableArchiveRequestKeys = new Map<string, string>();
  private workspaceReadOwner = '';
  deliverableReleaseKey = '';
  deliverableArchiveReason = '';
  private deliverableReview: { key: string; deliverableId: string; action: 'issue' | 'archive' } | null = null;
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
      this.workspaceReadOwner = this.ownerKey();
      return workspace;
    },
    'The requested consolidation scope is not available in the current firm and group grant.',
  );
  readonly deliverablePage = this.api.resource(
    () => this.id() ? `/api/ui/consolidation/scopes/${this.id()}/deliverables?page=${this.deliverablePageNumber()}&pageSize=100` : null,
    (value, path) => {
      const page = decodeGroupDeliverablePage(value, path);
      if (page.page !== this.deliverablePageNumber() || page.pageSize !== 100)
        throw new Error('Retained group deliverables belong to a different page.');
      return page;
    },
    'The retained group deliverables are not available in the current group grant.',
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
      this.deliverablePageNumber.set(1);
      this.selectedComponentCorrection.set(null);
      this.externalPacksScopeId.set(null);
      this.selectedPackageId = '';
      this.deliverableReleaseKey = '';
      this.deliverableArchiveReason = '';
      this.deliverableReview = null;
      this.pendingDeliverable.set(null);
      this.recoveryInput = '';
      this.journal.resetForOwnerChange();
    });
  });

  private readonly invalidateChangedDeliverableReview = effect(() => {
    const workspace = this.ws.data();
    const page = this.deliverablePage.data();
    this.ownerKey();
    untracked(() => {
      const review = this.deliverableReview;
      if (!review) return;
      const item = page?.items.find(d => d.deliverableId.toLowerCase() === review.deliverableId);
      if (!workspace || !item || review.key !== this.deliverableReviewKey(workspace, item, review.action))
        this.deliverableReview = null;
    });
  });

  private deliverableStorageKey(scopeId: string): string | null {
    const session = this.session.current();
    return session?.staff ? 'auditsphere-group-pending-v1:' + session.firmId.toLowerCase() + ':' + session.userId.toLowerCase() + ':' + scopeId.toLowerCase() : null;
  }

  private readonly restoreDeliverableRecovery = effect(() => {
    const owner = this.ownerKey();
    const workspace = this.ws.data();
    untracked(() => {
      if (!workspace || owner !== this.ownerKey() || this.workspaceReadOwner !== owner ||
          workspace.scope.id.toLowerCase() !== this.id()?.toLowerCase()) return;
      const key = this.deliverableStorageKey(workspace.scope.id);
      try {
        if (!key) throw new Error('Session unavailable');
        const raw = sessionStorage.getItem(key);
        if (raw === null) return;
        const pending = decodeDeliverableRecovery(JSON.parse(raw));
        if (!pending || pending.scopeId.toLowerCase() !== workspace.scope.id.toLowerCase() ||
            pending.groupId.toLowerCase() !== workspace.scope.groupId.toLowerCase() ||
            pending.actorId.toLowerCase() !== this.session.current()?.userId.toLowerCase()) throw new Error('Invalid recovery reference');
        this.pendingDeliverable.set(pending);
        this.cmd.uncertain.set(true); this.uncertainScopeId.set(pending.scopeId.toLowerCase());
        this.cmd.message.set('A group deliverable outcome is unconfirmed. Retry the exact original request.');
      } catch {
        this.cmd.uncertain.set(true); this.uncertainScopeId.set(workspace.scope.id.toLowerCase());
        this.cmd.message.set('The recovery reference is unavailable. Server actions remain blocked.');
      }
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
    const commandScopeId = this.id()?.toLowerCase() ?? '';
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
          this.uncertainScopeId.set(commandScopeId || null);
          this.cmd.message.set('A previous-scope action has an unconfirmed outcome. Return to that scope to inspect its persisted state; server actions remain blocked until the original command outcome is confirmed.');
        }
        return false;
      }
      this.cmd.failed.set(!outcome.ok);
      this.cmd.code.set(outcome.ok ? '' : outcome.code);
      if (!outcome.ok && outcome.unknown) {
        this.cmd.uncertain.set(true);
        this.uncertainScopeId.set(commandScopeId || null);
      }
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
  previousDeliverablePage(): void { this.deliverablePageNumber.update(page => Math.max(1, page - 1)); }
  nextDeliverablePage(page: ReturnType<typeof decodeGroupDeliverablePage>): void {
    if (page.page * page.pageSize < page.totalCount) this.deliverablePageNumber.set(page.page + 1);
  }
  previousRunsPage(): void { this.runsPageNumber.update(page => Math.max(1, page - 1)); }
  nextRunsPage(page: ScopeWorkspace['runs']): void {
    if (page.page * page.pageSize < page.totalCount) this.runsPageNumber.set(page.page + 1);
  }

  refreshUnknownOutcome(): void {
    const scopeId = this.id()?.toLowerCase();
    if (!scopeId || !this.cmd.uncertain() || this.uncertainScopeId() !== scopeId) return;
    this.cmd.message.set('Read-only scope refresh requested. It shows persisted state but does not confirm the original command outcome; server actions remain blocked until that outcome is confirmed.');
    this.ws.reload();
  }

  private async prepareDeliverableRecovery(
    workspace: ScopeWorkspace, ref: DeliverableRecovery, input: string, guard: () => boolean, after: () => void,
  ): Promise<void> {
    if (this.cmd.busy() || this.cmd.uncertain()) return;
    const owner = this.ownerKey(), key = this.deliverableStorageKey(ref.scopeId);
    if (!key) return;
    this.cmd.busy.set(true); this.cmd.message.set('');
    try {
      const existing = sessionStorage.getItem(key);
      if (existing !== null) {
        this.cmd.uncertain.set(true); this.uncertainScopeId.set(ref.scopeId.toLowerCase());
        const pending = decodeDeliverableRecovery(JSON.parse(existing));
        if (pending && pending.scopeId.toLowerCase() === ref.scopeId.toLowerCase() &&
            pending.groupId.toLowerCase() === ref.groupId.toLowerCase() && pending.actorId.toLowerCase() === ref.actorId.toLowerCase())
          this.pendingDeliverable.set(pending);
        this.cmd.message.set('A previous deliverable request must be recovered before another action.');
        return;
      }
      if (ref.action !== 'build') {
        ref.salt = Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, '0')).join('');
        ref.inputHash = await deliverableInputHash(ref.salt, input);
      }
      if (this.destroyed || owner !== this.ownerKey() || this.currentWorkspace() !== workspace || !guard()) return;
      const intervening = sessionStorage.getItem(key);
      if (intervening !== null) {
        this.cmd.uncertain.set(true); this.uncertainScopeId.set(ref.scopeId.toLowerCase());
        const pending = decodeDeliverableRecovery(JSON.parse(intervening));
        if (pending && pending.scopeId.toLowerCase() === ref.scopeId.toLowerCase() &&
            pending.groupId.toLowerCase() === ref.groupId.toLowerCase() && pending.actorId.toLowerCase() === ref.actorId.toLowerCase())
          this.pendingDeliverable.set(pending);
        this.cmd.message.set('A previous deliverable request must be recovered before another action.');
        return;
      }
      const raw = JSON.stringify(ref);
      sessionStorage.setItem(key, raw);
      if (sessionStorage.getItem(key) !== raw) throw new Error('Recovery reference unavailable');
      this.pendingDeliverable.set(ref);
      await this.sendDeliverableRecovery(owner, key, ref, input, false, after);
    } catch {
      if (!this.destroyed && owner === this.ownerKey()) {
        this.cmd.uncertain.set(true); this.uncertainScopeId.set(ref.scopeId.toLowerCase());
        this.cmd.message.set('The recovery reference is unavailable. Server actions remain blocked.');
      }
    } finally {
      this.cmd.busy.set(false);
    }
  }

  private async sendDeliverableRecovery(owner: string, key: string, ref: DeliverableRecovery, input: string,
    recovering: boolean, after?: () => void): Promise<void> {
    const base = '/api/ui/consolidation/scopes/' + ref.scopeId + '/deliverables';
    const url = ref.action === 'build' ? base + '/build' : base + '/' + ref.deliverableId + '/' + ref.action;
    const body = ref.action === 'build'
      ? { runId: ref.runId, expectedRunHash: ref.runHash, expectedGroupRevision: ref.groupRevision, requestKey: ref.requestKey }
      : ref.action === 'issue'
        ? { expectedDeliverableRevision: ref.revision, expectedGroupRevision: ref.groupRevision,
          expectedDeliverableSha256: ref.deliverableHash, authorizedReleaseKey: input, requestKey: ref.requestKey }
        : { expectedDeliverableRevision: ref.revision, expectedDeliverableSha256: ref.deliverableHash, reason: input, requestKey: ref.requestKey };
    let outcome;
    try { outcome = await this.api.command<unknown>(url, body, 'POST'); }
    catch { outcome = { ok: false as const, unknown: true as const, code: 'outcome.unknown', message: 'Outcome unconfirmed.', status: 0 }; }
    if (this.destroyed || owner !== this.ownerKey()) return;
    if (outcome.ok && matchesDeliverableReceipt(outcome.value, ref, input)) {
      try { sessionStorage.removeItem(key); } catch { this.cmd.uncertain.set(true); this.uncertainScopeId.set(ref.scopeId.toLowerCase()); return; }
      this.pendingDeliverable.set(null); this.cmd.uncertain.set(false); this.uncertainScopeId.set(null);
      this.recoveryInput = ''; this.cmd.failed.set(false); this.cmd.message.set('The original group deliverable action was confirmed.');
      after?.(); this.deliverablePage.reload(); this.ws.reload();
    } else if (!outcome.ok && !outcome.unknown && !recovering) {
      try { sessionStorage.removeItem(key); this.pendingDeliverable.set(null); }
      catch { this.cmd.uncertain.set(true); this.uncertainScopeId.set(ref.scopeId.toLowerCase()); }
      this.cmd.failed.set(true); this.cmd.message.set(outcome.message);
    } else {
      this.cmd.failed.set(true); this.cmd.uncertain.set(true); this.uncertainScopeId.set(ref.scopeId.toLowerCase());
      this.cmd.message.set('The original deliverable outcome is unconfirmed. Retry the same request before another action.');
    }
  }

  async retryDeliverableRecovery(): Promise<void> {
    const ref = this.pendingDeliverable(), workspace = this.currentWorkspace();
    const owner = this.ownerKey(), key = ref && this.deliverableStorageKey(ref.scopeId);
    if (!ref || !key || !workspace || this.workspaceReadOwner !== owner || this.cmd.busy() || !this.cmd.uncertain() ||
        workspace.scope.id.toLowerCase() !== ref.scopeId.toLowerCase() || workspace.scope.groupId.toLowerCase() !== ref.groupId.toLowerCase() ||
        this.session.current()?.userId.toLowerCase() !== ref.actorId.toLowerCase()) return;
    const input = this.recoveryInput.trim();
    this.cmd.busy.set(true);
    try {
      if (ref.action !== 'build' && (!ref.salt || await deliverableInputHash(ref.salt, input) !== ref.inputHash)) {
        this.cmd.message.set('Re-enter the exact original release reference or archive reason.'); return;
      }
      if (this.destroyed || owner !== this.ownerKey() || this.currentWorkspace() !== workspace) return;
      await this.sendDeliverableRecovery(owner, key, ref, input, true);
    } catch {
      if (!this.destroyed && owner === this.ownerKey()) this.cmd.message.set('The original outcome remains unconfirmed.');
    } finally { this.cmd.busy.set(false); }
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
    const workspace = this.currentWorkspace();
    if (!workspace || workspace.scope.id.toLowerCase() !== scopeId.toLowerCase() ||
        scopeId.toLowerCase() !== this.id()?.toLowerCase() || workspace.scope.status !== 'DRAFT' ||
        !workspace.canReview || !workspace.canApproveScope) return;
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
    const component = workspace?.components.find(item => item.id.toLowerCase() === componentId.toLowerCase());
    if (!workspace || !workspace.canReview || !component || component.status !== 'SUBMITTED' ||
        component.submittedByUserId === workspace.currentUserId || !component.canApprove) return;
    void this.runScopedCommand(this.ownerKey(), `/api/ui/consolidation/components/${componentId}/approve`, {}, 'Component approved.', undefined, () => this.ws.reload());
  }
  updateJournalLine(index: number, field: keyof NewJournalLine, value: string): void { this.journal.updateLine(index, field, value); }
  addJournalLine(): void { this.journal.addLine(); }
  removeJournalLine(index: number): void {
    const count = this.journalLines().length;
    if (count <= 2 || index < 0 || index >= count) return;
    // Index-tracked rows keep this input when the selected row is removed.
    this.journalTaxonomyInputs()[Math.min(index, count - 2)]?.nativeElement.focus();
    this.journal.removeLine(index);
  }
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

  buildDeliverable(workspace: ScopeWorkspace): void {
    const report = workspace.latestReport.value;
    const current = this.currentWorkspace();
    if (workspace.scope.id.toLowerCase() !== this.id()?.toLowerCase() || !report ||
        report.state !== 'CURRENT_APPROVED' || !report.runId || !report.runHash ||
        current?.scope.id.toLowerCase() !== workspace.scope.id.toLowerCase()) return;
    const scopeId = workspace.scope.id;
    const intentKey = [this.ownerKey(), scopeId.toLowerCase(), workspace.scope.groupRevision, report.runId.toLowerCase(), report.runHash].join(':');
    const requestKey = this.deliverableBuildRequestKeys.get(intentKey) ?? crypto.randomUUID();
    this.deliverableBuildRequestKeys.set(intentKey, requestKey);
    const actorId = this.session.current()?.userId;
    if (!actorId) return;
    const ref: DeliverableRecovery = { action: 'build', scopeId, groupId: workspace.scope.groupId, actorId, requestKey,
      runId: report.runId, runHash: report.runHash, groupRevision: workspace.scope.groupRevision,
      deliverableId: null, deliverableHash: null, revision: 0, salt: null, inputHash: null };
    void this.prepareDeliverableRecovery(workspace, ref, '', () => this.currentWorkspace() === workspace &&
      workspace.latestReport.value?.state === 'CURRENT_APPROVED', () => this.deliverableBuildRequestKeys.delete(intentKey));
  }

  deliverableDownload(scopeId: string, deliverableId: string): string {
    return `/api/ui/consolidation/scopes/${scopeId}/deliverables/${deliverableId}/download`;
  }

  updateDeliverableInput(field: 'release' | 'archive', value: string): void {
    if (field === 'release') this.deliverableReleaseKey = value;
    else this.deliverableArchiveReason = value;
    this.deliverableReview = null;
  }

  private deliverableReviewKey(workspace: ScopeWorkspace, deliverable: ReturnType<typeof decodeGroupDeliverablePage>['items'][number], action: 'issue' | 'archive'): string {
    return JSON.stringify([this.ownerKey(), workspace.scope.id.toLowerCase(), workspace.scope.groupRevision,
      workspace.latestReport.available, workspace.latestReport.value?.state,
      workspace.latestReport.value?.runId, workspace.latestReport.value?.runHash,
      deliverable.deliverableId.toLowerCase(), deliverable.status, deliverable.revision,
      deliverable.runHash, deliverable.deliverableSha256, action,
      action === 'issue' ? this.deliverableReleaseKey.trim() : this.deliverableArchiveReason.trim()]);
  }

  isDeliverableReviewed(workspace: ScopeWorkspace, deliverable: ReturnType<typeof decodeGroupDeliverablePage>['items'][number], action: 'issue' | 'archive'): boolean {
    return this.deliverableReview?.key === this.deliverableReviewKey(workspace, deliverable, action) &&
      this.deliverablePage.data()?.items.some(item => item.deliverableId.toLowerCase() === deliverable.deliverableId.toLowerCase() &&
        this.deliverableReviewKey(workspace, item, action) === this.deliverableReview?.key) === true;
  }

  setDeliverableReviewed(workspace: ScopeWorkspace, deliverable: ReturnType<typeof decodeGroupDeliverablePage>['items'][number], action: 'issue' | 'archive', reviewed: boolean): void {
    this.deliverableReview = reviewed ? { key: this.deliverableReviewKey(workspace, deliverable, action),
      deliverableId: deliverable.deliverableId.toLowerCase(), action } : null;
  }

  issueDeliverable(workspace: ScopeWorkspace,
    deliverable: ReturnType<typeof decodeGroupDeliverablePage>['items'][number]): void {
    const scopeId = workspace.scope.id;
    if (scopeId.toLowerCase() !== this.id()?.toLowerCase() || !this.currentWorkspace()?.canIssueDeliverable || workspace !== this.currentWorkspace() ||
        deliverable.status !== 'BUILT' || !deliverable.revision ||
        !this.deliverableReleaseKey.trim() || !this.isDeliverableReviewed(workspace, deliverable, 'issue')) return;
    const releaseKey = this.deliverableReleaseKey.trim();
    const review = this.deliverableReview;
    const intentKey = [this.ownerKey(), scopeId.toLowerCase(), deliverable.deliverableId.toLowerCase(),
      deliverable.revision, deliverable.deliverableSha256, releaseKey].join(':');
    const requestKey = this.deliverableIssueRequestKeys.get(intentKey) ?? crypto.randomUUID();
    this.deliverableIssueRequestKeys.set(intentKey, requestKey);
    const actorId = this.session.current()?.userId;
    if (!actorId) return;
    const ref: DeliverableRecovery = { action: 'issue', scopeId, groupId: workspace.scope.groupId, actorId, requestKey,
      runId: null, runHash: deliverable.runHash, groupRevision: workspace.scope.groupRevision,
      deliverableId: deliverable.deliverableId, deliverableHash: deliverable.deliverableSha256,
      revision: deliverable.revision, salt: null, inputHash: null };
    void this.prepareDeliverableRecovery(workspace, ref, releaseKey,
      () => workspace.canIssueDeliverable && this.isDeliverableReviewed(workspace, deliverable, 'issue'), () => {
      this.deliverableIssueRequestKeys.delete(intentKey);
      if (this.deliverableReview === review) {
        this.deliverableReleaseKey = '';
        this.deliverableReview = null;
      }
    });
  }

  archiveDeliverable(workspace: ScopeWorkspace,
    deliverable: ReturnType<typeof decodeGroupDeliverablePage>['items'][number]): void {
    const scopeId = workspace.scope.id;
    const reason = this.deliverableArchiveReason.trim();
    if (scopeId.toLowerCase() !== this.id()?.toLowerCase() || !this.currentWorkspace()?.canArchiveDeliverable || workspace !== this.currentWorkspace() ||
        deliverable.status !== 'ISSUED' || !deliverable.revision || !reason ||
        !this.isDeliverableReviewed(workspace, deliverable, 'archive')) return;
    const review = this.deliverableReview;
    const intentKey = [this.ownerKey(), scopeId.toLowerCase(), deliverable.deliverableId.toLowerCase(),
      deliverable.revision, deliverable.deliverableSha256, reason].join(':');
    const requestKey = this.deliverableArchiveRequestKeys.get(intentKey) ?? crypto.randomUUID();
    this.deliverableArchiveRequestKeys.set(intentKey, requestKey);
    const actorId = this.session.current()?.userId;
    if (!actorId) return;
    const ref: DeliverableRecovery = { action: 'archive', scopeId, groupId: workspace.scope.groupId, actorId, requestKey,
      runId: null, runHash: deliverable.runHash, groupRevision: workspace.scope.groupRevision,
      deliverableId: deliverable.deliverableId, deliverableHash: deliverable.deliverableSha256,
      revision: deliverable.revision, salt: null, inputHash: null };
    void this.prepareDeliverableRecovery(workspace, ref, reason,
      () => workspace.canArchiveDeliverable && this.isDeliverableReviewed(workspace, deliverable, 'archive'), () => {
      this.deliverableArchiveRequestKeys.delete(intentKey);
      if (this.deliverableReview === review) {
        this.deliverableArchiveReason = '';
        this.deliverableReview = null;
      }
    });
  }
}
