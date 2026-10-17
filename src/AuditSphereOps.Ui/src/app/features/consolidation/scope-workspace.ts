import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { SHARED } from '../../core/ui';
import { ConsolidationOwnership } from './ownership';
import { ConsolidationComponentCorrection } from './component-correction';
import { ConsolidationExternalPacks } from './external-packs';
import {
  decodeIntercompanyExceptionsPage,
  decodeIntercompanyMatchOptions,
  decodeScopeWorkspace,
  IntercompanyException,
  IntercompanyExceptionsPage,
  IntercompanyMatchOptions,
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

const CONSOLIDATION_SCALE = 1_000_000n;
const MAX_NUMERIC_19_6 = BigInt('9'.repeat(19));
const MAX_OWNERSHIP_PERCENT_SCALED = 100n * CONSOLIDATION_SCALE;

function parseConsolidationAmount(value: string): bigint | null {
  const input = value.trim();
  const parts = /^(\d+)(?:\.(\d{1,6}))?$/.exec(input);
  if (!parts || input.length > 64) return null;
  const scaled = BigInt(parts[1]) * CONSOLIDATION_SCALE + BigInt((parts[2] ?? '').padEnd(6, '0') || '0');
  return scaled <= MAX_NUMERIC_19_6 ? scaled : null;
}

function parseSignedConsolidationAmount(value: string): bigint | null {
  const input = value.trim();
  const parts = /^(-?)(\d{1,13})(?:\.(\d{1,6}))?$/.exec(input);
  if (!parts || input.length > 21) return null;
  const magnitude = BigInt(parts[2]) * CONSOLIDATION_SCALE + BigInt((parts[3] ?? '').padEnd(6, '0') || '0');
  if (magnitude > MAX_NUMERIC_19_6) return null;
  return parts[1] ? -magnitude : magnitude;
}

const INTERCOMPANY_NATURES = ['RECEIVABLE_PAYABLE', 'REVENUE_EXPENSE', 'DIVIDEND', 'INVESTMENT_EQUITY'] as const;
const INTERCOMPANY_PAGE_SIZE = 50;

function formatScaledAmount(value: bigint): string {
  return `${value / CONSOLIDATION_SCALE}.${(value % CONSOLIDATION_SCALE).toString().padStart(6, '0')}`;
}

/** Ownership is already expressed in percentage points by the API; preserve the value and append its unit. */
export function formatOwnershipPercent(value: string): string {
  const scaled = parseConsolidationAmount(value);
  if (scaled === null || scaled > MAX_OWNERSHIP_PERCENT_SCALED) return '—';
  const [whole, fraction = ''] = value.trim().split('.');
  const normalizedWhole = whole.replace(/^0+(?=\d)/, '') || '0';
  const normalizedFraction = fraction.replace(/0+$/, '');
  return `${normalizedWhole}${normalizedFraction ? `.${normalizedFraction}` : ''}%`;
}

@Component({
  selector: 'audit-consolidation-scope-workspace',
  imports: [FormsModule, RouterLink, MatButtonModule, ConsolidationOwnership, ConsolidationComponentCorrection, ConsolidationExternalPacks, ...SHARED],
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

        @if (w.scope.status === 'DRAFT' && w.canReview) {
          <div class="actions" style="margin-top: 1rem;">
            <button matButton="filled" (click)="approveScope(w.scope.id)" [disabled]="cmd.busy()">
              Approve perimeter
            </button>
          </div>
        }
      </section>

      <!-- Quick Navigation Tabs -->
      <nav aria-label="Scope sections" class="actions">
        <a [routerLink]="[]" fragment="components">Components ({{ w.componentPaging.totalCount }})</a>
        <a [routerLink]="[]" fragment="external-packs">External component packs</a>
        <a [routerLink]="[]" fragment="journals">Elimination journals ({{ w.journals.totalCount }})</a>
        <a [routerLink]="[]" fragment="runs">Runs & Reports ({{ w.runs.totalCount }})</a>
        <a [routerLink]="[]" fragment="readiness">Readiness ({{ w.componentReadiness.available ? w.readinessPaging?.totalCount ?? 0 : 'unavailable' }})</a>
        <a [routerLink]="[]" fragment="intercompany">Intercompany ({{ intercompanyCountLabel(w) }})</a>
        <a [routerLink]="[]" fragment="ownership">Ownership relationships</a>
      </nav>

      <audit-consolidation-ownership [scopeId]="w.scope.id" />

      <!-- Component Intake & Approval -->
      <section class="panel" id="components" aria-labelledby="components-heading">
        <h2 id="components-heading">Components & Intake</h2>
        <p><small>Consolidation operates strictly on approved, validated component financial packages. Preparers cannot approve their own submissions.</small></p>

        @if (w.scope.status === 'DRAFT' && w.canPrepare && w.eligiblePackages.items.length > 0) {
          <fieldset class="panel" style="margin: 1rem 0;">
            <legend><strong>Submit component from eligible package</strong></legend>
            <div class="inline-form">
              <label>
                Eligible package *
                <select [(ngModel)]="selectedPackageId" name="selectedPackage">
                  <option value="">-- Select validated package --</option>
                  @for (pkg of w.eligiblePackages.items; track pkg.packageId) {
                    <option [value]="pkg.packageId">
                      {{ pkg.clientLegalName }} ({{ pkg.currency }}, {{ pkg.periodBasis }})
                    </option>
                  }
                </select>
              </label>

              <label>
                Ownership % *
                <input type="text" inputmode="decimal" [(ngModel)]="ownershipPercent" name="ownershipPercent" maxlength="64" />
                @if (!isValidOwnershipPercent(ownershipPercent)) {
                  <small class="error-text" role="alert">Enter an ownership percentage from 0 to 100 with at most six decimal places.</small>
                }
              </label>

              <label>
                Control method *
                <select [(ngModel)]="controlMethod" name="controlMethod">
                  <option value="CONTROLLED">Controlled</option>
                  <option value="SIGNIFICANT_INFLUENCE">Significant influence</option>
                  <option value="JOINT_CONTROL">Joint control</option>
                </select>
              </label>

              <button matButton="filled" (click)="submitComponent(w)" [disabled]="cmd.busy() || !selectedPackageId || !isValidOwnershipPercent(ownershipPercent)">
                Submit component
              </button>
            </div>
          </fieldset>
        }

        @if (w.scope.status === 'DRAFT' && w.canPrepare) {
          <nav class="actions" aria-label="Eligible package pages">
            <span>{{ w.eligiblePackages.totalCount }} eligible packages · Page {{ w.eligiblePackages.page }} of {{ Math.max(1, Math.ceil(w.eligiblePackages.totalCount / w.eligiblePackages.pageSize)) }}</span>
            <button matButton="outlined" type="button" (click)="previousEligiblePackagesPage()" [disabled]="w.eligiblePackages.page <= 1 || ws.loading()">Previous</button>
            <button matButton="outlined" type="button" (click)="nextEligiblePackagesPage(w.eligiblePackages)" [disabled]="w.eligiblePackages.page * w.eligiblePackages.pageSize >= w.eligiblePackages.totalCount || ws.loading()">Next</button>
          </nav>
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
                  <td>{{ ownershipPercentDisplay(c.ownershipPercent) }}</td>
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
                    @if (w.canPrepare) {
                      <button matButton="outlined" type="button" (click)="toggleComponentCorrection(w, c.id)" [attr.aria-pressed]="isComponentCorrectionSelected(w, c.id)">
                        {{ isComponentCorrectionSelected(w, c.id) ? 'Selected for correction' : 'Correct source' }}
                      </button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="7">No components have been submitted for this scope version.</td></tr>
              }
            </tbody>
          </table>
        </div>
        <div class="actions" aria-label="Component pages">
          <button matButton="outlined" type="button" (click)="previousComponentsPage()" [disabled]="w.componentPaging.page <= 1 || ws.loading()">Previous</button>
          <span>{{ w.componentPaging.totalCount }} components · Page {{ w.componentPaging.page }} of {{ Math.max(1, Math.ceil(w.componentPaging.totalCount / w.componentPaging.pageSize)) }}</span>
          <button matButton="outlined" type="button" (click)="nextComponentsPage(w.componentPaging)" [disabled]="w.componentPaging.page * w.componentPaging.pageSize >= w.componentPaging.totalCount || ws.loading()">Next</button>
        </div>
        @if (selectedComponentCorrectionFor(w); as correction) {
          <audit-consolidation-component-correction
            [scopeId]="w.scope.id"
            [groupId]="w.scope.groupId"
            [componentId]="correction.id"
          />
        }
      </section>

      <section class="panel" id="external-packs" aria-labelledby="external-packs-heading">
        <h2 id="external-packs-heading">External component packs</h2>
        <p>Inspect source-linked submissions, reconcile and independently review them before pinning an approved pack.</p>
        <button type="button" matButton="outlined" (click)="toggleExternalPacks(w)"
          [attr.aria-expanded]="isExternalPacksOpen(w)" aria-controls="external-packs-workspace">
          {{ isExternalPacksOpen(w) ? 'Close external component packs' : 'Open external component packs' }}
        </button>
        <div id="external-packs-workspace">
          @if (isExternalPacksOpen(w)) {
            <audit-consolidation-external-packs [scopeId]="w.scope.id" (componentPinned)="ws.reload()" />
          }
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
                        <td><input type="text" [ngModel]="line.taxonomyCode" (ngModelChange)="updateJournalLine($index, 'taxonomyCode', $event)" placeholder="e.g. BS-100" /></td>
                        <td><input type="text" inputmode="decimal" maxlength="64" [ngModel]="line.debit" (ngModelChange)="updateJournalLine($index, 'debit', $event)" style="width: 8rem;" /></td>
                        <td><input type="text" inputmode="decimal" maxlength="64" [ngModel]="line.credit" (ngModelChange)="updateJournalLine($index, 'credit', $event)" style="width: 8rem;" /></td>
                        <td><input type="text" [ngModel]="line.description" (ngModelChange)="updateJournalLine($index, 'description', $event)" placeholder="Line memo" /></td>
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
                        @if (!journalAmountsValid()) {
                          <span class="error-text" role="alert">Enter non-negative decimal amounts within numeric(19,6), with no more than six decimal places.</span>
                        } @else if (isJournalBalanced()) {
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
              @for (j of w.journals.items; track j.id) {
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
                  <td class="number">{{ j.totalDebits | money:6 }}</td>
                  <td class="number">{{ j.totalCreditsAbs | money:6 }}</td>
                  <td><audit-status [value]="j.status" /></td>
                  <td>
                    <details>
                      <summary>{{ j.lines.length }} lines</summary>
                      <ul>
                        @for (l of j.lines; track l.id) {
                          <li>
                            <code>{{ l.taxonomyCode }}</code>: Dr {{ l.debit | money:6 }} / Cr {{ l.credit | money:6 }}
                            @if (l.description) { — {{ l.description }} }
                          </li>
                        }
                      </ul>
                    </details>
                  </td>
                  <td>
                    @if (j.status === 'SUBMITTED' && w.canReview && j.createdByUserId !== w.currentUserId) {
                      <div class="actions">
                        <button matButton="filled" (click)="approveJournal(j.id)" [disabled]="cmd.busy()">
                          Approve
                        </button>
                        <button type="button" (click)="promptReturnJournal(j.id)" [disabled]="cmd.busy()">
                          Return
                        </button>
                      </div>
                    } @else if (j.status === 'SUBMITTED' && j.createdByUserId === w.currentUserId) {
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
        <nav class="actions" aria-label="Elimination journal pages">
          <span>{{ w.journals.items.length }} of {{ w.journals.totalCount }} journals · Page {{ w.journals.page }} of {{ Math.max(1, Math.ceil(w.journals.totalCount / w.journals.pageSize)) }}</span>
          <button matButton="outlined" type="button" (click)="previousJournalsPage()" [disabled]="w.journals.page <= 1 || ws.loading()">Previous</button>
          <button matButton="outlined" type="button" (click)="nextJournalsPage(w.journals)" [disabled]="w.journals.page * w.journals.pageSize >= w.journals.totalCount || ws.loading()">Next</button>
        </nav>
      </section>

      <!-- Consolidation Runs & Reports -->
      <section class="panel" id="runs" aria-labelledby="runs-heading">
        <h2 id="runs-heading">Consolidation runs & reports</h2>
        <p><small>Consolidation executes over approved components and approved journals. Runs require maker/checker review before generating authoritative reports.</small></p>

        @if (w.canPrepare && w.scope.status === 'APPROVED') {
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
              @for (r of w.runs.items; track r.id) {
                <tr>
                  <td>{{ r.createdAt.slice(0, 16).replace('T', ' ') }} UTC</td>
                  <td><code>{{ r.engineVersion }}</code></td>
                  <td><code>{{ r.runHash.slice(0, 12) }}…</code></td>
                  <td class="number">{{ r.signedTotal | money }}</td>
                  <td><audit-status [value]="r.status" /></td>
                  <td>
                    @if (r.status === 'SUBMITTED' && w.canReview && r.createdByUserId !== w.currentUserId) {
                      <button matButton="filled" (click)="approveRun(r.id)" [disabled]="cmd.busy()">
                        Approve run
                      </button>
                    } @else if (r.status === 'SUBMITTED' && r.createdByUserId === w.currentUserId) {
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

        <nav class="actions" aria-label="Consolidation run pages">
          <span>{{ w.runs.items.length }} of {{ w.runs.totalCount }} runs · Page {{ w.runs.page }} of {{ Math.max(1, Math.ceil(w.runs.totalCount / w.runs.pageSize)) }}</span>
          <button matButton="outlined" type="button" (click)="previousRunsPage()" [disabled]="w.runs.page <= 1 || ws.loading()">Previous</button>
          <button matButton="outlined" type="button" (click)="nextRunsPage(w.runs)" [disabled]="w.runs.page * w.runs.pageSize >= w.runs.totalCount || ws.loading()">Next</button>
        </nav>

        @if (!w.latestReport.available) {
          <p class="error-text" role="alert">Latest report unavailable ({{ w.latestReport.errorCode }}): {{ w.latestReport.message }}</p>
        } @else if (w.latestReport.value?.state === 'CURRENT_APPROVED') {
          <div class="panel" style="margin-top: 1.5rem;">
            <h3>Authoritative consolidated report</h3>
            <p><small>Run {{ w.latestReport.value!.runId }} · Approved {{ w.latestReport.value!.approvedAt ? w.latestReport.value!.approvedAt!.slice(0, 16).replace('T', ' ') : '' }}</small></p>

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
                  @for (l of w.latestReport.value!.lines; track $index) {
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
              @if (!w.componentReadiness.available) {
                <tr><td colspan="6" class="error-text">Readiness unavailable ({{ w.componentReadiness.errorCode }}): {{ w.componentReadiness.message }}</td></tr>
              } @else {
                @for (m of w.componentReadiness.value ?? []; track m.clientId) {
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
              }
            </tbody>
          </table>
        </div>
        @if (w.readinessPaging; as paging) {
          <div class="actions" aria-label="Readiness pages">
            <button matButton="outlined" type="button" (click)="previousReadinessPage()" [disabled]="paging.page <= 1 || ws.loading()">Previous</button>
            <span>{{ paging.totalCount }} members · Page {{ paging.page }} of {{ Math.max(1, Math.ceil(paging.totalCount / paging.pageSize)) }}</span>
            <button matButton="outlined" type="button" (click)="nextReadinessPage(paging)" [disabled]="paging.page * paging.pageSize >= paging.totalCount || ws.loading()">Next</button>
          </div>
        }
      </section>

      <!-- Intercompany Matching, Independent Review and Retained History -->
      <section class="panel" id="intercompany" aria-labelledby="intercompany-heading">
        <h2 id="intercompany-heading">Intercompany matches & exceptions</h2>
        <p>Matches retain their exact source amounts and evidence. Approval is independent and does not create an elimination journal or post to component books.</p>

        @if (!w.intercompanyExceptions.available) {
          <p class="error-text" role="alert">Exception history unavailable ({{ w.intercompanyExceptions.errorCode }}): {{ w.intercompanyExceptions.message }}</p>
        }

        <div class="actions" role="group" aria-label="Intercompany match view">
          <button matButton="outlined" type="button" [attr.aria-pressed]="!intercompanyUnresolvedOnly()" (click)="showIntercompanyHistory()">All matches & history</button>
          <button matButton="outlined" type="button" [attr.aria-pressed]="intercompanyUnresolvedOnly()" (click)="showIntercompanyExceptions()">Unresolved exceptions</button>
        </div>

        @if (intercompanyMatchOptions.loading()) { <p role="status">Loading bounded group members and taxonomy options…</p> }
        @if (intercompanyMatchOptions.error()) { <p class="error-text" role="alert">{{ intercompanyMatchOptions.error() }}</p> }
        @if (intercompanyMatchOptions.data(); as options) {
          <p>
            Scope revision {{ options.pinnedGroupRevision }} · current group revision {{ options.currentGroupRevision }}
            · reporting currency <code>{{ options.reportingCurrency }}</code>
            @if (options.periodStartDate && options.periodEndDate) {
              · membership overlaps scope reporting period <code>{{ options.periodStartDate }}</code>–<code>{{ options.periodEndDate }}</code>
            }
          </p>
          @if (options.membershipBlocker) {
            <p class="error-text" role="alert">{{ options.membershipBlocker }}</p>
          }
          @if (!isCurrentIntercompanyRevision(w, options)) {
            <p class="error-text" role="alert">The group perimeter changed after this scope was approved. Refresh and review a current scope before recording or approving matches.</p>
          }
          @if (options.membersTruncated || options.taxonomyCodesTruncated) {
            <p role="status">Options are capped at 500 entries. Refine the approved group evidence before matching if a needed member or taxonomy code is not shown.</p>
          }

          @if (w.canPrepare) {
            <form class="panel" style="margin: 1rem 0;" (ngSubmit)="submitIntercompanyMatch(w, options)" aria-label="Record intercompany match">
              <h3>Record a match for independent review</h3>
              <p><small>Amounts are exact signed decimal strings, with up to six fractional digits. Review the current group revision before submitting.</small></p>
              <div class="inline-form">
                <label>Account nature
                  <select name="intercompanyNature" [(ngModel)]="intercompanyForm.accountNature" (ngModelChange)="invalidateIntercompanyReview()" required>
                    @for (nature of intercompanyNatures; track nature) { <option [value]="nature">{{ nature }}</option> }
                  </select>
                </label>
                <label>Period code
                  <input name="intercompanyPeriod" [(ngModel)]="intercompanyForm.periodCode" (ngModelChange)="invalidateIntercompanyReview()" maxlength="100" required>
                </label>
                <label>Currency
                  <input name="intercompanyCurrency" [ngModel]="options.reportingCurrency" readonly>
                </label>
                <label>Transaction reference
                  <input name="intercompanyTransaction" [(ngModel)]="intercompanyForm.transactionReference" (ngModelChange)="invalidateIntercompanyReview()" maxlength="200" required>
                </label>
                <label>Match mode
                  <select name="intercompanyMode" [(ngModel)]="intercompanyForm.matchMode" (ngModelChange)="invalidateIntercompanyReview()">
                    <option value="ONE_TO_ONE">One to one</option>
                    <option value="GROUPED">Grouped</option>
                  </select>
                </label>
                @if (intercompanyForm.matchMode === 'GROUPED') {
                  <label>Match group reference
                    <input name="intercompanyGroupReference" [(ngModel)]="intercompanyForm.matchGroupReference" (ngModelChange)="invalidateIntercompanyReview()" maxlength="200" required>
                  </label>
                }
              </div>

              <label style="display: block; margin: .75rem 0;">
                <input type="checkbox" name="outsidePerimeterReview" [(ngModel)]="intercompanyForm.outsidePerimeterReview" (ngModelChange)="invalidateIntercompanyReview()">
                Related party is outside the approved group perimeter (review only; no automatic elimination)
              </label>
              @if (intercompanyForm.outsidePerimeterReview) {
                <div class="inline-form">
                  <label>Outside party side
                    <select name="outsidePartySide" [(ngModel)]="intercompanyForm.outsidePartySide" (ngModelChange)="invalidateIntercompanyReview()">
                      <option value="SELLER">Seller</option>
                      <option value="BUYER">Buyer</option>
                    </select>
                  </label>
                  <label>Outside client ID
                    <input name="outsideClientId" [(ngModel)]="intercompanyForm.outsideClientId" (ngModelChange)="invalidateIntercompanyReview()" maxlength="36" required>
                  </label>
                </div>
              }

              <div class="inline-form">
                <label>Seller group member
                  <select name="sellerMember" [(ngModel)]="intercompanyForm.sellerClientId" (ngModelChange)="invalidateIntercompanyReview()" [disabled]="intercompanyForm.outsidePerimeterReview && intercompanyForm.outsidePartySide === 'SELLER'" required>
                    <option value="">Select a member</option>
                    @for (member of intercompanyMemberOptions(options); track member.clientId) { <option [value]="member.clientId">{{ member.legalName }}{{ member.hasApprovedComponent ? '' : ' · no approved component' }}</option> }
                  </select>
                </label>
                <label>Buyer group member
                  <select name="buyerMember" [(ngModel)]="intercompanyForm.buyerClientId" (ngModelChange)="invalidateIntercompanyReview()" [disabled]="intercompanyForm.outsidePerimeterReview && intercompanyForm.outsidePartySide === 'BUYER'" required>
                    <option value="">Select a member</option>
                    @for (member of intercompanyMemberOptions(options); track member.clientId) { <option [value]="member.clientId">{{ member.legalName }}{{ member.hasApprovedComponent ? '' : ' · no approved component' }}</option> }
                  </select>
                </label>
              </div>

              <div class="inline-form">
                <label>Seller exact amount
                  <input name="sellerExactAmount" type="text" inputmode="decimal" autocomplete="off" [(ngModel)]="intercompanyForm.sellerAmount" (ngModelChange)="invalidateIntercompanyReview()" maxlength="21" placeholder="-1200.123456" required>
                </label>
                <label>Buyer exact amount
                  <input name="buyerExactAmount" type="text" inputmode="decimal" autocomplete="off" [(ngModel)]="intercompanyForm.buyerAmount" (ngModelChange)="invalidateIntercompanyReview()" maxlength="21" placeholder="1200.123456" required>
                </label>
                <label>Matched amount
                  <input name="matchedExactAmount" type="text" inputmode="decimal" autocomplete="off" [(ngModel)]="intercompanyForm.matchedAmount" (ngModelChange)="invalidateIntercompanyReview()" maxlength="21" placeholder="1200.123456" required>
                </label>
              </div>

              <div class="inline-form">
                <label>Seller taxonomy code
                  <input name="sellerTaxonomy" list="intercompany-taxonomy-options" [(ngModel)]="intercompanyForm.sellerTaxonomyCode" (ngModelChange)="invalidateIntercompanyReview()" maxlength="100" required>
                </label>
                <label>Buyer taxonomy code
                  <input name="buyerTaxonomy" list="intercompany-taxonomy-options" [(ngModel)]="intercompanyForm.buyerTaxonomyCode" (ngModelChange)="invalidateIntercompanyReview()" maxlength="100" required>
                </label>
                <datalist id="intercompany-taxonomy-options">
                  @for (code of options.taxonomyCodes; track code) { <option [value]="code"></option> }
                </datalist>
                <label>Evidence reference
                  <input name="intercompanyEvidence" [(ngModel)]="intercompanyForm.evidenceReference" (ngModelChange)="invalidateIntercompanyReview()" maxlength="2000" required>
                </label>
                <label>Unmatched balance or signed difference reason
                  <input name="intercompanyDifferenceReason" [(ngModel)]="intercompanyForm.differenceReason" (ngModelChange)="invalidateIntercompanyReview()" maxlength="2000">
                  @if (intercompanyDifferenceReasonRequired()) {
                    <small>Required when either side remains unmatched or the stored signed difference is nonzero.</small>
                  }
                </label>
              </div>

              <label style="display: block; margin: .75rem 0;">
                <input type="checkbox" name="reviewedIntercompanyMatch" [(ngModel)]="intercompanyForm.reviewed">
                I reviewed both counterparties, exact amounts, taxonomies, evidence and the current group revision.
              </label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy() || !canSubmitIntercompanyMatch(w, options)">Submit for independent review</button>
            </form>
          }
        }

        @if (intercompanyMatches.loading()) { <p role="status">Loading retained intercompany matches…</p> }
        @if (intercompanyMatches.error()) { <p class="error-text" role="alert">{{ intercompanyMatches.error() }}</p> }
        @if (intercompanyMatches.data(); as matchPage) {
          <p>
            {{ intercompanyUnresolvedOnly() ? 'Unresolved exceptions' : 'All matches, including resolved history' }}
            · {{ matchPage.totalCount }} records
            · signed net difference (stored; rows may offset) <code>{{ matchPage.totalUnmatchedDifference }}</code>
            · gross unmatched exposure across {{ matchPage.totalCount }} matches in this view <code>{{ matchPage.totalGrossUnmatchedExposure }}</code>
            · scope revision {{ matchPage.pinnedGroupRevision }} / current group revision {{ matchPage.currentGroupRevision }}
          </p>
          <div class="table-scroll">
            <table>
              <thead><tr><th>Match</th><th>Full retained record</th><th>Status</th><th>Independent review</th></tr></thead>
              <tbody>
                @for (item of matchPage.items; track item.matchId) {
                  <tr>
                    <td><code>{{ item.matchId }}</code></td>
                    <td>
                      <details>
                        <summary>{{ item.accountNature }} · {{ item.periodCode }} · {{ item.currency }} · {{ item.transactionReference }}</summary>
                        <dl class="facts">
                          <dt>Seller client / taxonomy / exact amount</dt><dd><code>{{ item.sellerClientId }}</code> · <code>{{ item.sellerTaxonomyCode }}</code> · <code>{{ item.sellerAmount }}</code></dd>
                          <dt>Buyer client / taxonomy / exact amount</dt><dd><code>{{ item.buyerClientId }}</code> · <code>{{ item.buyerTaxonomyCode }}</code> · <code>{{ item.buyerAmount }}</code></dd>
                          <dt>Matched amount / stored signed difference</dt><dd><code>{{ item.matchedAmount }}</code> / <code>{{ item.difference }}</code></dd>
                          <dt>Gross unmatched residual on both sides</dt><dd><code>{{ item.unmatchedResidual }}</code></dd>
                          <dt>Difference reason</dt><dd>{{ item.differenceReason || 'None recorded' }}</dd>
                          <dt>Mode / group reference</dt><dd>{{ item.matchMode }} / {{ item.matchGroupReference || '—' }}</dd>
                          <dt>Outside-perimeter review</dt><dd>{{ item.outsidePerimeterReview ? 'Yes · review only' : 'No' }}</dd>
                          <dt>Evidence reference</dt><dd>{{ item.evidenceReference }}</dd>
                          <dt>Prepared by / at</dt><dd><code>{{ item.createdByUserId }}</code> · {{ item.createdAt }}</dd>
                          <dt>Reviewed by / at</dt><dd>{{ item.reviewedByUserId ? item.reviewedByUserId : 'Not reviewed' }} · {{ item.reviewedAt || '—' }}</dd>
                        </dl>
                      </details>
                    </td>
                    <td><audit-status [value]="item.status" /></td>
                    <td>
                      @if (canApproveIntercompanyMatch(w, item, matchPage)) {
                        <label><input type="checkbox" [ngModel]="isIntercompanyApprovalReviewed(item.matchId)" [ngModelOptions]="{ standalone: true }" (ngModelChange)="setIntercompanyApprovalReviewed(item.matchId, $event)"> I reviewed the full retained match and current revision</label>
                        <button matButton="filled" type="button" (click)="approveIntercompanyMatch(w, item, matchPage)" [disabled]="cmd.busy() || !isIntercompanyApprovalReviewed(item.matchId)">Approve match</button>
                      } @else if (item.status === 'SUBMITTED' && item.createdByUserId === w.currentUserId) {
                        <span>Maker/checker: preparers cannot approve their own match.</span>
                      } @else if (item.status === 'SUBMITTED' && !isCurrentIntercompanyPageRevision(w, matchPage)) {
                        <span class="error-text">The group revision changed. Approval is disabled.</span>
                      } @else if (item.status === 'SUBMITTED') {
                        <span>Independent reviewer grant required.</span>
                      } @else {
                        <span>{{ item.reviewedByUserId ? 'Reviewed by an independent user.' : 'No approval action.' }}</span>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="4">No {{ intercompanyUnresolvedOnly() ? 'unresolved exceptions' : 'retained matches' }} found.</td></tr>
                }
              </tbody>
            </table>
          </div>
          <div class="actions" aria-label="Intercompany match pages">
            <button matButton="outlined" type="button" (click)="previousIntercompanyPage()" [disabled]="matchPage.page <= 1 || intercompanyMatches.loading()">Previous</button>
            <span>Page {{ matchPage.page }} of {{ Math.max(1, Math.ceil(matchPage.totalCount / matchPage.pageSize)) }}</span>
            <button matButton="outlined" type="button" (click)="nextIntercompanyPage(matchPage)" [disabled]="matchPage.page * matchPage.pageSize >= matchPage.totalCount || intercompanyMatches.loading()">Next</button>
          </div>
        }
      </section>
    }

    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class ConsolidationScopeWorkspace {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly componentsPageNumber = signal(1);
  readonly readinessPageNumber = signal(1);
  readonly eligiblePackagesPageNumber = signal(1);
  readonly journalsPageNumber = signal(1);
  readonly runsPageNumber = signal(1);
  private historyPageScopeId: string | null = null;
  readonly selectedComponentCorrection = signal<{ scopeId: string; componentId: string } | null>(null);
  readonly externalPacksScopeId = signal<string | null>(null);
  private readonly clearCorrectionSelectionOnScopeChange = effect(() => {
    const scopeId = this.id();
    untracked(() => {
      const selected = this.selectedComponentCorrection();
      if (selected && selected.scopeId !== scopeId?.toLowerCase()) this.selectedComponentCorrection.set(null);
      if (this.externalPacksScopeId() !== scopeId?.toLowerCase()) this.externalPacksScopeId.set(null);
    });
  });
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
  readonly intercompanyMatchOptions = this.api.resource(
    () => (this.id() ? `/api/ui/consolidation/scopes/${this.id()}/intercompany-match-options` : null),
    (value, path) => {
      const options = decodeIntercompanyMatchOptions(value, path);
      if (options.scopeVersionId !== this.id()) throw new Error('Intercompany options belong to a different scope.');
      return options;
    },
    'Intercompany match options are unavailable in this group scope.',
  );
  readonly intercompanyUnresolvedOnly = signal(false);
  readonly intercompanyPageNumber = signal(1);
  readonly intercompanyMatches = this.api.resource(
    () => this.id()
      ? `/api/ui/consolidation/scopes/${this.id()}/intercompany-matches?page=${this.intercompanyPageNumber()}&pageSize=${INTERCOMPANY_PAGE_SIZE}&unresolvedOnly=${this.intercompanyUnresolvedOnly()}`
      : null,
    (value, path) => {
      const page = decodeIntercompanyExceptionsPage(value, path);
      if (page.scopeVersionId !== this.id() || page.page !== this.intercompanyPageNumber() || page.pageSize !== INTERCOMPANY_PAGE_SIZE)
        throw new Error('Intercompany matches belong to a different scope or page.');
      return page;
    },
    'Retained intercompany matches are unavailable in this group scope.',
  );
  readonly cmd = new CommandState(this.api);

  intercompanyCountLabel(w: ScopeWorkspace): string {
    const page = this.intercompanyMatches.data();
    if (page) return String(page.totalCount);
    const workspacePage = w.intercompanyExceptions.value;
    return w.intercompanyExceptions.available
      ? String(workspacePage?.totalCount ?? 0)
      : `unavailable (${w.intercompanyExceptions.errorCode})`;
  }

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

  previousReadinessPage(): void {
    this.readinessPageNumber.update(page => Math.max(1, page - 1));
  }

  nextReadinessPage(page: NonNullable<ScopeWorkspace['readinessPaging']>): void {
    if (page.page * page.pageSize < page.totalCount) this.readinessPageNumber.set(page.page + 1);
  }

  previousEligiblePackagesPage(): void {
    this.eligiblePackagesPageNumber.update((page) => Math.max(1, page - 1));
    this.selectedPackageId = '';
  }

  nextEligiblePackagesPage(page: ScopeWorkspace['eligiblePackages']): void {
    if (page.page * page.pageSize < page.totalCount) {
      this.eligiblePackagesPageNumber.set(page.page + 1);
      this.selectedPackageId = '';
    }
  }

  previousJournalsPage(): void {
    this.journalsPageNumber.update((page) => Math.max(1, page - 1));
  }

  nextJournalsPage(page: ScopeWorkspace['journals']): void {
    if (page.page * page.pageSize < page.totalCount) this.journalsPageNumber.set(page.page + 1);
  }

  previousRunsPage(): void {
    this.runsPageNumber.update((page) => Math.max(1, page - 1));
  }

  nextRunsPage(page: ScopeWorkspace['runs']): void {
    if (page.page * page.pageSize < page.totalCount) this.runsPageNumber.set(page.page + 1);
  }

  readonly intercompanyNatures = INTERCOMPANY_NATURES;
  private newIntercompanyForm() { return {
    sellerClientId: '',
    buyerClientId: '',
    outsideClientId: '',
    outsidePartySide: 'BUYER' as 'SELLER' | 'BUYER',
    accountNature: 'RECEIVABLE_PAYABLE',
    periodCode: '',
    transactionReference: '',
    matchMode: 'ONE_TO_ONE',
    matchGroupReference: '',
    outsidePerimeterReview: false,
    sellerAmount: '',
    buyerAmount: '',
    matchedAmount: '',
    sellerTaxonomyCode: '',
    buyerTaxonomyCode: '',
    evidenceReference: '',
    differenceReason: '',
    reviewed: false,
  }; }
  intercompanyForm = this.newIntercompanyForm();
  private intercompanyApprovalChecks: Record<string, boolean> = {};

  // Component Submission Form State
  selectedPackageId = '';
  private readonly resetHistoryPagesOnScopeChange = effect(() => {
    const scopeId = this.id()?.toLowerCase() ?? null;
    untracked(() => {
      if (scopeId === this.historyPageScopeId) return;
      this.historyPageScopeId = scopeId;
      this.componentsPageNumber.set(1);
      this.readinessPageNumber.set(1);
      this.eligiblePackagesPageNumber.set(1);
      this.journalsPageNumber.set(1);
      this.runsPageNumber.set(1);
      this.intercompanyPageNumber.set(1);
      this.intercompanyUnresolvedOnly.set(false);
      this.intercompanyForm = this.newIntercompanyForm();
      this.intercompanyApprovalChecks = {};
      this.selectedPackageId = '';
    });
  });
  ownershipPercent = '100';
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

  isComponentCorrectionSelected(w: ScopeWorkspace, componentId: string): boolean {
    const selected = this.selectedComponentCorrection();
    return selected?.scopeId === w.scope.id.toLowerCase() && selected.componentId === componentId.toLowerCase() &&
      w.canPrepare && w.components.some((component) => component.id.toLowerCase() === componentId.toLowerCase());
  }

  toggleComponentCorrection(w: ScopeWorkspace, componentId: string): void {
    const component = w.components.find((item) => item.id.toLowerCase() === componentId.toLowerCase());
    if (!w.canPrepare || !component) return;
    if (this.isComponentCorrectionSelected(w, component.id)) {
      this.selectedComponentCorrection.set(null);
      return;
    }
    this.selectedComponentCorrection.set({ scopeId: w.scope.id.toLowerCase(), componentId: component.id.toLowerCase() });
  }

  selectedComponentCorrectionFor(w: ScopeWorkspace): ConsolidationComponent | null {
    const selected = this.selectedComponentCorrection();
    if (!w.canPrepare || !selected || selected.scopeId !== w.scope.id.toLowerCase()) return null;
    return w.components.find((component) => component.id.toLowerCase() === selected.componentId) ?? null;
  }

  isExternalPacksOpen(w: ScopeWorkspace): boolean {
    return w.scope.id.toLowerCase() === this.id()?.toLowerCase() &&
      this.externalPacksScopeId() === w.scope.id.toLowerCase();
  }

  toggleExternalPacks(w: ScopeWorkspace): void {
    if (w.scope.id.toLowerCase() !== this.id()?.toLowerCase()) return;
    this.externalPacksScopeId.set(this.isExternalPacksOpen(w) ? null : w.scope.id.toLowerCase());
  }

  intercompanyMemberOptions(options: IntercompanyMatchOptions): IntercompanyMatchOptions['members'] {
    return options.members.filter((member) => this.intercompanyForm.outsidePerimeterReview || member.hasApprovedComponent);
  }

  isCurrentIntercompanyRevision(w: ScopeWorkspace, options: IntercompanyMatchOptions): boolean {
    return options.scopeVersionId === w.scope.id && options.groupId === w.scope.groupId &&
      options.pinnedGroupRevision === w.scope.groupRevision && options.currentGroupRevision === options.pinnedGroupRevision;
  }

  isCurrentIntercompanyPageRevision(w: ScopeWorkspace, page: IntercompanyExceptionsPage): boolean {
    const options = this.intercompanyMatchOptions.data();
    return page.scopeVersionId === w.scope.id && page.groupId === w.scope.groupId &&
      page.pinnedGroupRevision === w.scope.groupRevision && page.currentGroupRevision === page.pinnedGroupRevision &&
      options !== null && this.isCurrentIntercompanyRevision(w, options) &&
      options.currentGroupRevision === page.currentGroupRevision;
  }

  canSubmitIntercompanyMatch(w: ScopeWorkspace, options: IntercompanyMatchOptions): boolean {
    const form = this.intercompanyForm;
    if (!w.canPrepare || !this.isCurrentIntercompanyRevision(w, options) || options.periodStartDate === null || options.periodEndDate === null ||
        options.periodStartDate > options.periodEndDate || options.membershipBlocker !== null ||
        !INTERCOMPANY_NATURES.includes(form.accountNature as typeof INTERCOMPANY_NATURES[number]) ||
        !form.periodCode.trim() || form.periodCode.trim().length > 100 || !form.transactionReference.trim() || form.transactionReference.trim().length > 200 ||
        !form.evidenceReference.trim() || form.evidenceReference.trim().length > 2000 ||
        !form.sellerTaxonomyCode.trim() || form.sellerTaxonomyCode.trim().length > 100 ||
        !form.buyerTaxonomyCode.trim() || form.buyerTaxonomyCode.trim().length > 100 || form.differenceReason.trim().length > 2000 ||
        !form.reviewed || !['ONE_TO_ONE', 'GROUPED'].includes(form.matchMode) ||
        form.matchMode === 'GROUPED' && !form.matchGroupReference.trim()) return false;

    const sellerId = this.intercompanyClientId('SELLER');
    const buyerId = this.intercompanyClientId('BUYER');
    if (!this.isGuid(sellerId) || !this.isGuid(buyerId) || sellerId === buyerId) return false;
    const sellerMember = options.members.find((member) => member.clientId === sellerId);
    const buyerMember = options.members.find((member) => member.clientId === buyerId);
    if (form.outsidePerimeterReview) {
      const internalMember = form.outsidePartySide === 'SELLER' ? buyerMember : sellerMember;
      if (!internalMember || !this.isGuid(form.outsideClientId) || options.members.some((member) => member.clientId === form.outsideClientId)) return false;
    } else if (!sellerMember?.hasApprovedComponent || !buyerMember?.hasApprovedComponent) {
      return false;
    }

    const seller = parseSignedConsolidationAmount(form.sellerAmount);
    const buyer = parseSignedConsolidationAmount(form.buyerAmount);
    const matched = parseSignedConsolidationAmount(form.matchedAmount);
    if (seller === null || buyer === null || matched === null || matched < 0n || matched > (seller < 0n ? -seller : seller) ||
        matched > (buyer < 0n ? -buyer : buyer)) return false;
    const sellerResidual = (seller < 0n ? -seller : seller) - matched;
    const buyerResidual = (buyer < 0n ? -buyer : buyer) - matched;
    return (seller + buyer === 0n && sellerResidual === 0n && buyerResidual === 0n) || Boolean(form.differenceReason.trim());
  }

  intercompanyDifferenceReasonRequired(): boolean {
    const seller = parseSignedConsolidationAmount(this.intercompanyForm.sellerAmount);
    const buyer = parseSignedConsolidationAmount(this.intercompanyForm.buyerAmount);
    const matched = parseSignedConsolidationAmount(this.intercompanyForm.matchedAmount);
    if (seller === null || buyer === null || matched === null || matched < 0n) return false;
    const sellerMagnitude = seller < 0n ? -seller : seller;
    const buyerMagnitude = buyer < 0n ? -buyer : buyer;
    return seller + buyer !== 0n || sellerMagnitude > matched || buyerMagnitude > matched;
  }

  submitIntercompanyMatch(w: ScopeWorkspace, options: IntercompanyMatchOptions): void {
    if (!this.canSubmitIntercompanyMatch(w, options)) return;
    const form = this.intercompanyForm;
    const body = {
      sellerClientId: this.intercompanyClientId('SELLER'),
      buyerClientId: this.intercompanyClientId('BUYER'),
      accountNature: form.accountNature,
      periodCode: form.periodCode.trim(),
      currency: options.reportingCurrency,
      transactionReference: form.transactionReference.trim(),
      sellerAmount: form.sellerAmount.trim(),
      buyerAmount: form.buyerAmount.trim(),
      matchedAmount: form.matchedAmount.trim(),
      evidenceReference: form.evidenceReference.trim(),
      sellerTaxonomyCode: form.sellerTaxonomyCode.trim(),
      buyerTaxonomyCode: form.buyerTaxonomyCode.trim(),
      differenceReason: form.differenceReason.trim(),
      matchMode: form.matchMode,
      matchGroupReference: form.matchGroupReference.trim(),
      outsidePerimeterReview: form.outsidePerimeterReview,
      expectedGroupRevision: options.currentGroupRevision,
      reviewed: true,
    };
    void this.cmd.run(
      `/api/ui/consolidation/scopes/${w.scope.id}/intercompany-matches`, body,
      'Intercompany match submitted for independent review.',
    ).then((success) => {
      if (!success) return;
      this.intercompanyForm = this.newIntercompanyForm();
      this.intercompanyPageNumber.set(1);
      this.ws.reload();
      this.intercompanyMatchOptions.reload();
      this.intercompanyMatches.reload();
    });
  }

  showIntercompanyHistory(): void {
    this.intercompanyPageNumber.set(1);
    this.intercompanyUnresolvedOnly.set(false);
  }

  showIntercompanyExceptions(): void {
    this.intercompanyPageNumber.set(1);
    this.intercompanyUnresolvedOnly.set(true);
  }

  previousIntercompanyPage(): void {
    this.intercompanyPageNumber.update((page) => Math.max(1, page - 1));
  }

  nextIntercompanyPage(page: IntercompanyExceptionsPage): void {
    if (page.page * page.pageSize < page.totalCount) this.intercompanyPageNumber.set(page.page + 1);
  }

  isIntercompanyApprovalReviewed(matchId: string): boolean {
    return this.intercompanyApprovalChecks[matchId] === true;
  }

  setIntercompanyApprovalReviewed(matchId: string, reviewed: boolean): void {
    this.intercompanyApprovalChecks = { ...this.intercompanyApprovalChecks, [matchId]: reviewed };
  }

  canApproveIntercompanyMatch(w: ScopeWorkspace, item: IntercompanyException, page: IntercompanyExceptionsPage): boolean {
    return w.canReview && item.status === 'SUBMITTED' && item.createdByUserId !== w.currentUserId &&
      this.isCurrentIntercompanyPageRevision(w, page);
  }

  approveIntercompanyMatch(w: ScopeWorkspace, item: IntercompanyException, page: IntercompanyExceptionsPage): void {
    if (!this.canApproveIntercompanyMatch(w, item, page) || !this.isIntercompanyApprovalReviewed(item.matchId)) return;
    void this.cmd.run(
      `/api/ui/consolidation/intercompany-matches/${item.matchId}/approve`,
      { expectedGroupRevision: page.currentGroupRevision, reviewed: true },
      'Intercompany match approved. No component book or elimination journal was created.',
    ).then((success) => {
      if (!success) return;
      this.setIntercompanyApprovalReviewed(item.matchId, false);
      this.ws.reload();
      this.intercompanyMatchOptions.reload();
      this.intercompanyMatches.reload();
    });
  }

  private intercompanyClientId(side: 'SELLER' | 'BUYER'): string {
    const form = this.intercompanyForm;
    if (form.outsidePerimeterReview && form.outsidePartySide === side) return form.outsideClientId.trim().toLowerCase();
    return (side === 'SELLER' ? form.sellerClientId : form.buyerClientId).trim().toLowerCase();
  }

  private isGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  }

  invalidateIntercompanyReview(): void {
    this.intercompanyForm.reviewed = false;
  }

  isValidOwnershipPercent(value: string): boolean {
    const scaled = parseConsolidationAmount(value);
    return scaled !== null && scaled <= MAX_OWNERSHIP_PERCENT_SCALED;
  }

  ownershipPercentDisplay(value: string): string {
    return formatOwnershipPercent(value);
  }

  updateJournalLine(index: number, field: keyof NewJournalLine, value: string): void {
    this.journalLines.update((lines) => lines.map((line, i) => i === index ? { ...line, [field]: value } : line));
  }

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
    const pkg = w.eligiblePackages.items.find((p) => p.packageId === this.selectedPackageId);
    const ownershipPercent = this.ownershipPercent.trim();
    if (!pkg || !this.isValidOwnershipPercent(ownershipPercent)) return;

    void this.cmd.run(
      `/api/ui/consolidation/scopes/${w.scope.id}/components`,
      {
        clientId: pkg.clientId,
        engagementId: pkg.engagementId,
        packageId: pkg.packageId,
        ownershipPercent,
        controlMethod: this.controlMethod,
        periodBasis: pkg.periodBasis,
        taxonomyVersion: pkg.taxonomyVersion,
        mappingVersion: pkg.mappingVersion,
      },
      'Component submitted for independent review.',
      () => {
        this.selectedPackageId = '';
        this.ownershipPercent = '100';
      },
    ).finally(() => this.ws.reload());
  }

  approveComponent(componentId: string): void {
    void this.cmd.run(`/api/ui/consolidation/components/${componentId}/approve`, {}, 'Component approved.')
      .finally(() => this.ws.reload());
  }

  createJournal(scopeId: string): void {
    const totals = this.journalTotals();
    if (!totals || totals.debit <= 0n || totals.debit !== totals.credit) return;
    const lines = this.journalLines().map((l) => ({
      intercompanyMatchId: null,
      taxonomyCode: l.taxonomyCode.trim(),
      debit: l.debit.trim(),
      credit: l.credit.trim(),
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
        this.journalsPageNumber.set(1);
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
    void this.cmd.run(`/api/ui/consolidation/scopes/${scopeId}/run`, {}, 'Consolidation run calculated.', () => this.runsPageNumber.set(1))
      .finally(() => this.ws.reload());
  }

  approveRun(runId: string): void {
    void this.cmd.run(`/api/ui/consolidation/runs/${runId}/approve`, {}, 'Consolidation run approved.')
      .finally(() => this.ws.reload());
  }
}
