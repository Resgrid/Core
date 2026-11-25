using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P3 step-through: CallProtocols.StepLogJson (a Procedure or Dispatch protocol run on a call like a checklist, each step
	/// timestamped) and StepLogRevision (bumped on every step-log write, so two people stepping through at once never overwrite each
	/// other). Existing rows have no log. Guarded for safe retry.
	/// </summary>
	[Migration(379)]
	public class M0379_AddCallProtocolStepLog : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'StepLogJson') IS NULL ALTER TABLE [dbo].[CallProtocols] ADD [StepLogJson] nvarchar(max) NULL;");
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'StepLogRevision') IS NULL ALTER TABLE [dbo].[CallProtocols] ADD [StepLogRevision] int NOT NULL CONSTRAINT [DF_CallProtocols_StepLogRevision] DEFAULT (0);");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_CallProtocols_StepLogRevision' AND parent_object_id = OBJECT_ID('dbo.CallProtocols'))
	ALTER TABLE [dbo].[CallProtocols] DROP CONSTRAINT [DF_CallProtocols_StepLogRevision];
IF COL_LENGTH('dbo.CallProtocols', 'StepLogRevision') IS NOT NULL ALTER TABLE [dbo].[CallProtocols] DROP COLUMN [StepLogRevision];
IF COL_LENGTH('dbo.CallProtocols', 'StepLogJson') IS NOT NULL ALTER TABLE [dbo].[CallProtocols] DROP COLUMN [StepLogJson];");
		}
	}
}
