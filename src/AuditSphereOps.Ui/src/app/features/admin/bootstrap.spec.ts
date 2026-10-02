import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InstallationBootstrap, bootstrapStatus } from './bootstrap';
import { decode } from '../../core/decode';

describe('Initial administrator proof handling', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [InstallationBootstrap], providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });

  it('clears the proof immediately and fences repeated writes after a lost response', async () => {
    const fixture = TestBed.createComponent(InstallationBootstrap), http = TestBed.inject(HttpTestingController), component = fixture.componentInstance;
    http.expectOne('/api/setup/session').flush({ bound: false, canBootstrap: true });
    await fixture.whenStable();
    component.form.setValue({ proof: 'synthetic-one-time-proof', reviewed: true });
    const completed = component.complete();
    expect(component.form.controls.proof.value).toBe('');
    const request = http.expectOne('/api/setup/bootstrap');
    expect(request.request.body).toEqual({ proof: 'synthetic-one-time-proof', reviewed: true });
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    await completed;
    expect(component.uncertain()).toBe(true);
    component.form.setValue({ proof: 'synthetic-new-proof', reviewed: true });
    await component.complete(); http.expectNone('/api/setup/bootstrap');
    fixture.destroy(); expect(component.form.controls.proof.value).toBe('');
  });

  it('never permits a bootstrap write when setup is closed', async () => {
    const fixture = TestBed.createComponent(InstallationBootstrap), http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/setup/session').flush({ bound: true, canBootstrap: false });
    await fixture.whenStable();
    fixture.componentInstance.form.setValue({ proof: 'synthetic-proof', reviewed: true });
    await fixture.componentInstance.complete(); http.expectNone('/api/setup/bootstrap');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#installation-proof')).toBeNull();
  });

  it('refuses malformed or unauthorized setup state', async () => {
    expect(() => decode(bootstrapStatus, { bound: false, canBootstrap: 'yes' })).toThrow();
    const fixture = TestBed.createComponent(InstallationBootstrap), http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/setup/session').flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.componentInstance.status()).toBeNull();
    expect(fixture.nativeElement.querySelector('#installation-proof')).toBeNull();
  });
});
