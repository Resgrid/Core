using System;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Controllers
{
	[EnableCors(origins: "*", headers: "*", methods: "*")]
	[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
	public class FeedsController : ApiController
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

		public Rss20FeedFormatter GetActiveCallsAsRSS(string key)
		{
			if (String.IsNullOrWhiteSpace(key))
				return null;

			var departmentId = _departmentSettingsService.GetDepartmentIdForRssKey(key);

			if (!departmentId.HasValue)
				return null;

			var department = _departmentsService.GetDepartmentById(departmentId.Value);
			var calls = _callsService.GetActiveCallsByDepartment(departmentId.Value);

			var feed = new SyndicationFeed(string.Format("{0} Active Calls", department.Name), string.Format("The active calls for the department {0}", department.Name), new Uri(Config.SystemBehaviorConfig.ResgridBaseUrl));
			feed.Authors.Add(new SyndicationPerson("team@resgrid.com"));
			feed.Categories.Add(new SyndicationCategory("Resgrid Calls"));
			feed.Description = new TextSyndicationContent(string.Format("The active calls for the department {0}", department.Name));
			feed.Items = calls.Select(call => new SyndicationItem(call.Name, call.NatureOfCall, new Uri($"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/ViewCall?callId=" + call.CallId), call.CallId.ToString(), call.LoggedOn)).ToList();

			return new Rss20FeedFormatter(feed);
		}

		public Atom10FeedFormatter GetActiveCallsAsAtom(string key)
		{
			if (String.IsNullOrWhiteSpace(key))
				return null;

			var departmentId = _departmentSettingsService.GetDepartmentIdForRssKey(key);

			if (!departmentId.HasValue)
				return null;

			var department = _departmentsService.GetDepartmentById(departmentId.Value);
			var calls = _callsService.GetActiveCallsByDepartment(departmentId.Value);

			var feed = new SyndicationFeed(string.Format("{0} Active Calls", department.Name), string.Format("The active calls for the department {0}", department.Name), new Uri(Config.SystemBehaviorConfig.ResgridBaseUrl));
			feed.Authors.Add(new SyndicationPerson("team@resgrid.com"));
			feed.Categories.Add(new SyndicationCategory("Resgrid Calls"));
			feed.Description = new TextSyndicationContent(string.Format("The active calls for the department {0}", department.Name));
			feed.Items = calls.Select(call => new SyndicationItem(call.Name, call.NatureOfCall, new Uri($"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/ViewCall?callId=" + call.CallId), call.CallId.ToString(), call.LoggedOn)).ToList();

			return new Atom10FeedFormatter(feed);
		}
	}
}
