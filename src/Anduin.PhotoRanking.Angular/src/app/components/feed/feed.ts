import { Component, HostListener, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService, Photo } from '../../services/photo';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';
import { RouterModule, ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-feed',
  standalone: true,
  imports: [CommonModule, PhotoViewerComponent, RouterModule],
  templateUrl: './feed.html',
  styleUrl: './feed.css',
})
export class FeedComponent implements OnInit {
  photos: Photo[] = [];
  isLoading = false;
  hasMore = true;
  pageSize = 20;
  private cursorScore: number | undefined;
  private cursorId: number | undefined;

  viewerOpen = false;
  initialPhotoId: number | null = null;
  viewerContext = 'feed'; // 'feed' context for now

  // For passing to viewer
  get photoIds(): number[] {
    return this.photos.map(p => p.id);
  }

  constructor(public photoService: PhotoService, private route: ActivatedRoute) { }

  ngOnInit() {
    this.loadMore();

    // Check URL for photo ID
    this.route.queryParams.subscribe(params => {
      // Simple check, robust routing will be implemented later
    });
  }

  loadMore() {
    if (this.isLoading || !this.hasMore) return;

    this.isLoading = true;
    this.photoService.getFeed(this.pageSize, this.cursorScore, this.cursorId).subscribe({
      next: (newPhotos) => {
        if (newPhotos.length === 0) {
          this.hasMore = false;
        } else {
          const existingIds = new Set(this.photos.map(p => p.id));
          const uniquePhotos = newPhotos.filter(p => !existingIds.has(p.id));
          this.photos = [...this.photos, ...uniquePhotos];

          const last = newPhotos[newPhotos.length - 1];
          this.cursorScore = last.predictedScore ?? last.estimatedScore ?? undefined;
          this.cursorId = last.id;
          if (uniquePhotos.length === 0) {
            this.hasMore = false;
          }
        }
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading feed', err);
        this.isLoading = false;
      }
    });
  }

  openViewer(photoId: number) {
    this.initialPhotoId = photoId;
    this.viewerOpen = true;
  }

  closeViewer() {
    this.viewerOpen = false;
    this.initialPhotoId = null;
  }

  onRate(photo: Photo, score: number, event: Event) {
    event.stopPropagation();
    this.photoService.ratePhoto(photo.id, score).subscribe({
      next: (updatedPhoto) => {
        // A final score removes the photo from the unrated For You feed.
        this.photos = this.photos.filter(p => p.id !== photo.id);
      },
      error: (err) => console.error(err)
    });
  }

  onViewerRated(photoId: number) {
    this.photos = this.photos.filter(p => p.id !== photoId);
  }

  @HostListener('window:scroll')
  onScroll() {
    if ((window.innerHeight + window.scrollY) >= document.body.offsetHeight - 500) {
      this.loadMore();
    }
  }
}
