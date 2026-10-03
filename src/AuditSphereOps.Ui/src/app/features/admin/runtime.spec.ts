import { decode } from '../../core/decode';
import { administrationRuntime } from './runtime';
const status={firmId:'11111111-1111-4111-8111-111111111111',safetyStateRecorded:true,operatingMode:'RECOVERY_QUARANTINE',deploymentEpoch:'9007199254740993',environment:'Test',externalEffectsEnabled:false,simulationAdaptersAllowed:true};
describe('administration runtime contract',()=>{
  it('preserves exact epochs and quarantine',()=>{expect(decode(administrationRuntime,status).deploymentEpoch).toBe('9007199254740993');});
  it('does not invent missing safety state',()=>{const r=decode(administrationRuntime,{...status,safetyStateRecorded:false,operatingMode:null,deploymentEpoch:null});expect(r.operatingMode).toBeNull();});
  it('rejects contradictory, malformed or secret-like runtime data',()=>{
    for(const deploymentEpoch of [1,'0','9223372036854775808']) expect(()=>decode(administrationRuntime,{...status,deploymentEpoch})).toThrow();
    expect(()=>decode(administrationRuntime,{...status,safetyStateRecorded:false})).toThrow();
    expect(()=>decode(administrationRuntime,{...status,environment:'private-host.example'})).toThrow();
  });
});
