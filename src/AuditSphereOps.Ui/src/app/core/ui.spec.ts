import { afterEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { StatusChip } from './ui';

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
