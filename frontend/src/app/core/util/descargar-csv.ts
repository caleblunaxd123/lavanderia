export type CeldaCsv = string | number | boolean | null | undefined;

/**
 * Descarga una tabla como CSV (UTF-8 con BOM, para que Excel respete tildes y la ñ).
 * Los números van sin comillas para que Excel los trate como números y se puedan sumar/graficar.
 */
export function descargarCsv(nombreArchivo: string, columnas: string[], filas: CeldaCsv[][]): void {
  const celda = (v: CeldaCsv): string => {
    if (v === null || v === undefined) return '';
    if (typeof v === 'number') return Number.isFinite(v) ? String(v) : '';
    if (typeof v === 'boolean') return v ? 'Sí' : 'No';
    return `"${v.replace(/"/g, '""')}"`;
  };
  const lineas = [
    columnas.map(c => celda(c)).join(','),
    ...filas.map(f => f.map(celda).join(','))
  ];
  const blob = new Blob(['﻿' + lineas.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombreArchivo.endsWith('.csv') ? nombreArchivo : `${nombreArchivo}.csv`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** "2026-11-01T09:05:00" → "2026-11-01 09:05" (legible y ordenable en Excel). */
export function fechaHoraCsv(valor: string | null | undefined): string {
  return (valor ?? '').slice(0, 16).replace('T', ' ');
}
