import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService, GlobalStats } from '../../services/photo';
import { Router } from '@angular/router';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';

@Component({
  selector: 'app-advanced',
  standalone: true,
  imports: [CommonModule, PhotoViewerComponent],
  templateUrl: './advanced.html',
  styleUrl: './advanced.css',
})
export class AdvancedComponent implements OnInit {
  isLoading = true;
  stats: any = {
    topAlbumsByScore: [],
    topAlbumsByRatedRate: [],
    topManualPhotos: [],
    topPredictedUnratedPhotos: [],
    ratingHistory: []
  };
  globalStats: GlobalStats | null = null;

  loadedCounts = {
    albumScore: 10,
    albumRatedRate: 10,
    photoScore: 20,
    predictedPhoto: 20,
    ratingHistory: 20
  };

  viewerOpen = false;
  initialPhotoId: number | null = null;
  viewerPhotos: any[] = [];

  constructor(public photoService: PhotoService, private router: Router) { }

  ngOnInit() {
    this.photoService.getGlobalStats().subscribe({
      next: (data) => {
        this.globalStats = data;
      },
      error: (err) => console.error('Error loading global stats', err)
    });

    this.photoService.getTopStats().subscribe({
      next: (data) => {
        this.stats = data;
        this.isLoading = false;
        // Reset counts based on initial data, or keep defaults if strictly followed
        this.loadedCounts.albumScore = data.topAlbumsByScore?.length || 0;
        this.loadedCounts.albumRatedRate = data.topAlbumsByRatedRate?.length || 0;
        this.loadedCounts.photoScore = data.topManualPhotos?.length || 0;
        this.loadedCounts.predictedPhoto = data.topPredictedUnratedPhotos?.length || 0;
        this.loadedCounts.ratingHistory = data.ratingHistory?.length || 0;
      },
      error: (err) => {
        console.error('Error loading stats', err);
        this.isLoading = false;
      }
    });
  }

  loadMore(section: string) {
    let endpoint = '';
    let skip = 0;
    const take = 20;

    switch (section) {
      case 'albumScore':
        skip = this.loadedCounts.albumScore;
        endpoint = `albums/top-by-score?skip=${skip}&take=${take}`;
        break;
      case 'albumRatedRate':
        skip = this.loadedCounts.albumRatedRate;
        endpoint = `albums/top-by-ratedrate?skip=${skip}&take=${take}`;
        break;
      case 'photoScore':
        skip = this.loadedCounts.photoScore;
        endpoint = `photos/top-by-score?skip=${skip}&take=${take}`;
        break;
      case 'predictedPhoto':
        skip = this.loadedCounts.predictedPhoto;
        endpoint = `photos/top-predicted?skip=${skip}&take=${take}`;
        break;
      case 'ratingHistory':
        skip = this.loadedCounts.ratingHistory;
        endpoint = `photos/rating-history?skip=${skip}&take=${take}`;
        break;
    }

    if (!endpoint) return;

    this.photoService.getMoreStats(endpoint).subscribe({
      next: (items: any[]) => {
        if (section === 'albumScore') {
          this.stats.topAlbumsByScore = [...this.stats.topAlbumsByScore, ...items];
          this.loadedCounts.albumScore += items.length;
        } else if (section === 'albumRatedRate') {
          this.stats.topAlbumsByRatedRate = [...this.stats.topAlbumsByRatedRate, ...items];
          this.loadedCounts.albumRatedRate += items.length;
        } else if (section === 'photoScore') {
          this.stats.topManualPhotos = [...this.stats.topManualPhotos, ...items];
          this.loadedCounts.photoScore += items.length;
        } else if (section === 'predictedPhoto') {
          this.stats.topPredictedUnratedPhotos = [...this.stats.topPredictedUnratedPhotos, ...items];
          this.loadedCounts.predictedPhoto += items.length;
        } else if (section === 'ratingHistory') {
          this.stats.ratingHistory = [...this.stats.ratingHistory, ...items];
          this.loadedCounts.ratingHistory += items.length;
        }
      },
      error: (err) => console.error(`Error loading more ${section}`, err)
    });
  }

  openAlbum(albumId: string) {
    this.router.navigate(['/album', albumId]);
  }

  // Open viewer helper
  openViewer(photoId: number, photos: any[]) {
    this.viewerPhotos = photos;
    this.initialPhotoId = photoId;
    this.viewerOpen = true;
  }

  closeViewer() {
    this.viewerOpen = false;
    this.initialPhotoId = null;
    this.viewerPhotos = [];
  }
}
