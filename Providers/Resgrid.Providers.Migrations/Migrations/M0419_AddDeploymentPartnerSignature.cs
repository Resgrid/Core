using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A3 follow-up (e-Sign §20 "Deployments (Records)": the partner agency's acceptance of an assignment, L2 when the partner is a
	/// linked Resgrid department). RmsExternalOrders gets PartnerDepartmentLinkId and PartnerDepartmentId (int NULL: the linked department the
	/// order is filled for); RmsExternalOrderFills gets PartnerDepartmentId, PartnerSignStatus (int, 0), PartnerSignRequestId, PartnerSignRequestedOn,
	/// PartnerSignedOn and PartnerSignedByUserId, plus IX_RmsExternalOrderFills_PartnerSign (PartnerDepartmentId, PartnerSignStatus) for the
	/// partner's "awaiting your signature" list. Placeholder number (renumbered at merge). Existence-guarded; Down removes exactly what Up added.
	/// </summary>
	[Migration(419)]
	public class M0419_AddDeploymentPartnerSignature : Migration
	{
		private static readonly (string Table, string Name, string Type, string Default)[] Columns =
		{
			("RmsExternalOrders", "PartnerDepartmentLinkId", "int NULL", null),
			("RmsExternalOrders", "PartnerDepartmentId", "int NULL", null),
			("RmsExternalOrderFills", "PartnerDepartmentId", "int NULL", null),
			("RmsExternalOrderFills", "PartnerSignStatus", "int NOT NULL", "0"),
			("RmsExternalOrderFills", "PartnerSignRequestId", "int NULL", null),
			("RmsExternalOrderFills", "PartnerSignRequestedOn", "datetime2 NULL", null),
			("RmsExternalOrderFills", "PartnerSignedOn", "datetime2 NULL", null),
			("RmsExternalOrderFills", "PartnerSignedByUserId", "nvarchar(128) NULL", null)
		};

		public override void Up()
		{
			foreach (var (table, name, type, value) in Columns)
				Execute.Sql($@"
IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL AND COL_LENGTH('dbo.{table}', '{name}') IS NULL
	ALTER TABLE [dbo].[{table}] ADD [{name}] {type}{(value == null ? string.Empty : $" CONSTRAINT [DF_{table}_{name}] DEFAULT ({value})")};");
			Execute.Sql("IF OBJECT_ID('dbo.RmsExternalOrderFills', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RmsExternalOrderFills_PartnerSign' AND object_id = OBJECT_ID('dbo.RmsExternalOrderFills')) " +
				"CREATE INDEX [IX_RmsExternalOrderFills_PartnerSign] ON [dbo].[RmsExternalOrderFills] ([PartnerDepartmentId], [PartnerSignStatus]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RmsExternalOrderFills_PartnerSign' AND object_id = OBJECT_ID('dbo.RmsExternalOrderFills')) " +
				"DROP INDEX [IX_RmsExternalOrderFills_PartnerSign] ON [dbo].[RmsExternalOrderFills];");
			foreach (var (table, name, _, value) in Columns)
				Execute.Sql($@"
{(value == null ? string.Empty : $"IF OBJECT_ID('DF_{table}_{name}', 'D') IS NOT NULL ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [DF_{table}_{name}];")}
IF COL_LENGTH('dbo.{table}', '{name}') IS NOT NULL ALTER TABLE [dbo].[{table}] DROP COLUMN [{name}];");
		}
	}
}
