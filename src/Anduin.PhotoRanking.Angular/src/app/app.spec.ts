import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app';
import { DOCUMENT } from '@angular/common';
import { recoverChunkNavigation } from './app.config';
import { vi } from 'vitest';
import { NavigationError, provideRouter, Router } from '@angular/router';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([])]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });
});

describe('Navigation after a deployment', () => {
  it('opens the requested statistics URL with the current shell when its lazy chunk is missing', () => {
    const assign = vi.fn();
    TestBed.configureTestingModule({ providers: [
      { provide: Router, useValue: { navigated: true } },
      { provide: DOCUMENT, useValue: { location: { assign } } }
    ] });
    TestBed.runInInjectionContext(() => recoverChunkNavigation(new NavigationError(2, '/advanced',
      new TypeError('Failed to fetch dynamically imported module: https://example.test/old-chunk.js'))));
    expect(assign).toHaveBeenCalledExactlyOnceWith('/advanced');
  });

  it('does not reload an initial failure or unrelated navigation errors', () => {
    const assign = vi.fn();
    const router = { navigated: false };
    TestBed.configureTestingModule({ providers: [
      { provide: Router, useValue: router },
      { provide: DOCUMENT, useValue: { location: { assign } } }
    ] });
    TestBed.runInInjectionContext(() => recoverChunkNavigation(new NavigationError(1, '/advanced',
      new TypeError('Failed to fetch dynamically imported module: missing.js'))));
    router.navigated = true;
    TestBed.runInInjectionContext(() => recoverChunkNavigation(new NavigationError(2, '/advanced',
      new Error('API unavailable'))));
    expect(assign).not.toHaveBeenCalled();
  });
});
