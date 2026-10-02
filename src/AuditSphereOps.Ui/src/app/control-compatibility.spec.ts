import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, FormField } from '@angular/forms/signals';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';

@Component({
  imports: [FormField, MatFormFieldModule, MatInputModule],
  template: '<mat-form-field><mat-label>Client name</mat-label><input matInput [formField]="fields.name"></mat-form-field>',
})
class ControlSmoke {
  readonly model = signal({name: ''});
  readonly fields = form(this.model);
}
@Component({imports: [MatDialogModule], template: '<h2 mat-dialog-title>Review access</h2><mat-dialog-content>Scope remains server-authorized.</mat-dialog-content>'})
class DialogSmoke {}

describe('pinned control compatibility', () => {
  afterEach(() => { TestBed.inject(MatDialog).closeAll(); TestBed.resetTestingModule(); });
  it('updates Signal Forms from a Material input without Zone.js', async () => {
    TestBed.configureTestingModule({imports: [ControlSmoke, MatDialogModule]});
    const fixture = TestBed.createComponent(ControlSmoke);
    await fixture.whenStable();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = 'Client A'; input.dispatchEvent(new Event('input', {bubbles: true}));
    await fixture.whenStable();
    expect(fixture.componentInstance.model().name).toBe('Client A');
    fixture.componentInstance.model.set({name: 'Client B'});
    await fixture.whenStable();
    expect(input.value).toBe('Client B');
  });
  it('renders an accessible Material dialog overlay', async () => {
    TestBed.configureTestingModule({imports: [DialogSmoke, MatDialogModule]});
    const fixture = TestBed.createComponent(DialogSmoke);
    const ref = TestBed.inject(MatDialog).open(DialogSmoke);
    await fixture.whenStable();
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Review access');
    expect(document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby')).toBeTruthy();
    ref.close(); await fixture.whenStable();
  });
});
