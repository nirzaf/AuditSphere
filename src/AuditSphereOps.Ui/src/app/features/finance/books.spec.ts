import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { vi } from 'vitest';
import { FirmExpenseRejectionDialog } from './books';

describe('firm expense rejection reason', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [FirmExpenseRejectionDialog],
    providers: [{ provide: MAT_DIALOG_DATA, useValue: 1000 }, { provide: MatDialogRef, useValue: { close: vi.fn() } }],
  }));

  afterEach(() => TestBed.resetTestingModule());

  it('requires a nonblank reviewer reason and trims it before closing', async () => {
    const fixture = TestBed.createComponent(FirmExpenseRejectionDialog);
    fixture.detectChanges();
    const dialog = fixture.componentInstance;
    const close = TestBed.inject(MatDialogRef).close;
    const submit = fixture.nativeElement.querySelector('button[matButton="filled"]') as HTMLButtonElement;
    const reason = fixture.nativeElement.querySelector('#expense-rejection-reason') as HTMLTextAreaElement;

    const enter = async (value: string) => {
      reason.value = value;
      reason.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      fixture.detectChanges();
    };

    expect(submit.disabled).toBe(true);
    await enter('   ');
    expect(submit.disabled).toBe(true);
    dialog.submit();
    expect(close).not.toHaveBeenCalled();

    await enter('  Missing supporting receipt.  ');
    expect(submit.disabled).toBe(false);
    submit.click();
    await fixture.whenStable();
    expect(close).toHaveBeenCalledWith('Missing supporting receipt.');
    fixture.destroy();
  });
});
