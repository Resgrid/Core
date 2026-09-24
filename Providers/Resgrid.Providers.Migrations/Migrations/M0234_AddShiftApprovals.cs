using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Supervisor approval and per-day roster edits for shifts. ShiftSignups gains ApprovalPending (a signup on a
	/// RequireApproval shift waits for a supervisor), AssignedByUserId (a supervisor placed the person on that day) and
	/// review audit columns. ShiftSignupTrades gains ApprovalPending (the requester picked an offer and a supervisor must
	/// approve the swap) and the same review audit columns. Existing rows default to not pending, which matches how they
	/// behaved before this migration.
	/// </summary>
	[Migration(234)]
	public class M0234_AddShiftApprovals : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ApprovalPending') IS NULL ALTER TABLE [ShiftSignups] ADD [ApprovalPending] bit NOT NULL CONSTRAINT [DF_ShiftSignups_ApprovalPending] DEFAULT(0);");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'AssignedByUserId') IS NULL ALTER TABLE [ShiftSignups] ADD [AssignedByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ReviewedByUserId') IS NULL ALTER TABLE [ShiftSignups] ADD [ReviewedByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ReviewedOn') IS NULL ALTER TABLE [ShiftSignups] ADD [ReviewedOn] datetime NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ReviewNote') IS NULL ALTER TABLE [ShiftSignups] ADD [ReviewNote] nvarchar(max) NULL;");

			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ApprovalPending') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [ApprovalPending] bit NOT NULL CONSTRAINT [DF_ShiftSignupTrades_ApprovalPending] DEFAULT(0);");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ReviewedByUserId') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [ReviewedByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ReviewedOn') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [ReviewedOn] datetime NULL;");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ReviewNote') IS NULL ALTER TABLE [ShiftSignupTrades] ADD [ReviewNote] nvarchar(max) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ReviewNote') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [ReviewNote];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ReviewedOn') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [ReviewedOn];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ReviewedByUserId') IS NOT NULL ALTER TABLE [ShiftSignupTrades] DROP COLUMN [ReviewedByUserId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignupTrades', 'ApprovalPending') IS NOT NULL BEGIN ALTER TABLE [ShiftSignupTrades] DROP CONSTRAINT [DF_ShiftSignupTrades_ApprovalPending]; ALTER TABLE [ShiftSignupTrades] DROP COLUMN [ApprovalPending]; END");

			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ReviewNote') IS NOT NULL ALTER TABLE [ShiftSignups] DROP COLUMN [ReviewNote];");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ReviewedOn') IS NOT NULL ALTER TABLE [ShiftSignups] DROP COLUMN [ReviewedOn];");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ReviewedByUserId') IS NOT NULL ALTER TABLE [ShiftSignups] DROP COLUMN [ReviewedByUserId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'AssignedByUserId') IS NOT NULL ALTER TABLE [ShiftSignups] DROP COLUMN [AssignedByUserId];");
			Execute.Sql("IF COL_LENGTH('ShiftSignups', 'ApprovalPending') IS NOT NULL BEGIN ALTER TABLE [ShiftSignups] DROP CONSTRAINT [DF_ShiftSignups_ApprovalPending]; ALTER TABLE [ShiftSignups] DROP COLUMN [ApprovalPending]; END");
		}
	}
}
