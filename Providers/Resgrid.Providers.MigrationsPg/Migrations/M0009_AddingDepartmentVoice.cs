using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(9)]
	public class M0009_AddingDepartmentVoice : Migration
	{
		public override void Up()
		{
			Create.Table("DepartmentVoices")
			   .WithColumn("DepartmentVoiceId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("StartConferenceNumber").AsInt32().NotNullable();

			Create.ForeignKey("FK_DepartmentVoices_Department")
				.FromTable("DepartmentVoices").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Table("DepartmentVoiceChannels")
			   .WithColumn("DepartmentVoiceChannelId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentVoiceId").AsCustom("citext").NotNullable()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext")
			   .WithColumn("SystemConferenceId").AsCustom("citext")
			   .WithColumn("SystemCallflowId").AsCustom("citext")
			   .WithColumn("ConferenceNumber").AsInt32()
			   .WithColumn("IsDefault").AsBoolean();

			Create.ForeignKey("FK_DepartmentVoiceChannels_Department")
				.FromTable("DepartmentVoiceChannels").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_DepartmentVoiceChannels_DepartmentVoices")
				.FromTable("DepartmentVoiceChannels").ForeignColumn("DepartmentVoiceId")
				.ToTable("DepartmentVoices").PrimaryColumn("DepartmentVoiceId");

			Create.Table("DepartmentVoiceUsers")
			   .WithColumn("DepartmentVoiceUserId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentVoiceId").AsCustom("citext").NotNullable()
			   .WithColumn("UserId").AsCustom("citext").NotNullable()
			   .WithColumn("SystemUserId").AsCustom("citext")
			   .WithColumn("SystemDeviceId").AsCustom("citext");

			Create.ForeignKey("FK_DepartmentVoiceUsers_DepartmentVoices")
				.FromTable("DepartmentVoiceUsers").ForeignColumn("DepartmentVoiceId")
				.ToTable("DepartmentVoices").PrimaryColumn("DepartmentVoiceId");

		}

		public override void Down()
		{

		}
	}
}
