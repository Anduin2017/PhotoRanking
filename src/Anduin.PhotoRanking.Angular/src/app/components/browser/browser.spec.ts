import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BrowserComponent } from './browser';

describe('BrowserComponent', () => {
  let component: BrowserComponent;
  let fixture: ComponentFixture<BrowserComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BrowserComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BrowserComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
