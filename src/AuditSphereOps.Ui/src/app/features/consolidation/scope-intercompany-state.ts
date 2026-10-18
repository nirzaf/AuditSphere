import { effect, signal, untracked } from '@angular/core';
import { Api, type Resource } from '../../core/api';
import {
  decodeIntercompanyExceptionsPage,
  decodeIntercompanyMatchOptions,
  type IntercompanyException,
  type IntercompanyExceptionsPage,
  type IntercompanyMatchOptions,
  type ScopeWorkspace,
} from './scope-contracts';
import { parseSignedConsolidationAmount } from './scope-amounts';

const INTERCOMPANY_NATURES = ['RECEIVABLE_PAYABLE', 'REVENUE_EXPENSE', 'DIVIDEND', 'INVESTMENT_EQUITY'] as const;
const INTERCOMPANY_PAGE_SIZE = 50;

export interface IntercompanyForm {
  sellerClientId: string;
  buyerClientId: string;
  outsideClientId: string;
  outsidePartySide: 'SELLER' | 'BUYER';
  accountNature: string;
  periodCode: string;
  transactionReference: string;
  matchMode: string;
  matchGroupReference: string;
  outsidePerimeterReview: boolean;
  sellerAmount: string;
  buyerAmount: string;
  matchedAmount: string;
  sellerTaxonomyCode: string;
  buyerTaxonomyCode: string;
  evidenceReference: string;
  differenceReason: string;
  reviewed: boolean;
}

type RunScopedCommand = <T>(
  owner: string,
  url: string,
  body: unknown,
  success: string,
  after?: (value: T) => void,
) => Promise<boolean>;

export class ScopeIntercompanyState {
  readonly intercompanyNatures = INTERCOMPANY_NATURES;
  readonly intercompanyMembersPageNumber = signal(1);
  readonly intercompanyMemberSearch = signal('');
  readonly intercompanyUnresolvedOnly = signal(false);
  readonly intercompanyPageNumber = signal(1);
  private readonly selectedIntercompanyMembers = signal<{ scopeId: string; seller: string; buyer: string } | null>(null);
  private resolvedMemberRequests = new WeakMap<IntercompanyMatchOptions, { seller: string; buyer: string }>();
  private intercompanyApprovalChecks: Record<string, boolean> = {};
  intercompanyForm: IntercompanyForm = ScopeIntercompanyState.newIntercompanyForm();
  readonly intercompanyMatchOptions: Resource<IntercompanyMatchOptions>;
  readonly intercompanyMatches: Resource<IntercompanyExceptionsPage>;

  private lastOwner: string | null = null;

  constructor(
    api: Api,
    private readonly scopeId: () => string | null,
    private readonly workspace: () => ScopeWorkspace | null,
    private readonly owner: () => string,
    private readonly runCommand: RunScopedCommand,
    private readonly reloadWorkspace: () => void,
  ) {
    this.intercompanyMatchOptions = api.resource(
      () => {
        const scopeId = this.scopeId();
        this.owner();
        if (!scopeId) return null;
        const selected = this.selectedIntercompanyMembers();
        const selection = selected?.scopeId === scopeId.toLowerCase() ? selected : null;
        return `/api/ui/consolidation/scopes/${scopeId}/intercompany-match-options?membersPage=${this.intercompanyMembersPageNumber()}&membersPageSize=25&memberSearch=${encodeURIComponent(this.intercompanyMemberSearch())}` +
          (selection?.seller ? `&selectedSellerClientId=${selection.seller}` : '') +
          (selection?.buyer ? `&selectedBuyerClientId=${selection.buyer}` : '');
      },
      (value, path) => {
        const options = decodeIntercompanyMatchOptions(value, path);
        const scopeId = this.scopeId();
        const selected = this.selectedIntercompanyMembers();
        if (options.scopeVersionId !== scopeId ||
            options.memberPaging.page !== this.intercompanyMembersPageNumber() || options.memberPaging.pageSize !== 25 ||
            options.memberSearch !== this.intercompanyMemberSearch().trim() ||
            options.resolvedSeller !== null && (selected?.scopeId !== scopeId?.toLowerCase() || options.resolvedSeller.clientId !== selected?.seller) ||
            options.resolvedBuyer !== null && (selected?.scopeId !== scopeId?.toLowerCase() || options.resolvedBuyer.clientId !== selected?.buyer))
          throw new Error('Intercompany options belong to a different scope, page or selected member.');
        this.resolvedMemberRequests.set(options, { seller: selected?.seller ?? '', buyer: selected?.buyer ?? '' });
        return options;
      },
      'Intercompany match options are unavailable in this group scope.',
    );
    this.intercompanyMatches = api.resource(
      () => this.scopeId()
        ? `/api/ui/consolidation/scopes/${this.scopeId()}/intercompany-matches?page=${this.intercompanyPageNumber()}&pageSize=${INTERCOMPANY_PAGE_SIZE}&unresolvedOnly=${this.intercompanyUnresolvedOnly()}`
        : null,
      (value, path) => {
        const page = decodeIntercompanyExceptionsPage(value, path);
        if (page.scopeVersionId !== this.scopeId() || page.page !== this.intercompanyPageNumber() || page.pageSize !== INTERCOMPANY_PAGE_SIZE)
          throw new Error('Intercompany matches belong to a different scope or page.');
        return page;
      },
      'Retained intercompany matches are unavailable in this group scope.',
    );

    effect(() => {
      const owner = this.owner();
      untracked(() => {
        if (owner === this.lastOwner) return;
        this.lastOwner = owner;
        this.resetForOwnerChange();
      });
    });
  }

