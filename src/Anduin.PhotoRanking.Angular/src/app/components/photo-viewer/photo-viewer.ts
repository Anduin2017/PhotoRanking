import { Component, ElementRef, EventEmitter, Input, OnInit, Output, ViewChild, AfterViewInit, OnDestroy, HostListener, ChangeDetectorRef, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { Photo, PhotoService } from '../../services/photo';
import Swiper from 'swiper';
import { Navigation, Virtual, Zoom } from 'swiper/modules';
import { Router } from '@angular/router';

@Component({
  selector: 'app-photo-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './photo-viewer.html',
  styleUrl: './photo-viewer.css',
})
export class PhotoViewerComponent implements OnInit, AfterViewInit, OnDestroy, OnChanges {
  readonly scoreOptions = [0, 1, 2, 3, 4, 5, 6];
  @Input() startPhotoId: number | null = null;
  @Input() photos: Photo[] = [];
  @Input() hasMore = false;
  @Input() autoPlay = false;
  @Output() close = new EventEmitter<void>();
  @Output() requestMore = new EventEmitter<void>();
  @Output() rated = new EventEmitter<Photo>();

  @ViewChild('swiperContainer') swiperContainer!: ElementRef;
  @ViewChild('summaryScroller') summaryScroller?: ElementRef<HTMLElement>;

  autoReadSummary = false;
  speechState: 'idle' | 'loading' | 'playing' | 'blocked' | 'error' = 'idle';
  speechMessage = '';
  private summaryAudio: HTMLAudioElement | null = null;
  private speechRequest: Subscription | null = null;
  private speechUrl: string | null = null;
  private speechText: string | null = null;
  private speechGeneration = 0;
  showSummary = true;
  summary: string | null = null;
  summaryResponses: { round: number; text: string }[] = [];
  summaryIndex = 0;
  private summaryLastScrollAt: number | null = null;
  private summaryScrollRemainder = 0;
  private summaryBottomSince: number | null = null;
  private destroyed = false;
  private activePhotoId: number | null = null;
  private summaryRefreshTimer: ReturnType<typeof setInterval> | null = null;
  private summaryScrollTimer: ReturnType<typeof setInterval> | null = null;
  private summaryPauseUntil = 0;
  private summaryPointer: { id: number; y: number; top: number } | null = null;

  swiper: Swiper | null = null;
  currentPhoto: Photo | null = null;
  showInfo = true;
  guessedScore: number | null = null;
  flipped = false;

  // Slideshow
  isPlaying = false;
  slideInterval = 15; // seconds
  showSettings = false;
  private timer: any = null;
  private wakeLock: any = null;

  constructor(
    public photoService: PhotoService, 
    private router: Router,
    private cdr: ChangeDetectorRef) { }

  ngOnInit() {
    this.summaryRefreshTimer = setInterval(() => this.refreshSummary(), 10000);
    this.summaryScrollTimer = setInterval(() => this.scrollSummary(), 100);
  }

  scrollSummary() {
    const element = this.summaryScroller?.nativeElement;
    if (!element || !this.showSummary || !this.summary || document.hidden ||
        Date.now() < this.summaryPauseUntil || this.summaryPointer ||
        window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      this.summaryBottomSince = null;
      this.summaryLastScrollAt = null;
      this.summaryScrollRemainder = 0;
      return;
    }
    const now = Date.now();
    const elapsed = now - (this.summaryLastScrollAt ?? now);
    this.summaryLastScrollAt = now;
    if (element.scrollTop + element.clientHeight < element.scrollHeight - 1) {
      const style = window.getComputedStyle(element);
      const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.9;
      const distance = this.summaryScrollRemainder + lineHeight * elapsed / 2000;
      const pixels = Math.floor(distance);
      element.scrollTop += pixels;
      this.summaryScrollRemainder = distance - pixels;
      this.summaryBottomSince = null;
    } else if (this.summaryResponses.length > 1 &&
        !(this.summaryResponses[this.summaryIndex]?.round === 3 &&
          ['loading', 'playing', 'blocked'].includes(this.speechState))) {
      this.summaryBottomSince ??= Date.now();
      if (Date.now() - this.summaryBottomSince >= 4000) {
        this.selectSummaryResponse((this.summaryIndex + 1) % this.summaryResponses.length);
      }
    }
  }

  selectSummaryResponse(index: number) {
    const response = this.summaryResponses[index];
    if (!response) return;
    if (response.round !== 3) this.stopSummarySpeech();
    this.summaryIndex = index;
    this.summary = response.text;
    this.summaryBottomSince = null;
    this.summaryLastScrollAt = null;
    this.summaryScrollRemainder = 0;
    this.summaryPauseUntil = Date.now() + 2000;
    this.cdr.detectChanges();
    if (this.summaryScroller) this.summaryScroller.nativeElement.scrollTop = 0;
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['photos'] && !changes['photos'].firstChange) {
      if (this.swiper && this.swiper.virtual) {
        this.swiper.virtual.slides = this.photos;
        this.swiper.virtual.update(true);
      }
    }
  }

  ngAfterViewInit() {
    this.initSwiper();
    if (this.autoPlay) {
      this.startSlideshow();
    }
  }

  ngOnDestroy() {
    this.destroyed = true;
    this.stopSummarySpeech();
    if (this.summaryRefreshTimer) clearInterval(this.summaryRefreshTimer);
    if (this.summaryScrollTimer) clearInterval(this.summaryScrollTimer);
    this.stopSlideshow();
    if (this.swiper) {
      this.swiper.destroy();
    }
  }

  initSwiper() {
    if (!this.swiperContainer) return;

    if (this.swiper) {
      this.swiper.destroy();
    }

    this.swiper = new Swiper(this.swiperContainer.nativeElement, {
      modules: [Virtual, Zoom],
      zoom: {
        maxRatio: 5,
        minRatio: 1,
      },
      virtual: {
        slides: this.photos,
        renderSlide: (slide: any) => {
          const photo = slide as Photo;
          const imgUrl = this.photoService.getImageUrl(photo.filePath);
          const flipTransform = this.flipped ? 'transform: scaleX(-1);' : '';
          return `<div class="swiper-slide" style="display: flex; justify-content: center; align-items: center; width: 100%; height: 100%;">
                        <div class="swiper-zoom-container">
                            <img src="${imgUrl}" style="max-width:100%; max-height:100%; object-fit:contain; ${flipTransform}" />
                        </div>
                    </div>`;
        }
      },
      spaceBetween: 20,
      grabCursor: true,
      on: {
        slideChange: () => {
          const index = this.swiper?.activeIndex || 0;
          if (this.photos[index]) {
            this.updateOverlay(this.photos[index].id);
          }

          if (index >= this.photos.length - 5) {
            this.requestMore.emit();
          }
        },
        click: () => {
          this.showInfo = !this.showInfo;
          this.cdr.detectChanges();
        }
      }
    });

    if (this.startPhotoId) {
      const index = this.photos.findIndex(p => p.id === this.startPhotoId);
      if (index !== -1) {
        this.swiper.slideTo(index, 0);
        this.updateOverlay(this.startPhotoId);
      }
    }
  }

  updateOverlay(photoId: number) {
    if (!photoId) return;

    this.stopSummarySpeech();
    this.speechText = null;
    this.activePhotoId = photoId;
    this.summary = null;
    this.summaryResponses = [];
    this.summaryIndex = 0;
    this.summaryBottomSince = null;
    this.summaryLastScrollAt = null;
    this.summaryScrollRemainder = 0;
    this.summaryPauseUntil = Date.now() + 2000;
    if (this.summaryScroller) this.summaryScroller.nativeElement.scrollTop = 0;
    this.refreshSummary();
    this.guessedScore = null;
    this.photoService.getPhoto(photoId).subscribe(photo => {
      if (this.activePhotoId !== photoId) return;
      this.currentPhoto = photo;
      if (photo.manualScore != null && photo.predictedScore != null) {
        this.guessedScore = photo.predictedScore;
      }
    });

    this.photoService.viewPhoto(photoId).subscribe();
  }

  refreshSummary() {
    const id = this.activePhotoId;
    if (!id || !this.showSummary) return;
    this.photoService.getSummary(id).subscribe({
      next: result => {
        if (this.destroyed || this.activePhotoId !== id) return;
        const responses = [
          { round: 3, text: result.summary }, { round: 2, text: result.response2 },
          { round: 1, text: result.response1 }
        ].filter((response): response is { round: number; text: string } => !!response.text?.trim());
        // Polling unchanged responses must not restart playback at round three.
        if (JSON.stringify(this.summaryResponses) !== JSON.stringify(responses)) {
          if (responses.find(r => r.round === 3)?.text !== this.summaryResponses.find(r => r.round === 3)?.text) {
            this.stopSummarySpeech();
            this.speechText = null;
          }
          this.summaryResponses = responses;
          this.summaryIndex = 0;
          this.summary = null;
          this.summaryBottomSince = null;
          if (responses.length) this.selectSummaryResponse(0);
        }
        this.startSummarySpeech();
        this.cdr.detectChanges();
      },
      error: () => {
        if (this.activePhotoId === id) {
          this.stopSummarySpeech();
          this.summary = null;
          this.summaryResponses = [];
        }
      }
    });
  }

  toggleSummarySpeech() {
    this.stopSummarySpeech();
    this.speechText = null;
    if (!this.autoReadSummary) return;
    const third = this.summaryResponses.findIndex(r => r.round === 3);
    if (third >= 0) this.selectSummaryResponse(third);
    this.startSummarySpeech();
  }

  private startSummarySpeech() {
    const text = this.summaryResponses.find(r => r.round === 3)?.text;
    const photoId = this.activePhotoId;
    if (!this.autoReadSummary || this.destroyed || !photoId || !text || this.speechText === text ||
        this.summaryResponses[this.summaryIndex]?.round !== 3) return;
    this.stopSummarySpeech();
    this.speechText = text;
    this.speechState = 'loading';
    this.speechMessage = '正在生成语音…';
    const generation = this.speechGeneration;
    this.speechRequest = this.photoService.getSummarySpeech(photoId, text).subscribe({
      next: audio => {
        if (generation !== this.speechGeneration || this.destroyed) return;
        if (!(audio instanceof Blob)) {
          this.speechMessage = audio.status === 'queued'
            ? `语音排队中${audio.queue_position ? '，第' + audio.queue_position + '位' : ''}`
            : `正在合成语音${audio.total_segments ? '，' + audio.completed_segments + '/' + audio.total_segments + '段' : ''}`;
          this.cdr.detectChanges();
          return;
        }
        this.speechUrl = URL.createObjectURL(audio);
        this.summaryAudio ??= new Audio();
        this.summaryAudio.src = this.speechUrl;
        this.summaryAudio.onended = () => {
          if (generation !== this.speechGeneration || this.destroyed) return;
          this.speechState = 'idle';
          this.speechMessage = '';
          this.summaryBottomSince = null;
          this.cdr.detectChanges();
        };
        this.summaryAudio.onerror = () => {
          if (generation !== this.speechGeneration || this.destroyed) return;
          this.speechState = 'error';
          this.speechMessage = '语音播放失败';
          this.cdr.detectChanges();
        };
        void this.playSummarySpeech();
      },
      error: error => {
        if (generation !== this.speechGeneration || this.destroyed) return;
        this.speechState = 'error';
        this.speechMessage = error.status === 503 ? '语音服务尚未配置'
          : error.status === 429 ? '语音队列已满，请稍后重试' : '语音生成失败，请稍后重试';
        this.cdr.detectChanges();
      }
    });
  }

  async playSummarySpeech() {
    if (!this.summaryAudio || !this.speechUrl) return;
    const generation = this.speechGeneration;
    try {
      await this.summaryAudio.play();
      if (generation !== this.speechGeneration || this.destroyed) return;
      this.speechState = 'playing';
      this.speechMessage = '正在朗读第三轮';
    } catch (error) {
      if (generation !== this.speechGeneration || this.destroyed) return;
      this.speechState = (error as DOMException).name === 'NotAllowedError' ? 'blocked' : 'error';
      this.speechMessage = this.speechState === 'blocked' ? '点击播放以允许声音' : '语音播放失败';
    }
    this.cdr.detectChanges();
  }

  retrySummarySpeech() {
    this.speechText = null;
    this.startSummarySpeech();
  }

  private stopSummarySpeech() {
    this.speechGeneration++;
    this.speechRequest?.unsubscribe();
    this.speechRequest = null;
    if (this.summaryAudio) {
      this.summaryAudio.onended = null;
      this.summaryAudio.onerror = null;
      this.summaryAudio.pause();
      this.summaryAudio.removeAttribute('src');
      this.summaryAudio.load();
    }
    if (this.speechUrl) URL.revokeObjectURL(this.speechUrl);
    this.speechUrl = null;
    this.speechState = 'idle';
    this.speechMessage = '';
    this.summaryBottomSince = null;
  }

  toggleSummary() {
    this.summaryBottomSince = null;
    this.summaryLastScrollAt = null;
    this.summaryScrollRemainder = 0;
    this.summaryPauseUntil = Date.now() + 2000;
    if (this.showSummary) this.refreshSummary();
  }

  startSummaryDrag(event: PointerEvent) {
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    const element = event.currentTarget as HTMLElement;
    this.summaryPointer = { id: event.pointerId, y: event.clientY, top: element.scrollTop };
    element.setPointerCapture(event.pointerId);
    this.summaryPauseUntil = Date.now() + 10000;
    event.stopPropagation();
  }

  dragSummary(event: PointerEvent) {
    if (!this.summaryPointer || this.summaryPointer.id !== event.pointerId) return;
    (event.currentTarget as HTMLElement).scrollTop = this.summaryPointer.top + this.summaryPointer.y - event.clientY;
  }

  endSummaryDrag() {
    this.summaryPointer = null;
    this.summaryPauseUntil = Date.now() + 10000;
  }

  pauseSummaryScroll(event: Event) {
    event.stopPropagation();
    this.summaryBottomSince = null;
    this.summaryPauseUntil = Date.now() + 10000;
  }

  onClose() {
    this.autoReadSummary = false;
    this.stopSummarySpeech();
    this.stopSlideshow();
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(err => console.error(err));
    }
    this.close.emit();
  }

  @HostListener('document:visibilitychange')
  async onVisibilityChange() {
    if (document.visibilityState === 'visible' && this.isPlaying) {
      await this.requestWakeLock();
    }
  }

  async requestWakeLock() {
    if ('wakeLock' in navigator) {
      try {
        this.wakeLock = await (navigator as any).wakeLock.request('screen');
        this.wakeLock.addEventListener('release', () => {
          console.log('Screen Wake Lock released');
        });
        console.log('Screen Wake Lock active');
      } catch (err: any) {
        console.error(`${err.name}, ${err.message}`);
      }
    }
  }

  releaseWakeLock() {
    if (this.wakeLock !== null) {
      this.wakeLock.release().catch(console.error);
      this.wakeLock = null;
    }
  }

  // Slideshow methods
  togglePlay(event?: Event) {
    if (event) event.stopPropagation();
    
    if (this.isPlaying) {
      this.stopSlideshow();
    } else {
      this.startSlideshow();
    }
  }

  startSlideshow() {
    this.isPlaying = true;
    this.requestWakeLock();
    if (document.fullscreenEnabled && !document.fullscreenElement) {
      document.documentElement.requestFullscreen().catch(err => {
        console.error(`Error attempting to enable full-screen mode: ${err.message}`);
      });
    }
    this.timer = setInterval(() => {
      if (this.swiper) {
        if (this.swiper.isEnd) {
          if (!this.hasMore) {
            this.swiper.slideTo(0);
          }
          // If hasMore is true, we just stay at the end and wait for new photos to arrive.
          // Once new photos arrive, isEnd will become false in the next interval.
        } else {
          this.swiper.slideNext();
        }
      }
    }, this.slideInterval * 1000);
  }

  stopSlideshow() {
    this.isPlaying = false;
    this.releaseWakeLock();
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  toggleSettings(event?: Event) {
    if (event) event.stopPropagation();
    this.showSettings = !this.showSettings;
  }

  updateInterval() {
    // Clamp values
    if (this.slideInterval < 0.2) this.slideInterval = 0.2;
    if (this.slideInterval > 900) this.slideInterval = 900;

    if (this.isPlaying) {
      this.stopSlideshow();
      this.startSlideshow();
    }
  }

  ratePhoto(score: number, event?: Event) {
    if (event) {
      event.stopPropagation();
    }

    if (!this.currentPhoto) return;

    this.photoService.ratePhoto(this.currentPhoto.id, score).subscribe({
      next: (updatedPhoto) => {
        // Preserve album if missing
        if (!updatedPhoto.album && this.currentPhoto?.album) {
          updatedPhoto.album = this.currentPhoto.album;
        }

        this.currentPhoto = updatedPhoto;
        this.guessedScore = updatedPhoto.predictedScore ?? null;
        // Optionally update the photo in the list if reference is shared or find it by ID
        const index = this.photos.findIndex(p => p.id === updatedPhoto.id);
        if (index !== -1) {
          this.photos[index] = { ...this.photos[index], ...updatedPhoto };
        }
        if (this.swiper && !this.swiper.isEnd) {
          this.swiper.slideNext();
        } else if (this.hasMore) {
          this.requestMore.emit();
        }
        this.rated.emit(updatedPhoto);
      },
      error: (err) => {
        console.error(err);
        if (err.error && err.error.error) {
          alert(err.error.error);
        } else {
          alert('打分失败，请稍后重试。');
        }
      }
    });
  }

  isRated(score: number): boolean {
    if (!this.currentPhoto || this.currentPhoto.manualScore == null) {
      return false;
    }
    return Math.round(this.currentPhoto.manualScore) === score;
  }

  truncateAlbumName(name: string): string {
    if (!name) return '';
    if (name.length <= 20) return name;
    return name.substring(0, 17) + '...';
  }

  openAlbum() {
    if (this.showInfo && this.currentPhoto) {
      this.onClose();
      this.router.navigate(['/album', this.currentPhoto.albumId]);
    }
  }

  toggleFlip(event?: Event) {
    if (event) event.stopPropagation();
    this.flipped = !this.flipped;

    // Force re-render of Swiper virtual slides so the inline flip
    // transform is picked up in renderSlide immediately.
    if (this.swiper?.virtual) {
      this.swiper.virtual.update(true);
    }
  }

  viewSimilar(event?: Event) {
    if(event) event.stopPropagation();
    if (this.currentPhoto) {
      this.onClose();
      this.router.navigate(['/similar', this.currentPhoto.id]);
    }
  }

  deletePhoto(event?: Event) {
    if (event) event.stopPropagation();
    if (!this.currentPhoto) return;

    const confirmed = confirm('确定要删除这张照片吗？此操作不可撤销。');
    if (!confirmed) return;

    const photoId = this.currentPhoto.id;
    const currentIndex = this.swiper?.activeIndex || 0;

    this.photoService.deletePhoto(photoId).subscribe({
      next: () => {
        // Remove from local photos array
        this.photos = this.photos.filter(p => p.id !== photoId);

        if (this.photos.length === 0) {
          // No photos left, close the viewer
          this.onClose();
          return;
        }

        // Update swiper virtual slides
        if (this.swiper && this.swiper.virtual) {
          this.swiper.virtual.slides = this.photos;
          this.swiper.virtual.update(true);

          // Navigate to the next photo or stay at the same position
          const newIndex = Math.min(currentIndex, this.photos.length - 1);
          this.swiper.slideTo(newIndex, 0);
          if (this.photos[newIndex]) {
            this.updateOverlay(this.photos[newIndex].id);
          }
        }
      },
      error: (err) => {
        console.error('删除照片失败:', err);
        alert('删除照片失败，请稍后重试。');
      }
    });
  }

  @HostListener('window:wheel', ['$event'])
  onWheel(event: WheelEvent) {
    if (event.target instanceof Element && event.target.closest('.summary-scroller')) return;
    if (this.swiper && this.swiper.zoom) {
      event.preventDefault();
      if (event.deltaY < 0) {
        this.swiper.zoom.in();
      } else {
        this.swiper.zoom.out();
      }
    }
  }

  @HostListener('window:keydown', ['$event'])
  handleKeyboardEvent(event: KeyboardEvent) {
    const target = event.target;
    if (target instanceof HTMLInputElement ||
        target instanceof HTMLTextAreaElement ||
        target instanceof HTMLSelectElement ||
        (target instanceof HTMLElement && target.isContentEditable)) {
      return;
    }

    if (event.key === 'ArrowRight') {
      this.swiper?.slideNext();
    } else if (event.key === 'ArrowLeft') {
      this.swiper?.slidePrev();
    } else if (event.key === 'Escape') {
      this.onClose();
    } else if (event.key >= '0' && event.key <= '6') {
      this.ratePhoto(parseInt(event.key));
    }
  }
}
