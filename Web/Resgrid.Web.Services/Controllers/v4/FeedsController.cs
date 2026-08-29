using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Web.Services.Models.v4.Health;
using System.Reflection;
using Resgrid.Web.Services.Helpers;
using System.Net.Mime;
using System.ServiceModel.Syndication;
using System;
using System.Linq;
using System.Xml;
using System.Text;
using System.IO;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Gets calls and other data formatted for different feed formats, like RSS.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class FeedsController : V4AuthenticatedApiControllerbase
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallsService _callsService;

		public FeedsController(IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService, ICallsService callsService)
		{
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_callsService = callsService;
		}

		[HttpGet("GetActiveCallsAsRSS")]
		[Produces(MediaTypeNames.Text.Xml)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[AllowAnonymous]
		public async Task<IActionResult> GetActiveCallsAsRSS(string key)
		{
			if (String.IsNullOrWhiteSpace(key))
				return NotFound();

			var departmentId = await _departmentSettingsService.GetDepartmentIdForRssKeyAsync(key);

			if (!departmentId.HasValue)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(departmentId.Value);

			var feed = new SyndicationFeed(string.Format("{0} Active Calls", department.Name), string.Format("The active calls for the department {0}", department.Name), new Uri(Config.SystemBehaviorConfig.ResgridBaseUrl));
			feed.Authors.Add(new SyndicationPerson("team@resgrid.com"));
			feed.Categories.Add(new SyndicationCategory("Resgrid Calls"));
			feed.Description = new TextSyndicationContent(string.Format("The active calls for the department {0}", department.Name));

			// ADP egress (plan 5.5): this is an anonymous integration feed — protected departments'
			// enveloped values must never leave as content (nor as ciphertext). Generic titles keep
			// the feed functional; details require signing in.
			feed.Items = calls.Select(call => new SyndicationItem(
				Resgrid.Model.ProtectedDataEnvelope.HasEnvelopePrefix(call.Name)
					? $"Protected dispatch {call.Number}"
					: call.Name,
				Resgrid.Model.ProtectedDataEnvelope.HasEnvelopePrefix(call.NatureOfCall)
					? "A protected dispatch is available. Sign in to Resgrid to view details."
					: call.NatureOfCall,
				new Uri($"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/ViewCall?callId=" + call.CallId),
				call.CallId.ToString(), call.LoggedOn)).ToList();


			var settings = new XmlWriterSettings
			{
				Encoding = Encoding.UTF8,
				NewLineHandling = NewLineHandling.Entitize,
				NewLineOnAttributes = true,
				Indent = true
			};

			using (var stream = new MemoryStream())
			{
				using (var xmlWriter = XmlWriter.Create(stream, settings))
				{
					var rssFormatter = new Rss20FeedFormatter(feed, false);
					rssFormatter.WriteTo(xmlWriter);
					xmlWriter.Flush();
				}
				return File(stream.ToArray(), "application/rss+xml; charset=utf-8");
			}
		}
	}
}
