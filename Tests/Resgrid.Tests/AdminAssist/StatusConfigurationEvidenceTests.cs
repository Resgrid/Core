using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class StatusConfigurationEvidenceTests
	{
		private const int DepartmentId = 7;
		private static readonly AdminAssistActor Actor = new AdminAssistActor(DepartmentId, "admin");

		private Mock<ICustomStateService> _customStates;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentSettingsService> _settings;
		private List<CustomState> _sets;
		private List<UnitType> _unitTypes;
		private List<UnitTypeCallStatusOverride> _overrides;

		[SetUp]
		public void SetUp()
		{
			_sets = new List<CustomState>();
			_unitTypes = new List<UnitType>();
			_overrides = new List<UnitTypeCallStatusOverride>();
			_customStates = new Mock<ICustomStateService>();
			_customStates.Setup(c => c.GetAllActiveCustomStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _sets);
			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _unitTypes);
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetUnitCallDispatchStatusTargetAsync(DepartmentId)).ReturnsAsync(StatusTarget.Default);
			_settings.Setup(s => s.GetUnitCallReleaseStatusTargetAsync(DepartmentId)).ReturnsAsync(StatusTarget.Default);
			_settings.Setup(s => s.GetUnitCallStatusOverridesByUnitTypeAsync(DepartmentId)).ReturnsAsync(() => _overrides);
		}

		private static CustomState Set(int id, CustomStateTypes type, string name, params (int Id, string Text, int BaseType)[] options)
		{
			var set = new CustomState { CustomStateId = id, DepartmentId = DepartmentId, Type = (int)type, Name = name, Details = new Collection<CustomStateDetail>() };
			var order = 0;
			foreach (var option in options)
				set.Details.Add(new CustomStateDetail { CustomStateDetailId = option.Id, CustomStateId = id, ButtonText = option.Text, BaseType = option.BaseType, Order = order++ });
			return set;
		}

		private async Task<Dictionary<string, ConfigurationEvidence>> Evidence()
		{
			var source = new StatusConfigurationEvidenceSource(_customStates.Object, _units.Object, _settings.Object);
			var evidence = (await source.ReadAsync(Actor, DateTime.UtcNow, CancellationToken.None)).ToDictionary(e => e.Id);
			Assert.That(evidence.Keys, Is.EquivalentTo(source.EvidenceIds));
			return evidence;
		}

		private Task<IReadOnlyList<FindingSubject>> Subjects(string ruleId) =>
			new StatusConfigurationFindingSubjects(_customStates.Object, _units.Object, _settings.Object).ReadAsync(Actor, ruleId, CancellationToken.None);

		[Test]
		public async Task A_unit_set_without_meanings_is_flagged_for_both_rules()
		{
			// Brockville, 2026-10-13: every option of the unit type's set had no meaning, so dispatch and release changed nothing.
			_sets.Add(Set(2041, CustomStateTypes.Unit, "Dispatch Status", (1, "DISPATCHED", -1), (2, "RETURNING", -1), (3, "IN QUARTERS", -1)));
			_unitTypes.Add(new UnitType { UnitTypeId = 10, DepartmentId = DepartmentId, Type = "Pumper", CustomStatesId = 2041 });
			_unitTypes.Add(new UnitType { UnitTypeId = 11, DepartmentId = DepartmentId, Type = "Rescue" });

			var evidence = await Evidence();

			Assert.That(evidence["statusOptionsWithoutMeaning"].Number, Is.EqualTo(3m));
			Assert.That(evidence["unitTypesWithoutStatusTarget"].Number, Is.EqualTo(1m), "The built-in statuses always have Responding and Released.");
			Assert.That((await Subjects("status-meanings")).Select(s => s.Name), Is.EqualTo(new[] { "Dispatch Status (3)" }));
			Assert.That((await Subjects("unit-status-targets")).Select(s => (s.Id, s.Name)), Is.EqualTo(new[] { ("10", "Pumper") }));
		}

		[Test]
		public async Task Meanings_on_the_dispatch_and_release_chain_clear_both_rules()
		{
			_sets.Add(Set(2041, CustomStateTypes.Unit, "Dispatch Status",
				(1, "DISPATCHED", (int)ActionBaseTypes.Dispatched), (2, "RETURNING", (int)ActionBaseTypes.Returning), (3, "IN QUARTERS", (int)ActionBaseTypes.InQuarters)));
			_unitTypes.Add(new UnitType { UnitTypeId = 10, DepartmentId = DepartmentId, Type = "Pumper", CustomStatesId = 2041 });

			var evidence = await Evidence();

			Assert.That(evidence["statusOptionsWithoutMeaning"].Number, Is.EqualTo(0m));
			Assert.That(evidence["unitTypesWithoutStatusTarget"].Number, Is.EqualTo(0m));
		}

		[Test]
		public async Task A_meaning_the_set_lacks_flags_the_unit_type_but_leave_unchanged_does_not()
		{
			_sets.Add(Set(2041, CustomStateTypes.Unit, "Dispatch Status",
				(1, "DISPATCHED", (int)ActionBaseTypes.Dispatched), (3, "IN QUARTERS", (int)ActionBaseTypes.InQuarters)));
			_unitTypes.Add(new UnitType { UnitTypeId = 10, DepartmentId = DepartmentId, Type = "Pumper", CustomStatesId = 2041 });
			_settings.Setup(s => s.GetUnitCallReleaseStatusTargetAsync(DepartmentId)).ReturnsAsync(StatusTarget.ForBaseType(ActionBaseTypes.Returning));

			Assert.That((await Evidence())["unitTypesWithoutStatusTarget"].Number, Is.EqualTo(1m));

			_settings.Setup(s => s.GetUnitCallReleaseStatusTargetAsync(DepartmentId)).ReturnsAsync(StatusTarget.LeaveUnchanged);

			Assert.That((await Evidence())["unitTypesWithoutStatusTarget"].Number, Is.EqualTo(0m));
		}

		[Test]
		public async Task A_unit_type_override_naming_an_active_option_covers_a_missing_meaning()
		{
			_sets.Add(Set(2041, CustomStateTypes.Unit, "Dispatch Status", (1, "DISPATCHED", -1), (2, "RETURNING", -1)));
			_unitTypes.Add(new UnitType { UnitTypeId = 10, DepartmentId = DepartmentId, Type = "Pumper", CustomStatesId = 2041 });
			_overrides.Add(new UnitTypeCallStatusOverride { UnitTypeId = 10, DispatchStatus = 1, ReleaseStatus = 2 });

			Assert.That((await Evidence())["unitTypesWithoutStatusTarget"].Number, Is.EqualTo(0m));

			_overrides[0].ReleaseStatus = 99; // not an option of the set: the department-wide release status applies again

			Assert.That((await Evidence())["unitTypesWithoutStatusTarget"].Number, Is.EqualTo(1m));
		}

		[Test]
		public async Task Only_sets_something_reads_count_towards_missing_meanings()
		{
			_sets.Add(Set(1, CustomStateTypes.Personnel, "Personnel", (11, "Responding", (int)ActionBaseTypes.Responding), (12, "Standing By", -1)));
			_sets.Add(Set(2, CustomStateTypes.Staffing, "Staffing", (21, "On Duty", (int)StaffingBaseTypes.OnShift), (22, "Pager Only", -1)));
			_sets.Add(Set(3, CustomStateTypes.Unit, "Unused unit set", (31, "Enroute", -1)));
			var deleted = Set(4, CustomStateTypes.Personnel, "Deleted option", (41, "Gone", -1));
			deleted.Details.First().IsDeleted = true;
			_sets.Add(deleted);

			var evidence = await Evidence();

			Assert.That(evidence["statusOptionsWithoutMeaning"].Number, Is.EqualTo(2m), "The unused unit set and deleted options are not counted.");
			Assert.That((await Subjects("status-meanings")).Select(s => s.Name), Is.EqualTo(new[] { "Personnel (1)", "Staffing (1)" }));
		}

		[Test]
		public async Task Other_rules_and_other_departments_get_no_subjects()
		{
			_sets.Add(Set(2041, CustomStateTypes.Unit, "Dispatch Status", (1, "DISPATCHED", -1)));
			_unitTypes.Add(new UnitType { UnitTypeId = 10, DepartmentId = DepartmentId, Type = "Pumper", CustomStatesId = 2041 });

			Assert.That(await Subjects("empty-groups"), Is.Empty);

			_unitTypes.Add(new UnitType { UnitTypeId = 12, DepartmentId = DepartmentId + 1, Type = "Foreign" });

			Assert.ThrowsAsync<InvalidOperationException>(async () => await Subjects("unit-status-targets"), "Rows from another department stop the read.");
		}
	}
}
