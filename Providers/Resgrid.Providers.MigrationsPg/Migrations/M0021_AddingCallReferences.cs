using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(21)]
	public class M0021_AddingCallReferences : Migration
	{
		public override void Up()
		{
			Create.Table("CallReferences".ToLower())
			   .WithColumn("CallReferenceId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("SourceCallId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("TargetCallId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Note".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_CallReferences_Call_Source")
				.FromTable("CallReferences".ToLower()).ForeignColumn("SourceCallId".ToLower())
				.ToTable("Calls".ToLower()).PrimaryColumn("CallId".ToLower());

			Create.ForeignKey("FK_CallReferences_Call_Target")
				.FromTable("CallReferences".ToLower()).ForeignColumn("TargetCallId".ToLower())
				.ToTable("Calls".ToLower()).PrimaryColumn("CallId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
