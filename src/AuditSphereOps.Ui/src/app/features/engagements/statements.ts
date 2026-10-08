import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { TrialBalanceSource } from './tb-source';
import {
  decodeStatementPage,
  decodeStatementContributions,
  decodeStatementEvidence,
  statementLocation,
  sameStatementBasis,
  StatementLocation,
} from './statement-contracts';

@Component({
  selector: 'audit-statement-drilldown',
  imports: [RouterLink, FormField, MatButtonModule, TrialBalanceSource, ...SHARED],
  templateUrl: './statements.html',
  styleUrl: './statements.scss',
})
export class StatementDrillDown {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly id = routeGuid();
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  readonly location = computed(() => statementLocation((k) => this.query().get(k)));
  readonly model = signal({ filter: '' });
  readonly fields = form(this.model, (p) => maxLength(p.filter, 80));
  /** Read at click time, so the return path is the page the user is on now, not the one the component opened on. */
  currentUrl(): string {
    return this.router.url;
  }
  private readonly viewUrl = computed(() => {
    const q = this.location();
    return this.id() && q
      ? `${this.base()}/workspace?${new URLSearchParams({ section: q.section, filter: q.filter, page: String(q.page) })}`
      : null;
  });
  readonly view = this.api.resource(
    () => this.viewUrl(),
    (value, path) => {
      const v = decodeStatementPage(value, path);
      if (
        v.basis.engagementId !== this.id() ||
        (this.location()?.section !== 'split' &&
          v.section !== 'split' &&
          v.section !== this.location()?.section)
      )
        throw new Error('Wrong statement context');
      return v;
    },
    'Statements are unavailable in your current engagement scope.',
  );
  readonly stale = computed(() => {
    const v = this.view.data(),
      q = this.location();
    return !!v && !!q?.basis && q.basis !== v.basis.revision;
  });
  private readonly detailUrl = computed(() => {
    const v = this.view.data(),
      q = this.location();
    if (!v || !q?.line || this.stale()) return null;
    const isProfit = [
      'INCOME',
      'REVENUE',
      'P&L',
      'PROFIT_LOSS',
      'P_AND_L',
      'EXPENSE',
      'EXPENSES',
    ].includes(q.lineSection.trim().toUpperCase());
    const reqSec = q.section === 'split' ? (isProfit ? 'profit' : 'position') : q.section;
    return `${this.base()}/contributions?${new URLSearchParams({
      section: reqSec,
      destination: q.line,
      statementSection: q.lineSection,
      revision: v.basis.revision,
      page: String(q.accounts),
      procedurePage: String(q.procedures),
    })}`;
  });
  readonly detail = this.api.resource(
    () => this.detailUrl(),
    decodeStatementContributions,
    'This statement line is unavailable in the current scope.',
  );
  readonly visibleView = computed(() => (this.detail.error() ? null : this.view.data()));
  readonly currentDetail = computed(() => {
    const v = this.view.data(),
      d = this.detail.data(),
      q = this.location();
    return v && d && q && !this.stale() && sameStatementBasis(v, d, q.line, q.lineSection)
      ? d
      : null;
  });
  private readonly evidenceUrl = computed(() => {
    const d = this.currentDetail(),
      q = this.location();
    return d && q?.procedure && d.procedures.some((p) => p.procedureId === q.procedure)
      ? `/api/ui/procedures/${q.procedure}/review`
      : null;
  });
  readonly evidence = this.api.resource(
    () => this.evidenceUrl(),
    decodeStatementEvidence,
    'This supporting record is unavailable in your current scope.',
  );
  readonly currentEvidence = computed(() => {
    const e = this.evidence.data(),
      d = this.currentDetail(),
      q = this.location();
    return e &&
      d &&
      q &&
      e.procedureId === q.procedure &&
      d.procedures.some((p) => p.procedureId === e.procedureId)
      ? e
      : null;
  });

  readonly profitLines = computed(() => {
    const v = this.view.data();
    if (!v) return [];
    if (v.profitOrLoss) return v.profitOrLoss.lines;
    return v.section === 'profit' ? v.lines : [];
  });

  readonly positionLines = computed(() => {
    const v = this.view.data();
    if (!v) return [];
    if (v.financialPosition) return v.financialPosition.lines;
    return v.section === 'position' ? v.lines : [];
  });

