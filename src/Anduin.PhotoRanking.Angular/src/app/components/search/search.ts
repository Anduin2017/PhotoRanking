import { Component, HostListener, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Photo, PhotoService } from '../../services/photo';
import { PhotoViewerComponent } from '../photo-viewer/photo-viewer';

@Component({
  selector: 'app-search',
  standalone: true,
  imports: [CommonModule, PhotoViewerComponent],
  templateUrl: './search.html',
  styleUrl: './search.css',
})
export class SearchComponent implements OnDestroy {
  photos: Photo[] = [];

  queryImageUrl: string | null = null;
  isLoading = false;
  hasSearched = false;
  dragOver = false;

  viewerOpen = false;
  initialPhotoId: number | null = null;

  private objectUrl: string | null = null;

  constructor(public photoService: PhotoService) { }

  ngOnDestroy() {
    this.revokeObjectUrl();
  }

  private revokeObjectUrl() {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }

  onDragOver(event: DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = true;
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = false;
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver = false;

    const files = event.dataTransfer?.files;
    if (files?.length) {
      this.handleFile(files[0]);
    }
  }

  onFileInputChange(event: Event) {
    const input = event.target as HTMLInputElement;
    const files = input.files;
    if (files?.length) {
      this.handleFile(files[0]);
    }
    input.value = '';
  }

  private handleFile(file: File) {
    if (!file.type.startsWith('image/')) return;

    this.revokeObjectUrl();
    this.objectUrl = URL.createObjectURL(file);
    this.queryImageUrl = this.objectUrl;

    this.isLoading = true;
    this.hasSearched = true;
    this.photos = [];

    this.photoService.searchByImage(file).subscribe({
      next: (results) => {
        this.photos = results;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Search failed', err);
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
