using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using System.Linq;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.Web.Services.Models.v4.Voice;
using System.Collections.Generic;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class VoiceController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IAuthorizationService _authorizationService;
		private readonly IVoiceService _voiceService;
		private readonly IDepartmentsService _departmentsService;

		public VoiceController(
			IAuthorizationService authorizationService,
			IVoiceService voiceService,
			IDepartmentsService departmentsService)
		{
			_authorizationService = authorizationService;
			_voiceService = voiceService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the available responding options (Calls/Stations) for the department
		/// </summary>
		/// <returns>Array of RecipientResult objects for each responding option in the department</returns>
		[HttpGet("GetDepartmentVoiceSettings")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DepartmentVoiceResult>> GetDepartmentVoiceSettings()
		{
			var result = new DepartmentVoiceResult();
			result.Data = new DepartmentVoiceResultData();

			result.Data.VoipServerWebsocketSslAddress = Config.VoipConfig.VoipServerWebsocketSslAddress;
			result.Data.VoiceEnabled = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);
			result.Data.Realm = Config.VoipConfig.VoipDomain;
			result.Data.CallerIdName = await UserHelper.GetFullNameForUser(UserId);

			if (result.Data.VoiceEnabled)
			{
				result.Data.Channels = new List<DepartmentVoiceChannelResultData>();

				var voice = await _voiceService.GetVoiceSettingsForDepartmentAsync(DepartmentId);

				if (voice != null)
				{
					if (voice.Channels != null && voice.Channels.Any())
					{
						foreach (var chan in voice.Channels)
						{
							var channel = new DepartmentVoiceChannelResultData();
							channel.Name = chan.Name;
							channel.IsDefault = chan.IsDefault;
							channel.ConferenceNumber = chan.ConferenceNumber;

							result.Data.Channels.Add(channel);
						}
					}

					result.Data.UserInfo = new DepartmentVoiceUserInfoResultData();
					result.Data.UserInfo.Username = UserId.Replace("-", "");

					var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
					result.Data.UserInfo.Pin = _departmentsService.ConvertDepartmentCodeToDigitPin(department.Code);
					result.Data.UserInfo.Password = Hashing.ComputeMD5Hash($"{UserId}{Config.SymmetricEncryptionConfig.InitVector}");
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