  readonly profitSummary = computed(() => {
    const v = this.view.data();
    if (!v) return null;
    if (v.profitOrLoss) return v.profitOrLoss;
    if (v.section === 'profit') {
      return {
        section: 'profit' as const,
        title: v.title,
        totalLabel: v.totalLabel,
        currentTotal: v.total,
        priorTotal: null,
        varianceTotal: null,
        percentageVarianceTotal: null,
        lineCount: v.lines.length,
        lines: v.lines,
      };
    }
    return null;
  });

  readonly positionSummary = computed(() => {
    const v = this.view.data();
    if (!v) return null;
    if (v.financialPosition) return v.financialPosition;
    if (v.section === 'position') {
      return {
        section: 'position' as const,
        title: v.title,
        totalLabel: v.totalLabel,
        currentTotal: v.total,
        priorTotal: null,
        varianceTotal: null,
        percentageVarianceTotal: null,
        lineCount: v.lines.length,
        lines: v.lines,
      };
    }
    return null;
  });

  readonly sourceOpen = signal(false);
  readonly downloading = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  private generation = 0;
  constructor() {
    let previousProcedure = '';
    effect(() => {
      const q = this.location();
      untracked(() => {
        this.model.set({ filter: q?.filter ?? '' });
        if (previousProcedure && !q?.procedure) this.view.reload();
        previousProcedure = q?.procedure ?? '';
      });
    });
    effect(() => {
      this.id();
      this.session.invalidation();
      this.query();
      untracked(() => {
        this.generation++;
        this.sourceOpen.set(false);
        this.message.set('');
        this.failed.set(false);
        this.downloading.set(false);
      });
    });
  }
  private base(): string {
    return `/api/ui/engagements/${this.id()}/statements`;
  }
  pages(n: number): number {
    return Math.max(1, Math.ceil(n / 25));
  }
  navigate(values: Partial<Record<keyof StatementLocation, string | number | null>>): void {
    if (this.downloading()) return;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: values,
      queryParamsHandling: 'merge',
    });
  }
  section(section: 'profit' | 'position' | 'split'): void {
    this.navigate({
      section,
      page: 1,
      line: null,
      lineSection: null,
      basis: null,
      accounts: null,
      procedures: null,
      procedure: null,
    });
  }
  apply(): void {
    if (this.fields().valid())
      this.navigate({
        filter: this.model().filter.trim(),
        page: 1,
        line: null,
        lineSection: null,
        basis: null,
        accounts: null,
        procedures: null,
        procedure: null,
      });
  }
  choose(line: string, section: string): void {
    const v = this.view.data();
    if (v)
      this.navigate({
        line,
        lineSection: section,
        basis: v.basis.revision,
        accounts: 1,
        procedures: 1,
        procedure: null,
      });
  }
  refresh(): void {
    if (!this.downloading()) {
      this.sourceOpen.set(false);
      this.message.set('');
      this.view.reload();
    }
  }
  useCurrent(): void {
    const v = this.view.data();
    if (v) this.navigate({ basis: v.basis.revision, procedure: null });
  }
  inspectSource(): void {
    this.sourceOpen.set(!this.sourceOpen());
  }
  async export(): Promise<void> {
    const v = this.view.data();
    if (
      !v ||
      this.detail.error() ||
      this.stale() ||
      this.downloading() ||
      !this.session.current()?.staff
    )
      return;
    const started = this.generation,
      epoch = this.session.invalidation();
    this.downloading.set(true);
    this.message.set('');
    try {
      const r = await this.api.download(
        this.base() + '/export',
        { revision: v.basis.revision },
        (m) =>
          started === this.generation &&
          epoch === this.session.invalidation() &&
          this.view.data() === v &&
          !this.stale() &&
          m.headers['x-statement-basis'] === v.basis.revision &&
          m.headers['x-mapping-id'] === v.basis.mappingId &&
          m.headers['x-engagement-id'] === v.basis.engagementId &&
          m.fileName ===
            `auditsphere-statements-${v.basis.engagementId}-${v.basis.mappingId}.csv` &&
          m.contentType.startsWith('text/csv') &&
          m.byteCount <= 8 * 1024 * 1024,
      );
      if (started !== this.generation || epoch !== this.session.invalidation()) return;
      this.failed.set(!r.ok);
      this.message.set(
        r.ok
          ? 'Complete statement contributions saved with their exact approved basis. This is a review projection, not an issued package.'
          : r.message,
      );
    } finally {
      if (started === this.generation) this.downloading.set(false);
    }
  }
}
