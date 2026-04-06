import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Photo, PhotoService } from '../../../services/photo';

interface DedupGroup {
  bestPhoto: Photo;
  duplicates: Photo[];
}

@Component({
  selector: 'app-album-dedup-preview',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './album-dedup-preview.html',
  styleUrl: './album-dedup-preview.css',
})
export class AlbumDedupPreviewComponent implements OnInit {
  albumId: string = '';
  similarity: number = 93;
  groups: DedupGroup[] = [];
  isLoading = true;
  // All photos in dedup view are tracked here – best photo starts unchecked, duplicates start checked
  selectedPhotos: { [id: number]: boolean } = {};
  isDeleting = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    public photoService: PhotoService
  ) {}

  ngOnInit() {
    this.route.params.subscribe((params) => {
      this.albumId = params['id'];
      this.route.queryParams.subscribe((queryParams) => {
        if (queryParams['similarity']) {
          this.similarity = parseFloat(queryParams['similarity']);
        }
        if (this.albumId) {
          this.loadPreview();
        }
      });
    });
  }

  loadPreview() {
    this.isLoading = true;
    this.photoService.getDedupPreview(this.albumId, this.similarity).subscribe({
      next: (data) => {
        this.groups = data;
        this.selectedPhotos = {};
        for (const group of this.groups) {
          // Best photo: unchecked by default (recommended to keep)
          this.selectedPhotos[group.bestPhoto.id] = false;
          // Duplicates: checked by default (recommended to delete)
          for (const dup of group.duplicates) {
            this.selectedPhotos[dup.id] = true;
          }
        }
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading dedup preview', err);
        this.isLoading = false;
      },
    });
  }

  get selectedCount(): number {
    return Object.values(this.selectedPhotos).filter((v) => v).length;
  }

  deleteSelected() {
    const idsToDelete = Object.keys(this.selectedPhotos)
      .map((k) => parseInt(k, 10))
      .filter((id) => this.selectedPhotos[id]);

    if (idsToDelete.length === 0) return;

    if (!confirm(`确定要永久删除 ${idsToDelete.length} 张照片吗？此操作不可撤销。`)) {
      return;
    }

    this.isDeleting = true;
    this.photoService.bulkDelete(idsToDelete).subscribe({
      next: () => {
        this.isDeleting = false;
        this.router.navigate(['/album', this.albumId]);
      },
      error: (err) => {
        console.error('Failed to delete photos', err);
        this.isDeleting = false;
      },
    });
  }
}

