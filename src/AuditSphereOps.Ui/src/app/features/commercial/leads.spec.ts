import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
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
});
