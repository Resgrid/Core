using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(21)]
	public class M0021_AddingCallReferences : Migration
	{
		public override void Up()
		{
			Create.Table("CallReferences")
			   .WithColumn("CallReferenceId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("SourceCallId").AsInt32().NotNullable()
			   .WithColumn("TargetCallId").AsInt32().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsCustom("citext").NotNullable()
			   .WithColumn("Note").AsCustom("citext").Nullable();

			Create.ForeignKey("FK_CallReferences_Call_Source")
				.FromTable("CallReferences").ForeignColumn("SourceCallId")
				.ToTable("Calls").PrimaryColumn("CallId");

			Create.ForeignKey("FK_CallReferences_Call_Target")
				.FromTable("CallReferences").ForeignColumn("TargetCallId")
				.ToTable("Calls").PrimaryColumn("CallId");
		}

		public override void Down()
		{

		}
	}
}
