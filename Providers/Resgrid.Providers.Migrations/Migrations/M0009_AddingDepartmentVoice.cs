using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(9)]
	public class M0009_AddingDepartmentVoice : Migration
	{
		public override void Up()
		{
			Create.Table("DepartmentVoices")
			   .WithColumn("DepartmentVoiceId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("StartConferenceNumber").AsInt32().NotNullable();

			Create.ForeignKey("FK_DepartmentVoices_Department")
				.FromTable("DepartmentVoices").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Table("DepartmentVoiceChannels")
			   .WithColumn("DepartmentVoiceChannelId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentVoiceId").AsString(128).NotNullable()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsString(512)
			   .WithColumn("SystemConferenceId").AsString(512)
			   .WithColumn("SystemCallflowId").AsString(512)
			   .WithColumn("ConferenceNumber").AsInt32()
			   .WithColumn("IsDefault").AsBoolean();

			Create.ForeignKey("FK_DepartmentVoiceChannels_Department")
				.FromTable("DepartmentVoiceChannels").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_DepartmentVoiceChannels_DepartmentVoices")
				.FromTable("DepartmentVoiceChannels").ForeignColumn("DepartmentVoiceId")
				.ToTable("DepartmentVoices").PrimaryColumn("DepartmentVoiceId");

			Create.Table("DepartmentVoiceUsers")
			   .WithColumn("DepartmentVoiceUserId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentVoiceId").AsString(128).NotNullable()
			   .WithColumn("UserId").AsString(128).NotNullable()
			   .WithColumn("SystemUserId").AsString(512)
			   .WithColumn("SystemDeviceId").AsString(512);

			Create.ForeignKey("FK_DepartmentVoiceUsers_DepartmentVoices")
				.FromTable("DepartmentVoiceUsers").ForeignColumn("DepartmentVoiceId")
				.ToTable("DepartmentVoices").PrimaryColumn("DepartmentVoiceId");

		}

		public override void Down()
		{

		}
	}
}
