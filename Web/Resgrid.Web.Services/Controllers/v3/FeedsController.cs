using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Controllers.Version3
{
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	//[EnableCors("_resgridWebsiteAllowSpecificOrigins")]
	public class FeedsController : ControllerBase
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
		public async Task<IActionResult> GetActiveCallsAsRSS(string key)
		{
			if (String.IsNullOrWhiteSpace(key))
				return null;

			var departmentId = await _departmentSettingsService.GetDepartmentIdForRssKeyAsync(key);

			if (!departmentId.HasValue)
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(departmentId.Value);

			var feed = new SyndicationFeed(string.Format("{0} Active Calls", department.Name), string.Format("The active calls for the department {0}", department.Name), new Uri(Config.SystemBehaviorConfig.ResgridBaseUrl));
			feed.Authors.Add(new SyndicationPerson("team@resgrid.com"));
			feed.Categories.Add(new SyndicationCategory("Resgrid Calls"));
			feed.Description = new TextSyndicationContent(string.Format("The active calls for the department {0}", department.Name));
			feed.Items = calls.Select(call => new SyndicationItem(call.Name, call.NatureOfCall, new Uri($"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/ViewCall?callId=" + call.CallId), call.CallId.ToString(), call.LoggedOn)).ToList();


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

		[HttpGet("GetActiveCallsAsAtom")]
		[Produces(MediaTypeNames.Text.Xml)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<Atom10FeedFormatter> GetActiveCallsAsAtom(string key)
		{
			if (String.IsNullOrWhiteSpace(key))
				return null;

			var departmentId = await _departmentSettingsService.GetDepartmentIdForRssKeyAsync(key);

			if (!departmentId.HasValue)
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value);
			var calls =  await _callsService.GetActiveCallsByDepartmentAsync(departmentId.Value);

			var feed = new SyndicationFeed(string.Format("{0} Active Calls", department.Name), string.Format("The active calls for the department {0}", department.Name), new Uri(Config.SystemBehaviorConfig.ResgridBaseUrl));
			feed.Authors.Add(new SyndicationPerson("team@resgrid.com"));
			feed.Categories.Add(new SyndicationCategory("Resgrid Calls"));
			feed.Description = new TextSyndicationContent(string.Format("The active calls for the department {0}", department.Name));
			feed.Items = calls.Select(call => new SyndicationItem(call.Name, call.NatureOfCall, new Uri($"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/ViewCall?callId=" + call.CallId), call.CallId.ToString(), call.LoggedOn)).ToList();

			return new Atom10FeedFormatter(feed);
		}
	}
}
