using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class ConfigurationAuditTests
	{
		[Test]
		public void Computed_shift_windows_are_not_stored_configuration_and_cannot_break_audit()
		{
			var day = new ShiftDay { ShiftId = 1, Day = new System.DateTime(2026, 9, 24) };
			var stamp = ConfigurationAuditProjection.Project(day);
			Assert.That(stamp.Values, Does.Not.Contain("Start").And.Not.Contain("End"));
		}

		[Test]
		public void Configuration_children_use_the_same_transactional_journal_boundary()
		{
			foreach (var type in new[] { typeof(CallTypesRepository), typeof(CallQuickTemplateRepository), typeof(UnitTypesRepository), typeof(CustomStateRepository),
				typeof(CustomStateDetailRepository), typeof(RunCardsRepository), typeof(RunCardAlarmLevelsRepository), typeof(RunCardTriggersRepository),
				typeof(RunCardUnitRequirementsRepository), typeof(RunCardRoleRequirementsRepository), typeof(RunCardAvailabilitySelectionsRepository),
				typeof(ShiftDaysRepository), typeof(ShiftGroupsRepository), typeof(ShiftGroupRolesRepository), typeof(ShiftGroupAssignmentsRepository), typeof(ShiftPersonRepository) })
				Assert.That(type.BaseType.GetGenericTypeDefinition(), Is.EqualTo(typeof(AuditedConfigurationRepository<>)), type.Name);
		}
		[Test]
		public void Secret_rotation_is_detected_without_persisting_secret_or_digest()
		{
			var row = new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken, Setting = "secret-before" };
			var before = ConfigurationAuditProjection.Project(row); row.Setting = "secret-after";
			var after = ConfigurationAuditProjection.Project(row);
			Assert.That(before.Fingerprint, Is.Not.EqualTo(after.Fingerprint));
			Assert.That(before.Values, Is.EqualTo(after.Values));
			Assert.That(after.Values, Does.Not.Contain("secret").And.Not.Contain(after.Fingerprint));
		}
		[Test]
		public void Organization_configuration_diffs_do_not_persist_names_identifiers_or_contact_data()
		{
			var units = new Unit { DepartmentId = 7, Name = "Private apparatus", VIN = "Sensitive VIN", PlateNumber = "Private plate", StationGroupId = 87654321 };
			var before = ConfigurationAuditProjection.Project(units); units.Name = "Renamed apparatus";
			var after = ConfigurationAuditProjection.Project(units);
			Assert.That(before.Fingerprint, Is.Not.EqualTo(after.Fingerprint));
			Assert.That(after.Values, Does.Not.Contain("apparatus").And.Not.Contain("Sensitive VIN").And.Not.Contain("Private plate").And.Not.Contain("87654321"));
			foreach (var row in new IEntity[] { new DepartmentGroup { DepartmentId = 7, Name = "Sensitive group name" }, new PersonnelRole { DepartmentId = 7, Name = "Sensitive role name" }, new Shift { DepartmentId = 7, Name = "Sensitive shift name", Code = "Sensitive code" } })
				Assert.That(ConfigurationAuditProjection.Project(row).Values, Does.Not.Contain("Sensitive"));
		}
		[Test]
		public void Allowlisted_boolean_diff_is_useful_but_malformed_strings_remain_redacted()
		{
			var row = new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.EnableTextToCall, Setting = "true" };
			Assert.That(ConfigurationAuditProjection.Project(row).Values, Does.Contain("\"Setting\":true"));
			row.Setting = "unexpected secret";
			Assert.That(ConfigurationAuditProjection.Project(row).Values, Does.Contain("InvalidStoredValue").And.Not.Contain("unexpected secret"));
		}
	}
}
