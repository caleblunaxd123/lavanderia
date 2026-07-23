# Matriz de pruebas manuales — Lavanderia / LaviSystem

Checklist para verificar el sistema antes de una entrega o de subir a producción.
Marca cada caso como ✅ (pasa), ❌ (falla → reportar) o ⏭️ (no aplica).

Lo que ya está cubierto por **pruebas automáticas** (no hace falta probarlo a mano) está anotado
como _[auto]_ — corre `dotnet test Lavanderia.sln` para ejecutarlas (50 tests).

---

## 1. Acceso y sesión

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 1.1 | Login con usuario/clave correctos | Entra al sistema | |
| 1.2 | Login con clave incorrecta | "Credenciales inválidas", no entra | |
| 1.3 | 10+ intentos fallidos seguidos | Bloquea con "Demasiados intentos" (rate limit) | |
| 1.4 | Admin con varias sedes | Pide elegir sede antes de entrar | |
| 1.5 | Dejar la app abierta >20 min y hacer una acción | Sigue funcionando (renueva token solo) | |
| 1.6 | Botón mostrar/ocultar contraseña | Alterna ver la clave | |

## 2. Registrar pedido

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 2.1 | Pedido normal (Tienda) con 1 servicio y pago total | Se crea, ticket imprime, estado PAGADO | |
| 2.2 | Pedido sin celular del cliente | Bloquea: "debe tener un celular" _[auto]_ | |
| 2.3 | Celular que no empieza en 9 o no tiene 9 dígitos | Bloquea con mensaje claro _[auto]_ | |
| 2.4 | Cantidad 0 o negativa en un ítem | Bloquea | |
| 2.5 | Fecha de entrega anterior al ingreso | Bloquea _[auto]_ | |
| 2.6 | Pago inicial mayor al total | Bloquea | |
| 2.7 | Descuento por encima del tope configurado | Bloquea: "máximo permitido es X%" | |
| 2.8 | Pedido Delivery sin marcar punto en el mapa | Bloquea: "confirma el punto exacto" _[auto]_ | |
| 2.9 | Pedido Recojo sin dirección | Bloquea _[auto]_ | |
| 2.10 | Canjear más puntos de los que tiene el cliente | Bloquea | |
| 2.11 | Llenar medio pedido y pulsar F5 / cerrar | Avisa "¿salir? se perderán los cambios" | |
| 2.12 | Doble clic rápido en "Registrar" | Crea un solo pedido, no dos | |
| 2.13 | Urgente con recargo | El total sube según el % de recargo | |

## 3. Flujo del pedido (estados)

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 3.1 | Avanzar pedido por todas las áreas hasta LISTO | Cada avance queda en el historial _[auto]_ | |
| 3.2 | Intentar entregar un pedido con saldo pendiente | Bloquea: "registra el pago antes de entregar" | |
| 3.3 | Entregar un pedido ya pagado | Pasa a ENTREGADO | |
| 3.4 | Intentar avanzar un pedido ENTREGADO/ANULADO/DONADO | Bloquea: "estado final" _[auto]_ | |
| 3.5 | Reenviar a almacén un pedido pendiente | Pasa directo a LISTO | |
| 3.6 | Dos personas avanzan el mismo pedido a la vez | Solo una gana; la otra ve "el pedido cambió, actualiza" | |
| 3.7 | Sede nueva recién creada | Ya trae las 6 áreas; el pedido puede avanzar | |
| 3.8 | Aviso automático de "listo" por WhatsApp al pasar a LISTO | Abre WhatsApp con el mensaje y el link | |

## 4. Pagos y anulación

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 4.1 | Pago parcial y luego el saldo | Estados PARCIAL → PAGADO | |
| 4.2 | Cobrar más del saldo pendiente | Bloquea | |
| 4.3 | Cobrar dos veces un pedido ya pagado | Bloquea: "ya está pagado por completo" | |
| 4.4 | Anular pedido con dinero cobrado | Bloquea: "gestiona la devolución antes" | |
| 4.5 | Anular pedido sin pagos (con motivo) | Se anula; los puntos se revierten | |
| 4.6 | Anular como usuario TRABAJADOR | Bloquea (solo ADMIN/COORDINADOR) | |
| 4.7 | Cada pago aparece en Cuadre de caja del día | El monto y método coinciden | |

## 5. Cierre de caja

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 5.1 | Efectivo contado = lo esperado | Banner verde "¡La caja cuadra!" | |
| 5.2 | Efectivo contado menor a lo esperado | "Falta S/ X" (rojo) | |
| 5.3 | Efectivo contado mayor a lo esperado | "Sobra S/ X" (amarillo) | |
| 5.4 | Verificar que Yape/Plin NO afecten el efectivo | El paso 1 solo suma cobros en efectivo | |
| 5.5 | "Total vendido hoy" (paso 2) | = efectivo + digital, coincide con los pagos | |
| 5.6 | Contar billete por billete | El total se calcula solo y coincide | |
| 5.7 | Registrar un gasto en efectivo | Baja "lo que debería haber en caja" | |
| 5.8 | Registrar un gasto por Yape | NO baja el efectivo, sí baja la ganancia | |
| 5.9 | Guardar cierre y generar PDF | Abre el PDF imprimible | |
| 5.10 | Volver a cerrar una caja ya cerrada | Pide confirmar antes de reemplazar | |

## 6. Clientes y CRM

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 6.1 | Crear, editar y desactivar cliente | Funciona; el histórico de pedidos se conserva | |
| 6.2 | Cliente con deuda aparece en CRM | Con el total adeudado | |
| 6.3 | Fusionar dos clientes duplicados | Se unifican; el origen queda desactivado | |

## 7. Delivery y seguimiento

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 7.1 | Convertir un pedido a Delivery | Genera link de seguimiento | |
| 7.2 | Convertir a Delivery un pedido ENTREGADO | Bloquea: "está finalizado" | |
| 7.3 | Abrir el link de seguimiento como cliente (sin sesión) | Ve el estado del pedido | |
| 7.4 | Asignar motorizado y abrir su link | El repartidor comparte GPS; el cliente ve el mapa | |
| 7.5 | Reprogramar la entrega desde el portal | Cambia la fecha | |

## 8. Multi-sede / plataforma (solo PROPIETARIO)

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 8.1 | Crear una empresa nueva (tenant) | Nace con sede, admin, áreas, tope de descuento 30% | |
| 8.2 | Datos de una empresa no se ven desde otra | Aislamiento por negocio | |
| 8.3 | Consolidado de sedes (dueño) | Suma correcta de todas las sedes | |
| 8.4 | Empresa con suscripción vencida | No puede operar; ve el aviso | |

## 9. Facturación electrónica (si está activa)

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 9.1 | Emitir boleta de un pedido | Genera XML firmado + PDF | |
| 9.2 | Ver comprobantes emitidos | Lista con estado SUNAT | |

## 10. Responsivo / dispositivos

| # | Caso | Resultado esperado | ✓ |
|---|------|--------------------|---|
| 10.1 | Usar el sistema en tablet (mostrador) | Todo legible y usable | |
| 10.2 | Usar en celular (dueño desde casa) | Menú y pantallas se adaptan | |

---

### Antes de subir a producción — verificaciones críticas

- [ ] Contraseña del admin cambiada (no dejar `admin123`)
- [ ] Clave JWT real configurada por variable de entorno (el sistema no arranca en Prod con la de ejemplo)
- [ ] Backup de la base de datos configurado (diario)
- [ ] HTTPS / candado activo en el dominio
- [ ] `dotnet test Lavanderia.sln` → 50/50 en verde
