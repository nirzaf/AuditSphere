import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { workspaceRoute } from '../../core/navigation';
import { Api } from '../../core/api';
import { arr, bool, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const task = obj({ id: text, title: text, status: text, workPackage: text, modules: arr(int, 100), blockedReason: text });
const group = obj({ number: int, name: text, tasks: arr(task, 5000), completed: nat, completionPercent: nat, active: nat, blocked: nat, pending: nat });
export const decodeProgress = obj({ snapshot: obj({ tasks: arr(task, 5000), modules: arr(group, 200), auditTasks: arr(task, 5000), sharedTasks: arr(task, 5000),
  auditPhases: arr(group, 200), publishedAtUtc: text, publicationAgeDays: nat, isStale: bool, freshnessWindowDays: nat,
  auditCompleted: nat, sharedCompleted: nat, completed: nat, completionPercent: nat, active: nat, blocked: nat, pending: nat }),
  untracked: arr(obj({ name: text, route: nullable(text) }), 100) });
type Task = ReturnType<typeof task>;
const FILTERS: [string, string][] = [['All', 'All'], ['Completed', 'Completed'], ['Active', 'Active or in review'], ['Pending', 'Pending or reopened'], ['Blocked', 'Blocked']];

@Component({
  selector: 'audit-task-bar',
  template: `<div class="task-bar" role="img" [attr.aria-label]="label + ': ' + count('COMPLETED') + ' completed, ' + active() + ' active, ' + pending() + ' pending, ' + count('BLOCKED') + ' blocked'">
    <span class="done" [style.flex-grow]="count('COMPLETED')"></span><span class="active" [style.flex-grow]="active()"></span><span class="pending" [style.flex-grow]="pending()"></span><span class="blocked" [style.flex-grow]="count('BLOCKED')"></span></div>`,
  styles: [`.task-bar{display:flex;height:.75rem;border-radius:4px;overflow:hidden;background:#e3ebf3;margin:.5rem 0}.done{background:#2e7d32}.active{background:#1565c0}.pending{background:#9e9e9e}.blocked{background:#c62828}`],
  inputs: ['tasks', 'label'],
})
export class TaskBar {
  tasks: Task[] = [];
  label = '';
  count(s: string): number { return this.tasks.filter((t) => t.status === s).length; }
  active(): number { return this.tasks.filter((t) => t.status === 'IN_PROGRESS' || t.status === 'IN_REVIEW').length; }
  pending(): number { return this.tasks.length - this.count('COMPLETED') - this.active() - this.count('BLOCKED'); }
}

@Component({
  selector: 'audit-project-progress',
  imports: [MatButtonModule, RouterLink, TaskBar, ...SHARED],
  template: `
    <a routerLink="/app/administration">← Firm administration</a>
    <audit-page-header title="Project task progress" eyebrow="Implementation tracker"
      description="Completed and pending implementation task cards by module. Bars report reviewed task-card status, not a percentage of working software or production readiness." />
    <div class="actions"><button matButton="outlined" (click)="p.reload()" [disabled]="p.loading()">Refresh task progress</button></div>
    <audit-state [loading]="p.loading()" [error]="p.error()" label="task-card progress" />
    @if (p.data(); as d) {
      @let s = d.snapshot;
      <section class="panel" aria-label="Task-card totals">
        <p><strong>Snapshot published:</strong> {{ s.publishedAtUtc }} UTC · {{ s.publicationAgeDays }} days old · stale after {{ s.freshnessWindowDays }} days.</p>
        @if (s.isStale) {
          <div class="notice" role="alert" aria-label="Stale task-card snapshot"><strong>Snapshot is stale.</strong> Review the task-card statuses and publish a current snapshot. Displayed statuses may be outdated.</div>
        }
        <p><strong>{{ s.completed }} / {{ s.tasks.length }} ({{ s.completionPercent }}%)</strong> completed · {{ s.active }} active or in review · {{ s.pending }} pending or reopened · {{ s.blocked }} blocked</p>
        <audit-task-bar [tasks]="s.tasks" label="Overall task-card progress" />
        <p aria-label="Progress bar key">Completed · Active or in review · Pending or reopened · Blocked</p>
        <p><small>A card becomes complete only after its documented review and evidence gates. The same cross-module card may appear in more than one module below; the total counts each card once.</small></p></section>
      <section class="panel" aria-label="Task-card filters"><h2>Find task cards</h2>
        <div class="actions" role="group" aria-label="Filter task cards by status">@for (f of filters; track f[0]) { <button [attr.aria-pressed]="filter() === f[0]" [matButton]="filter() === f[0] ? 'filled' : 'outlined'" (click)="filter.set(f[0])">{{ f[1] }}</button> }</div>
        <p><small>Showing {{ filtered(s.tasks).length }} of {{ s.tasks.length }} distinct task cards. Filters change the lists below; bars and counts always show all published cards.</small></p></section>
      @for (m of s.modules; track m.number) {
        <section class="panel" [attr.aria-labelledby]="'module-' + m.number"><h2 [id]="'module-' + m.number">Module {{ m.number }} — {{ m.name }} <small>{{ m.completed }} / {{ m.tasks.length }} ({{ m.completionPercent }}%)</small></h2>
          <audit-task-bar [tasks]="m.tasks" [label]="'Module ' + m.number + ' task-card progress'" />
          <p>{{ m.active }} active or in review · {{ m.pending }} pending · {{ m.blocked }} blocked</p>
          <details><summary>View {{ filtered(m.tasks).length }} of {{ m.tasks.length }} task cards</summary><ul>@for (t of filtered(m.tasks); track t.id) { <li><strong>{{ t.id }}</strong> {{ t.title }} <audit-status [value]="t.status" />@if (t.status === 'BLOCKED' && t.blockedReason) { <small>{{ t.blockedReason }}</small> }</li> }</ul></details></section>
      }
      <section class="panel" aria-labelledby="audit-workflow-heading"><h2 id="audit-workflow-heading">Audit workflow</h2><audit-task-bar [tasks]="s.auditTasks" label="Audit workflow task-card progress" />
        <p>{{ s.auditCompleted }} / {{ s.auditTasks.length }} completed ({{ completionPercent(s.auditTasks) }}%) · {{ active(s.auditTasks) }} active or in review · {{ pending(s.auditTasks) }} pending · {{ blocked(s.auditTasks) }} blocked</p>
        <details><summary>View {{ filtered(s.auditTasks).length }} of {{ s.auditTasks.length }} task cards</summary><ul>@for (t of filtered(s.auditTasks); track t.id) { <li><strong>{{ t.id }}</strong> {{ t.title }} <audit-status [value]="t.status" />@if (t.status === 'BLOCKED' && t.blockedReason) { <small>{{ t.blockedReason }}</small> }</li> }</ul></details></section>
      <section aria-labelledby="audit-phases-heading"><h2 id="audit-phases-heading">Audit task phases</h2>
        @for (ph of s.auditPhases; track ph.number) {
          <section class="panel" [attr.aria-labelledby]="'audit-phase-' + ph.number"><h3 [id]="'audit-phase-' + ph.number">{{ ph.name }} <small>{{ ph.completed }} / {{ ph.tasks.length }} ({{ ph.completionPercent }}%)</small></h3><audit-task-bar [tasks]="ph.tasks" [label]="ph.name + ' task-card progress'" />
            <p>{{ ph.active }} active or in review · {{ ph.pending }} pending · {{ ph.blocked }} blocked</p>
            <details><summary>View {{ filtered(ph.tasks).length }} of {{ ph.tasks.length }} task cards</summary><ul>@for (t of filtered(ph.tasks); track t.id) { <li><strong>{{ t.id }}</strong> {{ t.title }} <audit-status [value]="t.status" />@if (t.status === 'BLOCKED' && t.blockedReason) { <small>{{ t.blockedReason }}</small> }</li> }</ul></details></section>
        }</section>
      <section class="panel" aria-labelledby="shared-foundation-heading"><h2 id="shared-foundation-heading">Shared and cross-module foundation</h2><audit-task-bar [tasks]="s.sharedTasks" label="Shared foundation task-card progress" />
        <p>{{ s.sharedCompleted }} / {{ s.sharedTasks.length }} completed ({{ completionPercent(s.sharedTasks) }}%) · {{ active(s.sharedTasks) }} active or in review · {{ pending(s.sharedTasks) }} pending · {{ blocked(s.sharedTasks) }} blocked</p>
        <details><summary>View {{ filtered(s.sharedTasks).length }} of {{ s.sharedTasks.length }} task cards</summary><ul>@for (t of filtered(s.sharedTasks); track t.id) { <li><strong>{{ t.id }}</strong> {{ t.title }} <audit-status [value]="t.status" />@if (t.status === 'BLOCKED' && t.blockedReason) { <small>{{ t.blockedReason }}</small> }</li> }</ul></details></section>
      <section aria-labelledby="untracked-modules-heading"><h2 id="untracked-modules-heading">Other application modules</h2>
        <p>These areas have no dedicated module mapping in the published task pack. Their completion cannot be measured here yet; operational records in the app are separate from implementation task status.</p>
        @for (a of d.untracked; track a.name) { <section class="panel"><h3>{{ a.name }} <small>Untracked</small></h3><div role="img" [attr.aria-label]="a.name + ': implementation progress not measured'" style="height:.75rem;border-radius:4px;background:#e3ebf3;margin:.5rem 0"></div><p>No module-specific progress mapping published.</p>@if (a.route && ownedRoute(a.route); as destination) { <a [routerLink]="destination">Open module</a> }</section> }</section>
    }
  `,
})
export class ProjectProgress {
  private readonly api = inject(Api);
  readonly p = this.api.resource(() => '/api/ui/administration/project-progress', decodeProgress, 'Only an authorized firm administrator can view the implementation tracker.');
  readonly filter = signal('All');
  readonly filters = FILTERS;
  readonly ownedRoute = workspaceRoute;
  completionPercent(tasks: Task[]): number { return tasks.length ? Math.floor(tasks.filter(t => t.status === 'COMPLETED').length * 100 / tasks.length) : 0; }
  active(tasks: Task[]): number { return tasks.filter(t => t.status === 'IN_PROGRESS' || t.status === 'IN_REVIEW').length; }
  pending(tasks: Task[]): number { return tasks.filter(t => t.status === 'NOT_STARTED' || t.status === 'REOPENED').length; }
  blocked(tasks: Task[]): number { return tasks.filter(t => t.status === 'BLOCKED').length; }
  filtered(tasks: Task[]): Task[] {
    const f = this.filter();
    return tasks.filter((t) => f === 'Completed' ? t.status === 'COMPLETED' : f === 'Active' ? t.status === 'IN_PROGRESS' || t.status === 'IN_REVIEW'
      : f === 'Pending' ? t.status === 'NOT_STARTED' || t.status === 'REOPENED' : f === 'Blocked' ? t.status === 'BLOCKED' : true);
  }
}
