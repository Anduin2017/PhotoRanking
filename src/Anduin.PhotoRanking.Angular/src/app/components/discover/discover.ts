import { Component, HostListener, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Photo, PhotoService } from '../../services/photo';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';

@Component({
  selector: 'app-discover',
  standalone: true,
  imports: [CommonModule, FormsModule, PhotoViewerComponent, RouterModule],
  templateUrl: './discover.html',
  styleUrl: './discover.css',
})
export class DiscoverComponent implements OnInit {
  photos: Photo[] = [];
  page = 1;
  isLoading = false;
  hasMore = true;
  pageSize = 30;
  mode = 'waiting';
  minScore = 3.0;
  sortBy = 'random'; // New property for sorting in featured mode
  loadingMode: string | null = null; // Track which mode is currently being loaded
  loadingMinScore: number | null = null; // Track which minScore is currently being loaded
  loadingSortBy: string | null = null; // Track which sortBy is currently being loaded

  viewerOpen = false;
  initialPhotoId: number | null = null;

  constructor(public photoService: PhotoService, private route: ActivatedRoute, private router: Router) { }

  ngOnInit() {
    this.route.params.subscribe(params => { // Use query params or route params for mode? Old app used hash route #discover/waiting
      // We will move mode to route param in app.routes
    });

    // Default load
    this.loadMore();
  }

  setMode(mode: string) {
    if (this.mode === mode) return;
    this.mode = mode;
    this.resetAndLoad();
  }

  setMinScore(score: number) {
    this.minScore = score;
    this.resetAndLoad();
  }

  setSortBy(event: Event) {
    const target = event.target as HTMLSelectElement;
    this.sortBy = target.value;
    this.resetAndLoad();
  }

  private resetAndLoad() {
    this.photos = [];
    this.page = 1;
    this.hasMore = true;
    this.isLoading = false; // Reset loading state to allow immediate new request
    this.loadMore();
  }

  loadMore() {
    if (this.isLoading || !this.hasMore) return;

    this.isLoading = true;
    const requestMode = this.mode; // Capture the current mode for this request
    const requestMinScore = this.minScore; // Capture the current minScore for this request
    const requestSortBy = this.sortBy; // Capture the current sortBy
    this.loadingMode = requestMode;
    this.loadingMinScore = requestMinScore;
    this.loadingSortBy = requestSortBy;

    const minScoreToSend = (requestMode === 'enjoy' || requestMode === 'featured') ? requestMinScore : undefined;
    const sortToSend = (requestMode === 'featured') ? requestSortBy : undefined;
    this.photoService.getDiscoverPhotos(requestMode, this.page, this.pageSize, minScoreToSend, sortToSend).subscribe({
      next: (newPhotos) => {
        // Ignore this response if the mode, minScore or sortBy has changed since the request was made
        if (this.loadingMode !== requestMode || this.loadingMinScore !== requestMinScore || this.loadingSortBy !== requestSortBy) {
          return;
        }

        if (newPhotos.length === 0) {
          if (this.page === 1) {
            // specific empty state handling if needed
          }
        }

        if (newPhotos.length < this.pageSize) {
          this.hasMore = false;
        }

        this.photos = [...this.photos, ...newPhotos];
        this.page++;
        this.isLoading = false;
      },
      error: (err) => {
        // Ignore errors from outdated requests
        if (this.loadingMode !== requestMode || this.loadingMinScore !== requestMinScore || this.loadingSortBy !== requestSortBy) {
          return;
        }

        console.error('Error loading discover', err);
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

  @HostListener('window:scroll')
  onScroll() {
    if ((window.innerHeight + window.scrollY) >= document.body.offsetHeight - 500) {
      this.loadMore();
    }
  }
}
