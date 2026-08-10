// ============================================================
// Marca del PRODUCTO (el SaaS en sí), NO de una lavandería.
// ------------------------------------------------------------
// "LaviSystem" es el nombre comercial del sistema que se alquila
// a las lavanderías. Cada lavandería (tenant) tiene su propia
// marca (nombre/logo/colores) que se muestra dentro de /{slug}/...
//
// Este nombre aparece en el login neutral (/login, acceso del
// propietario), el panel de plataforma y el título del navegador.
//
// 👉 Si algún día se decide otro nombre comercial, se cambia
//    SOLO aquí y se actualiza en toda la app.
// ============================================================

/** Nombre comercial del producto SaaS. */
export const PRODUCTO_NOMBRE = 'LaviSystem';

/** Bajada / descripción corta del producto. */
export const PRODUCTO_TAGLINE = 'Sistema de gestión para lavanderías';

/** Logo completo a color (para fondos claros). */
export const PRODUCTO_LOGO = 'lavisystem-logo-trans.png';
/** Logo completo en blanco (para fondos oscuros: login, plataforma). */
export const PRODUCTO_LOGO_BLANCO = 'lavisystem-logo-white.png';
/** Solo la marca (nube) en blanco, para encabezados compactos. */
export const PRODUCTO_ICONO_BLANCO = 'lavisystem-icon-white.png';
/** Marca (nube+plancha) a color adaptada para fondo navy: plancha blanca, nube cian/teal. */
export const PRODUCTO_MARCA_NAVY = 'lavisystem-mark-navy.png';
/** Logo OFICIAL completo (marca + wordmark + bajada) del archivo de marca, sobre fondo navy. */
export const PRODUCTO_LOGO_LOGIN = 'lavisystem-login.png';
/** Logo OFICIAL a color (para fondos claros: panel izquierdo blanco del login). */
export const PRODUCTO_LOGO_COLOR = 'lavisystem-logo-color.png';

/** Crédito del desarrollador (aparece en el login y en el pie del sidebar). */
export const DESARROLLADOR_CREDITO = 'Desarrollado por Caleb Luna · LunaIT Solution';