  private static newIntercompanyForm(): IntercompanyForm {
    return {
      sellerClientId: '', buyerClientId: '', outsideClientId: '', outsidePartySide: 'BUYER',
      accountNature: 'RECEIVABLE_PAYABLE', periodCode: '', transactionReference: '',
      matchMode: 'ONE_TO_ONE', matchGroupReference: '', outsidePerimeterReview: false,
      sellerAmount: '', buyerAmount: '', matchedAmount: '', sellerTaxonomyCode: '', buyerTaxonomyCode: '',
      evidenceReference: '', differenceReason: '', reviewed: false,
    };
  }

  private resetForOwnerChange(): void {
    this.intercompanyMembersPageNumber.set(1);
    this.intercompanyMemberSearch.set('');
    this.selectedIntercompanyMembers.set(null);
    this.intercompanyUnresolvedOnly.set(false);
    this.intercompanyPageNumber.set(1);
    this.intercompanyForm = ScopeIntercompanyState.newIntercompanyForm();
    this.intercompanyApprovalChecks = {};
    this.resolvedMemberRequests = new WeakMap();
  }

  countLabel(workspace: ScopeWorkspace): string {
    const page = this.intercompanyMatches.data();
    if (page) return String(page.totalCount);
    const workspacePage = workspace.intercompanyExceptions.value;
    return workspace.intercompanyExceptions.available
      ? String(workspacePage?.totalCount ?? 0)
      : `unavailable (${workspace.intercompanyExceptions.errorCode})`;
  }

  selectMember(side: 'SELLER' | 'BUYER', clientId: string): void {
    if (side === 'SELLER') this.intercompanyForm.sellerClientId = clientId;
    else this.intercompanyForm.buyerClientId = clientId;
    this.refreshMemberSelection();
  }

  changeOutside(field: 'enabled' | 'side' | 'clientId', value: string | boolean): void {
    if (field === 'enabled') this.intercompanyForm.outsidePerimeterReview = value === true;
    else if (field === 'side') {
      if (value !== 'SELLER' && value !== 'BUYER') return;
      this.intercompanyForm.outsidePartySide = value;
    } else this.intercompanyForm.outsideClientId = String(value);
    this.refreshMemberSelection();
  }

  private refreshMemberSelection(): void {
    this.invalidateReview();
    const scopeId = this.scopeId();
    const seller = this.clientId('SELLER');
    const buyer = this.clientId('BUYER');
    this.selectedIntercompanyMembers.set(scopeId ? {
      scopeId: scopeId.toLowerCase(), seller: this.isGuid(seller) ? seller : '', buyer: this.isGuid(buyer) ? buyer : '',
    } : null);
  }

  searchMembers(search: string): void {
    this.intercompanyMemberSearch.set(search.slice(0, 100));
    this.intercompanyMembersPageNumber.set(1);
    this.invalidateReview();
  }

  previousMembersPage(): void {
    this.intercompanyMembersPageNumber.update(page => Math.max(1, page - 1));
    this.invalidateReview();
  }

  nextMembersPage(page: NonNullable<IntercompanyMatchOptions['memberPaging']>): void {
    if (page.page * page.pageSize < page.totalCount) {
      this.intercompanyMembersPageNumber.set(page.page + 1);
      this.invalidateReview();
    }
  }

