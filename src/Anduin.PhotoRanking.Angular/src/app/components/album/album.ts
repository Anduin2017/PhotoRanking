import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Photo, PhotoService, Album } from '../../services/photo';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';

@Component({
  selector: 'app-album',
  standalone: true,
  imports: [CommonModule, FormsModule, PhotoViewerComponent],
  templateUrl: './album.html',
  styleUrl: './album.css',
})
export class AlbumComponent implements OnInit {
  albumId: string = '';
  album: Album | null = null;
  photos: Photo[] = [];
  isLoading = true;
  sortBy: string = 'filename';

  viewerOpen = false;
  initialPhotoId: number | null = null;
  dedupSimilarity: number = 93;

  constructor(private route: ActivatedRoute, public photoService: PhotoService, private router: Router) { }

  ngOnInit() {
    this.route.params.subscribe(params => {
      this.albumId = params['id']; // We need to ensure route config has :id
      if (this.albumId) {
        this.loadAlbum(this.albumId);
      }
    });
  }

  loadAlbum(id: string) {
    this.isLoading = true;
    this.photoService.getAlbum(id, this.sortBy).subscribe({
      next: (details) => {
        this.album = details.album;
        this.photos = details.photos;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading album', err);
        this.isLoading = false;
      }
    });
  }

  onSortChange(newSort: string) {
    this.sortBy = newSort;
    this.loadAlbum(this.albumId);
  }

  openViewer(photoId: number) {
    this.initialPhotoId = photoId;
    this.viewerOpen = true;
  }

  closeViewer() {
    this.viewerOpen = false;
    this.initialPhotoId = null;
  }

  goToDedup() {
    this.router.navigate(['/album', this.albumId, 'dedup'], {
      queryParams: { similarity: this.dedupSimilarity }
    });
  }
}
