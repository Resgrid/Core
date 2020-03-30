using System.Net;
using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Controllers.Version3.Models.Profile;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against a logged in users (determined via the token) profile
	/// </summary>
	public class ProfileController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ILimitsService _limitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IAddressService _addressService;

		public ProfileController(IUsersService usersService, IDepartmentsService departmentsService, ILimitsService limitsService,
			IUserProfileService userProfileService, IAuthorizationService authorizationService, IAddressService addressService)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_limitsService = limitsService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
			_addressService = addressService;
		}

		/// <summary>
		/// Gets the mobile carriers in the Resgrid system. If you need a mobile carrier added contact team@resgrid.com.
		/// </summary>
		/// <returns></returns>
		public List<MobileCarriersResult> GetMobileCarriers()
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
			
			return carriers;
		}

		/// <summary>
		/// Gets the time zones in the Resgrid system. If you need a time zone added or corrected contact team@resgrid.com.
		/// </summary>
		/// <returns></returns>
		public List<TimeZoneResult> GetTimeZones()
		{
			return TimeZones.Zones.Select(zone => new TimeZoneResult()
			{
				Id = zone.Key, Nme = zone.Value
			}).ToList();
		}

		/// <summary>
		/// Gets the Resgrid user profile for the user
		/// </summary>
		/// <returns>ProfileResult object with the users profile data</returns>
		public ProfileResult GetProfile()
		{
			var profile = _userProfileService.GetProfileByUserId(UserId.ToUpper(), true);

			if (profile == null)
				throw HttpStatusCode.NotFound.AsException();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var dm = _departmentsService.GetDepartmentMember(UserId.ToUpper(), DepartmentId);
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
				Val = _limitsService.CanDepartmentUseVoice(DepartmentId),
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
				var address = _addressService.GetAddressById(profile.HomeAddressId.Value);

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
				var address = _addressService.GetAddressById(profile.MailingAddressId.Value);

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

			return result;
		}

		/// <summary>
		/// Toggles a users profile to enable/disable custom push sounds when the app is backgrounded.
		/// </summary>
		/// <returns>An HTTP Result</returns>
		[HttpGet]
		public HttpResponseMessage ToggleCustomPushSounds(bool enableCustomPushSounds)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);

			var profile = _userProfileService.GetUserProfileForEditing(UserId.ToUpper());

			if (profile == null)
				throw HttpStatusCode.NotFound.AsException();

			profile.CustomPushSounds = enableCustomPushSounds;
			_userProfileService.SaveProfile(DepartmentId, profile);
			
			return result;
		}

		/// <summary>
		/// Updates a users profile
		/// </summary>
		/// <returns>An HTTP Result</returns>
		[HttpPut]
		public HttpResponseMessage UpdateProfile(UpdateProfileInput input)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);

			var profile = _userProfileService.GetUserProfileForEditing(UserId.ToUpper());

			if (profile == null)
				throw HttpStatusCode.NotFound.AsException();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var dm = _departmentsService.GetDepartmentMember(UserId.ToUpper(), DepartmentId);
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

			_userProfileService.SaveProfile(DepartmentId, profile);
			_departmentsService.InvalidateDepartmentUsersInCache(department.DepartmentId);

			return result;
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
