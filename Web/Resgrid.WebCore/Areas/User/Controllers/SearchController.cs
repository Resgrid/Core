using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Providers.Claims;
using System.Collections.Generic;
using Resgrid.WebCore.Areas.User.Models.Search;
using Newtonsoft.Json;
using System.Linq;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class SearchController : SecureBaseController
	{
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public SearchController(Model.Services.IAuthorizationService authorizationService, IDepartmentSettingsService departmentSettingsService)
		{
			_authorizationService = authorizationService;
			_departmentSettingsService = departmentSettingsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> GetSearchResults(string query)
		{
			List<SearchResultJson> allActions = new List<SearchResultJson>();
			List<SearchResultJson> results = null;

			allActions.Add(new SearchResultJson
			{
				Label = "/Calls",
				Summary = "View Calls and Dispatches",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Dispatch/Dashboard"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Personnel",
				Summary = "View People (Personnel)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Personnel"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Units",
				Summary = "View Units (Teams or Apparatuses)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Units"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Mapping",
				Summary = "Large Map View which allows filtering and layers",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Mapping"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Shifts",
				Summary = "Shifts (Signup, Recurring, Workshift)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Shifts"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Logs",
				Summary = "Logs for activity in the department (Run, Training, Work, Meetings, Callbacks)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Logs"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/NewLog",
				Summary = "Create a new Log (i.e. Run Report, Training Log)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Logs/NewLog"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Reports",
				Summary = "Generate reports based on data in the Department",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Reports"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Calendar",
				Summary = "Calendar where you can schedule and signup to events, trainings",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Calendar"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Notes",
				Summary = "Department notes which are small bits of information",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Notes"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Documents",
				Summary = "Upload and Share documents (like pdfs, word docs, excel)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Documents"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Trainings",
				Summary = "Trainings, Study Guides Procedures for people to review",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Trainings"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Inventory",
				Summary = "Inventory for your Stations and Units",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Inventory"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Inbox",
				Summary = "View your Messages Inbox",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Messages/Inbox"
			});

			allActions.Add(new SearchResultJson
			{
				Label = "/Profile",
				Summary = "View and Edit your own User Profile",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Home/EditUserProfile?UserId=" + UserId
			});

			if (await _authorizationService.CanUserCreateCallAsync(UserId, DepartmentId))
			{
				allActions.Add(new SearchResultJson
				{
					Label = "/NewCall",
					Summary = "Create and Dispatch a new Call",
					Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Dispatch/NewCall"
				});
			}

			allActions.Add(new SearchResultJson
			{
				Label = "/ArchivedCalls",
				Summary = "View Archived Calls (old Calls)",
				Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Dispatch/ArchivedCalls"
			});

			if (await _authorizationService.CanUserAddNewUserAsync(DepartmentId, UserId))
			{
				allActions.Add(new SearchResultJson
				{
					Label = "/AddPerson",
					Summary = "Manually Create a User Account",
					Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Personnel/AddPerson"
				});

				allActions.Add(new SearchResultJson
				{
					Label = "/ManageInvites",
					Summary = "Send Email Invites for users to create their own Accounts",
					Url = Config.SystemBehaviorConfig.ResgridBaseUrl + "/User/Department/Invites"
				});
			}

			if (string.IsNullOrWhiteSpace(query))
				results = allActions;
			else
			{
				var querySet = query.Trim().ToLower();
				results = allActions.Where(x => x.Label.ToLower().Contains(querySet) || x.Summary.ToLower().Contains(querySet)).ToList();
			}

			return Content(JsonConvert.SerializeObject(results), "application/json");// Json(results);
		}
	}
}
