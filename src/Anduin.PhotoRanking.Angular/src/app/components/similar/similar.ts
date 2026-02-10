import { Component, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Photo, PhotoService } from '../../services/photo';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';

@Component({
  selector: 'app-similar',
  standalone: true,
  imports: [CommonModule, PhotoViewerComponent],
  templateUrl: './similar.html',
  styleUrl: './similar.css',
})
export class SimilarComponent implements OnInit {
  targetId: number = 0;
  targetPhoto: Photo | null = null;
  photos: Photo[] = [];
  isLoading = true;
  isLoadingMore = false;
  hasMore = true;
  skip = 0;
  readonly take = 20;

  viewerOpen = false;
  initialPhotoId: number | null = null;

  constructor(private route: ActivatedRoute, public photoService: PhotoService) { }

  ngOnInit() {
    this.route.params.subscribe(params => {
      this.targetId = +params['id'];
      if (this.targetId) {
        this.resetAndLoad();
      }
    });
  }

  resetAndLoad() {
    this.photos = [];
    this.skip = 0;
    this.hasMore = true;
    this.isLoading = true;
    this.loadTargetPhoto();
    this.loadMore();
  }

  loadTargetPhoto() {
    this.photoService.getPhoto(this.targetId).subscribe({
      next: (photo) => {
        this.targetPhoto = photo;
      },
      error: (err) => {
        console.error('Error loading target photo', err);
      }
    });
  }

  loadMore() {
    if (this.isLoadingMore || !this.hasMore || !this.targetId) return;

    this.isLoadingMore = true;
    this.photoService.getSimilarPhotos(this.targetId, this.skip, this.take).subscribe({
      next: (newPhotos) => {
        if (newPhotos.length < this.take) {
          this.hasMore = false;
        }
        this.photos = [...this.photos, ...newPhotos];
        this.skip += newPhotos.length;
        this.isLoading = false;
        this.isLoadingMore = false;
      },
      error: (err) => {
        console.error('Error loading similar photos', err);
        this.isLoading = false;
        this.isLoadingMore = false;
      }
    });
  }

  @HostListener('window:scroll', [])
  onScroll(): void {
    if ((window.innerHeight + window.scrollY) >= document.body.offsetHeight - 500) {
      this.loadMore();
    }
  }

  openViewer(photoId: number) {
    this.initialPhotoId = photoId;
    this.viewerOpen = true;
  }

  closeViewer() {
    this.viewerOpen = false;
    this.initialPhotoId = null;
  }

  get viewerPhotos(): Photo[] {
    if (this.targetPhoto) {
      const exists = this.photos.some(p => p.id === this.targetPhoto!.id);
      if (!exists) {
        return [this.targetPhoto, ...this.photos];
      }
    }
    return this.photos;
  }
}
