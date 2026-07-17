-- ============================================================
-- 026: CRM basico — fecha de nacimiento para campanas de cumpleanos
-- ============================================================
USE Lavanderia;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Cliente') AND name = 'FechaNacimiento'
)
    ALTER TABLE dbo.Cliente ADD FechaNacimiento DATE NULL;
GO
