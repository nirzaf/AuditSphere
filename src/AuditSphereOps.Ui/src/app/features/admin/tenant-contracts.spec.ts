import { decode } from '../../core/decode';
import { consentDestination, directoryPage, tenantWorkspace } from './tenant-contracts';

describe('Microsoft tenant contracts', () => {
  const tenant = '00000000-0000-0000-0000-000000000001';
  it('refuses unbounded directory import and incomplete identity metadata', () => {
    const user = { tenantId: tenant, objectId: tenant, displayName: 'Member', userPrincipalName: 'member@example.test', accountEnabled: true, userType: 'Member' };
    expect(() => decode(directoryPage, { users: Array(26).fill(user), nextPageToken: null })).toThrow();
    expect(() => decode(directoryPage, { users: [{ ...user, objectId: 'email@example.test' }], nextPageToken: null })).toThrow();
  });
  it('rejects another tenant, a lookalike host and external simulator redirects', () => {
    expect(() => consentDestination({ redirect: `https://login.microsoftonline.com/00000000-0000-0000-0000-000000000002/v2.0/adminconsent?state=test` }, tenant, false)).toThrow();
    expect(() => consentDestination({ redirect: `https://login.microsoftonline.com.evil.test/${tenant}/v2.0/adminconsent?state=test` }, tenant, false)).toThrow();
    expect(() => consentDestination({ redirect: 'https://other.test/auth/m365-consent/simulated-consent?state=test' }, tenant, true)).toThrow();
    expect(consentDestination({ redirect: `https://login.microsoftonline.com/${tenant}/v2.0/adminconsent?state=test` }, tenant, false)).toContain(tenant);
  });
  it('does not turn a consent callback into verified capability state', () => {
    expect(() => decode(tenantWorkspace, { consentConfigured: true, directoryConfigured: true, simulation: false, workspace: { connection: { consentState: 'RETURNED_UNVERIFIED' } } })).toThrow();
  });
});
