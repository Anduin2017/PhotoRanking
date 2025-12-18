import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';

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
  createdAt: string;
}

export interface AlbumDetails {
  album: Album;
  photos: Photo[];
}

@Injectable({
  providedIn: 'root',
})
export class PhotoService {
  private apiBase = '/api';

  constructor(private http: HttpClient) { }

  getImageUrl(filePath: string): string {
    return `${this.apiBase}/images/${filePath}`;
  }

  getFeed(page: number, pageSize: number): Observable<Photo[]> {
    return this.http.get<Photo[]>(`${this.apiBase}/photos/feed?page=${page}&pageSize=${pageSize}`);
  }

  getPhoto(id: number): Observable<Photo> {
    return this.http.get<Photo>(`${this.apiBase}/photos/${id}`);
  }

  ratePhoto(id: number, score: number): Observable<Photo> {
    return this.http.post<Photo>(`${this.apiBase}/photos/${id}/rate`, { score });
  }

  viewPhoto(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiBase}/photos/${id}/view`, {});
  }

  getAlbums(): Observable<Album[]> {
    return this.http.get<Album[]>(`${this.apiBase}/albums`);
  }

  getAlbum(albumId: string): Observable<AlbumDetails> {
    // Handling encoded albumId if necessary, angular HttpParams usually handles this but manual encoding might be needed for path params
    return this.http.get<AlbumDetails>(`${this.apiBase}/albums/${encodeURIComponent(albumId)}`);
  }

  getDiscoverPhotos(mode: string, page: number, pageSize: number): Observable<Photo[]> {
    return this.http.get<Photo[]>(`${this.apiBase}/photos/discover?mode=${mode}&page=${page}&pageSize=${pageSize}`);
  }

  getTopStats(): Observable<any> {
    return this.http.get<any>(`${this.apiBase}/photos/stats/top`);
  }
}
