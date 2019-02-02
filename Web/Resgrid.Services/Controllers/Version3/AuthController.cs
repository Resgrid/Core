using System.Web.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.App_Start;
using System;
using System.Net;
using System.Web.Http;
using Resgrid.Web.Services.Controllers.Version2;
using Resgrid.Web.Services.Controllers.Version3.Models.Auth;
using RestSharp;
using System.Net.Http;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Service to generate an authentication token that is required to communicate with all other v3 services
	/// </summary>
	[RequireHttps]
	[JsonNetFormatterConfig]
	//[EnableCors(origins: "*", headers: "*", methods: "*")]
	public class AuthController : ApiController
	{
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;

		public AuthController(
			IUsersService usersService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService
			)
		{
			_usersService = usersService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
		}

		/// <summary>
		/// Generates a token that is then used for subsquent requests to the API.
		/// </summary>
		/// <param name="authInput">ValidateInput object with values populated</param>
		/// <returns>ValidateResult object, with IsValid set if the settings are correct</returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public ValidateResult Validate([FromBody]ValidateInput authInput)
		{
			if (this.ModelState.IsValid)
			{
				if (authInput == null)
					throw HttpStatusCode.BadRequest.AsException();

				var client = new RestClient(Config.SystemBehaviorConfig.ResgridBaseUrl);
				var request = new RestRequest($"/CoreBridge/ValidateLogIn", Method.POST);
				request.AddJsonBody(authInput);
				var response = client.Execute<Model.Results.ValidateLogInResult>(request);

				if (response.Data == null || !response.Data.Successful)
					throw HttpStatusCode.Unauthorized.AsException();

				var user = _usersService.GetUserByName(authInput.Usr);
				Department department = _departmentsService.GetDepartmentForUser(authInput.Usr);

				var result = new ValidateResult
				{
					Eml = user.Email,
					Uid = user.Id,
					Dnm = department.Name,
					Did = department.DepartmentId
				};

				if (department.CreatedOn.HasValue)
					result.Dcd = (department.CreatedOn.Value - new DateTime(1970, 1, 1).ToLocalTime()).TotalSeconds.ToString();
				else
					result.Dcd = new DateTime(1970, 1, 1).ToLocalTime().ToString();

				result.Tkn = V3AuthToken.Create(authInput.Usr, department.DepartmentId);
				result.Txd = DateTime.UtcNow.AddMonths(Config.SystemBehaviorConfig.APITokenMonthsTTL).ToShortDateString();

				var profile = _userProfileService.GetProfileByUserId(user.Id);
				result.Nme = profile.FullName.AsFirstNameLastName;

				return result;
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		public ActiveCompanyResult ValidateForDepartment([FromBody]SetActiveComapnyInput authInput)
		{
			if (this.ModelState.IsValid)
			{
				if (authInput == null)
					throw HttpStatusCode.BadRequest.AsException();

				// Hack while services is migrated to DotNetCore and can utilize the underlying calls
				var client = new RestClient(Config.SystemBehaviorConfig.ResgridBaseUrl);
				var request = new RestRequest($"/CoreBridge/ValidateLogIn", Method.POST);
				request.AddJsonBody(authInput);
				var response = client.Execute<Model.Results.ValidateLogInResult>(request);

				if (response.Data == null || !response.Data.Successful)
					throw HttpStatusCode.Unauthorized.AsException();

				var user = _usersService.GetUserByName(authInput.Usr);

				if (_departmentsService.IsMemberOfDepartment(authInput.Did, user.Id))
				{
					Department department = _departmentsService.GetDepartmentForUser(authInput.Usr);

					var result = new ActiveCompanyResult
					{
						Eml = user.Email,
						Uid = user.Id,
						Dnm = department.Name,
						Did = department.DepartmentId
					};

					if (department.CreatedOn.HasValue)
						result.Dcd = (department.CreatedOn.Value - new DateTime(1970, 1, 1).ToLocalTime()).TotalSeconds.ToString();
					else
						result.Dcd = new DateTime(1970, 1, 1).ToLocalTime().ToString();

					result.Tkn = V3AuthToken.Create(authInput.Usr, authInput.Did);
					result.Txd = DateTime.UtcNow.AddMonths(Config.SystemBehaviorConfig.APITokenMonthsTTL).ToShortDateString();

					var profile = _userProfileService.GetProfileByUserId(user.Id);
					result.Nme = profile.FullName.AsFirstNameLastName;

					return result;
				}

				throw HttpStatusCode.Unauthorized.AsException();
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
