using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Departments.CallSettings;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Call Settings form renders the email format and call numbering, not a POP3 mailbox. Saving it must keep a stored
	/// mailbox (every save used to blank it) and save the numbering.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallSettingsSaveTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "settings-admin";

		private DepartmentCallEmail _stored;
		private DepartmentCallEmail _saved;
		private Mock<ICallNumberingService> _numbering;
		private DefaultHttpContext _httpContext;
		private DepartmentController _controller;

		[SetUp]
		public void SetUp()
		{
			_stored = new DepartmentCallEmail { DepartmentCallEmailId = 3, DepartmentId = DepartmentId, Hostname = "pop.example.org", Port = 995, UseSsl = true, Username = "cad", Password = "mailbox-secret", FormatType = 1 };
			_saved = null;

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId)).ReturnsAsync(() => _stored);
			departments.Setup(d => d.SaveDepartmentEmailSettingsAsync(It.IsAny<DepartmentCallEmail>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentCallEmail e, CancellationToken ct) => _saved = e);
			departments.Setup(d => d.SaveDepartmentCallPruningAsync(It.IsAny<DepartmentCallPruning>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentCallPruning p, CancellationToken ct) => p);

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetCallNumberingConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new CallNumberingConfig());

			_numbering = new Mock<ICallNumberingService>();
			_numbering.Setup(n => n.GetNextAsync(DepartmentId, It.IsAny<CallNumberingConfig>(), It.IsAny<System.DateTime>()))
				.ReturnsAsync(new CallNumberSequenceStatus { ScopeKey = "26-#", NextSequence = 22, NextNumber = "26-22", Period = CallNumberResetPeriod.Yearly });
			_numbering.Setup(n => n.SaveAsync(DepartmentId, UserId, It.IsAny<CallNumberingUpdate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CallNumberingSaveResult());

			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Department.Department>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns((string key, object[] args) => new LocalizedString(key, key));

			_httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			// The numbering audit event records the caller's address.
			_httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _httpContext };

			_controller = new DepartmentController(departments.Object, Mock.Of<IUsersService>(), Mock.Of<IActionLogsService>(), Mock.Of<IEmailService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDeleteService>(), Mock.Of<IInvitesService>(), authorization.Object,
				Mock.Of<IAddressService>(), Mock.Of<ISubscriptionsService>(), Mock.Of<ILimitsService>(), Mock.Of<ICallsService>(), settings.Object, Mock.Of<IUnitsService>(),
				Mock.Of<ICertificationService>(), Mock.Of<INumbersService>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), Mock.Of<ICqrsProvider>(), Mock.Of<IPrinterProvider>(), Mock.Of<IQueueService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), Mock.Of<IContactsService>(), Mock.Of<ICheckInTimerService>(), Mock.Of<ISecurityPinService>(),
				Mock.Of<IRunCardsService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentProfileMediaService>(),
				localizer.Object, Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>(),
				Mock.Of<IProtectedReadService>(), _numbering.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private void Post(Dictionary<string, StringValues> fields)
		{
			_httpContext.Request.ContentType = "application/x-www-form-urlencoded";
			_httpContext.Request.Form = new FormCollection(fields);
		}

		private static CallSettingsView Model() => new CallSettingsView
		{
			EmailSettings = new DepartmentCallEmail { DepartmentCallEmailId = 3 },
			CallType = 4,
			CallNumberPattern = "FD{YYYY}-{SEQ}",
			CallNumberSequenceWidth = 4,
			CallNumberScopeKey = "26-#"
		};

		[Test]
		public async Task Saving_keeps_a_stored_mailbox_the_form_does_not_render()
		{
			Post(new Dictionary<string, StringValues> { ["EmailSettings.DepartmentCallEmailId"] = "3", ["CallType"] = "4" });

			var result = await _controller.CallSettings(Model(), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			_saved.Should().NotBeNull();
			_saved.Hostname.Should().Be("pop.example.org");
			_saved.Password.Should().Be("mailbox-secret");
			_saved.Username.Should().Be("cad");
			_saved.Port.Should().Be(995);
			_saved.UseSsl.Should().BeTrue();
			_saved.FormatType.Should().Be(4, "the email format is on the form and still saves");
		}

		[Test]
		public async Task A_form_that_posts_the_mailbox_still_saves_it()
		{
			Post(new Dictionary<string, StringValues> { ["EmailSettings.Hostname"] = "mail.example.org" });
			var model = Model();
			model.EmailSettings.Hostname = "mail.example.org";
			model.EmailSettings.Port = 110;

			await _controller.CallSettings(model, CancellationToken.None);

			_saved.Hostname.Should().Be("mail.example.org");
			_saved.Port.Should().Be(110);
		}

		[Test]
		public async Task Saving_saves_the_call_numbering_and_shows_what_was_saved()
		{
			Post(new Dictionary<string, StringValues>());
			var model = Model();
			model.CallNumberNextSequence = 154;

			var result = (ViewResult)await _controller.CallSettings(model, CancellationToken.None);

			_numbering.Verify(n => n.SaveAsync(DepartmentId, UserId, It.Is<CallNumberingUpdate>(u =>
				u.Pattern == "FD{YYYY}-{SEQ}" && u.SequenceWidth == 4 && u.ScopeKey == "26-#" && u.NextSequence == 154), It.IsAny<CancellationToken>()), Times.Once);
			var view = (CallSettingsView)result.Model;
			view.CallNumberNextNumber.Should().Be("26-22");
			view.CallNumberNextSequence.Should().BeNull("a raised number is never left in its box to be resubmitted");
			view.ErrorMessage.Should().BeNull();
		}
	}
}