  memberOptions(options: IntercompanyMatchOptions): IntercompanyMatchOptions['members'] {
    const members = [...options.members];
    for (const [resolved, selectedId] of [
      [options.resolvedSeller, this.intercompanyForm.sellerClientId],
      [options.resolvedBuyer, this.intercompanyForm.buyerClientId],
    ] as const) {
      if (resolved && resolved.clientId === selectedId && !members.some(member => member.clientId === resolved.clientId)) members.push(resolved);
    }
    return members.filter(member => this.intercompanyForm.outsidePerimeterReview || member.hasApprovedComponent);
  }

  isCurrentRevision(workspace: ScopeWorkspace, options: IntercompanyMatchOptions): boolean {
    return workspace.scope.id.toLowerCase() === this.scopeId()?.toLowerCase() &&
      options.scopeVersionId === workspace.scope.id && options.groupId === workspace.scope.groupId &&
      options.pinnedGroupRevision === workspace.scope.groupRevision && options.currentGroupRevision === options.pinnedGroupRevision;
  }

  isCurrentPageRevision(workspace: ScopeWorkspace, page: IntercompanyExceptionsPage): boolean {
    const options = this.intercompanyMatchOptions.data();
    return page.scopeVersionId === workspace.scope.id && page.groupId === workspace.scope.groupId &&
      page.pinnedGroupRevision === workspace.scope.groupRevision && page.currentGroupRevision === page.pinnedGroupRevision &&
      options !== null && this.isCurrentRevision(workspace, options) && options.currentGroupRevision === page.currentGroupRevision;
  }

  canSubmit(workspace: ScopeWorkspace, options: IntercompanyMatchOptions): boolean {
    const form = this.intercompanyForm;
    if (!workspace.canPrepare || !this.isCurrentRevision(workspace, options) || options.periodStartDate === null || options.periodEndDate === null ||
        options.periodStartDate > options.periodEndDate || options.membershipBlocker !== null ||
        !INTERCOMPANY_NATURES.includes(form.accountNature as typeof INTERCOMPANY_NATURES[number]) ||
        !form.periodCode.trim() || form.periodCode.trim().length > 100 || !form.transactionReference.trim() || form.transactionReference.trim().length > 200 ||
        !form.evidenceReference.trim() || form.evidenceReference.trim().length > 2000 ||
        !form.sellerTaxonomyCode.trim() || form.sellerTaxonomyCode.trim().length > 100 ||
        !form.buyerTaxonomyCode.trim() || form.buyerTaxonomyCode.trim().length > 100 || form.differenceReason.trim().length > 2000 ||
        !form.reviewed || !['ONE_TO_ONE', 'GROUPED'].includes(form.matchMode) ||
        form.matchMode === 'GROUPED' && !form.matchGroupReference.trim()) return false;

    const sellerId = this.clientId('SELLER');
    const buyerId = this.clientId('BUYER');
    if (!this.isGuid(sellerId) || !this.isGuid(buyerId) || sellerId === buyerId) return false;
    const members = this.memberOptions(options);
    const sellerMember = members.find(member => member.clientId === sellerId);
    const buyerMember = members.find(member => member.clientId === buyerId);
    if (form.outsidePerimeterReview) {
      const internalMember = form.outsidePartySide === 'SELLER' ? buyerMember : sellerMember;
      const request = this.resolvedMemberRequests.get(options);
      const outsideMember = form.outsidePartySide === 'SELLER' ? options.resolvedSeller : options.resolvedBuyer;
      if (!internalMember || !['SELLER', 'BUYER'].includes(form.outsidePartySide) ||
          this.intercompanyMatchOptions.loading() || this.intercompanyMatchOptions.error() ||
          !request || request.seller !== sellerId || request.buyer !== buyerId ||
          options.memberPaging.page !== this.intercompanyMembersPageNumber() ||
          options.memberSearch !== this.intercompanyMemberSearch().trim() || outsideMember !== null ||
          options.members.some(member => member.clientId === form.outsideClientId.trim().toLowerCase())) return false;
    } else if (!sellerMember?.hasApprovedComponent || !buyerMember?.hasApprovedComponent) return false;

    const seller = parseSignedConsolidationAmount(form.sellerAmount);
    const buyer = parseSignedConsolidationAmount(form.buyerAmount);
    const matched = parseSignedConsolidationAmount(form.matchedAmount);
    if (seller === null || buyer === null || matched === null || matched < 0n || matched > (seller < 0n ? -seller : seller) ||
        matched > (buyer < 0n ? -buyer : buyer)) return false;
    const sellerResidual = (seller < 0n ? -seller : seller) - matched;
    const buyerResidual = (buyer < 0n ? -buyer : buyer) - matched;
    return (seller + buyer === 0n && sellerResidual === 0n && buyerResidual === 0n) || Boolean(form.differenceReason.trim());
  }

