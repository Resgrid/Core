using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using WebFilter = Resgrid.Web.Filters.RecordsModuleDisabledExceptionFilter;
using ApiFilter = Resgrid.Web.Services.Filters.RecordsModuleDisabledExceptionFilter;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// A Records module that is not enabled for the department (RESGRID-WEB-1MM, -1N0, -1NK, -1MV, ...: a department with the
	/// Records flag on but Records not yet active opened a prevention page) is reported to the user, not answered with a 500.
	/// </summary>
	[TestFixture]
	public class RecordsModuleDisabledExceptionFilterTests
	{
		private CultureInfo _previousUiCulture;
		private ITempDataDictionary _tempData;

		[SetUp]
		public void SetUp()
		{
			_previousUiCulture = CultureInfo.CurrentUICulture;
			CultureInfo.CurrentUICulture = new CultureInfo("en");
		}

		[TearDown]
		public void TearDown()
		{
			CultureInfo.CurrentUICulture = _previousUiCulture;
		}

		// What a browser sends when the user follows a link or submits a form.
		private const string BrowserNavigationAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

		private ExceptionContext Context(Exception exception, string path = "/User/RecordHydrants", bool script = false, string controller = "RecordHydrants", string action = "Index", string accept = BrowserNavigationAccept)
		{
			var http = new DefaultHttpContext();
			_tempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			var tempDataFactory = new Mock<ITempDataDictionaryFactory>();
			tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>())).Returns(_tempData);
			http.RequestServices = new ServiceCollection().AddLogging().AddLocalization().AddSingleton(tempDataFactory.Object).BuildServiceProvider();
			http.Request.Method = "GET";
			http.Request.Path = path;
			if (script)
				http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
			if (accept != null)
				http.Request.Headers["Accept"] = accept;

			var routeData = new RouteData();
			routeData.Values["area"] = "User";
			routeData.Values["controller"] = controller;
			routeData.Values["action"] = action;
			return new ExceptionContext(new ActionContext(http, routeData, new ActionDescriptor()), new List<IFilterMetadata>()) { Exception = exception };
		}

		[Test]
		public void PageRequest_RedirectsToTheRecordsQueueWithTheMessage()
		{
			var context = Context(new RecordsModuleDisabledException(RecordsPreventionModule.Hydrants));

			new WebFilter().OnException(context);

			context.ExceptionHandled.Should().BeTrue();
			var redirect = context.Result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ControllerName.Should().Be("Records");
			redirect.ActionName.Should().Be("Index");
			redirect.RouteValues["area"].Should().Be("User");
			_tempData["RecordsError"].Should().Be("Hydrants: this Records module is not enabled for the department.");
		}

		[Test]
		public void ScriptRequest_Gets404WithTheMessage()
		{
			var context = Context(new RecordsModuleDisabledException(RecordsPreventionModule.Permits), "/User/RecordPermits/Types", script: true);

			new WebFilter().OnException(context);

			context.ExceptionHandled.Should().BeTrue();
			var json = context.Result.Should().BeOfType<JsonResult>().Subject;
			json.StatusCode.Should().Be(404);
			json.Value.Should().BeEquivalentTo(new { success = false, error = "records_module_disabled", message = "Permits: this Records module is not enabled for the department." });
		}

		[TestCase("application/pdf")]
		[TestCase("text/csv")]
		[TestCase("*/*")]
		[TestCase(null)]
		public void FileOrNonPageRequest_Gets404WithTheMessage_NotTheQueuePage(string accept)
		{
			var context = Context(new RecordsModuleDisabledException(RecordsPreventionModule.Inspections), "/User/RecordInspections/ExportPdf", accept: accept);

			new WebFilter().OnException(context);

			context.ExceptionHandled.Should().BeTrue();
			var json = context.Result.Should().BeOfType<JsonResult>().Subject;
			json.StatusCode.Should().Be(404);
			json.Value.Should().BeEquivalentTo(new { success = false, error = "records_module_disabled", message = "Inspections: this Records module is not enabled for the department." });
			_tempData.ContainsKey("RecordsError").Should().BeFalse("nothing is redirected to show it");
		}

		[Test]
		public void TheRecordsQueueItself_DoesNotRedirectToItself()
		{
			var context = Context(new RecordsModuleDisabledException(RecordsPreventionModule.QualityReview), "/User/Records", controller: "Records", action: "Index");

			new WebFilter().OnException(context);

			context.ExceptionHandled.Should().BeTrue();
			context.Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public void Message_IsInTheUsersLanguage_ForEveryModule()
		{
			CultureInfo.CurrentUICulture = new CultureInfo("de");
			foreach (var module in RecordsPreventionModules.All)
			{
				var context = Context(new RecordsModuleDisabledException(module));

				new WebFilter().OnException(context);

				var message = _tempData["RecordsError"] as string;
				message.Should().Contain("Akten-Modul", module.ToString());
				message.Should().NotStartWith(module.ToString(), "every module needs a localized name, not its enum or key name");
			}
		}

		[Test]
		public void OtherExceptions_AreLeftAlone()
		{
			var context = Context(new InvalidOperationException("Sequence contains no elements"));

			new WebFilter().OnException(context);

			context.ExceptionHandled.Should().BeFalse();
			context.Result.Should().BeNull();
		}

		[Test]
		public void Api_AnswersNotFoundProblemWithTheMessage()
		{
			var context = Context(new RecordsModuleDisabledException(RecordsPreventionModule.Inspections), "/api/v4/RecordInspections/List");

			new ApiFilter().OnException(context);

			context.ExceptionHandled.Should().BeTrue();
			var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
			result.StatusCode.Should().Be(404);
			var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
			problem.Title.Should().Be("The Records Inspections module is not enabled for this department.");
			problem.Type.Should().Be("records_module_disabled");
		}

		[Test]
		public void Api_OtherExceptions_AreLeftAlone()
		{
			var context = Context(new InvalidOperationException("Sequence contains no elements"), "/api/v4/RecordInspections/List");

			new ApiFilter().OnException(context);

			context.ExceptionHandled.Should().BeFalse();
			context.Result.Should().BeNull();
		}
	}
}
