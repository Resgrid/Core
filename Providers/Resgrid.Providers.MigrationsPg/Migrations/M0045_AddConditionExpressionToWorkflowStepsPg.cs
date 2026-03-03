using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Adds the optional ConditionExpression column to workflowsteps (PostgreSQL).
	/// This stores a Scriban expression that is evaluated at runtime to determine
	/// whether the step should execute. A null or empty value means "always run".
	/// </summary>
	[Migration(45)]
	public class M0045_AddConditionExpressionToWorkflowStepsPg : Migration
	{
		public override void Up()
		{
			Alter.Table("WorkflowSteps".ToLower())
				.AddColumn("ConditionExpression".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			Delete.Column("ConditionExpression".ToLower()).FromTable("WorkflowSteps".ToLower());
		}
	}
}