  differenceReasonRequired(): boolean {
    const seller = parseSignedConsolidationAmount(this.intercompanyForm.sellerAmount);
    const buyer = parseSignedConsolidationAmount(this.intercompanyForm.buyerAmount);
    const matched = parseSignedConsolidationAmount(this.intercompanyForm.matchedAmount);
    if (seller === null || buyer === null || matched === null || matched < 0n) return false;
    return seller + buyer !== 0n || (seller < 0n ? -seller : seller) > matched || (buyer < 0n ? -buyer : buyer) > matched;
  }

  submit(workspace: ScopeWorkspace, options: IntercompanyMatchOptions): void {
    if (!this.canSubmit(workspace, options)) return;
    const form = this.intercompanyForm;
    const body = {
      sellerClientId: this.clientId('SELLER'), buyerClientId: this.clientId('BUYER'), accountNature: form.accountNature,
      periodCode: form.periodCode.trim(), currency: options.reportingCurrency, transactionReference: form.transactionReference.trim(),
      sellerAmount: form.sellerAmount.trim(), buyerAmount: form.buyerAmount.trim(), matchedAmount: form.matchedAmount.trim(),
      evidenceReference: form.evidenceReference.trim(), sellerTaxonomyCode: form.sellerTaxonomyCode.trim(),
      buyerTaxonomyCode: form.buyerTaxonomyCode.trim(), differenceReason: form.differenceReason.trim(), matchMode: form.matchMode,
      matchGroupReference: form.matchGroupReference.trim(), outsidePerimeterReview: form.outsidePerimeterReview,
      expectedGroupRevision: options.currentGroupRevision, reviewed: true,
    };
    void this.runCommand(this.owner(), `/api/ui/consolidation/scopes/${workspace.scope.id}/intercompany-matches`, body,
      'Intercompany match submitted for independent review.', () => {
        this.intercompanyForm = ScopeIntercompanyState.newIntercompanyForm();
        this.selectedIntercompanyMembers.set(null);
        this.intercompanyPageNumber.set(1);
        this.reloadWorkspace();
        this.intercompanyMatchOptions.reload();
        this.intercompanyMatches.reload();
      });
  }

  showHistory(): void { this.intercompanyPageNumber.set(1); this.intercompanyUnresolvedOnly.set(false); }
  showExceptions(): void { this.intercompanyPageNumber.set(1); this.intercompanyUnresolvedOnly.set(true); }
  previousPage(): void { this.intercompanyPageNumber.update(page => Math.max(1, page - 1)); }
  nextPage(page: IntercompanyExceptionsPage): void {
    if (page.page * page.pageSize < page.totalCount) this.intercompanyPageNumber.set(page.page + 1);
  }
  isApprovalReviewed(matchId: string): boolean { return this.intercompanyApprovalChecks[matchId] === true; }
  setApprovalReviewed(matchId: string, reviewed: boolean): void {
    this.intercompanyApprovalChecks = { ...this.intercompanyApprovalChecks, [matchId]: reviewed };
  }
  canApprove(workspace: ScopeWorkspace, item: IntercompanyException, page: IntercompanyExceptionsPage): boolean {
    return workspace.scope.id.toLowerCase() === this.scopeId()?.toLowerCase() && workspace.canReview &&
      item.status === 'SUBMITTED' && item.createdByUserId !== workspace.currentUserId &&
      page.items.some(match => match.matchId.toLowerCase() === item.matchId.toLowerCase()) &&
      this.isCurrentPageRevision(workspace, page);
  }
  approve(workspace: ScopeWorkspace, item: IntercompanyException, page: IntercompanyExceptionsPage): void {
    if (!this.canApprove(workspace, item, page) || !this.isApprovalReviewed(item.matchId)) return;
    void this.runCommand(this.owner(), `/api/ui/consolidation/intercompany-matches/${item.matchId}/approve`,
      { expectedGroupRevision: page.currentGroupRevision, reviewed: true },
      'Intercompany match approved. No component book or elimination journal was created.', () => {
        this.setApprovalReviewed(item.matchId, false);
        this.reloadWorkspace();
        this.intercompanyMatchOptions.reload();
        this.intercompanyMatches.reload();
      });
  }
  invalidateReview(): void { this.intercompanyForm.reviewed = false; }

  private clientId(side: 'SELLER' | 'BUYER'): string {
    const form = this.intercompanyForm;
    if (form.outsidePerimeterReview && form.outsidePartySide === side) return form.outsideClientId.trim().toLowerCase();
    return (side === 'SELLER' ? form.sellerClientId : form.buyerClientId).trim().toLowerCase();
  }
  private isGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  }
}
