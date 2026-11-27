using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A3 (e-Sign S4, plan §20 Department Links row): the partner department's countersignature of a mutual-aid agreement, routed
	/// over the Department Link. MutualAidAgreements gains RequiresCountersignature, CountersignStatus, CountersignRequestId (the request in
	/// the partner department), CountersignRequestedOn, CountersignedOn, CountersignedByUserId and CountersignNote. Placeholder number
	/// (renumbered at merge). Existence-guarded; Down removes exactly what Up added.
	/// </summary>
	[Migration(417)]
	public class M0417_AddMutualAidCountersignature : Migration
	{
		private static readonly (string Name, string Type, string Default)[] Columns =
		{
			("RequiresCountersignature", "bit NOT NULL", "0"),
			("CountersignStatus", "int NOT NULL", "0"),
			("CountersignRequestId", "int NULL", null),
			("CountersignRequestedOn", "datetime2 NULL", null),
			("CountersignedOn", "datetime2 NULL", null),
			("CountersignedByUserId", "nvarchar(128) NULL", null),
			("CountersignNote", "nvarchar(1000) NULL", null)
		};

		public override void Up()
		{
			foreach (var (name, type, value) in Columns)
				Execute.Sql($@"
IF OBJECT_ID('dbo.MutualAidAgreements', 'U') IS NOT NULL AND COL_LENGTH('dbo.MutualAidAgreements', '{name}') IS NULL
	ALTER TABLE [dbo].[MutualAidAgreements] ADD [{name}] {type}{(value == null ? string.Empty : $" CONSTRAINT [DF_MutualAidAgreements_{name}] DEFAULT ({value})")};");
			Execute.Sql("IF OBJECT_ID('dbo.MutualAidAgreements', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MutualAidAgreements_Countersign' AND object_id = OBJECT_ID('dbo.MutualAidAgreements')) " +
				"CREATE INDEX [IX_MutualAidAgreements_Countersign] ON [dbo].[MutualAidAgreements] ([PartnerDepartmentId], [CountersignStatus]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MutualAidAgreements_Countersign' AND object_id = OBJECT_ID('dbo.MutualAidAgreements')) " +
				"DROP INDEX [IX_MutualAidAgreements_Countersign] ON [dbo].[MutualAidAgreements];");
			foreach (var (name, _, value) in Columns)
				Execute.Sql($@"
{(value == null ? string.Empty : $"IF OBJECT_ID('DF_MutualAidAgreements_{name}', 'D') IS NOT NULL ALTER TABLE [dbo].[MutualAidAgreements] DROP CONSTRAINT [DF_MutualAidAgreements_{name}];")}
IF COL_LENGTH('dbo.MutualAidAgreements', '{name}') IS NOT NULL ALTER TABLE [dbo].[MutualAidAgreements] DROP COLUMN [{name}];");
		}
	}
}
