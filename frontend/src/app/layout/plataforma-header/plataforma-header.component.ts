import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-plataforma-header',
  imports: [CommonModule, RouterLink],
  templateUrl: './plataforma-header.component.html',
  styleUrl: './plataforma-header.component.scss'
})
export class PlataformaHeaderComponent {
  private readonly auth = inject(AuthService);

  logout() { this.auth.logout(); }
}
