using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A3 (Protocols P5 / Training T5): two more Department Link share kinds per direction. DepartmentShareProtocols and
	/// LinkedDepartmentShareProtocols let the other side read the sharing side's published protocols and subscribe to its protocol sets;
	/// DepartmentShareTraining and LinkedDepartmentShareTraining let it subscribe to the sharing side's course sets and be sponsored
	/// over the link. Both default off. Placeholder number (renumbered at merge). Existence-guarded; Down removes exactly what Up added.
	/// </summary>
	[Migration(414)]
	public class M0414_AddDepartmentLinkContentShares : Migration
	{
		private static readonly string[] Columns =
		{
			"DepartmentShareProtocols", "DepartmentShareTraining", "LinkedDepartmentShareProtocols", "LinkedDepartmentShareTraining"
		};

		public override void Up()
		{
			foreach (var column in Columns)
				Execute.Sql($@"
IF OBJECT_ID('dbo.DepartmentLinks', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentLinks', '{column}') IS NULL
	ALTER TABLE [dbo].[DepartmentLinks] ADD [{column}] bit NOT NULL CONSTRAINT [DF_DepartmentLinks_{column}] DEFAULT (0);");
		}

		public override void Down()
		{
			foreach (var column in Columns)
				Execute.Sql($@"
IF OBJECT_ID('DF_DepartmentLinks_{column}', 'D') IS NOT NULL ALTER TABLE [dbo].[DepartmentLinks] DROP CONSTRAINT [DF_DepartmentLinks_{column}];
IF COL_LENGTH('dbo.DepartmentLinks', '{column}') IS NOT NULL ALTER TABLE [dbo].[DepartmentLinks] DROP COLUMN [{column}];");
		}
	}
}
