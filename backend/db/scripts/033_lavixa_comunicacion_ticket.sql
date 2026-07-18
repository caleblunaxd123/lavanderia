-- Datos y textos entregados por Lavixa para el ticket del cliente y WhatsApp.
DECLARE @Condiciones NVARCHAR(MAX) = N'Entrega de prendas: La devolución de sus prendas se realizará exclusivamente contra la presentación de la boleta de servicio, como garantía de seguridad para su propiedad.
Revisión previa de bolsillos: Recomendamos verificar el contenido de sus bolsillos antes de la entrega. Lavixa no asume responsabilidad por dinero, objetos de valor o dispositivos electrónicos olvidados, ni por los daños que estos pudieran ocasionar a la prenda.
Artículos no admitidos: Por razones de bioseguridad, no se admite el ingreso de ropa interior ni mascarillas. Las mascarillas detectadas serán descartadas de forma inmediata, sin derecho a reclamo; asimismo, no se asumirá responsabilidad por prendas íntimas remitidas de manera no declarada.
Plazo de observaciones: Toda observación respecto al estado de sus prendas deberá formularse antes de retirarse de nuestras instalaciones. Una vez concluida la atención, no se admitirán reclamos posteriores.
Prendas delicadas: Lavixa no garantiza resultados en prendas que, por su naturaleza, presenten fragilidad, desgaste significativo o defectos de confección.
Manchas persistentes: Nuestro servicio emplea técnicas especializadas de limpieza; sin embargo, no podemos garantizar la eliminación total de manchas crónicas o de difícil remoción.
Custodia y almacenamiento: Transcurridos 45 días calendario desde la fecha pactada de entrega, se aplicará un recargo del 20% en concepto de almacenamiento.
Prendas no reclamadas: Las prendas no retiradas dentro de los 90 días calendario se considerarán en situación de abandono, quedando Lavixa facultada para disponer de ellas mediante donación o remate, sin lugar a reclamo posterior.
Casos fortuitos o de fuerza mayor: Lavixa no será responsable por demoras originadas en circunstancias ajenas a su control, debidamente acreditadas.';

UPDATE c
   SET Direccion = N'Av. República de Israel N° 555 (Mz. Y1 Lt. 22), Urb. El Álamo, Comas',
       Telefono = N'912 863 488',
       HorarioAtencion = N'Lun a Sáb: 8:30 am - 7:30 pm
Dom: 8:30 am - 3:00 pm',
       MensajePieTicket = N'Gracias por confiar en Lavixa.',
       CondicionesServicio = @Condiciones
  FROM dbo.ConfiguracionNegocio c
  JOIN dbo.Negocio n ON n.Id = c.NegocioId
 WHERE LOWER(c.NombreNegocio) LIKE '%lavixa%' OR LOWER(n.Nombre) LIKE '%lavixa%';

DECLARE @PlantillaIngreso NVARCHAR(MAX) = N'¡Hola *{cliente}*!
Le saluda la lavandería *{negocio}*. Su orden es la *{numero}* con los siguientes ítems:

{items}

Monto total a pagar *S/{total}*, del cual falta pagar *S/{saldo}*.
Fecha de entrega: *{entrega}*.

Nuestro horario de atención es:
{horario}

{seguimiento}

*CONDICIONES DEL SERVICIO - {negocio}*
{condiciones}';

UPDATE p
   SET Mensaje = @PlantillaIngreso,
       Activa = 1
  FROM dbo.PlantillaWhatsapp p
  JOIN dbo.Negocio n ON n.Id = p.NegocioId
  LEFT JOIN dbo.ConfiguracionNegocio c ON c.NegocioId = n.Id
 WHERE p.Evento = 'INGRESO'
   AND (LOWER(c.NombreNegocio) LIKE '%lavixa%' OR LOWER(n.Nombre) LIKE '%lavixa%');
