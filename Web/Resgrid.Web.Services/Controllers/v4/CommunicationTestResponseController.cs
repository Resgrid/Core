using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;

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

		public CommunicationTestResponseController(ICommunicationTestService communicationTestService)
		{
			_communicationTestService = communicationTestService;
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

			return new ContentResult
			{
				Content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response><Say>Thank you. Your response has been recorded.</Say><Hangup/></Response>",
				ContentType = "application/xml",
				StatusCode = 200
			};
		}
	}
}
