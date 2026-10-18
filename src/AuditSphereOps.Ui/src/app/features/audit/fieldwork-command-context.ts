export type FieldworkCommandRecovery = 'tailoring';
export type FieldworkCommandAcquire = (recovery?: FieldworkCommandRecovery) => (() => void) | null;
