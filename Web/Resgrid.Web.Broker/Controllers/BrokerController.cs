using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Web.Broker.Models;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Web.Broker.Controllers
{
	/// <summary>
	/// Field-crypto endpoints for the application tier (behind WorkloadKeyMiddleware). The response
	/// body is always a ProtectedDataBrokerResult; the HTTP status mirrors its error code so plain
	/// HTTP clients and infrastructure see failures too. No endpoint here exposes key material, a
	/// general unwrap, or any bulk/no-grant path.
	/// </summary>
	[ApiController]
	[Route("api/v1/broker")]
	public class BrokerController : ControllerBase
	{
		private readonly BrokerOperationService _operationService;

		public BrokerController(BrokerOperationService operationService)
		{
			_operationService = operationService;
		}

		[HttpPost("decrypt")]
		public async Task<ActionResult<ProtectedDataBrokerResult>> Decrypt([FromBody] BrokerFieldOperationRequest request,
			CancellationToken cancellationToken)
		{
			var result = await _operationService.DecryptAsync(request, cancellationToken);
			return StatusCode(MapStatusCode(result), result);
		}

		[HttpPost("encrypt")]
		public async Task<ActionResult<ProtectedDataBrokerResult>> Encrypt([FromBody] BrokerFieldOperationRequest request,
			CancellationToken cancellationToken)
		{
			var result = await _operationService.EncryptAsync(request, cancellationToken);
			return StatusCode(MapStatusCode(result), result);
		}

		/// <summary>
		/// Purpose-bound workload decrypt (RMS plan section 5.9.4): no grant, the workload key plus an allow-listed
		/// purpose for an actively protected department. Refusals answer 403 workload_purpose_denied.
		/// </summary>
		[HttpPost("workload/decrypt")]
		public async Task<ActionResult<ProtectedDataBrokerResult>> DecryptForWorkload([FromQuery] string purpose,
			[FromBody] BrokerFieldOperationRequest request, CancellationToken cancellationToken)
		{
			var result = await _operationService.DecryptForWorkloadAsync(request, purpose, cancellationToken);
			return StatusCode(MapStatusCode(result), result);
		}

		private static int MapStatusCode(ProtectedDataBrokerResult result)
		{
			if (result.Success)
				return StatusCodes.Status200OK;

			switch (result.ErrorCode)
			{
				case "invalid_request":
					return StatusCodes.Status400BadRequest;
				case "too_many_items":
					return StatusCodes.Status413PayloadTooLarge;
				case "replayed_request":
					return StatusCodes.Status409Conflict;
				case "grant_expired":
				case "grant_invalid":
					return StatusCodes.Status401Unauthorized;
				case "grant_revoked":
				case "workload_purpose_denied":
					return StatusCodes.Status403Forbidden;
				case "no_active_key":
					return StatusCodes.Status409Conflict;
				default:
					// grant_validation_unavailable, kms_unavailable and anything unmapped fail closed
					// as a service fault.
					return StatusCodes.Status503ServiceUnavailable;
			}
		}
	}
}
