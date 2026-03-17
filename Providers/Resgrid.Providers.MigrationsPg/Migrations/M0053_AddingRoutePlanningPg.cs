using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(53)]
	public class M0053_AddingRoutePlanningPg : Migration
	{
		public override void Up()
		{
			// ── RoutePlans ──────────────────────────────────────────
			Create.Table("routeplans")
				.WithColumn("routeplanid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("unitid").AsInt32().Nullable()
				.WithColumn("name").AsCustom("citext").NotNullable()
				.WithColumn("description").AsCustom("citext").Nullable()
				.WithColumn("routestatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("routecolor").AsCustom("citext").Nullable()
				.WithColumn("startlatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("startlongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("endlatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("endlongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("usestationasstart").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("usestationasend").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("optimizestoporder").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("mapboxrouteprofile").AsCustom("citext").Nullable()
				.WithColumn("mapboxroutegeometry").AsCustom("text").Nullable()
				.WithColumn("estimateddistancemeters").AsDouble().Nullable()
				.WithColumn("estimateddurationseconds").AsDouble().Nullable()
				.WithColumn("geofenceradiusmeters").AsInt32().NotNullable().WithDefaultValue(100)
				.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("addedbyid").AsCustom("citext").NotNullable()
				.WithColumn("addedon").AsDateTime().NotNullable()
				.WithColumn("updatedbyid").AsCustom("citext").Nullable()
				.WithColumn("updatedon").AsDateTime().Nullable();

			Create.ForeignKey("fk_routeplans_departments")
				.FromTable("routeplans").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_routeplans_departmentid")
				.OnTable("routeplans")
				.OnColumn("departmentid");

			// ── RouteStops ──────────────────────────────────────────
			Create.Table("routestops")
				.WithColumn("routestopid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("routeplanid").AsCustom("citext").NotNullable()
				.WithColumn("stoporder").AsInt32().NotNullable()
				.WithColumn("name").AsCustom("citext").NotNullable()
				.WithColumn("description").AsCustom("citext").Nullable()
				.WithColumn("stoptype").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("callid").AsInt32().Nullable()
				.WithColumn("latitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("longitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("address").AsCustom("citext").Nullable()
				.WithColumn("geofenceradiusmeters").AsInt32().Nullable()
				.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("plannedarrivaltime").AsDateTime().Nullable()
				.WithColumn("planneddeparturetime").AsDateTime().Nullable()
				.WithColumn("estimateddwellminutes").AsInt32().Nullable()
				.WithColumn("contactname").AsCustom("citext").Nullable()
				.WithColumn("contactnumber").AsCustom("citext").Nullable()
				.WithColumn("notes").AsCustom("text").Nullable()
				.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("addedon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_routestops_routeplans")
				.FromTable("routestops").ForeignColumn("routeplanid")
				.ToTable("routeplans").PrimaryColumn("routeplanid");

			Create.Index("ix_routestops_routeplanid")
				.OnTable("routestops")
				.OnColumn("routeplanid");

			// ── RouteSchedules ──────────────────────────────────────
			Create.Table("routeschedules")
				.WithColumn("routescheduleid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("routeplanid").AsCustom("citext").NotNullable()
				.WithColumn("recurrencetype").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("recurrencecron").AsCustom("citext").Nullable()
				.WithColumn("daysofweek").AsCustom("citext").Nullable()
				.WithColumn("dayofmonth").AsInt32().Nullable()
				.WithColumn("scheduledstarttime").AsCustom("citext").Nullable()
				.WithColumn("effectivefrom").AsDateTime().NotNullable()
				.WithColumn("effectiveto").AsDateTime().Nullable()
				.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("lastinstancecreatedon").AsDateTime().Nullable()
				.WithColumn("addedon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_routeschedules_routeplans")
				.FromTable("routeschedules").ForeignColumn("routeplanid")
				.ToTable("routeplans").PrimaryColumn("routeplanid");

			Create.Index("ix_routeschedules_routeplanid")
				.OnTable("routeschedules")
				.OnColumn("routeplanid");

			// ── RouteInstances ──────────────────────────────────────
			Create.Table("routeinstances")
				.WithColumn("routeinstanceid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("routeplanid").AsCustom("citext").NotNullable()
				.WithColumn("routescheduleid").AsCustom("citext").Nullable()
				.WithColumn("unitid").AsInt32().NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("startedbyuserid").AsCustom("citext").NotNullable()
				.WithColumn("scheduledstarton").AsDateTime().Nullable()
				.WithColumn("actualstarton").AsDateTime().Nullable()
				.WithColumn("actualendon").AsDateTime().Nullable()
				.WithColumn("endedbyuserid").AsCustom("citext").Nullable()
				.WithColumn("totaldistancemeters").AsDouble().Nullable()
				.WithColumn("totaldurationseconds").AsDouble().Nullable()
				.WithColumn("actualroutegeometry").AsCustom("text").Nullable()
				.WithColumn("stopscompleted").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("stopstotal").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("notes").AsCustom("text").Nullable()
				.WithColumn("addedon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_routeinstances_routeplans")
				.FromTable("routeinstances").ForeignColumn("routeplanid")
				.ToTable("routeplans").PrimaryColumn("routeplanid");

			Create.ForeignKey("fk_routeinstances_departments")
				.FromTable("routeinstances").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_routeinstances_routeplanid")
				.OnTable("routeinstances")
				.OnColumn("routeplanid");

			Create.Index("ix_routeinstances_departmentid")
				.OnTable("routeinstances")
				.OnColumn("departmentid");

			Create.Index("ix_routeinstances_unitid")
				.OnTable("routeinstances")
				.OnColumn("unitid");

			// ── RouteInstanceStops ──────────────────────────────────
			Create.Table("routeinstancestops")
				.WithColumn("routeinstancestopid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("routeinstanceid").AsCustom("citext").NotNullable()
				.WithColumn("routestopid").AsCustom("citext").NotNullable()
				.WithColumn("stoporder").AsInt32().NotNullable()
				.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("checkinon").AsDateTime().Nullable()
				.WithColumn("checkintype").AsInt32().Nullable()
				.WithColumn("checkinlatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("checkinlongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("checkouton").AsDateTime().Nullable()
				.WithColumn("checkoutlatitude").AsDecimal(10, 7).Nullable()
				.WithColumn("checkoutlongitude").AsDecimal(10, 7).Nullable()
				.WithColumn("dwellseconds").AsInt32().Nullable()
				.WithColumn("skipreason").AsCustom("citext").Nullable()
				.WithColumn("notes").AsCustom("text").Nullable()
				.WithColumn("estimatedarrivalon").AsDateTime().Nullable()
				.WithColumn("actualarrivaldeviation").AsInt32().Nullable()
				.WithColumn("addedon").AsDateTime().NotNullable();

			Create.ForeignKey("fk_routeinstancestops_routeinstances")
				.FromTable("routeinstancestops").ForeignColumn("routeinstanceid")
				.ToTable("routeinstances").PrimaryColumn("routeinstanceid");

			Create.ForeignKey("fk_routeinstancestops_routestops")
				.FromTable("routeinstancestops").ForeignColumn("routestopid")
				.ToTable("routestops").PrimaryColumn("routestopid");

			Create.Index("ix_routeinstancestops_routeinstanceid")
				.OnTable("routeinstancestops")
				.OnColumn("routeinstanceid");

			// ── RouteDeviations ─────────────────────────────────────
			Create.Table("routedeviations")
				.WithColumn("routedeviationid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("routeinstanceid").AsCustom("citext").NotNullable()
				.WithColumn("detectedon").AsDateTime().NotNullable()
				.WithColumn("latitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("longitude").AsDecimal(10, 7).NotNullable()
				.WithColumn("deviationdistancemeters").AsDouble().NotNullable()
				.WithColumn("deviationtype").AsInt32().NotNullable()
				.WithColumn("isacknowledged").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("acknowledgedbyuserid").AsCustom("citext").Nullable()
				.WithColumn("acknowledgedon").AsDateTime().Nullable()
				.WithColumn("notes").AsCustom("citext").Nullable();

			Create.ForeignKey("fk_routedeviations_routeinstances")
				.FromTable("routedeviations").ForeignColumn("routeinstanceid")
				.ToTable("routeinstances").PrimaryColumn("routeinstanceid");

			Create.Index("ix_routedeviations_routeinstanceid")
				.OnTable("routedeviations")
				.OnColumn("routeinstanceid");
		}

		public override void Down()
		{
			Delete.Index("ix_routedeviations_routeinstanceid").OnTable("routedeviations");
			Delete.ForeignKey("fk_routedeviations_routeinstances").OnTable("routedeviations");
			Delete.Table("routedeviations");

			Delete.Index("ix_routeinstancestops_routeinstanceid").OnTable("routeinstancestops");
			Delete.ForeignKey("fk_routeinstancestops_routestops").OnTable("routeinstancestops");
			Delete.ForeignKey("fk_routeinstancestops_routeinstances").OnTable("routeinstancestops");
			Delete.Table("routeinstancestops");

			Delete.Index("ix_routeinstances_unitid").OnTable("routeinstances");
			Delete.Index("ix_routeinstances_departmentid").OnTable("routeinstances");
			Delete.Index("ix_routeinstances_routeplanid").OnTable("routeinstances");
			Delete.ForeignKey("fk_routeinstances_departments").OnTable("routeinstances");
			Delete.ForeignKey("fk_routeinstances_routeplans").OnTable("routeinstances");
			Delete.Table("routeinstances");

			Delete.Index("ix_routeschedules_routeplanid").OnTable("routeschedules");
			Delete.ForeignKey("fk_routeschedules_routeplans").OnTable("routeschedules");
			Delete.Table("routeschedules");

			Delete.Index("ix_routestops_routeplanid").OnTable("routestops");
			Delete.ForeignKey("fk_routestops_routeplans").OnTable("routestops");
			Delete.Table("routestops");

			Delete.Index("ix_routeplans_departmentid").OnTable("routeplans");
			Delete.ForeignKey("fk_routeplans_departments").OnTable("routeplans");
			Delete.Table("routeplans");
		}
	}
}
