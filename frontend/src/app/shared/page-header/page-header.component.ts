import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent, IconName } from '../icon/icon.component';

export type HeaderColor = 'azul' | 'verde' | 'naranja' | 'cian' | 'morado' | 'gris' | 'rojo';

/**
 * Encabezado de módulo con identidad visual: chip de icono en color de marca,
 * título en el tono del módulo, subtítulo, barra de acento y slot para acciones.
 * Uso:  <app-page-header icono="clipboard" color="azul" titulo="Pedidos" subtitulo="...">
 *          <button>+ Nuevo</button>
 *        </app-page-header>
 */
@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [CommonModule, IconComponent],
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss'
})
export class PageHeaderComponent {
  @Input() titulo = '';
  @Input() subtitulo?: string;
  @Input() icono?: IconName;
  @Input() color: HeaderColor = 'azul';
}
