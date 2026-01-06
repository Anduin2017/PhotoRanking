import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService } from '../../services/photo';
import { Router } from '@angular/router';

@Component({
  selector: 'app-advanced',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './advanced.html',
  styleUrl: './advanced.css',
})
export class AdvancedComponent implements OnInit {
  isLoading = true;
  stats: any = {
    topAlbumsByScore: [],
    topAlbumsByKnownRate: [],
    topPhotosByScore: [],
    topPhotosByKnownness: []
  };

  loadedCounts = {
    albumScore: 10,
    albumKnownRate: 10,
    photoScore: 20,
    photoKnownness: 20
  };

  constructor(public photoService: PhotoService, private router: Router) { }

  ngOnInit() {
    this.photoService.getTopStats().subscribe({
      next: (data) => {
        this.stats = data;
        this.isLoading = false;
        // Reset counts based on initial data, or keep defaults if strictly followed
        this.loadedCounts.albumScore = data.topAlbumsByScore?.length || 0;
        this.loadedCounts.albumKnownRate = data.topAlbumsByKnownRate?.length || 0;
        this.loadedCounts.photoScore = data.topPhotosByScore?.length || 0;
        this.loadedCounts.photoKnownness = data.topPhotosByKnownness?.length || 0;
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
    const take = 10;

    switch (section) {
      case 'albumScore':
        skip = this.loadedCounts.albumScore;
        endpoint = `albums/top-by-score?skip=${skip}&take=${take}`;
        break;
      case 'albumKnownRate':
        skip = this.loadedCounts.albumKnownRate;
        endpoint = `albums/top-by-knownrate?skip=${skip}&take=${take}`;
        break;
      case 'photoScore':
        skip = this.loadedCounts.photoScore;
        endpoint = `photos/top-by-score?skip=${skip}&take=${take}`;
        break;
      case 'photoKnownness':
        skip = this.loadedCounts.photoKnownness;
        endpoint = `photos/top-by-knownness?skip=${skip}&take=${take}`;
        break;
    }

    if (!endpoint) return;

    this.photoService.getMoreStats(endpoint).subscribe({
      next: (items: any[]) => {
        if (section === 'albumScore') {
          this.stats.topAlbumsByScore = [...this.stats.topAlbumsByScore, ...items];
          this.loadedCounts.albumScore += items.length;
        } else if (section === 'albumKnownRate') {
          this.stats.topAlbumsByKnownRate = [...this.stats.topAlbumsByKnownRate, ...items];
          this.loadedCounts.albumKnownRate += items.length;
        } else if (section === 'photoScore') {
          this.stats.topPhotosByScore = [...this.stats.topPhotosByScore, ...items];
          this.loadedCounts.photoScore += items.length;
        } else if (section === 'photoKnownness') {
          this.stats.topPhotosByKnownness = [...this.stats.topPhotosByKnownness, ...items];
          this.loadedCounts.photoKnownness += items.length;
        }
      },
      error: (err) => console.error(`Error loading more ${section}`, err)
    });
  }

  openAlbum(albumId: string) {
    this.router.navigate(['/album', albumId]);
  }

  // Open viewer helper
  openViewer(photoId: number) {
    // TODO: Implement photo viewer opening
    console.log('Open photo', photoId);
  }
}
