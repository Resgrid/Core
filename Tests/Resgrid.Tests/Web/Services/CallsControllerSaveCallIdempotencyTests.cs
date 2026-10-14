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
using Resgrid.Model.Events;
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
	/// v4 SaveCall with an Idempotency-Key (the SMTP relay sends the email's Message-Id; M0271 receipts). The relay retries a
	/// message whose answer it never got, and the call row commits before the broadcast, so without the key every retry after a
	/// late failure created another call. With it a repeat returns the first call, a retry after a failure finishes only the
	/// steps that never ran, and a repeat while the first is still running is told to come back.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallsControllerSaveCallIdempotencyTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "dispatcher-1";

		private Mock<ICallsService> _callsService;
		private Mock<IQueueService> _queue;
		private Mock<IEventAggregator> _events;
		private Mock<IWeatherAlertService> _weather;
		private MockInboundEmailReceiptsRepository _receipts;
		private List<Call> _saved;
		private int _broadcastFailures;
		private int _broadcasts;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_saved = new List<Call>();
			_broadcastFailures = 0;
			_broadcasts = 0;
			_receipts = new MockInboundEmailReceiptsRepository();

			_callsService = new Mock<ICallsService>();
			_callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call call, CancellationToken _) =>
				{
					if (call.CallId <= 0)
					{
						call.CallId = 700 + _saved.Count;
						_saved.Add(call);
					}

					return call;
				});
			_callsService.Setup(x => x.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => _saved.Find(c => c.CallId == id));
			_callsService.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);

			_queue = new Mock<IQueueService>();
			_queue.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallQueueItem item, CancellationToken _) =>
				{
					if (_broadcastFailures > 0)
					{
						_broadcastFailures--;
						throw new InvalidOperationException("Failed to enqueue call broadcast for processing.");
					}

					_broadcasts++;
					return true;
				});

			_events = new Mock<IEventAggregator>();
			_weather = new Mock<IWeatherAlertService>();
			_activity = new Activity(nameof(CallsControllerSaveCallIdempotencyTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private CallsController Controller(string idempotencyKey)
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			if (idempotencyKey != null)
				httpContext.Request.Headers[CallsController.IdempotencyKeyHeader] = idempotencyKey;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			departments.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>());
			departments.Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "responder-1" } });

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetAllRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>());

			var protectedWrite = new Mock<IProtectedWriteService>();
			protectedWrite.Setup(x => x.PreflightWriteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			protectedWrite.Setup(x => x.PrepareCallWriteAsync(It.IsAny<int>(), It.IsAny<Call>(), It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });

			return new CallsController(_callsService.Object, departments.Object, profiles.Object, Mock.Of<IGeoLocationProvider>(), authorization.Object,
				_queue.Object, Mock.Of<IUsersService>(), units.Object, Mock.Of<IActionLogsService>(), groups.Object, roles.Object, Mock.Of<IProtocolsService>(),
				_events.Object, Mock.Of<ICustomStateService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IShiftsService>(), Mock.Of<IMappingService>(),
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<ICommunicationService>(), _weather.Object, Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentDataProtectionService>(),
				Mock.Of<IProtectedReadService>(), protectedWrite.Object, Mock.Of<IContactsService>(), Mock.Of<IDispatchScopeService>(),
				Mock.Of<ICallLocationHistoryService>(), Mock.Of<IPendingCallsService>(), Mock.Of<ICallClosureService>(),
				new InboundEmailReceiptsService(_receipts, TimeProvider.System))
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		private static NewCallInput Input() => new NewCallInput
		{
			Priority = 1,
			Name = "Structure Fire",
			Nature = "Smoke showing",
			DispatchList = "0",
			ExternalId = "msg-1@cad.example"
		};

		private static string Id(ActionResult<SaveCallResult> result) => ((result.Result as CreatedAtActionResult)?.Value as SaveCallResult)?.Id;

		private int CallAddedEvents() =>
			_events.Invocations.Count(i => i.Method.Name == nameof(IEventAggregator.SendMessage) && i.Arguments[0] is CallAddedEvent);

		[Test]
		public async Task A_keyed_call_sent_again_returns_the_first_call_without_saving_or_paging_again()
		{
			var first = await Controller("msg-1@cad.example").SaveCall(Input(), CancellationToken.None);
			var again = await Controller("msg-1@cad.example").SaveCall(Input(), CancellationToken.None);

			Id(first).Should().Be("700");
			Id(again).Should().Be("700");
			_saved.Should().ContainSingle();
			_broadcasts.Should().Be(1);
			CallAddedEvents().Should().Be(1);
			_receipts.Receipts.Should().ContainSingle().Which.State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task A_retry_after_the_broadcast_failed_pages_the_saved_call_once_and_repeats_nothing_else()
		{
			_broadcastFailures = 1;

			await Controller("msg-1@cad.example").Awaiting(c => c.SaveCall(Input(), CancellationToken.None))
				.Should().ThrowAsync<InvalidOperationException>();

			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Saved);
			receipt.EntityId.Should().Be(700);
			((InboundEmailPendingSteps)receipt.PendingSteps).Should().HaveFlag(InboundEmailPendingSteps.CallBroadcast);
			((InboundEmailPendingSteps)receipt.PendingSteps).Should().NotHaveFlag(InboundEmailPendingSteps.CallAddedEvent);
			((InboundEmailPendingSteps)receipt.PendingSteps).Should().NotHaveFlag(InboundEmailPendingSteps.WeatherAlerts);
			receipt.ClaimedOn.Should().BeNull("the failed attempt gave its claim back");

			var retry = await Controller("msg-1@cad.example").SaveCall(Input(), CancellationToken.None);

			Id(retry).Should().Be("700");
			_saved.Should().ContainSingle("the retry finished the first call");
			_broadcasts.Should().Be(1);
			CallAddedEvents().Should().Be(1, "Call Added already went out before the broadcast failed");
			_weather.Verify(x => x.AttachWeatherAlertsToCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Once);
			_receipts.Receipts[0].State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task A_saved_call_closed_before_the_retry_is_not_paged()
		{
			_broadcastFailures = 1;
			await Controller("msg-1@cad.example").Awaiting(c => c.SaveCall(Input(), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
			_saved[0].State = (int)CallStates.Closed;

			var retry = await Controller("msg-1@cad.example").SaveCall(Input(), CancellationToken.None);

			Id(retry).Should().Be("700");
			_broadcasts.Should().Be(0);
		}

		[Test]
		public async Task A_keyed_call_still_in_flight_is_asked_to_come_back()
		{
			_receipts.Seed(new InboundEmailReceipt
			{
				InboundEmailReceiptId = Guid.NewGuid().ToString(),
				ProviderMessageId = InboundEmailReceiptsService.ApiRequestKey(InboundEmailKinds.ApiCall, DepartmentId, "msg-1@cad.example"),
				Kind = (int)InboundEmailKinds.ApiCall,
				State = (int)InboundEmailReceiptStates.Processing,
				Deliveries = 1,
				ReceivedOn = DateTime.UtcNow.AddSeconds(-20),
				ClaimedOn = DateTime.UtcNow.AddSeconds(-20)
			});

			var result = await Controller("msg-1@cad.example").SaveCall(Input(), CancellationToken.None);

			result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task Unkeyed_calls_are_created_every_time()
		{
			await Controller(null).SaveCall(Input(), CancellationToken.None);
			await Controller(null).SaveCall(Input(), CancellationToken.None);

			_saved.Should().HaveCount(2);
			_broadcasts.Should().Be(2);
			_receipts.Receipts.Should().BeEmpty();
		}
	}
}
