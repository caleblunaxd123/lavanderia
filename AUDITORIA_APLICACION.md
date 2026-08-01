# Auditoría de la aplicación — Lavandería (LaviSystem)

> Documento vivo de seguimiento. Última actualización: 2026-08-01.
> Sin secretos, tokens ni datos personales.

## 1. Arquitectura detectada

- **Frontend**: Angular 19 (standalone components + signals). `frontend/`. Build prod: `npm run build` (Angular CLI). Dev: `ng serve` (puerto 4300).
- **Backend**: ASP.NET Core .NET 9, **ADO.NET puro** (SqlClient, sin ORM). `backend/src/Lavanderia.Api/`. Patrón repositorio + controllers `TenantAwareControllerBase`.
- **Base de datos**: SQL Server Express. Migraciones SQL versionadas en `backend/db/scripts/NNN_*.sql` (001…043).
- **Multi-tenant**: `Negocio` (tenant) → `Sede` (sucursal). Aislamiento por columna `NegocioId`/`SedeId` en cada tabla; los claims del JWT (`negocioId`, `sedeId`) alimentan `ITenantContext`. Roles: PROPIETARIO (dueño SaaS) vs ADMIN/COORDINADOR/TRABAJADOR.
- **Demo compartida**: `scripts/iniciar-demo-compartida.ps1` (aplica migraciones, compila Angular prod, publica API Release en :5004, túnel Cloudflare). CI: `.github/workflows/quality.yml` (tests xUnit).

### Cómo ejecutar
- Backend dev: `dotnet run` en `backend/src/Lavanderia.Api` (:5000).
- Frontend dev: `npm start --prefix frontend` (:4300, proxy a :5000).
- Tests: `dotnet test` en `backend/tests/Lavanderia.Api.Tests`.
- Demo pública: `Iniciar-Lavixa.bat`.

## 2. Inventario técnico

| Elemento | Cantidad |
|---|---|
| Controllers | 30 |
| Repositorios | 22 |
| Migraciones SQL | 44 (001→043) |
| Páginas frontend | 43 |
| Endpoints (aprox.) | 168 (83 GET · 43 POST · 21 PUT · 11 PATCH · 10 DELETE) |
| Proyecto de tests | `Lavanderia.Api.Tests` (~46 archivos, xUnit, en CI) |

Módulos funcionales principales: Autenticación/Sedes · Pedidos (registro, listado kanban, detalle, timeline, pagos, anulación, fotos, ruta/delivery) · Clientes (CRM, fidelización/puntos, fusionar duplicados) · Servicios y categorías · Inventario (insumos, movimientos, clases, presentación) · Caja (cuadre, gastos) · Promociones · Reportes (general, gerencial, consolidado, cuadres) · Ajustes (negocio/marca, personal, usuarios/permisos, áreas, motorizados, tipos de gasto, pagos online, facturación electrónica) · Plataforma (panel del propietario SaaS) · Seguimiento público + pago (anónimo por token).

## 3. Línea base (build + tests)

- ✅ **Frontend compila** (`npm run build`) sin errores. **Typecheck estricto** (`tsc --noEmit`) sin errores.
- ✅ **Backend compila** (`dotnet build -c Debug`) — 0 errores.
- ✅ **Tests unitarios**: `dotnet test` → **50 correctas / 0 con error** (294 ms). (Se ejecutaron tras liberar el lock de Visual Studio.)
- ✅ **Migraciones**: 040–043 aplicadas y verificadas en la BD de demo; el runner del demo las aplica en orden.

### Escaneo de antipatrones (0 hallazgos)
- Backend: sin `catch {}` vacíos, sin `async void`, sin bloqueos `.Result`/`.Wait()`, sin `await` dentro de `foreach` en repositorios (N+1). SQL 100% parametrizado.
- Frontend: sin `console.log`/`debugger` olvidados; los 2 `catch {}` existentes son guardas de `localStorage` con fallback (correcto).

## 4. Auditoría de seguridad (pasada de esta sesión)

Escaneo estático orientado a OWASP sobre los 22 repositorios:

