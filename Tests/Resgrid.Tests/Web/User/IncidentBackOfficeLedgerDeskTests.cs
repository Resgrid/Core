using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The generic Incident Back Office ledger desk: every save posts the table key in the query string (?kind=IncidentResource)
	/// and the row's columns in the form. 23 tables have a column named Kind, and MVC binds form values ahead of the query string,
	/// so the posted column replaced the table key and every save of a resource, request or budget line came back 404.
	/// </summary>
	[TestFixture]
	public class IncidentBackOfficeLedgerDeskTests
	{
		[TestCase(nameof(IncidentBackOfficeControllerBase.Save))]
		[TestCase(nameof(IncidentBackOfficeControllerBase.Delete))]
		[TestCase(nameof(IncidentBackOfficeControllerBase.Transition))]
		public async Task A_posted_Kind_column_never_replaces_the_table_key(string action)
		{
			var parameter = typeof(IncidentBackOfficeControllerBase).GetMethod(action).GetParameters().Single(p => p.Name == "kind");

			var kind = await BindAsync(parameter, query: "?kind=IncidentResource", form: new Dictionary<string, StringValues> { ["Kind"] = "3", ["Name"] = "Engine 12" });

			kind.Should().Be("IncidentResource");
		}

		[Test]
		public async Task A_posted_policy_field_never_replaces_the_settings_section()
		{
			// The settings form posts one field per policy property; ?section= says which policy is being saved.
			var parameter = typeof(IncidentBackOfficeController).GetMethods().Where(m => m.Name == nameof(IncidentBackOfficeController.Settings))
				.SelectMany(m => m.GetParameters()).Single(p => p.Name == "section");

			var section = await BindAsync(parameter, query: "?section=Forecasts", form: new Dictionary<string, StringValues> { ["Section"] = "Retention", ["MealContingencyPercent"] = "10" });

			section.Should().Be("Forecasts");
		}

		[Test]
		public async Task Without_an_explicit_source_the_posted_column_would_win()
		{
			// Documents why the attribute is needed: an unattributed string parameter takes the form value over the query string.
			var parameter = typeof(IncidentBackOfficeLedgerDeskTests).GetMethod(nameof(Unattributed), BindingFlags.NonPublic | BindingFlags.Static).GetParameters()[0];

			var kind = await BindAsync(parameter, query: "?kind=IncidentResource", form: new Dictionary<string, StringValues> { ["Kind"] = "3" });

			kind.Should().Be("3");
		}

		[TestCase("America/Los_Angeles")]
		[TestCase("Pacific Standard Time")]
		[TestCase("Asia/Tokyo")]
		[TestCase("UTC")]
		public void A_new_rows_unset_times_render_blank_instead_of_throwing(string zone)
		{
			// A new row's non-nullable DateTime columns (a commitment's CommittedOn, a lifeline's AsOf) are default(DateTime) until
			// the first save; shifting 0001-01-01 west of UTC used to throw "LocalDateTime out of range of DateTime".
			var time = new IncidentTime(zone, null);

			time.Input(default(DateTime)).Should().BeNull();
			time.Format(default(DateTime)).Should().BeNull();
			time.Time(default(DateTime)).Should().BeNull();
			IncidentTime.Date(default(DateTime)).Should().BeNull();
			time.Invoking(t => t.Local(DateTime.MinValue)).Should().NotThrow();
			time.Invoking(t => t.Local(DateTime.MaxValue)).Should().NotThrow();
		}

		[Test]
		public void Set_times_still_render_in_the_incident_zone()
		{
			var time = new IncidentTime("America/Los_Angeles", "UTC");
			var utc = new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc);

			time.Input(utc).Should().Be("2026-10-27T12:30");
			time.Format(utc).Should().Be("2026-10-27 12:30");
			time.Time(utc).Should().Be("12:30");
			time.Parse("2026-10-27T12:30").Should().Be(utc);
			IncidentTime.Date(new DateTime(2026, 10, 26)).Should().Be("2026-10-26");
		}

		private static void Unattributed(string kind) { }

		private static async Task<string> BindAsync(ParameterInfo parameter, string query, IDictionary<string, StringValues> form)
		{
			var services = new ServiceCollection();
			services.AddLogging();
			services.AddMvc();
			using var provider = services.BuildServiceProvider();

			// A real urlencoded body, parsed by ASP.NET the way a browser post is: form keys match case-insensitively, which is
			// what lets the Kind column answer a lookup for kind.
			var body = Encoding.UTF8.GetBytes(string.Join("&", form.Select(f => Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value.ToString()))));
			var http = new DefaultHttpContext { RequestServices = provider };
			http.Request.Method = "POST";
			http.Request.QueryString = new QueryString(query);
			http.Request.ContentType = "application/x-www-form-urlencoded";
			http.Request.ContentLength = body.Length;
			http.Request.Body = new MemoryStream(body);

			var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
			var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
			var valueProvider = await CompositeValueProvider.CreateAsync(actionContext, options.ValueProviderFactories);

			var descriptor = new ControllerParameterDescriptor
			{
				Name = parameter.Name,
				ParameterInfo = parameter,
				ParameterType = parameter.ParameterType,
				BindingInfo = BindingInfo.GetBindingInfo(parameter.GetCustomAttributes())
			};
			var metadata = ((ModelMetadataProvider)provider.GetRequiredService<IModelMetadataProvider>()).GetMetadataForParameter(parameter);
			var binder = provider.GetRequiredService<IModelBinderFactory>().CreateBinder(new ModelBinderFactoryContext
			{
				BindingInfo = descriptor.BindingInfo,
				Metadata = metadata,
				CacheToken = descriptor
			});

			var result = await provider.GetRequiredService<ParameterBinder>().BindModelAsync(actionContext, binder, valueProvider, descriptor, metadata, null, null);
			return result.IsModelSet ? (string)result.Model : null;
		}
	}
}
