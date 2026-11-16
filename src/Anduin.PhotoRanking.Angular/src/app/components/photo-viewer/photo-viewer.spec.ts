import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PhotoViewerComponent } from './photo-viewer';
import { PhotoService } from '../../services/photo';
import { Router } from '@angular/router';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { Photo } from '../../services/photo';

describe('PhotoViewerComponent', () => {
  let component: PhotoViewerComponent;
  let fixture: ComponentFixture<PhotoViewerComponent>;

  const photoServiceMock = {
    getImageUrl: (path: string) => path,
    getSummary: (id: number) => of<{ summary: string | null }>({ summary: null }),
    getPhoto: (id: number) => of({}),
    viewPhoto: (id: number) => of({}),
    ratePhoto: (id: number, score: number) => of({}),
    deletePhoto: (id: number) => of({}),
    guessScore: (id: number) => of({ predictedScore: 0, votes: {} })
  };

  const routerMock = {
    navigate: () => {}
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PhotoViewerComponent],
      providers: [
        { provide: PhotoService, useValue: photoServiceMock },
        { provide: Router, useValue: routerMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(PhotoViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('offers the complete zero-to-six rating scale', () => {
    expect(component.scoreOptions).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });

  it('accepts zero and one as keyboard ratings', () => {
    const ratedScores: number[] = [];
    component.ratePhoto = (score: number) => ratedScores.push(score);

    component.handleKeyboardEvent(new KeyboardEvent('keydown', { key: '0' }));
    component.handleKeyboardEvent(new KeyboardEvent('keydown', { key: '1' }));

    expect(ratedScores).toEqual([0, 1]);
  });

  it('emits the updated photo so the feed can keep and reveal it', () => {
    const updatedPhoto: Photo = {
      id: 42,
      filePath: 'photo.jpg',
      albumId: 'album',
      viewCount: 0,
      manualScore: 5,
      predictedScore: 4.37,
      createdAt: '2026-08-27T00:00:00Z'
    };
    photoServiceMock.ratePhoto = () => of(updatedPhoto);
    component.currentPhoto = { ...updatedPhoto, manualScore: null };
    let emittedPhoto: Photo | undefined;
    component.rated.subscribe(photo => emittedPhoto = photo);

    component.ratePhoto(5);

    expect(emittedPhoto).toEqual(updatedPhoto);
  });
  it('ignores a previous photo summary arriving after navigation', () => {
    const oldResponse = new Subject<{ summary: string | null }>();
    const newResponse = new Subject<{ summary: string | null }>();
    vi.spyOn(photoServiceMock, 'getSummary')
      .mockReturnValueOnce(oldResponse).mockReturnValueOnce(newResponse);
    component.updateOverlay(1);
    component.updateOverlay(2);
    newResponse.next({ summary: 'current poem' });
    oldResponse.next({ summary: 'previous poem' });
    expect(component.summary).toBe('current poem');
  });

  it('enables summaries by default and lets the summary wheel avoid photo zoom', () => {
    expect(component.showSummary).toBe(true);
    const zoom = { in: vi.fn(), out: vi.fn() };
    component.swiper = { zoom } as any;
    const scroller = document.createElement('div');
    scroller.className = 'summary-scroller';
    const event = new WheelEvent('wheel', { deltaY: 10 });
    Object.defineProperty(event, 'target', { value: scroller });
    component.onWheel(event);
    expect(zoom.out).not.toHaveBeenCalled();
    component.swiper = null;
  });

});
