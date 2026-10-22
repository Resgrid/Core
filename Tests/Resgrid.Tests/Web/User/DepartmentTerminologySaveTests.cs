using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Departments;

namespace Resgrid.Tests.Web.User
{
	/// <summary>Department -> Terminology saves the department's own names (setting 118) and refuses only what it cannot store.</summary>
	[TestFixture]
	[NonParallelizable]
	public class DepartmentTerminologySaveTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "settings-admin";

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IAuthorizationService> _authorization;
		private DepartmentTerminologyConfig _saved;
		private DepartmentController _controller;

		private static readonly string Run = DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run);
		private static readonly string Work = DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Work);
		private static readonly string Meeting = DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Meeting);

		[SetUp]
		public void SetUp()
		{
			_saved = new DepartmentTerminologyConfig();
			_saved.Set(DepartmentTerms.Units, "Apparatus");

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetDepartmentTerminologyAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _saved);
			_settings.Setup(s => s.SetDepartmentTerminologyAsync(DepartmentId, It.IsAny<DepartmentTerminologyConfig>(), It.IsAny<CancellationToken>()))
				.Callback((int d, DepartmentTerminologyConfig c, CancellationToken ct) => _saved = c)
				.ReturnsAsync(new DepartmentSetting());

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Department.Department>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new DepartmentController(Mock.Of<IDepartmentsService>(), Mock.Of<IUsersService>(), Mock.Of<IActionLogsService>(), Mock.Of<IEmailService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDeleteService>(), Mock.Of<IInvitesService>(), _authorization.Object,
				Mock.Of<IAddressService>(), Mock.Of<ISubscriptionsService>(), Mock.Of<ILimitsService>(), Mock.Of<ICallsService>(), _settings.Object, Mock.Of<IUnitsService>(),
				Mock.Of<ICertificationService>(), Mock.Of<INumbersService>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), Mock.Of<ICqrsProvider>(), Mock.Of<IPrinterProvider>(), Mock.Of<IQueueService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), Mock.Of<IContactsService>(), Mock.Of<ICheckInTimerService>(), Mock.Of<ISecurityPinService>(),
				Mock.Of<IRunCardsService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentProfileMediaService>(),
				localizer.Object, Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>(),
				Mock.Of<IProtectedReadService>(), Mock.Of<ICallNumberingService>(), Mock.Of<IDocumentNumberingService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task The_page_lists_every_term_with_the_saved_names()
		{
			var result = await _controller.Terminology();

			var model = (TerminologyView)((ViewResult)result).Model;
			model.Terms.Select(t => t.Key).Should().Equal(DepartmentTerms.All.Select(t => t.Key));
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).Text.Should().Be("Apparatus");
			model.Terms.Single(t => t.Key == Run).Text.Should().BeNull();
		}

		[Test]
		public async Task Saving_stores_the_typed_names_and_clears_the_blank_ones()
		{
			var model = TerminologyView.From(_saved);
			model.Terms.Single(t => t.Key == Run).Text = "Incident";
			model.Terms.Single(t => t.Key == Work).Text = "  Shift On Duty ";
			model.Terms.Single(t => t.Key == Meeting).Text = "Officers Meeting";
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).Text = "";

			var result = await _controller.Terminology(model, CancellationToken.None);

			_saved.TextFor(Run).Should().Be("Incident");
			_saved.TextFor(Work).Should().Be("Shift On Duty");
			_saved.TextFor(Meeting).Should().Be("Officers Meeting");
			_saved.TextFor(DepartmentTerms.Units).Should().BeNull("an emptied box goes back to Resgrid's name");
			var view = (TerminologyView)((ViewResult)result).Model;
			view.Message.Should().Be("TerminologySaved");
			view.ErrorMessage.Should().BeNull();
		}

		[Test]
		public async Task Saving_stores_the_item_words_for_modules_that_have_them()
		{
			var model = TerminologyView.From(_saved);
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).HasSingular.Should().BeTrue();
			model.Terms.Single(t => t.Key == DepartmentTerms.Calendar).HasSingular.Should().BeFalse();
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).Singular = " Rig ";
			model.Terms.Single(t => t.Key == DepartmentTerms.Calls).Singular = "Incident";
			model.Terms.Single(t => t.Key == Run).Singular = "Ignored";

			await _controller.Terminology(model, CancellationToken.None);

			_saved.TextFor(DepartmentTerms.Units).Should().Be("Apparatus");
			_saved.SavedSingularFor(DepartmentTerms.Units).Should().Be("Rig");
			_saved.TextFor(DepartmentTerms.Calls).Should().BeNull("an item word can be renamed on its own");
			_saved.SingularFor(DepartmentTerms.Calls).Should().Be("Incident");
			_saved.Terms.Should().NotContain(t => t.Key == Run, "record types have no item word, so a lone one saves nothing");
		}

		[Test]
		public async Task An_item_word_over_the_limit_keeps_the_saved_names()
		{
			var tooLong = new string('x', DepartmentTerms.MaxLength + 1);
			var model = TerminologyView.From(_saved);
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).Text = "Teams";
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).Singular = tooLong;

			var result = await _controller.Terminology(model, CancellationToken.None);

			_saved.TextFor(DepartmentTerms.Units).Should().Be("Apparatus", "the row is refused as a whole");
			var view = (TerminologyView)((ViewResult)result).Model;
			view.Terms.Single(t => t.Key == DepartmentTerms.Units).Singular.Should().Be(tooLong);
			view.Terms.Single(t => t.Key == DepartmentTerms.Units).Text.Should().Be("Teams");
			view.Message.Should().Be("TerminologyPartlySaved");
		}

		[Test]
		public void The_page_shows_resgrids_own_wording()
		{
			typeof(Resgrid.Web.Areas.User.Controllers.DepartmentController).GetMethods()
				.Where(m => m.Name == nameof(Resgrid.Web.Areas.User.Controllers.DepartmentController.Terminology))
				.Should().HaveCount(2).And.OnlyContain(m => m.GetCustomAttributes(typeof(Resgrid.Web.Filters.SuppressDepartmentTermsAttribute), false).Any());
		}

		[Test]
		public async Task A_name_over_the_limit_keeps_the_saved_one_and_stays_in_its_box()
		{
			var tooLong = new string('x', DepartmentTerms.MaxLength + 1);
			var model = TerminologyView.From(_saved);
			model.Terms.Single(t => t.Key == DepartmentTerms.Units).Text = tooLong;
			model.Terms.Single(t => t.Key == Run).Text = "Incident";

			var result = await _controller.Terminology(model, CancellationToken.None);

			_saved.TextFor(DepartmentTerms.Units).Should().Be("Apparatus");
			_saved.TextFor(Run).Should().Be("Incident", "the other names still save");
			var view = (TerminologyView)((ViewResult)result).Model;
			view.Terms.Single(t => t.Key == DepartmentTerms.Units).Text.Should().Be(tooLong);
			view.ErrorMessage.Should().NotBeNullOrEmpty();
			view.Message.Should().Be("TerminologyPartlySaved");
		}

		[Test]
		public async Task Terms_missing_from_the_post_and_unknown_keys_change_nothing()
		{
			var model = new TerminologyView
			{
				Terms = new List<TerminologyRow>
				{
					new TerminologyRow { Key = "module.not-a-module", Text = "Anything" },
					new TerminologyRow { Key = Run, Text = "Incident" }
				}
			};

			await _controller.Terminology(model, CancellationToken.None);

			_saved.TextFor(DepartmentTerms.Units).Should().Be("Apparatus");
			_saved.TextFor(Run).Should().Be("Incident");
			_saved.Terms.Should().NotContain(t => t.Key == "module.not-a-module");
		}

		[Test]
		public async Task Only_department_editors_can_save()
		{
			_authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(false);

			var result = await _controller.Terminology(TerminologyView.From(_saved), CancellationToken.None);

			result.Should().NotBeOfType<ViewResult>("the base controller turns Unauthorized into a redirect");
			_settings.Verify(s => s.SetDepartmentTerminologyAsync(It.IsAny<int>(), It.IsAny<DepartmentTerminologyConfig>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
