import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { arr, bool, guid, int, nat, nullable, obj, sha256, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';

const line = obj({ lineCode: str(60), label: str(300), kind: str(40), section: str(40), order: int,
  displaySign: str(20), taxonomyNodeCode: nullable(str(100)), childLineCodes: nullable(arr(str(60), 500)),
  referencedLineCodes: nullable(arr(str(60), 500)), operations: nullable(arr(str(20), 500)),
  zeroDenominatorYieldsZero: bool, denominatorLineCode: nullable(str(60)), isSubtotal: bool });
const errorShape = obj({ lineCode: str(60), code: str(100), message: str(1000) });
const catalogueItem = obj({ layoutVersionId: guid, frameworkPolicy: str(100), name: str(200), versionNumber: nat,
  status: str(30), layoutHash: sha256, lineCount: nat, authorId: guid, publisherId: nullable(guid),
  createdAt: str(80), publishedAt: nullable(str(80)), canEdit: bool, canPublish: bool });
export const decodeStatementLayoutCatalogue = obj({ clientId: guid, page: nat, pageSize: nat, totalCount: nat,
  hasMore: bool, canCreate: bool, canPublish: bool, items: arr(catalogueItem, 51) });
export const decodeStatementLayoutDetail = obj({ layoutVersionId: guid, clientId: guid, frameworkPolicy: str(100),
  name: str(200), versionNumber: nat, status: str(30), layoutHash: sha256, lineCount: nat, authorId: guid,
  publisherId: nullable(guid), supersedesLayoutId: nullable(guid), publishReason: nullable(str(2000)),
  createdAt: str(80), publishedAt: nullable(str(80)), canEdit: bool, canPublish: bool, lines: arr(line, 500),
  errors: arr(errorShape, 1000) });
const receipt = obj({ id: guid, firmId: guid, clientId: guid, actorId: guid, actorEpoch: str(19), requestId: guid,
  action: str(10), requestHash: sha256, layoutVersionId: guid, expectedLayoutHash: nullable(sha256),
  resultLayoutHash: sha256, resultStatus: str(30), resultVersionNumber: nat, inputJson: str(120000),
  resultJson: str(1000000), createdAt: str(80) });
export const decodeStatementLayoutLookup = obj({ found: bool, receipt: nullable(receipt) });

interface LayoutLine {
  lineCode: string; label: string; kind: string; section: string; order: number; displaySign: string;
  taxonomyNodeCode: string | null; childLineCodes: string[] | null; referencedLineCodes: string[] | null;
  operations: string[] | null; zeroDenominatorYieldsZero: boolean; denominatorLineCode: string | null; isSubtotal: boolean;
}
interface EditableLine {
  lineCode: string; label: string; kind: string; section: string; order: number; displaySign: string;
  taxonomyNodeCode: string; childLineCodes: string; referencedLineCodes: string; operations: string;
  zeroDenominatorYieldsZero: boolean; denominatorLineCode: string; isSubtotal: boolean;
}
interface LayoutDraft {
  clientId: string; frameworkPolicy: string; name: string; supersedesLayoutId: string | null; lines: LayoutLine[];
}
interface EditableLayoutDraft {
  frameworkPolicy: string; name: string; supersedesLayoutId: string | null; lines: EditableLine[];
}
interface LayoutIntent {
  requestId: string; action: 'SAVE' | 'PUBLISH'; layoutVersionId: string | null; expectedLayoutHash: string | null;
  draft: LayoutDraft | null; reason: string | null; reviewed: false;
}

const emptyLine = (order: number): EditableLine => ({ lineCode: '', label: '', kind: 'HEADING', section: 'FINANCIAL_POSITION',
  order, displaySign: 'SIGNED', taxonomyNodeCode: '', childLineCodes: '', referencedLineCodes: '', operations: '',
  zeroDenominatorYieldsZero: false, denominatorLineCode: '', isSubtotal: false });
const emptyDraft = (): EditableLayoutDraft => ({ frameworkPolicy: '', name: '', supersedesLayoutId: null, lines: [emptyLine(1)] });
const lineKinds = ['HEADING', 'MAPPED_TAXONOMY_BALANCE', 'SUM_CHILD_LINES', 'TOTAL_REFERENCED_LINES', 'RATIO'];
const sections = ['FINANCIAL_POSITION', 'PROFIT_OR_LOSS', 'OCI', 'CHANGES_IN_EQUITY', 'CASH_FLOWS', 'NOTES'];
const signs = ['SIGNED', 'INVERTED', 'ABSOLUTE'];
const referenceList = (text: string) => text.split(/[\n,]/).map(x => x.trim().toUpperCase()).filter(Boolean);

export function statementLayoutIntent(raw: unknown): LayoutIntent | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const x = raw as Record<string, unknown>;
  const keys = ['requestId', 'action', 'layoutVersionId', 'expectedLayoutHash', 'draft', 'reason', 'reviewed'];
  if (Object.keys(x).length !== keys.length || keys.some(k => !Object.hasOwn(x, k)) ||
      typeof x['requestId'] !== 'string' || !/^[0-9a-f-]{36}$/i.test(x['requestId']) ||
      !['SAVE', 'PUBLISH'].includes(String(x['action'])) || x['reviewed'] !== false ||
      (x['layoutVersionId'] !== null && (typeof x['layoutVersionId'] !== 'string' || !/^[0-9a-f-]{36}$/i.test(x['layoutVersionId']))) ||
      (x['expectedLayoutHash'] !== null && (typeof x['expectedLayoutHash'] !== 'string' || !/^[0-9a-f]{64}$/.test(x['expectedLayoutHash'])))) return null;
  const action = x['action'] as LayoutIntent['action'];
  if (action === 'SAVE') {
    if (x['reason'] !== null || !x['draft'] || typeof x['draft'] !== 'object' || Array.isArray(x['draft'])) return null;
    const d = x['draft'] as Record<string, unknown>;
    if (Object.keys(d).length !== 5 || typeof d['clientId'] !== 'string' || !/^[0-9a-f-]{36}$/i.test(d['clientId']) ||
        typeof d['frameworkPolicy'] !== 'string' || d['frameworkPolicy'].length > 100 ||
        typeof d['name'] !== 'string' || d['name'].length > 200 ||
        (d['supersedesLayoutId'] !== null && (typeof d['supersedesLayoutId'] !== 'string' || !/^[0-9a-f-]{36}$/i.test(d['supersedesLayoutId']))) ||
        !Array.isArray(d['lines']) || d['lines'].length < 1 || d['lines'].length > 500) return null;
    const lines = d['lines'] as unknown[];
    try {
      const decodedLines = lines.map((v, i) => line(v, `intent.draft.lines[${i}]`));
      if (decodedLines.some((v, i) => v.order !== i + 1)) return null;
      return { requestId: x['requestId'] as string, action, layoutVersionId: x['layoutVersionId'] as string | null,
        expectedLayoutHash: x['expectedLayoutHash'] as string | null,
        draft: { clientId: d['clientId'] as string, frameworkPolicy: d['frameworkPolicy'] as string,
          name: d['name'] as string, supersedesLayoutId: d['supersedesLayoutId'] as string | null, lines: decodedLines },
        reason: null, reviewed: false };
    } catch { return null; }
  }
  return x['draft'] === null && typeof x['reason'] === 'string' && x['reason'].length <= 2000 &&
    typeof x['layoutVersionId'] === 'string' && typeof x['expectedLayoutHash'] === 'string'
    ? { requestId: x['requestId'] as string, action, layoutVersionId: x['layoutVersionId'] as string,
      expectedLayoutHash: x['expectedLayoutHash'] as string, draft: null, reason: x['reason'], reviewed: false } : null;
}

