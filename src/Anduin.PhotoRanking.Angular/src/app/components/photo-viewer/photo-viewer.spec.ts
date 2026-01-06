import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PhotoViewer } from './photo-viewer';

describe('PhotoViewer', () => {
  let component: PhotoViewer;
  let fixture: ComponentFixture<PhotoViewer>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PhotoViewer]
    })
    .compileComponents();

    fixture = TestBed.createComponent(PhotoViewer);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
