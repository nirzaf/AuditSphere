import {
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormField, FormRoot, disabled, form, maxLength, validate } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';

export type ResourceFormKind = 'profile' | 'certification' | 'availability' | 'allocation';
export type ResourceModel = {
  user: string;
  engagement: string;
  department: string;
  skills: string;
  capacity: string;
  target: string;
  name: string;
  expires: string;
  from: string;
  to: string;
  kind: string;
  hours: string;
  week: string;
};
export type ResourceStaffOption = {
  userId: string;
  name: string;
  department: string;
  skills: string[];
  weeklyCapacityMinutes: number;
  targetUtilizationPercent: string;
  hasProfile: boolean;
};
export const resourceMinutes = (hours: string, maximum = 80): number | null => {
  if (!/^\d{1,2}(\.\d{1,2})?$/.test(hours.trim())) return null;
  const n = Number(hours.trim());
  return n <= maximum ? Math.round(n * 60) : null;
};
export function validResourceDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.slice(0, 4) === '0000') return false;
  const parsed = new Date(value + 'T00:00:00Z');
  return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
const labels: Record<keyof ResourceModel, string> = {
  user: 'Team member',
  engagement: 'Engagement',
  department: 'Department',
  skills: 'Skills (comma separated)',
  capacity: 'Weekly capacity (hours)',
  target: 'Target utilization %',
  name: 'Certification',
  expires: 'Expires (optional)',
  from: 'From',
  to: 'To',
  kind: 'Kind',
  hours: 'Hours',
  week: 'Week of',
};
export function resourceModel(today: string): ResourceModel {
  return {
    user: '',
    engagement: '',
    department: 'Audit',
    skills: '',
    capacity: '40',
    target: '80',
    name: '',
    expires: '',
    from: today,
    to: today,
    kind: 'LEAVE',
    hours: '8',
    week: today,
  };
}
@Component({
  selector: 'audit-resource-editor',
  imports: [FormField, FormRoot, MatButtonModule],
  templateUrl: './resource-editor.html',
  styleUrl: './resource-editor.scss',
})
export class ResourceEditor {
  readonly kind = input.required<ResourceFormKind>();
  readonly users = input.required<ResourceStaffOption[]>();
  readonly engagements = input<{ id: string; label: string }[]>([]);
  readonly locked = input(false);
  readonly resetVersion = input(0);
  readonly requested = output<ResourceModel>();
  readonly dirtyChange = output<boolean>();
  private readonly today = new Date().toISOString().slice(0, 10);
  readonly model = signal(resourceModel(this.today));
  readonly baseline = signal(JSON.stringify(this.model()));
  readonly attempted = signal(false);
  readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly keys = computed<(keyof ResourceModel)[]>(() => {
    switch (this.kind()) {
      case 'profile':
        return ['user', 'department', 'skills', 'capacity', 'target'];
      case 'certification':
        return ['user', 'name', 'expires'];
      case 'availability':
        return ['user', 'from', 'to', 'kind', 'hours'];
      case 'allocation':
        return ['engagement', 'user', 'week', 'hours'];
    }
  });
  readonly button = computed(
    () =>
      ({
        profile: 'Save profile',
        certification: 'Add certification',
        availability: 'Record unavailability',
        allocation: 'Save allocation',
      })[this.kind()],
  );
  readonly fields = form(
    this.model,
    (p) => {
      disabled(p, () => this.locked());
      for (const key of Object.keys(labels) as (keyof ResourceModel)[]) {
        maxLength(p[key], key === 'skills' ? 500 : 100);
        validate(p[key], ({ value }) =>
          this.keys().includes(key) && this.error(key, value())
            ? { kind: 'resource', message: this.error(key, value()) }
            : undefined,
        );
      }
    },
    {
      submission: {
        ignoreValidators: 'none',
        action: async () => {
          if (!this.locked() && this.fields().valid() && !this.fields().pending())
            this.requested.emit({ ...this.model() });
        },
        onInvalid: () => {
          this.attempted.set(true);
          setTimeout(() => this.summary()?.nativeElement.focus());
        },
      },
    },
  );
  constructor() {
    effect(() => {
      this.resetVersion();
      untracked(() => {
        this.model.set(resourceModel(this.today));
        this.baseline.set(JSON.stringify(this.model()));
        this.attempted.set(false);
        this.fields().reset();
      });
    });
    let previousUser = '';
    effect(() => {
      const id = this.model().user,
        kind = this.kind();
      untracked(() => {
        if (id === previousUser) return;
        previousUser = id;
        const staff = this.users().find((x) => x.userId === id);
        if (kind === 'profile')
          this.model.update((m) => ({
            ...m,
            department: staff?.hasProfile ? staff.department : 'Audit',
            skills: staff?.hasProfile ? staff.skills.join(', ') : '',
            capacity: staff?.hasProfile
              ? String(Math.round((staff.weeklyCapacityMinutes / 60) * 100) / 100)
              : '40',
            target: staff?.hasProfile
              ? staff.targetUtilizationPercent.replace(/(\.\d*?[1-9])0+$|\.0+$/, '$1')
              : '80',
          }));
      });
    });
    effect(() => {
      const dirty = JSON.stringify(this.model()) !== this.baseline();
      untracked(() => this.dirtyChange.emit(dirty));
    });
  }
  label(key: keyof ResourceModel): string {
    return key === 'hours'
      ? this.kind() === 'availability'
        ? 'Hours per day'
        : 'Planned hours'
      : labels[key];
  }
  id(key: string): string {
    return 'resource-' + this.kind() + '-' + key;
  }
  error(key: keyof ResourceModel, value: string): string {
    if (key === 'user')
      return this.users().some((u) => u.userId === value)
        ? ''
        : 'Choose a currently available team member.';
    if (key === 'engagement')
      return this.engagements().some((e) => e.id === value)
        ? ''
        : 'Choose a currently available engagement.';
    if (key === 'department' || key === 'name')
      return value.trim() && !/[\x00-\x1f\x7f]/.test(value)
        ? ''
        : 'Enter ' + labels[key].toLowerCase() + '.';
    if (key === 'skills')
      return /[\x00-\x1f\x7f]/.test(value)
        ? 'Use a comma-separated list without control characters.'
        : '';
    if (key === 'capacity')
      return resourceMinutes(value) !== null
        ? ''
        : 'Enter 0–80 hours, with up to two decimal places.';
    if (key === 'target')
      return /^\d{1,3}(\.\d{1,6})?$/.test(value.trim()) && Number(value) <= 100
        ? ''
        : 'Enter a percentage from 0 to 100.';
    if (key === 'hours') {
      const minutes = resourceMinutes(value, this.kind() === 'availability' ? 24 : 80);
      return minutes !== null && (this.kind() !== 'availability' || minutes > 0)
        ? ''
        : this.kind() === 'availability'
          ? 'Enter more than 0 and up to 24 hours per day.'
          : 'Enter 0–80 planned hours.';
    }
    if (key === 'kind')
      return ['LEAVE', 'TRAINING', 'PUBLIC_HOLIDAY'].includes(value)
        ? ''
        : 'Choose a supported availability kind.';
    if (key === 'expires')
      return !value || validResourceDate(value)
        ? ''
        : 'Enter a valid expiry date or leave it empty.';
    if (['from', 'to', 'week'].includes(key)) {
      if (!validResourceDate(value)) return 'Enter a valid date.';
      if (key === 'to' && validResourceDate(this.model().from) && value < this.model().from)
        return 'The end date cannot precede the start date.';
    }
    return '';
  }
  errors(): { key: keyof ResourceModel; message: string }[] {
    return this.keys()
      .filter((k) => this.fields[k]().invalid())
      .map((k) => ({
        key: k,
        message: this.fields[k]()
          .errors()
          .map((e) => e.message ?? 'Check this field.')
          .join(' '),
      }));
  }
}
