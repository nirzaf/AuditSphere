import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, date, guid, instant, nat, nullable, obj, oneOf, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { SessionService } from '../../core/session';
import { decodeScheduleSources } from '../accounting/specialist-preparation';

export const decodeBuildInputs = obj({
  engagementId: guid,
  mappings: arr(obj({ id: guid, version: nat, datasetId: guid }), 100),
  plans: arr(obj({ id: guid, baseDatasetId: guid, resultHash: text }), 100),
  periods: arr(obj({ id: guid, periodCode: text, startDate: date, endDate: date, currency: text, basis: text }), 100),
  comparatives: arr(obj({ id: guid, engagementId: guid, framework: text, periodStart: text, periodEnd: text, currency: text, basis: nullable(text), status: text }), 100),
});
export const decodeBuildStatus = obj({
  operationId: guid,
  status: oneOf('PENDING', 'CLAIMED', 'REMOTE_STARTED', 'VERIFYING', 'COMPLETED', 'RETRY_WAIT',
    'AUTHORIZATION_BLOCKED', 'PROVIDER_BLOCKED', 'RESULT_UNCERTAIN', 'DEAD_LETTER', 'CANCEL_REQUESTED', 'CANCELLED_WITH_DISPOSITION'),
  attemptCount: nat,
  createdAt: instant,
  completedAt: nullable(instant),
  packageId: nullable(guid),
});
export type BuildInputs = ReturnType<typeof decodeBuildInputs>;
type BuildStatus = ReturnType<typeof decodeBuildStatus>;

const TERMINAL_BUILD_STATES = new Set<BuildStatus['status']>([
  'COMPLETED', 'AUTHORIZATION_BLOCKED', 'PROVIDER_BLOCKED', 'RESULT_UNCERTAIN', 'DEAD_LETTER', 'CANCELLED_WITH_DISPOSITION',
]);

export function isTerminalBuildStatus(status: BuildStatus['status']): boolean {
  return TERMINAL_BUILD_STATES.has(status);
}

