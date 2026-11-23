using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.DispatcherDesk;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Shifts → Dispatcher Desk on the web (M0324): the board and planner are for dispatchers, shift managers and admins;
	/// acting for another dispatcher needs a supervisor; planning your own break needs a shift that allows it; the settings are
	/// for department admins and round-trip through the config service, which normalizes them.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DispatcherDeskControllerTests
	{
		private const int DepartmentId = 31;
		private const string Me = "me-1";
		private const string Colleague = "colleague-2";

		private static readonly Claim DepartmentAdmin = new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);

		private Dictionary<Type, Mock> _mocks;
		private Department _department;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_department = new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time", Use24HourTime = true };

			M<IStringLocalizer<Resgrid.Localization.Areas.User.DispatcherDesk.DispatcherDesk>>().Setup(x => x[It.IsAny<string>()])
				.Returns((string key) => new LocalizedString(key, key));
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _department);
			M<IDepartmentsService>().Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Me, FirstName = "Me", LastName = "Myself" },
				new PersonName { UserId = Colleague, FirstName = "Col", LastName = "League" }
			});
			M<Resgrid.Model.Services.IAuthorizationService>().Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(ShiftManagementScope.None());
			M<IDispatcherDeskConfigService>().Setup(x => x.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DispatcherDeskConfig());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private DispatcherDeskController Build(params Claim[] claims)
		{
			var constructor = typeof(DispatcherDeskController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, Me),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (DispatcherDeskController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private void AsDispatcher() => M<IDispatchAccessService>().Setup(x => x.CanUseDispatchAsync(DepartmentId, Me)).ReturnsAsync(true);

		private void AsSupervisor() => M<IDispatcherPresenceService>().Setup(x => x.CanSuperviseAsync(DepartmentId, Me)).ReturnsAsync(true);

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static DispatcherDeskActionJson ActionJson(IActionResult result) =>
			result.Should().BeOfType<JsonResult>().Which.Value.Should().BeOfType<DispatcherDeskActionJson>().Subject;

		private static IFormCollection Form(Dictionary<string, StringValues> values = null) => new FormCollection(values ?? new Dictionary<string, StringValues>());

		[TestCase("EndBreak")]
		[TestCase("SetAway")]
		[TestCase("SetAvailable")]
		[TestCase("ReleaseItem")]
		[TestCase("TransferItem")]
		[TestCase("SaveBreak")]
		[TestCase("CancelBreak")]
		[TestCase("PlanBreaks")]
		[TestCase("Settings")]
		public void State_changing_actions_are_post_with_antiforgery(string action)
		{
			var posts = typeof(DispatcherDeskController).GetMethods().Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() != null).ToList();

			posts.Should().NotBeEmpty();
			posts.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null);
		}

		#region Board and access

		[Test]
		public async Task Board_and_planner_are_for_dispatchers_shift_managers_and_admins_only()
		{
			var controller = Build();

			ShouldBeRefused(await controller.Index());
			ShouldBeRefused(await controller.Breaks(null, null));
			(await controller.Board()).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			(await controller.BreaksData(null, null)).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			M<IDispatcherPresenceService>().Verify(x => x.GetDeskAsync(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);

			AsDispatcher();
			(await Build().Index()).Should().BeOfType<ViewResult>();

			M<IDispatchAccessService>().Reset();
			M<Resgrid.Model.Services.IAuthorizationService>().Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId))
				.ReturnsAsync(new ShiftManagementScope { GroupIds = new HashSet<int> { 4 } });
			(await Build().Index()).Should().BeOfType<ViewResult>("a shift manager sees the board");

			M<Resgrid.Model.Services.IAuthorizationService>().Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(ShiftManagementScope.None());
			(await Build(DepartmentAdmin).Index()).Should().BeOfType<ViewResult>("a department admin sees the board");
		}

		[Test]
		public async Task Board_maps_members_queue_and_coverage()
		{
			AsDispatcher();
			var now = DateTime.UtcNow;
			M<IDispatcherPresenceService>().Setup(x => x.GetDeskAsync(DepartmentId, It.IsAny<DateTime>())).ReturnsAsync(new DispatcherDeskSnapshot
			{
				Enabled = true,
				MinimumAvailable = 2,
				AvailableCount = 1,
				OnDutyCount = 2,
				Members = new List<DispatcherDeskMember>
				{
					new DispatcherDeskMember
					{
						UserId = Colleague, DisplayState = DispatcherDisplayStates.OnBreak, IsOnDuty = true, Capacity = 1,
						Presence = new DispatcherPresence { State = (int)DispatcherPresenceStates.OnBreak, DeviceKind = "phone" },
						ActiveBreak = new DispatcherBreak { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, StartedOn = now.AddMinutes(-5), ExpectedReturnOn = now.AddMinutes(25) }
					},
					new DispatcherDeskMember
					{
						UserId = Me, Name = "Me Myself", DisplayState = DispatcherDisplayStates.Available, IsOnDuty = true, Capacity = 1,
						Presence = new DispatcherPresence { State = (int)DispatcherPresenceStates.Available }
					}
				},
				Queue = new List<DispatcherQueueItem>
				{
					new DispatcherQueueItem { DispatcherQueueItemId = "q-closed", State = (int)DispatcherQueueItemStates.Completed, CreatedOn = now },
					new DispatcherQueueItem { DispatcherQueueItemId = "q-low", State = (int)DispatcherQueueItemStates.Queued, Priority = 1, CreatedOn = now.AddMinutes(-1), SourceAddress = "+15551234" },
					new DispatcherQueueItem { DispatcherQueueItemId = "q-high", State = (int)DispatcherQueueItemStates.Offered, Priority = 9, CreatedOn = now, AssignedUserId = Me, Preview = "<b>Smoke</b> in hallway" }
				}
			});
			M<IDispatcherBreaksService>().Setup(x => x.GetCoverageAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), DispatcherDeskController.BoardSlotMinutes))
				.ReturnsAsync(new DispatcherCoverage { Slots = new List<DispatcherCoverageSlot> { new DispatcherCoverageSlot { StartUtc = now.AddMinutes(-5), EndUtc = now.AddMinutes(10), OnDuty = 2, OnBreak = 1, Minimum = 2 } } });

			var board = (await Build().Board()).Should().BeOfType<JsonResult>().Which.Value.Should().BeOfType<DispatcherDeskBoardJson>().Subject;

			board.BelowMinimum.Should().BeTrue();
			board.Members.Select(x => x.UserId).Should().Equal(new[] { Me, Colleague }, "available dispatchers come first");
			var onBreak = board.Members.Single(x => x.UserId == Colleague);
			onBreak.Name.Should().Be("Col League", "a member without a name gets the department's name for them");
			onBreak.BreakType.Should().Be((int)DispatcherBreakTypes.Meal);
			onBreak.BreakDueBackMs.Should().Be(DispatcherDeskController.ToMs(now.AddMinutes(25)));
			onBreak.DeviceKind.Should().Be("phone");
			board.Queue.Select(x => x.ItemId).Should().Equal(new[] { "q-high", "q-low" }, "closed items are left out and higher priority goes first");
			board.Queue.First().AssignedToMe.Should().BeTrue();
			board.Queue.First().Preview.Should().Be("Smoke in hallway");
			board.Coverage.Single().BelowMinimum.Should().BeTrue();
			board.Coverage.Single().IsNow.Should().BeTrue();
		}

		[Test]
		public async Task Ending_another_dispatchers_break_needs_a_supervisor()
		{
			AsDispatcher();
			var controller = Build();

			var refused = ActionJson(await controller.EndBreak(Colleague, CancellationToken.None));
			refused.Success.Should().BeFalse();
			refused.Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));
			refused.Message.Should().Be("Error.NotPermitted");
			M<IDispatcherBreaksService>().Verify(x => x.EndBreakAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DispatcherBreakEndReasons>(), It.IsAny<CancellationToken>()), Times.Never);

			M<IDispatcherBreaksService>().Setup(x => x.EndBreakAsync(DepartmentId, It.IsAny<string>(), Me, It.IsAny<DispatcherBreakEndReasons>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherBreak>.Ok(new DispatcherBreak()));

			ActionJson(await controller.EndBreak(Me, CancellationToken.None)).Success.Should().BeTrue();
			M<IDispatcherBreaksService>().Verify(x => x.EndBreakAsync(DepartmentId, Me, Me, DispatcherBreakEndReasons.Returned, It.IsAny<CancellationToken>()), Times.Once);

			AsSupervisor();
			ActionJson(await Build().EndBreak(Colleague, CancellationToken.None)).Success.Should().BeTrue();
			M<IDispatcherBreaksService>().Verify(x => x.EndBreakAsync(DepartmentId, Colleague, Me, DispatcherBreakEndReasons.Supervisor, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Setting_another_dispatcher_away_or_available_needs_a_supervisor()
		{
			AsDispatcher();
			var controller = Build();

			ActionJson(await controller.SetAway(Colleague, "x", CancellationToken.None)).Success.Should().BeFalse();
			ActionJson(await controller.SetAvailable(Colleague, CancellationToken.None)).Success.Should().BeFalse();
			M<IDispatcherPresenceService>().Verify(x => x.SetAwayAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IDispatcherPresenceService>().Verify(x => x.SetAvailableAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			AsSupervisor();
			M<IDispatcherPresenceService>().Setup(x => x.SetAwayAsync(DepartmentId, Colleague, "Phone call", Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.NotSignedIn));

			var json = ActionJson(await Build().SetAway(Colleague, "  Phone call  ", CancellationToken.None));
			json.Success.Should().BeFalse();
			json.Error.Should().Be(nameof(DispatcherDeskErrors.NotSignedIn), "the service's refusal is passed on");
		}

		[Test]
		public async Task Releasing_or_transferring_an_item_needs_its_holder_or_a_supervisor()
		{
			AsDispatcher();
			M<IDispatcherRoutingService>().Setup(x => x.GetOpenItemsAsync(DepartmentId)).ReturnsAsync(new List<DispatcherQueueItem>
			{
				new DispatcherQueueItem { DispatcherQueueItemId = "theirs", DepartmentId = DepartmentId, AssignedUserId = Colleague, State = (int)DispatcherQueueItemStates.Accepted },
				new DispatcherQueueItem { DispatcherQueueItemId = "mine", DepartmentId = DepartmentId, AssignedUserId = Me, State = (int)DispatcherQueueItemStates.Accepted }
			});
			M<IDispatcherRoutingService>().Setup(x => x.ReleaseAsync(DepartmentId, "mine", Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Ok(new DispatcherQueueItem()));
			var controller = Build();

			ActionJson(await controller.ReleaseItem("theirs", CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));
			ActionJson(await controller.TransferItem("theirs", Me, null, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));
			ActionJson(await controller.ReleaseItem("gone", CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.ItemNotFound));
			ActionJson(await controller.ReleaseItem("mine", CancellationToken.None)).Success.Should().BeTrue();

			M<IDispatcherRoutingService>().Verify(x => x.ReleaseAsync(It.IsAny<int>(), "theirs", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IDispatcherRoutingService>().Verify(x => x.TransferAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			AsSupervisor();
			M<IDispatcherRoutingService>().Setup(x => x.TransferAsync(DepartmentId, "theirs", Me, Me, "cover", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Ok(new DispatcherQueueItem()));

			ActionJson(await Build().TransferItem("theirs", Me, "cover", CancellationToken.None)).Success.Should().BeTrue();
		}

		#endregion Board and access

		#region Breaks

		private void SelfSchedulingShift(int shiftId, bool allowed) =>
			M<IShiftDispatchersService>().Setup(x => x.GetSettingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<ShiftDispatchSetting>
			{
				new ShiftDispatchSetting { ShiftId = shiftId, IsEnabled = true, AllowSelfScheduledBreaks = allowed }
			});

		[Test]
		public async Task A_dispatcher_plans_their_own_break_only_on_a_shift_that_allows_it()
		{
			AsDispatcher();
			SelfSchedulingShift(8, false);
			M<IShiftDispatchersService>().Setup(x => x.GetDayRosterAsync(DepartmentId, 70)).ReturnsAsync(new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = Me, ShiftId = 8, ShiftDayId = 70 },
				new DispatcherDutyWindow { UserId = Colleague, ShiftId = 8, ShiftDayId = 70 }
			});
			DispatcherBreak saved = null;
			M<IDispatcherBreaksService>().Setup(x => x.SaveBreakAsync(DepartmentId, It.IsAny<DispatcherBreak>(), Me, It.IsAny<CancellationToken>()))
				.Callback((int _, DispatcherBreak b, string __, CancellationToken ___) => saved = b)
				.ReturnsAsync((int _, DispatcherBreak b, string __, CancellationToken ___) => DispatcherDeskResult<DispatcherBreak>.Ok(b, new List<DispatcherDeskWarning>
				{
					new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.CoverageBelowMinimum, Available = 0, Minimum = 1, StartUtc = b.ScheduledStartOn, EndUtc = b.ScheduledEndOn() }
				}));

			var input = new DispatcherBreakEditInput { ShiftDayId = 70, BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, Start = "2026-11-20T13:30", Note = " lunch " };

			ActionJson(await Build().SaveBreak(input, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));
			saved.Should().BeNull();

			SelfSchedulingShift(8, true);
			var json = ActionJson(await Build().SaveBreak(input, CancellationToken.None));

			json.Success.Should().BeTrue();
			json.Warnings.Should().ContainSingle("the coverage warning comes back with the saved break");
			saved.UserId.Should().Be(Me);
			saved.ShiftId.Should().Be(8);
			saved.ShiftDayId.Should().Be(70);
			saved.BreakType.Should().Be((int)DispatcherBreakTypes.Meal);
			saved.DurationMinutes.Should().Be(30);
			saved.State.Should().Be((int)DispatcherBreakStates.Scheduled);
			saved.Note.Should().Be("lunch");
			saved.ScheduledStartOn.Should().Be(new DateTime(2026, 11, 20, 13, 30, 0).DepartmentLocalToUtc(_department), "the form's time is department-local");

			saved = null;
			input.UserId = Colleague;
			ActionJson(await Build().SaveBreak(input, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted), "another dispatcher's break needs a supervisor");
			saved.Should().BeNull();

			AsSupervisor();
			ActionJson(await Build().SaveBreak(input, CancellationToken.None)).Success.Should().BeTrue();
			saved.UserId.Should().Be(Colleague);
		}

		[Test]
		public async Task SaveBreak_refuses_bad_input_and_a_break_that_already_started()
		{
			AsSupervisor();
			var controller = Build();

			ActionJson(await controller.SaveBreak(new DispatcherBreakEditInput { DurationMinutes = 15, Start = "tomorrow" }, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.InvalidInput));
			ActionJson(await controller.SaveBreak(new DispatcherBreakEditInput { DurationMinutes = 0, Start = "2026-11-20T10:00" }, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.InvalidInput));
			ActionJson(await controller.SaveBreak(new DispatcherBreakEditInput { DurationMinutes = 241, Start = "2026-11-20T10:00" }, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.InvalidInput));

			M<IDispatcherBreaksService>().Setup(x => x.GetBreakAsync(DepartmentId, "active")).ReturnsAsync(new DispatcherBreak { DispatcherBreakId = "active", DepartmentId = DepartmentId, State = (int)DispatcherBreakStates.Active });
			M<IDispatcherBreaksService>().Setup(x => x.GetBreakAsync(DepartmentId, "elsewhere")).ReturnsAsync(new DispatcherBreak { DispatcherBreakId = "elsewhere", DepartmentId = 99 });

			ActionJson(await controller.SaveBreak(new DispatcherBreakEditInput { DispatcherBreakId = "active", DurationMinutes = 15, Start = "2026-11-20T10:00" }, CancellationToken.None))
				.Error.Should().Be(nameof(DispatcherDeskErrors.BreakNotStartable));
			ActionJson(await controller.SaveBreak(new DispatcherBreakEditInput { DispatcherBreakId = "elsewhere", DurationMinutes = 15, Start = "2026-11-20T10:00" }, CancellationToken.None))
				.Error.Should().Be(nameof(DispatcherDeskErrors.BreakNotFound));
			ActionJson(await controller.CancelBreak("elsewhere", CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.BreakNotFound));

			M<IDispatcherBreaksService>().Verify(x => x.SaveBreakAsync(It.IsAny<int>(), It.IsAny<DispatcherBreak>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IDispatcherBreaksService>().Verify(x => x.CancelBreakAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Planning_your_own_break_needs_current_dispatch_access()
		{
			// Shift_View alone, on a self-scheduling shift, without the Dispatch app login any more.
			SelfSchedulingShift(8, true);
			M<IShiftDispatchersService>().Setup(x => x.GetDayRosterAsync(DepartmentId, 70)).ReturnsAsync(new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = Me, ShiftId = 8, ShiftDayId = 70 }
			});
			M<IDispatcherBreaksService>().Setup(x => x.GetBreakAsync(DepartmentId, "mine"))
				.ReturnsAsync(new DispatcherBreak { DispatcherBreakId = "mine", DepartmentId = DepartmentId, UserId = Me, ShiftId = 8, State = (int)DispatcherBreakStates.Scheduled });

			var input = new DispatcherBreakEditInput { ShiftDayId = 70, DurationMinutes = 15, Start = "2026-11-20T13:30" };
			ActionJson(await Build().SaveBreak(input, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));
			ActionJson(await Build().CancelBreak("mine", CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));

			M<IDispatcherBreaksService>().Verify(x => x.SaveBreakAsync(It.IsAny<int>(), It.IsAny<DispatcherBreak>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IDispatcherBreaksService>().Verify(x => x.CancelBreakAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_unchanged_start_keeps_the_stored_instant_in_the_repeated_hour()
		{
			// 2026-11-01 01:30 happens twice in New York (EDT, then EST). Whichever instant the conversion picks, the stored one is the other.
			AsSupervisor();
			var local = new DateTime(2026, 11, 1, 1, 30, 0);
			var converted = local.DepartmentLocalToUtc(_department);
			var stored = converted == new DateTime(2026, 11, 1, 5, 30, 0) ? new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc) : new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc);
			M<IDispatcherBreaksService>().Setup(x => x.GetBreakAsync(DepartmentId, "dst")).ReturnsAsync(new DispatcherBreak
			{
				DispatcherBreakId = "dst", DepartmentId = DepartmentId, UserId = Colleague, ShiftId = 8, State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = stored, DurationMinutes = 15
			});
			DispatcherBreak saved = null;
			M<IDispatcherBreaksService>().Setup(x => x.SaveBreakAsync(DepartmentId, It.IsAny<DispatcherBreak>(), Me, It.IsAny<CancellationToken>()))
				.Callback((int _, DispatcherBreak b, string __, CancellationToken ___) => saved = b)
				.ReturnsAsync((int _, DispatcherBreak b, string __, CancellationToken ___) => DispatcherDeskResult<DispatcherBreak>.Ok(b));

			ActionJson(await Build().SaveBreak(new DispatcherBreakEditInput { DispatcherBreakId = "dst", DurationMinutes = 15, Start = "2026-11-01T01:30", Note = "note only" },
				CancellationToken.None)).Success.Should().BeTrue();
			saved.ScheduledStartOn.Should().Be(stored, "a note-only edit does not move the break by an hour");

			ActionJson(await Build().SaveBreak(new DispatcherBreakEditInput { DispatcherBreakId = "dst", DurationMinutes = 15, Start = "2026-11-01T03:00" }, CancellationToken.None))
				.Success.Should().BeTrue();
			saved.ScheduledStartOn.Should().Be(new DateTime(2026, 11, 1, 3, 0, 0).DepartmentLocalToUtc(_department), "a changed start is converted as before");
		}

		[Test]
		public async Task Dates_that_cannot_form_a_window_fall_back_to_the_default_range()
		{
			AsSupervisor();
			M<IDispatcherRoutingService>().Setup(x => x.GetStatsAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new DispatcherQueueStats());

			// "to" at the first day there is, with no "from": a week back from it cannot be computed, so today's week is shown.
			var activity = (await Build().Activity(null, "0001-01-01")).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DispatcherDeskActivityView>().Subject;
			DateTime.Parse(activity.To).Year.Should().BeGreaterThan(2000);
			(await Build().Activity("9999-12-31", null)).Should().BeOfType<ViewResult>();

			// The planner adds a day for the end of a local date: the last day there is cannot have one.
			(await Build().BreaksData("9999-12-31", null)).Should().BeOfType<JsonResult>();
			(await Build().Breaks("9999-12-31", null)).Should().BeOfType<ViewResult>();
			(await Build().BreaksData("0001-01-01", null)).Should().BeOfType<JsonResult>();

			ActionJson(await Build().SaveBreak(new DispatcherBreakEditInput { DurationMinutes = 15, Start = "9999-12-31T23:59" }, CancellationToken.None))
				.Error.Should().Be(nameof(DispatcherDeskErrors.InvalidInput));
		}

		[Test]
		public async Task PlanBreaks_is_for_supervisors_on_the_departments_own_shift_day()
		{
			AsDispatcher();
			M<IShiftsService>().Setup(x => x.GetShiftDayByIdAsync(70)).ReturnsAsync(new ShiftDay { ShiftDayId = 70, ShiftId = 8 });
			M<IShiftsService>().Setup(x => x.GetShiftByIdAsync(8)).ReturnsAsync(new Shift { ShiftId = 8, DepartmentId = DepartmentId });
			M<IShiftsService>().Setup(x => x.GetShiftDayByIdAsync(71)).ReturnsAsync(new ShiftDay { ShiftDayId = 71, ShiftId = 9 });
			M<IShiftsService>().Setup(x => x.GetShiftByIdAsync(9)).ReturnsAsync(new Shift { ShiftId = 9, DepartmentId = 99 });

			ActionJson(await Build().PlanBreaks(70, false, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.NotPermitted));

			AsSupervisor();
			ActionJson(await Build().PlanBreaks(71, false, CancellationToken.None)).Error.Should().Be(nameof(DispatcherDeskErrors.ShiftNotFound));

			M<IDispatcherBreaksService>().Setup(x => x.PlanBreaksAsync(DepartmentId, 70, true, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<List<DispatcherBreak>>.Ok(new List<DispatcherBreak> { new DispatcherBreak(), new DispatcherBreak() }));

			var json = ActionJson(await Build().PlanBreaks(70, true, CancellationToken.None));
			json.Success.Should().BeTrue();
			json.Message.Should().Be("BreaksPlannedFormat");
			M<IDispatcherBreaksService>().Verify(x => x.PlanBreaksAsync(It.IsAny<int>(), 71, It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task BreaksData_lets_a_dispatcher_edit_only_their_own_row_on_a_self_scheduling_shift()
		{
			AsDispatcher();
			SelfSchedulingShift(8, true);
			var start = DateTime.UtcNow.Date.AddHours(12);
			M<IDispatcherBreaksService>().Setup(x => x.GetCoverageAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), DispatcherDeskController.PlannerSlotMinutes))
				.ReturnsAsync(new DispatcherCoverage
				{
					SlotMinutes = 5,
					Duty = new List<DispatcherDutyWindow>
					{
						new DispatcherDutyWindow { UserId = Me, ShiftId = 8, ShiftDayId = 70, ShiftName = "Days", ShiftColor = "#00ff00", StartUtc = start, EndUtc = start.AddHours(12) },
						new DispatcherDutyWindow { UserId = Colleague, ShiftId = 8, ShiftDayId = 70, ShiftName = "Days", ShiftColor = "red;background:url(x)", StartUtc = start, EndUtc = start.AddHours(12) }
					},
					Breaks = new List<DispatcherBreak>
					{
						new DispatcherBreak { DispatcherBreakId = "b1", UserId = Me, ShiftId = 8, State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = start.AddHours(2), DurationMinutes = 15 },
						new DispatcherBreak { DispatcherBreakId = "b2", UserId = Colleague, ShiftId = 8, State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = start.AddHours(3), DurationMinutes = 30 }
					},
					Warnings = new List<DispatcherDeskWarning> { new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.BreaksOverlap, UserIds = new List<string> { Me, Colleague } } }
				});

			var data = (await Build().BreaksData("2026-11-20", null)).Should().BeOfType<JsonResult>().Which.Value.Should().BeOfType<DispatcherBreaksJson>().Subject;

			data.Rows.Select(x => x.UserId).Should().Equal(new[] { Me, Colleague }, "your own row comes first");
			data.Rows[0].CanEdit.Should().BeTrue();
			data.Rows[0].Breaks.Single().CanEdit.Should().BeTrue();
			data.Rows[1].CanEdit.Should().BeFalse();
			data.Rows[1].Breaks.Single().CanEdit.Should().BeFalse();
			data.Rows[0].Duty.Single().Color.Should().Be("#00ff00");
			data.Rows[1].Duty.Single().Color.Should().BeNull("a colour that is not #rrggbb is not passed to the page");
			data.CanPlan.Should().BeFalse("planning a day needs a supervisor and a shift day");
			data.Warnings.Should().ContainSingle();
			data.ShiftDays.Single().ShiftDayId.Should().Be(70);
			data.Ticks.Should().NotBeEmpty();
		}

		#endregion Breaks

		#region Settings

		[Test]
		public async Task Settings_are_for_department_admins_only()
		{
			AsDispatcher();
			AsSupervisor();
			var controller = Build();

			ShouldBeRefused(await controller.Settings());
			ShouldBeRefused(await controller.Settings(new DispatcherDeskSettingsView { Enabled = true }, Form(), CancellationToken.None));
			M<IDispatcherDeskConfigService>().Verify(x => x.SaveConfigAsync(It.IsAny<int>(), It.IsAny<DispatcherDeskConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Settings_round_trip_through_the_service_which_normalizes_them()
		{
			DispatcherDeskConfig stored = new DispatcherDeskConfig();
			DispatcherDeskConfig posted = null;
			M<IDispatcherDeskConfigService>().Setup(x => x.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => stored);
			M<IDispatcherDeskConfigService>().Setup(x => x.SaveConfigAsync(DepartmentId, It.IsAny<DispatcherDeskConfig>(), Me, It.IsAny<CancellationToken>()))
				.Callback((int _, DispatcherDeskConfig c, string __, CancellationToken ___) => posted = c)
				.ReturnsAsync((int _, DispatcherDeskConfig c, string __, CancellationToken ___) => stored = c.Normalize());

			var model = new DispatcherDeskSettingsView
			{
				Enabled = true,
				RequireShift = false,
				OfferTimeoutSeconds = 5,
				MaxActiveItemsPerDispatcher = 40,
				EmailCallMode = (int)DispatcherEmailCallModes.Pending,
				TextOverflowMode = 7,
				AutoReturnAfterMinutes = 10,
				MinimumAvailableDispatchers = 2,
				RouteChat = false,
				SuppressAlertsOnBreak = false
			};
			var form = Form(new Dictionary<string, StringValues>
			{
				{ "breakPlanType_0", "2" }, { "breakPlanMinutes_0", "30" }, { "breakPlanOffset_0", "" },
				{ "breakPlanType_1", "1" }, { "breakPlanMinutes_1", "15" }, { "breakPlanOffset_1", "120" }
			});

			var result = await Build(DepartmentAdmin).Settings(model, form, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Settings");
			posted.Enabled.Should().BeTrue();
			posted.RequireShift.Should().BeFalse();
			posted.EmailCallMode.Should().Be((int)DispatcherEmailCallModes.Pending);
			posted.AutoReturnAfterMinutes.Should().Be(10);
			posted.RouteChat.Should().BeFalse();
			posted.SuppressAlertsOnBreak.Should().BeFalse();
			posted.DefaultBreakPlan.Should().BeEquivalentTo(new[]
			{
				new DispatcherDeskBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, TargetOffsetMinutes = -1 },
				new DispatcherDeskBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Break, DurationMinutes = 15, TargetOffsetMinutes = 120 }
			}, o => o.WithStrictOrdering(), "a blank target is stored as -1 (spread evenly)");

			var reloaded = (await Build(DepartmentAdmin).Settings()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DispatcherDeskSettingsView>().Subject;

			reloaded.OfferTimeoutSeconds.Should().Be(15, "the service clamps the offer timeout");
			reloaded.MaxActiveItemsPerDispatcher.Should().Be(10);
			reloaded.TextOverflowMode.Should().Be((int)DispatcherTextOverflowModes.PendingQueue, "an unknown mode reads back as the default");
			reloaded.EmailCallMode.Should().Be((int)DispatcherEmailCallModes.Pending);
			reloaded.AutoReturnAfterMinutes.Should().Be(10);
			reloaded.MinimumAvailableDispatchers.Should().Be(2);
			reloaded.DefaultBreakPlan.Select(x => x.TargetOffsetMinutes).Should().Equal(new int?[] { null, 120 });
		}

		[Test]
		public async Task Enhanced_AI_modes_need_the_add_on_but_a_department_keeps_the_mode_it_already_has()
		{
			var stored = new DispatcherDeskConfig { EmailCallMode = (int)DispatcherEmailCallModes.AutoDispatch };
			M<IDispatcherDeskConfigService>().Setup(x => x.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => stored);
			M<IEnhancedAiAccessService>().Setup(x => x.CanUseAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync(false);
			var model = new DispatcherDeskSettingsView { EmailCallMode = (int)DispatcherEmailCallModes.EnhancedAi };

			var refused = await Build(DepartmentAdmin).Settings(model, Form(), CancellationToken.None);

			var view = refused.Should().BeOfType<ViewResult>().Subject;
			view.ViewData.ModelState.IsValid.Should().BeFalse();
			((DispatcherDeskSettingsView)view.Model).EnhancedAiAvailable.Should().BeFalse();
			M<IDispatcherDeskConfigService>().Verify(x => x.SaveConfigAsync(It.IsAny<int>(), It.IsAny<DispatcherDeskConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			stored = new DispatcherDeskConfig { EmailCallMode = (int)DispatcherEmailCallModes.EnhancedAi };
			(await Build(DepartmentAdmin).Settings(model, Form(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			M<IDispatcherDeskConfigService>().Verify(x => x.SaveConfigAsync(DepartmentId, It.Is<DispatcherDeskConfig>(c => c.EmailCallMode == (int)DispatcherEmailCallModes.EnhancedAi), Me, It.IsAny<CancellationToken>()), Times.Once);

			M<IEnhancedAiAccessService>().Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDispatchTemplate)).ReturnsAsync(true);
			stored = new DispatcherDeskConfig();
			(await Build(DepartmentAdmin).Settings(new DispatcherDeskSettingsView { TextOverflowMode = (int)DispatcherTextOverflowModes.EnhancedAi }, Form(), CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>("a department with the add-on may switch to it");
		}

		#endregion Settings

		#region Activity

		[Test]
		public async Task Activity_is_for_supervisors_and_admins_and_covers_whole_local_days()
		{
			AsDispatcher();
			ShouldBeRefused(await Build().Activity(null, null));
			M<IDispatcherRoutingService>().Verify(x => x.GetStatsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);

			AsSupervisor();
			M<IDispatcherRoutingService>().Setup(x => x.GetStatsAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new DispatcherQueueStats
			{
				Created = 4,
				CreatedByKind = new Dictionary<int, int> { { (int)DispatcherQueueItemKinds.Text, 3 }, { (int)DispatcherQueueItemKinds.PendingCall, 1 } },
				Dispatchers = new List<DispatcherDeskStats> { new DispatcherDeskStats { UserId = Colleague, Accepted = 2 } }
			});

			var model = (await Build().Activity("2026-11-20", "2026-11-01")).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DispatcherDeskActivityView>().Subject;

			model.From.Should().Be("2026-11-01", "a reversed range is put the right way round");
			model.To.Should().Be("2026-11-20");
			model.CreatedByKind.Select(x => x.Key).Should().Equal((int)DispatcherQueueItemKinds.PendingCall, (int)DispatcherQueueItemKinds.Text);
			model.Dispatchers.Single().Name.Should().Be("Col League");
			M<IDispatcherRoutingService>().Verify(x => x.GetStatsAsync(DepartmentId,
				new DateTime(2026, 11, 1).DepartmentLocalToUtc(_department), new DateTime(2026, 11, 21).DepartmentLocalToUtc(_department)), Times.Once);

			var clamped = (await Build().Activity("2025-01-01", "2026-11-20")).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DispatcherDeskActivityView>().Subject;
			clamped.From.Should().Be(new DateTime(2026, 11, 20).AddDays(-(DispatcherDeskController.MaxActivityDays - 1)).ToString("yyyy-MM-dd"));
		}

		#endregion Activity

		#region Forms

		[Test]
		public void Break_plan_rows_are_read_in_order_bounded_and_typed()
		{
			var form = Form(new Dictionary<string, StringValues>
			{
				{ "breakPlanMinutes_10", "20" }, { "breakPlanType_10", "4" },
				{ "breakPlanMinutes_2", "999" }, { "breakPlanType_2", "77" }, { "breakPlanOffset_2", "-5" },
				{ "breakPlanMinutes_3", "abc" },
				{ "breakPlanMinutes_1", "10" }, { "breakPlanOffset_1", "99999" }
			});

			var plan = DispatcherDeskForms.ReadBreakPlan(form);

			plan.Select(x => x.DurationMinutes).Should().Equal(10, DispatcherDeskForms.MaxBreakMinutes, 20);
			plan.Select(x => x.BreakType).Should().Equal((int)DispatcherBreakTypes.Break, (int)DispatcherBreakTypes.Break, (int)DispatcherBreakTypes.Other);
			plan.Select(x => x.TargetOffsetMinutes).Should().Equal(DispatcherDeskForms.MaxTargetOffsetMinutes, null, null);
		}

		[Test]
		public void Standing_roster_reads_each_person_once_with_the_supervisor_flag()
		{
			var roster = DispatcherDeskForms.ReadStandingRoster(Form(new Dictionary<string, StringValues>
			{
				{ "dispatchRoster", new StringValues(new[] { "a", "b", "A" }) },
				{ "dispatchSupervisor", new StringValues(new[] { "b", "c" }) }
			}));

			roster.Should().BeEquivalentTo(new[] { ("a", false), ("b", true), ("c", true) }, o => o.WithStrictOrdering());
		}

		#endregion Forms
	}
}