function statementLayoutEditableDraft(raw: unknown): EditableLayoutDraft | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const d = raw as Record<string, unknown>;
  if (Object.keys(d).length !== 4 || typeof d['frameworkPolicy'] !== 'string' || d['frameworkPolicy'].length > 100 ||
      typeof d['name'] !== 'string' || d['name'].length > 200 ||
      (d['supersedesLayoutId'] !== null && (typeof d['supersedesLayoutId'] !== 'string' || !/^[0-9a-f-]{36}$/i.test(d['supersedesLayoutId']))) ||
      !Array.isArray(d['lines']) || d['lines'].length < 1 || d['lines'].length > 500) return null;
  const lines = d['lines'] as unknown[];
  const valid = lines.every((rawLine, i) => {
    if (!rawLine || typeof rawLine !== 'object' || Array.isArray(rawLine)) return false;
    const l = rawLine as Record<string, unknown>;
    return Object.keys(l).length === 13 && typeof l['lineCode'] === 'string' && l['lineCode'].length <= 60 &&
      typeof l['label'] === 'string' && l['label'].length <= 300 && typeof l['kind'] === 'string' && l['kind'].length <= 40 &&
      typeof l['section'] === 'string' && l['section'].length <= 40 && Number.isSafeInteger(l['order']) && Number(l['order']) === i + 1 &&
      typeof l['displaySign'] === 'string' && l['displaySign'].length <= 20 && typeof l['taxonomyNodeCode'] === 'string' && l['taxonomyNodeCode'].length <= 100 &&
      ['childLineCodes', 'referencedLineCodes', 'operations'].every(k => typeof l[k] === 'string' && (l[k] as string).length <= 12000) &&
      typeof l['zeroDenominatorYieldsZero'] === 'boolean' && typeof l['denominatorLineCode'] === 'string' && l['denominatorLineCode'].length <= 60 &&
      typeof l['isSubtotal'] === 'boolean';
  });
  return valid ? d as unknown as EditableLayoutDraft : null;
}

