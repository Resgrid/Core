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
using Microsoft.AspNetCore.Authorization;
using Resgrid.Providers.Voip;
using System.Web;

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
		private readonly IUserProfileService _userProfileService;

		public VoiceController(
			IAuthorizationService authorizationService,
			IVoiceService voiceService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService)
		{
			_authorizationService = authorizationService;
			_voiceService = voiceService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
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

			var liveKitProvder = new LiveKitProvider();

			result.Data.VoipServerWebsocketSslAddress = Config.VoipConfig.VoipServerWebsocketSslAddress;
			result.Data.VoiceEnabled = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);
			result.Data.Realm = Config.VoipConfig.VoipDomain;
			result.Data.CallerIdName = await UserHelper.GetFullNameForUser(UserId);
			result.Data.Type = (int)Config.SystemBehaviorConfig.VoipProviderType;

			var userInfo = await _userProfileService.GetProfileByUserIdAsync(UserId);

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
							channel.Id = chan.DepartmentVoiceChannelId;
							channel.Name = chan.Name;
							channel.IsDefault = chan.IsDefault;
							channel.ConferenceNumber = chan.ConferenceNumber;

							if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.LiveKit)
								channel.Token = liveKitProvder.GetTokenForRoom(userInfo.FullName.AsFirstNameLastName.Replace(" ", ""), chan.DepartmentVoiceChannelId);


							result.Data.Channels.Add(channel);
						}
					}

					result.Data.UserInfo = new DepartmentVoiceUserInfoResultData();
					result.Data.UserInfo.Username = UserId.Replace("-", "");
					result.Data.CanConnectApiToken = StringHelpers.Base64Encode(SymmetricEncryption.Encrypt(DepartmentId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase));

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

		/// <summary>
		/// Connects to an voip session, limited to only OpenVidu.
		/// </summary>
		/// <returns>Voice connection result containing the data needed to connect to a voip session</returns>
		[HttpGet("ConnectToSession")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<VoiceSessionConnectionResult>> ConnectToSession(string sessionId)
		{
			var result = new VoiceSessionConnectionResult();
			result.PageSize = 0;
			result.Status = ResponseHelper.NotFound;

			if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.OpenVidu)
			{
				var token = await _voiceService.GetOpenViduSessionToken(sessionId);

				if (token != null)
				{
					result.Data = new VoiceSessionConnectionResultData();
					result.Data.Token = token;
					result.PageSize = 1;
					result.Status = ResponseHelper.Success;
				}
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Determines if a user can connect to an voice session, limited to only LiveKit.
		/// </summary>
		/// <returns>Voice connection result containing the data needed to determine if a user can connect to a voice session</returns>
		[HttpGet("CanConnectToVoiceSession")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CanConnectToVoiceSessionResult>> CanConnectToVoiceSession(string token)
		{
			var result = new CanConnectToVoiceSessionResult();
			result.PageSize = 0;
			result.Status = ResponseHelper.NotFound;
			result.Data = new CanConnectToVoiceSessionResultData();

			if (string.IsNullOrWhiteSpace(token))
				return result;

			string depId = null;
			try
			{
				depId = SymmetricEncryption.Decrypt(StringHelpers.Base64Decode(token), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			}
			catch { }

			if (string.IsNullOrWhiteSpace(depId))
				return result;

			// Disabling for now, as there is some conversion between 
			//if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.LiveKit)
			//{
				var info = await _voiceService.GetCurrentUtilizationForLiveKit(int.Parse(depId));

				if (info != null)
				{
					result.Data.CurrentSessions = info.CurrentlyActive;
					result.Data.MaxSessions = info.SeatLimit;
					result.Data.CanConnect = (info.CurrentlyActive < info.SeatLimit);

					result.PageSize = 1;
					result.Status = ResponseHelper.Success;
				}
			//}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Returns all the department audio streams
		/// </summary>
		/// <returns>Array of RecipientResult objects for each responding option in the department</returns>
		[HttpGet("GetDepartmentAudioStreams")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DepartmentAudioResult>> GetDepartmentAudioStreams()
		{
			var result = new DepartmentAudioResult();
			result.Data = new List<DepartmentAudioResultStreamData>();

			var streams = await _voiceService.GetDepartmentAudiosByDepartmentIdAsync(DepartmentId);
			foreach (var stream in streams)
			{
				var data = new DepartmentAudioResultStreamData();
				data.Id = stream.DepartmentAudioId;
				data.Name = stream.Name;
				data.Url = stream.Data;

				if (string.IsNullOrWhiteSpace(stream.Type))
				{
					//if (stream.Data.Contains("broadcastify"))
					//	data.Type = "hls";
					//else
						data.Type = "audio/mp4";
				}
				else
				{
					data.Type = stream.Type;
				}

				result.Data.Add(data);
			}

			result.PageSize = streams.Count();
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
