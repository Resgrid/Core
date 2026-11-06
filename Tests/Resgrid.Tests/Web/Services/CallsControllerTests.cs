using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class CallsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "call-viewer";

		private Mock<ICallsService> _callsService;
		private Mock<IAuthorizationService> _authorizationService;
		private Mock<IProtocolsService> _protocolsService;
		private Mock<IDepartmentDataProtectionService> _dataProtectionService;
		private Mock<IProtectedReadService> _protectedCallReadService;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private Mock<IDispatchScopeService> _dispatchScope;
		private Mock<IMappingService> _mappingService;
		private Mock<IDepartmentsService> _departmentsService;
		private CallsController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_callsService = new Mock<ICallsService>();
			_authorizationService = new Mock<IAuthorizationService>();
			_protocolsService = new Mock<IProtocolsService>();
			_dataProtectionService = new Mock<IDepartmentDataProtectionService>();

			// Pass-through protected reads: these tests exercise unprotected departments, where the
			// resolver returns the calls untouched.
			_protectedCallReadService = new Mock<IProtectedReadService>();
			_protectedCallReadService
				.Setup(x => x.ResolveForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<Call>>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns<int, IReadOnlyList<Call>, string, string, CancellationToken>((d, calls, g, u, ct) =>
					Task.FromResult<IReadOnlyList<ProtectedReadResult>>(
						(calls ?? new List<Call>()).Select(c => new ProtectedReadResult { Call = c }).ToList()));
			_protectedCallReadService
				.Setup(x => x.ResolveForReadAsync(It.IsAny<int>(), It.IsAny<Call>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns<int, Call, string, string, CancellationToken>((d, call, g, u, ct) =>
					Task.FromResult(new ProtectedReadResult { Call = call }));

			// Unprotected department: every write goes through unchanged.
			_protectedWriteService = new Mock<IProtectedWriteService>();
			_protectedWriteService
				.Setup(x => x.PrepareCallWriteAsync(It.IsAny<int>(), It.IsAny<Call>(), It.IsAny<Call>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });

			_dispatchScope = PassThroughDispatchScope();
			_mappingService = new Mock<IMappingService>();
			_departmentsService = new Mock<IDepartmentsService>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("CallsControllerTests").Start();

			_controller = new CallsController(
				_callsService.Object,
				_departmentsService.Object,
				Mock.Of<IUserProfileService>(),
				Mock.Of<IGeoLocationProvider>(),
				_authorizationService.Object,
				Mock.Of<IQueueService>(),
				Mock.Of<IUsersService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IActionLogsService>(),
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(),
				_protocolsService.Object,
				Mock.Of<IEventAggregator>(),
				Mock.Of<ICustomStateService>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IShiftsService>(),
				_mappingService.Object,
				Mock.Of<IUserDefinedFieldsService>(),
				Mock.Of<ICommunicationService>(),
				Mock.Of<IWeatherAlertService>(),
				Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(),
				Mock.Of<IFeatureToggleService>(),
				_dataProtectionService.Object,
				_protectedCallReadService.Object,
				_protectedWriteService.Object,
				Mock.Of<IContactsService>(),
				_dispatchScope.Object,
				Mock.Of<ICallLocationHistoryService>(),
				Mock.Of<IPendingCallsService>(),
				Mock.Of<ICallClosureService>(),
				new Resgrid.Services.InboundEmailReceiptsService(new Resgrid.Tests.Mocks.MockInboundEmailReceiptsRepository(), TimeProvider.System))
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("u3246")]
		[TestCase("2147483648")]
		public async Task GetCall_ReturnsBadRequest_WhenCallIdIsInvalid(string callId)
		{
			var response = await _controller.GetCall(callId);

			response.Result.Should().BeOfType<BadRequestResult>();
			_callsService.Verify(
				service => service.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()),
				Times.Never);
		}

		/// <summary>Group-scoped dispatch off: every call list comes back unchanged.</summary>
		private static Mock<IDispatchScopeService> PassThroughDispatchScope()
		{
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(x => x.FilterCallsForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<Call>>()))
				.ReturnsAsync((int departmentId, string userId, List<Call> calls) => calls);
			scope.Setup(x => x.CanUserAccessCallAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Call>()))
				.ReturnsAsync(true);
			return scope;
		}

		[Test]
		public async Task GetCalls_LeavesOut_CallsOutsideTheCallersDispatchScope()
		{
			var departmentCalls = new List<Call>
			{
				new Call { CallId = 1, DepartmentId = DepartmentId, Name = "Out of area", LoggedOn = DateTime.UtcNow }
			};
			_callsService.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(departmentCalls);
			_dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, departmentCalls))
				.ReturnsAsync(new List<Call>());
			_mappingService.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>());

			var response = await _controller.GetCalls(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

			var result = (response.Result as OkObjectResult)?.Value as ActiveCallsResult;
			result.Should().NotBeNull();
			result.Data.Should().BeEmpty();
			_dispatchScope.Verify(x => x.FilterCallsForUserAsync(DepartmentId, UserId, departmentCalls), Times.Once);
		}

		[Test]
		public async Task GetCallYears_ReturnsDepartmentYearsInClientEnvelope()
		{
			_callsService.Setup(x => x.GetCallYearsByDeptartmentAsync(DepartmentId)).ReturnsAsync(["2026", "2024"]);

			var response = await _controller.GetCallYears();

			var result = (CallYearsResult)((OkObjectResult)response.Result).Value;
			result.Data.Should().Equal("2026", "2024");
			result.PageSize.Should().Be(2);
		}

		[Test]
		public async Task GetArchivedCalls_ReturnsScopedClosedCallsNewestFirst()
		{
			var earlier = new Call { CallId = 1, State = (int)CallStates.Closed, LoggedOn = new DateTime(2026, 1, 1) };
			var later = new Call { CallId = 2, State = (int)CallStates.FalseAlarm, LoggedOn = new DateTime(2026, 10, 1, 19, 15, 0), ClosedOn = new DateTime(2026, 10, 1, 20, 0, 0) };
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { TimeZone = "Central Standard Time" });
			var outOfScope = new Call { CallId = 3, State = (int)CallStates.Closed, LoggedOn = new DateTime(2026, 11, 1) };
			var pending = new Call { CallId = 4, State = (int)CallStates.Pending, LoggedOn = new DateTime(2026, 11, 1) };
			List<Call> departmentCalls = [earlier, later, outOfScope, pending];
			_callsService.Setup(x => x.GetClosedCallsByDepartmentYearAsync(DepartmentId, "2026")).ReturnsAsync(departmentCalls);
			_dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.IsAny<List<Call>>()))
				.ReturnsAsync((int departmentId, string userId, List<Call> calls) => calls.Where(call => call != outOfScope).ToList());
			_mappingService.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync([]);
			ArchivedCallCounts();

			var response = await _controller.GetArchivedCalls(2026);

			var result = (ArchivedCallsResult)((OkObjectResult)response.Result).Value;
			result.Data.Select(call => call.CallId).Should().Equal("2", "1");
			result.PageSize.Should().Be(2);
			result.Page.Should().Be(0);
			result.HasMore.Should().BeFalse();
			// The state test runs first, so a pending call never costs a scope check.
			_dispatchScope.Verify(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.Is<List<Call>>(calls => calls.Contains(pending))), Times.Never);
			result.Data[0].LoggedOn.Should().Be(new DateTime(2026, 10, 1, 14, 15, 0));
			result.Data[0].ClosedOn.Should().Be(new DateTime(2026, 10, 1, 15, 0, 0));
			result.Data[0].ClosedOnUtc.Should().Be(later.ClosedOn);
			result.Data[1].ClosedOnUtc.Should().BeNull();
		}

		[Test]
		public async Task GetArchivedCalls_PagesTheScopedYearAndCountsNotesAndAttachmentsOnlyForThePage()
		{
			// Five closed calls, newest is CallId 5; pages of two.
			var calls = Enumerable.Range(1, 5)
				.Select(id => new Call { CallId = id, State = (int)CallStates.Closed, LoggedOn = new DateTime(2026, 1, id) })
				.ToList();
			_callsService.Setup(x => x.GetClosedCallsByDepartmentYearAsync(DepartmentId, "2026")).ReturnsAsync(calls);
			_mappingService.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync([]);
			var countRequests = ArchivedCallCounts(new CallContentCounts { CallId = 3, NoteCount = 2, AudioCount = 1, ImageCount = 2, FileCount = 1 });

			var first = (ArchivedCallsResult)((OkObjectResult)(await _controller.GetArchivedCalls(2026, 0, 2)).Result).Value;
			var second = (ArchivedCallsResult)((OkObjectResult)(await _controller.GetArchivedCalls(2026, 1, 2)).Result).Value;
			var last = (ArchivedCallsResult)((OkObjectResult)(await _controller.GetArchivedCalls(2026, 2, 2)).Result).Value;
			var past = (ArchivedCallsResult)((OkObjectResult)(await _controller.GetArchivedCalls(2026, 3, 2)).Result).Value;

			first.Data.Select(call => call.CallId).Should().Equal("5", "4");
			first.HasMore.Should().BeTrue();
			second.Data.Select(call => call.CallId).Should().Equal("3", "2");
			second.Page.Should().Be(1);
			second.PageSize.Should().Be(2);
			second.HasMore.Should().BeTrue();
			last.Data.Select(call => call.CallId).Should().Equal("1");
			last.HasMore.Should().BeFalse();
			past.Data.Should().BeEmpty();
			past.HasMore.Should().BeFalse();

			var counted = second.Data[0];
			counted.NotesCount.Should().Be(2);
			counted.AudioCount.Should().Be(1);
			counted.ImgagesCount.Should().Be(2);
			counted.FileCount.Should().Be(1);
			second.Data[1].NotesCount.Should().Be(0, "a call with no notes or attachments has no counts row");

			// One counts query per page, for that page's calls only; no note or attachment rows are loaded.
			countRequests.Should().BeEquivalentTo(new[] { new[] { 5, 4 }, new[] { 3, 2 }, new[] { 1 } }, options => options.WithStrictOrdering());
			_callsService.Verify(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
			_protectedCallReadService.Verify(x => x.ResolveForReadAsync(DepartmentId,
				It.Is<IReadOnlyList<Call>>(page => page.Count <= 2), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
		}

		[TestCase(0)]
		[TestCase(10000)]
		public async Task GetArchivedCalls_RejectsInvalidYearBeforeQuerying(int year)
		{
			var response = await _controller.GetArchivedCalls(year);

			response.Result.Should().BeOfType<BadRequestObjectResult>();
			_callsService.Verify(x => x.GetClosedCallsByDepartmentYearAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[TestCase(-1, 50)]
		[TestCase(0, 0)]
		[TestCase(0, CallsController.MaxArchivedCallsPageSize + 1)]
		public async Task GetArchivedCalls_RejectsAnInvalidPageBeforeQuerying(int page, int pageSize)
		{
			var response = await _controller.GetArchivedCalls(2026, page, pageSize);

			response.Result.Should().BeOfType<BadRequestObjectResult>();
			_callsService.Verify(x => x.GetClosedCallsByDepartmentYearAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetArchivedCalls_AFarPageDoesNotOverflowIntoTheFirstPage()
		{
			_callsService.Setup(x => x.GetClosedCallsByDepartmentYearAsync(DepartmentId, "2026"))
				.ReturnsAsync([new Call { CallId = 1, State = (int)CallStates.Closed, LoggedOn = new DateTime(2026, 1, 1) }]);
			_mappingService.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync([]);
			ArchivedCallCounts();

			var result = (ArchivedCallsResult)((OkObjectResult)(await _controller.GetArchivedCalls(2026, int.MaxValue, CallsController.MaxArchivedCallsPageSize)).Result).Value;

			result.Data.Should().BeEmpty();
			result.HasMore.Should().BeFalse();
		}

		[Test]
		public async Task GetArchivedCalls_ScopesNewestFirstAndStopsOnceThePageAndOneMoreAreFound()
		{
			// Forty closed calls, newest is CallId 40. The scope keeps only even ids, so the first page of three
			// needs the four newest even calls: 40, 38, 36 and 34 to know another page follows.
			var calls = Enumerable.Range(1, 40)
				.Select(id => new Call { CallId = id, State = (int)CallStates.Closed, LoggedOn = new DateTime(2026, 1, 1).AddHours(id) })
				.ToList();
			var checkedIds = new List<int>();
			_callsService.Setup(x => x.GetClosedCallsByDepartmentYearAsync(DepartmentId, "2026")).ReturnsAsync(calls);
			_dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.IsAny<List<Call>>()))
				.ReturnsAsync((int departmentId, string userId, List<Call> chunk) =>
				{
					checkedIds.AddRange(chunk.Select(call => call.CallId));
					return chunk.Where(call => call.CallId % 2 == 0).ToList();
				});
			_mappingService.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync([]);
			ArchivedCallCounts();

			var result = (ArchivedCallsResult)((OkObjectResult)(await _controller.GetArchivedCalls(2026, 0, 3)).Result).Value;

			result.Data.Select(call => call.CallId).Should().Equal("40", "38", "36");
			result.HasMore.Should().BeTrue();
			// Chunks of four then eight: calls 40 down to 29 are checked, each once, and the older 28 never are.
			checkedIds.Should().Equal(Enumerable.Range(29, 12).Reverse());
		}

		/// <summary>
		/// GetCallContentCountsAsync answers with the given rows for the calls asked about, as the grouped query does (a call with
		/// no notes or attachments has no row). Returns the call ids of each request, in order.
		/// </summary>
		private List<int[]> ArchivedCallCounts(params CallContentCounts[] counts)
		{
			var requests = new List<int[]>();
			_callsService
				.Setup(x => x.GetCallContentCountsAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int departmentId, IEnumerable<int> callIds, CancellationToken cancellationToken) =>
				{
					var ids = callIds.ToArray();
					requests.Add(ids);
					return counts.Where(count => ids.Contains(count.CallId)).ToDictionary(count => count.CallId);
				});
			return requests;
		}

		[Test]
		public async Task GetPendingCalls_ForADepartmentApiKey_ReturnsTheWholeDepartment()
		{
			// The key acts as the managing user, whose membership it does not depend on: per-user scope filtering could hide
			// every call from a valid key.
			_controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}, Resgrid.Web.Services.Middleware.DepartmentApiKeyAuthHandler.AuthenticationType));
			var pending = new List<Call>
			{
				new Call { CallId = 3, DepartmentId = DepartmentId, Name = "Follow-up", State = (int)CallStates.Pending, LoggedOn = DateTime.UtcNow }
			};
			_callsService.Setup(x => x.GetPendingCallsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(pending);
			_dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, pending)).ReturnsAsync(new List<Call>());
			_mappingService.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>());

			var response = await _controller.GetPendingCalls();

			var result = (response.Result as OkObjectResult)?.Value as PendingCallsResult;
			result.Should().NotBeNull();
			result.Data.Select(x => x.CallId).Should().Equal(new[] { "3" });
			_dispatchScope.Verify(x => x.FilterCallsForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<Call>>()), Times.Never);
		}

		[Test]
		public async Task GetCall_HydratesProtocols_UsingDispatchProtocolId()
		{
			var call = new Call
			{
				CallId = 42,
				DepartmentId = DepartmentId,
				Name = "Structure Fire",
				Address = "123 Main St",
				LoggedOn = DateTime.UtcNow,
				Protocols = new Collection<CallProtocol>
				{
					new CallProtocol { CallProtocolId = 999, CallId = 42, DispatchProtocolId = 5 }
				}
			};

			_callsService
				.Setup(service => service.GetCallByIdAsync(42, It.IsAny<bool>()))
				.ReturnsAsync(call);
			_callsService
				.Setup(service => service.PopulateCallData(call, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(call);
			_authorizationService
				.Setup(service => service.CanUserViewCallAsync(UserId, 42))
				.ReturnsAsync(true);
			_protocolsService
				.Setup(service => service.GetProtocolByIdAsync(5))
				.ReturnsAsync(new DispatchProtocol
				{
					DispatchProtocolId = 5,
					DepartmentId = DepartmentId,
					Name = "Fire Response",
					Code = "FIRE",
					Triggers = new Collection<DispatchProtocolTrigger>(),
					Attachments = new Collection<DispatchProtocolAttachment>(),
					Questions = new Collection<DispatchProtocolQuestion>()
				});

			var response = await _controller.GetCall("42");

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<CallResult>().Subject;
			result.Data.Protocols.Should().ContainSingle(p => p.Id == "5");

			_protocolsService.Verify(service => service.GetProtocolByIdAsync(5), Times.Once);
			_protocolsService.Verify(service => service.GetProtocolByIdAsync(999), Times.Never);
		}

		[Test]
		public async Task GetCall_ForASystemKeyNamingAnotherDepartment_TakesTheTypeIconFromThatDepartment()
		{
			const int otherDepartmentId = 77;
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "SystemApiKey"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

			var call = new Call { CallId = 42, DepartmentId = otherDepartmentId, Name = "Structure Fire", Type = "Fire", Address = "123 Main St", LoggedOn = DateTime.UtcNow };
			_callsService.Setup(service => service.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(call);
			_callsService
				.Setup(service => service.PopulateCallData(call, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(call);
			_callsService.Setup(service => service.GetCallTypesForDepartmentAsync(otherDepartmentId))
				.ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 5, DepartmentId = otherDepartmentId, Type = "Fire", IconKey = "0123456789abcdef0123456789abcdef" } });
			_callsService.Setup(service => service.GetCallTypesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 6, DepartmentId = DepartmentId, Type = "Fire", IconKey = "fedcba9876543210fedcba9876543210" } });

			var response = await _controller.GetCall("42", otherDepartmentId.ToString());

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<CallResult>().Subject;
			result.Data.TypeIconUrl.Should().EndWith("/api/v4/TypeIcons/0123456789abcdef0123456789abcdef",
				"the call belongs to the department the key named, so its type is that department's");
			_callsService.Verify(service => service.GetCallTypesForDepartmentAsync(DepartmentId), Times.Never);
		}

		private void UseBigBoardSession()
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(Resgrid.Model.Security.SessionClaimTypes.ClientApp,
						((int)UserSessionClientApplication.BigBoard).ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
		}

		private Call SetupProtectedCall()
		{
			var call = new Call
			{
				CallId = 42,
				DepartmentId = DepartmentId,
				Number = "2026-134",
				Name = "Cardiac Arrest - Smith Residence",
				NatureOfCall = "62yo male, CPR in progress",
				Address = "123 Main St",
				LoggedOn = DateTime.UtcNow
			};

			_callsService.Setup(service => service.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(call);
			_callsService
				.Setup(service => service.PopulateCallData(call, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(call);
			_authorizationService.Setup(service => service.CanUserViewCallAsync(UserId, 42)).ReturnsAsync(true);

			return call;
		}

		[Test]
		public async Task GetCall_ReturnsSafeShell_ForBigBoardSessionOfProtectedDepartment()
		{
			SetupProtectedCall();
			_dataProtectionService.Setup(x => x.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(true);
			UseBigBoardSession();

			var response = await _controller.GetCall("42");

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<CallResult>().Subject;
			// The call's name and address are operational (2026-10-17) and survive the shell; the private fields do not.
			result.Data.Name.Should().Be("Cardiac Arrest - Smith Residence");
			result.Data.Address.Should().Be("123 Main St");
			result.Data.Nature.Should().BeNull();
			result.Data.Note.Should().BeNull();
			result.Data.ContactName.Should().BeNull();
			result.Data.ContactInfo.Should().BeNull();
			result.Data.Number.Should().Be("2026-134", "the system-generated call number is allowlisted");
			result.Data.UdfValues.Should().BeNullOrEmpty("submitted UDF free text is suppressed for the shell");
		}

		[Test]
		public async Task GetCall_IsUnchangedForBigBoard_WhenDepartmentIsNotProtected()
		{
			SetupProtectedCall();
			_dataProtectionService.Setup(x => x.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(false);
			UseBigBoardSession();

			var response = await _controller.GetCall("42");

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<CallResult>().Subject;
			result.Data.Name.Should().Be("Cardiac Arrest - Smith Residence");
			result.Data.Address.Should().Be("123 Main St");
		}

		[Test]
		public async Task GetCall_IsUnchangedForAttendedClients_EvenWhenProtected()
		{
			SetupProtectedCall();
			_dataProtectionService.Setup(x => x.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(true);

			var response = await _controller.GetCall("42");

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<CallResult>().Subject;
			result.Data.Name.Should().Be("Cardiac Arrest - Smith Residence",
				"attended clients are gated by grants in a later phase, never by the BigBoard shell");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not-a-number")]
		[TestCase("2147483648")]
		public async Task EditCall_ReturnsBadRequest_WhenIdIsInvalid(string id)
		{
			var response = await _controller.EditCall(new EditCallInput { Id = id }, CancellationToken.None);

			response.Result.Should().BeOfType<BadRequestResult>();
			_authorizationService.Verify(
				service => service.CanUserEditCallAsync(It.IsAny<string>(), It.IsAny<int>()),
				Times.Never);
		}

		#region Delete and close each check their own permission

		private const int ManagedCallId = 77;

		/// <summary>An undispatched call in the caller's department (a dispatched call can't be deleted at all).</summary>
		private void SetupManagedCall()
		{
			var call = new Call { CallId = ManagedCallId, DepartmentId = DepartmentId, Name = "Welfare check", HasBeenDispatched = false };

			_callsService.Setup(x => x.GetCallByIdAsync(ManagedCallId, It.IsAny<bool>())).ReturnsAsync(call);
			_callsService.Setup(x => x.PopulateCallData(call, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(call);
			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call saved, CancellationToken ct) => saved);
		}

		private void SetupPermissions(bool canClose, bool canDelete)
		{
			_authorizationService.Setup(x => x.CanUserCloseCallAsync(UserId, ManagedCallId, DepartmentId)).ReturnsAsync(canClose);
			_authorizationService.Setup(x => x.CanUserDeleteCallAsync(UserId, ManagedCallId, DepartmentId)).ReturnsAsync(canDelete);
		}

		[Test]
		public async Task DeleteCall_IsRefused_WhenTheCallerMayCloseButNotDelete()
		{
			SetupManagedCall();
			SetupPermissions(canClose: true, canDelete: false);

			var response = await _controller.DeleteCall(ManagedCallId.ToString());

			response.Result.Should().BeOfType<UnauthorizedResult>();
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_authorizationService.Verify(x => x.CanUserCloseCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task DeleteCall_Succeeds_WhenTheCallerMayDeleteButNotClose()
		{
			SetupManagedCall();
			SetupPermissions(canClose: false, canDelete: true);

			var response = await _controller.DeleteCall(ManagedCallId.ToString());

			response.Result.Should().BeOfType<OkObjectResult>();
			_callsService.Verify(x => x.SaveCallAsync(It.Is<Call>(c => c.CallId == ManagedCallId && c.IsDeleted), It.IsAny<CancellationToken>()), Times.Once);
			_authorizationService.Verify(x => x.CanUserCloseCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task CloseCall_IsRefused_WhenTheCallerMayDeleteButNotClose()
		{
			SetupManagedCall();
			SetupPermissions(canClose: false, canDelete: true);

			var response = await _controller.CloseCall(new CloseCallInput { Id = ManagedCallId.ToString(), Type = (int)CallStates.Closed }, CancellationToken.None);

			response.Result.Should().BeOfType<UnauthorizedResult>();
			_callsService.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_authorizationService.Verify(x => x.CanUserDeleteCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task CloseCall_Succeeds_WhenTheCallerMayCloseButNotDelete()
		{
			SetupManagedCall();
			SetupPermissions(canClose: true, canDelete: false);

			var response = await _controller.CloseCall(new CloseCallInput { Id = ManagedCallId.ToString(), Type = (int)CallStates.Closed, Notes = "Resolved on scene" }, CancellationToken.None);

			response.Result.Should().BeOfType<OkObjectResult>();
			_callsService.Verify(x => x.SaveCallAsync(It.Is<Call>(c => c.CallId == ManagedCallId && c.State == (int)CallStates.Closed && c.ClosedByUserId == UserId),
				It.IsAny<CancellationToken>()), Times.Once);
			_authorizationService.Verify(x => x.CanUserDeleteCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		#endregion
	}
}
