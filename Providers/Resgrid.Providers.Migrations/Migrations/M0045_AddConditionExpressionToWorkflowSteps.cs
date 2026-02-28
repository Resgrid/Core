using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Adds the optional ConditionExpression column to WorkflowSteps.
	/// This stores a Scriban expression that is evaluated at runtime to determine
	/// whether the step should execute. A null or empty value means "always run".
	/// </summary>
	[Migration(45)]
	public class M0045_AddConditionExpressionToWorkflowSteps : Migration
	{
		public override void Up()
		{
			Alter.Table("WorkflowSteps")
				.AddColumn("ConditionExpression").AsString(4096).Nullable();
		}

		public override void Down()
		{
			Delete.Column("ConditionExpression").FromTable("WorkflowSteps");
		}
	}
}

