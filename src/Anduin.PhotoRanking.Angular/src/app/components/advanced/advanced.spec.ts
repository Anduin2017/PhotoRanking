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

  it('shows summary progress and opens a completed example in the viewer', () => {
    const photo = { id: 42, filePath: 'example.jpg', albumId: 'test', viewCount: 0, createdAt: '' };
    component.summarySettings = { prompt1: 'one', prompt2: 'two', prompt3: 'three', topPercent: 3,
      configured: true, completedCount: 1, targetCount: 3, examples: [photo] };
    fixture.detectChanges();
    const progress = fixture.nativeElement.querySelector('progress') as HTMLProgressElement;
    expect(progress.value).toBe(1);
    expect(progress.max).toBe(3);
    fixture.nativeElement.querySelector('.summary-examples button').click();
    expect(component.viewerOpen).toBe(true);
    expect(component.initialPhotoId).toBe(42);
    expect(component.viewerPhotos).toEqual([photo]);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
