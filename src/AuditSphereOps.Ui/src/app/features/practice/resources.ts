import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormField, FormRoot, disabled, form, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, CommandState } from '../../core/api';
import { SessionService } from '../../core/session';
import { arr, bool, date, decode, dec, guid, nat, nullable, obj, oneOf, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { hours } from './analytics';
import {
  ResourceEditor,
  ResourceFormKind,
  ResourceModel,
  RESOURCE_PHASES,
  resourceMinutes,
  validResourceDate,
} from './resource-editor';
import { CertificationReview } from './certification-review';
import { ResourceNavigationDialog } from './resource-navigation-dialog';
import { TabDrafts, PendingRequestReference, pendingRequestReference } from '../../core/tab-drafts';
import {
  PlanningPreview,
  PlanningReceipt,
  planningFields,
  planningSummary,
  decodePlanningPreview,
  decodePlanningReceipt,
  decodePlanningLookup,
} from './planning-command-contracts';
const cell = obj({
  weekStart: date,
  capacityMinutes: nat,
  unavailableMinutes: nat,
  plannedMinutes: nat,
  approvedActualMinutes: nat,
  plannedUtilizationPercent: nullable(dec),
  actualUtilizationPercent: nullable(dec),
  overAllocated: bool,
  approvedBillableMinutes: nat,
});
const allocationPhase = oneOf('PLANNING', 'FIELDWORK', 'COMPLETION', 'REPORTING', 'UNASSIGNED');
const availabilityKind = oneOf('LEAVE', 'TRAINING', 'PUBLIC_HOLIDAY');
const row = obj({
  userId: guid,
  name: text,
  department: text,
  skills: arr(text, 100),
  certifications: arr(obj({ id: guid, name: text, expiresOn: nullable(date), current: bool }), 100),
  weeklyCapacityMinutes: nat,
  targetUtilizationPercent: dec,
  hasProfile: bool,
  weeks: arr(cell, 12),
  availability: arr(
    obj({ id: guid, startDate: date, endDate: date, kind: availabilityKind, minutesPerDay: nat }),
    1000,
  ),
  allocations: arr(
    obj({ engagementId: guid, engagementLabel: text, weekStart: date, phase: allocationPhase,
      riskArea: nullable(text), plannedMinutes: nat }),
    500,
  ),
});
const resourcesResponse = obj({
  grid: obj({ firstWeek: date, weeks: nat, rows: arr(row, 2000) }),
  engagements: arr(obj({ id: guid, label: text }), 200),
});

export function resourceWeekStarts(firstWeek: string, weeks: number): string[] {
  if (!validResourceDate(firstWeek) || !Number.isInteger(weeks) || weeks < 1 || weeks > 12)
    throw new Error('Invalid resource range');
  const start = Date.parse(firstWeek + 'T00:00:00Z');
  return Array.from({ length: weeks }, (_, index) =>
    new Date(start + index * 7 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10),
  );
}

export function decodeResources(value: unknown) {
  const result = decode(resourcesResponse, value);
  const starts = resourceWeekStarts(result.grid.firstWeek, result.grid.weeks);
  const rangeEnd = new Date(Date.parse(starts[starts.length - 1] + 'T00:00:00Z') + 6 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 10);
  if (
    result.grid.rows.some(
      (r) =>
        r.weeks.length !== starts.length ||
        r.weeks.some((week, index) => week.weekStart !== starts[index]) ||
        r.allocations.some((allocation) => !starts.includes(allocation.weekStart)) ||
        r.availability.some((entry) => entry.startDate > entry.endDate || entry.startDate > rangeEnd || entry.endDate < result.grid.firstWeek),
    )
  )
    throw new Error('Invalid resource grid range');
  return result;
}

const whole = (v: string | null) => (v === null ? null : v.split('.')[0]);
export const toMinutes = resourceMinutes;

@Component({
  selector: 'audit-resource-planning',
  imports: [FormField, FormRoot, MatButtonModule, MatDialogModule, RouterLink, ResourceEditor, ...SHARED],
  templateUrl: './resources.html',
  styleUrl: './resources.scss',
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ResourcePlanning {
  reviewCertification(id: string) { this.dialog.open(CertificationReview, { data: id, width: '640px', maxWidth: '95vw' }); }
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);
  private readonly drafts = inject(TabDrafts);
  private editVisit = 0;
  private reviewedOwner = '';
  private reviewedEdit = 0;
  readonly preview = signal<PlanningPreview | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly receipt = signal<PlanningReceipt | null>(null);
  readonly absent = signal(false);
  readonly summary = planningSummary;
  readonly reviewPanel = viewChild<ElementRef<HTMLElement>>('planningReview');
  readonly assentModel = signal({ reviewed: false });
  readonly assentFields = form(this.assentModel);
  private alive = true;
  private commandVisit = 0;
  private readonly today = new Date().toISOString().slice(0, 10);
  readonly rangeModel = signal({ start: this.today, weeks: 4 });
  private readonly range = signal({ start: this.today, weeks: 4 });
  readonly rangeAttempted = signal(false);
  readonly view = this.api.resource(
    () => `/api/ui/practice/resources?start=${this.range().start}&weeks=${this.range().weeks}`,
    decodeResources,
    'A firm-wide Partner, Manager or Administrator can view resource planning.',
  );
  readonly cmd = new CommandState(this.api);
  readonly locked = computed(
    () =>
      this.cmd.busy() ||
      this.cmd.uncertain() ||
      !!this.preview() ||
      this.view.loading() ||
      !this.view.data() ||
      !this.session.current()?.staff,
  );
  readonly rangeFields = form(
    this.rangeModel,
    (p) => {
      disabled(p, () => this.cmd.busy() || this.cmd.uncertain());
      validate(p.start, ({ value }) => (validResourceDate(value()) ? undefined : { kind: 'date' }));
      validate(p.weeks, ({ value }) =>
        Number.isInteger(value()) && value() >= 1 && value() <= 12 ? undefined : { kind: 'range' },
      );
    },
    {
      submission: {
        ignoreValidators: 'none',
        action: async () => {
          if (
            !this.cmd.busy() &&
            !this.cmd.uncertain() &&
            this.session.current()?.staff &&
            (await this.confirmDiscard())
          )
            this.range.set({ ...this.rangeModel() });
        },
        onInvalid: () => {
          this.rangeAttempted.set(true);
        },
      },
    },
  );
  readonly hrs = (m: number) => hours(m).replace(/\.0$/, '');
  readonly whole = whole;
  readonly phaseLabel = (phase: string) =>
    RESOURCE_PHASES.find((item) => item.value === phase)?.label ?? 'Legacy phase not assigned';
  readonly availabilityLabel = (kind: string) =>
    kind === 'LEAVE' ? 'Leave' : kind === 'TRAINING' ? 'Training' : 'Public holiday';
  readonly weekStarts = resourceWeekStarts;
  readonly hasAvailability = computed(
    () => this.view.data()?.grid.rows.some((r) => r.availability.length) ?? false,
  );
  readonly hasAllocations = computed(
    () => this.view.data()?.grid.rows.some((r) => r.allocations.length) ?? false,
  );
  /** Narrows the loaded grid by name, department or skill; it never changes or reloads planning data. */
  readonly teamFilter = signal('');
  readonly visibleRows = computed(() => {
    const rows = this.view.data()?.grid.rows ?? [];
    const term = this.teamFilter().trim().toLowerCase();
    return term ? rows.filter((r) => [r.name, r.department, ...r.skills].some((v) => v.toLowerCase().includes(term))) : rows;
  });
  /** Read-only staff/week inspection; selecting a cell never saves. */
  readonly selectedCell = signal<{ userId: string; weekStart: string } | null>(null);
  readonly cellDetail = computed(() => {
    const selection = this.selectedCell();
    const row = selection ? this.view.data()?.grid.rows.find((r) => r.userId === selection.userId) : undefined;
    const week = row?.weeks.find((w) => w.weekStart === selection?.weekStart);
    return row && week ? { row, week, allocations: row.allocations.filter((a) => a.weekStart === week.weekStart) } : null;
  });
  readonly versions = signal({ profile: 0, certification: 0, availability: 0, allocation: 0 });
  private readonly dirty = signal({
    profile: false,
    certification: false,
    availability: false,
    allocation: false,
  });
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.alive = false;
    });
    effect(() => {
      const identity = this.owner();
      untracked(() => {
        this.commandVisit++;
        this.cmd.busy.set(false);
        this.preview.set(null);
        this.pending.set(null);
        this.receipt.set(null);
        this.absent.set(false);
        this.assentModel.set({ reviewed: false });
        this.reset();
        this.cmd.message.set('');
        this.cmd.failed.set(false);
        this.cmd.uncertain.set(false);
      });
    });
    effect(() => {
      const data = this.view.data();
      if (data)
        untracked(() => {
          const recovered = this.drafts.readPendingRequest(this.scope());
          if (recovered.state === 'ready') {
            this.pending.set(recovered.draft.value);
            this.cmd.uncertain.set(true);
            this.cmd.message.set(
              'A planning request reference is retained. Verify its receipt before another action.',
            );
          } else if (recovered.state === 'unavailable' || recovered.state === 'stale') {
            this.cmd.uncertain.set(true);
            this.cmd.message.set(
              'Planning recovery storage is unavailable or stale. No action can be submitted. Restore storage and refresh.',
            );
          } else if (!this.pending()) this.cmd.uncertain.set(false);
        });
    });
  }
  private owner(): string {
    const s = this.session.current();
    return `${s?.firmId}/${s?.userId}/${s?.generation}/${this.session.invalidation()}`;
  }
  setDirty(kind: ResourceFormKind, value: boolean): void {
    if (this.dirty()[kind] === value) return;
    this.editVisit++;
    this.preview.set(null);
    this.assentModel.set({ reviewed: false });
    this.dirty.update((d) => ({ ...d, [kind]: value }));
  }
  private reset(): void {
    this.versions.update((v) => ({
      profile: v.profile + 1,
      certification: v.certification + 1,
      availability: v.availability + 1,
      allocation: v.allocation + 1,
    }));
    this.dirty.set({
      profile: false,
      certification: false,
      availability: false,
      allocation: false,
    });
  }
  private async confirmDiscard(): Promise<boolean> {
    if (!Object.values(this.dirty()).some(Boolean)) return true;
    const owner = this.owner();
    const allowed = await firstValueFrom(this.dialog.open(ResourceNavigationDialog).afterClosed());
    if (!this.alive || owner !== this.owner() || !allowed) return false;
    this.reset();
    return true;
  }
  async confirmNavigation(): Promise<boolean> {
    if (!this.session.current()?.staff) return true;
    if (this.cmd.busy() || this.cmd.uncertain()) {
      this.cmd.message.set('Reconcile the planning request before leaving this workspace.');
      return false;
    }
    return this.confirmDiscard();
  }
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.cmd.busy() || this.cmd.uncertain() || Object.values(this.dirty()).some(Boolean)) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  async refresh(): Promise<void> {
    if (this.cmd.busy()) return;
    if (await this.confirmDiscard()) this.view.reload();
  }
  private scope() {
    return {
      entity: 'resource-planning-request',
      baseRevision: this.preview()?.reviewBasis ?? '0'.repeat(64),
    };
  }
  async send(kind: ResourceFormKind, m: ResourceModel): Promise<void> {
    if (this.locked()) return;
    const owner = this.owner(),
      visit = ++this.commandVisit,
      edit = this.editVisit,
      requestId = crypto.randomUUID(),
      fields = planningFields(kind, m);
    this.cmd.busy.set(true);
    this.cmd.message.set('');
    try {
      const r = await this.api.command('/api/ui/practice/resources/preview', {
        requestId,
        fields,
      });
      if (
        !this.alive ||
        owner !== this.owner() ||
        visit !== this.commandVisit ||
        edit !== this.editVisit
      )
        return;
      if (!r.ok) {
        if (r.status === 401 || r.status === 403) {
          this.preview.set(null);
          this.view.reload();
        }
        this.cmd.failed.set(true);
        this.cmd.message.set(
          'The planning preview was not obtained. No mutation was submitted. Refresh and review again.',
        );
        return;
      }
      const p = decodePlanningPreview(r.value);
      if (
        p.requestId !== requestId ||
        p.fields.userId !== m.user ||
        p.fields.kind !== kind.toUpperCase() ||
        (kind === 'allocation' &&
          (p.fields.engagementId !== fields.engagementId ||
            p.fields.weekStart !== fields.weekStart ||
            p.fields.plannedMinutes !== fields.plannedMinutes ||
            p.fields.phase !== fields.phase ||
            p.fields.riskArea !== fields.riskArea))
      )
        throw new Error('Unsupported preview');
      this.preview.set(p);
      this.reviewedOwner = owner;
      this.reviewedEdit = edit;
      setTimeout(() => {
        if (this.alive && owner === this.owner()) this.reviewPanel()?.nativeElement.focus();
      });
      this.assentModel.set({ reviewed: false });
      this.cmd.failed.set(false);
      this.cmd.message.set(
        'Review the exact planning fields and effects. No mutation has been submitted.',
      );
    } catch {
      if (owner === this.owner()) {
        this.preview.set(null);
        this.cmd.failed.set(true);
        this.cmd.message.set('Unsupported planning preview. Refresh before review.');
      }
    } finally {
      if (this.alive && visit === this.commandVisit && owner === this.owner())
        this.cmd.busy.set(false);
    }
  }
  cancelReview(): void {
    if (!this.cmd.busy() && !this.pending()) {
      this.preview.set(null);
      this.assentModel.set({ reviewed: false });
    }
  }
  async confirmReviewed(): Promise<void> {
    const p = this.preview();
    if (
      !p ||
      this.cmd.busy() ||
      this.cmd.uncertain() ||
      !this.assentModel().reviewed ||
      this.reviewedOwner !== this.owner() ||
      this.reviewedEdit !== this.editVisit
    )
      return;
    const reference = { requestId: p.requestId, requestHash: p.requestHash };
    if (!this.drafts.save(this.scope(), reference, pendingRequestReference, true)) {
      this.cmd.failed.set(true);
      this.cmd.message.set(
        'Recovery reference could not be saved. No mutation was submitted. Restore tab storage before confirming.',
      );
      return;
    }
    const owner = this.owner(),
      visit = ++this.commandVisit;
    this.pending.set(reference);
    this.cmd.uncertain.set(true);
    this.cmd.busy.set(true);
    this.preview.set(null);
    this.assentModel.set({ reviewed: false });
    try {
      const r = await this.api.command('/api/ui/practice/resources/commands', {
        requestId: p.requestId,
        fields: p.fields,
        requestHash: p.requestHash,
        reviewBasis: p.reviewBasis,
        reviewed: true,
      });
      if (!this.alive || owner !== this.owner() || visit !== this.commandVisit) return;
      if (!r.ok) {
        this.cmd.failed.set(true);
        this.cmd.message.set(
          !r.unknown && r.code === 'revision.stale'
            ? 'Planning inputs changed after this review. The reviewed action was not accepted. Verify the retained request before closing it or reviewing the current values.'
            : 'The planning action requires receipt verification. No action is retried automatically.',
        );
        return;
      }
      const receipt = decodePlanningReceipt(r.value);
      this.acceptReceipt(receipt, reference);
      if (JSON.stringify(receipt.preview) !== JSON.stringify(p))
        throw new Error('Unsupported receipt');
      this.receipt.set(receipt);
      this.cmd.failed.set(false);
      this.cmd.message.set('Planning action recorded. Acknowledge its receipt before continuing.');
    } catch {
      if (owner === this.owner()) {
        this.receipt.set(null);
        this.cmd.failed.set(true);
        this.cmd.message.set(
          'The planning result could not be verified. Verify the retained request receipt.',
        );
      }
    } finally {
      if (this.alive && visit === this.commandVisit && owner === this.owner())
        this.cmd.busy.set(false);
    }
  }
  private acceptReceipt(receipt: PlanningReceipt, reference: PendingRequestReference): void {
    if (
      receipt.actorId !== this.session.current()?.userId ||
      receipt.requestId !== reference.requestId ||
      receipt.requestHash !== reference.requestHash
    )
      throw new Error('Unsupported receipt owner');
  }
  async verifyReceipt(): Promise<void> {
    const reference = this.pending();
    if (!reference || this.cmd.busy()) return;
    const owner = this.owner(),
      visit = ++this.commandVisit;
    this.cmd.busy.set(true);
    this.receipt.set(null);
    this.absent.set(false);
    try {
      const r = await this.api.get(
        '/api/ui/practice/resources/receipts/' +
          reference.requestId +
          '?requestHash=' +
          reference.requestHash,
        decodePlanningLookup,
      );
      if (!this.alive || owner !== this.owner() || visit !== this.commandVisit) return;
      if (r.receipt) {
        this.acceptReceipt(r.receipt, reference);
        this.receipt.set(r.receipt);
        this.cmd.message.set(
          'Retained planning receipt recovered. Acknowledge it before continuing.',
        );
      } else {
        this.absent.set(true);
        this.cmd.message.set(
          'No committed receipt was found. Explicitly close this request and reload before preparing a new action.',
        );
      }
    } catch {
      if (owner === this.owner()) {
        this.cmd.failed.set(true);
        this.cmd.message.set(
          'Planning receipt unavailable in the current session. The retained reference remains unresolved.',
        );
      }
    } finally {
      if (this.alive && visit === this.commandVisit && owner === this.owner())
        this.cmd.busy.set(false);
    }
  }
  acknowledge(): void {
    if (this.cmd.busy() || (!this.receipt() && !this.absent())) return;
    if (!this.drafts.clear(this.scope().entity)) {
      this.cmd.message.set('The planning reference could not be cleared. Retry acknowledgement.');
      return;
    }
    const kind = this.receipt()?.kind.toLowerCase() as ResourceFormKind | undefined;
    this.pending.set(null);
    this.receipt.set(null);
    this.absent.set(false);
    this.cmd.uncertain.set(false);
    this.preview.set(null);
    this.assentModel.set({ reviewed: false });
    if (kind) {
      this.versions.update((v) => ({ ...v, [kind]: v[kind] + 1 }));
      this.setDirty(kind, false);
    } else this.reset();
    if (Object.values(this.dirty()).some(Boolean))
      this.cmd.message.set(
        'Planning receipt acknowledged. Other forms have unsubmitted edits; refresh after saving or discarding them.',
      );
    else {
      this.cmd.message.set('Planning receipt acknowledged. Loading the current grid.');
      this.view.reload();
    }
  }
}
