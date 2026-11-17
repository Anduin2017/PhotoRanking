import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PhotoService, SpeechJob } from './photo';

describe('PhotoService', () => {
  let service: PhotoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PhotoService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(PhotoService);
    http = TestBed.inject(HttpTestingController);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); vi.useRealTimers(); });

  const base = '/api/summary/photos/1/speech/jobs';
  const queued: SpeechJob = { id: 'bound-job', status: 'queued', queue_position: 2, completed_segments: 0, total_segments: 0 };

  it('submits once, reports queue and segment progress, then downloads completed audio', () => {
    vi.useFakeTimers();
    const values: (SpeechJob | Blob)[] = [];
    service.getSummarySpeech(1, 'final').subscribe(value => values.push(value));
    http.expectOne(base).flush(queued);
    expect(values).toEqual([queued]);
    vi.advanceTimersByTime(1500);
    const status = http.expectOne(base + '/bound-job');
    expect(status.request.body).toEqual({ summary: 'final' });
    status.flush({ ...queued, status: 'running', completed_segments: 1, total_segments: 3 });
    expect((values[1] as SpeechJob).completed_segments).toBe(1);
    vi.advanceTimersByTime(1500);
    http.expectOne(base + '/bound-job').flush({ ...queued, status: 'completed' });
    const audio = new Blob(['wav']);
    http.expectOne(base + '/bound-job/audio').flush(audio);
    expect(values.at(-1)).toBe(audio);
    vi.advanceTimersByTime(10000);http.expectNone(base);
  });

  it('stops status polling when the viewer unsubscribes', () => {
    vi.useFakeTimers();
    const subscription = service.getSummarySpeech(1, 'final').subscribe();
    http.expectOne(base).flush(queued);
    subscription.unsubscribe();vi.advanceTimersByTime(10000);
    http.expectNone(base + '/bound-job');
  });

  it('resubmits an expired handle once and reuses the server job protocol', () => {
    vi.useFakeTimers();
    const values: (SpeechJob | Blob)[] = [];
    service.getSummarySpeech(1, 'final').subscribe(value => values.push(value));
    http.expectOne(base).flush(queued);
    vi.advanceTimersByTime(1500);
    http.expectOne(base + '/bound-job').flush({ ...queued, status: 'expired' });
    http.expectOne(base).flush({ ...queued, id: 'fresh-bound-job', status: 'completed' });
    http.expectOne(base + '/fresh-bound-job/audio').flush(new Blob(['wav']));
    expect(values.at(-1) instanceof Blob).toBe(true);
  });

  it('leaves a full queue retry to the user instead of repeatedly submitting', () => {
    const error = vi.fn();
    service.getSummarySpeech(1, 'final').subscribe({ error });
    http.expectOne(base).flush({}, { status: 429, statusText: 'Queue full' });
    expect(error.mock.calls[0][0].status).toBe(429);
    http.expectNone(base);
  });
});
