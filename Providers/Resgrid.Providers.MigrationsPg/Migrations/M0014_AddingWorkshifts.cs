using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(14)]
	public class M0014_AddingWorkshifts : Migration
	{
		public override void Up()
		{
			Create.Table("Workshifts".ToLower())
			   .WithColumn("WorkshiftId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Type".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Color".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Start".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("End".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedById".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_Workshifts_Department")
				.FromTable("Workshifts".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.Table("WorkshiftDays".ToLower())
			   .WithColumn("WorkshiftDayId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("Day".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("Processed".ToLower()).AsBoolean().NotNullable();

			Create.ForeignKey("FK_WorkshiftDays_Workshifts")
				.FromTable("WorkshiftDays".ToLower()).ForeignColumn("WorkshiftId".ToLower())
				.ToTable("Workshifts".ToLower()).PrimaryColumn("WorkshiftId".ToLower());

			Create.Table("WorkshiftEntities".ToLower())
			   .WithColumn("WorkshiftEntityId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("BackingId".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_WorkshiftEntities_Workshifts")
				.FromTable("WorkshiftEntities".ToLower()).ForeignColumn("WorkshiftId".ToLower())
				.ToTable("Workshifts".ToLower()).PrimaryColumn("WorkshiftId".ToLower());

			Create.Table("WorkshiftFills".ToLower())
			   .WithColumn("WorkshiftFillId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("WorkshiftId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("WorkshiftDayId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("WorkshiftEntityId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("FilledById".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Approved".ToLower()).AsBoolean().NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedById".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_WorkshiftFills_Workshifts")
				.FromTable("WorkshiftFills".ToLower()).ForeignColumn("WorkshiftId".ToLower())
				.ToTable("Workshifts".ToLower()).PrimaryColumn("WorkshiftId".ToLower());

			Create.ForeignKey("FK_WorkshiftFills_WorkshiftDays")
				.FromTable("WorkshiftFills".ToLower()).ForeignColumn("WorkshiftDayId".ToLower())
				.ToTable("WorkshiftDays".ToLower()).PrimaryColumn("WorkshiftDayId".ToLower());

			Create.ForeignKey("FK_WorkshiftFills_WorkshiftEntities")
				.FromTable("WorkshiftFills".ToLower()).ForeignColumn("WorkshiftEntityId".ToLower())
				.ToTable("WorkshiftEntities".ToLower()).PrimaryColumn("WorkshiftEntityId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