export interface AllocationForm { generalLedgerLineId: string; signedAmount: string }
export interface ScheduleAllocationForm { specialistScheduleId: string; componentCode: 'DEPRECIATION' | 'IMPAIRMENT'; signedAmount: string }
export interface MovementForm {
  section: string; description: string; amount: string; isNonCash: boolean; calculationRole: string; movementDate: string; activityCode: string;
  sourceReferences: string; rationale: string; allocations: AllocationForm[]; scheduleAllocations: ScheduleAllocationForm[];
}
export interface DisclosureForm { code: string; response: string; notApplicable: boolean; rationale: string }
/** One equity rollforward row. Its field names are the server's EquityLineInput names, so the request is not renamed. */
export interface EquityLineForm {
  lineCode: string; description: string; openingAmount: string; profitOrLossAmount: string; ociAmount: string;
  capitalMovementAmount: string; dividendsAmount: string; closingAmount: string; evidenceReference: string;
}
export interface EquityTransferForm {
  transferReference: string; sourceLineCode: string; destinationLineCode: string; amount: string; evidenceReference: string;
}
/** One numeric note line. The server calls the statement destination `faceDestinationCode`. */
export interface NoteLineForm { noteCode: string; faceDestinationCode: string; amount: string; evidenceReference: string; allocations: AllocationForm[] }
/** One foreign-exchange effect on cash, with its evidence. */
export interface FxCashEffectForm { currencyPair: string; amount: string; evidenceReference: string }
export interface BuildForm {
  mappingVersionId: string; adjustmentPlanId: string; periodId: string; framework: string; templateVersion: string;
  periodStart: string; periodEnd: string; cashBeginning: string; cashEnding: string; cashAccountCodes: string;
  openingEvidence: string; closingEvidence: string; comparativePackageId: string; comparativeBasis: string; comparativeEvidence: string;
  fxAssessmentState: '' | 'ASSESSED_ZERO' | 'EFFECTS_RECORDED'; fxAssessmentEvidence: string; fxAssessmentRationale: string;
  movements: MovementForm[]; disclosures: DisclosureForm[];
  equityLines: EquityLineForm[]; equityTransfers: EquityTransferForm[]; noteLines: NoteLineForm[]; fxCashEffects: FxCashEffectForm[];
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const AMOUNT = /^-?\d{1,13}(\.\d{1,6})?$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const list = (value: string): string[] => value.split(',').map((x) => x.trim()).filter((x) => x.length > 0);
const EQUITY_AMOUNT_FIELDS: ReadonlyArray<readonly [keyof EquityLineForm, string]> = [
  ['openingAmount', 'the opening balance'],
  ['profitOrLossAmount', 'profit or loss'],
  ['ociAmount', 'other comprehensive income'],
  ['capitalMovementAmount', 'capital movements'],
  ['dividendsAmount', 'dividends'],
  ['closingAmount', 'the closing balance'],
];

/**
 * Exact six-decimal units of an amount that already passed AMOUNT. BigInt keeps the sums off floating point,
 * so a contribution that equals its movement compares equal however many digits it carries.
 */
function toMicro(amount: string): bigint {
  const negative = amount.startsWith('-');
  const [whole, fraction = ''] = (negative ? amount.slice(1) : amount).split('.');
  const units = BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0'));
  return negative ? -units : units;
}

const absMicro = (value: bigint): bigint => (value < 0n ? -value : value);

function sumContributions(allocations: readonly { signedAmount: string }[]): bigint {
  return allocations.reduce((sum, a) => sum + toMicro(a.signedAmount), 0n);
}

/**
 * The statement effect of linked ledger contributions, mirroring the server's projection role exactly. A cash movement draws on the
 * noncash counterpart of a cash event, so the ledger sign reverses onto the cash flow: a credited receivable of -270,000 is a receipt of
 * +270,000, and a debited equipment line of +25,000 is an investing outflow of -25,000. A noncash movement keeps the ledger sign. The
 * ledger amounts themselves are sent unchanged; this projection is only the mechanical preview the server recomputes.
 */
export function projectedContribution(allocations: readonly AllocationForm[], isNonCash: boolean): bigint {
  const ledger = sumContributions(allocations);
  return isNonCash ? ledger : -ledger;
}

/**
 * The server's shape rule: projected contributions may not exceed the movement amount or run against its sign. Inputs must already be
 * valid.
 */
function contributionsOverdrawn(allocations: readonly { signedAmount: string }[], amount: string, isNonCash: boolean): boolean {
  const ledger = sumContributions(allocations);
  const total = isNonCash ? ledger : -ledger;
  if (total === 0n) return false;
  const target = toMicro(amount);
  return absMicro(total) > absMicro(target) || (total < 0n) !== (target < 0n);
}

/** Exact decimal text for a six-decimal unit count, for showing the projected statement effect without floating point. */
export function formatMicro(units: bigint): string {
  const negative = units < 0n;
  const magnitude = negative ? -units : units;
  const whole = magnitude / 1_000_000n;
  const fraction = (magnitude % 1_000_000n).toString().padStart(6, '0').replace(/0+$/, '');
  return `${negative ? '-' : ''}${whole}${fraction ? `.${fraction}` : ''}`;
}

/** The projected statement effect of a movement's valid links, or null while any link or amount is not yet a valid value. */
export function movementProjection(m: MovementForm): string | null {
  const values = [...m.allocations, ...m.scheduleAllocations];
  if (!values.length || values.some((a) => !AMOUNT.test(a.signedAmount))) return null;
  const total = sumContributions(values);
  return formatMicro(m.isNonCash ? total : -total);
}

export function emptyBuildForm(): BuildForm {
  return { mappingVersionId: '', adjustmentPlanId: '', periodId: '', framework: 'IFRS', templateVersion: '', periodStart: '', periodEnd: '',
    cashBeginning: '0', cashEnding: '0', cashAccountCodes: '', openingEvidence: '', closingEvidence: '',
    comparativePackageId: '', comparativeBasis: '', comparativeEvidence: '', movements: [], disclosures: [],
    fxAssessmentState: '', fxAssessmentEvidence: '', fxAssessmentRationale: '',
    equityLines: [], equityTransfers: [], noteLines: [], fxCashEffects: [] };
}

export function emptyMovement(): MovementForm {
  return { section: 'OPERATING', description: '', amount: '', isNonCash: false, calculationRole: '', movementDate: '', activityCode: '', sourceReferences: '',
    rationale: '', allocations: [], scheduleAllocations: [] };
}

export function emptyEquityLine(): EquityLineForm {
  return { lineCode: '', description: '', openingAmount: '', profitOrLossAmount: '', ociAmount: '', capitalMovementAmount: '',
    dividendsAmount: '', closingAmount: '', evidenceReference: '' };
}

export function emptyEquityTransfer(): EquityTransferForm {
  return { transferReference: '', sourceLineCode: '', destinationLineCode: '', amount: '', evidenceReference: '' };
}

export function emptyNoteLine(): NoteLineForm {
  return { noteCode: '', faceDestinationCode: '', amount: '', evidenceReference: '', allocations: [] };
}

export function emptyFxCashEffect(): FxCashEffectForm {
  return { currencyPair: '', amount: '', evidenceReference: '' };
}

/**
 * The request the staff build screen sends. It refuses anything the server would reject for form reasons, so a plain typo never
 * reaches the worker. The server still decides every accounting rule, including whether a source line belongs to the period.
 *
 * A schedule with no rows is sent as null: the request DTO's default for an absent schedule, so the server treats it as not supplied.
 * Sending an empty array instead would reach the server's typed-accounting guard. A schedule with rows is always an array, so an
 * entered schedule is never silently dropped.
 */
export function toBuildRequest(form: BuildForm): { body: unknown } | { error: string } {
  if (!GUID.test(form.mappingVersionId) || !GUID.test(form.adjustmentPlanId)) return { error: 'Choose an approved mapping and a finalized adjustment plan.' };
  if (!DATE.test(form.periodStart) || !DATE.test(form.periodEnd)) return { error: 'Choose a reporting period.' };
  if (!form.templateVersion.trim() || form.templateVersion.length > 200) return { error: 'Enter a template version of up to 200 characters.' };
  if (!AMOUNT.test(form.cashBeginning) || !AMOUNT.test(form.cashEnding)) return { error: 'Opening and closing cash must be amounts with at most six decimals.' };
  const comparativeId = form.comparativePackageId.trim();
  if (comparativeId && (!GUID.test(comparativeId) || !form.comparativeBasis.trim() || !form.comparativeEvidence.trim()))
    return { error: 'A comparative package needs a valid package ID, basis, and evidence reference.' };
  if (form.movements.length === 0) return { error: 'Add at least one cash-flow movement.' };
  for (const [index, m] of form.movements.entries()) {
    const at = `Movement ${index + 1}`;
    if (!['OPERATING', 'INVESTING', 'FINANCING'].includes(m.section)) return { error: `${at}: choose a section.` };
    const roleSections: Record<string, readonly string[]> = {
      STARTING_PROFIT_SUBTOTAL: ['OPERATING'], NONCASH_PROFIT_ADJUSTMENT: ['OPERATING'], OPERATING_BALANCE_MOVEMENT: ['OPERATING'],
      INVESTING_CASH_MOVEMENT: ['INVESTING'], FINANCING_CASH_MOVEMENT: ['FINANCING'],
      NONCASH_INVESTING_FINANCING_DISCLOSURE: ['INVESTING', 'FINANCING'],
    };
    const requiresNoncashSource = ['NONCASH_PROFIT_ADJUSTMENT', 'NONCASH_INVESTING_FINANCING_DISCLOSURE'].includes(m.calculationRole);
    const requiresCashSource = ['STARTING_PROFIT_SUBTOTAL', 'INVESTING_CASH_MOVEMENT', 'FINANCING_CASH_MOVEMENT'].includes(m.calculationRole);
    if (m.calculationRole && (!roleSections[m.calculationRole]?.includes(m.section) ||
      (requiresNoncashSource && !m.isNonCash) || (requiresCashSource && m.isNonCash)))
      return { error: `${at}: calculation role, section, and noncash-source flag must agree.` };
    if (!m.description.trim() || m.description.length > 2000) return { error: `${at}: enter a description.` };
    if (!AMOUNT.test(m.amount)) return { error: `${at}: enter an amount with at most six decimals.` };
    if (!DATE.test(m.movementDate)) return { error: `${at}: enter the movement date.` };
    if (!/^[A-Za-z0-9_.-]{1,100}$/.test(m.activityCode.trim())) return { error: `${at}: enter an activity code.` };
    if (list(m.sourceReferences).length === 0) return { error: `${at}: enter at least one evidence reference.` };
    if (!m.rationale.trim() || m.rationale.length > 2000) return { error: `${at}: enter the classification rationale.` };
    for (const a of m.allocations) {
      if (!GUID.test(a.generalLedgerLineId)) return { error: `${at}: a source line is not valid.` };
      if (!AMOUNT.test(a.signedAmount) || Number(a.signedAmount) === 0) return { error: `${at}: a source contribution needs a non-zero amount.` };
    }
    if (m.allocations.length && m.scheduleAllocations.length)
      return { error: `${at}: use accepted general-ledger lines or specialist schedule sources, not both.` };
    if (m.scheduleAllocations.length && !m.isNonCash)
      return { error: `${at}: specialist schedule sources can only support a noncash movement.` };
    for (const a of m.scheduleAllocations) {
      if (!GUID.test(a.specialistScheduleId) || !['DEPRECIATION', 'IMPAIRMENT'].includes(a.componentCode))
        return { error: `${at}: choose a valid approved asset schedule component.` };
      if (!AMOUNT.test(a.signedAmount) || !/[1-9]/.test(a.signedAmount) || a.signedAmount.startsWith('-'))
        return { error: `${at}: a specialist component contribution must be a positive amount.` };
    }
    if (new Set(m.scheduleAllocations.map((a) => `${a.specialistScheduleId.toLowerCase()}:${a.componentCode}`)).size !== m.scheduleAllocations.length)
      return { error: `${at}: a specialist schedule component can contribute to a movement only once.` };
    if (new Set(m.allocations.map((a) => a.generalLedgerLineId.toLowerCase())).size !== m.allocations.length)
      return { error: `${at}: a source line can contribute to a movement only once.` };
    if ((m.allocations.length || m.scheduleAllocations.length) && contributionsOverdrawn([...m.allocations, ...m.scheduleAllocations], m.amount, m.isNonCash))
      return { error: `${at}: source contributions exceed the movement amount or run against its sign.` };
  }
  for (const d of form.disclosures) {
    if (!d.code.trim() || d.code.length > 100) return { error: 'Each disclosure needs a code.' };
    if (d.notApplicable ? !d.rationale.trim() : !d.response.trim()) return { error: `Disclosure ${d.code}: give a response, or a rationale when not applicable.` };
  }
  if (form.disclosures.length === 0) return { error: 'Add at least one disclosure.' };
  const equityCodes = new Set<string>();
  for (const [index, row] of form.equityLines.entries()) {
    const at = `Equity line ${index + 1}`;
    const code = row.lineCode.trim().toUpperCase();
    if (!code || code.length > 100) return { error: `${at}: enter a line code of up to 100 characters.` };
    if (equityCodes.has(code)) return { error: `${at}: each equity line code can appear only once.` };
    equityCodes.add(code);
    if (!row.description.trim() || row.description.trim().length > 2000) return { error: `${at}: enter a description.` };
    for (const [field, label] of EQUITY_AMOUNT_FIELDS) {
      if (!AMOUNT.test(row[field])) return { error: `${at}: enter ${label} with at most six decimals.` };
    }
    if (!row.evidenceReference.trim() || row.evidenceReference.trim().length > 2000) return { error: `${at}: enter an evidence reference.` };
  }
  if (form.equityTransfers.length && !form.equityLines.length)
    return { error: 'Add the source and destination equity component lines before recording a transfer.' };
  const transferReferences = new Set<string>();
  for (const [index, row] of form.equityTransfers.entries()) {
    const at = `Equity transfer ${index + 1}`;
    const reference = row.transferReference.trim().toUpperCase();
    const source = row.sourceLineCode.trim().toUpperCase();
    const destination = row.destinationLineCode.trim().toUpperCase();
    if (!reference || reference.length > 100) return { error: `${at}: enter a reference of up to 100 characters.` };
    if (transferReferences.has(reference)) return { error: `${at}: transfer references must be unique.` };
    transferReferences.add(reference);
    if (!source || !destination || source === destination || !equityCodes.has(source) || !equityCodes.has(destination))
      return { error: `${at}: choose two different equity components entered above.` };
    if (!AMOUNT.test(row.amount) || row.amount.startsWith('-') || !/[1-9]/.test(row.amount))
      return { error: `${at}: enter a positive amount with at most six decimals.` };
    if (!row.evidenceReference.trim() || row.evidenceReference.trim().length > 2000)
      return { error: `${at}: enter an evidence reference.` };
  }
  const noteIdentities = new Set<string>();
  for (const [index, row] of form.noteLines.entries()) {
    const at = `Note line ${index + 1}`;
    const noteCode = row.noteCode.trim().toUpperCase();
    const face = row.faceDestinationCode.trim().toUpperCase();
    if (!noteCode || noteCode.length > 100) return { error: `${at}: enter a note code of up to 100 characters.` };
    if (!face || face.length > 100) return { error: `${at}: enter a face destination code of up to 100 characters.` };
    const identity = `${noteCode}\u001f${face}`;
    if (noteIdentities.has(identity)) return { error: `${at}: each note code and face destination pair can appear only once.` };
    noteIdentities.add(identity);
    if (!AMOUNT.test(row.amount)) return { error: `${at}: enter an amount with at most six decimals.` };
    if (!row.evidenceReference.trim() || row.evidenceReference.trim().length > 2000) return { error: `${at}: enter an evidence reference.` };
    for (const allocation of row.allocations) {
      if (!GUID.test(allocation.generalLedgerLineId)) return { error: `${at}: a source line is not valid.` };
      if (!AMOUNT.test(allocation.signedAmount) || toMicro(allocation.signedAmount) === 0n)
        return { error: `${at}: a source contribution needs a non-zero amount with at most six decimals.` };
    }
    if (new Set(row.allocations.map((allocation) => allocation.generalLedgerLineId.toLowerCase())).size !== row.allocations.length)
      return { error: `${at}: a source line can contribute to a note only once.` };
    if (row.allocations.length && sumContributions(row.allocations) !== toMicro(row.amount))
      return { error: `${at}: source contributions must total the note amount exactly.` };
  }
  for (const [index, row] of form.fxCashEffects.entries()) {
    const at = `FX cash effect ${index + 1}`;
    if (!row.currencyPair.trim() || row.currencyPair.trim().length > 20) return { error: `${at}: enter a currency pair of up to 20 characters.` };
    if (!AMOUNT.test(row.amount)) return { error: `${at}: enter an amount with at most six decimals.` };
    if (!row.evidenceReference.trim() || row.evidenceReference.trim().length > 2000) return { error: `${at}: enter an evidence reference.` };
    if (toMicro(row.amount) === 0n) return { error: `${at}: zero is recorded with an assessed-zero population, not as an effect row.` };
  }
  if (form.fxAssessmentState && (!form.fxAssessmentEvidence.trim() || form.fxAssessmentEvidence.trim().length > 2000 ||
      !form.fxAssessmentRationale.trim() || form.fxAssessmentRationale.trim().length > 2000))
    return { error: 'An FX cash-effect assessment needs bounded evidence and a rationale.' };
  if (form.fxAssessmentState === 'ASSESSED_ZERO' && form.fxCashEffects.length > 0)
    return { error: 'An assessed-zero FX population cannot contain FX effect rows.' };
  if (form.fxAssessmentState === 'EFFECTS_RECORDED' && form.fxCashEffects.length === 0)
    return { error: 'An effects-recorded FX assessment needs at least one non-zero evidenced effect row.' };
  return {
    body: {
      adjustmentPlanId: form.adjustmentPlanId, mappingVersionId: form.mappingVersionId, framework: form.framework.trim(),
      periodStart: form.periodStart, periodEnd: form.periodEnd, templateVersion: form.templateVersion.trim(),
      supplementaryInformation: {
        // Keep accounting values as decimal strings through JSON serialization. Converting to JS
        // Number here can round values before the API's exact decimal converter sees them.
        cashBeginning: form.cashBeginning, cashEnding: form.cashEnding,
        comparative: comparativeId ? { packageId: comparativeId, basis: form.comparativeBasis.trim(), evidenceReference: form.comparativeEvidence.trim() } : null,
        cashFlowLines: form.movements.map((m) => ({
          section: m.section, description: m.description.trim(), amount: m.amount, isNonCash: m.isNonCash,
          calculationRole: m.calculationRole || null,
          movementDate: m.movementDate, activityCode: m.activityCode.trim(), sourceReferences: list(m.sourceReferences),
          classificationRationale: m.rationale.trim(),
          sourceAllocations: m.allocations.length
            ? m.allocations.map((a) => ({ generalLedgerLineId: a.generalLedgerLineId, signedAmount: a.signedAmount }))
            : null,
          scheduleSourceAllocations: m.scheduleAllocations.length
            ? m.scheduleAllocations.map((a) => ({ specialistScheduleId: a.specialistScheduleId, componentCode: a.componentCode, signedAmount: a.signedAmount }))
            : null,
        })),
        disclosures: form.disclosures.map((d) => ({ code: d.code.trim(), response: d.notApplicable ? '' : d.response.trim(),
          notApplicable: d.notApplicable, rationale: d.notApplicable ? d.rationale.trim() : null })),
        equityLines: form.equityLines.length ? form.equityLines.map((row) => ({
          lineCode: row.lineCode.trim(), description: row.description.trim(), openingAmount: row.openingAmount,
          profitOrLossAmount: row.profitOrLossAmount, ociAmount: row.ociAmount, capitalMovementAmount: row.capitalMovementAmount,
          dividendsAmount: row.dividendsAmount, closingAmount: row.closingAmount, evidenceReference: row.evidenceReference.trim(),
        })) : null,
        equityTransfers: form.equityTransfers.length ? form.equityTransfers.map((row) => ({
          transferReference: row.transferReference.trim(), sourceLineCode: row.sourceLineCode.trim(),
          destinationLineCode: row.destinationLineCode.trim(), amount: row.amount,
          evidenceReference: row.evidenceReference.trim(),
        })) : null,
        noteLines: form.noteLines.length ? form.noteLines.map((row) => ({
          noteCode: row.noteCode.trim(), faceDestinationCode: row.faceDestinationCode.trim(), amount: row.amount,
          evidenceReference: row.evidenceReference.trim(), sourceAllocations: row.allocations.length
            ? row.allocations.map((allocation) => ({ generalLedgerLineId: allocation.generalLedgerLineId, signedAmount: allocation.signedAmount }))
            : null,
        })) : null,
        fxCashEffects: form.fxCashEffects.length ? form.fxCashEffects.map((row) => ({
          currencyPair: row.currencyPair.trim(), amount: row.amount, evidenceReference: row.evidenceReference.trim(),
        })) : null,
        fxCashAssessment: form.fxAssessmentState ? {
          state: form.fxAssessmentState, evidenceReference: form.fxAssessmentEvidence.trim(), rationale: form.fxAssessmentRationale.trim(),
        } : null,
        cashAccountCodes: list(form.cashAccountCodes), openingCashEvidenceReferences: list(form.openingEvidence),
        closingCashEvidenceReferences: list(form.closingEvidence),
      },
    },
  };
}

/**
 * Schedules this screen has not yet supplied, plus the source-lineage gaps it can see. Guidance only: the server validates every
 * schedule when the build runs and remains the authority. An empty list is not a reviewability claim.
 */
export function scheduleBlockers(form: BuildForm): string[] {
  const open: string[] = [];
  if (form.equityLines.length === 0)
    open.push('Equity rollforward: no rows entered. Each equity line must reconcile opening balance, profit or loss, OCI, capital movements, dividends and closing balance, with evidence.');
  if (form.noteLines.length === 0)
    open.push('Numeric note lines: none entered. Each note amount must cross-cast to its statement face destination.');
  if (!form.fxAssessmentState)
    open.push('FX cash-effect population: not assessed. Missing evidence is not treated as zero.');
  else if (!form.fxAssessmentEvidence.trim() || !form.fxAssessmentRationale.trim())
    open.push('FX cash-effect assessment: evidence and rationale are required.');
  if (form.fxAssessmentState === 'ASSESSED_ZERO' && form.fxCashEffects.length > 0)
    open.push('FX cash-effect assessment is inconsistent: assessed zero cannot contain effect rows.');
  if (form.fxAssessmentState === 'EFFECTS_RECORDED' && form.fxCashEffects.length === 0)
    open.push('FX cash-effect assessment is inconsistent: recorded effects need at least one effect row.');
  if (form.movements.length === 0) open.push('Cash-flow movements: none entered. Add at least one movement.');
  for (const [index, m] of form.movements.entries()) {
    const at = `Cash-flow source lineage: movement ${index + 1}`;
    if (m.allocations.length === 0 && m.scheduleAllocations.length === 0) {
      open.push(`${at} is not linked to accepted general-ledger lines, so it stays a draft without lineage.`);
    } else if (!AMOUNT.test(m.amount) || m.allocations.some((a) => !GUID.test(a.generalLedgerLineId) || !AMOUNT.test(a.signedAmount))) {
      open.push(`${at} has a link or amount that is not a valid value yet.`);
    } else if (m.allocations.length && m.scheduleAllocations.length) {
      open.push(`${at} mixes accepted ledger and specialist schedule sources; split the movement by source type.`);
    } else if (m.scheduleAllocations.length && !m.isNonCash) {
      open.push(`${at} uses specialist schedule sources for a cash movement; mark it noncash or link accepted ledger lines.`);
    } else if (m.scheduleAllocations.some((a) => !GUID.test(a.specialistScheduleId) || !AMOUNT.test(a.signedAmount) || a.signedAmount.startsWith('-')) ||
      (m.allocations.length ? projectedContribution(m.allocations, m.isNonCash) : sumContributions(m.scheduleAllocations)) !== toMicro(m.amount)) {
      open.push(`${at} has linked lines that do not total the movement amount, so it is not source-resolved.`);
    }
  }
  for (const [index, row] of form.noteLines.entries()) {
    if (!row.allocations.length) continue;
    const at = `Note source attribution: note line ${index + 1}`;
    if (!AMOUNT.test(row.amount) || row.allocations.some((allocation) => !GUID.test(allocation.generalLedgerLineId) ||
      !AMOUNT.test(allocation.signedAmount) || toMicro(allocation.signedAmount) === 0n) ||
      new Set(row.allocations.map((allocation) => allocation.generalLedgerLineId.toLowerCase())).size !== row.allocations.length)
      open.push(`${at} has a link or amount that is not a valid value yet.`);
    else if (sumContributions(row.allocations) !== toMicro(row.amount))
      open.push(`${at} has linked lines that do not total the note amount, so it is not source-resolved.`);
  }
  if (form.disclosures.length === 0)
    open.push('Disclosures: none entered. Add at least one disclosure response, or a rationale when not applicable.');
  return open;
}

/** The submit control must not read as a complete, reviewable build while any schedule is still open. */
export function queueButtonLabel(openBlockers: readonly string[]): string {
  return openBlockers.length ? 'Queue draft build with open blockers' : 'Queue package build';
}

@Component({
  selector: 'audit-financial-package-build',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Build financial package</span></nav>
    <audit-page-header title="Build financial package" eyebrow="Financial reporting"
      description="Queue a draft build of a financial-statement package. Cash-flow movements can be linked to accepted general-ledger lines. The worker builds the draft; it becomes reviewable only after every schedule validates and independent review." />
    <audit-state [loading]="inputs.loading()" [error]="inputs.error()" label="the build inputs" />
    @if (inputs.data(); as data) {
      <form class="panel" aria-label="Financial package build" (submit)="$event.preventDefault(); submit()">
        <h2>Basis</h2>
        <label>Approved mapping
          <select name="mapping" aria-label="Approved mapping" [(ngModel)]="form.mappingVersionId" (ngModelChange)="form.adjustmentPlanId = ''">
            <option value="">Choose a mapping</option>
            @for (m of data.mappings; track m.id) { <option [value]="m.id">Version {{ m.version }} · {{ m.id }}</option> }
          </select>
        </label>
        <label>Finalized adjustment plan
          <select name="plan" aria-label="Finalized adjustment plan" [(ngModel)]="form.adjustmentPlanId">
            <option value="">Choose a plan</option>
            @for (p of plansFor(data); track p.id) { <option [value]="p.id">{{ p.id }}</option> }
          </select>
        </label>
        <label>Reporting period
          <select name="period" aria-label="Reporting period" [(ngModel)]="form.periodId" (ngModelChange)="choosePeriod(data)">
            <option value="">Choose a period</option>
            @for (p of data.periods; track p.id) { <option [value]="p.id">{{ p.periodCode }} · {{ p.startDate }} to {{ p.endDate }} · {{ p.currency }} · {{ p.basis }}</option> }
          </select>
        </label>
        <label>Framework <input name="framework" [(ngModel)]="form.framework" (ngModelChange)="clearComparative(data)" maxlength="50" /></label>
        <label>Template version <input name="template" [(ngModel)]="form.templateVersion" maxlength="200" required /></label>
        <h2>Comparative package</h2>
        <label>Comparative package (optional)
          <select name="comparativePackage" [(ngModel)]="form.comparativePackageId">
            <option value="">No comparative package</option>
            @for (p of comparativesFor(data); track p.id) { <option [value]="p.id">{{ p.periodStart }} to {{ p.periodEnd }} · {{ p.framework }} · {{ p.currency }} · engagement {{ p.engagementId }} · {{ p.id }}</option> }
          </select>
        </label>
        @if (form.comparativePackageId.trim()) {
          <label>Comparative basis <input name="comparativeBasis" [(ngModel)]="form.comparativeBasis" maxlength="100" required /></label>
          <label>Comparative evidence reference <input name="comparativeEvidence" [(ngModel)]="form.comparativeEvidence" maxlength="2000" required /></label>
        }

        <h2>Cash</h2>
        <label>Opening cash <input name="cashBeginning" [(ngModel)]="form.cashBeginning" inputmode="decimal" /></label>
        <label>Closing cash <input name="cashEnding" [(ngModel)]="form.cashEnding" inputmode="decimal" /></label>
        <label>Cash account codes (comma separated) <input name="cashAccounts" [(ngModel)]="form.cashAccountCodes" /></label>
        <label>Opening cash evidence (comma separated) <input name="openingEvidence" [(ngModel)]="form.openingEvidence" /></label>
        <label>Closing cash evidence (comma separated) <input name="closingEvidence" [(ngModel)]="form.closingEvidence" /></label>

        <h2>Cash-flow movements</h2>
        <p><small>A movement that links to accepted general-ledger lines is source-resolved only when its projected links total its amount. Link the noncash counterpart of each cash event with its ledger sign unchanged: for a cash movement the ledger sign reverses onto the cash flow (a credited receivable of -270,000 is a receipt of +270,000); a noncash movement keeps the ledger sign. Preparer text alone is never lineage.</small></p>
        @for (m of form.movements; track $index; let i = $index) {
          <fieldset class="movement">
            <legend>Movement {{ i + 1 }}</legend>
            <label>Section <select [name]="'section' + i" [(ngModel)]="m.section"><option>OPERATING</option><option>INVESTING</option><option>FINANCING</option></select></label>
            <label>Description <input [name]="'description' + i" [(ngModel)]="m.description" maxlength="2000" /></label>
            <label>Amount <input [name]="'amount' + i" [(ngModel)]="m.amount" inputmode="decimal" /></label>
            <label>Indirect calculation role <select [name]="'calcRole' + i" [(ngModel)]="m.calculationRole">
              <option value="">Legacy interpretation (role not recorded)</option>
              <option value="STARTING_PROFIT_SUBTOTAL">Starting profit subtotal</option>
              <option value="NONCASH_PROFIT_ADJUSTMENT">Noncash profit adjustment</option>
              <option value="OPERATING_BALANCE_MOVEMENT">Operating balance movement</option>
              <option value="INVESTING_CASH_MOVEMENT">Investing cash movement</option>
              <option value="FINANCING_CASH_MOVEMENT">Financing cash movement</option>
              <option value="NONCASH_INVESTING_FINANCING_DISCLOSURE">Noncash investing/financing disclosure</option>
            </select></label>
            <label><input type="checkbox" [name]="'noncash' + i" [(ngModel)]="m.isNonCash" /> Noncash source provenance (source links only; a selected calculation role governs the bridge)</label>
            <p><small>Calculation roles classify draft output only; they do not mean the selected methodology has been professionally approved. Noncash profit adjustments are included in the operating reconciliation; noncash investing/financing disclosures are shown separately and excluded from cash totals.</small></p>
            <label>Movement date <input type="date" [name]="'date' + i" [(ngModel)]="m.movementDate" /></label>
            <label>Activity code <input [name]="'activity' + i" [(ngModel)]="m.activityCode" maxlength="100" /></label>
            <label>Evidence references (comma separated) <input [name]="'evidence' + i" [(ngModel)]="m.sourceReferences" /></label>
            <label>Classification rationale <textarea [name]="'rationale' + i" [(ngModel)]="m.rationale" maxlength="2000" rows="2"></textarea></label>
            <h3>Accepted source lines</h3>
            @if (m.allocations.length) {
              <ul>
                @for (a of m.allocations; track $index; let j = $index) {
                  <li><code>{{ a.generalLedgerLineId }}</code> <input [name]="'alloc' + i + '-' + j" [(ngModel)]="a.signedAmount" inputmode="decimal" aria-label="Contribution amount" />
                    <button matButton type="button" (click)="m.allocations.splice(j, 1)">Remove</button></li>
                }
              </ul>
              @if (projection(m); as effect) {
                <p><small>Projected statement effect of these ledger amounts: <span class="number">{{ effect }}</span> ({{ m.isNonCash ? 'noncash role keeps the ledger sign' : 'cash role reverses the ledger sign' }}). The server recomputes this when the build runs.</small></p>
              }
            }
            @if (m.scheduleAllocations.length) {
              <h4>Approved asset schedule sources</h4>
              <ul>@for (a of m.scheduleAllocations; track $index; let j = $index) {
                <li>{{ a.componentCode }} · <code>{{ a.specialistScheduleId }}</code> · {{ a.signedAmount }}
                  <button matButton type="button" (click)="m.scheduleAllocations.splice(j, 1)">Remove</button></li>
              }</ul>
              @if (projection(m); as effect) { <p><small>Projected noncash effect from exact approved schedule components: <span class="number">{{ effect }}</span>. The server rechecks the retained schedule and client generation when the build runs.</small></p> }
            }
            <button matButton="outlined" type="button" (click)="openMovementSources(i)">{{ pickerFor() === i ? 'Hide source lines' : 'Choose source lines' }}</button>
            <button matButton type="button" (click)="form.movements.splice(i, 1)">Remove movement</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.movements.push(newMovement())">Add movement</button>

        @if ((pickerFor() !== null || notePickerFor() !== null) && form.periodId) {
          <section aria-label="Accepted source lines" class="panel">
            <h3>Accepted general-ledger lines for the period</h3>
            <audit-state [loading]="sources.loading()" [error]="sources.error()" label="the accepted source lines" />
            @if (sources.data(); as page) {
              <div class="table-scroll"><table>
                <caption>Accepted general-ledger lines, page {{ page.page }}</caption>
                <thead><tr><th scope="col">Date</th><th scope="col">Journal</th><th scope="col">Account</th><th scope="col" class="number">Signed amount</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
                <tbody>@for (line of page.lines; track line.lineId) {
                  <tr><td>{{ line.postingDate }}</td><td>{{ line.journal }}</td><td>{{ line.accountCode }}</td><td class="number">{{ line.signedAmount }} {{ line.currency }}</td>
                    <td><button matButton="outlined" type="button" (click)="addSelectedSource(line.lineId, line.signedAmount)">{{ notePickerFor() !== null ? 'Add to note ' + (notePickerFor()! + 1) : 'Add to movement ' + (pickerFor()! + 1) }}</button></td></tr>
                } @empty { <tr><td colspan="5">No accepted general-ledger lines in this period on this page.</td></tr> }</tbody></table></div>
              <p class="actions">
                <button matButton="outlined" type="button" [disabled]="sourcePage() <= 1" (click)="sourcePage.set(sourcePage() - 1)">Previous lines</button>
                <button matButton="outlined" type="button" [disabled]="!page.hasMore" (click)="sourcePage.set(sourcePage() + 1)">Next lines</button>
              </p>
              @if (pickerFor() !== null && page.specialistSources.length) {
                <h4>Current independently approved asset schedule components</h4>
                <p><small>These source amounts are available for noncash movements only. The package build revalidates the schedule revision, approval and client input generation.</small></p>
                @if (!form.movements[pickerFor()!]?.isNonCash) { <p role="status">Mark the selected movement as noncash to add an approved asset schedule component.</p> }
                <div class="table-scroll"><table>
                  <caption>Approved asset schedule components</caption>
                  <thead><tr><th scope="col">Component</th><th scope="col">Schedule revision</th><th scope="col" class="number">Amount</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
                  <tbody>@for (source of page.specialistSources; track source.scheduleId + ':' + source.componentCode) {
                    <tr><td>{{ source.componentCode }}</td><td>{{ source.revision }} · <code>{{ source.scheduleId }}</code></td><td class="number">{{ source.signedAmount }} {{ source.currency }}</td>
                      <td><button matButton="outlined" type="button" [disabled]="!form.movements[pickerFor()!]?.isNonCash || !!form.movements[pickerFor()!]?.allocations.length"
                        (click)="addSelectedScheduleSource(source.scheduleId, source.componentCode, source.signedAmount)">Add to movement {{ (pickerFor()! + 1) }}</button></td></tr>
                  }</tbody></table></div>
              }
            }
          </section>
        }

        <h2>Equity rollforward</h2>
        <p><small>One row per equity line. Dividends are deducted from the balance. The server requires each row to reconcile: opening, profit or loss, OCI, capital movements and dividends give the closing balance.</small></p>
        @for (row of form.equityLines; track $index; let i = $index) {
          <fieldset class="movement">
            <legend>Equity line {{ i + 1 }}</legend>
            <label>Equity line code <input [name]="'eqCode' + i" [(ngModel)]="row.lineCode" maxlength="100" /></label>
            <label>Equity line description <input [name]="'eqDesc' + i" [(ngModel)]="row.description" maxlength="2000" /></label>
            <label>Equity opening balance <input [name]="'eqOpen' + i" [(ngModel)]="row.openingAmount" inputmode="decimal" /></label>
            <label>Equity profit or loss <input [name]="'eqPl' + i" [(ngModel)]="row.profitOrLossAmount" inputmode="decimal" /></label>
            <label>Equity other comprehensive income <input [name]="'eqOci' + i" [(ngModel)]="row.ociAmount" inputmode="decimal" /></label>
            <label>Equity capital movements <input [name]="'eqCap' + i" [(ngModel)]="row.capitalMovementAmount" inputmode="decimal" /></label>
            <label>Equity dividends <input [name]="'eqDiv' + i" [(ngModel)]="row.dividendsAmount" inputmode="decimal" /></label>
            <label>Equity closing balance <input [name]="'eqClose' + i" [(ngModel)]="row.closingAmount" inputmode="decimal" /></label>
            <label>Equity evidence reference <input [name]="'eqEvidence' + i" [(ngModel)]="row.evidenceReference" maxlength="2000" /></label>
            <button matButton type="button" (click)="form.equityLines.splice(i, 1)">Remove equity line {{ i + 1 }}</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.equityLines.push(newEquityLine())">Add equity line</button>

        <h3>Reserve and component transfers</h3>
        <p><small>Each transfer is a paired, zero-sum movement between the components above; it does not create income or expense. Add the transfer to both component rollforwards and cite its supporting evidence.</small></p>
        @for (row of form.equityTransfers; track $index; let i = $index) {
          <fieldset class="movement">
            <legend>Equity transfer {{ i + 1 }}</legend>
            <label>Transfer reference <input [name]="'eqTransferRef' + i" [(ngModel)]="row.transferReference" maxlength="100" /></label>
            <label>From component <select [name]="'eqTransferSource' + i" [(ngModel)]="row.sourceLineCode"><option value="">Choose component</option>@for (component of form.equityLines; track $index) { <option [value]="component.lineCode">{{ component.lineCode || 'Uncoded component' }}</option> }</select></label>
            <label>To component <select [name]="'eqTransferDestination' + i" [(ngModel)]="row.destinationLineCode"><option value="">Choose component</option>@for (component of form.equityLines; track $index) { <option [value]="component.lineCode">{{ component.lineCode || 'Uncoded component' }}</option> }</select></label>
            <label>Transfer amount <input [name]="'eqTransferAmount' + i" [(ngModel)]="row.amount" inputmode="decimal" /></label>
            <label>Transfer evidence reference <input [name]="'eqTransferEvidence' + i" [(ngModel)]="row.evidenceReference" maxlength="2000" /></label>
            <button matButton type="button" (click)="form.equityTransfers.splice(i, 1)">Remove equity transfer {{ i + 1 }}</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.equityTransfers.push(newEquityTransfer())">Add reserve/component transfer</button>

        <h2>Numeric note lines</h2>
        <p><small>Each note amount must cross-cast to the statement face destination it feeds. Enter the face destination code exactly as the approved mapping defines it.</small></p>
        @for (row of form.noteLines; track $index; let i = $index) {
          <fieldset class="movement">
            <legend>Note line {{ i + 1 }}</legend>
            <label>Note code <input [name]="'nlCode' + i" [(ngModel)]="row.noteCode" maxlength="100" /></label>
            <label>Face destination code <input [name]="'nlFace' + i" [(ngModel)]="row.faceDestinationCode" maxlength="100" /></label>
            <label>Note amount <input [name]="'nlAmount' + i" [(ngModel)]="row.amount" inputmode="decimal" /></label>
            <label>Note evidence reference <input [name]="'nlEvidence' + i" [(ngModel)]="row.evidenceReference" maxlength="2000" /></label>
            <h3>Source attribution (optional)</h3>
            <p><small>Accepted general-ledger links evidence provenance only; they do not approve the note content.</small></p>
            @if (row.allocations.length) {
              <ul>
                @for (allocation of row.allocations; track $index; let j = $index) {
                  <li><code>{{ allocation.generalLedgerLineId }}</code>
                    <label>Signed contribution <input [name]="'nlAlloc' + i + '-' + j" [(ngModel)]="allocation.signedAmount" inputmode="decimal" /></label>
                    <button matButton type="button" (click)="row.allocations.splice(j, 1)">Remove source link</button></li>
                }
              </ul>
              @if (noteAllocationIssue(row); as issue) { <p role="alert">{{ issue }}</p> }
            }
            <button matButton="outlined" type="button" (click)="openNoteSources(i)">{{ notePickerFor() === i ? 'Hide source lines' : 'Choose accepted source lines' }}</button>
            <button matButton type="button" (click)="form.noteLines.splice(i, 1)">Remove note line {{ i + 1 }}</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.noteLines.push(newNoteLine())">Add note line</button>

        <h2>FX cash effects</h2>
        <p><small>Explicitly assess the period FX cash population. Zero is an evidenced assessment state, never an implied default.</small></p>
        <label>FX cash-effect assessment
          <select name="fxAssessmentState" [(ngModel)]="form.fxAssessmentState">
            <option value="">Not assessed</option><option value="ASSESSED_ZERO">Assessed zero</option><option value="EFFECTS_RECORDED">Effects recorded</option>
          </select>
        </label>
        @if (form.fxAssessmentState) {
          <label>FX assessment evidence reference <input name="fxAssessmentEvidence" [(ngModel)]="form.fxAssessmentEvidence" maxlength="2000" /></label>
          <label>FX assessment rationale <textarea name="fxAssessmentRationale" [(ngModel)]="form.fxAssessmentRationale" maxlength="2000" rows="2"></textarea></label>
        }
        <p><small>FX effects and this assessment require independent review with the cash-flow source links before statement presentation can pass.</small></p>
        @for (row of form.fxCashEffects; track $index; let i = $index) {
          <fieldset class="movement">
            <legend>FX cash effect {{ i + 1 }}</legend>
            <label>FX currency pair <input [name]="'fxPair' + i" [(ngModel)]="row.currencyPair" maxlength="20" /></label>
            <label>FX amount <input [name]="'fxAmount' + i" [(ngModel)]="row.amount" inputmode="decimal" /></label>
            <label>FX evidence reference <input [name]="'fxEvidence' + i" [(ngModel)]="row.evidenceReference" maxlength="2000" /></label>
            <button matButton type="button" (click)="form.fxCashEffects.splice(i, 1)">Remove FX cash effect {{ i + 1 }}</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.fxCashEffects.push(newFxCashEffect())">Add FX cash effect</button>

        <h2>Disclosures</h2>
        @for (d of form.disclosures; track $index; let k = $index) {
          <fieldset class="movement">
            <legend>Disclosure {{ k + 1 }}</legend>
            <label>Code <input [name]="'dcode' + k" [(ngModel)]="d.code" maxlength="100" /></label>
            <label><input type="checkbox" [name]="'dna' + k" [(ngModel)]="d.notApplicable" /> Not applicable</label>
            @if (d.notApplicable) {
              <label>Rationale <textarea [name]="'drat' + k" [(ngModel)]="d.rationale" maxlength="2000" rows="2"></textarea></label>
            } @else {
              <label>Response <textarea [name]="'dresp' + k" [(ngModel)]="d.response" maxlength="4000" rows="2"></textarea></label>
            }
            <button matButton type="button" (click)="form.disclosures.splice(k, 1)">Remove disclosure</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.disclosures.push(newDisclosure())">Add disclosure</button>

        @let openBlockers = blockers();
        <section class="panel" aria-labelledby="schedule-blockers-heading">
          <h2 id="schedule-blockers-heading">Schedules still needed</h2>
          @if (openBlockers.length) {
            <p>Queuing now produces a draft. The schedules below must be supplied before the build can be reviewed. This list is guidance; the server validates every schedule when the build runs.</p>
            <ul>@for (b of openBlockers; track $index) { <li>{{ b }}</li> }</ul>
          } @else {
            <p>This screen lists no open schedules. The server still validates every schedule, and the package stays a draft until it passes review.</p>
          }
        </section>
        <p><small>A queued build is a draft. It is not reviewable until every schedule validates and an independent reviewer records a decision.</small></p>
        <p class="actions"><button matButton="filled" type="submit" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Queuing…' : labelFor(openBlockers) }}</button></p>
      </form>
      @if (formError()) { <p role="alert">{{ formError() }}</p> }
      @if (queued(); as id) {
        <section class="panel" role="status">
          @if (buildStatus.data(); as state) {
            @if (state.status === 'COMPLETED' && state.packageId) {
              <h2>Build finished</h2>
              <p>Operation <code>{{ id }}</code> finished. The package is a draft: open it to see its status. A draft is not reviewable until every schedule validates and an independent reviewer records a decision.</p>
              <a [routerLink]="['/app/accounting/packages', state.packageId]">Open built package</a>
            } @else if (isTerminalBuildState(state.status)) {
              <h2>Build stopped</h2>
              <p>Operation <code>{{ id }}</code> ended in {{ state.status }}. No successful package result was confirmed. Refresh the inputs or ask a firm administrator to review the operation.</p>
            } @else {
              <h2>Build in progress</h2>
              <p>Operation <code>{{ id }}</code> is {{ state.status }} (attempt {{ state.attemptCount }}). The worker is processing it; this is not yet a built package.</p>
            }
            <button matButton="outlined" type="button" (click)="refreshBuildStatus()">Refresh build status</button>
          } @else {
            <h2>Build queued</h2>
            <p>Operation <code>{{ id }}</code> is queued. The worker builds the package; a queued operation is not a built package.</p>
            @if (buildStatus.error()) { <p role="alert">Build status could not be refreshed. You can retry the status read.</p> }
            <button matButton="outlined" type="button" (click)="refreshBuildStatus()">Refresh build status</button>
          }
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class FinancialPackageBuild {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private buildBinding = '';
  readonly id = routeGuid();
  readonly inputs = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/financial-package-inputs` : null),
    decodeBuildInputs, 'The build inputs are not available in your current scope.');
  readonly cmd = new CommandState(this.api);
  readonly form: BuildForm = emptyBuildForm();
  readonly formError = signal('');
  readonly queued = signal<string | null>(null);
  readonly buildStatus = this.api.resource(() => {
    const operationId = this.queued();
    const engagementId = this.id();
    return operationId && engagementId && this.buildBinding === this.bindingKey(operationId)
      ? `/api/ui/engagements/${engagementId}/financial-package-builds/${operationId}`
      : null;
  }, decodeBuildStatus, 'The build status is unavailable in your current scope.');
  readonly pickerFor = signal<number | null>(null);
  readonly notePickerFor = signal<number | null>(null);
  readonly sourcePage = signal(1);
  /** The period the source picker reads. A signal, so changing the period reloads the lines. */
  readonly chosenPeriod = signal('');
  readonly isTerminalBuildState = isTerminalBuildStatus;
  readonly labelFor = queueButtonLabel;

  constructor() {
    effect(() => {
      const operationId = this.queued();
      const binding = operationId ? this.bindingKey(operationId) : '';
      const status = this.buildStatus.data()?.status;
      const error = this.buildStatus.error();
      const loading = this.buildStatus.loading();
      if (!operationId) { this.stopPolling(); return; }
      if (this.buildBinding !== binding) {
        this.stopPolling();
        this.buildBinding = '';
        this.queued.set(null);
        return;
      }
      if (status && TERMINAL_BUILD_STATES.has(status) || this.isDefiniteAccessLoss(error)) {
        this.stopPolling();
        return;
      }
      if (loading) this.stopPolling();
      else this.schedulePoll();
    });
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  private bindingKey(operationId: string): string {
    const actor = this.session.current();
    return [operationId, this.id() ?? '', actor?.firmId ?? '', actor?.userId ?? '', actor?.generation ?? '', this.session.invalidation()].join(':');
  }

  private isDefiniteAccessLoss(error: string): boolean {
    return error === 'Your session ended. Sign in again.' || error === 'The build status is unavailable in your current scope.';
  }

  private schedulePoll(): void {
    if (this.pollTimer !== null) return;
    const operationId = this.queued();
    const binding = operationId ? this.bindingKey(operationId) : '';
    if (!operationId || !binding || this.buildBinding !== binding || this.buildStatus.loading()) return;
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      if (this.queued() !== operationId || this.bindingKey(operationId) !== binding || this.buildStatus.loading() ||
          this.isTerminalBuildStatus(this.buildStatus.data()?.status ?? '') || this.isDefiniteAccessLoss(this.buildStatus.error())) return;
      this.buildStatus.reload();
    }, 3000);
  }

  private stopPolling(): void {
    if (this.pollTimer === null) return;
    clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }

  private isTerminalBuildStatus(status: string): boolean {
    return TERMINAL_BUILD_STATES.has(status as BuildStatus['status']);
  }

  queueBuildOperation(operationId: string): void {
    this.stopPolling();
    this.buildBinding = this.bindingKey(operationId);
    this.queued.set(operationId);
  }

  refreshBuildStatus(): void {
    if (this.buildStatus.loading() || this.isTerminalBuildStatus(this.buildStatus.data()?.status ?? '') ||
        this.isDefiniteAccessLoss(this.buildStatus.error())) return;
    this.stopPolling();
    this.buildStatus.reload();
  }
  readonly sources = this.api.resource(() => {
    const periodId = this.chosenPeriod();
    return this.id() && periodId && (this.pickerFor() !== null || this.notePickerFor() !== null)
      ? `/api/ui/engagements/${this.id()}/specialist-preparation/source-lines?periodId=${periodId}&page=${this.sourcePage()}`
      : null;
  }, decodeScheduleSources, 'The accepted general-ledger lines are unavailable in your current scope.');

  newMovement(): MovementForm { return emptyMovement(); }
  projection(m: MovementForm): string | null { return movementProjection(m); }
  newDisclosure(): DisclosureForm { return { code: '', response: '', notApplicable: false, rationale: '' }; }
  newEquityLine(): EquityLineForm { return emptyEquityLine(); }
  newEquityTransfer(): EquityTransferForm { return emptyEquityTransfer(); }
  newNoteLine(): NoteLineForm { return emptyNoteLine(); }
  newFxCashEffect(): FxCashEffectForm { return emptyFxCashEffect(); }

  openMovementSources(movement: number): void {
    this.notePickerFor.set(null);
    this.pickerFor.set(this.pickerFor() === movement ? null : movement);
    this.sourcePage.set(1);
  }

  openNoteSources(note: number): void {
    this.pickerFor.set(null);
    this.notePickerFor.set(this.notePickerFor() === note ? null : note);
    this.sourcePage.set(1);
  }

  addSelectedSource(lineId: string, signedAmount: string): void {
    const note = this.notePickerFor();
    if (note !== null) {
      const row = this.form.noteLines[note];
      if (row && !row.allocations.some((allocation) => allocation.generalLedgerLineId === lineId))
        row.allocations.push({ generalLedgerLineId: lineId, signedAmount });
      return;
    }
    const movement = this.pickerFor();
    if (movement !== null) this.addSource(movement, lineId, signedAmount);
  }

  addSelectedScheduleSource(scheduleId: string, componentCode: string, signedAmount: string): void {
    const movement = this.pickerFor();
    const row = movement === null ? null : this.form.movements[movement];
    if (!row || row.allocations.length || !row.isNonCash ||
        (componentCode !== 'DEPRECIATION' && componentCode !== 'IMPAIRMENT') ||
        row.scheduleAllocations.some(x => x.specialistScheduleId === scheduleId && x.componentCode === componentCode)) return;
    row.scheduleAllocations.push({ specialistScheduleId: scheduleId, componentCode, signedAmount });
  }

  noteAllocationIssue(row: NoteLineForm): string | null {
    if (!row.allocations.length) return null;
    if (!AMOUNT.test(row.amount) || row.allocations.some((allocation) => !GUID.test(allocation.generalLedgerLineId) ||
      !AMOUNT.test(allocation.signedAmount) || toMicro(allocation.signedAmount) === 0n))
      return 'Enter valid note and signed contribution amounts with at most six decimals.';
    if (new Set(row.allocations.map((allocation) => allocation.generalLedgerLineId.toLowerCase())).size !== row.allocations.length)
      return 'A general-ledger line can be linked to this note only once.';
    return sumContributions(row.allocations) === toMicro(row.amount)
      ? null : 'Signed source contributions must total the note amount exactly.';
  }

  /** The open schedules and lineage gaps for the current form. Recomputed on each check, so it follows every edit. */
  blockers(): string[] { return scheduleBlockers(this.form); }

  /** Only plans finalized on the chosen mapping's dataset can build that mapping, so the list follows the mapping. */
  plansFor(data: BuildInputs) {
    const mapping = data.mappings.find((m) => m.id === this.form.mappingVersionId);
    return mapping ? data.plans.filter((p) => p.baseDatasetId === mapping.datasetId) : [];
  }

  comparativesFor(data: BuildInputs) {
    const period = data.periods.find((p) => p.id === this.form.periodId);
    return period ? data.comparatives.filter((p) => p.status === 'VALIDATED' && p.framework === this.form.framework.trim() &&
      p.currency === period.currency && (!p.basis || p.basis.toUpperCase() === period.basis.toUpperCase()) &&
      p.periodEnd < period.startDate) : [];
  }

  choosePeriod(data: BuildInputs): void {
    const period = data.periods.find((p) => p.id === this.form.periodId);
    this.form.periodStart = period?.startDate ?? '';
    this.form.periodEnd = period?.endDate ?? '';
    this.clearComparative(data);
    this.chosenPeriod.set(this.form.periodId);
    this.sourcePage.set(1);
  }

  clearComparative(data: BuildInputs): void {
    if (this.form.comparativePackageId && !this.comparativesFor(data).some((x) => x.id === this.form.comparativePackageId))
      this.form.comparativePackageId = '';
  }

  addSource(movement: number, lineId: string, signedAmount: string): void {
    const m = this.form.movements[movement];
    if (!m || m.scheduleAllocations.length || m.allocations.some((a) => a.generalLedgerLineId === lineId)) return;
    m.allocations.push({ generalLedgerLineId: lineId, signedAmount });
  }

  submit(): void {
    this.formError.set('');
    const built = toBuildRequest(this.form);
    if ('error' in built) { this.formError.set(built.error); return; }
    void this.cmd.run<string>('/api/ui/accounting/packages/builds', built.body, 'Package build queued.', (operation) => this.queueBuildOperation(operation));
  }
}
