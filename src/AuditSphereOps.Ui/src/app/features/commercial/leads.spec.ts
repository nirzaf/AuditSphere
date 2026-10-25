import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { Drafts } from '../../core/drafts';
import { Leads, decodeLeads, isLeadChannel, LEAD_CHANNELS } from './leads';
describe('Commercial lead contract', () => {
  it('accepts bounded empty result', () => {
    expect(decodeLeads({ items: [], total: 0, page: 0, pageSize: 25 }).total).toBe(0);
  });
  it('rejects unbounded page and malformed identity', () => {
    expect(() => decodeLeads({ items: [], total: 0, page: 0, pageSize: 101 })).toThrow();
    expect(() =>
      decodeLeads({ items: [{ id: 'email@example.test' }], total: 1, page: 0, pageSize: 25 }),
    ).toThrow();
  });
  it('accepts complete summaries only when they describe the same cohort as the total', () => {
    const page = (summary: unknown) => ({ items: [], total: 40, page: 0, pageSize: 25, summary });
    expect(decodeLeads(page({ matching: 40, awaitingQualification: 30, qualified: 5 })).summary?.awaitingQualification).toBe(30);
    expect(() => decodeLeads(page({ matching: 39, awaitingQualification: 1, qualified: 1 }))).toThrow();
    expect(() => decodeLeads(page({ matching: 40, awaitingQualification: 35, qualified: 6 }))).toThrow();
    expect(() => decodeLeads(page({ matching: 40, awaitingQualification: -1, qualified: 0 }))).toThrow();
  });
  it('rejects an unknown owner state or malformed owner identity', () => {
    const lead = { id: '00000000-0000-0000-0000-000000000001', name: 'L', source: 'Email', primaryContactName: null,
      primaryContactEmail: null, status: 'NEW', createdAt: '2026-10-16T00:00:00Z' };
    expect(() => decodeLeads({ items: [{ ...lead, ownerState: 'GUESSED' }], total: 1, page: 0, pageSize: 25 })).toThrow();
    expect(() => decodeLeads({ items: [{ ...lead, ownerUserId: 'me' }], total: 1, page: 0, pageSize: 25 })).toThrow();
    expect(decodeLeads({ items: [{ ...lead, ownerState: 'UNASSIGNED', ownerUserId: null }], total: 1, page: 0, pageSize: 25 })
      .items[0].ownerState).toBe('UNASSIGNED');
  });
});

describe('Lead channels (STE 4.1.1)', () => {
  it('offers exactly the approved channel set', () => {
    expect([...LEAD_CHANNELS]).toEqual(['Phone', 'WhatsApp', 'Email', 'Web Form', 'Referral', 'In-person']);
  });

  it('accepts only approved channels and rejects free text', () => {
    expect(isLeadChannel('Referral')).toBe(true);
    expect(isLeadChannel('Web Form')).toBe(true);
    expect(isLeadChannel('Web')).toBe(false);
    expect(isLeadChannel('web form')).toBe(false);
    expect(isLeadChannel('')).toBe(false);
  });
});


