using System;
using System.Net;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Security;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Services.Models;
using Membership = System.Web.Security.Membership;
using RestSharp;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers
{
	[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
	[EnableCors(origins: Config.ApiConfig.CorsAllowedHostnames, headers: "*", methods: Config.ApiConfig.CorsAllowedMethods, SupportsCredentials = true)]
	public class DepartmentRegistrationController : ApiController
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IEmailService _emailService;
		private readonly IInvitesService _invitesService;
		private readonly IUserProfileService _userProfileService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IAffiliateService _affiliateService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IEmailMarketingProvider _emailMarketingProvider;

		public DepartmentRegistrationController(IDepartmentsService departmentsService, IUsersService usersService, IEmailService emailService,
			IInvitesService invitesService, IUserProfileService userProfileService,	ISubscriptionsService subscriptionsService, IAffiliateService affiliateService, 
			IEventAggregator eventAggregator, IEmailMarketingProvider emailMarketingProvider)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_emailService = emailService;
			_invitesService = invitesService;
			_userProfileService = userProfileService;
			_subscriptionsService = subscriptionsService;
			_affiliateService = affiliateService;
			_eventAggregator = eventAggregator;
			_emailMarketingProvider = emailMarketingProvider;
		}

		[HttpGet]
		public EmailCheckResult CheckIfEmailInUse(string emailAddress)
		{
			var result = new EmailCheckResult();
			if (!String.IsNullOrWhiteSpace(emailAddress))
			{
				var email = HttpUtility.UrlDecode(emailAddress);
				var user = _usersService.GetUserByEmail(email);

				if (user != null)
					result.EmailInUse = true;

				return result;
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		[HttpGet]
		public DepartmentCheckResult CheckIfDepartmentNameUsed(string departmentName)
		{
			var result = new DepartmentCheckResult();
			if (!String.IsNullOrWhiteSpace(departmentName))
			{
				var name = HttpUtility.UrlDecode(departmentName);
				result.DepartmentNameInUse = _departmentsService.DoesDepartmentExist(name);

				return result;
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		[HttpGet]
		public UsernameCheckResult CheckIfUserNameUsed(string userName)
		{
			var result = new UsernameCheckResult();
			if (!String.IsNullOrWhiteSpace(userName))
			{
				var name = HttpUtility.UrlDecode(userName);
				var user = _usersService.GetUserByName(name);

				if (user != null)
					result.UserNameInUse = true;
				else
					result.UserNameInUse = false;

				return result;
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		[HttpPost]
		public DepartmentCreationResult Register(DepartmentCreationInput model)
		{
			var result = new DepartmentCreationResult();

			if (ModelState.IsValid)
			{

				var client = new RestClient(Config.SystemBehaviorConfig.ResgridBaseUrl);
				var request = new RestRequest($"/CoreBridge/RegisterDepartment", Method.POST);
				request.AddJsonBody(model);
				var response = client.Execute<DepartmentCreationResult>(request);

				if (response.Data != null && !response.Data.Successful)
					throw HttpStatusCode.BadRequest.AsException();

				result.Successful = true;
				return result;
			}

			throw HttpStatusCode.BadRequest.AsException();
		}
	}
}
