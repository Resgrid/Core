using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Lms;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// GET /User/LmsRequirements/Onboarding answered 500 ("The view 'Onboarding' was not found"): it renders through Index, whose unnamed
	/// View(model) resolved the view from the Onboarding action name. Index now names its view, so both routes render Index.cshtml.
	/// </summary>
	[TestFixture]
	public class LmsRequirementsOnboardingViewTests
	{
		private const int DepartmentId = 4101;
		private const string UserId = "lms-onboarding-officer";
		private Mock<ILmsRequirementService> _requirements;

		[SetUp]
		public void SetUp() => _requirements = new Mock<ILmsRequirementService>();

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private LmsRequirementsController Build()
		{
			var constructor = typeof(LmsRequirementsController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(ILmsRequirementService) ? _requirements.Object :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (LmsRequirementsController)constructor.Invoke(arguments);

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());

			// OnActionExecutionAsync sets the actor from the LMS gate; the action is called directly here, so set it the same way.
			typeof(LmsRequirementsController).GetField("_actor", BindingFlags.NonPublic | BindingFlags.Instance)!
				.SetValue(controller, new LmsActor { DepartmentId = DepartmentId, UserId = UserId, IsCurrentMember = true, IsDepartmentAdmin = true, Gate = new LmsDepartmentGate { DepartmentId = DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = true } });
			return controller;
		}

		private static LmsRequirementRow Row(int id, bool onboarding) =>
			new LmsRequirementRow { Requirement = new LmsRequirement { LmsRequirementId = id, DepartmentId = DepartmentId, Name = "Requirement " + id, IsOnboarding = onboarding } };

		[Test]
		public async Task Onboarding_renders_the_Index_view_with_only_the_onboarding_requirements()
		{
			_requirements.Setup(x => x.GetRequirementsAsync(It.IsAny<LmsActor>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<LmsRequirementRow> { Row(1, true), Row(2, false) });

			var result = await Build().Onboarding();

			var view = result.Should().BeOfType<ViewResult>().Subject;
			view.ViewName.Should().Be("Index");
			var model = view.Model.Should().BeOfType<LmsRequirementsIndexView>().Subject;
			model.OnboardingOnly.Should().BeTrue();
			model.Rows.Select(r => r.Requirement.LmsRequirementId).Should().Equal(1);
		}

		[Test]
		public async Task Index_still_renders_the_Index_view_with_every_requirement()
		{
			_requirements.Setup(x => x.GetRequirementsAsync(It.IsAny<LmsActor>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<LmsRequirementRow> { Row(1, true), Row(2, false) });

			var result = await Build().Index();

			var view = result.Should().BeOfType<ViewResult>().Subject;
			view.ViewName.Should().Be("Index");
			view.Model.Should().BeOfType<LmsRequirementsIndexView>().Which.Rows.Should().HaveCount(2);
		}
	}
}
