import { Component, ElementRef, EventEmitter, Input, OnInit, Output, ViewChild, AfterViewInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Photo, PhotoService } from '../../services/photo';
import Swiper from 'swiper';
import { Navigation, Virtual } from 'swiper/modules';

@Component({
  selector: 'app-photo-viewer',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './photo-viewer.html',
  styleUrl: './photo-viewer.css',
})
export class PhotoViewerComponent implements OnInit, AfterViewInit, OnDestroy {
  @Input() startPhotoId: number | null = null;
  @Input() photos: Photo[] = [];
  @Output() close = new EventEmitter<void>();

  @ViewChild('swiperContainer') swiperContainer!: ElementRef;

  swiper: Swiper | null = null;
  currentPhoto: Photo | null = null;

  constructor(public photoService: PhotoService) { }

  ngOnInit() {
    // Swiper init happens in ngAfterViewInit
  }

  ngAfterViewInit() {
    this.initSwiper();
  }

  ngOnDestroy() {
    if (this.swiper) {
      this.swiper.destroy();
    }
  }

  initSwiper() {
    if (!this.swiperContainer) return;

    // RE-DOING the configuration to match the successful vanilla JS implementation EXACTLY
    // The previous swiper initialization was incomplete and commented out.
    // If a swiper instance already exists, destroy it before creating a new one.
    if (this.swiper) {
      this.swiper.destroy();
    }

    this.swiper = new Swiper(this.swiperContainer.nativeElement, {
      modules: [Virtual],
      virtual: {
        slides: this.photos,
        renderSlide: (slide: any) => {
          const photo = slide as Photo;
          const imgUrl = this.photoService.getImageUrl(photo.filePath);
          return `<div class="swiper-slide" style="display: flex; justify-content: center; align-items: center; width: 100%; height: 100%;">
                        <img src="${imgUrl}" class="swiper-zoom-target" style="max-width:100%; max-height:100%; object-fit:contain;" />
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

    this.photoService.getPhoto(photoId).subscribe(photo => {
      this.currentPhoto = photo;
    });

    this.photoService.viewPhoto(photoId).subscribe();
  }

  onClose() {
    this.close.emit();
  }

  ratePhoto(score: number, event?: Event) {
    if (event) {
      event.stopPropagation();
    }

    if (!this.currentPhoto) return;

    this.photoService.ratePhoto(this.currentPhoto.id, score).subscribe({
      next: (updatedPhoto) => {
        this.currentPhoto = updatedPhoto;
        // Optionally update the photo in the list if reference is shared or find it by ID
        const index = this.photos.findIndex(p => p.id === updatedPhoto.id);
        if (index !== -1) {
          this.photos[index] = { ...this.photos[index], ...updatedPhoto };
        }
      },
      error: (err) => console.error(err)
    });
  }

  @HostListener('window:keydown', ['$event'])
  handleKeyboardEvent(event: KeyboardEvent) {
    if (event.key === 'ArrowRight') {
      this.swiper?.slideNext();
    } else if (event.key === 'ArrowLeft') {
      this.swiper?.slidePrev();
    } else if (event.key === 'Escape') {
      this.onClose();
    } else if (event.key >= '0' && event.key <= '5') {
      this.ratePhoto(parseInt(event.key));
    }
  }
}
