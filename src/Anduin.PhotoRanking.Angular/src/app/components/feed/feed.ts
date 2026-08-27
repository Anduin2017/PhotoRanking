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
  readonly scoreOptions = [0, 1, 2, 3, 4, 5, 6];
  photos: Photo[] = [];
  isLoading = false;
  hasMore = true;
  pageSize = 20;
  private readonly feedSeed = Math.floor(Math.random() * 2_147_483_647);
  private cursorScore: number | undefined;
  private cursorRank: number | undefined;
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
    this.photoService.getFeed(
      this.pageSize,
      this.cursorScore,
      this.cursorId,
      this.feedSeed,
      this.cursorRank).subscribe({
      next: (newPhotos) => {
        if (newPhotos.length === 0) {
          this.hasMore = false;
        } else {
          const existingIds = new Set(this.photos.map(p => p.id));
          const uniquePhotos = newPhotos.filter(p => !existingIds.has(p.id));
          this.photos = [...this.photos, ...uniquePhotos];

          const last = newPhotos[newPhotos.length - 1];
          this.cursorScore = last.predictedScore ?? last.estimatedScore ?? undefined;
          this.cursorRank = last.feedRank ?? undefined;
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
        this.revealRatedPhoto(updatedPhoto);
      },
      error: (err) => console.error(err)
    });
  }

  onViewerRated(updatedPhoto: Photo) {
    this.revealRatedPhoto(updatedPhoto);
  }

  private revealRatedPhoto(updatedPhoto: Photo) {
    this.photos = this.photos.map(photo => photo.id === updatedPhoto.id
      ? {
          ...photo,
          ...updatedPhoto,
          album: updatedPhoto.album ?? photo.album,
          manualScore: updatedPhoto.manualScore ?? updatedPhoto.independentScore ?? photo.manualScore,
          predictedScore: updatedPhoto.predictedScore ?? updatedPhoto.estimatedScore ??
            photo.predictedScore ?? photo.estimatedScore
        }
      : photo);
  }

  @HostListener('window:scroll')
  onScroll() {
    if ((window.innerHeight + window.scrollY) >= document.body.offsetHeight - 500) {
      this.loadMore();
    }
  }
}
