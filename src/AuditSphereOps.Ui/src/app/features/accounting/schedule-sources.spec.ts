import { decodeScheduleSources } from './specialist-preparation';
import { decode } from '../../core/decode';
const id='11111111-1111-4111-8111-111111111111';
const source={periodId:id,bookId:null,batchId:id,acceptanceId:id,sourceHash:'a'.repeat(64),inputGeneration:2,currency:'QAR',page:1,hasMore:false,
 lines:[{lineId:id,transactionId:id,journal:'J-1',accountCode:'1000',postingDate:'2026-06-30',signedAmount:'-100.123456',currency:'QAR',lineHash:'b'.repeat(64)}],specialistSources:[]};
describe('accepted schedule source contract',()=>{
 it('preserves signed exact source amounts and immutable identities',()=>{
  expect(decode(decodeScheduleSources,source).lines[0].signedAmount).toBe('-100.123456');
 });
 it('decodes independently approved specialist schedule component identities and exact amounts',()=>{
  const specialist={scheduleId:id,componentCode:'DEPRECIATION',signedAmount:'1200.25',currency:'QAR',scheduleHash:'c'.repeat(64),inputGeneration:2,revision:3};
  expect(decode(decodeScheduleSources,{...source,specialistSources:[specialist]}).specialistSources[0]).toEqual(specialist);
  expect(()=>decode(decodeScheduleSources,{...source,specialistSources:[{...specialist,scheduleHash:'invalid'}]})).toThrow();
 });
 it('rejects numeric money, malformed hashes and oversized pages',()=>{
  expect(()=>decode(decodeScheduleSources,{...source,lines:[{...source.lines[0],signedAmount:-100}]})).toThrow();
  expect(()=>decode(decodeScheduleSources,{...source,sourceHash:'unverified'})).toThrow();
  expect(()=>decode(decodeScheduleSources,{...source,lines:Array(26).fill(source.lines[0])})).toThrow();
 });
});
