import { AfterViewInit, Component, ElementRef, EventEmitter, Input, NgZone, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild, inject } from '@angular/core';
import * as L from 'leaflet';
import { IconComponent } from '../icon/icon.component';

export interface UbicacionMapa {
  latitud: number;
  longitud: number;
  /** Dirección aproximada resuelta desde el mapa (reverse geocoding). */
  direccion?: string;
  /** Distrito resuelto desde el mapa. */
  distrito?: string;
}

@Component({
  selector: 'app-mapa-ubicacion',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './mapa-ubicacion.component.html',
  styleUrl: './mapa-ubicacion.component.scss'
})
export class MapaUbicacionComponent implements AfterViewInit, OnChanges, OnDestroy {
  private readonly zone = inject(NgZone);

  @ViewChild('mapa', { static: true }) mapaElement!: ElementRef<HTMLDivElement>;
  @Input() latitud: number | null = null;
  @Input() longitud: number | null = null;
  @Output() ubicacionChange = new EventEmitter<UbicacionMapa | null>();

  ubicando = false;
  geocodificando = false;
  errorUbicacion = '';

  private mapa?: L.Map;
  private marcador?: L.CircleMarker;

  ngAfterViewInit(): void {
    this.mapa = L.map(this.mapaElement.nativeElement, {
      center: this.tieneCoordenadas() ? [this.latitud!, this.longitud!] : [-12.0464, -77.0428],
      zoom: this.tieneCoordenadas() ? 17 : 12,
      zoomControl: true
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(this.mapa);

    this.mapa.on('click', ({ latlng }: L.LeafletMouseEvent) => {
      this.zone.run(() => this.establecerPunto(latlng.lat, latlng.lng, true));
    });

    this.actualizarMarcador(false);
    setTimeout(() => this.mapa?.invalidateSize(), 0);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['latitud'] || changes['longitud']) && this.mapa) {
      this.actualizarMarcador(false);
    }
  }

  usarMiUbicacion(): void {
    this.errorUbicacion = '';
    if (!navigator.geolocation) {
      this.errorUbicacion = 'Este dispositivo no permite obtener la ubicación.';
      return;
    }
    // La geolocalización del navegador solo funciona en un contexto seguro (HTTPS)
    // o en localhost. Desde el celular por IP local (http://192.168...) queda bloqueada.
    if (!window.isSecureContext) {
      this.errorUbicacion = 'La ubicación automática necesita una conexión segura (HTTPS). Por ahora marca el punto tocando el mapa.';
      return;
    }

    this.ubicando = true;
    navigator.geolocation.getCurrentPosition(
      posicion => this.zone.run(() => {
        this.ubicando = false;
        this.establecerPunto(posicion.coords.latitude, posicion.coords.longitude, true);
      }),
      (err: GeolocationPositionError) => this.zone.run(() => {
        this.ubicando = false;
        this.errorUbicacion = err.code === err.PERMISSION_DENIED
          ? 'Bloqueaste el acceso a la ubicación. Habilítalo en el navegador o marca el punto en el mapa.'
          : err.code === err.TIMEOUT
            ? 'La ubicación tardó demasiado. Intenta de nuevo o marca el punto en el mapa.'
            : 'No pudimos obtener la ubicación. Actívala o marca el punto en el mapa.';
      }),
      { enableHighAccuracy: true, timeout: 10000, maximumAge: 30000 }
    );
  }

  limpiar(): void {
    this.latitud = null;
    this.longitud = null;
    this.marcador?.remove();
    this.marcador = undefined;
    this.ubicacionChange.emit(null);
  }

  ngOnDestroy(): void {
    this.mapa?.remove();
  }

  private tieneCoordenadas(): boolean {
    return Number.isFinite(this.latitud) && Number.isFinite(this.longitud);
  }

  private establecerPunto(latitud: number, longitud: number, emitir: boolean): void {
    this.latitud = Number(latitud.toFixed(6));
    this.longitud = Number(longitud.toFixed(6));
    this.errorUbicacion = '';
    this.actualizarMarcador(true);
    if (!emitir) return;

    // Al elegir un punto, resolvemos dirección y distrito para autocompletar los campos.
    const lat = this.latitud, lon = this.longitud;
    this.geocodificando = true;
    this.reverseGeocode(lat, lon).then(dir => {
      this.zone.run(() => {
        this.geocodificando = false;
        this.ubicacionChange.emit({ latitud: lat, longitud: lon, direccion: dir?.direccion, distrito: dir?.distrito });
      });
    }).catch(() => {
      this.zone.run(() => {
        this.geocodificando = false;
        this.ubicacionChange.emit({ latitud: lat, longitud: lon });
      });
    });
  }

  /** Reverse geocoding gratuito con Nominatim (OpenStreetMap) — sin API key. */
  private async reverseGeocode(lat: number, lon: number): Promise<{ direccion?: string; distrito?: string } | null> {
    const ctrl = new AbortController();
    const t = setTimeout(() => ctrl.abort(), 6000);
    try {
      const url = `https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${lat}&lon=${lon}&addressdetails=1&accept-language=es`;
      const resp = await fetch(url, { signal: ctrl.signal, headers: { 'Accept': 'application/json' } });
      if (!resp.ok) return null;
      const data = await resp.json();
      const a = data.address ?? {};
      const calle = [a.road, a.house_number].filter(Boolean).join(' ').trim();
      const direccion = calle || (typeof data.display_name === 'string' ? data.display_name.split(',')[0] : undefined);
      const distrito = a.city_district || a.suburb || a.town || a.quarter || a.neighbourhood || a.village || undefined;
      return { direccion: direccion || undefined, distrito };
    } finally {
      clearTimeout(t);
    }
  }

  private actualizarMarcador(centrar: boolean): void {
    if (!this.mapa || !this.tieneCoordenadas()) return;
    const punto: L.LatLngExpression = [this.latitud!, this.longitud!];
    if (!this.marcador) {
      this.marcador = L.circleMarker(punto, {
        radius: 9,
        color: '#ffffff',
        weight: 3,
        fillColor: '#0b57d0',
        fillOpacity: 1
      }).addTo(this.mapa);
    } else {
      this.marcador.setLatLng(punto);
    }
    if (centrar) this.mapa.setView(punto, Math.max(this.mapa.getZoom(), 17));
  }
}
