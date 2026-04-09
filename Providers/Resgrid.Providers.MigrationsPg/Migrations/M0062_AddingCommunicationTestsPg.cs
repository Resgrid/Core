using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(62)]
	public class M0062_AddingCommunicationTestsPg : Migration
	{
		public override void Up()
		{
			Create.Table("communicationtests")
				.WithColumn("communicationtestid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("name").AsCustom("citext").NotNullable()
				.WithColumn("description").AsCustom("text").Nullable()
				.WithColumn("scheduletype").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("sunday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("monday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("tuesday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("wednesday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("thursday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("friday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("saturday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("dayofmonth").AsInt32().Nullable()
				.WithColumn("time").AsCustom("citext").Nullable()
				.WithColumn("testsms").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("testemail").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("testvoice").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("testpush").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("active").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
				.WithColumn("createdon").AsDateTime().NotNullable()
				.WithColumn("updatedon").AsDateTime().Nullable()
				.WithColumn("responsewindowminutes").AsInt32().NotNullable().WithDefaultValue(60);

			Create.ForeignKey("fk_communicationtests_departments")
				.FromTable("communicationtests").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_communicationtests_departmentid")
				.OnTable("communicationtests")
				.OnColumn("departmentid");

			Create.Table("communicationtestruns")
				.WithColumn("communicationtestrunid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("communicationtestid").AsCustom("citext").NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("initiatedbyuserid").AsCustom("citext").Nullable()
				.WithColumn("startedon").AsDateTime().NotNullable()
				.WithColumn("completedon").AsDateTime().Nullable()
				.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("runcode").AsCustom("citext").NotNullable()
				.WithColumn("totaluserstested").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("totalresponses").AsInt32().NotNullable().WithDefaultValue(0);

			Create.ForeignKey("fk_communicationtestruns_communicationtests")
				.FromTable("communicationtestruns").ForeignColumn("communicationtestid")
				.ToTable("communicationtests").PrimaryColumn("communicationtestid");

			Create.Index("ix_communicationtestruns_communicationtestid")
				.OnTable("communicationtestruns")
				.OnColumn("communicationtestid");

			Create.Index("ix_communicationtestruns_departmentid")
				.OnTable("communicationtestruns")
				.OnColumn("departmentid");

			Create.Index("ix_communicationtestruns_runcode")
				.OnTable("communicationtestruns")
				.OnColumn("runcode")
				.Unique();

			Create.Table("communicationtestresults")
				.WithColumn("communicationtestresultid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("communicationtestrunid").AsCustom("citext").NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("userid").AsCustom("citext").NotNullable()
				.WithColumn("channel").AsInt32().NotNullable()
				.WithColumn("contactvalue").AsCustom("citext").Nullable()
				.WithColumn("contactcarrier").AsCustom("citext").Nullable()
				.WithColumn("verificationstatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("sendattempted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("sendsucceeded").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("senton").AsDateTime().Nullable()
				.WithColumn("responded").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("respondedon").AsDateTime().Nullable()
				.WithColumn("responsetoken").AsCustom("citext").Nullable();

			Create.ForeignKey("fk_communicationtestresults_communicationtestruns")
				.FromTable("communicationtestresults").ForeignColumn("communicationtestrunid")
				.ToTable("communicationtestruns").PrimaryColumn("communicationtestrunid");

			Create.Index("ix_communicationtestresults_communicationtestrunid")
				.OnTable("communicationtestresults")
				.OnColumn("communicationtestrunid");

			Create.Index("ix_communicationtestresults_departmentid")
				.OnTable("communicationtestresults")
				.OnColumn("departmentid");

			Create.Index("ix_communicationtestresults_responsetoken")
				.OnTable("communicationtestresults")
				.OnColumn("responsetoken");
		}

		public override void Down()
		{
			Delete.Table("communicationtestresults");
			Delete.Table("communicationtestruns");
			Delete.Table("communicationtests");
		}
	}
}
