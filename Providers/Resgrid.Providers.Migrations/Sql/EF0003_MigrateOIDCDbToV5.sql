BEGIN TRANSACTION;

-- Only rename Type -> ClientType when the pre-V5 [Type] column is actually present; a database
-- provisioned by EF EnsureCreated is already V5-shaped (ClientType, ApplicationType, JsonWebKeySet,
-- Settings) and just needs the history row stamped below.
IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20240412153137_UpdateOpenIddictModelsToV5')
   AND COL_LENGTH(N'dbo.OpenIddictApplications', N'Type') IS NOT NULL
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
