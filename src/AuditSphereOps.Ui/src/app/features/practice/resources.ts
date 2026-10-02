import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, dec, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { hours } from './analytics';

const cell = obj({ weekStart: date, capacityMinutes: nat, unavailableMinutes: nat, plannedMinutes: nat, approvedActualMinutes: nat,
  plannedUtilizationPercent: nullable(dec), actualUtilizationPercent: nullable(dec), overAllocated: bool });
const row = obj({ userId: guid, name: text, department: text, skills: arr(text, 100), certifications: arr(obj({ name: text, expiresOn: nullable(date), current: bool }), 100),
  weeklyCapacityMinutes: nat, targetUtilizationPercent: dec, hasProfile: bool, weeks: arr(cell, 12),
  allocations: arr(obj({ engagementId: guid, engagementLabel: text, weekStart: date, plannedMinutes: nat }), 500) });
export const decodeResources = obj({ grid: obj({ firstWeek: date, weeks: nat, rows: arr(row, 2000) }), engagements: arr(obj({ id: guid, label: text }), 200) });

const whole = (v: string | null) => (v === null ? null : v.split('.')[0]);
export const toMinutes = (h: string): number | null => (/^\d{1,2}(\.\d{1,2})?$/.test(h.trim()) ? Math.round(Number(h) * 60) : null);

