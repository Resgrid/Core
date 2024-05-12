BEGIN TRANSACTION;

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20240412153137_UpdateOpenIddictModelsToV5')
BEGIN
   EXECUTE sp_rename N'dbo.OpenIddictApplications.Type', N'Tmp_ClientType', 'COLUMN' 
   EXECUTE sp_rename N'dbo.OpenIddictApplications.Tmp_ClientType', N'ClientType', 'COLUMN' 

ALTER TABLE dbo.OpenIddictApplications ADD
	ApplicationType nvarchar(MAX) NULL,
	JsonWebKeySet nvarchar(MAX) NULL,
	Settings nvarchar(MAX) NULL
END;

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20240412153137_UpdateOpenIddictModelsToV5')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20240412153137_UpdateOpenIddictModelsToV5', N'5.0.9');
END;

COMMIT;
