import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { SectionTabs } from './section-tabs';
import { pageIconFor } from './ui';

@Component({
  imports: [SectionTabs],
  template: `<audit-section-tabs [sections]="sections" label="Record sections" />
    <section id="first"><h2>First</h2></section><section id="second"><h2>Second</h2></section>`,
})
class Host {
  readonly sections = [{ id: 'first', label: 'One' }, { id: 'second', label: 'Two' }, { id: 'absent', label: 'Missing' }];
}

describe('record section tabs', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('keeps every section on the page and moves focus to the chosen one without changing the route', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const links = [...root.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Record sections"] a')];
    expect(links.map(a => a.textContent?.trim())).toEqual(['One', 'Two', 'Missing']);
    expect(root.querySelector('#first')).not.toBeNull();
    const before = location.href;
    links[1].click();
    fixture.detectChanges();
    expect(location.href).toBe(before);
    expect(document.activeElement?.id).toBe('second');
    expect(links[1].getAttribute('aria-current')).toBe('location');
    links[2].click();
    expect(location.href).toBe(before);
  });
});

describe('page header icon', () => {
  it('uses the icon of the matching sidebar item, or of the area for record pages', () => {
    expect(pageIconFor('/app')).toBe('layout-dashboard');
    expect(pageIconFor('/app/administration/users')).toBe('users-round');
    expect(pageIconFor('/app/finance/invoices?page=2')).toBe('receipt');
    expect(pageIconFor('/ui/app/accounting/mappings/123')).toBe('git-branch');
    expect(pageIconFor('/app/engagements/0247f5d8-4f57-4df5-ad3a-aff29b3a8517')).toBe('briefcase-business');
    expect(pageIconFor('/app/findings/abc')).toBe('file-search');
    expect(pageIconFor('/app/engagements/0247f5d8-4f57-4df5-ad3a-aff29b3a8517/general-ledger/upload')).toBe('calculator');
    expect(pageIconFor('/app/engagements/0247f5d8-4f57-4df5-ad3a-aff29b3a8517/audit-plan')).toBe('file-search');
    expect(pageIconFor('/portal/requests/1')).toBe('globe');
  });
});
