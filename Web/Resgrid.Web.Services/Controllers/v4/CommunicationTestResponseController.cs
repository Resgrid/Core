using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CommunicationTestMessages = Resgrid.Localization.Areas.User.CommunicationTest.CommunicationTestMessageCatalog;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Twilio;
using System;
using System.Threading;
using System.Threading.Tasks;
using Twilio.AspNet.Core;
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
		/// Voice call entry point - Twilio fetches this when the communication test call is answered.
		/// Plays the test prompt and gathers a keypress, which posts back to VoiceWebhook.
		/// </summary>
		[HttpGet("VoiceCall")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ContentResult> VoiceCall(string token)
		{
			var response = new VoiceResponse();
			var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

			if (string.IsNullOrWhiteSpace(token))
			{
				await _twilioVoiceResponseService.AppendPromptAsync(response, CommunicationTestMessages.BuildVoiceNoResponse(null), cancellationToken, null);
				response.Hangup();

				return BuildVoiceResult(response);
			}

			var voice = await ResolveVoiceContextAsync(token);

			// GET on the gather action so Twilio puts Digits in the query string, which binds to the
			// VoiceWebhook parameter. A form POST would not bind to a simple string parameter.
			var actionUrl = new Uri($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CommunicationTestResponse/VoiceWebhook?token={Uri.EscapeDataString(token)}");

			// Two passes so a recipient who misses the first prompt still gets a chance to confirm.
			for (int repeat = 0; repeat < 2; repeat++)
			{
				var gather = new global::Twilio.TwiML.Voice.Gather(numDigits: 1, action: actionUrl, method: "GET")
				{
					BargeIn = true
				};

				await _twilioVoiceResponseService.AppendPromptsAsync(gather,
					CommunicationTestMessages.GetVoicePrompts(voice.Culture), cancellationToken, voice.TtsVoice);

				response.Append(gather);
			}

			await _twilioVoiceResponseService.AppendPromptAsync(response,
				CommunicationTestMessages.BuildVoiceNoResponse(voice.Culture), cancellationToken, voice.TtsVoice);
			response.Hangup();

			return BuildVoiceResult(response);
		}

		/// <summary>
		/// Voice webhook endpoint - receives DTMF keypress callbacks. Signature validated: this is the
		/// one endpoint on the voice path that writes, so possession of a response token alone must not
		/// be enough to mark a member as reached on a readiness report.
		/// </summary>
		[HttpPost("VoiceWebhook")]
		[HttpGet("VoiceWebhook")]
		[AllowAnonymous]
		[ValidateRequest]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ContentResult> VoiceWebhook(string token, string Digits)
		{
			if (!string.IsNullOrWhiteSpace(token) && Digits == "1")
			{
				await _communicationTestService.RecordVoiceResponseAsync(token);
			}

			var response = new VoiceResponse();
			var voice = string.IsNullOrWhiteSpace(token)
				? default
				: await ResolveVoiceContextAsync(token);
			await _twilioVoiceResponseService.AppendPromptAsync(response,
				CommunicationTestMessages.BuildVoiceRecorded(voice.Culture),
				HttpContext?.RequestAborted ?? CancellationToken.None, voice.TtsVoice);
			response.Hangup();

			return BuildVoiceResult(response);
		}

		private static ContentResult BuildVoiceResult(VoiceResponse response)
		{
			return new ContentResult
			{
				Content = response.ToString(),
				ContentType = "application/xml",
				StatusCode = 200
			};
		}

		/// <summary>
		/// What language the call speaks and which TTS voice reads it. The person being called picks
		/// the language, not the department: a test proves reachability, and a member who set their
		/// profile to Spanish is not reliably reached by an English recording. The department's TTS
		/// language remains the fallback for members who never chose one.
		/// </summary>
		private async Task<(string Culture, string TtsVoice)> ResolveVoiceContextAsync(string token)
		{
			var departmentVoice = await GetDepartmentTtsLanguageAsync(token);

			var recipientLanguage = await _communicationTestService.GetRecipientLanguageByResponseTokenAsync(token);
			if (string.IsNullOrWhiteSpace(recipientLanguage))
				return (null, departmentVoice);

			// Only speak in the recipient's language when a voice actually exists for it; otherwise the
			// text would be right but read by a voice that mispronounces it.
			return EspeakVoiceCatalog.TryNormalizeIdentifier(recipientLanguage, out var recipientVoice)
				? (recipientLanguage, recipientVoice)
				: (recipientLanguage, departmentVoice);
		}

		private async Task<string> GetDepartmentTtsLanguageAsync(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
				return null;

			var departmentId = await _communicationTestService.GetDepartmentIdByResponseTokenAsync(token);

			if (!departmentId.HasValue)
				return null;

			return await _departmentSettingsService.GetTtsLanguageForDepartmentAsync(departmentId.Value);
		}
	}
}
