using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(14)]
	public class M0014_AddingWorkshifts : Migration
	{
		public override void Up()
		{
			Create.Table("Workshifts")
			   .WithColumn("WorkshiftId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("Name").AsString(512).NotNullable()
			   .WithColumn("Color").AsString(512).NotNullable()
			   .WithColumn("Start").AsDateTime2().NotNullable()
			   .WithColumn("End").AsDateTime2().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsString(128).NotNullable();

			Create.ForeignKey("FK_Workshifts_Department")
				.FromTable("Workshifts").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Table("WorkshiftDays")
			   .WithColumn("WorkshiftDayId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId").AsString(128).NotNullable()
			   .WithColumn("Day").AsDateTime2().NotNullable()
			   .WithColumn("Processed").AsBoolean().NotNullable();

			Create.ForeignKey("FK_WorkshiftDays_Workshifts")
				.FromTable("WorkshiftDays").ForeignColumn("WorkshiftId")
				.ToTable("Workshifts").PrimaryColumn("WorkshiftId");

			Create.Table("WorkshiftEntities")
			   .WithColumn("WorkshiftEntityId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId").AsString(128).NotNullable()
			   .WithColumn("BackingId").AsString(128).NotNullable();

			Create.ForeignKey("FK_WorkshiftEntities_Workshifts")
				.FromTable("WorkshiftEntities").ForeignColumn("WorkshiftId")
				.ToTable("Workshifts").PrimaryColumn("WorkshiftId");

			Create.Table("WorkshiftFills")
			   .WithColumn("WorkshiftFillId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId").AsString(128).NotNullable()
			   .WithColumn("WorkshiftDayId").AsString(128).NotNullable()
			   .WithColumn("WorkshiftEntityId").AsString(128).NotNullable()
			   .WithColumn("FilledById").AsInt32().NotNullable()
			   .WithColumn("Approved").AsBoolean().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsString(128).NotNullable();

			Create.ForeignKey("FK_WorkshiftFills_Workshifts")
				.FromTable("WorkshiftFills").ForeignColumn("WorkshiftId")
				.ToTable("Workshifts").PrimaryColumn("WorkshiftId");

			Create.ForeignKey("FK_WorkshiftFills_WorkshiftDays")
				.FromTable("WorkshiftFills").ForeignColumn("WorkshiftDayId")
				.ToTable("WorkshiftDays").PrimaryColumn("WorkshiftDayId");

			Create.ForeignKey("FK_WorkshiftFills_WorkshiftEntities")
				.FromTable("WorkshiftFills").ForeignColumn("WorkshiftEntityId")
				.ToTable("WorkshiftEntities").PrimaryColumn("WorkshiftEntityId");
		}

		public override void Down()
		{

		}
	}
}
