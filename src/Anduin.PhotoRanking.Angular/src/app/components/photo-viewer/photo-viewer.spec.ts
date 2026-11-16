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

    Object.defineProperty(window, 'matchMedia', { configurable: true, value: vi.fn().mockReturnValue({ matches: false }) });
    fixture = TestBed.createComponent(PhotoViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => vi.restoreAllMocks());

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

  it('plays all saved answers in 3-2-1 order and loops only after the end hold', () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(0);
    vi.spyOn(photoServiceMock, 'getSummary').mockReturnValue(of({ summary: 'third', response2: 'second', response1: 'first' }));
    component.updateOverlay(1);
    fixture.detectChanges();
    const scroller = component.summaryScroller!.nativeElement;
    Object.defineProperty(scroller, 'clientHeight', { value: 100 });
    Object.defineProperty(scroller, 'scrollHeight', { value: 100 });
    expect(component.summaryResponses.map(x => x.round)).toEqual([3, 2, 1]);
    expect(component.summary).toBe('third');
    for (const [start, end, text] of [[3000, 7000, 'second'], [10000, 14000, 'first'], [17000, 21000, 'third']] as const) {
      clock.mockReturnValue(start); component.scrollSummary();
      clock.mockReturnValue(end - 1); component.scrollSummary();
      expect(component.summary).not.toBe(text);
      clock.mockReturnValue(end); component.scrollSummary();
      expect(component.summary).toBe(text);
    }
  });

  it('keeps the current answer and scroll position when polling unchanged responses', () => {
    vi.spyOn(photoServiceMock, 'getSummary').mockReturnValue(of({ summary: 'third', response2: 'second', response1: 'first' }));
    component.updateOverlay(1);
    component.selectSummaryResponse(1);
    fixture.detectChanges();
    component.summaryScroller!.nativeElement.scrollTop = 42;
    component.refreshSummary();
    expect(component.summary).toBe('second');
    expect(component.summaryIndex).toBe(1);
    expect(component.summaryScroller!.nativeElement.scrollTop).toBe(42);
  });

  it('supports legacy final-only answers and clears every round on invalidation', () => {
    const request = vi.spyOn(photoServiceMock, 'getSummary').mockReturnValue(of({ summary: 'legacy poem' }));
    component.updateOverlay(1);
    expect(component.summaryResponses).toEqual([{ round: 3, text: 'legacy poem' }]);
    request.mockReturnValue(of({ summary: null }));
    component.refreshSummary();
    expect(component.summary).toBeNull();
    expect(component.summaryResponses).toEqual([]);
  });

  it('keeps manual round selection available with reduced motion and pauses after interaction', () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(0);
    vi.spyOn(photoServiceMock, 'getSummary').mockReturnValue(of({ summary: 'third', response2: 'second', response1: 'first' }));
    component.updateOverlay(1);
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('.summary-rounds button');
    expect([...buttons].map((b: any) => b.textContent.trim())).toEqual(['第3轮', '第2轮', '第1轮']);
    buttons[1].click();
    expect(component.summary).toBe('second');
    component.pauseSummaryScroll(new Event('wheel'));
    clock.mockReturnValue(9000); component.scrollSummary();
    expect(component.summary).toBe('second');
    vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: true } as MediaQueryList);
    clock.mockReturnValue(30000); component.scrollSummary();
    expect(component.summary).toBe('second');
    buttons[2].click(); expect(component.summary).toBe('first');
  });

});
