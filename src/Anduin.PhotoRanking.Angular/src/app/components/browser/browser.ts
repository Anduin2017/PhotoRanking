import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService, Album } from '../../services/photo';
import { Router } from '@angular/router';

@Component({
  selector: 'app-browser',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './browser.html',
  styleUrl: './browser.css',
})
export class BrowserComponent implements OnInit {
  currentPath = '';
  cachedAlbums: Album[] = [];
  isLoading = true;

  // Computed view model
  breadcrumbParts: { name: string, path: string }[] = [];
  folders: { name: string, fullPath: string }[] = [];
  albums: Album[] = [];

  constructor(public photoService: PhotoService, private router: Router) { }

  ngOnInit() {
    this.photoService.getAlbums().subscribe({
      next: (albums) => {
        this.cachedAlbums = albums;
        this.isLoading = false;
        this.renderView();
      },
      error: (err) => {
        console.error('Error loading browser', err);
        this.isLoading = false;
      }
    });
  }

  // Navigate to a path (folder or root)
  navigate(path: string) {
    this.currentPath = path;
    this.renderView();
  }

  renderView() {
    // 1. Update Breadcrumbs
    this.breadcrumbParts = [];
    if (this.currentPath) {
      const parts = this.currentPath.split('/');
      let accumulator = '';
      parts.forEach((part, index) => {
        accumulator += (index > 0 ? '/' : '') + part;
        this.breadcrumbParts.push({ name: part, path: accumulator });
      });
    }

    // 2. Filter Content
    const folderMap = new Map<string, string>();
    const currentLevelAlbums: Album[] = [];

    this.cachedAlbums.forEach(album => {
      const albumPath = album.albumId;

      if (albumPath.startsWith(this.currentPath ? this.currentPath + '/' : '') ||
        (this.currentPath === '' && !albumPath.includes('/'))) {

        const relativePath = this.currentPath ? albumPath.substring(this.currentPath.length + 1) : albumPath;
        const parts = relativePath.split('/');

        if (parts.length === 1) {
          currentLevelAlbums.push(album);
        } else {
          const folderName = parts[0];
          const folderPath = this.currentPath ? `${this.currentPath}/${folderName}` : folderName;
          if (!folderMap.has(folderName)) {
            folderMap.set(folderName, folderPath);
          }
        }
      }
    });

    this.folders = Array.from(folderMap.entries())
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([name, fullPath]) => ({ name, fullPath }));

    this.albums = currentLevelAlbums.sort((a, b) => a.name.localeCompare(b.name));
  }

  openAlbum(albumId: string) {
    this.router.navigate(['/album', albumId]);
  }
}
