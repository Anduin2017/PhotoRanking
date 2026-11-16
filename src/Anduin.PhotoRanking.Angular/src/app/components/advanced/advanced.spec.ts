import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { of } from 'rxjs';
import { vi } from 'vitest';
import { AdvancedComponent } from './advanced';

describe('AdvancedComponent', () => {
  let component: AdvancedComponent;
  let fixture: ComponentFixture<AdvancedComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdvancedComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdvancedComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('saves slider coverage without saving unfinished prompt edits', () => {
    const saved = { prompt1: 'one', prompt2: 'two', prompt3: 'three', topPercent: 3 };
    component.savedSummarySettings = { ...saved };
    component.summarySettings = { ...saved, prompt3: 'unfinished edit', topPercent: 7 };
    const save = vi.spyOn(component.photoService, 'saveSummarySettings').mockReturnValue(of({ ...saved, topPercent: 7 }));
    component.saveSummaryPercent();
    expect(save).toHaveBeenCalledWith({ ...saved, topPercent: 7 });
    expect(component.summarySettings?.prompt3).toBe('unfinished edit');
    expect(component.savedSummarySettings?.prompt3).toBe('three');
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
