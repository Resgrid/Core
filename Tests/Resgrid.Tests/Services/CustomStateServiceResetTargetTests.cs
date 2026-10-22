using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The daily status and staffing resets (Department Settings) now save a meaning ("b{n}") and resolve it when they run,
	/// so a department on its own statuses or levels resets to its option with that meaning. Values saved before meanings
	/// keep working: one that is still the department's option is used as is, anything else (a built-in value after the
	/// department moved to its own set, or an option since deleted) follows its meaning, and nothing matching means skip.
	/// </summary>
	[TestFixture]
	public class CustomStateServiceResetTargetTests
	{
		private const int CustomDepartmentId = 14856;
		private const int BuiltInDepartmentId = 2;

		private const int AvailableStatusId = 19721;
		private const int EnRouteStatusId = 19722;
		private const int RespondingStatusId = 19724;
		private const int UnavailableStatusId = 19725;
		private const int DeletedUnavailableStatusId = 19726;

		private const int OnDutyLevelId = 19801;
		private const int OffLevelId = 19802;
		private const int DeletedOffLevelId = 19803;

		private CustomStateService _service;

		[SetUp]
		public void SetUp()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;

			var repository = new Mock<ICustomStateRepository>();
			repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(CustomDepartmentId)).ReturnsAsync(new List<CustomState>
			{
				Set(4547, CustomStateTypes.Personnel,
					Option(AvailableStatusId, 4547, "Available", ActionBaseTypes.Available),
					Option(EnRouteStatusId, 4547, "En Route", ActionBaseTypes.Enroute),
					Option(RespondingStatusId, 4547, "Responding", ActionBaseTypes.Responding),
					Option(UnavailableStatusId, 4547, "Unavailable", ActionBaseTypes.Unavailable),
					Option(DeletedUnavailableStatusId, 4547, "Out (old)", ActionBaseTypes.Unavailable, deleted: true)),
				Set(4548, CustomStateTypes.Staffing,
					Option(OnDutyLevelId, 4548, "On Duty", StaffingBaseTypes.Available),
					Option(OffLevelId, 4548, "Off", StaffingBaseTypes.Unavailable),
					Option(DeletedOffLevelId, 4548, "Off (old)", StaffingBaseTypes.Unavailable, deleted: true))
			});
			repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(BuiltInDepartmentId)).ReturnsAsync(new List<CustomState>());

			_service = new CustomStateService(repository.Object, Mock.Of<ICustomStateDetailRepository>(), Mock.Of<IEventAggregator>(), Mock.Of<ICacheProvider>());
		}

		private static CustomState Set(int id, CustomStateTypes type, params CustomStateDetail[] details) => new CustomState
		{
			CustomStateId = id,
			DepartmentId = CustomDepartmentId,
			Type = (int)type,
			Name = type.ToString(),
			Details = new List<CustomStateDetail>(details)
		};

		private static CustomStateDetail Option(int id, int setId, string text, ActionBaseTypes baseType, bool deleted = false) =>
			new CustomStateDetail { CustomStateDetailId = id, CustomStateId = setId, ButtonText = text, ButtonColor = "#2f9e44", BaseType = (int)baseType, IsDeleted = deleted };

		private static CustomStateDetail Option(int id, int setId, string text, StaffingBaseTypes baseType, bool deleted = false) =>
			new CustomStateDetail { CustomStateDetailId = id, CustomStateId = setId, ButtonText = text, ButtonColor = "#2f9e44", BaseType = (int)baseType, IsDeleted = deleted };

		private static string Meaning(ActionBaseTypes baseType) => StatusMatch.BaseTypeToken((int)baseType);

		private static string Meaning(StaffingBaseTypes baseType) => StatusMatch.BaseTypeToken((int)baseType);

		#region Status reset

		[Test]
		public async Task A_status_meaning_resets_to_the_departments_option_with_that_meaning()
		{
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, Meaning(ActionBaseTypes.Available))).Should().Be(AvailableStatusId);
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, Meaning(ActionBaseTypes.Unavailable))).Should().Be(UnavailableStatusId,
				"the deleted option with the same meaning is never picked");
		}

		[Test]
		public async Task A_status_meaning_on_the_built_in_statuses_resets_to_the_built_in_status()
		{
			(await _service.ResolveStatusResetAsync(BuiltInDepartmentId, Meaning(ActionBaseTypes.Available))).Should().Be((int)ActionTypes.StandingBy);
		}

		[Test]
		public async Task A_saved_status_that_is_still_the_departments_option_is_kept()
		{
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, EnRouteStatusId.ToString())).Should().Be(EnRouteStatusId);
			(await _service.ResolveStatusResetAsync(BuiltInDepartmentId, ((int)ActionTypes.OnScene).ToString())).Should().Be((int)ActionTypes.OnScene);
		}

		[Test]
		public async Task A_saved_built_in_status_on_a_custom_department_follows_its_meaning()
		{
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, ((int)ActionTypes.Responding).ToString())).Should().Be(RespondingStatusId);
		}

		[Test]
		public async Task A_saved_status_since_deleted_follows_its_meaning()
		{
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, DeletedUnavailableStatusId.ToString())).Should().Be(UnavailableStatusId);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not a status")]
		[TestCase("b999")]
		public async Task An_unreadable_status_target_resolves_to_nothing(string target)
		{
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, target)).Should().BeNull();
		}

		[Test]
		public async Task The_no_meaning_status_token_resolves_to_nothing()
		{
			(await _service.ResolveStatusResetAsync(CustomDepartmentId, Meaning(ActionBaseTypes.None))).Should().BeNull();
		}

		#endregion Status reset

		#region Staffing reset

		[Test]
		public async Task A_staffing_meaning_resets_to_the_departments_level_with_that_meaning()
		{
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, Meaning(StaffingBaseTypes.Unavailable))).Should().Be(OffLevelId);
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, Meaning(StaffingBaseTypes.Available))).Should().Be(OnDutyLevelId);
		}

		[Test]
		public async Task A_staffing_meaning_on_the_built_in_levels_resets_to_the_built_in_level()
		{
			(await _service.ResolveStaffingResetAsync(BuiltInDepartmentId, Meaning(StaffingBaseTypes.Unavailable))).Should().Be((int)UserStateTypes.Unavailable);
			(await _service.ResolveStaffingResetAsync(BuiltInDepartmentId, Meaning(StaffingBaseTypes.OnShift))).Should().Be((int)UserStateTypes.OnShift);
		}

		[Test]
		public async Task A_staffing_meaning_with_no_level_resolves_to_nothing()
		{
			(await _service.ResolveStaffingResetAsync(BuiltInDepartmentId, Meaning(StaffingBaseTypes.OffDuty))).Should().BeNull("no built-in level means Off Duty");
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, Meaning(StaffingBaseTypes.Delayed))).Should().BeNull("the department has no Delayed level");
		}

		[Test]
		public async Task A_saved_level_that_is_still_the_departments_level_is_kept()
		{
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, OnDutyLevelId.ToString())).Should().Be(OnDutyLevelId);
			(await _service.ResolveStaffingResetAsync(BuiltInDepartmentId, ((int)UserStateTypes.Committed).ToString())).Should().Be((int)UserStateTypes.Committed);
		}

		[Test]
		public async Task A_saved_built_in_level_on_a_custom_department_follows_its_meaning()
		{
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, ((int)UserStateTypes.Unavailable).ToString())).Should().Be(OffLevelId);
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, ((int)UserStateTypes.Delayed).ToString())).Should().BeNull(
				"the department has no Delayed level, so the reset is skipped rather than writing a level it does not use");
		}

		[Test]
		public async Task A_saved_level_since_deleted_follows_its_meaning()
		{
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, DeletedOffLevelId.ToString())).Should().Be(OffLevelId);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("off")]
		public async Task An_unreadable_staffing_target_resolves_to_nothing(string target)
		{
			(await _service.ResolveStaffingResetAsync(CustomDepartmentId, target)).Should().BeNull();
		}

		[Test]
		public async Task The_no_meaning_staffing_resolves_to_nothing()
		{
			(await _service.ResolvePersonnelStaffingAsync(CustomDepartmentId, StaffingBaseTypes.None)).Should().BeNull();
			(await _service.ResolvePersonnelStaffingAsync(BuiltInDepartmentId, StaffingBaseTypes.None)).Should().BeNull();
		}

		#endregion Staffing reset
	}
}