describe('Lead qualification exact confirmation and denial', () => {
  const id = '00000000-0000-0000-0000-000000000001', other = '00000000-0000-0000-0000-000000000002';
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: SessionService, useValue: { current: signal({ staff: true }), invalidation: signal(0), clear: vi.fn() } },
      { provide: Drafts, useValue: { load: () => null, clear: vi.fn() } },
    ] }); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function open() {
    const c = TestBed.runInInjectionContext(() => new Leads()); TestBed.tick();
    const lead = { id, name: 'Synthetic lead', source: 'Email', primaryContactName: null,
      primaryContactEmail: null, status: 'NEW', createdAt: '2026-10-16T00:00:00Z' };
    http.expectOne(r => r.url === '/api/ui/leads').flush({ items: [lead], total: 1, page: 0, pageSize: 25 });
    return { c, lead };
  }
  function renderLeads() {
    const fixture = TestBed.createComponent(Leads);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(r => r.url === '/api/ui/leads').flush({
      items: [{ id, name: 'Synthetic lead', source: 'Email', primaryContactName: null,
        primaryContactEmail: null, status: 'NEW', createdAt: '2026-10-16T00:00:00Z' }],
      total: 1, page: 0, pageSize: 25,
    });
    fixture.detectChanges();
    return fixture;
  }
  it('shows complete server counts and labels a page-only fallback honestly', () => {
    const fixture = TestBed.createComponent(Leads);
    fixture.detectChanges(); TestBed.tick();
    const lead = { id, name: 'Synthetic lead', source: 'Email', primaryContactName: null,
      primaryContactEmail: null, status: 'NEW', createdAt: '2026-10-16T00:00:00Z', ownerState: 'UNASSIGNED' };
    http.expectOne(r => r.url === '/api/ui/leads').flush({ items: [lead], total: 30, page: 0, pageSize: 25,
      summary: { matching: 30, awaitingQualification: 7, qualified: 4 } });
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('New · awaiting qualification');
    expect(text).toMatch(/New · awaiting qualification\s*7/);
    expect(text).toContain('not only this page');
    expect(text).toContain('Unassigned');
    fixture.componentInstance.load();
    http.expectOne(r => r.url === '/api/ui/leads').flush({ items: [lead], total: 30, page: 0, pageSize: 25 });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toMatch(/New on this page\s*1/);
  });
  it('sends only the chosen filters, never widening a malformed choice', () => {
    const { c } = open();
    c.statusFilter = 'NEW'; c.ownerFilter = 'me'; c.load();
    const request = http.expectOne(r => r.url === '/api/ui/leads');
    expect(request.request.params.get('status')).toBe('NEW');
    expect(request.request.params.get('owner')).toBe('me');
    expect(request.request.params.has('source')).toBe(false);
    request.flush({ items: [], total: 0, page: 0, pageSize: 25, summary: { matching: 0, awaitingQualification: 0, qualified: 0 } });
    expect(c.createOpen()).toBe(false);
    c.clearFilters();
    const cleared = http.expectOne(r => r.url === '/api/ui/leads');
    expect(cleared.request.params.has('status')).toBe(false);
    cleared.flush({ items: [], total: 0, page: 0, pageSize: 25 });
    expect(c.createOpen()).toBe(true);
  });
  it('refuses an unconfirmed or different lead identity without dispatch', () => {
    const { c, lead } = open(); c.qualify(id); c.qualification.set(lead); c.qualify(other);
    http.expectNone(r => r.method === 'POST');
  });
  it('clears protected rows and confirmation on command-time scope denial without retry', () => {
    const { c, lead } = open(); c.qualification.set(lead); c.qualify(id);
    http.expectOne('/api/ui/leads/' + id + '/qualify').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(c.data()).toBeNull(); expect(c.qualification()).toBeNull(); c.qualify(id);
    http.expectNone(r => r.method === 'POST');
  });
  it('unsubscribes a pending qualification when its injection lifetime ends', () => {
    const { c, lead } = open(); c.qualification.set(lead); c.qualify(id);
    const request = http.expectOne('/api/ui/leads/' + id + '/qualify'); TestBed.resetTestingModule();
    expect(request.cancelled).toBe(true); expect(c.commandStatus()).not.toBe('Commercial change recorded.');
  });
  it('exposes the leads table as a named keyboard-scroll region', () => {
    const fixture = renderLeads();
    const region = fixture.nativeElement.querySelector('.table-scroll') as HTMLDivElement;
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('aria-label')).toBe('Commercial leads; scroll horizontally to see all columns');
    expect(region.tabIndex).toBe(0);
    region.focus();
    expect(document.activeElement).toBe(region);

    const qualifyButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Qualify Synthetic lead') as HTMLButtonElement;
    qualifyButton.focus();
    qualifyButton.click();
    fixture.detectChanges();
    const cancelButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Cancel') as HTMLButtonElement;
    cancelButton.focus();
    cancelButton.click();
    fixture.detectChanges();
    expect(document.activeElement).toBe(qualifyButton);

    qualifyButton.focus();
    qualifyButton.click();
    fixture.detectChanges();
    const confirmButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Confirm qualification') as HTMLButtonElement;
    confirmButton.focus();
    confirmButton.click();
    const command = http.expectOne('/api/ui/leads/' + id + '/qualify');
    command.flush({});
    http.expectOne((r) => r.url === '/api/ui/leads').flush({
      items: [{ id, name: 'Synthetic lead', source: 'Email', primaryContactName: null,
        primaryContactEmail: null, status: 'QUALIFIED', createdAt: '2026-10-16T00:00:00Z' }],
      total: 1, page: 0, pageSize: 25,
    });
    fixture.detectChanges(); TestBed.tick();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#leads-page-heading'));
    fixture.destroy();
  });
  it('clears qualification at refresh start and does not restore it after a denied read and retry', () => {
    const fixture = renderLeads();
    const component = fixture.componentInstance;
    const qualifyButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Qualify Synthetic lead') as HTMLButtonElement;
    qualifyButton.click();
    fixture.detectChanges();
    expect(component.qualification()?.id).toBe(id);

    component.load();
    expect(component.qualification()).toBeNull();
    http.expectOne((r) => r.url === '/api/ui/leads').flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    const retry = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Retry') as HTMLButtonElement;
    retry.click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/ui/leads').flush({
      items: [{ id, name: 'Synthetic lead', source: 'Email', primaryContactName: null,
        primaryContactEmail: null, status: 'NEW', createdAt: '2026-10-16T00:00:00Z' }],
      total: 1, page: 0, pageSize: 25,
    });
    fixture.detectChanges();
    expect(component.qualification()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Confirm qualification');
    fixture.destroy();
  });
  it('does not move focus to the page heading after qualification if the user interacts elsewhere', () => {
    const fixture = renderLeads();
    const qualifyButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Qualify Synthetic lead') as HTMLButtonElement;
    qualifyButton.click();
    fixture.detectChanges();
    const confirmButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Confirm qualification') as HTMLButtonElement;
    confirmButton.focus();
    confirmButton.click();
    const command = http.expectOne('/api/ui/leads/' + id + '/qualify');

    const outsideButton = document.createElement('button');
    outsideButton.textContent = 'Other action';
    document.body.append(outsideButton);
    outsideButton.focus();
    outsideButton.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    command.flush({});
    http.expectOne((r) => r.url === '/api/ui/leads').flush({
      items: [{ id, name: 'Synthetic lead', source: 'Email', primaryContactName: null,
        primaryContactEmail: null, status: 'QUALIFIED', createdAt: '2026-10-16T00:00:00Z' }],
      total: 1, page: 0, pageSize: 25,
    });
    fixture.detectChanges(); TestBed.tick();
    expect(document.activeElement).toBe(outsideButton);
    outsideButton.remove();
    fixture.destroy();
  });
});
