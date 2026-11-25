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
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.ProtocolContent;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using V4 = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 surface for Omni (P3): policies, the staleness sentence, call step writes behind the right to edit the call, and a version
	/// in the reader's language with its steps (translations fall back to the source).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ProtocolContentApiTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "api-user";

		private Mock<IProtocolOfflineService> _offline;
		private Mock<IProtocolStepThroughService> _steps;
		private Mock<IProtocolTranslationService> _translations;
		private Mock<IProtocolAccessService> _access;
		private Mock<IProtocolLibraryService> _library;
		private Mock<IAuthorizationService> _authorization;

		[SetUp]
		public void SetUp()
		{
			_offline = new Mock<IProtocolOfflineService>();
			_steps = new Mock<IProtocolStepThroughService>();
			_translations = new Mock<IProtocolTranslationService>();
			_access = new Mock<IProtocolAccessService>();
			_library = new Mock<IProtocolLibraryService>();
			_authorization = new Mock<IAuthorizationService>();
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test"))
			};
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = null;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private V4.ProtocolContentController Controller()
		{
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key == "StaleVersionMessage" ? "You are viewing {0}. {1} is in force since {2}." : key));
			return new V4.ProtocolContentController(_offline.Object, _steps.Object, _translations.Object, _access.Object, _library.Object, Mock.Of<IAiKnowledgeService>(),
				_authorization.Object, localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = ApiClaims._httpContextAccessor.HttpContext }
			};
		}

		private static string PolicyOf(string action) =>
			typeof(V4.ProtocolContentController).GetMethods().Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Select(a => a.Policy).SingleOrDefault();

		[TestCase("GetOfflineManifest", ResgridResources.Protocol_View)]
		[TestCase("GetStaleness", ResgridResources.Protocol_View)]
		[TestCase("GetVersionContent", ResgridResources.Protocol_View)]
		[TestCase("GetSopLibrary", ResgridResources.Protocol_View)]
		[TestCase("GetCallChecklists", ResgridResources.Call_View)]
		[TestCase("StartCallChecklist", ResgridResources.Call_Update)]
		[TestCase("RecordCallStep", ResgridResources.Call_Update)]
		[TestCase("CompleteCallChecklist", ResgridResources.Call_Update)]
		public void Endpoints_carry_their_policy(string action, string policy)
		{
			PolicyOf(action).Should().Be(policy);
		}

		[Test]
		public async Task Staleness_says_which_version_is_viewed_and_which_is_in_force_since_when()
		{
			_offline.Setup(x => x.GetStalenessAsync(DepartmentId, UserId, 9, 5, null)).ReturnsAsync(new ProtocolStaleness
			{
				ProtocolId = 9, ViewedVersionId = 5, ViewedLabel = "3.0", CurrentVersionId = 6, CurrentLabel = "4.0", CurrentEffectiveOn = new DateTime(2026, 11, 20), IsStale = true
			});

			var result = (await Controller().GetStaleness(9, 5)).Result as OkObjectResult;

			((GetProtocolStalenessResult)result.Value).Data.Message.Should().Be("You are viewing v3.0. v4.0 is in force since 2026-11-20.");
			(await Controller().GetStaleness(8, 5)).Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task A_step_needs_the_right_to_edit_the_call_and_returns_the_checklist()
		{
			_authorization.Setup(x => x.CanUserEditCallAsync(UserId, 40)).ReturnsAsync(false);
			(await Controller().RecordCallStep(new RecordCallStepInput { CallId = 40, CallProtocolId = 300, StepId = "s1-a", State = 1 }, CancellationToken.None)).Result
				.Should().NotBeOfType<OkObjectResult>();
			_steps.Verify(x => x.RecordStepAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ProtocolStepStates>(), It.IsAny<string>(),
				It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);

			_authorization.Setup(x => x.CanUserEditCallAsync(UserId, 40)).ReturnsAsync(true);
			var checklist = new ProtocolCallChecklist
			{
				Row = new CallProtocol { CallProtocolId = 300, CallId = 40 },
				Protocol = new DispatchProtocol { DispatchProtocolId = 9, Kind = (int)ProtocolKinds.Procedure, Code = "RIT", Name = "RIT" },
				Steps = new List<ProtocolStepView> { new ProtocolStepView { Step = new ProtocolStepDefinition { Id = "s1-a", Text = "Check air", Kind = "step", Order = 1 }, State = ProtocolStepStates.Done } }
			};
			var device = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);
			_steps.Setup(x => x.RecordStepAsync(DepartmentId, 40, 300, "s1-a", ProtocolStepStates.Done, null, device, UserId, It.IsAny<CancellationToken>(), "dev-77"))
				.ReturnsAsync(new ProtocolStepResult { Checklist = checklist });

			// Round 5F: the device's own event id travels to the service (an offline replay with the same id is ignored there).
			var ok = (await Controller().RecordCallStep(new RecordCallStepInput { CallId = 40, CallProtocolId = 300, StepId = "s1-a", State = 1, DeviceTimestamp = device, ClientStepId = "dev-77" },
				CancellationToken.None)).Result as OkObjectResult;

			var data = ((CallProtocolChecklistResult)ok.Value).Data;
			data.IsIncidentSop.Should().BeTrue();
			data.Done.Should().Be(1);
			data.Steps.Single().State.Should().Be((int)ProtocolStepStates.Done);
		}

		[Test]
		public async Task A_version_is_returned_in_the_readers_language_with_its_steps_and_the_source_hash()
		{
			var json = ProtocolBlocks.Normalize(new ProtocolContentDocument
			{
				Schema = ProtocolContentDocument.BlockSchema,
				Blocks = new List<ProtocolContentBlock> { new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "s", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "a", Html = "Eins" } } } }
			}).ToJson();
			var version = new ProtocolVersion { ProtocolVersionId = 6, DepartmentId = DepartmentId, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Published, Major = 4, ContentHash = new string('b', 64), ContentJson = json, ContentHtml = "<p>en</p>" };
			var protocol = new DispatchProtocol { DispatchProtocolId = 9, DepartmentId = DepartmentId, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 6 };
			_library.Setup(x => x.GetVersionAsync(DepartmentId, 6)).ReturnsAsync(version);
			_library.Setup(x => x.GetAudienceAsync(DepartmentId, 9)).ReturnsAsync(new List<ProtocolAudience>());
			_access.Setup(x => x.GetReadableAsync(DepartmentId, UserId, 9)).ReturnsAsync(protocol);
			_access.Setup(x => x.GetReaderAsync(DepartmentId, UserId)).ReturnsAsync(new ProtocolReader { DepartmentId = DepartmentId, UserId = UserId, IsCurrentMember = true });
			var translation = new ProtocolVersionTranslation { ProtocolVersionTranslationId = 70, Locale = "de", ContentJson = json, ContentHtml = ProtocolContentDocument.Parse(json, null).Render(), ContentHash = new string('e', 64), Status = 1 };
			_translations.Setup(x => x.GetLocalizedAsync(DepartmentId, version, "de")).ReturnsAsync(new ProtocolLocalizedContent { Version = version, Translation = translation, Locale = "de", AvailableLocales = new List<string> { "en", "de" } });

			var ok = (await Controller().GetVersionContent(6, "de")).Result as OkObjectResult;

			var data = ((GetProtocolVersionContentResult)ok.Value).Data;
			data.Locale.Should().Be("de");
			data.ContentHash.Should().Be(new string('b', 64), "the source version's hash, whatever language is shown");
			data.TranslationContentHash.Should().Be(new string('e', 64));
			data.ContentHtml.Should().Contain("id=\"b-s-a\"");
			data.Steps.Single().Id.Should().Be("s-a");
		}
	}
}
