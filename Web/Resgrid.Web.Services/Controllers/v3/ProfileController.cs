using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.Services.Controllers.Version3.Models.Profile;
using Resgrid.Model.Events;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against a logged in users (determined via the token) profile
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class ProfileController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ILimitsService _limitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IAddressService _addressService;
		private readonly IEventAggregator _eventAggregator;

		public ProfileController(IUsersService usersService, IDepartmentsService departmentsService, ILimitsService limitsService,
			IUserProfileService userProfileService, IAuthorizationService authorizationService, IAddressService addressService,
			IEventAggregator eventAggregator)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_limitsService = limitsService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
			_addressService = addressService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>
		/// Gets the mobile carriers in the Resgrid system. If you need a mobile carrier added contact team@resgrid.com.
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetMobileCarriers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult<List<MobileCarriersResult>> GetMobileCarriers()
		{
			var carriers = new List<MobileCarriersResult>();

			foreach (var field in typeof(MobileCarriers).GetFields())
			{
				DescriptionAttribute attribute = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;

				if (attribute == null)
					continue;

				carriers.Add(new MobileCarriersResult()
				{
					Cid = (int)field.GetValue(null),
					Nme = attribute.Description
				});
			}
			
			return Ok(carriers);
		}

		/// <summary>
		/// Gets the time zones in the Resgrid system. If you need a time zone added or corrected contact team@resgrid.com.
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetTimeZones")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult<List<TimeZoneResult>> GetTimeZones()
		{
			return Ok(TimeZones.Zones.Select(zone => new TimeZoneResult()
			{
				Id = zone.Key, Nme = zone.Value
			}).ToList());
		}

		/// <summary>
		/// Gets the Resgrid user profile for the user
		/// </summary>
		/// <returns>ProfileResult object with the users profile data</returns>
		[HttpGet("GetProfile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProfileResult>> GetProfile()
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(UserId.ToUpper(), true);

			if (profile == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var dm = await _departmentsService.GetDepartmentMemberAsync(UserId.ToUpper(), DepartmentId);
			var membership = _usersService.GetMembershipByUserId(UserId.ToUpper());

			var result = new ProfileResult
			{
				Uid = UserId.ToUpper().ToString(),
				Adm = department.IsUserAnAdmin(UserId.ToUpper()),
				Hid = dm.IsHidden.GetValueOrDefault(),
				Dis = dm.IsDisabled.GetValueOrDefault(),
				Fnm = profile.FirstName,
				Lnm = profile.LastName,
				Eml = membership.Email,
				Tz = profile.TimeZone,
				Mob = profile.MobileNumber,
				Moc = profile.MobileCarrier,
				Hmn = profile.HomeNumber,
				Sce = profile.SendEmail,
				Scp = profile.SendPush,
				Scs = profile.SendSms,
				Sme = profile.SendMessageEmail,
				Smp = profile.SendMessagePush,
				Sms = profile.SendMessageSms,
				Sne = profile.SendNotificationEmail,
				Snp = profile.SendNotificationPush,
				Sns = profile.SendNotificationSms,
				Id = profile.IdentificationNumber,
				Val = await _limitsService.CanDepartmentUseVoiceAsync(DepartmentId),
				Voc = profile.VoiceForCall,
				Vcm = profile.VoiceCallMobile,
				Vch = profile.VoiceCallHome,
				Lup = profile.LastUpdated
			};

			if (membership.LockoutEnd.HasValue)
				result.Lkd = true;
			else
				result.Lkd = false;

			if (profile.HomeAddressId.HasValue)
			{
				var address = await _addressService.GetAddressByIdAsync(profile.HomeAddressId.Value);

				if (address != null)
				{
					result.Hme = new AddressResult()
					{
						Aid = address.AddressId,
						Str = address.Address1,
						Cty = address.City,
						Ste = address.State,
						Zip = address.PostalCode,
						Cnt = address.Country
					};
				}
			}

			if (profile.MailingAddressId.HasValue)
			{
				var address = await _addressService.GetAddressByIdAsync(profile.MailingAddressId.Value);

				if (address != null)
				{
					result.Mal = new AddressResult()
					{
						Aid = address.AddressId,
						Str = address.Address1,
						Cty = address.City,
						Ste = address.State,
						Zip = address.PostalCode,
						Cnt = address.Country
					};
				}
			}

			return Ok(result);
		}

		/// <summary>
		/// Toggles a users profile to enable/disable custom push sounds when the app is backgrounded.
		/// </summary>
		/// <returns>An HTTP Result</returns>
		[HttpGet("ToggleCustomPushSounds")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult ToggleCustomPushSounds(bool enableCustomPushSounds, CancellationToken cancellationToken)
		{
			return Ok();
		}

		/// <summary>
		/// Updates a users profile
		/// </summary>
		/// <returns>An HTTP Result</returns>
		[HttpPost("UpdateProfile")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> UpdateProfile(UpdateProfileInput input, CancellationToken cancellationToken)
		{
			var result = Ok();

			var profile = await _userProfileService.GetProfileByUserIdAsync(UserId.ToUpper());

			if (profile == null)
				return NotFound();

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.ProfileUpdated;
			auditEvent.Before = profile.CloneJsonToString();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var dm = await _departmentsService.GetDepartmentMemberAsync(UserId.ToUpper(), DepartmentId);
			var membership = _usersService.GetMembershipByUserId(UserId.ToUpper());


			if (!String.IsNullOrWhiteSpace(input.Email) && membership.Email != input.Email)
			{
				membership.Email = input.Email;
				_usersService.SaveUser(membership);
			}

			if (!String.IsNullOrWhiteSpace(input.FirstName) && profile.FirstName != input.FirstName)
				profile.FirstName = input.FirstName;

			if (!String.IsNullOrWhiteSpace(input.LastName) && profile.LastName != input.LastName)
				profile.LastName = input.LastName;

			if (profile.IdentificationNumber != input.Id)
				profile.IdentificationNumber = input.Id;

			if (profile.HomeNumber != input.HomePhone)
				profile.HomeNumber = input.HomePhone;

			if (input.MobileCarrier != 0 && !String.IsNullOrWhiteSpace(input.MobilePhone))
			{
				profile.MobileCarrier = input.MobileCarrier;
				profile.MobileNumber = input.MobilePhone;
			}

			profile.SendSms = input.SendCallSms;
			profile.SendPush = input.SendCallPush;
			profile.SendEmail = input.SendCallEmail;

			profile.SendMessageSms = input.SendMessageSms;
			profile.SendMessagePush = input.SendMessagePush;
			profile.SendMessageEmail = input.SendMessageEmail;

			profile.SendNotificationSms = input.SendNotificationSms;
			profile.SendNotificationPush = input.SendNotificationPush;
			profile.SendNotificationEmail = input.SendNotificationEmail;

			await _userProfileService.SaveProfileAsync(DepartmentId, profile, cancellationToken);
			_departmentsService.InvalidateDepartmentUsersInCache(department.DepartmentId);

			auditEvent.After = profile.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return Ok(result);
		}
	}
}
