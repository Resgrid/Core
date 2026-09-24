using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protected Workflows for EHR integration. Calls.SubjectIdentifiers holds external subject and record identifiers
	/// as a JSON object (protected: ADP catalog 29, calls.subjectidentifiers); Calls.Part2ConsentOnFile is the structural
	/// 42 CFR Part 2 consent flag. UdfFields.Sensitivity tags a custom field none (0), restricted (1) or Part 2 (2) for
	/// release. WorkflowCredentials.PublicJwks carries the PUBLIC keys of a private_key_jwt credential (its JWKS).
	/// Every column is nullable or has a constant default, so the adds are metadata-only.
	/// </summary>
	[Migration(233)]
	public class M0233_AddProtectedWorkflowsEhr : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF COL_LENGTH('Calls', 'SubjectIdentifiers') IS NULL
	ALTER TABLE [Calls] ADD [SubjectIdentifiers] nvarchar(max) NULL;
IF COL_LENGTH('Calls', 'Part2ConsentOnFile') IS NULL
	ALTER TABLE [Calls] ADD [Part2ConsentOnFile] bit NOT NULL CONSTRAINT [DF_Calls_Part2ConsentOnFile] DEFAULT (0);
IF COL_LENGTH('UdfFields', 'Sensitivity') IS NULL
	ALTER TABLE [UdfFields] ADD [Sensitivity] int NOT NULL CONSTRAINT [DF_UdfFields_Sensitivity] DEFAULT (0);
IF COL_LENGTH('WorkflowCredentials', 'PublicJwks') IS NULL
	ALTER TABLE [WorkflowCredentials] ADD [PublicJwks] nvarchar(max) NULL;");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF COL_LENGTH('WorkflowCredentials', 'PublicJwks') IS NOT NULL
	ALTER TABLE [WorkflowCredentials] DROP COLUMN [PublicJwks];
IF COL_LENGTH('UdfFields', 'Sensitivity') IS NOT NULL
BEGIN
	ALTER TABLE [UdfFields] DROP CONSTRAINT [DF_UdfFields_Sensitivity];
	ALTER TABLE [UdfFields] DROP COLUMN [Sensitivity];
END
IF COL_LENGTH('Calls', 'Part2ConsentOnFile') IS NOT NULL
BEGIN
	ALTER TABLE [Calls] DROP CONSTRAINT [DF_Calls_Part2ConsentOnFile];
	ALTER TABLE [Calls] DROP COLUMN [Part2ConsentOnFile];
END
IF COL_LENGTH('Calls', 'SubjectIdentifiers') IS NOT NULL
	ALTER TABLE [Calls] DROP COLUMN [SubjectIdentifiers];");
		}
	}
}
