import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, Subject, tap } from 'rxjs';

export interface Album {
  albumId: string;
  name: string;
  albumScore: number;
  ratedRate: number;
  ratedPhotoCount: number;
  averageManualScore?: number;
  standardDeviation: number;
  photoCount: number;
  thumbnailPath?: string;
  highestScore?: number;
  createdAt: string;
  updatedAt: string;
}

export interface GuessScoreResult {
  predictedScore: number;
  uncertainty?: number | null;
  novelty?: number | null;
  modelVersion?: string;
  votes: { [score: number]: number };
}

export interface Photo {
  id: number;
  filePath: string;
  albumId: string;
  album?: Album;
  manualScore?: number | null;
  // Legacy wire fields remain optional while old containers/clients roll forward.
  overallScore?: number;
  independentScore?: number | null;
  knownness?: number;
  ratingCount?: number;
  viewCount: number;
  similarity?: number;
  estimatedScore?: number | null;
  predictedScore?: number | null;
  displayScore?: number | null;
  estimatedScoreModelVersion?: string;
  predictionUncertainty?: number | null;
  predictionNovelty?: number | null;
  feedRank?: number | null;
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
  fullyUnratedAlbumCount: number;
  fullyRatedAlbumCount: number;
  scoreDistribution: { [key: number]: number };
  averagePhotosPerAlbum: number;
  overallAverageScore: number;
  manualAverageScore: number;
  averageAlbumRatedRate: number;
  indexedPhotoCount: number;
  totalPhotoCount: number;
  predictionEvaluationCount: number;
  predictionMeanAbsoluteError?: number;
  predictionWithinOneRate?: number;
  activePredictionModelVersion?: string;
  activePredictionModelTrainedAt?: string;
  activePredictionModelRatingWatermark?: string;
  activePredictionModelTrainingPhotoCount?: number;
  activePredictionCoverageTrainingPhotoCount?: number;
  activePredictionModelValidationMae?: number;
  activePredictionModelEnsembleSize?: number;
  predictionReadyCount: number;
  activeLearningReadyCount: number;
  averagePredictionUncertainty?: number;
  averagePredictionNovelty?: number;
  activePredictionCoverageCentroidCount?: number;
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

  getFeed(
    size: number = 20,
    beforeScore?: number,
    beforeId?: number,
    seed?: number,
    beforeRank?: number): Observable<Photo[]> {
    let url = `${this.apiBase}/photos/feed?size=${size}`;
    if (beforeId !== undefined) {
      url += `&beforeId=${beforeId}`;
    }
    if (beforeScore !== undefined) {
      url += `&beforeScore=${beforeScore}`;
    }
    if (seed !== undefined) {
      url += `&seed=${seed}`;
    }
    if (beforeRank !== undefined) {
      url += `&beforeRank=${beforeRank}`;
    }
    return this.http.get<Photo[]>(url);
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

  getDiscoverPhotos(
    mode: string,
    page: number,
    pageSize: number,
    minScore?: number,
    maxScore?: number,
    sort?: string,
    shuffleSeed?: number): Observable<Photo[]> {
    let url = `${this.apiBase}/photos/discover?mode=${mode}&page=${page}&pageSize=${pageSize}`;
    if (minScore !== undefined && minScore !== null) {
      url += `&minScore=${minScore}`;
    }
    if (maxScore !== undefined && maxScore !== null) {
      url += `&maxScore=${maxScore}`;
    }
    if (sort) {
      url += `&sort=${sort}`;
    }
    if (shuffleSeed !== undefined) {
      url += `&shuffleSeed=${shuffleSeed}`;
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

  deletePhoto(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiBase}/photos/${id}`).pipe(
      tap(() => this.ratingChanged$.next())
    );
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
