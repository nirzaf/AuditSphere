import { describe, expect, it } from 'vitest';
import { displayUtcInstant } from './overview';

describe('consolidation report timestamps', () => {
  it('shows the approval instant in UTC with an explicit timezone label', () => {
    expect(displayUtcInstant('2026-10-07T10:00:00Z')).toBe('2026-10-07 10:00 UTC');
    expect(displayUtcInstant('2026-10-07T13:00:00+03:00')).toBe('2026-10-07 10:00 UTC');
    expect(displayUtcInstant(null)).toBe('');
  });
});
