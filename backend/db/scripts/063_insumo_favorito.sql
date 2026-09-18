-- ================================================================
-- 063_insumo_favorito.sql
-- Marca de "favorito" por insumo (compartida por sede): los insumos
-- más usados (detergentes, etc.) se marcan con estrella para verlos
-- de una al entrar a Inventario (suben al tope y hay filtro "solo favoritos").
-- Idempotente.
-- ================================================================
IF COL_LENGTH('dbo.Insumo', 'Favorito') IS NULL
    ALTER TABLE dbo.Insumo ADD Favorito BIT NOT NULL CONSTRAINT DF_Insumo_Favorito DEFAULT 0;
GO
