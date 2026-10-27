import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { WorkspaceNavigation } from './workspace-navigation';
import { NAVIGATION } from '../navigation';

@Component({ template: '' })
class Blank {}

const GROUP_IDS = NAVIGATION.map(group => group.id);
const ENGAGEMENT = '/app/engagements/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('workspace navigation group highlighting', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['/app', ['work']],
    ['/app/work', ['work']],
    ['/app/finance/books', ['firm-finance']],
    ['/app/accounting/mappings', ['execution']],
    ['/app/accounting/remeasurement', ['execution']],
    ['/app/consolidation/scopes/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', ['execution']],
    [`${ENGAGEMENT}`, ['planning']],
    [`${ENGAGEMENT}/tb-intake`, ['execution']],
    [`${ENGAGEMENT}/general-ledger`, ['execution']],
    [`${ENGAGEMENT}/audit-plan`, ['planning']],
    [`${ENGAGEMENT}/pbc`, ['execution']],
    [`${ENGAGEMENT}/confirmations`, ['execution']],
    ['/app/accounting/reviews', ['review-delivery']],
    ['/app/audit/library', ['reference-admin']],
    ['/app/administration/users', ['reference-admin']],
  ])('highlights the owning group for %s', async (url, expected) => {
    TestBed.configureTestingModule({
      imports: [WorkspaceNavigation],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', component: Blank }])],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const nav = TestBed.createComponent(WorkspaceNavigation).componentInstance;
    expect(GROUP_IDS.filter(id => nav.isCurrentGroup(id))).toEqual(expected);
  });
});