@Component({
  selector: 'audit-statement-layout-setup',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header title="Statement layouts" eyebrow="Client accounting setup"
      description="Create a client-owned typed layout draft, review its validation and publish it independently.">
    </audit-page-header>
    <button matButton (click)="refresh()" [disabled]="busy()">Refresh layouts</button>
    <audit-state [loading]="catalogue.loading()" [error]="catalogue.error()" label="statement layouts" />
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (pending(); as pendingIntent) {
      <section aria-label="Unconfirmed statement-layout command"><h2>Unconfirmed command</h2>
        <p>Request <code>{{ pendingIntent.requestId }}</code>. The original fields remain fixed while its outcome is checked.</p>
        <button matButton (click)="reconcile()" [disabled]="busy()">Check retained receipt</button>
        @if (absent()) { <label><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)" /> I reviewed the exact retained intent and authorize one identical retry.</label>
          <button matButton (click)="retryOriginal()" [disabled]="busy() || !reviewed()">Retry exact request</button> }
        @if (receipt()) { <button matButton (click)="acknowledge()" [disabled]="busy()">Acknowledge retained result</button> }
      </section>
    }
    @if (catalogue.data(); as page) {
      <section aria-label="Statement layout catalogue"><h2>Latest client layouts</h2>
        @if (!page.items.length) { <p>No statement layouts exist for this client. Start with a new draft.</p> }
        <ul>@for (item of page.items; track item.layoutVersionId) {
          <li><button matButton (click)="open(item.layoutVersionId)" [disabled]="busy() || uncertain()">{{ item.frameworkPolicy }} · {{ item.name }} · v{{ item.versionNumber }} · {{ item.status }}</button>
            <span>{{ item.lineCount }} lines · {{ item.layoutHash }}</span></li>
        }</ul>
        <button matButton (click)="newDraft()" [disabled]="!page.canCreate || busy() || uncertain()">Create layout draft</button>
      </section>
      @if (detail.loading() || detail.error()) { <audit-state [loading]="detail.loading()" [error]="detail.error()" label="selected layout" /> }
      @if (selected(); as selectedDetail) {
        <section aria-label="Selected statement layout"><h2>{{ selectedDetail.frameworkPolicy }} · {{ selectedDetail.name }} · v{{ selectedDetail.versionNumber }}</h2>
          <p>Status {{ selectedDetail.status }} · {{ selectedDetail.lineCount }} lines · SHA-256 <code>{{ selectedDetail.layoutHash }}</code></p>
          @if (selectedDetail.supersedesLayoutId) { <p>Explicit predecessor <code>{{ selectedDetail.supersedesLayoutId }}</code></p> }
          @if (selectedDetail.errors.length) { <section role="alert"><h3>Draft validation findings</h3><ul>@for (e of selectedDetail.errors; track $index) { <li>{{ e.lineCode || 'Layout' }} · {{ e.code }}: {{ e.message }}</li> }</ul></section> }
        </section>
      }
      @if (editing() && !uncertain() && !receipt()) {
        <section aria-label="Typed statement layout editor"><h2>{{ layoutVersionId() ? 'Revise exact draft' : 'New layout draft' }}</h2>
          @if (editableAvailable()) { <p>Unsent editable fields are saved in this tab, without review assent.</p><button matButton type="button" (click)="restoreDraft()" [disabled]="busy()">Restore tab draft</button> }
          <fieldset [disabled]="busy()"><label>Framework <input name="framework" maxlength="100" [ngModel]="model().frameworkPolicy" (ngModelChange)="setModel({frameworkPolicy:$event})" [disabled]="!!layoutVersionId()" /></label>
            <label>Layout name <input name="layoutName" maxlength="200" [ngModel]="model().name" (ngModelChange)="setModel({name:$event})" [disabled]="!!layoutVersionId()" /></label>
            @if (!layoutVersionId()) { <label>Exact published predecessor (optional; never selected automatically)
              <select name="predecessor" [ngModel]="model().supersedesLayoutId" (ngModelChange)="setModel({supersedesLayoutId:$event || null})">
                <option [ngValue]="null">No predecessor</option>
                @for (item of matchingPredecessors(); track item.layoutVersionId) { <option [ngValue]="item.layoutVersionId">{{ item.frameworkPolicy }} · {{ item.name }} · v{{ item.versionNumber }}</option> }
              </select></label> }
            <div class="line-editor"><h3>Typed lines</h3><p>Use the existing statement-line grammar. Structural validation findings remain on the draft for correction; unsupported renderer sections and ratios cannot be published here.</p>
              @for (entry of model().lines; track $index; let i = $index) {
                <fieldset class="line"><legend>Line {{ i + 1 }}</legend>
                  <label>Code <input [name]="'code-' + i" maxlength="60" [ngModel]="entry.lineCode" (ngModelChange)="setLine(i,{lineCode:$event})" /></label>
                  <label>Label <input [name]="'label-' + i" maxlength="300" [ngModel]="entry.label" (ngModelChange)="setLine(i,{label:$event})" /></label>
                  <label>Kind <select [name]="'kind-' + i" [ngModel]="entry.kind" (ngModelChange)="setLine(i,{kind:$event})">@for (kind of lineKinds; track kind) { <option [value]="kind">{{ kind }}</option> }</select></label>
                  <label>Section <select [name]="'section-' + i" [ngModel]="entry.section" (ngModelChange)="setLine(i,{section:$event})">@for (section of sections; track section) { <option [value]="section">{{ section }}</option> }</select></label>
                  <label>Display sign <select [name]="'sign-' + i" [ngModel]="entry.displaySign" (ngModelChange)="setLine(i,{displaySign:$event})">@for (sign of signs; track sign) { <option [value]="sign">{{ sign }}</option> }</select></label>
                  @if (entry.kind === 'MAPPED_TAXONOMY_BALANCE') { <label>Taxonomy node <input [name]="'taxonomy-' + i" maxlength="100" [ngModel]="entry.taxonomyNodeCode" (ngModelChange)="setLine(i,{taxonomyNodeCode:$event})" /></label> }
                  @if (entry.kind === 'SUM_CHILD_LINES') { <label>Child line codes, comma separated <input [name]="'children-' + i" maxlength="12000" [ngModel]="entry.childLineCodes" (ngModelChange)="setLine(i,{childLineCodes:$event})" /></label> }
                  @if (entry.kind === 'TOTAL_REFERENCED_LINES' || entry.kind === 'RATIO') { <label>Referenced line codes, comma separated <input [name]="'refs-' + i" maxlength="12000" [ngModel]="entry.referencedLineCodes" (ngModelChange)="setLine(i,{referencedLineCodes:$event})" /></label> }
                  @if (entry.kind === 'TOTAL_REFERENCED_LINES') { <label>Operations, comma separated ADD / SUBTRACT <input [name]="'ops-' + i" maxlength="12000" [ngModel]="entry.operations" (ngModelChange)="setLine(i,{operations:$event})" /></label> }
                  @if (entry.kind === 'RATIO') { <label>Denominator line code <input [name]="'denominator-' + i" maxlength="60" [ngModel]="entry.denominatorLineCode" (ngModelChange)="setLine(i,{denominatorLineCode:$event})" /></label>
                    <label><input type="checkbox" [checked]="entry.zeroDenominatorYieldsZero" (change)="setLine(i,{zeroDenominatorYieldsZero:$any($event.target).checked})" /> A zero denominator yields zero</label> }
                  <label><input type="checkbox" [checked]="entry.isSubtotal" (change)="setLine(i,{isSubtotal:$any($event.target).checked})" /> This line is a subtotal</label>
                  <button matButton type="button" (click)="removeLine(i)" [disabled]="model().lines.length <= 1">Remove line</button>
                </fieldset>
              }
              <button matButton type="button" (click)="addLine()" [disabled]="model().lines.length >= 500">Add typed line</button>
            </div>
            <label><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)" /> I reviewed these exact client, framework, name, predecessor and typed line values for saving.</label>
            <button matButton (click)="save()" [disabled]="busy() || !reviewed() || !canSave()">Save reviewed draft</button>
          </fieldset>
        </section>
      }
      @if (selected()?.canPublish && !receipt() && !uncertain()) { <section aria-label="Independent layout publication">
        <h2>Independent publication</h2><p>The publisher must differ from the draft author. This publication applies only to the selected client-owned version.</p>
        <label>Publication reason <textarea name="publishReason" maxlength="2000" [ngModel]="publishReason()" (ngModelChange)="publishReason.set($event); reviewed.set(false)"></textarea></label>
        <label><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)" /> I independently reviewed the exact layout hash, validation and renderer-supported lines.</label>
        <button matButton (click)="publish()" [disabled]="busy() || !reviewed() || !publishReason().trim()">Publish independently</button>
      </section> }
      @if (receipt(); as saved) { <section aria-label="Retained statement layout result"><h2>{{ saved.action === 'SAVE' ? 'Layout draft retained' : 'Layout publication retained' }}</h2>
        <p>Request <code>{{ saved.requestId }}</code> · version <code>{{ saved.layoutVersionId }}</code> · {{ saved.resultStatus }} · {{ saved.resultLayoutHash }}</p>
        <button matButton (click)="acknowledge()" [disabled]="busy()">Acknowledge exact retained result</button></section> }
    }
  `,
  styles: [`:host{display:block;min-width:0}.line-editor{margin-block:1.5rem}.line{display:grid;grid-template-columns:repeat(auto-fit,minmax(14rem,1fr));gap:.6rem;margin-block:1rem;padding:1rem}label{display:block;margin-block:.6rem}input,select,textarea{max-width:100%;box-sizing:border-box}li{overflow-wrap:anywhere}code{overflow-wrap:anywhere}`],
})
export class StatementLayoutSetup implements NavigationProtected {
  readonly clientId = routeGuid('clientId');
  readonly lineKinds = lineKinds; readonly sections = sections; readonly signs = signs;
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts); private readonly dialog = inject(MatDialog);
  readonly catalogue = this.api.resource(() => this.clientId() ? this.root() : null, decodeStatementLayoutCatalogue);
  readonly selectedId = signal<string | null>(null);
  readonly detail = this.api.resource(() => this.selectedId() ? `${this.root()}/${this.selectedId()}` : null, decodeStatementLayoutDetail);
  readonly selected = signal<ReturnType<typeof decodeStatementLayoutDetail> | null>(null);
  readonly layoutVersionId = signal<string | null>(null);
  readonly model = signal<EditableLayoutDraft>(emptyDraft());
  readonly publishReason = signal(''); readonly reviewed = signal(false); readonly busy = signal(false);
  readonly message = signal(''); readonly pending = signal<LayoutIntent | null>(null);
  readonly receipt = signal<ReturnType<typeof receipt> | null>(null);
  readonly uncertain = signal(false); readonly absent = signal(false); readonly editableAvailable = signal(false);
  private generation = 0; private identity = ''; private loadedKey = '';
  private baseline = JSON.stringify(emptyDraft());
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline);
  readonly editing = computed(() => this.catalogue.data()?.canCreate === true && (this.layoutVersionId() === null || this.selected()?.canEdit === true));
  readonly matchingPredecessors = computed(() => (this.catalogue.data()?.items ?? []).filter(x => x.status === 'PUBLISHED' &&
    x.frameworkPolicy === this.model().frameworkPolicy.trim() && x.name === this.model().name.trim()));
  readonly canSave = computed(() => {
    const m = this.model(); return !!this.clientId() && m.frameworkPolicy.trim().length > 0 && m.frameworkPolicy.trim().length <= 100 &&
      m.name.trim().length > 0 && m.name.trim().length <= 200 && m.lines.length > 0 && m.lines.length <= 500 &&
      m.lines.every((x, i) => x.lineCode.trim().length > 0 && x.lineCode.trim().length <= 60 && x.label.trim().length > 0 &&
        x.label.trim().length <= 300 && lineKinds.includes(x.kind) && sections.includes(x.section) && signs.includes(x.displaySign) &&
        Number.isSafeInteger(x.order) && x.order === i + 1);
  });

  constructor() {
    effect(() => {
      const id = this.clientId(), s = this.session.current(), epoch = this.session.invalidation();
      const key = `${id}:${s?.firmId}:${s?.userId}:${s?.generation}:${epoch}`;
      if (key === this.identity) return;
      this.identity = key; this.generation++;
      untracked(() => {
        this.selectedId.set(null); this.selected.set(null); this.layoutVersionId.set(null); this.model.set(emptyDraft());
        this.baseline = JSON.stringify(this.model()); this.loadedKey = ''; this.publishReason.set(''); this.reviewed.set(false);
        this.pending.set(null); this.receipt.set(null); this.uncertain.set(false); this.absent.set(false);
        this.busy.set(false); this.message.set(''); this.editableAvailable.set(false);
        if (id && s?.staff) {
          const saved = this.drafts.readPendingSubmission(this.scope(id), statementLayoutIntent);
          if (saved.state === 'ready') {
            this.pending.set(saved.draft.value); this.uncertain.set(true);
            this.message.set('A prior layout command has an unconfirmed result. Check its retained receipt before editing.');
          } else if (saved.state === 'stale' || saved.state === 'unavailable') {
            this.uncertain.set(true); this.message.set('A prior statement-layout recovery record cannot be read safely. Do not send another command.');
          } else {
            this.editableAvailable.set(this.drafts.read(this.scope(id), statementLayoutEditableDraft).state === 'ready');
          }
        }
      });
    });
    effect(() => {
      const d = this.detail.data(); if (!d) return;
      const key = `${d.layoutVersionId}:${d.layoutHash}`;
      if (key === this.loadedKey) return;
      this.loadedKey = key;
      untracked(() => {
        this.selected.set(d); this.layoutVersionId.set(d.layoutVersionId); this.publishReason.set(''); this.reviewed.set(false);
        if (!this.uncertain() && d.canEdit) {
          const draft = this.fromDetail(d); this.model.set(draft); this.baseline = JSON.stringify(draft);
        }
      });
    });
  }
  private root() { return `/api/ui/accounting/clients/${this.clientId()}/statement-layouts`; }
  private scope(id = this.clientId()!) { return { entity: `statement-layout/${id}/command`, baseRevision: '0'.repeat(64) }; }
  private fromDetail(d: ReturnType<typeof decodeStatementLayoutDetail>): EditableLayoutDraft {
    return { frameworkPolicy: d.frameworkPolicy, name: d.name, supersedesLayoutId: d.supersedesLayoutId,
      lines: d.lines.map(x => ({ lineCode: x.lineCode, label: x.label, kind: x.kind, section: x.section, order: x.order,
        displaySign: x.displaySign, taxonomyNodeCode: x.taxonomyNodeCode ?? '', childLineCodes: x.childLineCodes?.join(', ') ?? '',
        referencedLineCodes: x.referencedLineCodes?.join(', ') ?? '', operations: x.operations?.join(', ') ?? '',
        zeroDenominatorYieldsZero: x.zeroDenominatorYieldsZero, denominatorLineCode: x.denominatorLineCode ?? '', isSubtotal: x.isSubtotal })) };
  }
  setModel(change: Partial<EditableLayoutDraft>) { this.model.update(x => ({ ...x, ...change })); this.reviewed.set(false); }
  setLine(i: number, change: Partial<EditableLine>) {
    this.model.update(m => ({ ...m, lines: m.lines.map((x, index) => index === i ? { ...x, ...change } : x) })); this.reviewed.set(false);
  }
  addLine() { if (this.model().lines.length < 500) this.model.update(m => ({ ...m, lines: [...m.lines, emptyLine(m.lines.length + 1)] })); this.reviewed.set(false); }
  removeLine(i: number) { if (this.model().lines.length > 1) this.model.update(m => ({ ...m, lines: m.lines.filter((_, index) => index !== i).map((x, n) => ({ ...x, order: n + 1 })) })); this.reviewed.set(false); }
  open(id: string) { if (this.busy() || this.uncertain()) return; this.selected.set(null); this.loadedKey = ''; this.selectedId.set(id); }
  newDraft() { if (this.busy() || this.uncertain() || !this.catalogue.data()?.canCreate) return;
    this.selectedId.set(null); this.selected.set(null); this.loadedKey = ''; this.layoutVersionId.set(null);
    this.model.set(emptyDraft()); this.baseline = JSON.stringify(this.model()); this.publishReason.set(''); this.reviewed.set(false); this.message.set(''); }
  private toLines(d: EditableLayoutDraft): LayoutLine[] { return d.lines.map((x, i) => ({ lineCode: x.lineCode.trim().toUpperCase(), label: x.label.trim(),
    kind: x.kind, section: x.section, order: i + 1, displaySign: x.displaySign, taxonomyNodeCode: x.taxonomyNodeCode.trim() || null,
    childLineCodes: referenceList(x.childLineCodes), referencedLineCodes: referenceList(x.referencedLineCodes),
    operations: referenceList(x.operations), zeroDenominatorYieldsZero: x.zeroDenominatorYieldsZero,
    denominatorLineCode: x.denominatorLineCode.trim().toUpperCase() || null, isSubtotal: x.isSubtotal })); }
  async save() {
    const d = this.model(), client = this.clientId(); if (!client || !this.canSave() || this.busy() || this.uncertain() || !this.reviewed()) return;
    const detail = this.selected(), draft = { clientId: client, frameworkPolicy: d.frameworkPolicy.trim(), name: d.name.trim(),
      lines: this.toLines(d), supersedesLayoutId: d.supersedesLayoutId };
    const intent: LayoutIntent = { requestId: crypto.randomUUID(), action: 'SAVE', layoutVersionId: this.layoutVersionId(),
      expectedLayoutHash: this.layoutVersionId() ? detail?.layoutHash ?? null : null, draft, reason: null, reviewed: false };
    await this.send(intent);
  }
  async publish() {
    const d = this.selected(), reason = this.publishReason().trim();
    if (!d?.canPublish || !reason || reason.length > 2000 || !this.reviewed() || this.busy() || this.uncertain()) return;
    const intent: LayoutIntent = { requestId: crypto.randomUUID(), action: 'PUBLISH', layoutVersionId: d.layoutVersionId,
      expectedLayoutHash: d.layoutHash, draft: null, reason, reviewed: false };
    await this.send(intent);
  }
  private async send(intent: LayoutIntent) {
    const client = this.clientId(), generation = this.generation; if (!client) return;
    if (!this.drafts.save(this.scope(client), intent, statementLayoutIntent, true)) {
      this.message.set('The original command cannot be stored in this tab. No layout change was sent.'); return;
    }
    this.pending.set(intent); this.busy.set(true); this.reviewed.set(false); this.message.set('');
    const endpoint = intent.action === 'SAVE' ? `${this.root()}/drafts` : `${this.root()}/${intent.layoutVersionId}/publish`;
    const body = intent.action === 'SAVE'
      ? { requestId: intent.requestId, layoutVersionId: intent.layoutVersionId, expectedLayoutHash: intent.expectedLayoutHash, draft: intent.draft, reviewed: true }
      : { requestId: intent.requestId, expectedLayoutHash: intent.expectedLayoutHash, reason: intent.reason, reviewed: true };
    const out = await this.api.command(endpoint, body);
    if (generation !== this.generation) return;
    this.busy.set(false);
    if (!out.ok) {
      if (out.unknown) { this.uncertain.set(true); this.absent.set(false); this.message.set(UNKNOWN_OUTCOME); }
      else { this.message.set(out.message); this.clearPending(); }
      return;
    }
    try {
      const r = receipt(out.value, 'receipt');
      if (r.clientId !== client || r.actorId !== this.session.current()?.userId || r.actorEpoch !== this.session.current()?.generation ||
          r.requestId !== intent.requestId || r.action !== intent.action || (intent.layoutVersionId && r.layoutVersionId !== intent.layoutVersionId) ||
          r.expectedLayoutHash !== intent.expectedLayoutHash) throw new Error('Receipt does not match the original layout command');
      this.receipt.set(r); this.uncertain.set(true); this.absent.set(false);
      this.message.set('The exact layout result is retained. Acknowledge it before starting another command.');
    } catch { this.uncertain.set(true); this.message.set(UNKNOWN_OUTCOME); }
  }
  private clearPending() {
    const client = this.clientId(); if (!client || !this.drafts.clear(this.scope(client).entity)) {
      this.uncertain.set(true); this.message.set('The recovery record could not be cleared. Keep this command fenced and retry the receipt check.'); return;
    }
    this.pending.set(null); this.uncertain.set(false); this.absent.set(false); this.receipt.set(null); this.reviewed.set(false);
  }
  async reconcile() {
    const p = this.pending(), client = this.clientId(), generation = this.generation;
    if (!p || !client || this.busy()) return; this.busy.set(true);
    try {
      const out = await this.api.get(`${this.root()}/receipts/${p.requestId}`, decodeStatementLayoutLookup);
      if (generation !== this.generation) return;
      if (!out.found && !out.receipt) { this.absent.set(true); this.message.set('No receipt is retained. The original request remains locked; an identical retry requires fresh assent.'); }
      else if (out.found && out.receipt && out.receipt.requestId === p.requestId && out.receipt.clientId === client &&
        out.receipt.actorId === this.session.current()?.userId && out.receipt.actorEpoch === this.session.current()?.generation && out.receipt.action === p.action &&
        out.receipt.expectedLayoutHash === p.expectedLayoutHash && (!p.layoutVersionId || out.receipt.layoutVersionId === p.layoutVersionId)) {
        this.receipt.set(out.receipt); this.absent.set(false); this.message.set('The retained receipt confirms the original request. Acknowledge this exact outcome.');
      } else throw new Error('Receipt identity mismatch');
    } catch { if (generation === this.generation) this.message.set('Receipt verification failed. Keep the original request fenced and try the read again.'); }
    finally { if (generation === this.generation) this.busy.set(false); }
  }
  retryOriginal() { const p = this.pending(); if (!p || !this.absent() || !this.reviewed() || this.busy()) return Promise.resolve(); return this.send(p); }
  acknowledge() {
    const retained = this.receipt(); if (!retained || !this.pending()) return;
    const reopenSaved = retained.action === 'SAVE' ? retained.layoutVersionId : null;
    const alreadySelected = this.selectedId() === reopenSaved;
    this.clearPending(); if (this.uncertain()) return;
    this.selectedId.set(reopenSaved); this.selected.set(null); this.layoutVersionId.set(reopenSaved); this.loadedKey = '';
    if (alreadySelected && reopenSaved) this.detail.reload();
    this.catalogue.reload(); this.message.set('Retained statement-layout result acknowledged.');
  }
  refresh() { if (this.busy()) return; this.reviewed.set(false); this.catalogue.reload(); if (this.selectedId()) this.detail.reload(); }
  restoreDraft() {
    if (this.busy() || this.uncertain()) return;
    const saved = this.drafts.read(this.scope(), statementLayoutEditableDraft);
    if (saved.state !== 'ready') { this.message.set('The editable layout draft is stale or unavailable.'); return; }
    this.model.set(saved.draft.value); this.baseline = JSON.stringify(this.model()); this.reviewed.set(false); this.editableAvailable.set(false);
  }
  private saveDraft(): boolean {
    const saved = !this.busy() && !this.uncertain() && this.drafts.save(this.scope(), this.model(), statementLayoutEditableDraft);
    if (saved) { this.baseline = JSON.stringify(this.model()); this.editableAvailable.set(true); }
    this.message.set(saved ? 'Editable layout fields are saved in this tab. Review assent is excluded.' : 'The editable layout draft could not be saved.');
    return saved;
  }
  private discardDraft(): boolean {
    if (this.busy() || this.uncertain() || !this.drafts.clear(this.scope().entity)) return false;
    this.model.set(JSON.parse(this.baseline) as EditableLayoutDraft); this.reviewed.set(false); this.editableAvailable.set(false); return true;
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) { this.message.set('Resolve the retained layout result before leaving this page.'); return false; }
    if (!this.dirty()) return true;
    const result = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    return result === 'save' ? this.saveDraft() : result === 'discard' ? this.discardDraft() : false;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) { e.preventDefault(); e.returnValue = ''; }
  }
}
