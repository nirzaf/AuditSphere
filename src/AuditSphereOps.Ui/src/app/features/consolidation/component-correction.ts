import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { SHARED } from '../../core/ui';
import { decodeScopeWorkspace } from './scope-contracts';
import type { ConsolidationComponent, ScopeSummary } from './scope-contracts';

type CorrectionSource =
  | { state: 'invalid' | 'loading' | 'unavailable' | 'stale-scope' | 'wrong-group' | 'unknown-component' }
  | { state: 'ready'; scope: ScopeSummary; component: ConsolidationComponent };

@Component({
  selector: 'audit-consolidation-component-correction',
  imports: [RouterLink, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="component-correction-heading">
      <h3 id="component-correction-heading">Correct this component</h3>
      <p>A source correction is recorded in the entity accounting workflow and reviewed as a new component package. This scope’s component and package evidence remain unchanged.</p>
      @if (source().state === 'invalid') {
        <p role="alert">The correction source identity is missing or invalid. Reopen this handoff from a current authorized scope.</p>
      } @else {
        <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="the original consolidation component evidence" />
        @let current = source();
        @switch (current.state) {
          @case ('invalid') { <p role="alert">The correction source identity is missing or invalid. Reopen this handoff from a current authorized scope.</p> }
          @case ('loading') { <p role="status">Loading the original scope evidence…</p> }
          @case ('unavailable') {
            @if (!workspace.error()) { <p role="alert">The source scope is unavailable in the current authorization context. Reopen this handoff from an authorized scope.</p> }
          }
          @case ('stale-scope') { <p role="alert">The fetched scope identity no longer matches this handoff. Reopen it from the current scope workspace.</p> }
          @case ('wrong-group') { <p role="alert">The fetched scope belongs to a different group than this handoff. No correction link is available.</p> }
          @case ('unknown-component') { <p role="alert">This component identity is not present in the current authorized source scope. The handoff may be stale; reopen it from the scope workspace.</p> }
          @case ('ready') {
            <h4>Exact original scope and component evidence</h4>
            <dl>
              <dt>Group</dt><dd>{{ current.scope.groupName }} · {{ current.scope.groupCode }} · <code>{{ current.scope.groupId }}</code></dd>
              <dt>Scope version</dt><dd>v{{ current.scope.version }} · <code>{{ current.scope.id }}</code></dd>
              <dt>Scope period identity</dt><dd><code>{{ current.scope.periodId }}</code></dd>
              <dt>Scope status</dt><dd><code>{{ current.scope.status }}</code></dd>
              <dt>Component identity</dt><dd><code>{{ current.component.id }}</code></dd>
              <dt>Entity</dt><dd>{{ current.component.clientLegalName }} · <code>{{ current.component.clientId }}</code></dd>
              <dt>Source type</dt><dd><code>{{ current.component.sourceType }}</code></dd>
              <dt>Internal package ID</dt><dd><code>{{ current.component.packageId ?? 'Not applicable' }}</code></dd>
              <dt>External component-pack ID</dt><dd><code>{{ current.component.externalComponentPackId ?? 'Not applicable' }}</code></dd>
              <dt>Package hash</dt><dd><code>{{ current.component.packageHash }}</code></dd>
              <dt>Period basis</dt><dd><code>{{ current.component.periodBasis }}</code></dd>
              <dt>Taxonomy version</dt><dd><code>{{ current.component.taxonomyVersion }}</code></dd>
              <dt>Mapping version</dt><dd><code>{{ current.component.mappingVersion }}</code></dd>
              <dt>Currency</dt><dd><code>{{ current.component.currency }}</code></dd>
              <dt>Ownership percentage</dt><dd><code>{{ current.component.ownershipPercent }}</code></dd>
              <dt>Control method</dt><dd><code>{{ current.component.controlMethod }}</code></dd>
              <dt>Component status</dt><dd><code>{{ current.component.status }}</code></dd>
              <dt>Submitted by / at</dt><dd><code>{{ current.component.submittedByUserId }} · {{ current.component.submittedAt }}</code></dd>
              <dt>Approved by / at</dt><dd><code>{{ current.component.approvedByUserId ?? 'Not approved' }} · {{ current.component.approvedAt ?? 'Not approved' }}</code></dd>
            </dl>
            <p><a [routerLink]="['/app/consolidation']" [queryParams]="{ correctionScopeId: current.scope.id, correctionGroupId: current.scope.groupId, correctionComponentId: current.component.id }">Open the authorized new-scope correction handoff</a></p>
          }
        }
      }
    </section>
  `,
})
export class ConsolidationComponentCorrection {
  private readonly api = inject(Api);
  readonly scopeId = input.required<string>();
  readonly groupId = input.required<string>();
  readonly componentId = input.required<string>();

  private readonly identity = computed(() => {
    const scopeId = this.scopeId();
    const groupId = this.groupId();
    const componentId = this.componentId();
    return guidPattern.test(scopeId) && guidPattern.test(groupId) && guidPattern.test(componentId)
      ? { scopeId: scopeId.toLowerCase(), groupId: groupId.toLowerCase(), componentId: componentId.toLowerCase() }
      : null;
  });

  readonly workspace = this.api.resource(
    () => {
      const identity = this.identity();
      return identity ? `/api/ui/consolidation/scopes/${identity.scopeId}?selectedComponentId=${identity.componentId}` : null;
    },
    decodeScopeWorkspace,
    'The original consolidation scope is unavailable in the current authorization context.',
  );

  readonly source = computed<CorrectionSource>(() => {
    const identity = this.identity();
    if (!identity) return { state: 'invalid' };
    const workspace = this.workspace.data();
    if (!workspace) return this.workspace.loading() ? { state: 'loading' } : { state: 'unavailable' };
    if (workspace.scope.id.toLowerCase() !== identity.scopeId) return { state: 'stale-scope' };
    if (workspace.scope.groupId.toLowerCase() !== identity.groupId) return { state: 'wrong-group' };
    if (workspace.resolvedComponent && workspace.resolvedComponent.id.toLowerCase() !== identity.componentId)
      return { state: 'unknown-component' };
    const component = workspace.resolvedComponent ?? workspace.components.find(x => x.id.toLowerCase() === identity.componentId);
    return component ? { state: 'ready', scope: workspace.scope, component } : { state: 'unknown-component' };
  });
}
