import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Album } from './album';

describe('Album', () => {
  let component: Album;
  let fixture: ComponentFixture<Album>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Album]
    })
    .compileComponents();

    fixture = TestBed.createComponent(Album);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
