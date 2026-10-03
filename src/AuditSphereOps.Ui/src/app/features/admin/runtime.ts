import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { bool, guid, nullable, obj, oneOf, text, Decoder } from '../../core/decode';
import { SHARED } from '../../core/ui';
const epoch: Decoder<string> = value => {
  if(typeof value!=='string' || !/^[1-9][0-9]{0,18}$/.test(value) || BigInt(value)>9223372036854775807n) throw new Error('Unsupported deployment epoch.');
  return value;
};
const shape=obj({firmId:guid,safetyStateRecorded:bool,operatingMode:nullable(text),deploymentEpoch:nullable(epoch),
  environment:oneOf('Development','Test','Acceptance','Staging','Production','CUSTOM'),externalEffectsEnabled:bool,simulationAdaptersAllowed:bool});
export const administrationRuntime: Decoder<ReturnType<typeof shape>> = (value,path) => {
  const r=shape(value,path);
  if(r.safetyStateRecorded !== (r.operatingMode!==null && r.deploymentEpoch!==null) || !r.safetyStateRecorded && (r.operatingMode!==null || r.deploymentEpoch!==null)) throw new Error('Unsupported safety state.');
  return r;
};
@Component({selector:'audit-administration-runtime',imports:[RouterLink,MatButtonModule,...SHARED],template:`
<section class="panel" aria-labelledby="runtime-heading"><h2 id="runtime-heading">Firm safety and runtime</h2>
<button matButton (click)="ws.reload()">Refresh safety state</button>
<audit-state [loading]="ws.loading()" [error]="ws.error()" label="Firm safety and runtime" />
@if(ws.data(); as r) {
<dl class="facts"><dt>Firm ID</dt><dd>{{ r.firmId }}</dd><dt>Operating mode</dt><dd>{{ r.operatingMode ?? 'NOT_RECORDED' }}</dd>
<dt>Deployment epoch</dt><dd>{{ r.deploymentEpoch ?? 'Not recorded' }}</dd><dt>Hosting environment</dt><dd>{{ r.environment }}</dd>
<dt>External effects enabled</dt><dd>{{ r.externalEffectsEnabled ? 'Yes' : 'No' }}</dd><dt>Simulation adapters allowed</dt><dd>{{ r.simulationAdaptersAllowed ? 'Yes' : 'No' }}</dd></dl>
@if(!r.safetyStateRecorded) { <p role="alert">Firm safety state is missing. An operator must establish the persisted state; normal operation and readiness are not inferred.</p> }
@if(r.operatingMode === 'RECOVERY_QUARANTINE') { <p role="alert">Recovery quarantine: new operations are halted. Manual operator review is required.</p> }
<p>These runtime flags do not prove Microsoft consent, provider availability or production readiness.</p>
<a matButton routerLink="/app/operations">Review operations and recovery</a>
<a matButton href="/health/live" target="_blank" rel="noopener">Check system liveness</a>
<a matButton href="/health/ready" target="_blank" rel="noopener">Check system readiness</a>
}
</section>`})
export class AdministrationRuntime {
  readonly ws=inject(Api).resource(()=>'/api/ui/administration/runtime',administrationRuntime,'Current firm-wide Administrator access is required.');
}
