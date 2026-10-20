import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { decodeTemplateCatalog, decodeTemplateRevision, WorkflowTemplates } from './workflow-templates';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const other = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const revisionId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const catalog = [{ templateId: id, latestRevisionId: revisionId, version: 1, name: 'Annual audit',
  serviceRoute: 'FinancialStatementAudit', industryKey: null, revisionStatus: 'DRAFT', archived: false,
  createdAt: '2026-10-12T07:00:00Z' }];
const revision = {
  templateId: id, revisionId, version: 1, name: 'Annual audit', serviceRoute: 'FinancialStatementAudit',
  industryKey: null, status: 'DRAFT', contentDigest: 'a'.repeat(64), revision: 1,
  createdByUserId: id, lastEditedByUserId: id, reviewedByUserId: null,
  createdAt: '2026-10-12T07:00:00Z', updatedAt: '2026-10-12T07:00:00Z', publishedAt: null, archivedAt: null,
  steps: [{ id: other, stableKey: 'planning', phase: 'AUDIT_PLANNING', title: 'Approve the plan',
    instructions: 'Review the current plan.', suggestedStaffingLevel: 'AUDIT_MANAGER', estimatedEffortMinutes: 60,
    dueDateOffsetDays: 14, pbcRequestReference: null, requiredTask: true, sortOrder: 1, dependsOnStepKeys: [],
    taskGroupName: 'Planning' }],
};

describe('workflow template contract', () => {
  it('decodes a firm catalog and exact revision steps', () => {
    expect(decodeTemplateCatalog(catalog, 'catalog')[0].version).toBe(1);
    expect(decodeTemplateRevision(revision, 'revision').steps[0].dueDateOffsetDays).toBe(14);
    expect(decodeTemplateRevision(revision, 'revision').steps[0].taskGroupName).toBe('Planning');
    expect(decodeTemplateRevision(revision, 'revision').steps[0].evidenceRequirements).toEqual([]);
    expect(() => decodeTemplateRevision({ ...revision, steps: [{ ...revision.steps[0], evidenceRequirements: [{ requirementKey: 'evidence', kind: 'AUTOMATIC_APPROVAL' }] }] }, 'revision')).toThrow();
    const requirement = { requirementKey: 'materiality', kind: 'CURRENT_MATERIALITY_APPROVED' };
    expect(() => decodeTemplateRevision({ ...revision, steps: [{ ...revision.steps[0], evidenceRequirements: [requirement, requirement] }] }, 'revision')).toThrow();
    expect(() => decodeTemplateRevision({ ...revision, revision: '1' }, 'revision')).toThrow();
  });
});

describe('workflow template authoring screen', () => {
  function setup(currentUserId = id) {
    TestBed.configureTestingModule({
      imports: [WorkflowTemplates],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(SessionService).current.set({ userId: currentUserId, firmId: id, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(WorkflowTemplates);
    fixture.detectChanges();
    http.expectOne('/api/ui/practice/workflow-templates').flush(catalog);
    fixture.detectChanges();
    return { fixture, http, component: fixture.componentInstance };
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('shows review only to a different user than the draft author and editor', () => {
    const { fixture, http } = setup();
    const templateButton = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find(button => button.textContent?.includes('Annual audit'))!;
    templateButton.click();
    fixture.detectChanges();
    http.expectOne(`/api/ui/practice/workflow-templates/${id}/revisions/${revisionId}`).flush(revision);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Edit draft');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Task group: Planning');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Review draft');
  });

  it('offers independent review to a different user', () => {
    const { fixture, http } = setup(other);
    const templateButton = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find(button => button.textContent?.includes('Annual audit'))!;
    templateButton.click();
    fixture.detectChanges();
    http.expectOne(`/api/ui/practice/workflow-templates/${id}/revisions/${revisionId}`).flush(revision);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Review draft');
  });

  it('moves focus into a new draft and returns it to the opener on cancel', async () => {
    const { fixture } = setup();
    const host = fixture.nativeElement as HTMLElement;
    const trigger = host.querySelector<HTMLButtonElement>('#new-template-trigger')!;
    trigger.focus();
    trigger.click();
    await fixture.whenStable();

    const heading = host.querySelector<HTMLElement>('#revision-heading')!;
    expect(trigger.ownerDocument.activeElement).toBe(heading);

    const cancel = [...host.querySelectorAll('button')].find(button => button.textContent?.trim() === 'Cancel')!;
    cancel.focus();
    cancel.click();
    await fixture.whenStable();

    expect(trigger.ownerDocument.activeElement).toBe(trigger);
  });

  it('sends a new template as an unapproved draft with structured task metadata', async () => {
    const { fixture, http, component } = setup();
    component.startNewTemplate();
    component.draft.name = 'Annual audit';
    component.draft.serviceRoute = 'FinancialStatementAudit';
    component.draft.steps[0].title = 'Approve the plan';
    component.draft.steps[0].taskGroupName = '  Planning  ';
    component.draft.steps[0].evidenceRequirements = [{ requirementKey: ' materiality ', kind: 'CURRENT_MATERIALITY_APPROVED' }];
    const saving = component.saveDraft();
    const request = http.expectOne('/api/ui/practice/workflow-templates/drafts');
    expect(request.request.body.templateId).toBeNull();
    expect(request.request.body.expectedRevision).toBeNull();
    expect(request.request.body.steps[0]).toMatchObject({
      stableKey: 'step-1', phase: 'AUDIT_FIELDWORK', title: 'Approve the plan',
      requiredTask: true, sortOrder: 1, dependsOnStepKeys: [],
      taskGroupName: 'Planning', evidenceRequirements: [{ requirementKey: 'materiality', kind: 'CURRENT_MATERIALITY_APPROVED' }],
    });
    request.flush({ value: revisionId });
    await saving;
    const refresh = http.expectOne('/api/ui/practice/workflow-templates');
    refresh.flush(catalog);
    fixture.detectChanges();
    http.expectOne(`/api/ui/practice/workflow-templates/${id}/revisions/${revisionId}`).flush(revision);
    fixture.detectChanges();
    expect(component.selectedTemplate()).toBe(id);
  });
});
