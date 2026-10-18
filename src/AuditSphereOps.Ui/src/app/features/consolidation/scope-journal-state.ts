import { computed, signal } from '@angular/core';
import type { ScopeWorkspace } from './scope-contracts';
import { formatScaledAmount, MAX_NUMERIC_19_6, parseConsolidationAmount } from './scope-amounts';

export interface NewJournalLine {
  taxonomyCode: string;
  debit: string;
  credit: string;
  description: string;
}

type RunScopedCommand = <T>(
  owner: string,
  url: string,
  body: unknown,
  success: string,
  after?: (value: T) => void,
  settled?: () => void,
) => Promise<boolean>;

export class ScopeJournalState {
  journalNumber = '';
  journalType = 'INTERCOMPANY_ELIMINATION';
  journalCurrency = '';
  journalEvidence = '';
  readonly journalLines = signal<NewJournalLine[]>([
    { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
    { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
  ]);

  private readonly journalTotals = computed(() => {
    let debit = 0n;
    let credit = 0n;
    for (const line of this.journalLines()) {
      const lineDebit = parseConsolidationAmount(line.debit);
      const lineCredit = parseConsolidationAmount(line.credit);
      if (lineDebit === null || lineCredit === null) return null;
      debit += lineDebit;
      credit += lineCredit;
      if (debit > MAX_NUMERIC_19_6 || credit > MAX_NUMERIC_19_6) return null;
    }
    return { debit, credit };
  });

  readonly journalAmountsValid = computed(() => this.journalTotals() !== null);
  readonly totalDebits = computed(() => {
    const totals = this.journalTotals();
    return totals ? formatScaledAmount(totals.debit) : '—';
  });
  readonly totalCredits = computed(() => {
    const totals = this.journalTotals();
    return totals ? formatScaledAmount(totals.credit) : '—';
  });
  readonly isJournalBalanced = computed(() => {
    const totals = this.journalTotals();
    return totals !== null && totals.debit > 0n && totals.debit === totals.credit;
  });
  readonly journalDifference = computed(() => {
    const totals = this.journalTotals();
    return totals ? formatScaledAmount(totals.debit >= totals.credit ? totals.debit - totals.credit : totals.credit - totals.debit) : '—';
  });

  constructor(
    private readonly owner: () => string,
    private readonly runCommand: RunScopedCommand,
    private readonly workspace: () => ScopeWorkspace | null,
    private readonly setJournalsPage: (page: number) => void,
    private readonly reloadWorkspace: () => void,
  ) {}

  resetForOwnerChange(): void {
    this.journalNumber = '';
    this.journalType = 'INTERCOMPANY_ELIMINATION';
    this.journalCurrency = '';
    this.journalEvidence = '';
    this.journalLines.set([
      { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
      { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
    ]);
  }

  updateLine(index: number, field: keyof NewJournalLine, value: string): void {
    this.journalLines.update(lines => lines.map((line, i) => i === index ? { ...line, [field]: value } : line));
  }
  addLine(): void {
    this.journalLines.update(lines => [...lines, { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' }]);
  }
  removeLine(index: number): void { this.journalLines.update(lines => lines.filter((_, i) => i !== index)); }

  create(scopeId: string): void {
    const totals = this.journalTotals();
    if (!totals || totals.debit <= 0n || totals.debit !== totals.credit) return;
    const lines = this.journalLines().map(line => ({
      intercompanyMatchId: null,
      taxonomyCode: line.taxonomyCode.trim(),
      debit: line.debit.trim(),
      credit: line.credit.trim(),
      description: line.description.trim(),
    }));
    const workspace = this.workspace();
    const currency = this.journalCurrency.trim() || workspace?.scope.reportingCurrency || 'USD';
    const owner = this.owner();
    void this.runCommand(owner, `/api/ui/consolidation/scopes/${scopeId}/journals`, {
      journalNumber: this.journalNumber.trim(),
      journalType: this.journalType,
      currency,
      evidenceReference: this.journalEvidence.trim(),
      lines,
    }, 'Elimination journal submitted for review.', () => {
      this.setJournalsPage(1);
      this.journalNumber = '';
      this.journalEvidence = '';
      this.journalLines.set([
        { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
        { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
      ]);
    }, this.reloadWorkspace);
  }

  approve(journalId: string): void {
    void this.runCommand(this.owner(), `/api/ui/consolidation/journals/${journalId}/approve`, {}, 'Journal approved.', undefined, this.reloadWorkspace);
  }
  promptReturn(journalId: string): void {
    const reason = window.prompt('Enter return reason for this journal:');
    if (!reason || !reason.trim()) return;
    void this.runCommand(this.owner(), `/api/ui/consolidation/journals/${journalId}/return`, { reason: reason.trim() }, 'Journal returned.', undefined, this.reloadWorkspace);
  }
  resubmit(journalId: string): void {
    void this.runCommand(this.owner(), `/api/ui/consolidation/journals/${journalId}/resubmit`, {}, 'Journal resubmitted.', undefined, this.reloadWorkspace);
  }
}
