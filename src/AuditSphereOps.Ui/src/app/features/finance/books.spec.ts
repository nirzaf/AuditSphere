import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { vi } from 'vitest';
import { FirmBooks, FirmExpenseRejectionDialog } from './books';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';

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

describe('firm expense paging and exact recovery', () => {
  it('navigates continuation and recovers a posting by exact identity independently of the visible page', async () => {
    TestBed.configureTestingModule({ imports: [FirmBooks], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    const userId = '11111111-1111-4111-8111-111111111111';
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true });
    const fixture = TestBed.createComponent(FirmBooks), http = TestBed.inject(HttpTestingController);
    const workspace = { accounts: [], expenses: [], categories: ['OTHER'], canPrepare: false, canReview: false,
      maxEvidenceBytes: 5242880, maxReviewCommentLength: 1000, expensePage: 1, hasMoreExpenses: true };
    const id = '22222222-2222-4222-8222-222222222222';
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne('/api/ui/finance/books').flush(workspace); TestBed.tick(); fixture.detectChanges();
      const next = [...fixture.nativeElement.querySelectorAll('button')].find((b: any) => b.textContent.includes('Next expenses')) as HTMLButtonElement;
      expect(next.disabled).toBe(false); next.click(); TestBed.tick();
      http.expectOne('/api/ui/finance/books?expensePage=2').flush({ ...workspace, expensePage: 2, hasMoreExpenses: false });
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Expense page 2');
      const finalNext = [...fixture.nativeElement.querySelectorAll('button')].find((b: any) => b.textContent.includes('Next expenses')) as HTMLButtonElement;
      expect(finalNext.disabled).toBe(true);
      fixture.componentInstance.pendingPostId.set(id);
      const recovery = fixture.componentInstance.verifyPostState();
      http.expectOne(`/api/ui/finance/books?expenseId=${id}`).flush({ ...workspace, hasMoreExpenses: false, expenses: [{ id,
        expenseDate: '2026-01-01', category: 'OTHER', payee: 'Synthetic supplier', description: 'Retained source', amount: '1.00', currency: 'QAR',
        evidenceFileName: 'source.pdf', evidenceSha256: 'a'.repeat(64), status: 'POSTED', reviewComment: null, preparedByMe: true }] });
      await recovery;
      expect(fixture.componentInstance.postReconciliation()).toBe('POSTED');
      TestBed.tick(); http.match('/api/ui/finance/books?expensePage=2').forEach(request => request.flush({ ...workspace, expensePage: 2, hasMoreExpenses: false }));
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
