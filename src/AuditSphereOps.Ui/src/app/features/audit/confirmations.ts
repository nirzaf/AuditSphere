import {
  Component,
  DestroyRef,
  HostListener,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { form, FormField, required, maxLength, applyEach } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { TabDrafts, DraftRead, DraftScope } from '../../core/tab-drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import {
  emptyCreate,
  emptyBatchCase,
  emptyBatch,
  emptyAction,
  intent,
  createDraft,
  batchDraft,
  actionDraft,
  DraftKind,
} from './confirmation-drafts';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { confirmationPage, confirmationDetail, confirmationAmount } from './confirmation-contracts';
@Component({
  selector: 'audit-confirmations',
  imports: [FormField, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb">
      <a [routerLink]="['/app/engagements', id()]">Engagement</a> /
      <a [routerLink]="['/app/engagements', id(), 'audit-fieldwork']">Fieldwork</a>
    </nav>
    <audit-page-header
      title="Confirmations"
      eyebrow="Audit evidence"
      description="Prepare cases, record observed dispatch and responses, and independently review alternative work."
    />
    <p>
      Preparation and approval do not send a confirmation. Dispatch is recorded only from an
      observed approved-channel reference; this workspace sends no external messages.
    </p>
    <section class="panel" aria-label="Confirmation draft recovery">
      <h2>Unsubmitted tab drafts</h2>
      <p>
        Save only in this browser tab for up to four hours. Drafts are not evidence or shared server
        records. No review authorization, credentials or files are retained. Recover explicitly
        after loading the current authorized revision, then review again.
      </p>
      @for (kind of draftKinds; track kind) {
        @if (kind !== 'action' || action()) {
          <section [attr.aria-label]="draftLabel(kind) + ' draft'">
            <h3>{{ draftLabel(kind) }}</h3>
            <button
              matButton
              (click)="saveDraft(kind)"
              [disabled]="!hasIntent(kind) || busy() || !scope(kind)"
            >
              Save {{ draftLabel(kind) }} draft in tab
            </button>
            @if (candidates()[kind] === 'ready') {
              <button matButton (click)="recoverDraft(kind)" [disabled]="busy() || hasIntent(kind)">
                Recover {{ draftLabel(kind) }} draft
              </button>
            }
            @if (candidates()[kind] === 'stale') {
              <p role="status">
                Saved {{ draftLabel(kind) }} draft belongs to an expired, unsupported or changed
                revision. It cannot be recovered.
              </p>
            }
            @if (candidates()[kind] === 'unavailable') {
              <p role="status">Tab storage is unavailable. Edits remain in memory only.</p>
            }
            @if (
              candidates()[kind] === 'ready' || candidates()[kind] === 'stale' || hasIntent(kind)
            ) {
              <button matButton (click)="discardDraft(kind)" [disabled]="busy()">
                Discard {{ draftLabel(kind) }} draft
              </button>
            }
            @if (stale(kind)) {
              <p role="alert">
                Current evidence changed. Your edits are retained, but submission is blocked.
              </p>
              <button matButton (click)="rebase(kind)" [disabled]="busy() || !scope(kind)">
                Use refreshed revision for {{ draftLabel(kind) }} draft
              </button>
            }
          </section>
        }
      }
      @if (draftMessage()) {
        <p role="status">{{ draftMessage() }}</p>
      }
    </section>
    <button matButton (click)="refresh()" [disabled]="busy()">Refresh confirmation register</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="confirmation register" />
    @if (ws.data(); as w) {
      @if (w.outstandingCritical) {
        <p role="alert">
          {{ w.outstandingCritical }} critical confirmation(s) remain outstanding. Report generation
          remains held until the required evidence and human review clear the server gates.
        </p>
      }
      <label
        >Confirmation filter
        <select
          [value]="filter()"
          (change)="filterFromEvent($event)"
          [disabled]="busy() || uncertain()"
        >
          <option value="ALL">All cases</option>
          <option value="OUTSTANDING">Outstanding cases</option>
          <option value="CRITICAL">Critical cases</option>
        </select></label
      >
      <div class="table-scroll">
        <table>
          <caption>
            Authorized confirmation cases
          </caption>
          <thead>
            <tr>
              <th>Source / respondent</th>
              <th>Booked amount</th>
              <th>State</th>
              <th>Criticality</th>
              <th>Dispatch / follow-up</th>
              <th>Owner</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (c of w.items; track c.id) {
              <tr>
                <td>{{ c.areaCode }} · {{ c.sourceRecordId }}<br />{{ c.respondent }}</td>
                <td>{{ c.bookedAmount | money }} {{ c.currency }}</td>
                <td><audit-status [value]="c.status" /></td>
                <td>{{ c.critical ? 'Critical' : 'Not marked critical' }}</td>
                <td>{{ c.dispatchedAt ?? 'Not dispatched' }}<br />{{ c.monitoring }}</td>
                <td>
                  <code>{{ c.ownerId }}</code>
                </td>
                <td>
                  <button
                    matButton
                    (click)="select(c.id)"
                    [disabled]="busy() || uncertain()"
                    [attr.aria-label]="'Review confirmation for ' + c.respondent"
                  >
                    Review case
                  </button>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="7">No confirmation cases match this authorized filter.</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <nav aria-label="Confirmation pages">
        <button matButton (click)="move(-1)" [disabled]="page() === 0 || busy() || uncertain()">
          Previous confirmation page</button
        >Page {{ page() + 1
        }}<button matButton (click)="move(1)" [disabled]="!w.hasMore || busy() || uncertain()">
          Next confirmation page
        </button>
      </nav>
      @if (w.canPrepare) {
        <details
          class="panel"
          [open]="createExpanded()"
          (toggle)="createExpanded.set($any($event.target).open)"
        >
          <summary>Prepare confirmation</summary>
          <form (submit)="$event.preventDefault(); create()">
            <fieldset [disabled]="busy() || uncertain()">
              <legend>New case identity</legend>
              <label>Audit area<input [formField]="createForm.areaCode" /></label
              ><label>Source record<input [formField]="createForm.sourceRecordId" /></label>
              <label
                >Booked amount<input
                  [formField]="createForm.bookedAmount"
                  inputmode="decimal"
                  aria-describedby="amount-help"
              /></label>
              <p id="amount-help">
                Use an exact decimal amount with up to 14 whole digits and 6 decimal places.
                Unsupported precision is refused. No browser financial calculation determines the
                result.
              </p>
              <label>Currency<input [formField]="createForm.currency" /></label
              ><label
                >Confirmation date<input type="date" [formField]="createForm.confirmationDate"
              /></label>
              <label>Respondent<input [formField]="createForm.respondent" /></label
              ><label
                >Validated contact source<textarea
                  [formField]="createForm.contactValidationSource"
                ></textarea>
              </label>
              <label
                ><input type="checkbox" [formField]="createForm.reviewed" />I reviewed the case
                identity, amount, date and contact source.</label
              >
              <button matButton="filled" [disabled]="!canCreate()">
                Prepare reviewed confirmation
              </button>
            </fieldset>
          </form>
        </details>
      }
      @if (w.canPrepare) {
        <details
          class="panel"
          [open]="batchExpanded()"
          (toggle)="batchExpanded.set($any($event.target).open)"
        >
          <summary>Prepare confirmation batch</summary>
          <form (submit)="$event.preventDefault(); createBatch()">
            <fieldset [disabled]="busy() || uncertain()">
              <legend>Reviewed batch identity</legend>
              <p>
                Every case is prepared together or the entire batch is refused. This prepares local
                drafts and sends no confirmations. Up to 100 cases may share an area, currency and
                confirmation date.
              </p>
              <label>Batch audit area<input [formField]="batchForm.areaCode" /></label>
              <label>Batch currency<input [formField]="batchForm.currency" /></label>
              <label
                >Batch confirmation date<input type="date" [formField]="batchForm.confirmationDate"
              /></label>
              <label
                >Linked applicable procedure ID (optional)<input
                  [formField]="batchForm.procedureId"
              /></label>
              @for (c of batchModel().cases; track $index; let i = $index) {
                <fieldset>
                  <legend>Batch case {{ i + 1 }}</legend>
                  <label
                    >Batch source record {{ i + 1
                    }}<input [formField]="batchForm.cases[i].sourceRecordId"
                  /></label>
                  <label
                    >Batch booked amount {{ i + 1
                    }}<input [formField]="batchForm.cases[i].bookedAmount" inputmode="decimal"
                  /></label>
                  <label
                    >Batch respondent {{ i + 1 }}<input [formField]="batchForm.cases[i].respondent"
                  /></label>
                  <label
                    >Batch validated contact source {{ i + 1
                    }}<textarea [formField]="batchForm.cases[i].contactValidationSource"></textarea>
                  </label>
                  <button
                    matButton
                    type="button"
                    (click)="removeBatchCase(i)"
                    [disabled]="batchModel().cases.length === 1"
                    [attr.aria-label]="'Remove batch case ' + (i + 1)"
                  >
                    Remove case
                  </button>
                </fieldset>
              }
              <button
                matButton
                type="button"
                (click)="addBatchCase()"
                [disabled]="batchModel().cases.length >= 100"
              >
                Add batch case
              </button>
              <p role="status">
                {{ batchModel().cases.length }} case(s) selected. Review each exact amount, identity
                and contact source above. Duplicate trimmed source records and unsupported amounts
                are refused.
              </p>
              <label
                ><input type="checkbox" [formField]="batchForm.reviewed" />I reviewed every batch
                case and the shared area, currency, date and procedure.</label
              >
              <button matButton="filled" [disabled]="!canCreateBatch()">
                Prepare reviewed batch
              </button>
            </fieldset>
          </form>
        </details>
      }
    }
    <audit-state
      [loading]="detail.loading()"
      [error]="detail.error()"
      label="current confirmation evidence"
    />
    @if (detail.data(); as d) {
      <section class="panel" aria-labelledby="case-review">
        <h2 id="case-review">{{ d.case.respondent }} · current case review</h2>
        <p>
          <code>{{ d.case.id }}</code> · {{ d.case.status }} ·
          {{ d.case.critical ? 'Critical' : 'Not marked critical' }}
        </p>
        <p>
          Source {{ d.case.areaCode }} · {{ d.case.sourceRecordId }} · confirmation date
          {{ d.case.confirmationDate }}
        </p>
        <p>
          Exact booked amount <code>{{ d.case.bookedAmount }}</code> {{ d.case.currency }}
        </p>
        <p>Contact validation: {{ d.contactValidationSource }}</p>
        <p>
          Observed dispatch: {{ d.case.dispatchedAt ?? 'Not dispatched' }} · reference
          {{ d.dispatchReference ?? 'None' }}
        </p>
        <h3>Response revisions</h3>
        @for (r of d.responses; track r.id) {
          <article>
            <h4>Revision {{ r.revision }} · {{ r.decision }}</h4>
            <p>
              {{ r.origin }} / {{ r.channel }} · {{ r.reference }} · received {{ r.receivedAt }}
            </p>
            <p>
              Exact confirmed amount <code>{{ r.confirmedAmount ?? 'No amount observed' }}</code> ·
              exact difference <code>{{ r.differenceAmount ?? 'Not applicable' }}</code>
              {{ d.case.currency }}
            </p>
            <p>{{ r.authenticityAssessment }}</p>
            <p>
              Independent reviewer {{ r.reviewerId ?? 'Required' }} ·
              {{ r.reviewedAt ?? 'Not reviewed' }}
            </p>
          </article>
        } @empty {
          <p>No response observations recorded.</p>
        }
        <h3>Alternative procedure revisions</h3>
        @for (a of d.alternatives; track a.id) {
          <article>
            <h4>{{ a.purpose }} · {{ a.status }}</h4>
            <ul>
              @for (ref of a.evidenceReferences; track $index) {
                <li>
                  <code>{{ ref }}</code>
                </li>
              }
            </ul>
            <p>{{ a.conclusion }}</p>
            <p>Reviewer {{ a.reviewerId ?? 'Required' }} · {{ a.reviewedAt ?? 'Not reviewed' }}</p>
          </article>
        } @empty {
          <p>No alternative work recorded.</p>
        }
        @if (d.closure; as closure) {
          <section aria-label="Retained closure decision">
            <h3>Retained closure decision</h3>
            <p>{{ closure.conclusion }}</p>
            <p>
              Closed by <code>{{ closure.closedByUserId }}</code> at {{ closure.closedAt }}
            </p>
            <p>
              Reviewed evidence SHA-256 <code>{{ closure.evidenceSha256 }}</code>
            </p>
          </section>
        } @else if (d.case.status === 'CLOSED') {
          <p role="status">
            Historical closure has no retained reviewer conclusion or evidence snapshot. No closure
            proof is inferred.
          </p>
        }
        <label
          >Case action<select
            aria-label="Case action"
            [value]="action()"
            (change)="actionFromEvent($event)"
            [disabled]="busy() || uncertain()"
          >
            <option value="">Select reviewed action</option>
            @for (a of actions(); track a) {
              <option [value]="a">{{ label(a) }}</option>
            }
          </select></label
        >
        @if (action()) {
          <form (submit)="$event.preventDefault(); act()">
            <fieldset [disabled]="busy() || uncertain()">
              <legend>{{ label(action()) }}</legend>
              @if (action() === 'DISPATCH') {
                <p>
                  This records evidence of actual dispatch. Preparing a document or pressing this
                  button is not delivery evidence.
                </p>
                <label
                  >Observed dispatch reference<input [formField]="actionForm.reference"
                /></label>
              }
              @if (action() === 'RESPONSE') {
                <label>Response origin<input [formField]="actionForm.origin" /></label
                ><label>Channel<input [formField]="actionForm.channel" /></label
                ><label
                  >Receipt / nonresponse observation reference<input
                    [formField]="actionForm.reference" /></label
                ><label
                  >Confirmed amount (leave blank if no amount)<input
                    [formField]="actionForm.confirmedAmount"
                    inputmode="decimal" /></label
                ><label
                  >Authenticity assessment<textarea
                    [formField]="actionForm.authenticityAssessment"
                  ></textarea></label
                ><label
                  >Explicit decision<select
                    aria-label="Explicit decision"
                    [formField]="actionForm.decision"
                  >
                    <option value="AGREED">Agreed</option>
                    <option value="DIFFERENCE">Difference</option>
                    <option value="NO_RESPONSE">No response</option>
                    <option value="ALTERNATIVE_REQUIRED">Alternative required</option>
                  </select></label
                >
                <p>A late response creates a new revision requiring fresh independent review.</p>
              }
              @if (action() === 'ALTERNATIVE') {
                <label>Purpose<textarea [formField]="actionForm.purpose"></textarea></label
                ><label
                  >Evidence references (one per line)<textarea
                    [formField]="actionForm.evidence"
                  ></textarea></label
                ><label
                  >Professional conclusion<textarea [formField]="actionForm.conclusion"></textarea>
                </label>
              }
              @if (action() === 'CLOSE') {
                <label
                  >Reviewer conclusion<textarea [formField]="actionForm.conclusion"></textarea>
                </label>
                <p>
                  Closure requires reviewed substantive response or current independently reviewed
                  alternative work. Nonresponse alone does not clear the gate.
                </p>
              }
              @if (action() === 'CRITICALITY') {
                <label
                  ><input type="checkbox" [formField]="actionForm.critical" />Critical to the
                  report</label
                ><label
                  >Criticality rationale<textarea [formField]="actionForm.rationale"></textarea>
                </label>
              }
              <label
                ><input type="checkbox" [formField]="actionForm.reviewed" />I reviewed this exact
                case and its current evidence revisions.</label
              >
              <button matButton="filled" [disabled]="!canAct()">{{ label(action()) }}</button>
            </fieldset>
          </form>
        }
      </section>
    }
    @if (uncertain()) {
      <p role="status">
        Outcome unknown. No automatic retry occurs. Refresh persisted cases and evidence, then
        select and review again.
      </p>
    }
    <audit-command-message [message]="message()" [failed]="failed()" />
  `,
  styles: `
    label {
      display: block;
      margin-block: 0.8rem;
    }
    input:not([type='checkbox']),
    textarea,
    select {
      display: block;
      max-width: 100%;
      box-sizing: border-box;
    }
    form {
      display: block;
      min-width: 0;
      max-width: 100%;
    }
    textarea {
      width: 100%;
      min-width: 0;
    }
    fieldset {
      border: 0;
      padding: 0;
      margin: 0;
      min-inline-size: 0;
    }
    nav {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
    }
    code {
      overflow-wrap: anywhere;
    }
  `,
})
export class Confirmations {
  private readonly api = inject(Api);
  private readonly drafts = inject(TabDrafts);
  private readonly dialogs = inject(MatDialog);
  private readonly session = inject(SessionService);
  readonly id = routeGuid();
  readonly page = signal(0);
  readonly filter = signal('ALL');
  readonly selected = signal<string | null>(null);
  readonly ws = this.api.resource(
    () => (this.id() ? this.base() + '?page=' + this.page() + '&filter=' + this.filter() : null),
    confirmationPage,
  );
  readonly detail = this.api.resource(
    () => (this.id() && this.selected() ? this.base() + '/' + this.selected() : null),
    confirmationDetail,
  );
  readonly createModel = signal(emptyCreate());
  readonly createForm = form(this.createModel, (p) => {
    required(p.areaCode);
    maxLength(p.areaCode, 40);
    required(p.sourceRecordId);
    maxLength(p.sourceRecordId, 200);
    required(p.bookedAmount);
    required(p.currency);
    maxLength(p.currency, 3);
    required(p.confirmationDate);
    required(p.respondent);
    maxLength(p.respondent, 500);
    required(p.contactValidationSource);
    maxLength(p.contactValidationSource, 2000);
  });
  readonly batchModel = signal(emptyBatch());
  readonly batchForm = form(this.batchModel, (p) => {
    required(p.areaCode);
    maxLength(p.areaCode, 40);
    required(p.currency);
    maxLength(p.currency, 3);
    required(p.confirmationDate);
    maxLength(p.procedureId, 36);
    applyEach(p.cases, (c) => {
      required(c.sourceRecordId);
      maxLength(c.sourceRecordId, 200);
      required(c.bookedAmount);
      required(c.respondent);
      maxLength(c.respondent, 500);
      required(c.contactValidationSource);
      maxLength(c.contactValidationSource, 2000);
    });
  });
  readonly createExpanded = signal(false);
  readonly batchExpanded = signal(false);
  readonly draftKinds: DraftKind[] = ['create', 'batch', 'action'];
  readonly candidates = signal<Record<DraftKind, DraftRead<unknown>['state']>>({
    create: 'absent',
    batch: 'absent',
    action: 'absent',
  });
  readonly draftMessage = signal('');
  private readonly bases: Record<DraftKind, string> = { create: '', batch: '', action: '' };
  private readonly saved: Record<DraftKind, string> = { create: '', batch: '', action: '' };
  private readonly signatures: Record<DraftKind, string> = { create: '', batch: '', action: '' };
  private currentCase = '';
  private leaveDialog: MatDialogRef<UnsavedChangesDialog> | null = null;

  readonly actionModel = signal(emptyAction());
  readonly actionForm = form(this.actionModel, (p) => {
    maxLength(p.reference, 500);
    maxLength(p.origin, 40);
    maxLength(p.channel, 40);
    maxLength(p.authenticityAssessment, 4000);
    maxLength(p.purpose, 2000);
    maxLength(p.evidence, 20000);
    maxLength(p.conclusion, 4000);
    maxLength(p.rationale, 4000);
  });
  readonly action = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  private refreshing = false;
  constructor() {
    effect(() => {
      this.id();
      this.session.invalidation();
      untracked(() => {
        this.leaveDialog?.close('keep');
        this.selected.set(null);
        this.reset('create');
        this.reset('batch');
        this.reset('action');
        this.action.set('');
        this.currentCase = '';
        this.createExpanded.set(false);
        this.batchExpanded.set(false);
        this.message.set('');
        this.draftMessage.set('');
        this.uncertain.set(false);
        this.candidates.set({ create: 'absent', batch: 'absent', action: 'absent' });
      });
    });
    effect(() => {
      const d = this.detail.data();
      if (d)
        untracked(() => {
          if (d.case.id !== this.currentCase) {
            this.currentCase = d.case.id;
            this.reset('action');
            this.action.set('');
          }
          this.actionModel.update((m) => ({ ...m, reviewed: false }));
          if (this.stale('action')) this.saved.action = '';
          this.checkCandidate('action');
        });
    });
    effect(() => {
      const w = this.ws.data();
      if (w)
        untracked(() => {
          this.createModel.update((m) => ({ ...m, reviewed: false }));
          this.batchModel.update((m) => ({ ...m, reviewed: false }));
          if (this.stale('create')) this.saved.create = '';
          if (this.stale('batch')) this.saved.batch = '';
          this.checkCandidate('create');
          this.checkCandidate('batch');
          if (this.refreshing) {
            this.refreshing = false;
            this.uncertain.set(false);
            this.message.set('Persisted register refreshed. Select the case and review again.');
          }
        });
    });
    for (const kind of this.draftKinds)
      effect(() => {
        const model =
          kind === 'create'
            ? this.createModel()
            : kind === 'batch'
              ? this.batchModel()
              : this.actionModel();
        const signature = JSON.stringify(intent(model));
        const base = this.scope(kind)?.baseRevision;
        if (signature !== this.signatures[kind])
          untracked(() => {
            this.signatures[kind] = signature;
            if (this.hasIntent(kind) && !this.bases[kind] && base) this.bases[kind] = base;
            this.unreview(kind);
          });
      });
    inject(DestroyRef).onDestroy(() => this.leaveDialog?.close('keep'));
  }
  draftLabel(kind: DraftKind) {
    return kind === 'create' ? 'case' : kind === 'batch' ? 'batch' : 'action';
  }
  scope(kind: DraftKind): DraftScope | null {
    const baseRevision =
      kind === 'action' ? this.detail.data()?.reviewToken : this.ws.data()?.createReviewToken;
    const entity =
      'confirmations/' +
      this.id() +
      '/' +
      (kind === 'action' ? this.selected() + '/' + this.action() : kind);
    return this.id() && baseRevision && (kind !== 'action' || (this.selected() && this.action()))
      ? { entity, baseRevision }
      : null;
  }
  private value(kind: DraftKind) {
    return intent(
      kind === 'create'
        ? this.createModel()
        : kind === 'batch'
          ? this.batchModel()
          : this.actionModel(),
    );
  }
  private validate(kind: DraftKind, raw: unknown): unknown | null {
    return kind === 'create'
      ? createDraft(raw)
      : kind === 'batch'
        ? batchDraft(raw)
        : actionDraft(raw);
  }
  hasIntent(kind: DraftKind) {
    const empty =
      kind === 'create'
        ? emptyCreate()
        : kind === 'batch'
          ? emptyBatch()
          : { ...emptyAction(), critical: this.detail.data()?.case.critical ?? false };
    return JSON.stringify(this.value(kind)) !== JSON.stringify(intent(empty));
  }
  private dirty(kind: DraftKind) {
    return this.hasIntent(kind) && JSON.stringify(this.value(kind)) !== this.saved[kind];
  }
  stale(kind: DraftKind) {
    const base = this.scope(kind)?.baseRevision;
    return !!base && !!this.bases[kind] && base !== this.bases[kind] && this.hasIntent(kind);
  }
  private unreview(kind: DraftKind) {
    if (kind === 'create') this.createModel.update((m) => ({ ...m, reviewed: false }));
    else if (kind === 'batch') this.batchModel.update((m) => ({ ...m, reviewed: false }));
    else this.actionModel.update((m) => ({ ...m, reviewed: false }));
  }
  private reset(kind: DraftKind) {
    if (kind === 'create') this.createModel.set(emptyCreate());
    else if (kind === 'batch') this.batchModel.set(emptyBatch());
    else
      this.actionModel.set({
        ...emptyAction(),
        critical: this.detail.data()?.case.critical ?? false,
      });
    this.bases[kind] = '';
    this.saved[kind] = '';
    this.signatures[kind] = '';
  }
  private checkCandidate(kind: DraftKind) {
    const scope = this.scope(kind);
    if (scope)
      this.candidates.update((c) => ({
        ...c,
        [kind]: this.drafts.read(scope, (raw) => this.validate(kind, raw)).state,
      }));
  }
  saveDraft(kind: DraftKind, pending = this.uncertain()): boolean {
    const scope = this.scope(kind);
    if (!scope || (!this.hasIntent(kind) && !pending)) return false;
    if (this.stale(kind)) {
      this.draftMessage.set(
        'Evidence changed. Keep this page open, inspect the refreshed revision and explicitly use it before saving your edits.',
      );
      return false;
    }
    const stored = this.drafts.save(
      { ...scope, baseRevision: this.bases[kind] || scope.baseRevision },
      this.value(kind),
      (raw) => this.validate(kind, raw),
      pending,
    );
    this.draftMessage.set(
      stored
        ? 'Draft saved in this tab only. It expires in four hours; recovery requires fresh review.'
        : 'Draft could not be saved. Your edits remain in memory only; keep this page open.',
    );
    if (stored) {
      this.saved[kind] = JSON.stringify(this.value(kind));
      this.checkCandidate(kind);
    }
    return stored;
  }
  recoverDraft(kind: DraftKind) {
    const scope = this.scope(kind);
    if (!scope || this.busy() || this.hasIntent(kind)) return;
    const read = this.drafts.read(scope, (raw) => this.validate(kind, raw));
    this.candidates.update((c) => ({ ...c, [kind]: read.state }));
    if (read.state !== 'ready') return;
    if (kind === 'create') {
      this.createExpanded.set(true);
      const v = createDraft(read.draft.value);
      if (v) this.createModel.set({ ...v, reviewed: false });
    } else if (kind === 'batch') {
      this.batchExpanded.set(true);
      const v = batchDraft(read.draft.value);
      if (v) this.batchModel.set({ ...v, reviewed: false });
    } else {
      const v = actionDraft(read.draft.value);
      if (v) this.actionModel.set({ ...v, reviewed: false });
    }
    this.bases[kind] = scope.baseRevision;
    this.saved[kind] = JSON.stringify(this.value(kind));
    this.uncertain.set(read.draft.submissionPending);
    this.draftMessage.set(
      read.draft.submissionPending
        ? 'Submission may have started. Refresh persisted evidence before another action; no retry occurs.'
        : 'Draft recovered explicitly. Review the current evidence and your fields again before submitting.',
    );
  }
  discardDraft(kind: DraftKind) {
    if (this.busy()) return;
    const scope = this.scope(kind);
    const removed = scope ? this.drafts.clear(scope.entity) : false;
    this.reset(kind);
    this.checkCandidate(kind);
    this.draftMessage.set(
      removed
        ? 'Tab draft discarded.'
        : 'Edits discarded from memory. Tab storage could not be cleared.',
    );
  }
  rebase(kind: DraftKind) {
    const scope = this.scope(kind);
    if (!scope || this.busy()) return;
    this.bases[kind] = scope.baseRevision;
    this.saved[kind] = '';
    this.unreview(kind);
    this.draftMessage.set(
      'Edits retained against the refreshed revision. Independently inspect current evidence and review again.',
    );
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent) {
    if (this.busy() || this.draftKinds.some((k) => this.dirty(k))) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  confirmNavigation(): boolean | Promise<boolean> {
    if (!this.session.current()) return true;
    if (this.busy()) return false;
    if (!this.draftKinds.some((k) => this.dirty(k))) return true;
    return this.resolveNavigation();
  }
  private async resolveNavigation(): Promise<boolean> {
    if (this.leaveDialog) return false;
    const epoch = this.session.invalidation(),
      engagement = this.id();
    const dialog = this.dialogs.open(UnsavedChangesDialog, {
      width: '540px',
      maxWidth: '95vw',
      disableClose: true,
    });
    this.leaveDialog = dialog;
    const result = await firstValueFrom(dialog.afterClosed());
    this.leaveDialog = null;
    if (epoch !== this.session.invalidation() || engagement !== this.id()) return false;
    if (result === 'save')
      return this.draftKinds
        .filter((k) => this.dirty(k))
        .map((k) => this.saveDraft(k))
        .every(Boolean);
    if (result === 'discard') {
      for (const k of this.draftKinds) if (this.dirty(k)) this.discardDraft(k);
      return true;
    }
    return false;
  }
  private protect(change: () => void) {
    const decision = this.confirmNavigation();
    if (typeof decision === 'boolean') {
      if (decision) change();
    } else
      void decision.then((ok) => {
        if (ok) change();
      });
  }
  addBatchCase() {
    if (!this.busy() && !this.uncertain() && this.batchModel().cases.length < 100)
      this.batchModel.update((m) => ({
        ...m,
        reviewed: false,
        cases: [...m.cases, emptyBatchCase()],
      }));
  }
  removeBatchCase(index: number) {
    if (!this.busy() && !this.uncertain() && this.batchModel().cases.length > 1)
      this.batchModel.update((m) => ({
        ...m,
        reviewed: false,
        cases: m.cases.filter((_, i) => i !== index),
      }));
  }
  canCreateBatch() {
    const m = this.batchModel(),
      sources = m.cases.map((c) => c.sourceRecordId.trim());
    return (
      !!this.ws.data()?.canPrepare &&
      !this.batchForm().invalid() &&
      m.reviewed &&
      !this.stale('batch') &&
      !this.busy() &&
      !this.uncertain() &&
      m.cases.length >= 1 &&
      m.cases.length <= 100 &&
      new Set(sources).size === sources.length &&
      /^[A-Z]{3}$/.test(m.currency) &&
      /^\d{4}-\d{2}-\d{2}$/.test(m.confirmationDate) &&
      (!m.procedureId ||
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(m.procedureId)) &&
      m.cases.every(
        (c) =>
          !!confirmationAmount(c.bookedAmount) &&
          !!c.sourceRecordId.trim() &&
          !!c.respondent.trim() &&
          !!c.contactValidationSource.trim(),
      )
    );
  }
  async createBatch() {
    if (!this.canCreateBatch()) return;
    const w = this.ws.data()!,
      m = this.batchModel();
    await this.send(
      'batch',
      this.base() + '/batch',
      {
        ...m,
        procedureId: m.procedureId || null,
        reviewToken: w.createReviewToken,
        cases: m.cases.map((c) => ({ ...c, bookedAmount: confirmationAmount(c.bookedAmount) })),
      },
      () => this.reset('batch'),
    );
  }

  private base() {
    return '/api/ui/engagements/' + this.id() + '/confirmations';
  }
  refresh() {
    if (this.busy()) return;
    this.protect(() => {
      this.refreshing = true;
      this.ws.reload();
      this.detail.reload();
    });
  }
  select(id: string) {
    if (!this.busy() && !this.uncertain() && id !== this.selected())
      this.protect(() => {
        this.reset('action');
        this.action.set('');
        this.selected.set(id);
      });
  }
  filterChanged(value: string) {
    if (!['ALL', 'OUTSTANDING', 'CRITICAL'].includes(value) || this.busy() || this.uncertain())
      return;
    this.protect(() => {
      this.reset('action');
      this.action.set('');
      this.selected.set(null);
      this.page.set(0);
      this.filter.set(value);
    });
  }
  move(change: number) {
    if (this.busy() || this.uncertain()) return;
    this.protect(() => {
      this.reset('action');
      this.action.set('');
      this.selected.set(null);
      this.page.update((p) => Math.max(0, p + change));
    });
  }
  filterFromEvent(event: Event) {
    const target = event.target as HTMLSelectElement,
      value = target.value;
    target.value = this.filter();
    this.filterChanged(value);
  }
  actionFromEvent(event: Event) {
    const target = event.target as HTMLSelectElement,
      value = target.value;
    target.value = this.action();
    this.chooseAction(value);
  }
  chooseAction(value: string) {
    if (value === this.action()) return;
    this.protect(() => {
      this.reset('action');
      this.action.set(this.actions().includes(value) ? value : '');
      this.checkCandidate('action');
    });
  }
  label(a: string) {
    return (
      (
        {
          APPROVE: 'Approve preparation',
          DISPATCH: 'Record observed dispatch',
          RESPONSE: 'Record response observation',
          REVIEW_RESPONSE: 'Review current response',
          ALTERNATIVE: 'Record alternative work',
          REVIEW_ALTERNATIVE: 'Review current alternative',
          CLOSE: 'Close reviewed case',
          CRITICALITY: 'Record criticality',
        } as Record<string, string>
      )[a] ?? 'Select action'
    );
  }
  actions(): string[] {
    const d = this.detail.data(),
      w = this.ws.data();
    if (!d || !w) return [];
    const result: string[] = [];
    if (w.canPrepare) {
      if (d.case.status === 'APPROVED') result.push('DISPATCH');
      if (d.case.dispatchedAt && d.case.status !== 'CLOSED') result.push('RESPONSE');
      if (['NO_RESPONSE', 'ALTERNATIVE_REQUIRED'].includes(d.case.status))
        result.push('ALTERNATIVE');
    }
    if (w.canReview) {
      if (d.case.status === 'DRAFT' && !d.preparedByMe) result.push('APPROVE');
      const r = d.responses[0],
        a = d.alternatives[0];
      if (r && !r.preparedByMe && !r.reviewerId) result.push('REVIEW_RESPONSE');
      if (a && !a.preparedByMe && a.status !== 'REVIEWED') result.push('REVIEW_ALTERNATIVE');
      if (!['DRAFT', 'CLOSED'].includes(d.case.status)) result.push('CLOSE');
    }
    if (w.canSetCriticality) result.push('CRITICALITY');
    return result;
  }
  canCreate() {
    const m = this.createModel();
    return (
      !!this.ws.data()?.canPrepare &&
      !this.createForm().invalid() &&
      m.reviewed &&
      !this.stale('create') &&
      !!confirmationAmount(m.bookedAmount) &&
      /^\d{4}-\d{2}-\d{2}$/.test(m.confirmationDate) &&
      !this.busy() &&
      !this.uncertain()
    );
  }
  canAct() {
    const m = this.actionModel(),
      a = this.action();
    if (
      this.stale('action') ||
      this.actionForm().invalid() ||
      !m.reviewed ||
      !this.actions().includes(a) ||
      this.busy() ||
      this.uncertain()
    )
      return false;
    if (a === 'DISPATCH') return !!m.reference.trim();
    if (a === 'RESPONSE')
      return (
        !!m.origin.trim() &&
        !!m.channel.trim() &&
        !!m.reference.trim() &&
        !!m.authenticityAssessment.trim() &&
        (!m.confirmedAmount.trim() || !!confirmationAmount(m.confirmedAmount))
      );
    if (a === 'ALTERNATIVE')
      return !!m.purpose.trim() && !!m.evidence.trim() && !!m.conclusion.trim();
    if (a === 'CLOSE') return !!m.conclusion.trim();
    if (a === 'CRITICALITY') return !!m.rationale.trim();
    return true;
  }
  async create() {
    if (!this.canCreate()) return;
    const w = this.ws.data()!,
      m = this.createModel();
    await this.send(
      'create',
      this.base(),
      { ...m, bookedAmount: confirmationAmount(m.bookedAmount), reviewToken: w.createReviewToken },
      () => this.reset('create'),
    );
  }
  async act() {
    if (!this.canAct()) return;
    const d = this.detail.data()!,
      m = this.actionModel(),
      a = this.action();
    const body = {
      ...m,
      action: a,
      reviewToken: d.reviewToken,
      evidenceId:
        a === 'REVIEW_RESPONSE'
          ? d.responses[0]?.id
          : a === 'REVIEW_ALTERNATIVE'
            ? d.alternatives[0]?.id
            : null,
      confirmedAmount: m.confirmedAmount.trim() ? confirmationAmount(m.confirmedAmount) : null,
      evidenceReferences: m.evidence
        .split('\n')
        .map((s) => s.trim())
        .filter(Boolean),
    };
    await this.send('action', this.base() + '/' + d.case.id + '/actions', body, () => {
      this.reset('action');
      this.action.set('');
      this.detail.reload();
    });
  }
  private async send(kind: DraftKind, url: string, body: unknown, after: () => void) {
    const epoch = this.session.invalidation(),
      engagement = this.id();
    this.saveDraft(kind, true);
    this.busy.set(true);
    try {
      const r = await this.api.command(url, body);
      if (epoch !== this.session.invalidation() || engagement !== this.id()) return;
      this.failed.set(!r.ok);
      this.message.set(
        r.ok ? 'Local evidence recorded. Review current persisted state.' : r.message,
      );
      this.createModel.update((m) => ({ ...m, reviewed: false }));
      this.batchModel.update((m) => ({ ...m, reviewed: false }));
      this.actionModel.update((m) => ({ ...m, reviewed: false }));
      if (r.ok) {
        const scope = this.scope(kind);
        if (scope) this.drafts.clear(scope.entity);
        after();
        this.ws.reload();
      } else if (r.unknown) this.uncertain.set(true);
      else {
        this.saveDraft(kind, false);
        this.selected.set(null);
        this.reset('action');
        this.action.set('');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
