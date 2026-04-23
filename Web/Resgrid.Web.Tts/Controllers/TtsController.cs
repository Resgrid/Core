using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Models;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Web.Tts.Controllers
{
	[ApiController]
	[Route("tts")]
	[EnableRateLimiting("tts")]
	public sealed class TtsController : ControllerBase
	{
		private readonly ITtsService _ttsService;
		private readonly ICacheService _cacheService;
		private readonly ITtsPlaybackUrlService _ttsPlaybackUrlService;
		private readonly TtsOptions _options;

		public TtsController(
			ITtsService ttsService,
			ICacheService cacheService,
			ITtsPlaybackUrlService ttsPlaybackUrlService,
			IOptions<TtsOptions> options)
		{
			_ttsService = ttsService;
			_cacheService = cacheService;
			_ttsPlaybackUrlService = ttsPlaybackUrlService;
			_options = options.Value;
		}

		[HttpPost]
		[ProducesResponseType(typeof(TtsResponse), StatusCodes.Status200OK)]
		[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<TtsResponse>> GenerateAsync([FromBody] TtsRequest request, CancellationToken cancellationToken)
		{
			try
			{
				var response = await _ttsService.GenerateAsync(request, cancellationToken);
				ApplyPlaybackUrl(response);
				return Ok(response);
			}
			catch (ArgumentException ex)
			{
				return BadRequest(CreateProblemDetails(ex.Message));
			}
		}

		[HttpPost("batch")]
		[ProducesResponseType(typeof(IReadOnlyCollection<TtsResponse>), StatusCodes.Status200OK)]
		[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<IReadOnlyCollection<TtsResponse>>> GenerateBatchAsync([FromBody] List<TtsRequest> requests, CancellationToken cancellationToken)
		{
			try
			{
				var responses = await _ttsService.GenerateBatchAsync(requests, cancellationToken);
				foreach (var response in responses)
				{
					ApplyPlaybackUrl(response);
				}

				return Ok(responses);
			}
			catch (ArgumentException ex)
			{
				return BadRequest(CreateProblemDetails(ex.Message));
			}
		}

		[HttpGet("audio/{hash:length(64)}.wav")]
		[DisableRateLimiting]
		[Produces("audio/wav")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> GetAudioAsync(string hash, CancellationToken cancellationToken)
		{
			var audio = await _cacheService.TryGetAudioAsync(hash, cancellationToken);

			if (audio is null)
			{
				return NotFound();
			}

			Response.Headers.CacheControl = $"public,max-age={_options.PlaybackCacheControlSeconds},immutable";

			return File(
				audio.AudioBytes,
				audio.ContentType,
				lastModified: audio.LastModified,
				entityTag: new EntityTagHeaderValue(audio.EntityTag),
				enableRangeProcessing: true);
		}

		private ProblemDetails CreateProblemDetails(string detail)
		{
			return new ProblemDetails
			{
				Status = StatusCodes.Status400BadRequest,
				Title = "Invalid TTS request",
				Detail = detail
			};
		}

		private void ApplyPlaybackUrl(TtsResponse response)
		{
			response.Url = _ttsPlaybackUrlService.CreatePlaybackUrl(Request, response.Hash).ToString();
		}
	}
}
