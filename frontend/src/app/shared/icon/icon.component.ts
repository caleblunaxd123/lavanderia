import { Component, Input } from '@angular/core';

export type IconName =
  | 'close' | 'edit' | 'trash' | 'disable' | 'refresh' | 'check' | 'fire' | 'printer'
  | 'cash' | 'warning' | 'calendar' | 'ban' | 'note' | 'download' | 'basket' | 'phone-alert'
  | 'package' | 'pin' | 'clipboard' | 'money-bag' | 'users' | 'whatsapp' | 'plus' | 'search'
  | 'chevron-left' | 'chevron-right' | 'chevron-first' | 'chevron-last' | 'arrow-left' | 'arrow-right'
  | 'smartphone' | 'bank' | 'credit-card' | 'info';

@Component({
  selector: 'app-icon',
  standalone: true,
  templateUrl: './icon.component.html',
  styleUrl: './icon.component.scss'
})
export class IconComponent {
  @Input() name: IconName = 'check';
  @Input() size = 16;
}
