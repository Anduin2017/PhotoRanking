import { Component, ElementRef, EventEmitter, Input, OnInit, Output, ViewChild, AfterViewInit, OnDestroy, HostListener, ChangeDetectorRef, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
  @Output() rated = new EventEmitter<number>();

  @ViewChild('swiperContainer') swiperContainer!: ElementRef;

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
    // Swiper init happens in ngAfterViewInit
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

    this.guessedScore = null;
    this.photoService.getPhoto(photoId).subscribe(photo => {
      this.currentPhoto = photo;
      if (photo.manualScore != null && photo.predictedScore != null) {
        this.guessedScore = photo.predictedScore;
      }
    });

    this.photoService.viewPhoto(photoId).subscribe();
  }

  onClose() {
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
        this.rated.emit(updatedPhoto.id);
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
