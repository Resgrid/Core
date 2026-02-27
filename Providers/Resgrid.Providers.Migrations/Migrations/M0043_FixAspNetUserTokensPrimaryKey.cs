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
					WHERE  TABLE_NAME  = 'AspNetUserTokens'
					AND    COLUMN_NAME = 'LoginProvider'
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
					WHERE  TABLE_NAME  = 'AspNetUserTokens'
					AND    COLUMN_NAME = 'Name'
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
					WHERE  TABLE_NAME  = 'AspNetUserTokens'
					AND    COLUMN_NAME = 'UserId'
					AND    CHARACTER_MAXIMUM_LENGTH = -1
				)
				BEGIN
					ALTER TABLE [dbo].[AspNetUserTokens]
						ALTER COLUMN [UserId] NVARCHAR(450) NOT NULL;
				END

				-- Step 4: Drop whatever PK currently exists if it is not already the correct composite one
				IF EXISTS (
					SELECT 1
					FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
					WHERE  tc.TABLE_NAME      = 'AspNetUserTokens'
					AND    tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
					AND    NOT EXISTS (
					           SELECT 1
					           FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
					           WHERE  kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
					           AND    kcu.TABLE_NAME      = tc.TABLE_NAME
					           AND    kcu.COLUMN_NAME     = 'LoginProvider'
					       )
				)
				BEGIN
					DECLARE @pkName NVARCHAR(256);
					SELECT TOP 1 @pkName = CONSTRAINT_NAME
					FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS
					WHERE  TABLE_NAME     = 'AspNetUserTokens'
					AND    CONSTRAINT_TYPE = 'PRIMARY KEY';

					EXEC ('ALTER TABLE [dbo].[AspNetUserTokens] DROP CONSTRAINT [' + @pkName + ']');

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

