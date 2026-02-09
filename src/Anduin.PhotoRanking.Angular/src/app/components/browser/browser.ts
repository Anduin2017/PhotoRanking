import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService, Album } from '../../services/photo';
import { Router, ActivatedRoute } from '@angular/router';

export type SortOption = 'name' | 'knownRate' | 'albumScore' | 'photoCount';

interface FolderItem {
  name: string;
  fullPath: string;
  isParent?: boolean;
  photoCount: number;
  albumScore: number;
  knownRate: number;
}

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
  sortBy: SortOption = 'name';

  // Computed view model
  breadcrumbParts: { name: string, path: string }[] = [];
  folders: FolderItem[] = [];
  albums: Album[] = [];

  constructor(
    public photoService: PhotoService,
    private router: Router,
    private route: ActivatedRoute) { }

  ngOnInit() {
    this.photoService.getAlbums().subscribe({
      next: (albums) => {
        this.cachedAlbums = albums;
        this.isLoading = false;
        
        // Listen to route changes
        this.route.params.subscribe(params => {
          this.currentPath = params['path'] || '';
          this.renderView();
        });
      },
      error: (err) => {
        console.error('Error loading browser', err);
        this.isLoading = false;
      }
    });

    const savedSort = localStorage.getItem('browser_sort_by') as SortOption;
    if (savedSort) {
      this.sortBy = savedSort;
    }
  }

  // Navigate to a path (folder or root)
  navigate(path: string) {
    if (path === '') {
      this.router.navigate(['/browser']);
    } else {
      this.router.navigate(['/browser', ...path.split('/')]);
    }
  }

  changeSort(option: SortOption) {
    this.sortBy = option;
    localStorage.setItem('browser_sort_by', option);
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
    const folderMap = new Map<string, string>(); // name -> fullPath
    const currentLevelAlbums: Album[] = [];

    this.cachedAlbums.forEach(album => {
      const albumPath = album.albumId;

      if (albumPath === this.currentPath) {
        // This case might happen if currentPath is actually an album
        // But usually we go to /album/:id for that.
        // However, it's possible an album is also a folder.
        return; 
      }

      if (albumPath.startsWith(this.currentPath ? this.currentPath + '/' : '')) {
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

    // 3. Aggregate Folder Stats
    const folders: FolderItem[] = [];
    folderMap.forEach((fullPath, name) => {
      const descendantAlbums = this.cachedAlbums.filter(a => 
        a.albumId === fullPath || a.albumId.startsWith(fullPath + '/')
      );
      
      const totalPhotos = descendantAlbums.reduce((sum, a) => sum + a.photoCount, 0);
      const avgScore = totalPhotos > 0 
          ? descendantAlbums.reduce((sum, a) => sum + a.albumScore * a.photoCount, 0) / totalPhotos
          : 0;
      const avgKnown = totalPhotos > 0
          ? descendantAlbums.reduce((sum, a) => sum + a.knownRate * a.photoCount, 0) / totalPhotos
          : 0;

      folders.push({
        name,
        fullPath,
        photoCount: totalPhotos,
        albumScore: avgScore,
        knownRate: avgKnown
      });
    });

    // 4. Sort
    const sortFn = (a: any, b: any) => {
      if (this.sortBy === 'name') return a.name.localeCompare(b.name);
      if (this.sortBy === 'knownRate') return b.knownRate - a.knownRate;
      if (this.sortBy === 'albumScore') return b.albumScore - a.albumScore;
      if (this.sortBy === 'photoCount') return b.photoCount - a.photoCount;
      return 0;
    };

    this.folders = folders.sort(sortFn);
    
    // Add parent folder if not at root
    if (this.currentPath) {
      const lastSlashIndex = this.currentPath.lastIndexOf('/');
      const parentPath = lastSlashIndex === -1 ? '' : this.currentPath.substring(0, lastSlashIndex);
      this.folders.unshift({ 
        name: '.. (上级目录)', 
        fullPath: parentPath, 
        isParent: true,
        photoCount: 0,
        albumScore: 0,
        knownRate: 0
      });
    }

    this.albums = currentLevelAlbums.sort(sortFn);
  }

  openAlbum(albumId: string) {
    this.router.navigate(['/album', albumId]);
  }
}
