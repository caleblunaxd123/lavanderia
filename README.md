# Lavanderia - Sistema de gestion

Sistema web para lavanderias en Peru. Cubre operacion diaria, caja, clientes,
promociones, reportes, inventario, configuracion multiempresa/multisede y
facturacion electronica SUNAT.

## Estructura

```text
Lavanderia/
|-- backend/
|   |-- src/Lavanderia.Api/       # Web API .NET 9 (ADO.NET puro, no ORM)
|   `-- db/scripts/               # Scripts SQL Server incrementales
|-- frontend/                     # Angular 20 standalone
`-- Lavanderia.sln
```

## Stack

- Backend: .NET 9 Web API, ADO.NET (`Microsoft.Data.SqlClient`), SQL Server 2019+, JWT Bearer, BCrypt.
- Frontend: Angular 20 standalone, Signals, lazy loading, interceptor JWT, guards por modulo.
- PDF/QR: QuestPDF y QRCoder para tickets/comprobantes.
- Facturacion electronica: generacion UBL, firma XML y envio SUNAT directo.
- SaaS: `Negocio` por empresa, `Sede` por sucursal y rutas publicas tipo `/:empresaSlug/login`.

## Arranque local

### 1. Base de datos

Requiere SQL Server local o remoto. Ejecuta en orden los scripts de `backend/db/scripts`.

```powershell
Get-ChildItem backend\db\scripts\*.sql | Sort-Object Name | ForEach-Object {
  sqlcmd -S localhost -E -i $_.FullName
}
```

Esto crea la BD `Lavanderia` con tablas base, roles, areas de lavado, catalogo,
inventario, multiempresa/multisede y facturacion electronica.

### 2. Backend

Configura la cadena de conexion y clave JWT en `backend/src/Lavanderia.Api/appsettings.json`
o, mejor para desarrollo, en `appsettings.Development.json`.

```powershell
cd backend/src/Lavanderia.Api
dotnet run
```

Al primer arranque, si la BD no tiene usuarios activos, crea el admin desde
`SeedAdmin` (por defecto `admin` / `admin123`). Cambia esa clave en produccion.

### 3. Frontend

```powershell
cd frontend
npm install
npm start
```

Abre `http://localhost:4200`. Para modo SaaS usa una ruta con slug, por ejemplo
`http://localhost:4200/lavixa/login`.

## Modulos principales

- Login, sesion JWT y seleccion de sede.
- Pedidos: registro, pagos, delivery, seguimiento publico, repartidor, tablero Kanban, avance de area y anulacion.
- Clientes: busqueda, puntos, frecuentes y fusion.
- Caja: efectivo, billeteras, transferencias, POS, gastos y cuadre por usuario.
- Inventario: tarjetas de stock, busqueda, movimientos, compras y alertas de reposicion.
- Reportes: ventas, pedidos, clientes, servicios y productividad.
- Ajustes: negocio, sedes, usuarios, permisos, servicios, categorias, areas, tipos de gasto, puntos y plantillas.
- Facturacion electronica: configuracion fiscal, certificado, emision y comprobantes.

## Modelo SaaS

Cada empresa de lavanderia se modela como `Negocio` y puede tener una o varias `Sede`.

- La URL publica usa `/:empresaSlug/login`.
- El login valida que el usuario pertenezca al negocio de la URL.
- Los usuarios quedan vinculados a `NegocioId` y opcionalmente a una `SedeId`.
- Un admin con `SedeId = NULL` puede elegir sede activa despues del login.
- Catalogos y clientes viven por negocio.
- Pedidos, caja, inventario y personal operan por sede.

## Responsive

El sistema esta adaptado a movil:

- Header con hamburguesa y drawer lateral en pantallas pequenas.
- Tablas principales convertidas en cards apiladas en movil.
- Wizard de registro adaptable de 3 columnas a 2 y luego a 1.
- Botones e inputs con tamanos tactiles en login y seleccion de sede.
- Tablero Kanban con desplazamiento contenido y centro de atencion operativa adaptable.

## Seguridad y aislamiento

- Los calculos de total, descuento y estado de pago se hacen en el servidor.
- Los repositorios filtran por `NegocioId` o `SedeId` segun el modulo.
- El cambio de sede respeta la sede fija del usuario.
- Las credenciales SUNAT se protegen con Data Protection.
- `appsettings.Development.json` queda fuera de Git para configuracion local sensible.

## Endpoints utiles

- `POST /api/auth/login`
- `POST /api/auth/seleccionar-sede`
- `GET /health`
- `GET /api/configuracion/publico/{slug}`
- `GET /api/pedidos`, `POST /api/pedidos`, `POST /api/pedidos/{id}/avanzar`
- `GET /api/insumos`, `POST /api/insumos/{id}/movimientos`
- `GET /api/facturacion-electronica/*`

## Verificacion

```powershell
dotnet build Lavanderia.sln
cd frontend
npm run build
npm test -- --watch=false
```
