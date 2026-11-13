using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Mocks;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The DispatchList forms on v4 SaveCall and EditCall. Everyone ("All Call") is only ever explicit: "0". A blank list
	/// (older apps send one when nothing was picked) or a list with no entries ("|") creates a call with nobody dispatched,
	/// and a blank list on an edit keeps the call's recipients. The apps (Omni's DISPATCH_LIST_NOBODY) depend on this, so a
	/// refactor of the list parsing must keep it.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallsControllerDispatchListTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "dispatcher-1";
		private const int CallId = 500;

		private Mock<ICallsService> _callsService;
		private Mock<IQueueService> _queue;
		private Mock<IDepartmentSettingsService> _settings;
		private List<Call> _saved;
		private int _broadcasts;
		private Call _stored;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_saved = new List<Call>();
			_broadcasts = 0;
			_stored = null;

			_callsService = new Mock<ICallsService>();
			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call call, CancellationToken _) =>
				{
					if (call.CallId <= 0)
						call.CallId = 700 + _saved.Count;

					if (!_saved.Contains(call))
						_saved.Add(call);

					return call;
				});
			_callsService.Setup(x => x.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => _stored != null && _stored.CallId == id ? _stored : _saved.Find(c => c.CallId == id));
			_callsService.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);

			_queue = new Mock<IQueueService>();
			_queue.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallQueueItem item, CancellationToken _) =>
				{
					_broadcasts++;
					return true;
				});

			_settings = new Mock<IDepartmentSettingsService>();
			_activity = new Activity(nameof(CallsControllerDispatchListTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private void RequireDispatchList() =>
			_settings.Setup(x => x.GetNewCallFieldPolicyAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = true, Required = true } } });

		private CallsController Controller()
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			departments.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = "responder-1", IsDisabled = false },
					new DepartmentMember { UserId = "responder-2", IsDisabled = false }
				});
			departments.Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "responder-1" }, new IdentityUser { Id = "responder-2" } });

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 3 } });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetAllRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 4 } });
			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 7 } });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>());
			profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Dictionary<string, UserProfile>());

			var protectedWrite = new Mock<IProtectedWriteService>();
			protectedWrite.Setup(x => x.PreflightWriteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			protectedWrite.Setup(x => x.PrepareCallWriteAsync(It.IsAny<int>(), It.IsAny<Call>(), It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });

			return new CallsController(_callsService.Object, departments.Object, profiles.Object, Mock.Of<IGeoLocationProvider>(), authorization.Object,
				_queue.Object, Mock.Of<IUsersService>(), units.Object, Mock.Of<IActionLogsService>(), groups.Object, roles.Object, Mock.Of<IProtocolsService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), _settings.Object, Mock.Of<IShiftsService>(), Mock.Of<IMappingService>(),
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<ICommunicationService>(), Mock.Of<IWeatherAlertService>(), Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentDataProtectionService>(),
				Mock.Of<IProtectedReadService>(), protectedWrite.Object, Mock.Of<IContactsService>(), Mock.Of<IDispatchScopeService>(),
				Mock.Of<ICallLocationHistoryService>(), Mock.Of<IPendingCallsService>(), Mock.Of<ICallClosureService>(),
				new InboundEmailReceiptsService(new MockInboundEmailReceiptsRepository(), TimeProvider.System))
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		private static NewCallInput NewCall(string dispatchList, bool pending = false) => new NewCallInput
		{
			Priority = 1,
			Name = "Fuel spill",
			Nature = "Logged for the record",
			DispatchList = dispatchList,
			IsPending = pending ? true : null
		};

		private static int DispatchCount(Call call) =>
			(call.Dispatches?.Count ?? 0) + (call.GroupDispatches?.Count ?? 0) + (call.RoleDispatches?.Count ?? 0) + (call.UnitDispatches?.Count ?? 0);

		[TestCase("0")]
		[TestCase(" 0 ")]
		public async Task SaveCall_pages_everyone_only_for_zero(string dispatchList)
		{
			await Controller().SaveCall(NewCall(dispatchList), CancellationToken.None);

			_saved.Should().ContainSingle().Which.Dispatches.Select(x => x.UserId).Should().BeEquivalentTo("responder-1", "responder-2");
			_broadcasts.Should().Be(1);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		public async Task SaveCall_with_a_blank_list_dispatches_nobody(string dispatchList)
		{
			// Older apps send a blank list when nothing was picked; it used to page the whole department.
			var result = await Controller().SaveCall(NewCall(dispatchList), CancellationToken.None);

			// The broadcast is still queued, as for "|": with no dispatches on the call it pages no one.
			result.Result.Should().BeOfType<CreatedAtActionResult>();
			DispatchCount(_saved.Should().ContainSingle().Subject).Should().Be(0);
		}

		[Test]
		public async Task EditCall_with_a_blank_list_keeps_the_recipients_and_pages_nobody_new()
		{
			_stored = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				State = (int)CallStates.Active,
				Name = "Fuel spill",
				NatureOfCall = "Logged for the record",
				HasBeenDispatched = true,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = "responder-1" } },
				GroupDispatches = new List<CallDispatchGroup>(),
				RoleDispatches = new List<CallDispatchRole>(),
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = CallId, UnitId = 7 } }
			};

			var result = await Controller().EditCall(new EditCallInput { Id = CallId.ToString(), Priority = 1, Name = "Fuel spill", Nature = "Logged for the record", DispatchList = "" },
				CancellationToken.None);

			result.Result.Should().BeOfType<OkObjectResult>();
			_stored.Dispatches.Select(x => x.UserId).Should().Equal("responder-1");
			_stored.UnitDispatches.Select(x => x.UnitId).Should().Equal(7);
			_broadcasts.Should().Be(0);
		}

		[Test]
		public async Task EditCall_with_zero_adds_everyone_without_duplicating_who_is_already_on_the_call()
		{
			_stored = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				State = (int)CallStates.Active,
				Name = "Fuel spill",
				NatureOfCall = "Logged for the record",
				HasBeenDispatched = true,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = "responder-1" } },
				GroupDispatches = new List<CallDispatchGroup>(),
				RoleDispatches = new List<CallDispatchRole>(),
				UnitDispatches = new List<CallDispatchUnit>()
			};

			await Controller().EditCall(new EditCallInput { Id = CallId.ToString(), Priority = 1, Name = "Fuel spill", Nature = "Logged for the record", DispatchList = "0" },
				CancellationToken.None);

			_stored.Dispatches.Select(x => x.UserId).Should().BeEquivalentTo("responder-1", "responder-2");
		}

		[Test]
		public async Task SaveCall_with_a_list_that_has_no_entries_creates_the_call_with_nobody_dispatched()
		{
			var result = await Controller().SaveCall(NewCall("|"), CancellationToken.None);

			result.Result.Should().BeOfType<CreatedAtActionResult>();
			var call = _saved.Should().ContainSingle().Subject;
			call.State.Should().Be((int)CallStates.Active);
			DispatchCount(call).Should().Be(0);
		}

		[Test]
		public async Task SaveCall_of_a_pending_call_with_a_list_that_has_no_entries_proposes_nobody()
		{
			await Controller().SaveCall(NewCall("|", pending: true), CancellationToken.None);

			var call = _saved.Should().ContainSingle().Subject;
			call.State.Should().Be((int)CallStates.Pending);
			DispatchCount(call).Should().Be(0);
			_broadcasts.Should().Be(0);
		}

		[Test]
		public async Task SaveCall_still_dispatches_an_explicit_list()
		{
			await Controller().SaveCall(NewCall("P:responder-2|G:3|R:4|U:7|"), CancellationToken.None);

			var call = _saved.Should().ContainSingle().Subject;
			call.Dispatches.Select(x => x.UserId).Should().Equal("responder-2");
			call.GroupDispatches.Select(x => x.DepartmentGroupId).Should().Equal(3);
			call.RoleDispatches.Select(x => x.RoleId).Should().Equal(4);
			call.UnitDispatches.Select(x => x.UnitId).Should().Equal(7);
		}

		[Test]
		public async Task SaveCall_refuses_a_list_with_no_entries_when_the_department_requires_a_dispatch_list()
		{
			RequireDispatchList();

			var result = await Controller().SaveCall(NewCall("|"), CancellationToken.None);

			result.Result.Should().BeOfType<BadRequestObjectResult>();
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task SaveCall_accepts_a_named_list_when_the_department_requires_a_dispatch_list()
		{
			RequireDispatchList();

			var result = await Controller().SaveCall(NewCall("U:7"), CancellationToken.None);

			result.Result.Should().BeOfType<CreatedAtActionResult>();
			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task EditCall_with_a_list_that_has_no_entries_removes_every_dispatch()
		{
			_stored = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				State = (int)CallStates.Active,
				Name = "Fuel spill",
				NatureOfCall = "Logged for the record",
				HasBeenDispatched = true,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = "responder-1" } },
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = CallId, DepartmentGroupId = 3 } },
				RoleDispatches = new List<CallDispatchRole> { new CallDispatchRole { CallId = CallId, RoleId = 4 } },
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = CallId, UnitId = 7 } }
			};

			var result = await Controller().EditCall(new EditCallInput { Id = CallId.ToString(), Priority = 1, Name = "Fuel spill", Nature = "Logged for the record", DispatchList = "|" },
				CancellationToken.None);

			result.Result.Should().BeOfType<OkObjectResult>();
			DispatchCount(_stored).Should().Be(0);
			_broadcasts.Should().Be(0);
		}

		private void HideDispatchLists(params string[] keys) =>
			_settings.Setup(x => x.GetNewCallFieldPolicyAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new NewCallFieldPolicy { Rules = keys.Select(k => new NewCallFieldRule { Key = k, Visible = false }).ToList() });

		[Test]
		public async Task EditCall_keeps_the_dispatches_of_a_list_the_department_hides()
		{
			// The client's edit form has no personnel list, so the list it sends names none: a run card's (or another
			// surface's) personnel dispatch stays. A shown list is still replaced by what was sent.
			HideDispatchLists(NewCallFieldKeys.DispatchPersonnel);
			_stored = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				State = (int)CallStates.Active,
				Name = "Fuel spill",
				NatureOfCall = "Logged for the record",
				HasBeenDispatched = true,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = "responder-1" } },
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = CallId, DepartmentGroupId = 3 } },
				RoleDispatches = new List<CallDispatchRole>(),
				UnitDispatches = new List<CallDispatchUnit>()
			};

			var result = await Controller().EditCall(new EditCallInput { Id = CallId.ToString(), Priority = 1, Name = "Fuel spill", Nature = "Logged for the record", DispatchList = "U:7" },
				CancellationToken.None);

			result.Result.Should().BeOfType<OkObjectResult>();
			_stored.Dispatches.Select(x => x.UserId).Should().Equal("responder-1");
			_stored.GroupDispatches.Should().BeEmpty();
			_stored.UnitDispatches.Select(x => x.UnitId).Should().Equal(7);
		}

		[Test]
		public async Task EditCall_with_every_list_hidden_and_a_list_with_no_entries_keeps_every_dispatch()
		{
			HideDispatchLists(NewCallFieldKeys.DispatchPersonnel, NewCallFieldKeys.DispatchGroups, NewCallFieldKeys.DispatchRoles, NewCallFieldKeys.DispatchUnits);
			_stored = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				State = (int)CallStates.Active,
				Name = "Fuel spill",
				NatureOfCall = "Logged for the record",
				HasBeenDispatched = true,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = "responder-1" } },
				GroupDispatches = new List<CallDispatchGroup>(),
				RoleDispatches = new List<CallDispatchRole>(),
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = CallId, UnitId = 7 } }
			};

			await Controller().EditCall(new EditCallInput { Id = CallId.ToString(), Priority = 1, Name = "Fuel spill", Nature = "Logged for the record", DispatchList = "|" },
				CancellationToken.None);

			DispatchCount(_stored).Should().Be(2);
		}

		[Test]
		public async Task SaveCall_holds_an_app_that_sends_a_map_page_to_a_required_one_but_not_an_app_that_has_no_input()
		{
			_settings.Setup(x => x.GetNewCallFieldPolicyAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.MapPage, Visible = true, Required = true } } });

			var blank = NewCall("|");
			blank.MapPage = "";
			(await Controller().SaveCall(blank, CancellationToken.None)).Result.Should().BeOfType<BadRequestObjectResult>();

			(await Controller().SaveCall(NewCall("|"), CancellationToken.None)).Result.Should().BeOfType<CreatedAtActionResult>();

			var withPage = NewCall("|");
			withPage.MapPage = "12-C4";
			(await Controller().SaveCall(withPage, CancellationToken.None)).Result.Should().BeOfType<CreatedAtActionResult>();
			_saved.Last().MapPage.Should().Be("12-C4");
		}

		[Test]
		public async Task GetNewCallFieldPolicy_reports_each_list_a_section_hidden_before_still_hides()
		{
			// Clients read keys as they are: a list with no rule of its own would read as shown.
			_settings.Setup(x => x.GetNewCallFieldPolicyAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = false } } });

			var result = (await Controller().GetNewCallFieldPolicy()).Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<NewCallFieldPolicyResult>().Subject;

			result.Data.Rules.Where(x => !x.Visible).Select(x => x.Key).Should().BeEquivalentTo(
				NewCallFieldKeys.DispatchList,
				NewCallFieldKeys.DispatchPersonnel,
				NewCallFieldKeys.DispatchGroups,
				NewCallFieldKeys.DispatchRoles,
				NewCallFieldKeys.DispatchUnits);
		}
	}
}
