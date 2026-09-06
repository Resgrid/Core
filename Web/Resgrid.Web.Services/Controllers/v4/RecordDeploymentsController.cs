using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Create Deployment from External Order over v4 (RMS plan section 4.1 external-order fill contract, RMS-1C,
	/// Preview). Manual entry and artifact snapshots only; no ordering-system connector and no write-back. Creating
	/// or changing a deployment needs Record_Create; reading needs Record_View plus visibility of its Record.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordDeploymentsController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordDeploymentsService _deployments;
		private readonly IRmsExternalOrdersRepository _orders;
		private readonly IRecordsCutoverService _cutoverService;

		public RecordDeploymentsController(IRecordDeploymentsService deployments, IRmsExternalOrdersRepository orders, IRecordsCutoverService cutoverService)
		{
			_deployments = deployments;
			_orders = orders;
			_cutoverService = cutoverService;
		}

		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentsResult>> List(bool includeClosed = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			var orders = await _deployments.ListAsync(DepartmentId, UserId, includeClosed);
			var result = new RecordDeploymentsResult { Status = ResponseHelper.Success };
			foreach (var order in orders)
			{
				var aggregate = await _deployments.GetAsync(DepartmentId, UserId, order.RmsExternalOrderId);
				if (aggregate != null) result.Data.Add(RecordsRms1bApiMapper.ToDeployment(aggregate));
			}
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var aggregate = await _deployments.GetAsync(DepartmentId, UserId, id);
				if (aggregate == null) return NotFound();
				return Ok(Wrap(aggregate));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("GetForRecord")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentResult>> GetForRecord(string recordId)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var aggregate = await _deployments.GetForRecordAsync(DepartmentId, UserId, recordId);
				if (aggregate == null) return NotFound();
				return Ok(Wrap(aggregate));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Create")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentResult>> Create([FromBody] CreateRecordDeploymentInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try
			{
				var origin = RecordsApiHelper.ResolveOrigin(null);
				var created = RecordsRms1bApiMapper.ToCreateInput(input, origin);
				created.IdempotencyKey = RecordsApiHelper.ResolveIdempotencyKey(input.IdempotencyKey, Request);
				var aggregate = await _deployments.CreateFromExternalOrderAsync(DepartmentId, UserId, created, cancellationToken);
				return StatusCode(StatusCodes.Status201Created, Wrap(aggregate, ResponseHelper.Created));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("AddFill")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentResult>> AddFill(string id, [FromBody] RecordDeploymentFillInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try
			{
				await _deployments.AddFillAsync(DepartmentId, UserId, id, input, cancellationToken);
				return Ok(Wrap(await _deployments.GetAsync(DepartmentId, UserId, id)));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Moves one fill through accept/decline, mobilize, check-in, assign, release, demobilize and return.</summary>
		[HttpPost("TransitionFill")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentResult>> TransitionFill(string fillId, [FromBody] RecordDeploymentFillTransitionInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try
			{
				var fill = await _deployments.TransitionFillAsync(DepartmentId, UserId, fillId, input, cancellationToken);
				return Ok(Wrap(await _deployments.GetAsync(DepartmentId, UserId, fill.RmsExternalOrderId)));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Records a later snapshot of the same external order; the previous artifact stays on record as a superseded reference.</summary>
		[HttpPost("Snapshot")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentResult>> Snapshot(string id, [FromBody] RecordDeploymentSnapshotInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ArtifactBase64)) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try
			{
				byte[] artifact;
				try { artifact = Convert.FromBase64String(input.ArtifactBase64); } catch (FormatException) { return Problem(statusCode: StatusCodes.Status400BadRequest, title: "ArtifactBase64 is not valid base64.", type: "record_deployment_validation"); }
				await _deployments.RecordSourceSnapshotAsync(DepartmentId, UserId, id, input.SourceVersion, artifact, input.ArtifactFileName, input.ArtifactContentType, cancellationToken);
				return Ok(Wrap(await _deployments.GetAsync(DepartmentId, UserId, id)));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Closeout")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentResult>> Closeout(string id, [FromBody] CloseoutRecordDeploymentInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var rowVersion = RecordsApiContract.ParseETag(Request.Headers[RecordsApiContract.IfMatchHeader]) ?? input.RowVersion;
			try
			{
				await _deployments.CloseoutAsync(DepartmentId, UserId, id, rowVersion, input.Notes, cancellationToken);
				return Ok(Wrap(await _deployments.GetAsync(DepartmentId, UserId, id)));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Artifact")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Artifact(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var aggregate = await _deployments.GetAsync(DepartmentId, UserId, id, true);
				if (aggregate?.Order?.ArtifactData == null) return NotFound();
				return File(aggregate.Order.ArtifactData, string.IsNullOrWhiteSpace(aggregate.Order.ArtifactContentType) ? "application/octet-stream" : aggregate.Order.ArtifactContentType, aggregate.Order.ArtifactFileName ?? "order-artifact");
			}
			catch (Exception ex) { return Fail(ex); }
		}

		private RecordDeploymentResult Wrap(RecordDeploymentAggregate aggregate, string status = ResponseHelper.Success)
		{
			var result = new RecordDeploymentResult { Data = RecordsRms1bApiMapper.ToDeployment(aggregate), Status = status, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			Response.Headers[RecordsApiContract.ETagHeader] = result.Data.ETag;
			return result;
		}

		private ActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordConcurrencyException conflict: return Problem(statusCode: StatusCodes.Status409Conflict, title: conflict.Message, type: "record_deployment_conflict");
				case RecordIdempotencyException idempotency: return Problem(statusCode: StatusCodes.Status409Conflict, title: idempotency.Message, type: "record_idempotency_conflict");
				case ArgumentException argument: return Problem(statusCode: StatusCodes.Status400BadRequest, title: argument.Message, type: "record_deployment_validation");
				case InvalidOperationException invalid: return Problem(statusCode: StatusCodes.Status409Conflict, title: invalid.Message, type: "record_deployment_state");
				default: throw ex;
			}
		}

		private async Task<bool> FlagOnAsync() => (await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled;

		private async Task<ActionResult> UsableAsync()
		{
			var state = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!state.FlagEnabled) return NotFound();
			return state.RecordsUsable ? null : Problem(statusCode: StatusCodes.Status409Conflict, title: "Records is not activated for this department.", type: "records_not_activated");
		}
	}
}
