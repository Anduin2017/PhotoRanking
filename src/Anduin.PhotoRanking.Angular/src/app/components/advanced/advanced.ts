import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService } from '../../services/photo';

@Component({
  selector: 'app-advanced',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './advanced.html',
  styleUrl: './advanced.css',
})
export class AdvancedComponent implements OnInit {
  isLoading = true;
  stats: any = null;

  constructor(public photoService: PhotoService) { }

  ngOnInit() {
    this.photoService.getTopStats().subscribe({
      next: (data) => {
        this.stats = data;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading stats', err);
        this.isLoading = false;
      }
    });
  }
}
