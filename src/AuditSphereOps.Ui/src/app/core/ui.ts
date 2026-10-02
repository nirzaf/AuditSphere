import { Component, Pipe, PipeTransform, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { money, percent } from './decode';

/** Exact decimal string → grouped display; never converts through floating point. */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: string | null | undefined, places = 2): string {
    return money(value, places);
  }
}

@Pipe({ name: 'percent' })
export class PercentPipe implements PipeTransform {
  transform(value: string | null | undefined, places = 2): string {
    return percent(value, places);
  }
}

@Component({
  selector: 'audit-page-header',
  template: `<header>
    @if (eyebrow()) { <p class="eyebrow">{{ eyebrow() }}</p> }
    <h1>{{ title() }}</h1>
    @if (description()) { <p>{{ description() }}</p> }
    <ng-content />
  </header>`,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly eyebrow = input('');
  readonly description = input('');
}

/** Loading / error / empty states shared by every migrated page. */
@Component({
  selector: 'audit-state',
  imports: [MatProgressBarModule],
  template: `@if (loading()) { <mat-progress-bar mode="indeterminate" [attr.aria-label]="'Loading ' + label()" /> }
    @if (error()) { <section role="alert" class="state-error"><p>{{ error() }}</p></section> }`,
})
export class StateBlock {
  readonly loading = input(false);
  readonly error = input('');
  readonly label = input('content');
}

@Component({
  selector: 'audit-status',
  template: `<span class="status-chip" [attr.data-status]="value()">{{ label() }}</span>`,
})
export class StatusChip {
  readonly value = input.required<string>();
  protected label(): string {
    return this.value().replace(/_/g, ' ').toLowerCase();
  }
}

@Component({
  selector: 'audit-command-message',
  template: `@if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'" [class.error-text]="failed()" class="command-result">{{ message() }}</p> }`,
})
export class CommandMessage {
  readonly message = input('');
  readonly failed = input(false);
}

export const SHARED = [MoneyPipe, PercentPipe, PageHeader, StateBlock, StatusChip, CommandMessage] as const;
