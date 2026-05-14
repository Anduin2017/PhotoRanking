import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, Subject, tap } from 'rxjs';

export interface Album {
  albumId: string;
  name: string;
  albumScore: number;
  knownRate: number;
  standardDeviation: number;
  photoCount: number;
  thumbnailPath?: string;
  highestScore?: number;
  createdAt: string;
  updatedAt: string;
}

export interface GuessScoreResult {
  predictedScore: number;
  votes: { [score: number]: number };
}

export interface Photo {
  id: number;
  filePath: string;
  albumId: string;
  album?: Album;
  overallScore: number;
  independentScore?: number;
  knownness: number;
  ratingCount: number;
  viewCount: number;
  similarity?: number;
  estimatedScore?: number;
  createdAt: string;
}

export interface AlbumDetails {
  album: Album;
  photos: Photo[];
}

export interface GlobalStats {
  waitingCount: number;
  ratedCount: number;
  fullyUnknownAlbumCount: number;
  fullyKnownAlbumCount: number;
  scoreDistribution: { [key: number]: number };
  averagePhotosPerAlbum: number;
  averageAlbumKnownRate: number;
  overallAverageScore: number;
  indexedPhotoCount: number;
  totalPhotoCount: number;
}

@Injectable({
  providedIn: 'root',
})
export class PhotoService {
  private apiBase = '/api';

  public ratingChanged$ = new Subject<void>();

  constructor(private http: HttpClient) { }

  getImageUrl(filePath: string): string {
    return `${this.apiBase}/images/${encodeURI(filePath)}`;
  }

  getFeed(size: number = 20, pool: number = 200): Observable<Photo[]> {
    return this.http.get<Photo[]>(`${this.apiBase}/photos/feed?size=${size}&pool=${pool}`);
  }

  getPhoto(id: number): Observable<Photo> {
    return this.http.get<Photo>(`${this.apiBase}/photos/${id}`);
  }

  ratePhoto(id: number, score: number): Observable<Photo> {
    return this.http.post<Photo>(`${this.apiBase}/photos/${id}/rate`, { score }).pipe(
      tap(() => this.ratingChanged$.next())
    );
  }

  viewPhoto(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiBase}/photos/${id}/view`, {});
  }

  getAlbums(): Observable<Album[]> {
    return this.http.get<Album[]>(`${this.apiBase}/albums`);
  }

  getAlbum(albumId: string, sortBy: string = 'filename'): Observable<AlbumDetails> {
    // Handling encoded albumId if necessary, angular HttpParams usually handles this but manual encoding might be needed for path params
    return this.http.get<AlbumDetails>(`${this.apiBase}/albums/${encodeURIComponent(albumId)}?sortBy=${sortBy}`);
  }

  getDiscoverPhotos(mode: string, page: number, pageSize: number, minScore?: number, sort?: string): Observable<Photo[]> {
    let url = `${this.apiBase}/photos/discover?mode=${mode}&page=${page}&pageSize=${pageSize}`;
    if (minScore !== undefined && minScore !== null) {
      url += `&minScore=${minScore}`;
    }
    if (sort) {
      url += `&sort=${sort}`;
    }
    return this.http.get<Photo[]>(url);
  }

  getGlobalStats(): Observable<GlobalStats> {
    return this.http.get<GlobalStats>(`${this.apiBase}/admin/global-stats`);
  }

  getTopStats(): Observable<any> {
    return this.http.get<any>(`${this.apiBase}/photos/stats/top`);
  }

  getMoreStats(endpoint: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiBase}/${endpoint}`);
  }

  getSimilarPhotos(id: number, skip: number = 0, take: number = 20): Observable<Photo[]> {
    return this.http.get<Photo[]>(`${this.apiBase}/photos/${id}/similar?skip=${skip}&take=${take}`);
  }

  guessScore(id: number): Observable<GuessScoreResult> {
    return this.http.get<GuessScoreResult>(`${this.apiBase}/photos/${id}/guess-score`);
  }

  getDedupPreview(albumId: string, similarity: number = 93.0): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiBase}/albums/dedup-preview/${encodeURIComponent(albumId)}?similarity=${similarity}`);
  }

  bulkDelete(photoIds: number[]): Observable<any> {
    return this.http.post<any>(`${this.apiBase}/photos/bulk-delete`, photoIds);
  }

  searchByImage(file: File, take: number = 20): Observable<Photo[]> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<Photo[]>(`${this.apiBase}/photos/search-by-image?take=${take}`, formData);
  }
}
