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
	public class RedirectController : ApiController
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallsService _callsService;

		public RedirectController(IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService, ICallsService callsService)
		{
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_callsService = callsService;
		}
	}
}
