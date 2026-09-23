using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using NUnit.Framework;
using Resgrid.Web.ServicesCore;
using Resgrid.Web.Services.Controllers.v4;
using Swashbuckle.AspNetCore.Swagger;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Generates the real v4 OpenAPI document with Startup's Swagger settings. Swashbuckle refuses the whole
	/// document when two visible actions share a method and path (RESGRID-API-9F), so any new conflict fails here
	/// instead of at /swagger/v4/swagger.json in production.
	/// </summary>
	[TestFixture]
	public class SwaggerDocumentTests
	{
		private const string UploadTemplate = "api/v{VersionId:apiVersion}/ChecklistRuns/UploadChecklistRunFile";

		private static WebApplication BuildApp()
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.Services.AddControllers().AddApplicationPart(typeof(Startup).Assembly).AddNewtonsoftJson();
			builder.Services.AddApiVersioning();
			builder.Services.AddSwaggerGen();
			builder.Services.AddSwaggerGenNewtonsoftSupport();
			builder.Services.ConfigureSwaggerGen(Startup.ConfigureSwagger);
			return builder.Build();
		}

		[Test]
		public void V4_document_generates_without_conflicting_method_path_pairs()
		{
			using var app = BuildApp();
			var provider = app.Services.GetRequiredService<ISwaggerProvider>();

			OpenApiDocument document = null;
			FluentActions.Invoking(() => document = provider.GetSwagger("v4")).Should().NotThrow();

			document.Paths.Should().NotBeEmpty();
			var upload = document.Paths["/api/v{VersionId}/ChecklistRuns/UploadChecklistRunFile"].Operations[OperationType.Post];
			upload.RequestBody.Content.Keys.Should().Contain("multipart/form-data");
		}

		[Test]
		public void Checklist_evidence_json_transport_stays_routable_on_the_upload_path()
		{
			// Responder and Unit post base64 evidence as JSON to UploadChecklistRunFile; the action is hidden from
			// the document only, so the route and its Consumes constraint must remain.
			using var app = BuildApp();
			var actions = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
				.OfType<ControllerActionDescriptor>()
				.Where(a => a.ControllerTypeInfo.AsType() == typeof(ChecklistRunsController) && a.AttributeRouteInfo?.Template == UploadTemplate)
				.ToList();

			string[] ConsumedTypes(string actionName) => actions.Single(a => a.ActionName == actionName).ActionConstraints
				.OfType<Microsoft.AspNetCore.Mvc.ConsumesAttribute>().SelectMany(c => c.ContentTypes).ToArray();

			ConsumedTypes(nameof(ChecklistRunsController.UploadChecklistRunFile)).Should().Equal("multipart/form-data");
			ConsumedTypes(nameof(ChecklistRunsController.UploadChecklistRunEvidence)).Should().Equal("application/json");
		}
	}
}
