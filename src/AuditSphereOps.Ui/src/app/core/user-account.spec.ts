import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { UserAccount } from './user-account';
import { SessionService } from './session';

describe('signed-in account profile', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('shows the current identity, routes profile management to Microsoft, and clears on identity loss', async () => {
    TestBed.configureTestingModule({ imports: [UserAccount], providers: [provideHttpClient(), provideRouter([{ path: '**', children: [] }])] });
    const session = TestBed.inject(SessionService);
    const account = { userId: '00000000-0000-0000-0000-000000000001', firmId: '00000000-0000-0000-0000-000000000010',
      generation: '1', staff: true, displayName: 'Alex Reviewer', email: 'alex@example.invalid', microsoftAccount: true };
    session.current.set(account);
    const fixture = TestBed.createComponent(UserAccount);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('summary')?.textContent).toContain('Alex Reviewer');
    const details = root.querySelector('details')!;
    details.open = true;
    expect(root.querySelector('.profile')?.textContent).toContain('alex@example.invalid');
    const microsoft = root.querySelector('a[target="_blank"]');
    expect(microsoft?.getAttribute('href')).toBe('https://myaccount.microsoft.com/personal-info');
    expect(microsoft?.getAttribute('rel')).toContain('noreferrer');
    // Staff open their own AuditSphere profile from the same menu, which then closes.
    const profile = [...root.querySelectorAll('a')].find(a => a.textContent?.includes('View and update your profile'));
    expect(profile?.getAttribute('href')).toBe('/app/profile');
    profile!.click();
    expect(details.open).toBe(false);
    details.open = true;
    details.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(details.open).toBe(false);
    expect(document.activeElement).toBe(root.querySelector('summary'));
    details.open = true;
    session.current.set({ ...account, userId: '00000000-0000-0000-0000-000000000002', displayName: 'Sam Client',
      email: 'sam@example.invalid', staff: false, microsoftAccount: false });
    await fixture.whenStable();
    expect(details.open).toBe(false);
    expect(root.textContent).not.toContain('alex@example.invalid');
    expect(root.querySelector('a')).toBeNull();
    expect(root.textContent).toContain('AuditSphere administrator');
    session.clear();
    await fixture.whenStable();
    expect(root.querySelector('details')).toBeNull();
    fixture.destroy();
  });
});
