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
  page = 1;
  isLoading = false;
  hasMore = true;
  pageSize = 20;

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
    this.photoService.getFeed(this.page, this.pageSize).subscribe({
      next: (newPhotos) => {
        if (newPhotos.length === 0) {
          this.hasMore = false;
        } else {
          this.photos = [...this.photos, ...newPhotos];
          this.page++;
          if (newPhotos.length < this.pageSize) {
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
        // Update the photo in the list with new stats
        const index = this.photos.findIndex(p => p.id === photo.id);
        if (index !== -1) {
          this.photos[index] = { ...this.photos[index], ...updatedPhoto };
          // Animation logic could go here
        }
      },
      error: (err) => console.error(err)
    });
  }

  @HostListener('window:scroll')
  onScroll() {
    if ((window.innerHeight + window.scrollY) >= document.body.offsetHeight - 500) {
      this.loadMore();
    }
  }
}
