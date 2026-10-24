export type FieldworkCommandRecovery = 'tailoring' | 'schedule-source-recovery';
export type FieldworkCommandAcquire = (recovery?: FieldworkCommandRecovery) => (() => void) | null;
