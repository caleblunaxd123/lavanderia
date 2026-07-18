/** Celular móvil de Perú: 9 dígitos que empiezan con 9. */
export const CELULAR_PERU = /^9\d{8}$/;

/** True si el celular está vacío (opcional) o tiene formato válido de celular peruano. */
export function esCelularValido(cel: string | null | undefined): boolean {
  const c = (cel ?? '').trim();
  return c.length === 0 || CELULAR_PERU.test(c);
}

/** True solo si es un celular válido y no vacío (para campos obligatorios). */
export function esCelularObligatorioValido(cel: string | null | undefined): boolean {
  return CELULAR_PERU.test((cel ?? '').trim());
}
