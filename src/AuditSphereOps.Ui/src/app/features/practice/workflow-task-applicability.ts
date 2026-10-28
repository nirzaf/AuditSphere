import { Component, computed, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, instant, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { WorkflowEvidenceSourcePicker } from './workflow-evidence-source-picker';

const revision = (value: unknown, path: string): string => {
  if (typeof value !== 'string' || !/^[1-9][0-9]{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error(`${path}: expected an exact positive Int64 string`);
  return value;
};
const decisionKind = oneOf('APPROVE', 'RETURN', 'REVOKE');
const decision = obj({ id: guid, version: revision, kind: decisionKind, actorUserId: guid, actorName: str(300),
  reason: str(1000), occurredAt: instant });
const proposal = obj({ id: guid, version: revision, actorUserId: guid, actorName: str(300), reason: str(1000),
  occurredAt: instant, sourceReceiptId: guid, basisHash: sha256, basisJson: str(200000), isLatest: bool,
  basisCurrent: bool, currentBasisJson: nullable(str(200000)), blocker: nullable(str(1000)), decision: nullable(decision) });
const preview = obj({ sourceRequestId: guid, sourceReceiptId: nullable(guid), basisHash: nullable(sha256),
  basisJson: nullable(str(200000)), blocker: nullable(str(1000)) });
const workspaceShape = obj({ engagementId: guid, taskId: guid, taskRevision: revision, latestProposalId: nullable(guid),
  proposal: nullable(proposal), preview: nullable(preview), canPropose: bool, canApprove: bool, canReturn: bool, canRevoke: bool });
export const decodeTaskApplicability = (value: unknown, path: string) => {
  const w = workspaceShape(value, path); const p = w.proposal; const v = w.preview;
  if (w.canPropose && (!v?.sourceReceiptId || !v.basisHash || !v.basisJson || v.blocker) ||
    w.canApprove && (!p?.isLatest || !p.basisCurrent || p.decision !== null) ||
    w.canReturn && (!p || p.decision !== null) || w.canRevoke && p?.decision?.kind !== 'APPROVE' ||
    p?.isLatest && p.id !== w.latestProposalId || p?.basisCurrent && p.currentBasisJson !== p.basisJson)
    throw new Error('Inconsistent applicability review state.');
  return w;
};
export const decodeApplicabilityReceipt = obj({ engagementId: guid, taskId: guid, idempotencyKey: guid,
  proposalId: guid, decisionId: nullable(guid), version: revision });
const cursor = obj({ occurredAt: instant, recordType: oneOf('PROPOSAL', 'DECISION'), id: guid });
const historyItem = obj({ id: guid, recordType: oneOf('PROPOSAL', 'DECISION'), kind: oneOf('PROPOSE', 'APPROVE', 'RETURN', 'REVOKE'),
  proposalId: guid, version: revision, actorUserId: guid, actorName: str(300), reason: str(1000), basisHash: sha256, occurredAt: instant });
const historyShape = obj({ engagementId: guid, taskId: guid, items: arr(historyItem, 25), hasMore: bool, nextCursor: nullable(cursor) });
export const decodeApplicabilityHistory = (value: unknown, path: string) => {
  const h = historyShape(value, path); const last = h.items.at(-1);
  if (h.hasMore !== (h.nextCursor !== null) || h.hasMore && (h.items.length !== 25 ||
    h.nextCursor!.id !== last?.id || h.nextCursor!.recordType !== last.recordType || h.nextCursor!.occurredAt !== last.occurredAt) ||
    new Set(h.items.map(x => `${x.recordType}:${x.id}`)).size !== h.items.length ||
    h.items.some(x => (x.recordType === 'PROPOSAL') !== (x.kind === 'PROPOSE')))
    throw new Error('Inconsistent retained history page.');
  return h;
};
type Workspace = ReturnType<typeof decodeTaskApplicability>;
type Action = 'PROPOSE' | ReturnType<typeof decisionKind>;
type Review = { action: Action; workspace: Workspace; reason: string; body: Record<string, unknown>; key: string; proposalId: string | null };

@Component({
  selector: 'audit-workflow-task-applicability',
  imports: [FormsModule, MatButtonModule, WorkflowEvidenceSourcePicker, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="task-applicability-heading">
      <h2 id="task-applicability-heading">Conditional task applicability</h2>
      <p>{{ title() }}. An independent reviewer must decide an exact accepted-source proposal. A cancellation reason is not an approved decision.</p>
      <p>This records task applicability only. It never completes a task, clears a hold, approves a professional procedure or satisfies a task-completed prerequisite.</p>
      <button matButton (click)="refresh()" [disabled]="busy()">Refresh applicability</button>
      <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="conditional task applicability" />
      @if (workspace.data(); as w) {
        <p>Task revision {{ w.taskRevision }}</p>
        @if (w.proposal; as p) {
          <h3>{{ p.isLatest ? 'Latest proposal' : 'Historical proposal' }} · version {{ p.version }}</h3>
          <p>{{ p.actorName }} · {{ p.occurredAt }} · {{ p.reason }}</p>
          <p>Proposal {{ p.id }} · Accepted-source receipt {{ p.sourceReceiptId }} · Basis SHA-256 {{ p.basisHash }}</p>
          <p>{{ p.basisCurrent ? 'Retained basis matches the current task and source.' : 'Retained basis is stale. It cannot exclude this task from current progress.' }}</p>
          @if (p.blocker) { <p role="status">{{ p.blocker }}</p> }
          <details><summary>Retained exact basis</summary><pre>{{ p.basisJson }}</pre></details>
          @if (!p.basisCurrent && p.currentBasisJson) { <details><summary>Current basis for comparison</summary><pre>{{ p.currentBasisJson }}</pre></details> }
          @if (p.decision; as d) { <p>Retained {{ d.kind }} · version {{ d.version }} · {{ d.actorName }} · {{ d.occurredAt }} · {{ d.reason }}</p> }
          @else { <p>Awaiting an independent decision.</p> }
          <p>A retained approval is effective only while its exact basis remains current and all separate professional applicability requirements and holds permit it.</p>
          @if (!p.isLatest) { <button matButton (click)="showProposal(null)" [disabled]="busy()">Show latest proposal</button> }
        } @else { <p>No conditional applicability proposal is recorded.</p> }
        @if (!pending() && !review()) {
          <audit-workflow-evidence-source-picker [engagementId]="engagementId()" kind="PBC_ACCEPTED_OR_CLOSED"
            [stepTitle]="title()" requirementKey="task-applicability" pickerId="task-applicability-source"
            [selectedId]="sourceRequestId()" [disabled]="busy() || storageUnavailable()"
            (sourceSelected)="selectSource($event)" />
          @if (w.preview; as v) {
            @if (v.blocker) { <p role="status">{{ v.blocker }}</p> }
            @if (v.basisJson) { <details open><summary>Exact source preview</summary><p>Request {{ v.sourceRequestId }} · Receipt {{ v.sourceReceiptId }} · SHA-256 {{ v.basisHash }}</p><pre>{{ v.basisJson }}</pre></details> }
          }
          @if (w.canPropose || w.canApprove || w.canReturn || w.canRevoke) {
            <label>Reason for this exact action<textarea [(ngModel)]="reason" maxlength="1000" rows="3"></textarea></label>
            <div class="actions">
              @if (w.canPropose) { <button matButton (click)="prepare('PROPOSE')" [disabled]="busy() || storageUnavailable()">Review new proposal</button> }
              @if (w.canApprove) { <button matButton (click)="prepare('APPROVE')" [disabled]="busy() || storageUnavailable()">Review independent approval</button> }
              @if (w.canReturn) { <button matButton (click)="prepare('RETURN')" [disabled]="busy() || storageUnavailable()">Review return</button> }
              @if (w.canRevoke) { <button matButton (click)="prepare('REVOKE')" [disabled]="busy() || storageUnavailable()">Review revocation</button> }
            </div>
          } @else { <p>No decision is currently available to you for this selection. Select a current accepted source to prepare a conditional proposal.</p> }
        }
        @if (review(); as r) {
          <section aria-label="Review exact applicability action">
            <h3>Review {{ r.action }}</h3><p>{{ r.reason }}</p>
            <p>Exact task revision {{ r.workspace.taskRevision }} · Proposal head {{ r.workspace.latestProposalId || 'None' }}</p>
            @if (r.action === 'PROPOSE') { <pre>{{ r.workspace.preview?.basisJson }}</pre> }
            @else { <p>Proposal {{ r.proposalId }} · Basis SHA-256 {{ r.workspace.proposal?.basisHash }}</p><pre>{{ r.workspace.proposal?.basisJson }}</pre> }
            <label><input type="checkbox" [(ngModel)]="confirmed" /> I reviewed this exact action, source basis and reason.</label>
            <button matButton="filled" (click)="submit()" [disabled]="!confirmed || busy() || !!pending()">Save reviewed applicability action</button>
            <button matButton (click)="clearReview()" [disabled]="busy() || !!pending()">Back to editing</button>
          </section>
        }
      }
      @if (storageUnavailable()) { <p role="alert">Recovery storage is unavailable. No new action will be submitted. Restore storage access and reopen this task.</p> }
      @if (pending()) { <p role="status">An action has an unconfirmed receipt. Do not submit another action. A recovered receipt is historical evidence; refresh checks current applicability.</p>
        <button matButton (click)="recover()" [disabled]="busy()">Read saved applicability receipt</button> }
      <audit-command-message [message]="message()" [failed]="failed()" />
      <button matButton (click)="openHistory()" [disabled]="busy()">Read retained applicability history</button>
      @if (historyOpen()) {
        <audit-state [loading]="history.loading()" [error]="history.error()" label="retained applicability history" />
        @if (history.data(); as h) {
          <ul>@for (item of h.items; track item.recordType + ':' + item.id) {
            <li>{{ item.kind }} · version {{ item.version }} · {{ item.actorName }} · {{ item.occurredAt }}<p>{{ item.reason }}</p>
              <button matButton (click)="showProposal(item.proposalId)" [disabled]="busy() || !!pending()">Inspect retained proposal</button></li>
          } @empty { <li>No retained applicability events.</li> }</ul>
          <button matButton (click)="firstHistory()" [disabled]="busy()">Refresh first history page</button>
          @if (h.hasMore) { <button matButton (click)="nextHistory()" [disabled]="busy()">Older applicability events</button> }
        }
      }
    </section>
  `,
  styles: `:host { display: block; } .panel { margin-block: 1rem; padding: 1rem; border: 1px solid var(--line); border-radius: .5rem; overflow-wrap: anywhere; }
    label { display: grid; gap: .5rem; margin-block: .75rem; } textarea { width: min(100%, 42rem); min-width: 0; box-sizing: border-box; }
    pre { white-space: pre-wrap; overflow-wrap: anywhere; font-size: .8rem; } .actions { display: flex; flex-wrap: wrap; gap: .5rem; }
    li { padding-block: .5rem; } button { max-width: 100%; height: auto; min-height: 44px; white-space: normal; }`,
})
export class WorkflowTaskApplicabilityPanel {
  readonly engagementId = input.required<string>();
  readonly taskId = input.required<string>();
  readonly title = input.required<string>();
  readonly changed = output<void>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly sourceSelection = signal<{ context: string; id: string } | null>(null);
  private readonly proposalSelection = signal<{ context: string; id: string } | null>(null);
  readonly sourceRequestId = computed(() => this.sourceSelection()?.context === this.storageKey() ? this.sourceSelection()!.id : null);
  readonly selectedProposalId = computed(() => this.proposalSelection()?.context === this.storageKey() ? this.proposalSelection()!.id : null);
  readonly historyOpen = signal(false);
  readonly historyCursor = signal<ReturnType<typeof cursor> | null>(null);
  readonly workspace = this.api.resource(() => this.workspaceUrl(), (value, path) => {
    const w = decodeTaskApplicability(value, path); this.requireScope(w);
    if (w.preview && w.preview.sourceRequestId !== this.sourceRequestId() ||
      this.selectedProposalId() && w.proposal?.id !== this.selectedProposalId() ||
      w.proposal?.actorUserId === this.session.current()?.userId && (w.canApprove || w.canReturn || w.canRevoke))
      throw new Error('The reviewed selection or independent actor changed.');
    return w;
  }, 'Conditional task applicability is unavailable in this task scope.');
  readonly history = this.api.resource(() => this.historyUrl(), (value, path) => {
    const h = decodeApplicabilityHistory(value, path); this.requireScope(h); return h;
  }, 'Applicability history is unavailable in this task scope.');
  readonly review = signal<Review | null>(null);
  readonly pending = signal<string | null>(null);
  readonly busy = signal(false);
  readonly storageUnavailable = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');
  reason = '';
  confirmed = false;
  private destroyed = false;
  private historyContext = '';

  constructor() {
    effect(() => {
      this.engagementId(); this.taskId(); this.session.current(); this.session.invalidation();
      untracked(() => {
        this.sourceSelection.set(null); this.proposalSelection.set(null); this.historyOpen.set(false); this.historyCursor.set(null);
        this.clearReview(); this.reason = ''; this.message.set(''); this.failed.set(false); this.storageUnavailable.set(false);
        try { const saved = sessionStorage.getItem(this.storageKey()); this.pending.set(saved ? guid(saved, 'saved request key') : null); }
        catch { this.pending.set(null); this.storageUnavailable.set(true); }
      });
    });
    effect(() => { this.workspace.data(); untracked(() => this.clearReview()); });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
  }

  clearReview(): void { this.review.set(null); this.confirmed = false; }
  refresh(): void { this.clearReview(); this.workspace.reload(); if (this.historyOpen()) this.firstHistory(); }
  selectSource(id: string): void {
    if (this.busy() || this.pending()) return;
    this.clearReview(); this.sourceSelection.set({ context: this.storageKey(), id: guid(id, 'source request') }); this.proposalSelection.set(null);
  }
  showProposal(id: string | null): void {
    if (this.busy() || this.pending()) return;
    this.clearReview(); this.sourceSelection.set(null); this.proposalSelection.set(id ? { context: this.storageKey(), id } : null);
  }
  openHistory(): void { this.historyContext = this.storageKey(); this.historyOpen.set(true); }
  firstHistory(): void {
    if (this.historyCursor()) this.historyCursor.set(null);
    else this.history.reload();
  }
  nextHistory(): void { const h = this.history.data(); if (h?.hasMore) this.historyCursor.set(h.nextCursor); }

  prepare(action: Action): void {
    const w = this.workspace.data();
    if (!w || this.busy() || this.pending() || this.storageUnavailable() || !this.allowed(action, w)) return;
    const reason = this.reason.trim();
    if (!reason || reason.length > 1000) { this.failed.set(true); this.message.set('Enter a reason of 1 to 1,000 characters.'); return; }
    const key = crypto.randomUUID(); const p = w.proposal;
    const body = action === 'PROPOSE'
      ? { sourceReceiptId: w.preview!.sourceReceiptId, expectedProposalId: w.latestProposalId, basisHash: w.preview!.basisHash }
      : { proposalId: p!.id, expectedDecisionId: p!.decision?.id ?? null, kind: action, basisHash: p!.basisHash };
    this.review.set({ action, workspace: w, reason, key, proposalId: action === 'PROPOSE' ? null : p!.id,
      body: { ...body, expectedTaskRevision: w.taskRevision, reason, idempotencyKey: key, reviewed: true } });
    this.confirmed = false; this.failed.set(false); this.message.set('');
  }

  async submit(): Promise<void> {
    const r = this.review(); const w = this.workspace.data();
    if (!r || !w || w !== r.workspace || !this.confirmed || !this.allowed(r.action, w) ||
      this.busy() || this.pending() || this.storageUnavailable()) return;
    const context = this.storageKey(); const generation = this.session.invalidation(); const url = this.baseUrl();
    try { sessionStorage.setItem(context, r.key); }
    catch { this.storageUnavailable.set(true); return; }
    this.pending.set(r.key); this.busy.set(true);
    try {
      const result = await this.api.command<unknown>(`${url}/${r.action === 'PROPOSE' ? 'proposals' : 'decisions'}`, r.body);
      if (!this.active(context, generation)) return;
      if (result.ok) this.accept(result.value, r.key, context, r);
      else {
        this.failed.set(true); this.message.set(result.message);
        if (!result.unknown) { sessionStorage.removeItem(context); this.pending.set(null); this.clearReview(); this.workspace.reload(); }
      }
    } catch {
      if (this.active(context, generation)) { this.failed.set(true); this.message.set('The action receipt is unconfirmed. Read the saved receipt before any new action.'); }
    } finally { this.busy.set(false); }
  }

  async recover(): Promise<void> {
    const key = this.pending(); if (!key || this.busy()) return;
    const context = this.storageKey(); const generation = this.session.invalidation(); const url = this.baseUrl();
    this.busy.set(true);
    try {
      const value = await this.api.get(`${url}/receipts/${encodeURIComponent(key)}`, decodeApplicabilityReceipt);
      if (this.active(context, generation)) this.accept(value, key, context);
    } catch {
      if (this.active(context, generation)) { this.failed.set(true); this.message.set('No matching saved receipt was confirmed. Keep the recovery key; no action was resent.'); }
    } finally { this.busy.set(false); }
  }

  private accept(value: unknown, key: string, context: string, review?: Review): void {
    const r = decodeApplicabilityReceipt(value, 'receipt'); this.requireScope(r);
    if (r.idempotencyKey !== key || review && (review.proposalId && r.proposalId !== review.proposalId ||
      (review.action === 'PROPOSE') !== (r.decisionId === null))) throw new Error('Unmatched applicability receipt.');
    sessionStorage.removeItem(context); this.pending.set(null); this.clearReview(); this.reason = '';
    this.failed.set(false); this.message.set('Applicability receipt confirmed. Current task progress and separate professional decisions are re-evaluated independently.');
    this.workspace.reload(); if (this.historyOpen()) this.firstHistory(); this.changed.emit();
  }
  private allowed(action: Action, w: Workspace): boolean {
    return action === 'PROPOSE' ? w.canPropose : action === 'APPROVE' ? w.canApprove : action === 'RETURN' ? w.canReturn : w.canRevoke;
  }
  private requireScope(x: { engagementId: string; taskId: string }): void {
    if (x.engagementId !== this.engagementId() || x.taskId !== this.taskId()) throw new Error('Task scope changed.');
  }
  private active(context: string, generation: number): boolean {
    return !this.destroyed && context === this.storageKey() && generation === this.session.invalidation();
  }
  private baseUrl(): string { return `/api/ui/engagements/${this.engagementId()}/workflow/tasks/${this.taskId()}/applicability`; }
  private workspaceUrl(): string {
    const p = new URLSearchParams(); if (this.sourceRequestId()) p.set('sourceRequestId', this.sourceRequestId()!);
    if (this.selectedProposalId()) p.set('proposalId', this.selectedProposalId()!);
    return this.baseUrl() + (p.size ? `?${p}` : '');
  }
  private historyUrl(): string | null {
    if (!this.historyOpen() || this.historyContext !== this.storageKey()) return null;
    const c = this.historyCursor(); const p = c ? new URLSearchParams({ afterOccurredAt: c.occurredAt, afterType: c.recordType, afterId: c.id }) : null;
    return `${this.baseUrl()}/history${p ? '?' + p : ''}`;
  }
  private storageKey(): string {
    return `auditsphere.workflow-applicability:${this.session.current()?.firmId ?? 'none'}:${this.session.current()?.userId ?? 'none'}:${this.engagementId()}:${this.taskId()}`;
  }
}
