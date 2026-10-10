import { arr,bool,date,dec,guid,instant,nat,nullable,obj,oneOf,sha256,str,token } from '../../core/decode';
export const confirmationStatus=oneOf('DRAFT','APPROVED','DISPATCHED','RESPONSE_RECEIVED','NO_RESPONSE','ALTERNATIVE_REQUIRED','CLOSED');
export const confirmationRow=obj({id:guid,areaCode:str(40),sourceRecordId:str(200),respondent:str(500),bookedAmount:dec,currency:str(3),confirmationDate:date,
  status:confirmationStatus,dispatchedAt:nullable(instant),monitoring:oneOf('CLOSED','RESPONSE_RECEIVED','ALTERNATIVE_PROCEDURES','ALTERNATIVE_PROCEDURES_DUE','FOLLOW_UP_DUE','AWAITING_RESPONSE','NOT_DISPATCHED'),critical:bool,criticalityRationale:nullable(str(4000)),ownerId:guid});
export const confirmationPage=obj({engagementId:guid,page:nat,hasMore:bool,outstandingCritical:nat,createReviewToken:sha256,canPrepare:bool,canReview:bool,canSetCriticality:bool,items:arr(confirmationRow,25),areaCodes:arr(str(40),64)});
export const confirmationDetail=obj({case:confirmationRow,reviewToken:sha256,contactValidationSource:str(2000),dispatchReference:nullable(str(4000)),preparedByMe:bool,
  responses:arr(obj({id:guid,revision:token,origin:str(4000),channel:str(4000),reference:str(4000),confirmedAmount:nullable(dec),differenceAmount:nullable(dec),authenticityAssessment:str(4000),decision:oneOf('AGREED','DIFFERENCE','NO_RESPONSE','ALTERNATIVE_REQUIRED'),receivedAt:instant,preparedByMe:bool,reviewerId:nullable(guid),reviewedAt:nullable(instant)}),100),
  alternatives:arr(obj({id:guid,purpose:str(4000),evidenceReferences:arr(str(1000),100),conclusion:str(4000),status:oneOf('SUBMITTED','REVIEWED'),preparedByMe:bool,reviewerId:nullable(guid),reviewedAt:nullable(instant)}),100),
  closure:nullable(obj({id:guid,conclusion:str(4000),evidenceSha256:sha256,closedByUserId:guid,closedAt:instant}))});
export type ConfirmationDetail=ReturnType<typeof confirmationDetail>;
/** A no-response observation is not a respondent's reviewed substantive response. */
export function substantiveResponse(d:ConfirmationDetail):boolean {const r=d.responses[0];return !!r?.reviewerId&&['AGREED','DIFFERENCE'].includes(r.decision);}

export function confirmationAmount(value:string):string|null {const v=value.trim();return /^-?\d{1,14}(\.\d{1,6})?$/.test(v)?v:null;}
