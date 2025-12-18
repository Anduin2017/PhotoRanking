import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Photo, PhotoService, Album } from '../../services/photo';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';

@Component({
  selector: 'app-album',
  standalone: true,
  imports: [CommonModule, PhotoViewerComponent],
  templateUrl: './album.html',
  styleUrl: './album.css',
})
export class AlbumComponent implements OnInit {
  albumId: string = '';
  album: Album | null = null;
  photos: Photo[] = [];
  isLoading = true;

  viewerOpen = false;
  initialPhotoId: number | null = null;

  constructor(private route: ActivatedRoute, public photoService: PhotoService) { }

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
    this.photoService.getAlbum(id).subscribe({
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

  openViewer(photoId: number) {
    this.initialPhotoId = photoId;
    this.viewerOpen = true;
  }

  closeViewer() {
    this.viewerOpen = false;
    this.initialPhotoId = null;
  }
}
