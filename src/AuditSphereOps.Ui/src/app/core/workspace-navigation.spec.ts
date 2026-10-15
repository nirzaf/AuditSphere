import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { WorkspaceNavigation } from './workspace-navigation';

@Component({ template: '' })
class Blank {}

const TITLES = ['Administration', 'Practice', 'Accounting', 'Consolidation', 'Audit'];
const ENGAGEMENT = '/app/engagements/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('workspace navigation group highlighting', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['/app', ['Practice']],
    ['/app/work', ['Practice']],
    ['/app/finance/books', ['Practice']],
    ['/app/accounting/mappings', ['Accounting']],
    ['/app/accounting/remeasurement', ['Accounting']],
    ['/app/consolidation/scopes/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', ['Consolidation']],
    [`${ENGAGEMENT}`, ['Practice']],
    [`${ENGAGEMENT}/tb-intake`, ['Accounting']],
    [`${ENGAGEMENT}/general-ledger`, ['Accounting']],
    [`${ENGAGEMENT}/audit-plan`, ['Audit']],
    ['/app/audit/library', ['Audit']],
    ['/app/administration/users', ['Administration']],
  ])('highlights the owning group for %s', async (url, expected) => {
    TestBed.configureTestingModule({
      imports: [WorkspaceNavigation],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', component: Blank }])],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const nav = TestBed.createComponent(WorkspaceNavigation).componentInstance;
    expect(TITLES.filter(title => nav.isCurrentGroup(title))).toEqual(expected);
  });
});