@Component({
  selector: 'audit-resource-planning',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header title="Resource planning" eyebrow="Practice"
      description="Team availability, skills, certifications and planned allocation by week. Planned and approved-actual utilization are shown separately; over-allocation is flagged, never hidden." />
    <form class="inline-form" aria-label="Grid range" (submit)="$event.preventDefault(); apply()">
      <label>First week <input type="date" name="start" [(ngModel)]="start" /></label>
      <label>Weeks <input type="number" name="weeks" min="1" max="12" [(ngModel)]="weeks" /></label>
      <button matButton="outlined" type="submit">Show</button>
    </form>
    <audit-state [loading]="view.loading()" [error]="view.error()" label="the resource grid" />
    @if (view.data(); as v) {
      <div class="table-scroll"><table aria-label="Resource grid">
        <thead><tr><th scope="col">Team member</th><th scope="col">Skills and certifications</th><th scope="col">Target</th>
          @for (w of v.grid.rows[0]?.weeks ?? []; track w.weekStart) { <th scope="col">Week of {{ w.weekStart }}</th> }</tr></thead>
        <tbody>@for (r of v.grid.rows; track r.userId) {
          <tr [attr.data-user]="r.name">
            <th scope="row">{{ r.name }}<small>{{ r.department }}{{ r.hasProfile ? '' : ' · no profile' }}</small></th>
            <td>@if (r.skills.length) { <span>{{ r.skills.join(', ') }}</span> }
              @for (c of r.certifications; track c.name) { <small [class.error-text]="!c.current">{{ c.name }}{{ c.expiresOn ? ' (to ' + c.expiresOn + ')' : '' }}{{ c.current ? '' : ' expired' }}</small> }</td>
            <td>{{ r.hasProfile ? whole(r.targetUtilizationPercent) + '%' : '—' }}</td>
            @for (c of r.weeks; track c.weekStart) {
              <td [class.error-text]="c.overAllocated" [attr.aria-label]="r.name + ' week of ' + c.weekStart + ': planned ' + hrs(c.plannedMinutes) + ' of ' + hrs(c.capacityMinutes) + ' hours' + (c.overAllocated ? ', over-allocated' : '')">
                <strong>{{ hrs(c.plannedMinutes) }}</strong> / {{ hrs(c.capacityMinutes) }} h
                <small>{{ c.plannedUtilizationPercent !== null ? 'planned ' + whole(c.plannedUtilizationPercent) + '%' : 'no capacity' }}{{ c.actualUtilizationPercent !== null ? ' · actual ' + whole(c.actualUtilizationPercent) + '%' : '' }}</small>
                @if (c.unavailableMinutes > 0) { <small>{{ hrs(c.unavailableMinutes) }} h unavailable</small> }
                @if (c.overAllocated) { <small class="error-text">Over-allocated</small> }
              </td>
            }
          </tr>
        } @empty { <tr><td colspan="3">No active staff.</td></tr> }</tbody>
      </table></div>
      <section class="panel" aria-labelledby="profile-heading">
        <h2 id="profile-heading">Staff profile</h2>
        <form class="inline-form" (submit)="$event.preventDefault(); saveProfile()">
          <label>Team member <select name="user" [(ngModel)]="p.user"><option value="">Select</option>@for (r of v.grid.rows; track r.userId) { <option [value]="r.userId">{{ r.name }}</option> }</select></label>
          <label>Department <input name="dept" [(ngModel)]="p.department" maxlength="100" /></label>
          <label>Skills (comma separated) <input name="skills" [(ngModel)]="p.skills" maxlength="500" /></label>
          <label>Weekly capacity (hours) <input name="capacity" inputmode="decimal" [(ngModel)]="p.capacity" /></label>
          <label>Target utilization % <input name="target" inputmode="decimal" [(ngModel)]="p.target" /></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy()">Save profile</button>
        </form>
        <h3>Certification</h3>
        <form class="inline-form" (submit)="$event.preventDefault(); addCertification()">
          <label>Certification <input name="cert" [(ngModel)]="p.cert" maxlength="100" /></label>
          <label>Expires (optional) <input type="date" name="certExpires" [(ngModel)]="p.certExpires" /></label>
          <button matButton="outlined" type="submit" [disabled]="cmd.busy()">Add certification</button>
        </form>
        <h3>Unavailability</h3>
        <form class="inline-form" (submit)="$event.preventDefault(); addAvailability()">
          <label>From <input type="date" name="leaveStart" [(ngModel)]="p.leaveStart" /></label>
          <label>To <input type="date" name="leaveEnd" [(ngModel)]="p.leaveEnd" /></label>
          <label>Kind <select name="kind" [(ngModel)]="p.kind"><option value="LEAVE">Leave</option><option value="TRAINING">Training</option><option value="PUBLIC_HOLIDAY">Public holiday</option></select></label>
          <label>Hours per day <input name="leaveHours" inputmode="decimal" [(ngModel)]="p.leaveHours" /></label>
          <button matButton="outlined" type="submit" [disabled]="cmd.busy()">Record unavailability</button>
        </form>
      </section>
      <section class="panel" aria-labelledby="allocation-heading">
        <h2 id="allocation-heading">Allocate to an engagement</h2>
        <p>Only people staffed on the engagement can be allocated. Enter 0 hours to remove an allocation.</p>
        <form class="inline-form" (submit)="$event.preventDefault(); allocate()">
          <label>Engagement <select name="engagement" [(ngModel)]="a.engagement"><option value="">Select</option>@for (e of v.engagements; track e.id) { <option [value]="e.id">{{ e.label }}</option> }</select></label>
          <label>Team member <select name="allocUser" [(ngModel)]="a.user"><option value="">Select</option>@for (r of v.grid.rows; track r.userId) { <option [value]="r.userId">{{ r.name }}</option> }</select></label>
          <label>Week of <input type="date" name="week" [(ngModel)]="a.week" /></label>
          <label>Planned hours <input name="hours" inputmode="decimal" [(ngModel)]="a.hours" /></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy()">Save allocation</button>
        </form>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class ResourcePlanning {
  private readonly api = inject(Api);
  private readonly today = new Date().toISOString().slice(0, 10);
  start = this.today;
  weeks = 4;
  private readonly range = signal({ start: this.today, weeks: 4 });
  readonly view = this.api.resource(() => `/api/ui/practice/resources?start=${this.range().start}&weeks=${this.range().weeks}`, decodeResources,
    'A Partner, Manager or Administrator can view resource planning.');
  readonly cmd = new CommandState(this.api);
  readonly hrs = (m: number) => hours(m).replace(/\.0$/, '');
  readonly whole = whole;
  p = { user: '', department: 'Audit', skills: '', capacity: '40', target: '80', cert: '', certExpires: '', leaveStart: this.today, leaveEnd: this.today, kind: 'LEAVE', leaveHours: '8' };
  a = { engagement: '', user: '', week: this.today, hours: '8' };

  apply(): void { this.range.set({ start: this.start, weeks: Math.min(12, Math.max(1, Math.trunc(Number(this.weeks)) || 4)) }); }
  private invalid(message: string): void { this.cmd.failed.set(true); this.cmd.message.set(message); }
  private send(url: string, body: unknown, ok: string): void { this.cmd.run(url, body, ok).finally(() => this.view.reload()); }
  saveProfile(): void {
    const capacity = toMinutes(this.p.capacity);
    if (!this.p.user || capacity === null || !/^\d{1,3}(\.\d{1,2})?$/.test(this.p.target.trim())) return this.invalid('Choose a team member and enter capacity hours and a target percentage.');
    this.send('/api/ui/practice/resources/profiles', { userId: this.p.user, department: this.p.department, skills: this.p.skills, weeklyCapacityMinutes: capacity, targetUtilizationPercent: this.p.target.trim() }, 'Profile saved.');
  }
  addCertification(): void {
    if (!this.p.user) return this.invalid('Choose a team member.');
    this.send('/api/ui/practice/resources/certifications', { userId: this.p.user, name: this.p.cert, expiresOn: this.p.certExpires || null }, 'Certification recorded.');
  }
  addAvailability(): void {
    const minutes = toMinutes(this.p.leaveHours);
    if (!this.p.user || minutes === null) return this.invalid('Choose a team member and hours per day.');
    this.send('/api/ui/practice/resources/availability', { userId: this.p.user, startDate: this.p.leaveStart, endDate: this.p.leaveEnd, kind: this.p.kind, minutesPerDay: minutes }, 'Unavailability recorded.');
  }
  allocate(): void {
    const minutes = toMinutes(this.a.hours);
    if (!this.a.engagement || !this.a.user || minutes === null) return this.invalid('Choose an engagement, a team member and planned hours.');
    this.send('/api/ui/practice/resources/allocations', { engagementId: this.a.engagement, userId: this.a.user, weekStart: this.a.week, plannedMinutes: minutes }, 'Allocation saved.');
  }
}
