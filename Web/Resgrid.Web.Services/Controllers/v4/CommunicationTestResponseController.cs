using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Twilio;
using System.Threading;
using System.Threading.Tasks;
using Twilio.TwiML;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Public endpoints for communication test responses (email confirm, voice webhook)
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CommunicationTestResponseController : V4AuthenticatedApiControllerbase
	{
		private readonly ICommunicationTestService _communicationTestService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ITwilioVoiceResponseService _twilioVoiceResponseService;

		public CommunicationTestResponseController(
			ICommunicationTestService communicationTestService,
			IDepartmentSettingsService departmentSettingsService,
			ITwilioVoiceResponseService twilioVoiceResponseService)
		{
			_communicationTestService = communicationTestService;
			_departmentSettingsService = departmentSettingsService;
			_twilioVoiceResponseService = twilioVoiceResponseService;
		}

		/// <summary>
		/// Email confirmation endpoint - user clicks link with token to confirm receipt
		/// </summary>
		[HttpGet("EmailConfirm")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ContentResult> EmailConfirm(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
			{
				return new ContentResult
				{
					Content = "<html><body><h2>Invalid request.</h2></body></html>",
					ContentType = "text/html",
					StatusCode = 400
				};
			}

			var success = await _communicationTestService.RecordEmailResponseAsync(token);

			var html = success
				? "<html><body><h2>Thank you!</h2><p>Your communication test response has been recorded.</p></body></html>"
				: "<html><body><h2>Response not found.</h2><p>This link may have already been used or has expired.</p></body></html>";

			return new ContentResult
			{
				Content = html,
				ContentType = "text/html",
				StatusCode = 200
			};
		}

		/// <summary>
		/// Voice webhook endpoint - receives DTMF keypress callbacks
		/// </summary>
		[HttpPost("VoiceWebhook")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ContentResult> VoiceWebhook(string token, string Digits)
		{
			if (!string.IsNullOrWhiteSpace(token) && Digits == "1")
			{
				await _communicationTestService.RecordVoiceResponseAsync(token);
			}

			var response = new VoiceResponse();
			var ttsLanguage = await GetDepartmentTtsLanguageAsync(token);
			await _twilioVoiceResponseService.AppendPromptAsync(response, TwilioVoicePromptCatalog.CommunicationTestRecorded, HttpContext?.RequestAborted ?? CancellationToken.None, ttsLanguage);
			response.Hangup();

			return new ContentResult
			{
				Content = response.ToString(),
				ContentType = "application/xml",
				StatusCode = 200
			};
		}

		private async Task<string> GetDepartmentTtsLanguageAsync(string token)
		{
			var departmentId = await _communicationTestService.GetDepartmentIdByResponseTokenAsync(token);

			if (!departmentId.HasValue)
				return null;

			return await _departmentSettingsService.GetTtsLanguageForDepartmentAsync(departmentId.Value);
		}
	}
}
