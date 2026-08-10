-- 046: Identidad de marca LaviSystem como estilo por defecto de TODA la app.
-- El área de trabajo de cada negocio se pinta con SUS colores guardados (ColorPrimario/
-- Secundario/Acento). Hasta ahora todos tenían el default genérico viejo (#0b57d0 azul
-- Google / #29b6f6 / #f5a623 naranja), por eso el workspace no seguía la marca LaviSystem
-- del login/plataforma. Esto los alinea a la paleta oficial LaviSystem:
--   Principal 01 #053465 (navy) · Secundario #06B0BD (cian) · Principal 02 #046086 (azul medio).
-- Solo actualiza los que aún tienen el default viejo, para NO pisar a un negocio que a
-- futuro personalice su propia marca desde Ajustes → Negocio.
SET QUOTED_IDENTIFIER ON;
GO

UPDATE dbo.ConfiguracionNegocio
   SET ColorPrimario   = '#053465',
       ColorSecundario = '#06B0BD',
       ColorAcento     = '#046086'
 WHERE ColorPrimario = '#0b57d0'
   AND ColorSecundario = '#29b6f6';
GO

PRINT 'OK 046: negocios con default viejo actualizados a la paleta LaviSystem.';
GO
