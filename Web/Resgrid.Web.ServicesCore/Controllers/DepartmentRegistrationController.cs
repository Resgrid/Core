using System;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Services.Models;
using RestSharp;

namespace Resgrid.Web.Services.Controllers
{
	[Produces("application/json")]
	[ApiController]
	[Route("[controller]")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class DepartmentRegistrationController : ControllerBase
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

		// TODO: Rate limit
		[HttpGet("CheckIfEmailInUse")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<EmailCheckResult> CheckIfEmailInUse(string emailAddress)
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

			return BadRequest();
		}

		// TODO: Rate limit
		[HttpGet("CheckIfDepartmentNameUsed")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<DepartmentCheckResult>> CheckIfDepartmentNameUsed(string departmentName)
		{
			var result = new DepartmentCheckResult();
			if (!String.IsNullOrWhiteSpace(departmentName))
			{
				var name = HttpUtility.UrlDecode(departmentName);
				result.DepartmentNameInUse = await _departmentsService.DoesDepartmentExistAsync(name);

				return result;
			}

			return BadRequest();
		}

		// TODO: Rate limit
		[HttpGet("CheckIfUserNameUsed")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<UsernameCheckResult>> CheckIfUserNameUsed(string userName)
		{
			var result = new UsernameCheckResult();
			if (!String.IsNullOrWhiteSpace(userName))
			{
				var name = HttpUtility.UrlDecode(userName);
				var user = await _usersService.GetUserByNameAsync(name);

				if (user != null)
					result.UserNameInUse = true;
				else
					result.UserNameInUse = false;

				return result;
			}

			return BadRequest();
		}

		[HttpPost("Register")]
		public async Task<ActionResult<DepartmentCreationResult>> Register(DepartmentCreationInput model)
		{
			var result = new DepartmentCreationResult();

			if (ModelState.IsValid)
			{

				//TODO: No more CoreBridge, so fix yo.
				var client = new RestClient(Config.SystemBehaviorConfig.ResgridBaseUrl);
				var request = new RestRequest($"/CoreBridge/RegisterDepartment", Method.Post);
				request.AddJsonBody(model);
				var response = await client.ExecuteAsync<DepartmentCreationResult>(request);

				if (response.Data != null && !response.Data.Successful)
					return BadRequest();

				result.Successful = true;
				return result;
			}

			return BadRequest();
		}
	}
}
