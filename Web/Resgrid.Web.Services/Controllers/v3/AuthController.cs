using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using Resgrid.Web.Services.Controllers.Version3.Models.Auth;
using RestSharp;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.ServicesCore.Middleware;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Service to generate an authentication token that is required to communicate with all other v3 services
	/// </summary>
#if (!DEBUG && !DOCKER)
	//[RequireHttps]
#endif
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiController]
	[AllowAnonymous]
	[ApiExplorerSettings(GroupName = "v3")]
	//[EnableCors("_resgridWebsiteAllowSpecificOrigins")]
	public class AuthController : ControllerBase
	{
		private readonly SignInManager<Model.Identity.IdentityUser> _signInManager;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ISystemAuditsService _systemAuditsService;

		public AuthController(
			IUsersService usersService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService,
			SignInManager<Model.Identity.IdentityUser> signInManager,
			ISystemAuditsService systemAuditsService
			)
		{
			_usersService = usersService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_signInManager = signInManager;
			_systemAuditsService = systemAuditsService;
		}

		/// <summary>
		/// Generates a token that is then used for subsquent requests to the API.
		/// </summary>
		/// <param name="authInput">ValidateInput object with values populated</param>
		/// <returns>ValidateResult object, with IsValid set if the settings are correct</returns>
		[HttpPost("Validate")]
		[AllowAnonymous]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<ValidateResult>> Validate([FromBody]ValidateInput authInput)
		{
			if (this.ModelState.IsValid)
			{
				if (authInput == null)
					return BadRequest();

				var signInResult = await _signInManager.PasswordSignInAsync(authInput.Usr, authInput.Pass, true, lockoutOnFailure: true);
				SystemAudit audit = new SystemAudit();
				audit.System = (int)SystemAuditSystems.Api;
				audit.Type = (int)SystemAuditTypes.Login;
				audit.Username = authInput.Usr;
				audit.Successful = signInResult.Succeeded;
				audit.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				audit.ServerName = Environment.MachineName;
				audit.Data = $"V3 Validate, {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				await _systemAuditsService.SaveSystemAuditAsync(audit);

				if (signInResult.Succeeded)
				{
					if (await _usersService.DoesUserHaveAnyActiveDepartments(authInput.Usr))
					{
						var user = await _usersService.GetUserByNameAsync(authInput.Usr);
						Department department = await _departmentsService.GetDepartmentForUserAsync(authInput.Usr);

						var result = new ValidateResult
						{
							Eml = user.Email,
							Uid = user.Id,
							Dnm = department.Name,
							Did = department.DepartmentId
						};

						if (department.CreatedOn.HasValue)
							result.Dcd = (department.CreatedOn.Value - new DateTime(1970, 1, 1).ToLocalTime())
								.TotalSeconds.ToString();
						else
							result.Dcd = new DateTime(1970, 1, 1).ToLocalTime().ToString();

						result.Tkn = V3AuthToken.Create(authInput.Usr, department.DepartmentId);
						result.Txd = DateTime.UtcNow.AddMonths(Config.SystemBehaviorConfig.APITokenMonthsTTL)
							.ToShortDateString();

						var profile = await _userProfileService.GetProfileByUserIdAsync(user.Id);
						result.Nme = profile.FullName.AsFirstNameLastName;

						return result;
					}
				}
			}

			return BadRequest();
		}

		[AllowAnonymous]
		[HttpPost("ValidateForDepartment")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<ActiveCompanyResult>> ValidateForDepartment([FromBody]SetActiveComapnyInput authInput)
		{
			if (this.ModelState.IsValid)
			{
				if (authInput == null)
					return BadRequest();

				var signInResult = await _signInManager.PasswordSignInAsync(authInput.Usr, authInput.Pass, true, lockoutOnFailure: true);
				SystemAudit audit = new SystemAudit();
				audit.System = (int)SystemAuditSystems.Api;
				audit.Type = (int)SystemAuditTypes.Login;
				audit.Username = authInput.Usr;
				audit.Successful = signInResult.Succeeded;
				audit.IpAddress = Request.HttpContext.Connection.RemoteIpAddress.ToString();
				audit.ServerName = Environment.MachineName;
				audit.Data = $"V3 ValidateForDepartment, {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				await _systemAuditsService.SaveSystemAuditAsync(audit);

				if (signInResult.Succeeded)
				{
					if (await _usersService.DoesUserHaveAnyActiveDepartments(authInput.Usr))
					{

						var user = await _usersService.GetUserByNameAsync(authInput.Usr);

						if (await _departmentsService.IsMemberOfDepartmentAsync(authInput.Did, user.Id))
						{
							Department department = await _departmentsService.GetDepartmentForUserAsync(authInput.Usr);

							var result = new ActiveCompanyResult
							{
								Eml = user.Email,
								Uid = user.Id,
								Dnm = department.Name,
								Did = department.DepartmentId
							};

							if (department.CreatedOn.HasValue)
								result.Dcd = (department.CreatedOn.Value - new DateTime(1970, 1, 1).ToLocalTime())
									.TotalSeconds.ToString();
							else
								result.Dcd = new DateTime(1970, 1, 1).ToLocalTime().ToString();

							result.Tkn = V3AuthToken.Create(authInput.Usr, authInput.Did);
							result.Txd = DateTime.UtcNow.AddMonths(Config.SystemBehaviorConfig.APITokenMonthsTTL)
								.ToShortDateString();

							var profile = await _userProfileService.GetProfileByUserIdAsync(user.Id);
							result.Nme = profile.FullName.AsFirstNameLastName;

							return Ok(result);
						}
					}
				}

				return Unauthorized();
			}

			return BadRequest();
		}
	}
}
