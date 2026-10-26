import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { decodeUserProfile, UserProfilePage } from './profile';

const userId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const profile = (extra: object = {}) => ({
  userId, userType: 'Staff', roles: ['Manager', 'Staff'], fullName: 'Alex Reviewer', email: 'alex@example.invalid',
  position: null, gender: null, dateOfBirth: null, mobileNumber: null, joinedDate: null, revision: 0, updatedAt: null, ...extra,
});

describe('user profile contract', () => {
  it('refuses values outside the published profile contract', () => {
    expect(decodeUserProfile(profile({ gender: 'FEMALE', dateOfBirth: '1990-05-01' }), '').gender).toBe('FEMALE');
    expect(() => decodeUserProfile(profile({ gender: 'UNSPECIFIED' }), '')).toThrow();
    expect(() => decodeUserProfile(profile({ userType: 'Admin' }), '')).toThrow();
  });
});

describe('user profile page', () => {
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });

  function setup() {
    TestBed.configureTestingModule({
      imports: [UserProfilePage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true, microsoftAccount: true });
    const fixture = TestBed.createComponent(UserProfilePage);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), root: fixture.nativeElement as HTMLElement };
  }

  it('shows identity details as required and saves only the optional details with the loaded revision', async () => {
    const { fixture, http, root } = setup();
    http.expectOne('/api/ui/me/profile').flush(profile());
    fixture.detectChanges();
    const facts = root.querySelector('dl.facts')!.textContent!;
    expect(facts).toContain('Firm staff');
    expect(facts).toContain('Manager, Staff');
    expect(facts).toContain('Full nameRequired');
    expect(facts).toContain('alex@example.invalid');
    expect(root.querySelector('a[target="_blank"]')?.getAttribute('href')).toBe('https://myaccount.microsoft.com/personal-info');
    const page = fixture.componentInstance;
    expect(page.confirmNavigation()).toBe(true);
    page.form.position = '  Audit Senior ';
    page.form.mobileNumber = '+974 5555 1234';
    page.form.joinedDate = '2020-02-03';
    expect(page.dirty()).toBe(true);
    // A session refresh reloads the profile; it must not wipe the edits or move the save onto a later revision.
    page.view.reload();
    http.expectOne('/api/ui/me/profile').flush(profile({ revision: 5, position: 'Changed elsewhere' }));
    fixture.detectChanges();
    expect(page.form.position).toBe('  Audit Senior ');
    const saving = page.save();
    const request = http.expectOne('/api/ui/me/profile');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ position: 'Audit Senior', gender: null, dateOfBirth: null,
      mobileNumber: '+974 5555 1234', joinedDate: '2020-02-03', expectedRevision: 0 });
    request.flush({ value: profile({ position: 'Audit Senior', mobileNumber: '+974 5555 1234', joinedDate: '2020-02-03',
      revision: 1, updatedAt: '2026-10-26T17:30:00Z' }) });
    await saving;
    fixture.detectChanges();
    expect(page.dirty()).toBe(false);
    expect(page.current()?.revision).toBe(1);
    expect(root.textContent).toContain('Profile saved.');
    expect(root.textContent).toContain('Last updated 2026-10-26 17:30 UTC.');
  });

  it('keeps edits and offers a reload when another window saved first', async () => {
    const { fixture, http, root } = setup();
    http.expectOne('/api/ui/me/profile').flush(profile({ revision: 3 }));
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.form.position = 'Manager';
    const saving = page.save();
    http.expectOne('/api/ui/me/profile').flush({ code: 'revision.stale', message: 'Changed.' }, { status: 409, statusText: 'Conflict' });
    await saving;
    fixture.detectChanges();
    expect(page.form.position).toBe('Manager');
    const reload = [...root.querySelectorAll('button')].find(b => b.textContent?.includes('Reload profile'))!;
    reload.click();
    http.expectOne('/api/ui/me/profile').flush(profile({ revision: 4, position: 'Partner' }));
    fixture.detectChanges();
    expect(page.form.position).toBe('Partner');
  });

  it('never shows a profile that belongs to a different identity', () => {
    const { fixture, http, root } = setup();
    http.expectOne('/api/ui/me/profile').flush(profile({ userId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb' }));
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('does not match the signed-in account');
    expect(root.querySelector('form')).toBeNull();
  });
});
