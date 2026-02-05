import { Component, OnInit } from '@angular/core';
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
  photos: Photo[] = [];
  isLoading = true;

  viewerOpen = false;
  initialPhotoId: number | null = null;

  constructor(private route: ActivatedRoute, public photoService: PhotoService) { }

  ngOnInit() {
    this.route.params.subscribe(params => {
      this.targetId = +params['id'];
      if (this.targetId) {
        this.loadSimilar(this.targetId);
      }
    });
  }

  loadSimilar(id: number) {
    this.isLoading = true;
    this.photoService.getSimilarPhotos(id).subscribe({
      next: (photos) => {
        this.photos = photos;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading similar photos', err);
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
