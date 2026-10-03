import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { SHARED } from '../../core/ui';
import {
  decodeScopeWorkspace,
  ScopeWorkspace,
  ConsolidationComponent,
  ConsolidationJournal,
} from './scope-contracts';

interface NewJournalLine {
  taxonomyCode: string;
  debit: string;
  credit: string;
  description: string;
}

@Component({
  selector: 'audit-consolidation-scope-workspace',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Consolidation location">
      <a routerLink="/app/consolidation">Consolidation</a> / <span>Scope workspace</span>
    </nav>

    <audit-page-header
      title="Consolidation scope workspace"
      eyebrow="Group accounting"
      description="Perimeter management, component intake & approvals, elimination journals, and calculation runs."
    />

    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="the consolidation scope workspace" />

    @if (ws.data(); as w) {
      <p class="actions">
        <a routerLink="/app/consolidation">Group workspace</a>
        <audit-status [value]="w.scope.status" />
      </p>

      <!-- Scope Perimeter Summary -->
      <section class="panel" aria-labelledby="scope-heading">
        <h2 id="scope-heading">{{ w.scope.groupName }} ({{ w.scope.groupCode }}) · v{{ w.scope.version }}</h2>
        <dl class="facts">
          <dt>Method</dt><dd><code>{{ w.scope.method }}</code></dd>
          <dt>Reporting currency</dt><dd><code>{{ w.scope.reportingCurrency }}</code></dd>
          <dt>Opening basis</dt><dd>{{ w.scope.openingBasis }}</dd>
          <dt>Group perimeter revision</dt><dd>{{ w.scope.groupRevision }}</dd>
          <dt>Perimeter status</dt><dd><audit-status [value]="w.scope.status" /></dd>
        </dl>

        @if (w.scope.status === 'Draft' && w.canReview) {
          <div class="actions" style="margin-top: 1rem;">
            <button matButton="filled" (click)="approveScope(w.scope.id)" [disabled]="cmd.busy()">
              Approve perimeter
            </button>
          </div>
        }
      </section>

      <!-- Quick Navigation Tabs -->
      <nav aria-label="Scope sections" class="actions">
        <a [routerLink]="[]" fragment="components">Components ({{ w.components.length }})</a>
        <a [routerLink]="[]" fragment="journals">Elimination journals ({{ w.journals.length }})</a>
        <a [routerLink]="[]" fragment="runs">Runs & Reports ({{ w.runs.length }})</a>
        <a [routerLink]="[]" fragment="readiness">Readiness ({{ w.componentReadiness.length }})</a>
        <a [routerLink]="[]" fragment="intercompany">Intercompany ({{ w.intercompanyExceptions.length }})</a>
      </nav>

      <!-- Component Intake & Approval -->
      <section class="panel" id="components" aria-labelledby="components-heading">
        <h2 id="components-heading">Components & Intake</h2>
        <p><small>Consolidation operates strictly on approved, validated component financial packages. Preparers cannot approve their own submissions.</small></p>

        @if (w.scope.status === 'Draft' && w.canPrepare && w.eligiblePackages.length > 0) {
          <fieldset class="panel" style="margin: 1rem 0;">
            <legend><strong>Submit component from eligible package</strong></legend>
            <div class="inline-form">
              <label>
                Eligible package *
                <select [(ngModel)]="selectedPackageId" name="selectedPackage">
                  <option value="">-- Select validated package --</option>
                  @for (pkg of w.eligiblePackages; track pkg.packageId) {
                    <option [value]="pkg.packageId">
                      {{ pkg.clientLegalName }} ({{ pkg.currency }}, {{ pkg.periodBasis }})
                    </option>
                  }
                </select>
              </label>

              <label>
                Ownership % *
                <input type="number" [(ngModel)]="ownershipPercent" name="ownershipPercent" min="0" max="100" step="0.01" />
              </label>

              <label>
                Control method *
                <select [(ngModel)]="controlMethod" name="controlMethod">
                  <option value="CONTROLLED">Controlled</option>
                  <option value="SIGNIFICANT_INFLUENCE">Significant influence</option>
                  <option value="JOINT_CONTROL">Joint control</option>
                </select>
              </label>

              <button matButton="filled" (click)="submitComponent(w)" [disabled]="cmd.busy() || !selectedPackageId">
                Submit component
              </button>
            </div>
          </fieldset>
        }

        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Entity</th>
                <th>Currency</th>
                <th>Ownership</th>
                <th>Control method</th>
                <th>Period basis</th>
                <th>Status</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              @for (c of w.components; track c.id) {
                <tr>
                  <td>
                    <strong>{{ c.clientLegalName }}</strong>
                    <small>Digest: {{ c.packageHash.slice(0, 12) }}…</small>
                  </td>
                  <td><code>{{ c.currency }}</code></td>
                  <td>{{ c.ownershipPercent | percent }}</td>
                  <td>{{ c.controlMethod }}</td>
                  <td>{{ c.periodBasis }}</td>
                  <td><audit-status [value]="c.status" /></td>
                  <td>
                    @if (c.status === 'SUBMITTED' && w.canReview && c.submittedByUserId !== w.currentUserId) {
                      <button matButton="filled" (click)="approveComponent(c.id)" [disabled]="cmd.busy()">
                        Approve
                      </button>
                    } @else if (c.status === 'SUBMITTED' && c.submittedByUserId === w.currentUserId) {
                      <small>Maker/checker: independent review required</small>
                    } @else if (c.status === 'APPROVED') {
                      <small>Approved {{ c.approvedAt ? c.approvedAt.slice(0, 10) : '' }}</small>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="7">No components have been submitted for this scope version.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <!-- Elimination Journals -->
      <section class="panel" id="journals" aria-labelledby="journals-heading">
        <h2 id="journals-heading">Elimination journals</h2>
        <p><small>Journals eliminate intercompany balances and investments. Debits and credits must balance before submission.</small></p>

        @if (w.canPrepare) {
          <details style="margin: 1rem 0;">
            <summary><strong role="button">Create elimination journal</strong></summary>
            <div class="panel" style="margin-top: 0.5rem;">
              <div class="inline-form">
                <label>
                  Journal number *
                  <input type="text" [(ngModel)]="journalNumber" placeholder="EJ-001" maxlength="32" />
                </label>
                <label>
                  Type *
                  <select [(ngModel)]="journalType">
                    <option value="INTERCOMPANY_ELIMINATION">Intercompany elimination</option>
                    <option value="INVESTMENT_ELIMINATION">Investment elimination</option>
                    <option value="PROFIT_IN_INVENTORY">Unrealized profit in inventory</option>
                  </select>
                </label>
                <label>
                  Currency *
                  <input type="text" [(ngModel)]="journalCurrency" maxlength="3" style="width: 5rem;" />
                </label>
                <label>
                  Evidence reference *
                  <input type="text" [(ngModel)]="journalEvidence" placeholder="Working paper or agreement ref" maxlength="128" />
                </label>
              </div>

              <h3>Journal lines</h3>
              <div class="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Taxonomy code *</th>
                      <th>Debit</th>
                      <th>Credit</th>
                      <th>Description</th>
                      <th><span class="sr-only">Actions</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (line of journalLines(); track $index) {
                      <tr>
                        <td><input type="text" [(ngModel)]="line.taxonomyCode" placeholder="e.g. BS-100" /></td>
                        <td><input type="number" [(ngModel)]="line.debit" min="0" step="0.01" style="width: 8rem;" /></td>
                        <td><input type="number" [(ngModel)]="line.credit" min="0" step="0.01" style="width: 8rem;" /></td>
                        <td><input type="text" [(ngModel)]="line.description" placeholder="Line memo" /></td>
                        <td>
                          @if (journalLines().length > 2) {
                            <button type="button" (click)="removeJournalLine($index)">Remove</button>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                  <tfoot>
                    <tr>
                      <th>Total</th>
                      <th class="number">{{ totalDebits() }}</th>
                      <th class="number">{{ totalCredits() }}</th>
                      <th colspan="2">
                        @if (isJournalBalanced()) {
                          <span style="color: #127236; font-weight: 600;">✓ Balanced</span>
                        } @else {
                          <span class="error-text">Out of balance (Diff: {{ journalDifference() }})</span>
                        }
                      </th>
                    </tr>
                  </tfoot>
                </table>
              </div>

              <div class="actions" style="margin-top: 1rem;">
                <button type="button" (click)="addJournalLine()">Add line</button>
                <button
                  matButton="filled"
                  (click)="createJournal(w.scope.id)"
                  [disabled]="cmd.busy() || !isJournalBalanced() || !journalNumber.trim() || !journalEvidence.trim()"
                >
                  Submit journal
                </button>
              </div>
            </div>
          </details>
        }

        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Journal</th>
                <th>Type</th>
                <th>Currency</th>
                <th class="number">Debits</th>
                <th class="number">Credits</th>
                <th>Status</th>
                <th>Lines</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              @for (j of w.journals; track j.id) {
                <tr>
                  <td>
                    <strong>{{ j.journalNumber }}</strong>
                    <small>Ref: {{ j.evidenceReference }}</small>
                    @if (j.returnReason) {
                      <small class="error-text">Returned reason: {{ j.returnReason }}</small>
                    }
                  </td>
                  <td><code>{{ j.journalType }}</code></td>
                  <td><code>{{ j.currency }}</code></td>
                  <td class="number">{{ j.totalDebits | money }}</td>
                  <td class="number">{{ j.totalCreditsAbs | money }}</td>
                  <td><audit-status [value]="j.status" /></td>
                  <td>
                    <details>
                      <summary>{{ j.lines.length }} lines</summary>
                      <ul>
                        @for (l of j.lines; track l.id) {
                          <li>
                            <code>{{ l.taxonomyCode }}</code>: Dr {{ l.debit | money }} / Cr {{ l.credit | money }}
                            @if (l.description) { — {{ l.description }} }
                          </li>
                        }
                      </ul>
                    </details>
                  </td>
                  <td>
                    @if (j.status === 'Submitted' && w.canReview && j.createdByUserId !== w.currentUserId) {
                      <div class="actions">
                        <button matButton="filled" (click)="approveJournal(j.id)" [disabled]="cmd.busy()">
                          Approve
                        </button>
                        <button type="button" (click)="promptReturnJournal(j.id)" [disabled]="cmd.busy()">
                          Return
                        </button>
                      </div>
                    } @else if (j.status === 'Submitted' && j.createdByUserId === w.currentUserId) {
                      <small>Maker/checker: independent review required</small>
                    } @else if (j.status === 'Returned' && w.canPrepare) {
                      <button matButton="filled" (click)="resubmitJournal(j.id)" [disabled]="cmd.busy()">
                        Resubmit
                      </button>
                    } @else if (j.status === 'APPROVED') {
                      <small>Approved {{ j.approvedAt ? j.approvedAt.slice(0, 10) : '' }}</small>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="8">No elimination journals recorded for this scope.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <!-- Consolidation Runs & Reports -->
      <section class="panel" id="runs" aria-labelledby="runs-heading">
        <h2 id="runs-heading">Consolidation runs & reports</h2>
        <p><small>Consolidation executes over approved components and approved journals. Runs require maker/checker review before generating authoritative reports.</small></p>

        @if (w.canPrepare && w.scope.status === 'Approved') {
          <div class="actions" style="margin-bottom: 1rem;">
            <button matButton="filled" (click)="runConsolidation(w.scope.id)" [disabled]="cmd.busy()">
              Calculate consolidation run
            </button>
          </div>
        }

        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Run date</th>
                <th>Engine</th>
                <th>Hash</th>
                <th class="number">Signed total</th>
                <th>Status</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              @for (r of w.runs; track r.id) {
                <tr>
                  <td>{{ r.createdAt.slice(0, 16).replace('T', ' ') }} UTC</td>
                  <td><code>{{ r.engineVersion }}</code></td>
                  <td><code>{{ r.runHash.slice(0, 12) }}…</code></td>
                  <td class="number">{{ r.signedTotal | money }}</td>
                  <td><audit-status [value]="r.status" /></td>
                  <td>
                    @if (r.status === 'VERIFIED' && w.canReview && r.createdByUserId !== w.currentUserId) {
                      <button matButton="filled" (click)="approveRun(r.id)" [disabled]="cmd.busy()">
                        Approve run
                      </button>
                    } @else if (r.status === 'VERIFIED' && r.createdByUserId === w.currentUserId) {
                      <small>Maker/checker: independent review required</small>
                    } @else if (r.status === 'APPROVED') {
                      <small>Approved {{ r.approvedAt ? r.approvedAt.slice(0, 10) : '' }}</small>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="6">No consolidation runs executed for this scope.</td></tr>
              }
            </tbody>
          </table>
        </div>

        @if (w.latestReport?.state === 'CURRENT_APPROVED') {
          <div class="panel" style="margin-top: 1.5rem;">
            <h3>Authoritative consolidated report</h3>
            <p><small>Run {{ w.latestReport!.runId }} · Approved {{ w.latestReport!.approvedAt ? w.latestReport!.approvedAt!.slice(0, 16).replace('T', ' ') : '' }}</small></p>

            <div class="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Component / Adjustment</th>
                    <th>Taxonomy code</th>
                    <th class="number">Component</th>
                    <th class="number">Alignment</th>
                    <th class="number">Elimination</th>
                    <th class="number">Consolidated</th>
                    <th>Currency</th>
                  </tr>
                </thead>
                <tbody>
                  @for (l of w.latestReport!.lines; track $index) {
                    <tr>
                      <td>{{ l.component }}</td>
                      <td><code>{{ l.taxonomyCode }}</code></td>
                      <td class="number">{{ l.componentAmount | money }}</td>
                      <td class="number">{{ l.alignmentAmount | money }}</td>
                      <td class="number">{{ l.eliminationAmount | money }}</td>
                      <td class="number"><strong>{{ l.consolidatedAmount | money }}</strong></td>
                      <td><code>{{ l.currency }}</code></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        }
      </section>

      <!-- Component Readiness -->
      <section class="panel" id="readiness" aria-labelledby="readiness-heading">
        <h2 id="readiness-heading">Component readiness</h2>
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Member</th>
                <th>Currency</th>
                <th>Period basis</th>
                <th>Taxonomy</th>
                <th>State</th>
                <th>Compatibility</th>
              </tr>
            </thead>
            <tbody>
              @for (m of w.componentReadiness; track m.clientId) {
                <tr>
                  <td>{{ m.memberName }}</td>
                  <td><code>{{ m.currency }}</code></td>
                  <td>{{ m.periodBasis }}</td>
                  <td>{{ m.taxonomyVersion }}</td>
                  <td><audit-status [value]="m.componentState" /></td>
                  <td>
                    @if (m.currencyCompatible) {
                      <span style="color: #127236;">✓ Compatible</span>
                    } @else {
                      <span class="error-text">{{ m.mismatchReason || 'Incompatible' }}</span>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="6">No member readiness information available.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <!-- Intercompany Exceptions -->
      <section class="panel" id="intercompany" aria-labelledby="intercompany-heading">
        <h2 id="intercompany-heading">Intercompany exceptions</h2>
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Nature</th>
                <th>Mode</th>
                <th>Currency</th>
                <th class="number">Seller</th>
                <th class="number">Buyer</th>
                <th class="number">Matched</th>
                <th class="number">Difference</th>
                <th>Reason</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (item of w.intercompanyExceptions; track item.matchId) {
                <tr>
                  <td><code>{{ item.accountNature }}</code></td>
                  <td>{{ item.matchMode }}</td>
                  <td><code>{{ item.currency }}</code></td>
                  <td class="number">{{ item.sellerAmount | money }}</td>
                  <td class="number">{{ item.buyerAmount | money }}</td>
                  <td class="number">{{ item.matchedAmount | money }}</td>
                  <td class="number">{{ item.difference | money }}</td>
                  <td>{{ item.differenceReason }}</td>
                  <td><audit-status [value]="item.status" /></td>
                </tr>
              } @empty {
                <tr><td colspan="9">No intercompany exceptions found.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }

    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class ConsolidationScopeWorkspace {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(
    () => (this.id() ? `/api/ui/consolidation/scopes/${this.id()}` : null),
    decodeScopeWorkspace,
    'The requested consolidation scope is not available in the current firm and group grant.',
  );
  readonly cmd = new CommandState(this.api);

  // Component Submission Form State
  selectedPackageId = '';
  ownershipPercent = 100;
  controlMethod = 'CONTROLLED';

  // Journal Creation Form State
  journalNumber = '';
  journalType = 'INTERCOMPANY_ELIMINATION';
  journalCurrency = '';
  journalEvidence = '';
  readonly journalLines = signal<NewJournalLine[]>([
    { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
    { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
  ]);

  readonly totalDebits = computed(() => {
    let sum = 0;
    for (const l of this.journalLines()) {
      sum += parseFloat(l.debit) || 0;
    }
    return sum.toFixed(2);
  });

  readonly totalCredits = computed(() => {
    let sum = 0;
    for (const l of this.journalLines()) {
      sum += parseFloat(l.credit) || 0;
    }
    return sum.toFixed(2);
  });

  readonly isJournalBalanced = computed(() => {
    const dr = Math.round((parseFloat(this.totalDebits()) || 0) * 100);
    const cr = Math.round((parseFloat(this.totalCredits()) || 0) * 100);
    return dr > 0 && dr === cr;
  });

  readonly journalDifference = computed(() => {
    const dr = parseFloat(this.totalDebits()) || 0;
    const cr = parseFloat(this.totalCredits()) || 0;
    return Math.abs(dr - cr).toFixed(2);
  });

  addJournalLine(): void {
    this.journalLines.update((lines) => [
      ...lines,
      { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
    ]);
  }

  removeJournalLine(index: number): void {
    this.journalLines.update((lines) => lines.filter((_, i) => i !== index));
  }

  approveScope(scopeId: string): void {
    void this.cmd.run(`/api/ui/consolidation/scopes/${scopeId}/approve`, {}, 'Perimeter approved.')
      .finally(() => this.ws.reload());
  }

  submitComponent(w: ScopeWorkspace): void {
    const pkg = w.eligiblePackages.find((p) => p.packageId === this.selectedPackageId);
    if (!pkg) return;

    void this.cmd.run(
      `/api/ui/consolidation/scopes/${w.scope.id}/components`,
      {
        clientId: pkg.clientId,
        engagementId: pkg.engagementId,
        packageId: pkg.packageId,
        ownershipPercent: this.ownershipPercent,
        controlMethod: this.controlMethod,
        periodBasis: pkg.periodBasis,
        taxonomyVersion: pkg.taxonomyVersion,
        mappingVersion: pkg.mappingVersion,
      },
      'Component submitted for independent review.',
      () => {
        this.selectedPackageId = '';
        this.ownershipPercent = 100;
      },
    ).finally(() => this.ws.reload());
  }

  approveComponent(componentId: string): void {
    void this.cmd.run(`/api/ui/consolidation/components/${componentId}/approve`, {}, 'Component approved.')
      .finally(() => this.ws.reload());
  }

  createJournal(scopeId: string): void {
    const lines = this.journalLines().map((l) => ({
      taxonomyCode: l.taxonomyCode.trim(),
      debit: parseFloat(l.debit) || 0,
      credit: parseFloat(l.credit) || 0,
      description: l.description.trim(),
    }));

    const w = this.ws.data();
    const currency = this.journalCurrency.trim() || w?.scope.reportingCurrency || 'USD';

    void this.cmd.run(
      `/api/ui/consolidation/scopes/${scopeId}/journals`,
      {
        journalNumber: this.journalNumber.trim(),
        journalType: this.journalType,
        currency,
        evidenceReference: this.journalEvidence.trim(),
        lines,
      },
      'Elimination journal submitted for review.',
      () => {
        this.journalNumber = '';
        this.journalEvidence = '';
        this.journalLines.set([
          { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
          { taxonomyCode: '', debit: '0.00', credit: '0.00', description: '' },
        ]);
      },
    ).finally(() => this.ws.reload());
  }

  approveJournal(journalId: string): void {
    void this.cmd.run(`/api/ui/consolidation/journals/${journalId}/approve`, {}, 'Journal approved.')
      .finally(() => this.ws.reload());
  }

  promptReturnJournal(journalId: string): void {
    const reason = window.prompt('Enter return reason for this journal:');
    if (!reason || !reason.trim()) return;

    void this.cmd.run(`/api/ui/consolidation/journals/${journalId}/return`, { reason: reason.trim() }, 'Journal returned.')
      .finally(() => this.ws.reload());
  }

  resubmitJournal(journalId: string): void {
    void this.cmd.run(`/api/ui/consolidation/journals/${journalId}/resubmit`, {}, 'Journal resubmitted.')
      .finally(() => this.ws.reload());
  }

  runConsolidation(scopeId: string): void {
    void this.cmd.run(`/api/ui/consolidation/scopes/${scopeId}/run`, {}, 'Consolidation run calculated.')
      .finally(() => this.ws.reload());
  }

  approveRun(runId: string): void {
    void this.cmd.run(`/api/ui/consolidation/runs/${runId}/approve`, {}, 'Consolidation run approved.')
      .finally(() => this.ws.reload());
  }
}
