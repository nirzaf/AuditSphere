import { afterEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { codeLabel, StatusChip, utcTime } from './ui';

describe('shared status chip', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['VERIFIED', 'success', 'verified'],
    ['BLOCKED_EXTERNAL', 'error', 'blocked external'],
    ['STALE', 'warning', 'stale'],
    ['AWAITING REVIEW', 'info', 'awaiting review'],
    ['CUSTOM_STATE', 'neutral', 'custom state'],
  ])('keeps the readable status label and assigns the %s tone', (status, tone, label) => {
    TestBed.configureTestingModule({ imports: [StatusChip] });
    const fixture = TestBed.createComponent(StatusChip);
    fixture.componentRef.setInput('value', status);
    fixture.detectChanges();

    const chip = fixture.nativeElement.querySelector('.status-chip') as HTMLElement;
    expect(chip.textContent?.trim()).toBe(label);
    expect(chip.getAttribute('data-tone')).toBe(tone);
    expect(chip.getAttribute('data-status')).toBe(status);
    fixture.destroy();
  });
});

describe('shared display labels', () => {
  it('turns stored codes into plain words and leaves ordinary text alone', () => {
    expect(codeLabel('NOT_VERIFIED')).toBe('Not verified');
    expect(codeLabel('IN_PROGRESS')).toBe('In progress');
    expect(codeLabel('RelationshipManager')).toBe('Relationship Manager');
    expect(codeLabel('Staff')).toBe('Staff');
    expect(codeLabel('Engagement letter not issued.')).toBe('Engagement letter not issued.');
    expect(codeLabel(null)).toBe('—');
  });

  it('shows stored instants in UTC without inventing a time for bad input', () => {
    expect(utcTime('2026-10-27T03:16:13.153966+00:00')).toBe('27 Oct 2026, 03:16 UTC');
    expect(utcTime('2026-10-26T23:30:00-05:00')).toBe('27 Oct 2026, 04:30 UTC');
    expect(utcTime('not a time')).toBe('not a time');
    expect(utcTime(null)).toBe('—');
  });
});
