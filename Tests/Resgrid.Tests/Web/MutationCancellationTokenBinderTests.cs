using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Controllers;
using V4 = Resgrid.Web.Services.Controllers.v4;
using WebFilters = Resgrid.Web.Filters;
using ApiFilters = Resgrid.Web.Services.Filters;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// A dispatcher whose browser dropped the connection partway through closing a call (a double-clicked button, a closed
	/// tab) cancelled the save between its steps: the call was closed but its units were never released, and a retry could
	/// not release them because the call was no longer active (Sentry: TaskCanceledException in RepositoryBase.UpdateAsync
	/// on User.Dispatch.CloseCall/UpdateCall). The apps hit the v4 API the same way, over connections that drop more often.
	/// A write's CancellationToken is not tied to the client in either host; a read's still is.
	/// </summary>
	[TestFixture]
	public class MutationCancellationTokenBinderTests
	{
		private static readonly Func<ModelBindingContext, Task> WebBinder = c => new WebFilters.MutationCancellationTokenBinder().BindModelAsync(c);
		private static readonly Func<ModelBindingContext, Task> ApiBinder = c => new ApiFilters.MutationCancellationTokenBinder().BindModelAsync(c);

		[TestCase(typeof(DispatchController), "CloseCall")]
		[TestCase(typeof(DispatchController), "UpdateCall")]
		[TestCase(typeof(DispatchController), "NewCall")]
		[TestCase(typeof(UnitsController), "NewUnit")]
		public async Task A_web_write_keeps_going_after_the_browser_leaves(Type controller, string action)
		{
			var token = await BindAsync(WebBinder, controller, action, "POST");

			token.CanBeCanceled.Should().BeFalse();
		}

		[TestCase(typeof(DispatchController), "CloseCall")]
		[TestCase(typeof(DispatchController), "UpdateCall")]
		public async Task A_web_read_stops_when_the_browser_leaves(Type controller, string action)
		{
			var token = await BindAsync(WebBinder, controller, action, "GET");

			token.IsCancellationRequested.Should().BeTrue();
		}

		[TestCase(typeof(MapVisualizationsController), "Data")]
		[TestCase(typeof(WebApiBffController), "Proxy")]
		public async Task A_web_posted_read_or_proxy_opted_out_still_stops_when_the_browser_leaves(Type controller, string action)
		{
			var token = await BindAsync(WebBinder, controller, action, "POST");

			token.IsCancellationRequested.Should().BeTrue();
		}

		[TestCase(typeof(V4.CallsController), "SaveCall", "POST")]
		[TestCase(typeof(V4.CallsController), "CloseCall", "PUT")]
		[TestCase(typeof(V4.CallsController), "EditCall", "PUT")]
		[TestCase(typeof(V4.UnitStatusController), "SaveUnitStatus", "POST")]
		[TestCase(typeof(V4.PersonnelStatusesController), "SavePersonStatus", "POST")]
		[TestCase(typeof(V4.RecordExportTemplatesController), "Run", "POST")]
		[TestCase(typeof(V4.AdminAssistController), "PlanCreate", "POST")]
		[TestCase(typeof(V4.AdminAssistController), "DeleteConversation", "POST")]
		public async Task An_api_write_keeps_going_after_the_app_leaves(Type controller, string action, string method)
		{
			var token = await BindAsync(ApiBinder, controller, action, method);

			token.CanBeCanceled.Should().BeFalse();
		}

		[TestCase(typeof(V4.CallsController), "GetActiveCalls")]
		[TestCase(typeof(V4.AdminAssistController), "Overview")]
		public async Task An_api_read_stops_when_the_app_leaves(Type controller, string action)
		{
			var token = await BindAsync(ApiBinder, controller, action, "GET");

			token.IsCancellationRequested.Should().BeTrue();
		}

		// Previews, reads and model answers; Ask and the diagnostics settle their own cancellation (meter, lease, audit).
		[TestCase(typeof(V4.AdminAssistController), "PlanDraft")]
		[TestCase(typeof(V4.AdminAssistController), "Plan")]
		[TestCase(typeof(V4.AdminAssistController), "PlanExport")]
		[TestCase(typeof(V4.AdminAssistController), "Diagnose")]
		[TestCase(typeof(V4.AdminAssistController), "Diagnostic")]
		[TestCase(typeof(V4.AdminAssistController), "DiagnosticSupportPreview")]
		[TestCase(typeof(V4.AdminAssistController), "DiagnosticSupportExport")]
		[TestCase(typeof(V4.AdminAssistController), "Impact")]
		[TestCase(typeof(V4.AdminAssistController), "CapacityImpact")]
		[TestCase(typeof(V4.AdminAssistController), "DispatchImpact")]
		[TestCase(typeof(V4.AdminAssistController), "PermissionImpact")]
		[TestCase(typeof(V4.AdminAssistController), "ModuleImpact")]
		[TestCase(typeof(V4.AdminAssistController), "TextImportImpact")]
		[TestCase(typeof(V4.AdminAssistController), "SecurityImpact")]
		[TestCase(typeof(V4.AdminAssistController), "NotificationImpact")]
		[TestCase(typeof(V4.AdminAssistController), "RetentionImpact")]
		[TestCase(typeof(V4.AdminAssistController), "Ask")]
		[TestCase(typeof(V4.RecordSavedReportsController), "Run")]
		public async Task An_api_posted_read_opted_out_still_stops_when_the_app_leaves(Type controller, string action)
		{
			var token = await BindAsync(ApiBinder, controller, action, "POST");

			token.IsCancellationRequested.Should().BeTrue();
		}

		[Test]
		public void Web_binder_answers_before_mvcs_own_cancellation_token_binder()
		{
			var services = new ServiceCollection();
			services.AddLogging();
			services.AddMvc().AddMvcOptions(WebFilters.MutationCancellationTokenBinderProvider.Register);

			ResolveCancellationTokenBinder(services).Should().BeOfType<WebFilters.MutationCancellationTokenBinder>();
		}

		[Test]
		public void Api_binder_answers_before_mvcs_own_cancellation_token_binder()
		{
			var services = new ServiceCollection();
			services.AddLogging();
			services.AddControllers(ApiFilters.MutationCancellationTokenBinderProvider.Register);

			ResolveCancellationTokenBinder(services).Should().BeOfType<ApiFilters.MutationCancellationTokenBinder>();
		}

		[TestCase("Web/Resgrid.Web/Startup.cs", "Filters.MutationCancellationTokenBinderProvider.Register(options)")]
		[TestCase("Web/Resgrid.Web.Services/Startup.cs", "Resgrid.Web.Services.Filters.MutationCancellationTokenBinderProvider.Register(options)")]
		public void Startup_registers_the_binder(string startupPath, string registration)
		{
			var startup = File.ReadAllText(FindRepositoryFile(startupPath));

			startup.Should().Contain(registration);
		}

		private static IModelBinder ResolveCancellationTokenBinder(IServiceCollection services)
		{
			var provider = services.BuildServiceProvider();

			var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
			options.ModelBinderProviders.Should().Contain(p => p.GetType().Name == "CancellationTokenModelBinderProvider",
				"MVC's own provider is still registered, so ours has to come first to win");

			var metadata = provider.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(CancellationToken));
			return provider.GetRequiredService<IModelBinderFactory>().CreateBinder(new ModelBinderFactoryContext
			{
				Metadata = metadata,
				BindingInfo = new BindingInfo { BindingSource = BindingSource.Special }
			});
		}

		private static async Task<CancellationToken> BindAsync(Func<ModelBindingContext, Task> bind, Type controller, string action, string method)
		{
			var verb = method switch
			{
				"POST" => typeof(HttpPostAttribute),
				"PUT" => typeof(HttpPutAttribute),
				_ => typeof(HttpGetAttribute)
			};
			var methodInfo = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == action)
				.OrderByDescending(m => m.GetCustomAttributes(verb, true).Any())
				.First();

			// The API runs endpoint routing and reads EndpointMetadata; the website runs legacy routing and reads the
			// attributes off the method and controller. Both are filled here, as each host would.
			var descriptor = new ControllerActionDescriptor
			{
				ControllerTypeInfo = controller.GetTypeInfo(),
				MethodInfo = methodInfo,
				ActionName = action,
				ControllerName = controller.Name,
				EndpointMetadata = controller.GetCustomAttributes(true).Concat(methodInfo.GetCustomAttributes(true)).ToList()
			};

			using var aborted = new CancellationTokenSource();
			aborted.Cancel();

			var http = new DefaultHttpContext { RequestAborted = aborted.Token };
			http.Request.Method = method;

			var bindingContext = DefaultModelBindingContext.CreateBindingContext(
				new ActionContext(http, new RouteData(), descriptor),
				new CompositeValueProvider(),
				new EmptyModelMetadataProvider().GetMetadataForType(typeof(CancellationToken)),
				new BindingInfo { BindingSource = BindingSource.Special },
				"cancellationToken");

			await bind(bindingContext);

			bindingContext.Result.IsModelSet.Should().BeTrue();
			return (CancellationToken)bindingContext.Result.Model;
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			if (directory == null)
				throw new InvalidOperationException("Unable to locate the repository root.");

			return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
		}
	}
}
