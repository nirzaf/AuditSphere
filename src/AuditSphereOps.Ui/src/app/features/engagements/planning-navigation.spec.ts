import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { engagementNavigationGuard } from './planning-navigation.guard';

@Component({ template: '' })
class NavigationProbe {
  readonly destinations: string[] = [];
  confirmNavigation(destination: string) {
    this.destinations.push(destination);
    return !destination.includes('second');
  }
}

describe('Engagement navigation guard wiring', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('guards both route replacement and reused engagement parameters', async () => {
    const route = routes.find((r) => r.path === 'app/engagements/:id')!;
    expect(route.canDeactivate).toEqual([engagementNavigationGuard]);
    expect(route.runGuardsAndResolvers).toBe('paramsOrQueryParamsChange');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: route.path,
            component: NavigationProbe,
            canDeactivate: route.canDeactivate,
            runGuardsAndResolvers: route.runGuardsAndResolvers,
          },
          { path: 'other', component: NavigationProbe },
        ]),
      ],
    });
    const harness = await RouterTestingHarness.create();
    const probe = await harness.navigateByUrl('/app/engagements/first', NavigationProbe);
    await harness.navigateByUrl('/app/engagements/second');
    expect(TestBed.inject(Router).url).toBe('/app/engagements/first');
    expect(probe.destinations).toEqual(['/app/engagements/second']);
    await harness.navigateByUrl('/app/engagements/first?holdPage=1');
    expect(TestBed.inject(Router).url).toBe('/app/engagements/first?holdPage=1');
    await harness.navigateByUrl('/other');
    expect(probe.destinations).toEqual([
      '/app/engagements/second',
      '/app/engagements/first?holdPage=1',
      '/other',
    ]);
  });
});
