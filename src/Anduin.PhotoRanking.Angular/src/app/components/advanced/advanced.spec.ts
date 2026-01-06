import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Advanced } from './advanced';

describe('Advanced', () => {
  let component: Advanced;
  let fixture: ComponentFixture<Advanced>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Advanced]
    })
    .compileComponents();

    fixture = TestBed.createComponent(Advanced);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
