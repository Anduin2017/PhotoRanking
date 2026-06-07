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
});