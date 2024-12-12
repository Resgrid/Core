using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(14)]
	public class M0014_AddingWorkshifts : Migration
	{
		public override void Up()
		{
			Create.Table("Workshifts")
			   .WithColumn("WorkshiftId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext").NotNullable()
			   .WithColumn("Color").AsCustom("citext").NotNullable()
			   .WithColumn("Start").AsDateTime2().NotNullable()
			   .WithColumn("End").AsDateTime2().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_Workshifts_Department")
				.FromTable("Workshifts").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Table("WorkshiftDays")
			   .WithColumn("WorkshiftDayId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId").AsCustom("citext").NotNullable()
			   .WithColumn("Day").AsDateTime2().NotNullable()
			   .WithColumn("Processed").AsBoolean().NotNullable();

			Create.ForeignKey("FK_WorkshiftDays_Workshifts")
				.FromTable("WorkshiftDays").ForeignColumn("WorkshiftId")
				.ToTable("Workshifts").PrimaryColumn("WorkshiftId");

			Create.Table("WorkshiftEntities")
			   .WithColumn("WorkshiftEntityId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId").AsCustom("citext").NotNullable()
			   .WithColumn("BackingId").AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_WorkshiftEntities_Workshifts")
				.FromTable("WorkshiftEntities").ForeignColumn("WorkshiftId")
				.ToTable("Workshifts").PrimaryColumn("WorkshiftId");

			Create.Table("WorkshiftFills")
			   .WithColumn("WorkshiftFillId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId").AsCustom("citext").NotNullable()
			   .WithColumn("WorkshiftDayId").AsCustom("citext").NotNullable()
			   .WithColumn("WorkshiftEntityId").AsCustom("citext").NotNullable()
			   .WithColumn("FilledById").AsInt32().NotNullable()
			   .WithColumn("Approved").AsBoolean().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsCustom("citext").NotNullable();

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
