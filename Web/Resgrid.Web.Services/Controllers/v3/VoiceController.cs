using System.Threading.Tasks;
using Resgrid.Model.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.Services.Models.v3.Voice;
using Resgrid.Framework;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations that can be performed against resgrid voice (voip) services
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class VoiceController : V3AuthenticatedApiControllerbase
	{
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

		/// <summary>
		/// Returns all the available responding options (Calls/Stations) for the department
		/// </summary>
		/// <returns>Array of RecipientResult objects for each responding option in the department</returns>
		[HttpGet("GetDepartmentVoiceSettings")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DepartmentVoiceResult>> GetDepartmentVoiceSettings()
		{
			var result = new DepartmentVoiceResult();
			result.VoipServerWebsocketSslAddress = Config.VoipConfig.VoipServerWebsocketSslAddress;
			result.VoiceEnabled = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);
			result.Realm = Config.VoipConfig.VoipDomain;
			result.CallerIdName = await UserHelper.GetFullNameForUser(UserId);

			if (result.VoiceEnabled)
			{
				result.Channels = new List<DepartmentVoiceChannelResult>();

				var voice = await _voiceService.GetVoiceSettingsForDepartmentAsync(DepartmentId);

				if (voice != null)
				{
					if (voice.Channels != null && voice.Channels.Any())
					{
						foreach (var chan in voice.Channels)
						{
							var channel = new DepartmentVoiceChannelResult();
							channel.Name = chan.Name;
							channel.IsDefault = chan.IsDefault;
							channel.ConferenceNumber = chan.ConferenceNumber;

							result.Channels.Add(channel);
						}
					}

					result.UserInfo = new DepartmentVoiceUserInfoResult();
					result.UserInfo.Username = UserId.Replace("-", "");

					var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
					result.UserInfo.Pin = _departmentsService.ConvertDepartmentCodeToDigitPin(department.Code);
					result.UserInfo.Password = Hashing.ComputeMD5Hash($"{UserId}{Config.SymmetricEncryptionConfig.InitVector}");
				}
			}

			return Ok(result);
		}
	}
}
