using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Models.v4.Protocols;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using V4 = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 protocol surface (P0 items 1, 3, 4, 5a, 7): read endpoints behind Protocol_View, writes behind Create/Update, lists
	/// and the offline reference without disabled protocols or attachment bytes, the shared matcher, and edits in place by id.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ProtocolsApiTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "api-user";

		private Mock<IProtocolsService> _protocols;
		private Mock<IAuthorizationService> _authorization;
		private Mock<ICallsService> _calls;
		private Mock<IProtocolAccessService> _access;

		[SetUp]
		public void SetUp()
		{
			_protocols = new Mock<IProtocolsService>();
			// These tests are about the endpoints, not the read rule (ProtocolAudienceGuardTests covers that): every protocol is readable.
			_access = new Mock<IProtocolAccessService>();
			_access.Setup(x => x.GetReaderAsync(DepartmentId, UserId)).ReturnsAsync(ProtocolReader.Nobody(DepartmentId));
			_access.Setup(x => x.FilterReadableAsync(It.IsAny<ProtocolReader>(), It.IsAny<IEnumerable<DispatchProtocol>>()))
				.ReturnsAsync((ProtocolReader _, IEnumerable<DispatchProtocol> list) => list?.ToList() ?? new List<DispatchProtocol>());
			_access.Setup(x => x.CanReadAsync(It.IsAny<ProtocolReader>(), It.IsAny<DispatchProtocol>())).ReturnsAsync(true);
			_authorization = new Mock<IAuthorizationService>();
			_calls = new Mock<ICallsService>();

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = null;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private V4.ProtocolsController Protocols() => new V4.ProtocolsController(_protocols.Object, _authorization.Object, _calls.Object, Mock.Of<IEventAggregator>(), _access.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = ApiClaims._httpContextAccessor.HttpContext }
		};

		private static string PolicyOf(Type controller, string action) =>
			controller.GetMethods().Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Select(a => a.Policy).SingleOrDefault();

		[TestCase(typeof(V4.ProtocolsController), "GetAllProtocols", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.ProtocolsController), "GetProtocol", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.ProtocolsController), "GetFile", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.ProtocolsController), "GetProtocolsForCall", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.ProtocolsController), "SaveProtocol", ResgridResources.Protocol_Create)]
		[TestCase(typeof(V4.ProtocolsController), "UpdateProtocol", ResgridResources.Protocol_Update)]
		[TestCase(typeof(V4.ProtocolsController), "SetDisabled", ResgridResources.Protocol_Update)]
		[TestCase(typeof(V4.CallProtocolsController), "GetAllCallProtocols", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.CallProtocolsController), "GetProtocol", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.CallProtocolsController), "GetFile", ResgridResources.Protocol_View)]
		[TestCase(typeof(V4.CallProtocolsController), "RemoveCallProtocol", ResgridResources.Call_Update)]
		public void Protocol_endpoints_carry_their_policy(Type controller, string action, string policy)
		{
			PolicyOf(controller, action).Should().Be(policy);
		}

		[Test]
		public void The_sync_reference_no_longer_takes_bypassCache_from_the_client()
		{
			typeof(V4.SyncController).GetMethod("Reference").GetParameters().Should().BeEmpty();
		}

		[Test]
		public async Task The_list_asks_for_enabled_protocols_only()
		{
			_protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, true)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = DepartmentId, Name = "Active", Code = "A" }
			});

			var result = (await Protocols().GetAllProtocols()).Value;

			result.Data.Select(x => x.ProtocolId).Should().Equal("1");
			_protocols.Verify(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, true), Times.Once);
			_protocols.Verify(x => x.GetAllProtocolsForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task An_unknown_or_foreign_file_is_a_404()
		{
			_protocols.Setup(x => x.GetAttachmentForDepartmentAsync(DepartmentId, 5)).ReturnsAsync((DispatchProtocolAttachment)null);

			(await Protocols().GetFile(5)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Get_protocols_for_call_runs_the_shared_matcher()
		{
			var context = new ProtocolCallContext { Priority = 2, CallTypeId = 70, AtUtc = DateTime.UtcNow };
			// P4: the shared trigger engine's facts come along (none here: no nature, location or POI was sent).
			_protocols.Setup(x => x.BuildCallContextWithFactsAsync(DepartmentId, 2, 70, null, It.Is<DispatchTriggerCallInput>(d => d.Nature == null && d.LocationPoiId == null && d.LoggedOnUtc == null))).ReturnsAsync(context);
			_protocols.Setup(x => x.GetProtocolsForCallAsync(DepartmentId, context)).ReturnsAsync(new List<ProtocolMatch>
			{
				new ProtocolMatch
				{
					Protocol = new DispatchProtocol { DispatchProtocolId = 4, Name = "Fire", Code = "F", Questions = new List<DispatchProtocolQuestion>() },
					State = ProtocolStates.Active,
					MatchedTrigger = new DispatchProtocolTrigger { DispatchProtocolTriggerId = 40, Type = 1 }
				}
			});

			var result = (await Protocols().GetProtocolsForCall(2, 70)).Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<GetProtocolsForCallResult>().Subject;

			var match = result.Data.Should().ContainSingle().Subject;
			match.State.Should().Be(1);
			match.MatchedTriggerId.Should().Be("40");
			match.MatchedTriggerType.Should().Be(1);
		}

		[Test]
		public async Task Update_refuses_rows_of_another_protocol()
		{
			_authorization.Setup(x => x.CanUserModifyProtocolAsync(UserId, 50)).ReturnsAsync(true);
			_protocols.Setup(x => x.GetProtocolByIdAsync(50)).ReturnsAsync(new DispatchProtocol
			{
				DispatchProtocolId = 50, DepartmentId = DepartmentId, Name = "Mine", Code = "M",
				Triggers = new List<DispatchProtocolTrigger>(), Questions = new List<DispatchProtocolQuestion>(), Attachments = new List<DispatchProtocolAttachment>()
			});

			var result = await Protocols().UpdateProtocol(new ProtocolInput
			{
				ProtocolId = 50, Name = "Mine", Code = "M",
				Questions = new List<ProtocolQuestionInput> { new ProtocolQuestionInput { ProtocolQuestionId = 999, Question = "q", Answers = new List<ProtocolAnswerInput> { new ProtocolAnswerInput { Answer = "a" } } } }
			}, CancellationToken.None);

			result.Result.Should().BeOfType<BadRequestObjectResult>();
			_protocols.Verify(x => x.UpdateProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Update_keeps_the_ids_it_was_given()
		{
			_authorization.Setup(x => x.CanUserModifyProtocolAsync(UserId, 50)).ReturnsAsync(true);
			_protocols.Setup(x => x.GetProtocolByIdAsync(50)).ReturnsAsync(new DispatchProtocol
			{
				DispatchProtocolId = 50, DepartmentId = DepartmentId, Name = "Mine", Code = "M",
				Triggers = new List<DispatchProtocolTrigger>(),
				Questions = new List<DispatchProtocolQuestion>
				{
					new DispatchProtocolQuestion { DispatchProtocolQuestionId = 21, Question = "q", Answers = new List<DispatchProtocolQuestionAnswer> { new DispatchProtocolQuestionAnswer { DispatchProtocolQuestionAnswerId = 31, DispatchProtocolQuestionId = 21, Answer = "a" } } }
				},
				Attachments = new List<DispatchProtocolAttachment>()
			});
			DispatchProtocol edited = null;
			_protocols.Setup(x => x.UpdateProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<IEnumerable<int>>(), UserId, It.IsAny<CancellationToken>()))
				.Callback<DispatchProtocol, IEnumerable<int>, string, CancellationToken>((p, _, __, ___) => edited = p)
				.ReturnsAsync((DispatchProtocol p, IEnumerable<int> _, string __, CancellationToken ___) => p);

			await Protocols().UpdateProtocol(new ProtocolInput
			{
				ProtocolId = 50, Name = "Mine", Code = "M",
				Questions = new List<ProtocolQuestionInput> { new ProtocolQuestionInput { ProtocolQuestionId = 21, Question = "q2", Answers = new List<ProtocolAnswerInput> { new ProtocolAnswerInput { ProtocolQuestionAnswerId = 31, Answer = "a2" } } } }
			}, CancellationToken.None);

			edited.Questions.Single().DispatchProtocolQuestionId.Should().Be(21);
			edited.Questions.Single().Answers.Single().DispatchProtocolQuestionAnswerId.Should().Be(31);
		}

		/// <summary>
		/// D7: the offline reference payload (and its Redis copy) carried attachment bytes as base64. The serialized reference must
		/// never contain them, whatever a protocol's attachment holds.
		/// </summary>
		[Test]
		public async Task The_sync_reference_json_carries_no_attachment_bytes()
		{
			var bytes = Enumerable.Range(0, 64).Select(i => (byte)(i + 1)).ToArray();
			var protocols = new Mock<IProtocolsService>();
			protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, true)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol
				{
					DispatchProtocolId = 1, DepartmentId = DepartmentId, Name = "With file", Code = "WF",
					Attachments = new List<DispatchProtocolAttachment> { new DispatchProtocolAttachment { DispatchProtocolAttachmentId = 9, FileName = "sop.pdf", Data = bytes, Size = bytes.Length } }
				}
			});

			var service = new SyncService(Mock.Of<ICallsService>(), Mock.Of<ICommandsService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IMappingService>(), protocols.Object, Mock.Of<ICheckInTimerService>(), Mock.Of<ICustomStateService>(),
				Mock.Of<IUserProfileService>(), Mock.Of<IUserStateService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<ICacheProvider>());
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;

			var data = await service.GetReferenceDataAsync(DepartmentId);
			var json = JsonConvert.SerializeObject(data);

			json.Should().Contain("sop.pdf");
			json.Should().NotContain(Convert.ToBase64String(bytes));
			json.Should().NotContain("\"Data\":");
			protocols.Verify(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, true), Times.Once, "disabled protocols are left out of the reference");
		}
	}
}
