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

-- Only stamp the migration once the schema is genuinely V5: either the rename above just produced the
-- V5 columns (this runs in the same transaction, so the metadata is already visible), or the database
-- was already V5-shaped. Requiring every V5 column here means a database that is neither pre-V5 (it has
-- no [Type] to rename) nor V5 (it is missing these columns) is NOT falsely stamped as migrated.
IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20240412153137_UpdateOpenIddictModelsToV5')
   AND COL_LENGTH(N'dbo.OpenIddictApplications', N'ClientType') IS NOT NULL
   AND COL_LENGTH(N'dbo.OpenIddictApplications', N'ApplicationType') IS NOT NULL
   AND COL_LENGTH(N'dbo.OpenIddictApplications', N'JsonWebKeySet') IS NOT NULL
   AND COL_LENGTH(N'dbo.OpenIddictApplications', N'Settings') IS NOT NULL
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20240412153137_UpdateOpenIddictModelsToV5', N'5.0.9');
END;

COMMIT;
