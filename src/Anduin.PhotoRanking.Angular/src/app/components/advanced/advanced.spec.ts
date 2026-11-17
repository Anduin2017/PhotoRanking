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

  it('shows saved rounds and distinguishes first-token wait from generation', () => {
    component.summarySettings = { prompt1: 'one', prompt2: 'two', prompt3: 'three', topPercent: 3,
      configured: true, generation: { photoId: 42, round: 2, completedRounds: 1, phase: 'waiting',
        waitingSeconds: 12, generatingSeconds: 0, nextRetryAt: null, error: null } };
    fixture.detectChanges();
    let panel = fixture.nativeElement.querySelector('.summary-generation') as HTMLElement;
    expect(panel.textContent).toContain('第 2 / 3 轮');
    expect(panel.textContent).toContain('已保存 1 / 3 轮');
    expect(panel.textContent).toContain('等待首字（含排队和图片预处理）');
    component.summarySettings.generation = { ...component.summarySettings.generation!, phase: 'generating', generatingSeconds: 8 };
    fixture.detectChanges();
    panel = fixture.nativeElement.querySelector('.summary-generation') as HTMLElement;
    expect(panel.textContent).toContain('正在生成回答');
    expect(panel.textContent).toContain('生成 8 秒');
  });

  it('shows retry timing and errors without making a partial round a completed photo', () => {
    component.summarySettings = { prompt1: 'one', prompt2: 'two', prompt3: 'three', topPercent: 3,
      configured: true, completedCount: 0, targetCount: 1,
      generation: { photoId: 42, round: 3, completedRounds: 2, phase: 'retrying',
        waitingSeconds: 2, generatingSeconds: 15, nextRetryAt: '2026-11-17T10:00:00Z', error: 'Output budget exhausted' } };
    fixture.detectChanges();
    const panel = fixture.nativeElement.querySelector('.summary-generation') as HTMLElement;
    expect(panel.textContent).toContain('等待重试，将继续未完成的一轮');
    expect(panel.textContent).toContain('已保存 2 / 3 轮');
    expect(panel.querySelector('[role="alert"]')?.textContent).toContain('Output budget exhausted');
    expect((fixture.nativeElement.querySelector('progress') as HTMLProgressElement).value).toBe(0);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
