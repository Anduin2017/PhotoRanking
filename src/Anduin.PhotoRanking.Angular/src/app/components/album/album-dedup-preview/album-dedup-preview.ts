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
  selectedDuplicates: { [id: number]: boolean } = {};
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
        // Select all duplicates by default
        this.selectedDuplicates = {};
        for (const group of this.groups) {
          for (const dup of group.duplicates) {
            this.selectedDuplicates[dup.id] = true;
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
    return Object.values(this.selectedDuplicates).filter((v) => v).length;
  }

  deleteSelected() {
    const idsToDelete = Object.keys(this.selectedDuplicates)
      .map((k) => parseInt(k, 10))
      .filter((id) => this.selectedDuplicates[id]);

    if (idsToDelete.length === 0) return;

    if (!confirm(`Are you sure you want to permanently delete ${idsToDelete.length} photos?`)) {
      return;
    }

    this.isDeleting = true;
    this.photoService.bulkDelete(idsToDelete).subscribe({
      next: () => {
        this.isDeleting = false;
        // Navigation back to album
        this.router.navigate(['/album', this.albumId]);
      },
      error: (err) => {
        console.error('Failed to delete photos', err);
        this.isDeleting = false;
      },
    });
  }
}
