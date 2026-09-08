using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// External ordering-system connectors for mutual-aid deployments (RMS plan section 4.1, RMS-1C). Department
	/// administration only, enforced here and again in the service. A connector reads a documented feed under an
	/// encrypted credential, an hourly request limit, and an acknowledgement of the source's terms; write authority
	/// is refused. Import provisions unseen orders and records changed ones as new versioned snapshots; it never
	/// transitions a local fill — disagreements are listed by Reconciliation for a person.
	/// The Inbound action is the one anonymous endpoint: a source pushes a feed document with the connector's
	/// one-time token in <c>X-Resgrid-Connector-Token</c>; the server holds only a hash of that token.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordDeploymentConnectorsController : V4AuthenticatedApiControllerbase
	{
		public const string TokenHeader = "X-Resgrid-Connector-Token";

		private readonly IRecordDeploymentConnectorsService _connectors;
		private readonly IRecordsCutoverService _cutoverService;

		public RecordDeploymentConnectorsController(IRecordDeploymentConnectorsService connectors, IRecordsCutoverService cutoverService)
		{
			_connectors = connectors;
			_cutoverService = cutoverService;
		}

		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentConnectorsResult>> List()
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var result = new RecordDeploymentConnectorsResult { Status = ResponseHelper.Success, Data = (await _connectors.ListAsync(DepartmentId, UserId)).Select(ToData).ToList() };
				result.PageSize = result.Data.Count;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentConnectorResult>> Get(string id)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var connector = await _connectors.GetAsync(DepartmentId, UserId, id);
				if (connector == null) return NotFound();
				return Ok(Wrap(connector));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Create")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentConnectorCreatedResult>> Create([FromBody] RecordDeploymentConnectorInputData input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var created = await _connectors.CreateAsync(DepartmentId, UserId, ToInput(input), cancellationToken);
				var result = new RecordDeploymentConnectorCreatedResult { Status = ResponseHelper.Created, PageSize = 1, Data = new RecordDeploymentConnectorCreatedData { Connector = ToData(created.Connector), InboundToken = created.InboundToken } };
				ResponseHelper.PopulateV4ResponseData(result);
				return StatusCode(StatusCodes.Status201Created, result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Update")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentConnectorResult>> Update(string id, [FromBody] RecordDeploymentConnectorInputData input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var gate = await GateAsync();
			if (gate != null) return gate;
			try { return Ok(Wrap(await _connectors.UpdateAsync(DepartmentId, UserId, id, input.RowVersion, ToInput(input), cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SetEnabled")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentConnectorResult>> SetEnabled(string id, bool enabled, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try { return Ok(Wrap(await _connectors.SetEnabledAsync(DepartmentId, UserId, id, enabled, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("AcknowledgeTerms")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentConnectorResult>> AcknowledgeTerms(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try { return Ok(Wrap(await _connectors.AcknowledgeTermsAsync(DepartmentId, UserId, id, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("RotateToken")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentConnectorTokenResult>> RotateToken(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var token = await _connectors.RotateInboundTokenAsync(DepartmentId, UserId, id, cancellationToken);
				var connector = await _connectors.GetAsync(DepartmentId, UserId, id);
				var result = new RecordDeploymentConnectorTokenResult { Status = ResponseHelper.Success, PageSize = 1, Data = new RecordDeploymentConnectorCreatedData { Connector = ToData(connector), InboundToken = token } };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<StandardApiResponseV4Base>> Delete(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				await _connectors.DeleteAsync(DepartmentId, UserId, id, cancellationToken);
				var result = new StandardApiResponseV4Base { Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Run")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<ActionResult<RecordDeploymentConnectorRunApiResult>> Run(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var run = await _connectors.RunAsync(DepartmentId, UserId, id, cancellationToken);
				var result = new RecordDeploymentConnectorRunApiResult { Status = ResponseHelper.Success, PageSize = 1, Data = ToData(run.Run, run.Messages) };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Runs")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentConnectorRunsResult>> Runs(string id, int take = 50)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var result = new RecordDeploymentConnectorRunsResult { Status = ResponseHelper.Success, Data = (await _connectors.GetRunsAsync(DepartmentId, UserId, id, take)).Select(r => ToData(r, null)).ToList() };
				result.PageSize = result.Data.Count;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Reconciliation")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDeploymentReconciliationResult>> Reconciliation(string id = null)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var items = await _connectors.GetReconciliationAsync(DepartmentId, UserId, id);
				var result = new RecordDeploymentReconciliationResult
				{
					Status = ResponseHelper.Success,
					Data = items.Select(i => new RecordDeploymentReconciliationData
					{
						ConnectorId = i.ConnectorId, OrderId = i.OrderId, RecordId = i.RecordId, OrderNumber = i.OrderNumber, RequestNumber = i.RequestNumber, Kind = i.Kind,
						SourceStatus = i.SourceStatus, LocalStatus = i.LocalStatus, SourceVersion = i.SourceVersion, SourceCapturedOn = i.SourceCapturedOn
					}).ToList()
				};
				result.PageSize = result.Data.Count;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>
		/// A source pushes a feed document. No user session: the connector id is in the query and the connector's
		/// token in <c>X-Resgrid-Connector-Token</c>. A wrong token and an unknown connector are refused alike, and a
		/// refused push leaves no run row, so the endpoint reveals nothing about what exists.
		/// </summary>
		[HttpPost("Inbound")]
		[AllowAnonymous]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
		public async Task<ActionResult<RecordDeploymentConnectorRunApiResult>> Inbound(string connectorId, CancellationToken cancellationToken)
		{
			if (!RecordsConnectorConfig.Enabled) return NotFound();
			if (!Request.Headers.TryGetValue(TokenHeader, out var tokenValues) || string.IsNullOrWhiteSpace(tokenValues.ToString())) return Unauthorized();
			if (Request.ContentLength.HasValue && Request.ContentLength.Value > RecordsConnectorConfig.MaxFeedBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge);

			string body;
			using (var buffer = new MemoryStream())
			{
				var chunk = new byte[64 * 1024];
				int read;
				while ((read = await Request.Body.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
				{
					buffer.Write(chunk, 0, read);
					if (buffer.Length > RecordsConnectorConfig.MaxFeedBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge);
				}
				body = Encoding.UTF8.GetString(buffer.ToArray());
			}

			try
			{
				var run = await _connectors.ImportInboundAsync(connectorId, tokenValues.ToString().Trim(), body, cancellationToken);
				var result = new RecordDeploymentConnectorRunApiResult { Status = ResponseHelper.Success, PageSize = 1, Data = ToData(run.Run, run.Messages) };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			// A wrong token is 401 here rather than the 403 an authenticated caller gets; a malformed feed, a spent
			// hourly limit or a connector that is not ready map exactly as they do on every other action.
			catch (UnauthorizedAccessException) { return Unauthorized(); }
			catch (Exception ex) { return Fail(ex); }
		}

		#region Helpers

		private async Task<ActionResult> GateAsync()
		{
			if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin() ? null : Forbid();
		}

		private RecordDeploymentConnectorResult Wrap(RmsExternalOrderConnector connector)
		{
			var result = new RecordDeploymentConnectorResult { Data = ToData(connector), Status = ResponseHelper.Success, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordConcurrencyException conflict: return Problem(statusCode: StatusCodes.Status409Conflict, title: conflict.Message, type: "record_connector_conflict");
				case ArgumentException argument: return Problem(statusCode: StatusCodes.Status400BadRequest, title: argument.Message, type: "record_connector_validation");
				case InvalidOperationException invalid: return Problem(statusCode: StatusCodes.Status409Conflict, title: invalid.Message, type: "record_connector_state");
				default: throw ex;
			}
		}

		public static RecordDeploymentConnectorInput ToInput(RecordDeploymentConnectorInputData input) => new RecordDeploymentConnectorInput
		{
			ProviderKey = input.ProviderKey, Name = input.Name, SourceSystem = input.SourceSystem, SourceScheme = input.SourceScheme, ProfileKey = input.ProfileKey, BaseUrl = input.BaseUrl,
			CredentialKind = input.CredentialKind ?? RmsConnectorCredentialKinds.None, CredentialHeaderName = input.CredentialHeaderName, Credential = input.Credential, ReadEnabled = input.ReadEnabled,
			WriteEnabled = input.WriteEnabled, PollIntervalMinutes = input.PollIntervalMinutes, MaxRequestsPerHour = input.MaxRequestsPerHour, TermsReference = input.TermsReference
		};

		public static RecordDeploymentConnectorData ToData(RmsExternalOrderConnector c) => c == null ? null : new RecordDeploymentConnectorData
		{
			Id = c.RmsExternalOrderConnectorId, ProviderKey = c.ProviderKey, Name = c.Name, SourceSystem = c.SourceSystem, SourceScheme = c.SourceScheme, ProfileKey = c.ProfileKey, BaseUrl = c.BaseUrl,
			CredentialKind = c.CredentialKind, CredentialHeaderName = c.CredentialHeaderName, HasCredential = !string.IsNullOrEmpty(c.CredentialCiphertext), HasInboundToken = !string.IsNullOrEmpty(c.InboundTokenHash),
			ReadEnabled = c.ReadEnabled, WriteEnabled = c.WriteEnabled, PollIntervalMinutes = c.PollIntervalMinutes, MaxRequestsPerHour = c.MaxRequestsPerHour, RequestsThisHour = c.RequestsThisHour,
			TermsReference = c.TermsReference, TermsAcknowledgedOn = c.TermsAcknowledgedOn, TermsAcknowledgedByUserId = c.TermsAcknowledgedByUserId, IsEnabled = c.IsEnabled, IsReadyToRun = c.IsReadyToRun,
			LastPolledOn = c.LastPolledOn, LastSuccessOn = c.LastSuccessOn, LastError = c.LastError, ConsecutiveFailures = c.ConsecutiveFailures, CreatedOn = c.CreatedOn, ModifiedOn = c.ModifiedOn, RowVersion = c.RowVersion
		};

		public static RecordDeploymentConnectorRunData ToData(RmsExternalOrderConnectorRun r, System.Collections.Generic.List<string> messages) => r == null ? null : new RecordDeploymentConnectorRunData
		{
			Id = r.RmsExternalOrderConnectorRunId, ConnectorId = r.RmsExternalOrderConnectorId, Trigger = r.Trigger, TriggeredByUserId = r.TriggeredByUserId, StartedOn = r.StartedOn, FinishedOn = r.FinishedOn,
			Outcome = r.Outcome, Error = r.Error, RequestCount = r.RequestCount, OrdersSeen = r.OrdersSeen, OrdersCreated = r.OrdersCreated, SnapshotsRecorded = r.SnapshotsRecorded, RequestsAdded = r.RequestsAdded,
			Unchanged = r.Unchanged, Rejected = r.Rejected, Conflicts = r.Conflicts, SourceVersion = r.SourceVersion, Messages = messages ?? new System.Collections.Generic.List<string>()
		};

		#endregion
	}
}
