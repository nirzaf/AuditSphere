import { describe, expect, it } from 'vitest';
import { roleChoices } from './roles';

describe('Role assignment identity choices', () => {
  it('offers only client roles with explicit client or engagement scope for a guest client', () => {
    const choices = roleChoices('Client (Guest)', ['Administrator', 'Staff', 'ClientUser']);
    expect(choices.roles).toEqual(['ClientUser']);
    expect(choices.scopes).toEqual(['CLIENT', 'ENGAGEMENT']);
    expect(choices.initialRole).toBe('ClientUser');
  });
  it('keeps staff roles separate from client identities', () => {
    expect(roleChoices('Staff (Member)', ['Administrator', 'Staff', 'ClientUser']).roles).toEqual(['Administrator', 'Staff']);
  });
});
