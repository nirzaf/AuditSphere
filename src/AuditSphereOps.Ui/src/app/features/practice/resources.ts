import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormField, FormRoot, disabled, form, validate } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, CommandState } from '../../core/api';
import { SessionService } from '../../core/session';
import { arr, bool, date, dec, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { hours } from './analytics';
import {
  ResourceEditor,
  ResourceFormKind,
  ResourceModel,
  resourceMinutes,
  validResourceDate,
} from './resource-editor';
import { ResourceNavigationDialog } from './resource-navigation-dialog';
const cell = obj({
  weekStart: date,
  capacityMinutes: nat,
  unavailableMinutes: nat,
  plannedMinutes: nat,
  approvedActualMinutes: nat,
  plannedUtilizationPercent: nullable(dec),
  actualUtilizationPercent: nullable(dec),
  overAllocated: bool,
});
const row = obj({
  userId: guid,
  name: text,
  department: text,
  skills: arr(text, 100),
  certifications: arr(obj({ name: text, expiresOn: nullable(date), current: bool }), 100),
  weeklyCapacityMinutes: nat,
  targetUtilizationPercent: dec,
  hasProfile: bool,
  weeks: arr(cell, 12),
  allocations: arr(
    obj({ engagementId: guid, engagementLabel: text, weekStart: date, plannedMinutes: nat }),
    500,
  ),
});
export const decodeResources = obj({
  grid: obj({ firstWeek: date, weeks: nat, rows: arr(row, 2000) }),
  engagements: arr(obj({ id: guid, label: text }), 200),
});

const whole = (v: string | null) => (v === null ? null : v.split('.')[0]);
export const toMinutes = resourceMinutes;

@Component({
  selector: 'audit-resource-planning',
  imports: [FormField, FormRoot, MatButtonModule, MatDialogModule, ResourceEditor, ...SHARED],
  templateUrl: './resources.html',
  styleUrl: './resources.scss',
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ResourcePlanning {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);
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
  readonly hasAllocations = computed(
    () => this.view.data()?.grid.rows.some((r) => r.allocations.length) ?? false,
  );
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
        this.reset();
        this.cmd.message.set('');
        this.cmd.failed.set(false);
        this.cmd.uncertain.set(false);
      });
    });
  }
  private owner(): string {
    const s = this.session.current();
    return `${s?.firmId}/${s?.userId}/${s?.generation}/${this.session.invalidation()}`;
  }
  setDirty(kind: ResourceFormKind, value: boolean): void {
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
  async send(kind: ResourceFormKind, m: ResourceModel): Promise<void> {
    if (this.locked()) return;
    const owner = this.owner();
    const body: Record<ResourceFormKind, unknown> = {
      profile: {
        userId: m.user,
        department: m.department.trim(),
        skills: m.skills.trim(),
        weeklyCapacityMinutes: resourceMinutes(m.capacity),
        targetUtilizationPercent: m.target.trim(),
      },
      certification: { userId: m.user, name: m.name.trim(), expiresOn: m.expires || null },
      availability: {
        userId: m.user,
        startDate: m.from,
        endDate: m.to,
        kind: m.kind,
        minutesPerDay: resourceMinutes(m.hours, 24),
      },
      allocation: {
        engagementId: m.engagement,
        userId: m.user,
        weekStart: m.week,
        plannedMinutes: resourceMinutes(m.hours),
      },
    };
    const path = {
      profile: 'profiles',
      certification: 'certifications',
      availability: 'availability',
      allocation: 'allocations',
    }[kind];
    const message = {
      profile: 'Profile saved.',
      certification: 'Certification recorded.',
      availability: 'Unavailability recorded.',
      allocation: 'Allocation saved.',
    }[kind];
    const visit = ++this.commandVisit;
    this.cmd.busy.set(true);
    this.cmd.message.set('');
    let success = false;
    try {
      const result = await this.api.command('/api/ui/practice/resources/' + path, body[kind]);
      if (!this.alive || visit !== this.commandVisit || owner !== this.owner()) return;
      success = result.ok;
      this.cmd.failed.set(!result.ok);
      this.cmd.uncertain.set(!result.ok && result.unknown);
      this.cmd.message.set(result.ok ? message : result.message);
    } finally {
      if (this.alive && visit === this.commandVisit && owner === this.owner())
        this.cmd.busy.set(false);
    }
    if (success) {
      this.versions.update((v) => ({ ...v, [kind]: v[kind] + 1 }));
      this.setDirty(kind, false);
    }
    // Retain independent unsaved editors: do not reload/destroy them after another form's save.
    if (success && !Object.values(this.dirty()).some(Boolean)) this.view.reload();
  }
}
