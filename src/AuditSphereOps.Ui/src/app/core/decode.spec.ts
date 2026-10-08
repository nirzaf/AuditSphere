import { describe, expect, it } from 'vitest';
import { arr, dec, decimalInput, decode, guid, money, nullable, obj, oneOf, percent, str } from './decode';

describe('decoder kit', () => {
  const row = obj({ id: guid, amount: dec, status: oneOf('DRAFT', 'POSTED'), note: nullable(str(10)) });
  it('accepts declared shapes and keeps decimals as strings', () => {
    const v = decode(arr(row), [{ id: '11111111-1111-4111-8111-111111111111', amount: '12.3400', status: 'DRAFT', note: null }]);
    expect(v[0].amount).toBe('12.3400');
  });
  it('rejects numeric money, unknown states and oversized lists', () => {
    expect(() => decode(row, { id: '11111111-1111-4111-8111-111111111111', amount: 12.34, status: 'DRAFT', note: null })).toThrow();
    expect(() => decode(row, { id: '11111111-1111-4111-8111-111111111111', amount: '1', status: 'OTHER', note: null })).toThrow();
    expect(() => decode(arr(dec, 1), ['1', '2'])).toThrow();
  });
  it('formats exact decimals without floating point', () => {
    expect(money('12345678901234567890.125')).toBe('12,345,678,901,234,567,890.13');
    expect(money('-0.004')).toBe('0.00');
    expect(money('-1234.5')).toBe('-1,234.50');
    expect(money('999.995')).toBe('1,000.00');
    expect(money('0.1', 0)).toBe('0');
  });
  it('formats fractions as percentages exactly', () => {
    expect(percent('0.25')).toBe('25.00%');
    expect(percent('1')).toBe('100.00%');
    expect(percent('0.333333')).toBe('33.33%');
    expect(percent('-0.005')).toBe('-0.50%');
  });
  it('validates user decimal input', () => {
    expect(decimalInput('1,234.50')).toBe('1234.50');
    expect(decimalInput('1e5')).toBeNull();
    expect(decimalInput('1.123456')).toBeNull();
  });
});
