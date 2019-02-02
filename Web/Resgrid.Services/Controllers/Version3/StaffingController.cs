using System.Net;
using System.Net.Http;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Staffing;
using System;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against user statuses and their actions
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class StaffingController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserStateService _userStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;

		public StaffingController(
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IUserStateService userStateService,
			IUserProfileService userProfileService,
			IAuthorizationService authorizationService)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_userStateService = userStateService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
		}

		/// <summary>
		/// Gets the current staffing level (state) for the user
		/// </summary>
		/// <returns>StateResult object with the users current staffing level</returns>
		public StaffingResult GetCurrentStaffing()
		{
			var profile = _userProfileService.GetProfileByUserId(UserId);

			if (profile == null)
				throw HttpStatusCode.Unauthorized.AsException();

			var userState = _userStateService.GetLastUserStateByUserId(UserId);

			var result = new StaffingResult
			{
				Uid = UserId.ToString(),
				Nme = profile.FullName.AsFirstNameLastName,
				Typ = userState.State,
				Tms = userState.Timestamp.TimeConverter(_departmentsService.GetDepartmentById(DepartmentId, false)),
				Not = userState.Note
			};

			return result;
		}

		/// <summary>
		/// Sets the staffing level (state) for the current user.
		/// </summary>
		/// <param name="staffingInput">StateInput object with the State to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[AcceptVerbs("POST")]
		public HttpResponseMessage SetStaffing(StaffingInput staffingInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					if (String.IsNullOrWhiteSpace(staffingInput.Not))
						_userStateService.CreateUserState(UserId, DepartmentId, staffingInput.Typ);
					else
						_userStateService.CreateUserState(UserId, DepartmentId, staffingInput.Typ, staffingInput.Not);

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		/// <summary>
		/// Sets the staffing level (state) for the UserId specificed in the input data.
		/// </summary>
		/// <param name="staffingInput">StateInput object with the State to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[AcceptVerbs("PUT")]
		public HttpResponseMessage SetStaffingForUser(StaffingInput staffingInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var userToSetStatusFor = _departmentsService.GetDepartmentMember(staffingInput.Uid, DepartmentId);

					if (userToSetStatusFor == null)
						throw HttpStatusCode.NotFound.AsException();

					if (!_authorizationService.IsUserValidWithinLimits(staffingInput.Uid, DepartmentId))
						throw HttpStatusCode.Unauthorized.AsException();

					if (!_authorizationService.IsUserValidWithinLimits(userToSetStatusFor.UserId, DepartmentId))
						throw HttpStatusCode.Unauthorized.AsException();

					// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

					if (String.IsNullOrWhiteSpace(staffingInput.Not))
						_userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, staffingInput.Typ);
					else
						_userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, staffingInput.Typ, staffingInput.Not);

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
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
