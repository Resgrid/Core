using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Models;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Web.Tts.Controllers
{
	[ApiController]
	[Route("tts/admin")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public sealed class TtsAdminController : ControllerBase
	{
		private const string AdminKeyHeaderName = "X-Resgrid-Admin-Key";

		private readonly ITtsService _ttsService;
		private readonly ITtsPlaybackUrlService _ttsPlaybackUrlService;
		private readonly TtsOptions _options;

		public TtsAdminController(ITtsService ttsService, ITtsPlaybackUrlService ttsPlaybackUrlService, IOptions<TtsOptions> options)
		{
			_ttsService = ttsService;
			_ttsPlaybackUrlService = ttsPlaybackUrlService;
			_options = options.Value;
		}

		[HttpPost("static-prompts")]
		[ProducesResponseType(typeof(StaticPromptRegenerationResponse), StatusCodes.Status200OK)]
		[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
		[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<StaticPromptRegenerationResponse>> RegenerateStaticPromptsAsync(
			[FromHeader(Name = AdminKeyHeaderName)] string adminKey,
			[FromBody] StaticPromptRegenerationRequest request,
			CancellationToken cancellationToken)
		{
			if (!IsAuthorized(adminKey))
			{
				return Unauthorized(new ProblemDetails
				{
					Status = StatusCodes.Status401Unauthorized,
					Title = "Unauthorized",
					Detail = "A valid admin key is required to regenerate static prompts."
				});
			}

			var prompts = request?.Prompts?.Where(prompt => !string.IsNullOrWhiteSpace(prompt?.Text)).ToList()
				?? new List<TtsRequest>();

			if (prompts.Count == 0)
			{
				prompts = _options.PreGeneratedPrompts
					.Where(prompt => !string.IsNullOrWhiteSpace(prompt))
					.Select(prompt => new TtsRequest
					{
						Text = prompt.Trim(),
						Voice = _options.DefaultVoice,
						Speed = _options.DefaultSpeed
					})
					.ToList();
			}

			if (prompts.Count == 0)
			{
				return BadRequest(new ProblemDetails
				{
					Status = StatusCodes.Status400BadRequest,
					Title = "Invalid prompt refresh request",
					Detail = "At least one prompt is required."
				});
			}

			try
			{
				var results = await _ttsService.GenerateBatchAsync(prompts, cancellationToken);

				foreach (var result in results)
				{
					result.Url = _ttsPlaybackUrlService.CreatePlaybackUrl(Request, result.Hash).ToString();
				}

				return Ok(new StaticPromptRegenerationResponse
				{
					PromptCount = results.Count,
					Prompts = results
				});
			}
			catch (ArgumentException ex)
			{
				return BadRequest(new ProblemDetails
				{
					Status = StatusCodes.Status400BadRequest,
					Title = "Invalid prompt refresh request",
					Detail = ex.Message
				});
			}
		}

		private bool IsAuthorized(string suppliedKey)
		{
			if (string.IsNullOrWhiteSpace(_options.StaticPromptAdminKey) || string.IsNullOrWhiteSpace(suppliedKey))
			{
				return false;
			}

			var configuredBytes = Encoding.UTF8.GetBytes(_options.StaticPromptAdminKey);
			var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);

			return configuredBytes.Length == suppliedBytes.Length
				   && CryptographicOperations.FixedTimeEquals(configuredBytes, suppliedBytes);
		}
	}
}
