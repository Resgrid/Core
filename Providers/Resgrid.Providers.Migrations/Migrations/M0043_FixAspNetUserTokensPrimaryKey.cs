using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Fixes the AspNetUserTokens primary key.
	/// The table was previously created with UserId as the sole PK column, which causes a
	/// duplicate-key violation when a second token (e.g. RecoveryCodes) is inserted for the
	/// same user alongside an existing AuthenticatorKey row.
	/// The correct PK is the composite (UserId, LoginProvider, Name) per ASP.NET Core Identity.
	/// </summary>
	[Migration(43)]
	public class M0043_FixAspNetUserTokensPrimaryKey : Migration
	{
		public override void Up()
		{
			// The existing table may have LoginProvider / Name columns typed as nvarchar(MAX)
			// (created by the old EF6 identity schema).  SQL Server cannot use MAX-length columns
			// as PK / index key columns, so we must resize them first.
			// After resizing we drop whatever single-column PK exists and replace it with the
			// correct composite PK (UserId, LoginProvider, Name).
			Execute.Sql(@"
				-- Step 1: Resize LoginProvider to nvarchar(450) if it is currently MAX
				IF EXISTS (
					SELECT 1
					FROM   INFORMATION_SCHEMA.COLUMNS
					WHERE  TABLE_SCHEMA = 'dbo'
					AND    TABLE_NAME   = 'AspNetUserTokens'
					AND    COLUMN_NAME  = 'LoginProvider'
					AND    CHARACTER_MAXIMUM_LENGTH = -1
				)
				BEGIN
					ALTER TABLE [dbo].[AspNetUserTokens]
						ALTER COLUMN [LoginProvider] NVARCHAR(450) NOT NULL;
				END

				-- Step 2: Resize Name to nvarchar(450) if it is currently MAX
				IF EXISTS (
					SELECT 1
					FROM   INFORMATION_SCHEMA.COLUMNS
					WHERE  TABLE_SCHEMA = 'dbo'
					AND    TABLE_NAME   = 'AspNetUserTokens'
					AND    COLUMN_NAME  = 'Name'
					AND    CHARACTER_MAXIMUM_LENGTH = -1
				)
				BEGIN
					ALTER TABLE [dbo].[AspNetUserTokens]
						ALTER COLUMN [Name] NVARCHAR(450) NOT NULL;
				END

				-- Step 3: Resize UserId to nvarchar(450) if it is currently MAX
				IF EXISTS (
					SELECT 1
					FROM   INFORMATION_SCHEMA.COLUMNS
					WHERE  TABLE_SCHEMA = 'dbo'
					AND    TABLE_NAME   = 'AspNetUserTokens'
					AND    COLUMN_NAME  = 'UserId'
					AND    CHARACTER_MAXIMUM_LENGTH = -1
				)
				BEGIN
					ALTER TABLE [dbo].[AspNetUserTokens]
						ALTER COLUMN [UserId] NVARCHAR(450) NOT NULL;
				END

				-- Step 4: Determine whether the current PK exactly matches the desired composite
				--         (UserId, LoginProvider, Name) in that order with no extra columns.
				--         We need to recreate the PK if:
				--           (a) no PRIMARY KEY exists at all, or
				--           (b) the existing PK column set / order does not exactly equal the desired set.
				DECLARE @pkName NVARCHAR(256) = NULL;

				SELECT TOP 1 @pkName = tc.CONSTRAINT_NAME
				FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
				WHERE  tc.TABLE_SCHEMA    = 'dbo'
				AND    tc.TABLE_NAME      = 'AspNetUserTokens'
				AND    tc.CONSTRAINT_TYPE = 'PRIMARY KEY';

				-- Check whether the existing PK (if any) exactly matches the desired composite.
				-- We expect exactly 3 columns: UserId (ord 1), LoginProvider (ord 2), Name (ord 3).
				DECLARE @pkIsCorrect BIT = 0;

				IF @pkName IS NOT NULL
				BEGIN
					SELECT @pkIsCorrect = CASE
						WHEN COUNT(*) = 3
						 AND MAX(CASE WHEN COLUMN_NAME = 'UserId'        AND ORDINAL_POSITION = 1 THEN 1 ELSE 0 END) = 1
						 AND MAX(CASE WHEN COLUMN_NAME = 'LoginProvider' AND ORDINAL_POSITION = 2 THEN 1 ELSE 0 END) = 1
						 AND MAX(CASE WHEN COLUMN_NAME = 'Name'          AND ORDINAL_POSITION = 3 THEN 1 ELSE 0 END) = 1
						THEN 1 ELSE 0 END
					FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
					WHERE  TABLE_SCHEMA     = 'dbo'
					AND    TABLE_NAME       = 'AspNetUserTokens'
					AND    CONSTRAINT_NAME  = @pkName;
				END

				-- If no PK exists, or the existing PK is not the correct composite, rebuild it.
				IF @pkIsCorrect = 0
				BEGIN
					-- Drop the existing PK only if one was found.
					IF @pkName IS NOT NULL
					BEGIN
						EXEC ('ALTER TABLE [dbo].[AspNetUserTokens] DROP CONSTRAINT [' + @pkName + ']');
					END

					ALTER TABLE [dbo].[AspNetUserTokens]
						ADD CONSTRAINT [PK_AspNetUserTokens]
						PRIMARY KEY CLUSTERED ([UserId], [LoginProvider], [Name]);
				END
			");
		}

		public override void Down()
		{
			// Reverting to a single-column PK would corrupt data; intentionally a no-op.
		}
	}
}

