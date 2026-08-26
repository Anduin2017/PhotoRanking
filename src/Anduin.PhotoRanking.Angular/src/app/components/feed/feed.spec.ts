import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { FeedComponent } from './feed';
import { Photo } from '../../services/photo';

describe('FeedComponent', () => {
  let component: FeedComponent;
  let fixture: ComponentFixture<FeedComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FeedComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    })
    .compileComponents();

    fixture = TestBed.createComponent(FeedComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('offers the complete zero-to-six rating scale', () => {
    expect(component.scoreOptions).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });

  it('keeps a rated card and reveals the blind prediction', () => {
    const photo: Photo = {
      id: 42,
      filePath: 'photo.jpg',
      albumId: 'album',
      viewCount: 0,
      predictedScore: 4.37,
      createdAt: '2026-08-27T00:00:00Z'
    };
    component.photos = [photo];

    component.onViewerRated({ ...photo, manualScore: 5 });
    fixture.detectChanges();

    expect(component.photos).toHaveLength(1);
    expect(component.photos[0].manualScore).toBe(5);
    expect(component.photos[0].predictedScore).toBe(4.37);
    const comparison = fixture.nativeElement.querySelector('.rating-comparison');
    expect(comparison.textContent).toContain('你的最终分');
    expect(comparison.textContent).toContain('AI 评分前预测');
    expect(comparison.textContent).toContain('4.37');
  });
});
