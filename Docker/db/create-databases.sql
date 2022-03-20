IF NOT EXISTS(SELECT * FROM sys.databases WHERE name = 'Resgrid')
    BEGIN
        CREATE DATABASE Resgrid;
    END
GO

IF NOT EXISTS(SELECT * FROM sys.databases WHERE name = 'ResgridWorkers')
    BEGIN
        CREATE DATABASE ResgridWorkers;
    END
GO

IF NOT EXISTS(SELECT * FROM sys.databases WHERE name = 'ResgridOIDC')
    BEGIN
        CREATE DATABASE ResgridOIDC;
    END
GO