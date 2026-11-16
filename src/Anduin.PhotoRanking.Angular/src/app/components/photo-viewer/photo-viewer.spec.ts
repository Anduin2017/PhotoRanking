import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PhotoViewerComponent } from './photo-viewer';
import { PhotoService } from '../../services/photo';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { Photo } from '../../services/photo';

describe('PhotoViewerComponent', () => {
  let component: PhotoViewerComponent;
  let fixture: ComponentFixture<PhotoViewerComponent>;

  const photoServiceMock = {
    getImageUrl: (path: string) => path,
    getSummary: (id: number) => of<{ summary: string | null }>({ summary: null }),
    getSummarySpeech: (id: number, summary: string) => of(new Blob(['audio'])),
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
    for (const [start, end, text] of [[2000, 6000, 'second'], [8000, 12000, 'first'], [14000, 18000, 'third']] as const) {
      clock.mockReturnValue(start); component.scrollSummary();
      clock.mockReturnValue(end - 1); component.scrollSummary();
      expect(component.summary).not.toBe(text);
      clock.mockReturnValue(end); component.scrollSummary();
      expect(component.summary).toBe(text);
    }
  });

  it('starts untouched summaries after two seconds and scrolls one actual line every two seconds', () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(0);
    vi.spyOn(photoServiceMock, 'getSummary').mockReturnValue(of({ summary: 'long poem' }));
    component.updateOverlay(1);
    fixture.detectChanges();
    const scroller = component.summaryScroller!.nativeElement;
    scroller.style.lineHeight = '37px';
    Object.defineProperty(scroller, 'clientHeight', { value: 100 });
    Object.defineProperty(scroller, 'scrollHeight', { value: 1000 });
    clock.mockReturnValue(1999); component.scrollSummary();
    expect(scroller.scrollTop).toBe(0);
    for (let time = 2000; time <= 4000; time += 100) {
      clock.mockReturnValue(time); component.scrollSummary();
    }
    expect(scroller.scrollTop).toBeCloseTo(37, 0);
    component.pauseSummaryScroll(new Event('wheel'));
    clock.mockReturnValue(13000); component.scrollSummary();
    expect(scroller.scrollTop).toBeCloseTo(37, 0);
    clock.mockReturnValue(14000); component.scrollSummary();
    expect(scroller.scrollTop).toBeCloseTo(37, 0);
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

describe('Summary narration', () => {
  let fixture: ComponentFixture<PhotoViewerComponent>;
  let component: PhotoViewerComponent;
  let audio: any;
  let speech: ReturnType<typeof vi.fn>;
  beforeEach(async () => {
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: vi.fn().mockReturnValue({ matches: false }) });
    audio = { play: vi.fn().mockResolvedValue(undefined), pause: vi.fn(), load: vi.fn(),
      removeAttribute: vi.fn(), src: '', onended: null, onerror: null };
    vi.stubGlobal('Audio', class { constructor() { return audio; } });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:synthetic-audio');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    speech = vi.fn().mockReturnValue(of(new Blob(['audio'])));
    await TestBed.configureTestingModule({ imports: [PhotoViewerComponent], providers: [
      { provide: Router, useValue: { navigate: vi.fn() } },
      { provide: PhotoService, useValue: {
        getSummary: () => of({ summary: 'third poem', response2: 'second', response1: 'first' }),
        getSummarySpeech: speech, getPhoto: () => of({}), viewPhoto: () => of({}), getImageUrl: () => ''
      } }
    ] }).compileComponents();
    fixture = TestBed.createComponent(PhotoViewerComponent);component = fixture.componentInstance;
    fixture.detectChanges();component.updateOverlay(1);
  });
  afterEach(() => { fixture?.destroy();vi.unstubAllGlobals();vi.restoreAllMocks(); });

  it('is off by default and reads only the final answer once despite polling', async () => {
    expect(speech).not.toHaveBeenCalled();
    component.autoReadSummary = true;component.toggleSummarySpeech();await Promise.resolve();
    expect(speech).toHaveBeenCalledExactlyOnceWith(1, 'third poem');
    expect(component.speechState).toBe('playing');
    component.refreshSummary();audio.onended();component.refreshSummary();
    expect(speech).toHaveBeenCalledTimes(1);
  });

  it('holds round three during generation and playback, then resumes text cycling', async () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(0);
    const pending = new Subject<Blob>();speech.mockReturnValue(pending);
    component.autoReadSummary = true;component.toggleSummarySpeech();fixture.detectChanges();
    const scroller = component.summaryScroller!.nativeElement;
    Object.defineProperty(scroller, 'clientHeight', { value: 100 });
    Object.defineProperty(scroller, 'scrollHeight', { value: 100 });
    clock.mockReturnValue(2000);component.scrollSummary();
    clock.mockReturnValue(10000);component.scrollSummary();expect(component.summaryIndex).toBe(0);
    pending.next(new Blob(['audio']));await Promise.resolve();
    clock.mockReturnValue(20000);component.scrollSummary();expect(component.summaryIndex).toBe(0);
    audio.onended();component.scrollSummary();
    clock.mockReturnValue(24000);component.scrollSummary();expect(component.summaryIndex).toBe(1);
  });

  it('shows queue position and segment progress without starting audio early', async () => {
    const pending = new Subject<any>();speech.mockReturnValue(pending);
    component.autoReadSummary = true;component.toggleSummarySpeech();
    pending.next({ status: 'queued', queue_position: 4 });
    expect(component.speechMessage).toBe('语音排队中，第4位');expect(audio.play).not.toHaveBeenCalled();
    pending.next({ status: 'running', completed_segments: 2, total_segments: 5 });
    expect(component.speechMessage).toBe('正在合成语音，2/5段');expect(component.speechState).toBe('loading');
    pending.next(new Blob(['audio']));await Promise.resolve();expect(component.speechState).toBe('playing');
  });

  it('explains queue capacity failures without retrying automatically', () => {
    speech.mockReturnValue(throwError(() => ({ status: 429 })));
    component.autoReadSummary = true;component.toggleSummarySpeech();
    expect(component.speechMessage).toBe('语音队列已满，请稍后重试');expect(speech).toHaveBeenCalledTimes(1);
  });

  it('offers click-to-play when blocked without synthesizing again', async () => {
    audio.play.mockRejectedValueOnce(new DOMException('Blocked', 'NotAllowedError'));
    component.autoReadSummary = true;component.toggleSummarySpeech();await Promise.resolve();
    expect(component.speechState).toBe('blocked');
    await component.playSummarySpeech();expect(component.speechState).toBe('playing');
    expect(speech).toHaveBeenCalledTimes(1);
  });

  it('cancels old requests on navigation and stops on disabling playback', async () => {
    const pending = new Subject<Blob>();speech.mockReturnValueOnce(pending);
    component.autoReadSummary = true;component.toggleSummarySpeech();component.updateOverlay(2);
    await Promise.resolve();pending.next(new Blob(['old audio']));
    expect(speech.mock.calls.map(x => x[0])).toEqual([1, 2]);
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    component.autoReadSummary = false;component.toggleSummarySpeech();
    expect(audio.pause).toHaveBeenCalled();expect(URL.revokeObjectURL).toHaveBeenCalled();
    expect(component.speechState).toBe('idle');
  });

  it('stops narration when polling invalidates the final answer', async () => {
    component.autoReadSummary = true;component.toggleSummarySpeech();await Promise.resolve();
    vi.spyOn(TestBed.inject(PhotoService), 'getSummary').mockReturnValue(of({ summary: null }));
    component.refreshSummary();
    expect(component.speechState).toBe('idle');expect(component.summaryResponses).toEqual([]);
    expect(audio.pause).toHaveBeenCalled();expect(URL.revokeObjectURL).toHaveBeenCalled();
  });

  it('does not resume an old play promise after playback is disabled', async () => {
    let finish!: () => void;
    audio.play.mockReturnValueOnce(new Promise<void>(resolve => finish = resolve));
    component.autoReadSummary = true;component.toggleSummarySpeech();
    component.autoReadSummary = false;component.toggleSummarySpeech();
    finish();await Promise.resolve();expect(component.speechState).toBe('idle');
  });

  it('offers generation retry and stops on manual earlier rounds and closing', async () => {
    speech.mockReturnValueOnce(throwError(() => ({ status: 503 })));
    component.autoReadSummary = true;component.toggleSummarySpeech();
    expect(component.speechMessage).toBe('语音服务尚未配置');
    component.retrySummarySpeech();await Promise.resolve();expect(component.speechState).toBe('playing');
    component.selectSummaryResponse(1);expect(component.speechState).toBe('idle');
    component.onClose();expect(component.autoReadSummary).toBe(false);
  });
});
