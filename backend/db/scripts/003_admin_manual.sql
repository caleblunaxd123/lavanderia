-- ============================================================
-- Inserta manualmente el usuario admin
-- Usar si el seeder automatico no funciono al primer arranque.
-- Credenciales: admin / admin123
-- ============================================================
USE Lavanderia;
GO

DECLARE @RolAdminId INT = (SELECT Id FROM dbo.Rol WHERE Codigo = 'ADMIN');

IF @RolAdminId IS NULL
BEGIN
    PRINT 'ERROR: No existe el rol ADMIN. Ejecuta 002_seed.sql primero.';
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.Usuario WHERE Usuario = 'admin')
BEGIN
    -- Si ya existe pero no se puede loguear, actualizamos el hash
    UPDATE dbo.Usuario
       SET PasswordHash = '$2a$11$6VvzeOcZSNngDtEtGBCR/uQeEs.Ry5OrW4sc9hhjsgid9Pj0FK9si',
           Activo = 1
     WHERE Usuario = 'admin';
    PRINT 'Usuario admin ya existia. Password reseteado a admin123.';
END
ELSE
BEGIN
    INSERT INTO dbo.Usuario (Usuario, NombreCompleto, Email, PasswordHash, RolId, Activo)
    VALUES (
        'admin',
        N'Administrador',
        NULL,
        '$2a$11$6VvzeOcZSNngDtEtGBCR/uQeEs.Ry5OrW4sc9hhjsgid9Pj0FK9si',  -- BCrypt de admin123
        @RolAdminId,
        1
    );
    PRINT 'Usuario admin creado. Password: admin123';
END
GO

SELECT Id, Usuario, NombreCompleto, RolId, Activo FROM dbo.Usuario WHERE Usuario = 'admin';
GO
