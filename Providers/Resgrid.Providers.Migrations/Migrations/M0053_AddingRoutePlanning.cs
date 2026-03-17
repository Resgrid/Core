using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(53)]
	public class M0053_AddingRoutePlanning : Migration
	{
		public override void Up()
		{
			// ── RoutePlans ──────────────────────────────────────────
			Create.Table("RoutePlans")
				.WithColumn("RoutePlanId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("UnitId").AsInt32().Nullable()
				.WithColumn("Name").AsString(250).NotNullable()
				.WithColumn("Description").AsString(1000).Nullable()
				.WithColumn("RouteStatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("RouteColor").AsString(50).Nullable()
				.WithColumn("StartLatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("StartLongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("EndLatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("EndLongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("UseStationAsStart").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("UseStationAsEnd").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("OptimizeStopOrder").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("MapboxRouteProfile").AsString(50).Nullable()
				.WithColumn("MapboxRouteGeometry").AsString(int.MaxValue).Nullable()
				.WithColumn("EstimatedDistanceMeters").AsDouble().Nullable()
				.WithColumn("EstimatedDurationSeconds").AsDouble().Nullable()
				.WithColumn("GeofenceRadiusMeters").AsInt32().NotNullable().WithDefaultValue(100)
				.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("AddedById").AsString(128).NotNullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable()
				.WithColumn("UpdatedById").AsString(128).Nullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable();

			Create.ForeignKey("FK_RoutePlans_Departments")
				.FromTable("RoutePlans").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_RoutePlans_DepartmentId")
				.OnTable("RoutePlans")
				.OnColumn("DepartmentId");

			// ── RouteStops ──────────────────────────────────────────
			Create.Table("RouteStops")
				.WithColumn("RouteStopId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("RoutePlanId").AsString(128).NotNullable()
				.WithColumn("StopOrder").AsInt32().NotNullable()
				.WithColumn("Name").AsString(250).NotNullable()
				.WithColumn("Description").AsString(1000).Nullable()
				.WithColumn("StopType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("CallId").AsInt32().Nullable()
				.WithColumn("Latitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("Longitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("Address").AsString(500).Nullable()
				.WithColumn("GeofenceRadiusMeters").AsInt32().Nullable()
				.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("PlannedArrivalTime").AsDateTime2().Nullable()
				.WithColumn("PlannedDepartureTime").AsDateTime2().Nullable()
				.WithColumn("EstimatedDwellMinutes").AsInt32().Nullable()
				.WithColumn("ContactName").AsString(250).Nullable()
				.WithColumn("ContactNumber").AsString(50).Nullable()
				.WithColumn("Notes").AsString(int.MaxValue).Nullable()
				.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_RouteStops_RoutePlans")
				.FromTable("RouteStops").ForeignColumn("RoutePlanId")
				.ToTable("RoutePlans").PrimaryColumn("RoutePlanId");

			Create.Index("IX_RouteStops_RoutePlanId")
				.OnTable("RouteStops")
				.OnColumn("RoutePlanId");

			// ── RouteSchedules ──────────────────────────────────────
			Create.Table("RouteSchedules")
				.WithColumn("RouteScheduleId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("RoutePlanId").AsString(128).NotNullable()
				.WithColumn("RecurrenceType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("RecurrenceCron").AsString(250).Nullable()
				.WithColumn("DaysOfWeek").AsString(50).Nullable()
				.WithColumn("DayOfMonth").AsInt32().Nullable()
				.WithColumn("ScheduledStartTime").AsString(20).Nullable()
				.WithColumn("EffectiveFrom").AsDateTime2().NotNullable()
				.WithColumn("EffectiveTo").AsDateTime2().Nullable()
				.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("LastInstanceCreatedOn").AsDateTime2().Nullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_RouteSchedules_RoutePlans")
				.FromTable("RouteSchedules").ForeignColumn("RoutePlanId")
				.ToTable("RoutePlans").PrimaryColumn("RoutePlanId");

			Create.Index("IX_RouteSchedules_RoutePlanId")
				.OnTable("RouteSchedules")
				.OnColumn("RoutePlanId");

			// ── RouteInstances ──────────────────────────────────────
			Create.Table("RouteInstances")
				.WithColumn("RouteInstanceId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("RoutePlanId").AsString(128).NotNullable()
				.WithColumn("RouteScheduleId").AsString(128).Nullable()
				.WithColumn("UnitId").AsInt32().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("StartedByUserId").AsString(128).NotNullable()
				.WithColumn("ScheduledStartOn").AsDateTime2().Nullable()
				.WithColumn("ActualStartOn").AsDateTime2().Nullable()
				.WithColumn("ActualEndOn").AsDateTime2().Nullable()
				.WithColumn("EndedByUserId").AsString(128).Nullable()
				.WithColumn("TotalDistanceMeters").AsDouble().Nullable()
				.WithColumn("TotalDurationSeconds").AsDouble().Nullable()
				.WithColumn("ActualRouteGeometry").AsString(int.MaxValue).Nullable()
				.WithColumn("StopsCompleted").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("StopsTotal").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Notes").AsString(int.MaxValue).Nullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_RouteInstances_RoutePlans")
				.FromTable("RouteInstances").ForeignColumn("RoutePlanId")
				.ToTable("RoutePlans").PrimaryColumn("RoutePlanId");

			Create.ForeignKey("FK_RouteInstances_Departments")
				.FromTable("RouteInstances").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_RouteInstances_RoutePlanId")
				.OnTable("RouteInstances")
				.OnColumn("RoutePlanId");

			Create.Index("IX_RouteInstances_DepartmentId")
				.OnTable("RouteInstances")
				.OnColumn("DepartmentId");

			Create.Index("IX_RouteInstances_UnitId")
				.OnTable("RouteInstances")
				.OnColumn("UnitId");

			// ── RouteInstanceStops ──────────────────────────────────
			Create.Table("RouteInstanceStops")
				.WithColumn("RouteInstanceStopId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("RouteInstanceId").AsString(128).NotNullable()
				.WithColumn("RouteStopId").AsString(128).NotNullable()
				.WithColumn("StopOrder").AsInt32().NotNullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("CheckInOn").AsDateTime2().Nullable()
				.WithColumn("CheckInType").AsInt32().Nullable()
				.WithColumn("CheckInLatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("CheckInLongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("CheckOutOn").AsDateTime2().Nullable()
				.WithColumn("CheckOutLatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("CheckOutLongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("DwellSeconds").AsInt32().Nullable()
				.WithColumn("SkipReason").AsString(500).Nullable()
				.WithColumn("Notes").AsString(int.MaxValue).Nullable()
				.WithColumn("EstimatedArrivalOn").AsDateTime2().Nullable()
				.WithColumn("ActualArrivalDeviation").AsInt32().Nullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_RouteInstanceStops_RouteInstances")
				.FromTable("RouteInstanceStops").ForeignColumn("RouteInstanceId")
				.ToTable("RouteInstances").PrimaryColumn("RouteInstanceId");

			Create.ForeignKey("FK_RouteInstanceStops_RouteStops")
				.FromTable("RouteInstanceStops").ForeignColumn("RouteStopId")
				.ToTable("RouteStops").PrimaryColumn("RouteStopId");

			Create.Index("IX_RouteInstanceStops_RouteInstanceId")
				.OnTable("RouteInstanceStops")
				.OnColumn("RouteInstanceId");

			// ── RouteDeviations ─────────────────────────────────────
			Create.Table("RouteDeviations")
				.WithColumn("RouteDeviationId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("RouteInstanceId").AsString(128).NotNullable()
				.WithColumn("DetectedOn").AsDateTime2().NotNullable()
				.WithColumn("Latitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("Longitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("DeviationDistanceMeters").AsDouble().NotNullable()
				.WithColumn("DeviationType").AsInt32().NotNullable()
				.WithColumn("IsAcknowledged").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("AcknowledgedByUserId").AsString(128).Nullable()
				.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
				.WithColumn("Notes").AsString(500).Nullable();

			Create.ForeignKey("FK_RouteDeviations_RouteInstances")
				.FromTable("RouteDeviations").ForeignColumn("RouteInstanceId")
				.ToTable("RouteInstances").PrimaryColumn("RouteInstanceId");

			Create.Index("IX_RouteDeviations_RouteInstanceId")
				.OnTable("RouteDeviations")
				.OnColumn("RouteInstanceId");
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_RouteDeviations_RouteInstances").OnTable("RouteDeviations");
			Delete.Table("RouteDeviations");

			Delete.ForeignKey("FK_RouteInstanceStops_RouteStops").OnTable("RouteInstanceStops");
			Delete.ForeignKey("FK_RouteInstanceStops_RouteInstances").OnTable("RouteInstanceStops");
			Delete.Table("RouteInstanceStops");

			Delete.ForeignKey("FK_RouteInstances_Departments").OnTable("RouteInstances");
			Delete.ForeignKey("FK_RouteInstances_RoutePlans").OnTable("RouteInstances");
			Delete.Table("RouteInstances");

			Delete.ForeignKey("FK_RouteSchedules_RoutePlans").OnTable("RouteSchedules");
			Delete.Table("RouteSchedules");

			Delete.ForeignKey("FK_RouteStops_RoutePlans").OnTable("RouteStops");
			Delete.Table("RouteStops");

			Delete.ForeignKey("FK_RoutePlans_Departments").OnTable("RoutePlans");
			Delete.Table("RoutePlans");
		}
	}
}
