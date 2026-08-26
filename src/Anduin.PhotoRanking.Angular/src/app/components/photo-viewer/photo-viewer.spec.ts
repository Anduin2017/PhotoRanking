import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PhotoViewerComponent } from './photo-viewer';
import { PhotoService } from '../../services/photo';
import { Router } from '@angular/router';
import { of } from 'rxjs';

describe('PhotoViewerComponent', () => {
  let component: PhotoViewerComponent;
  let fixture: ComponentFixture<PhotoViewerComponent>;

  const photoServiceMock = {
    getImageUrl: (path: string) => path,
    getPhoto: (id: number) => of({}),
    viewPhoto: (id: number) => of({}),
    ratePhoto: (id: number, score: number) => of({}),
    deletePhoto: (id: number) => of({}),
    guessScore: (id: number) => of({ predictedScore: 0, votes: {} })
  };

  const routerMock = {
    navigate: () => {}
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PhotoViewerComponent],
      providers: [
        { provide: PhotoService, useValue: photoServiceMock },
        { provide: Router, useValue: routerMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(PhotoViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('offers the complete zero-to-six rating scale', () => {
    expect(component.scoreOptions).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });

  it('accepts zero and one as keyboard ratings', () => {
    const ratedScores: number[] = [];
    component.ratePhoto = (score: number) => ratedScores.push(score);

    component.handleKeyboardEvent(new KeyboardEvent('keydown', { key: '0' }));
    component.handleKeyboardEvent(new KeyboardEvent('keydown', { key: '1' }));

    expect(ratedScores).toEqual([0, 1]);
  });
});