| Vector | Resultado |
|---|---|
| **IDOR / aislamiento por tenant** | OK en el spot-check. Los repos reciben `negocioId`/`sedeId` y filtran (`WHERE … NegocioId=@… / SedeId=@…`). `Pedido.ObtenerPorIdAsync` → `WHERE p.Id=@Id AND p.SedeId=@SedeId`. Las pocas queries `WHERE Id=@Id` sueltas son: sub-consultas de enriquecimiento tras cargar un registro ya autorizado (motorizado, foto por PedidoId), operaciones del propietario sobre `Negocio` (rol PROPIETARIO), o Ids derivados en servidor (facturación, pagos por token). |
| **SQL injection** | Parametrizado en todo (SqlCommand + AddParam). El único SQL con interpolación (`RutaRepartoRepository` `SET {columna}`) usa un **whitelist** (switch con nombres fijos) → seguro. |
| **CSP / headers** | CSP estricta en `Program.cs` (`default-src 'self'`, `script-src 'self'`, etc.). |
| **Auth** | JWT con claims de tenant; endpoints con `[Authorize(Policy="Modulo:…")]`; refresh tokens con hash + revocación. |

**Endurecimiento pendiente (bajo/medio, no vuln confirmada)**: agregar filtro `NegocioId` defensivo en `FacturacionRepository.ActualizarResultadoAsync` (hoy el Id se deriva en servidor, pero un filtro extra sería defensa en profundidad).

## 5. Correcciones aplicadas en esta ronda de trabajo (commiteadas)

| Área | Corrección | Severidad |
|---|---|---|
| **Prod CSS** | El CSS global no se aplicaba en producción (`media=print` + `onload` bloqueado por CSP) → todo descuadrado. Se desactivó `inlineCritical`. | **Crítico** |
| Plantillas Excel | Generaban filas fantasma al reimportarse y Excel las rechazaba (comentarios VML). Corregido. | Alto |
| Clientes / catálogos | Anti-duplicados: por DNI, nombre+celular, nombre; insensible a tildes/mayúsculas; en alta manual y masiva; + índices únicos en BD (migr. 040/041). Fusionados duplicados reales; limpiados datos QA. | Alto |
| Combos | Solo muestran registros **activos** (filtro de categorías en Servicios). | Medio |
| Filtros | Búsqueda por nombre (no por campo agrupador que “inundaba”). | Medio |
| Facturación electrónica | Promovida a ítem de primer nivel del menú. | Bajo |
| Registro responsive | El formulario quedaba tapado por el menú lateral. | Alto |
| Inventario | 3 clases (equipo/material/insumo) + presentación “contenido por unidad” (bidón × N litros) + importación masiva. | Feature |
| Registrar pedido | Sección de fotos de evidencia; botón Registrar arriba (dinámico); inputs numéricos sin flechitas. | Feature/UX |
| Listados | Inactivos resaltados con acento rojo. Gráficas de barras prolijas. | UX |

## 6. Cobertura de revisión por módulo (matriz)

| Módulo | Revisión estática | Build | Aislamiento tenant | Prueba en vivo |
|---|---|---|---|---|
| Pedidos / Registrar | ✅ | ✅ | ✅ (SedeId) | Parcial (demo) |
| Clientes / CRM | ✅ | ✅ | ✅ (NegocioId) | Parcial |
| Servicios / Categorías | ✅ | ✅ | ✅ | Parcial |
| Inventario | ✅ | ✅ | ✅ (SedeId) | Pendiente demo |
| Caja / Cuadre | ✅ | ✅ | ✅ (SedeId) | Pendiente |
| Reportes | ✅ | ✅ | ✅ | Pendiente |
| Ajustes (varios) | ✅ | ✅ | ✅ | Parcial |
| Pagos / Seguimiento público | ✅ (por token) | ✅ | por token | Pendiente |
| Plataforma (propietario) | ✅ | ✅ | rol PROPIETARIO | Pendiente |

## 7. Limitaciones reales del entorno

- **Tests unitarios bloqueados** por lock de DLL de otra instancia (VS/IIS Express). Corren en CI.
- **Pruebas E2E exhaustivas** (los ~cientos de casos borde del checklist) no son ejecutables manualmente una por una en este entorno; se cubren build + escaneo estático + smoke test en el demo.
- **Integraciones externas** (Culqi/Izipay pasarela, APISUNAT facturación) requieren credenciales de terceros no disponibles; su esquema está listo.

## 8. Siguiente punto de continuación

1. Smoke test en el demo público de: inventario (campo presentación + 3 clases), registrar (botón arriba + fotos + cantidad manual), anti-duplicados en vivo.
2. Ejecutar suite de tests cuando se libere el lock (o vía CI).
3. (Opcional) filtro `NegocioId` defensivo en facturación.
