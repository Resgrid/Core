using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Public-records and access-to-information workflow (RMS plan section 4.7, registry M0171, RMS-3).
	/// <para>
	/// Three rules shape everything here. A production never mutates a source revision — it is a new artifact, so
	/// answering a request can never damage the record it answers from. The produced set is snapshotted with the
	/// revision IDs and checksums that were released, so a later amendment cannot quietly change what the
	/// department is on the hook for. And redaction runs off the same classification metadata the protected-field
	/// catalog will consume, so this is built once and enrolls cleanly when Protected Data lands (plan 5.9).
	/// </para>
	/// </summary>
	public partial class RecordsDisclosureService : IRecordsDisclosureService
	{
		public const string NumberPrefix = "PRR-";

		private readonly IRmsDisclosureRequestsRepository _requests;
		private readonly IRmsDisclosureProductionsRepository _productions;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsUdfService _udf;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsDisclosureService(IRmsDisclosureRequestsRepository requests, IRmsDisclosureProductionsRepository productions,
			IRmsOperationalRecordsRepository records, IRmsRevisionsRepository revisions, IRmsAccessAuditsRepository audits,
			IRecordsAuthorizationService authorization, IDepartmentSettingsService settings, IUnitOfWork unitOfWork,
			IRmsIncidentReportsRepository reports, IRecordsDocumentService documents, IRmsRecordAttachmentsRepository attachments, Resgrid.Model.Providers.IPdfProvider pdf, IRmsIncidentAnalysesRepository analyses, IRecordAttachmentScanner scanner, IRecordsUdfService udf)
		{
			_requests = requests;
			_productions = productions;
			_records = records;
			_revisions = revisions;
			_audits = audits;
			_authorization = authorization;
			_udf = udf;
			_settings = settings;
			_unitOfWork = unitOfWork;
			_reports = reports; _documents = documents; _attachments = attachments; _pdf = pdf; _analyses = analyses; _scanner = scanner;
		}

		public async Task<RmsDisclosureRequest> CreateRequestAsync(int departmentId, string userId, RmsDisclosureRequest request, CancellationToken cancellationToken = default)
		{
			await RequireDisclosureAsync(departmentId, userId);
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (string.IsNullOrWhiteSpace(request.RequesterName))
				throw new ArgumentException("A requester is required.", nameof(request));

			var config = await SafeConfigAsync(departmentId);
			var now = DateTime.UtcNow;
			var receivedOn = request.ReceivedOn == default ? now : request.ReceivedOn;

			request.RmsDisclosureRequestId = Guid.NewGuid().ToString();
			request.DepartmentId = departmentId;
			request.ProtectionId = Guid.NewGuid().ToString();
			request.ReceivedOn = receivedOn;
			// The clock starts when the department received it, not when someone got round to logging it.
			request.StatutoryDueOn ??= receivedOn.AddDays(Math.Max(1, config.StatutoryClockDays));
			request.State = (int)RmsDisclosureState.Received;
			request.RedactionProfile = Blank(request.RedactionProfile) ?? Blank(config.DefaultRedactionProfile) ?? RmsRedactionProfiles.Standard;
			request.CreatedOn = now;
			request.CreatedByUserId = userId;
			request.ModifiedOn = now;
			request.ModifiedByUserId = userId;
			request.RowVersion = 1;

			await InTransactionAsync(async () =>
			{
				request.RequestNumber = await AllocateNumberAsync(departmentId, receivedOn);
				await _requests.InsertAsync(request, cancellationToken, true);
				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Admin, "Disclosure request logged",
					new { request.RmsDisclosureRequestId, request.RequestNumber, request.StatutoryDueOn, request.JurisdictionProfile }, cancellationToken);
			});

			return request;
		}

		public async Task<RmsDisclosureRequest> GetAsync(int departmentId, string userId, string requestId)
		{
			await RequireDisclosureAsync(departmentId, userId);
			var row = await _requests.GetByIdForDepartmentAsync(departmentId, requestId);
			var restricted = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			await RequireDisclosureAsync(departmentId, userId);
			return row?.DeletedOn == null ? ProjectRequest(row, restricted) : null;
		}

		public async Task<List<RmsDisclosureRequest>> QueryAsync(int departmentId, string userId, IEnumerable<RmsDisclosureState> states, int skip = 0, int take = 50)
		{
			await RequireDisclosureAsync(departmentId, userId);
			var stateValues = states?.Select(s => (int)s).ToList();
			var rows = (await _requests.GetForDepartmentAsync(departmentId, stateValues, skip, take))?.ToList() ?? new List<RmsDisclosureRequest>();
			var restricted = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			await RequireDisclosureAsync(departmentId, userId);
			return rows.Select(r => ProjectRequest(r, restricted)).ToList();
		}
		private static RmsDisclosureRequest ProjectRequest(RmsDisclosureRequest row, bool restricted)
		{
			if (row == null) return null;
			var copy = JsonConvert.DeserializeObject<RmsDisclosureRequest>(JsonConvert.SerializeObject(row));
			if (!restricted) { copy.RequesterName = null; copy.RequesterOrganization = null; copy.RequesterContact = null; }
			return copy;
		}

		public async Task<RmsDisclosureRequest> SaveScopeAsync(int departmentId, string userId, string requestId, string scopeNarrative, RmsRecordQuery scope, string redactionProfile, CancellationToken cancellationToken = default)
		{
			await RequireDisclosureAsync(departmentId, userId);
			if (scope?.IncludeLegacy == true) throw new ArgumentException("Legacy Logs require a separately recorded review; this packet scope contains RMS records only.");
			var request = await LoadAsync(departmentId, requestId);
			RequireOpen(request);

			// Once something has been produced, the scope is the thing that was produced against; changing it
			// would leave the release describing a query that no longer exists.
			var existing = await _productions.GetForRequestAsync(departmentId, requestId);
			if (existing != null && existing.Any())
				throw new InvalidOperationException("The scope cannot change after a production exists; log a new request for a wider scope.");

			var now = DateTime.UtcNow;
			request.ScopeNarrative = Blank(scopeNarrative);
			request.ScopeQueryJson = scope == null ? null : JsonConvert.SerializeObject(Sanitize(scope));
			request.RedactionProfile = Blank(redactionProfile) ?? request.RedactionProfile;
			request.State = (int)RmsDisclosureState.Scoping;
			request.ModifiedOn = now;
			request.ModifiedByUserId = userId;
			request.RowVersion += 1;

			await InTransactionAsync(async () =>
			{
				await GuardRequestAsync(request, request.RowVersion - 1, cancellationToken);
				await _requests.UpdateAsync(request, cancellationToken, true);
				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Admin, "Disclosure scope saved", new { requestId, request.RedactionProfile }, cancellationToken);
			});

			return request;
		}

		public async Task<RmsDisclosureProduction> ReleaseAsync(int departmentId, string userId, string productionId, CancellationToken cancellationToken = default, string deliveryMethod = null, string deliveryReference = null)
		{
			var production = await GetAuthorizedProductionAsync(departmentId, userId, productionId)
				?? throw new UnauthorizedAccessException("The production is not accessible with your current permissions.");

			if (production.ReleasedOn.HasValue)
				throw new InvalidOperationException("The production has already been released.");
			if (string.IsNullOrWhiteSpace(deliveryMethod) || string.IsNullOrWhiteSpace(deliveryReference)) throw new ArgumentException("Record how the packet was delivered and its receipt or delivery reference.");
			if (deliveryMethod.Length > 200 || deliveryReference.Length > 1000) throw new ArgumentException("Delivery method is limited to 200 characters and reference to 1,000 characters.");

			var request = await LoadAsync(departmentId, production.DisclosureRequestId);
			RequireOpen(request);
			var unresolved = (bool?)JObject.Parse(production.ArtifactJson)["scope_fully_resolved"] == false;
			var now = DateTime.UtcNow;

			await InTransactionAsync(async () =>
			{
				await GuardRequestAsync(request, request.RowVersion, cancellationToken);
				production = await GetAuthorizedProductionAsync(departmentId, userId, productionId) ?? throw new UnauthorizedAccessException();
				if (production.ReleasedOn.HasValue || !await _productions.TryReleaseAsync(departmentId, productionId, production.RowVersion, userId, now, deliveryMethod.Trim(), deliveryReference.Trim(), cancellationToken))
					throw new InvalidOperationException("The production has already been released or changed. Reload it before continuing.");
				production = JsonConvert.DeserializeObject<RmsDisclosureProduction>(JsonConvert.SerializeObject(production));
				production.ReleasedByUserId = userId;
				production.ReleasedOn = now;
				production.DeliveryMethod = deliveryMethod.Trim(); production.DeliveryReference = deliveryReference.Trim();
				production.ModifiedOn = now;
				production.RowVersion += 1;

				request.State = (int)(unresolved ? RmsDisclosureState.InReview : RmsDisclosureState.Released);
				request.ClosedOn = unresolved ? null : now;
				request.ClosedByUserId = unresolved ? null : userId;
				request.ModifiedOn = now;
				request.ModifiedByUserId = userId;
				request.RowVersion += 1;
				await _requests.UpdateAsync(request, cancellationToken, true);

				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Share, "Disclosure released",
					new { request.RmsDisclosureRequestId, request.RequestNumber, production.RmsDisclosureProductionId, production.Checksum, deliveryMethod = deliveryMethod.Trim(), deliveryReference = deliveryReference.Trim(), unresolvedScope = unresolved }, cancellationToken);
			});

			return production;
		}

		public async Task<List<RmsDisclosureProduction>> GetProductionsAsync(int departmentId, string userId, string requestId)
		{
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures)) return new List<RmsDisclosureProduction>();
			var visible = new List<RmsDisclosureProduction>();
			foreach (var row in (await _productions.GetForRequestAsync(departmentId, requestId)) ?? Enumerable.Empty<RmsDisclosureProduction>())
			{
				var authorized = await GetAuthorizedProductionAsync(departmentId, userId, row.RmsDisclosureProductionId);
				if (authorized?.DisclosureRequestId == requestId) visible.Add(authorized);
			}
			// Reading a later packet can outlive the permissions used for an earlier one. Re-project the
			// completed collection instead of returning objects authorized during source hydration.
			var current = new List<RmsDisclosureProduction>();
			foreach (var row in visible)
			{
				var authorized = await GetAuthorizedProductionAsync(departmentId, userId, row.RmsDisclosureProductionId);
				if (authorized?.DisclosureRequestId == requestId) current.Add(authorized);
			}
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures)) return new List<RmsDisclosureProduction>();
			return current;
		}

		public async Task<RmsDisclosureProduction> GetAuthorizedProductionAsync(int departmentId, string userId, string productionId)
		{
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures)) return null;
			var production = await _productions.GetByIdForDepartmentAsync(departmentId, productionId);
			if (production == null || production.DepartmentId != departmentId || string.IsNullOrEmpty(production.ArtifactJson)
				|| production.Checksum != RecordSnapshotSerializer.Checksum(production.ArtifactJson)) return null;
			try
			{
				var artifact = JObject.Parse(production.ArtifactJson);
				var produced = JArray.Parse(production.ProducedSetJson);
				var manifest = artifact["manifest"] as JArray;
				if (produced.Count == 0 || produced.Count != production.RecordCount || manifest == null || manifest.Count != produced.Count) return null;
				var restricted = (bool?)artifact["restricted_content_included"] ?? production.RedactionProfile == RmsRedactionProfiles.FullDisclosure;
				var visibilityRequired=(int?)artifact["udf_visibility_required"] ?? 0;
				var containsUdf=(artifact["documents"] as JArray ?? new JArray()).OfType<JObject>().Any(d=>(((d["content"] as JObject)?["CustomFields"] as JObject)?["Fields"] as JArray)?.Count>0);
				if ((containsUdf || visibilityRequired>0) && await _udf.GetVisibilityLevelAsync(departmentId,userId)<visibilityRequired) return null;
				if (restricted && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) return null;
				for (var i = 0; i < produced.Count; i++)
				{
					if (!JToken.DeepEquals(produced[i], manifest[i]["record"])) return null;
					var id = (string)produced[i]["record_id"];
					if (string.IsNullOrWhiteSpace(id) || !await CanViewDisclosureRecordAsync(departmentId, userId, id, (RmsRecordKind)((int?)produced[i]["record_kind"] ?? (int)RmsRecordKind.Operational))) return null;
				}
				if ((containsUdf || visibilityRequired>0) && await _udf.GetVisibilityLevelAsync(departmentId,userId)<visibilityRequired) return null;
				if (restricted && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) return null;
				if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures)) return null;
				return production;
			}
			catch (JsonException) { return null; }
			catch (InvalidOperationException) { return null; }
			catch (ArgumentException) { return null; }
		}

		public async Task<RmsDisclosureRequest> CloseAsync(int departmentId, string userId, string requestId, RmsDisclosureState disposition, string reason, CancellationToken cancellationToken = default)
		{
			await RequireDisclosureAsync(departmentId, userId);
			if (disposition != RmsDisclosureState.Denied && disposition != RmsDisclosureState.Withdrawn && disposition != RmsDisclosureState.Closed)
				throw new ArgumentException("A request closes as denied, withdrawn or closed.", nameof(disposition));
			if (string.IsNullOrWhiteSpace(reason))
				throw new ArgumentException("A reason is required; a refusal without a recorded basis is not defensible.", nameof(reason));

			var request = await LoadAsync(departmentId, requestId);
			RequireOpen(request);

			var now = DateTime.UtcNow;
			request.State = (int)disposition;
			request.DispositionReason = reason.Trim();
			request.ClosedOn = now;
			request.ClosedByUserId = userId;
			request.ModifiedOn = now;
			request.ModifiedByUserId = userId;
			request.RowVersion += 1;

			await InTransactionAsync(async () =>
			{
				await GuardRequestAsync(request, request.RowVersion - 1, cancellationToken);
				await _requests.UpdateAsync(request, cancellationToken, true);
				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Admin, "Disclosure closed: " + disposition, new { requestId, reason = request.DispositionReason }, cancellationToken);
			});

			return request;
		}

		public async Task<bool> VerifyProductionAsync(int departmentId, string productionId)
		{
			var production = await _productions.GetByIdForDepartmentAsync(departmentId, productionId);
			if (production == null || string.IsNullOrWhiteSpace(production.Checksum))
				return false;

			return string.Equals(production.Checksum, RecordSnapshotSerializer.Checksum(production.ArtifactJson ?? string.Empty), StringComparison.Ordinal);
		}

		// ── internals ────────────────────────────────────────────────────────────────

		/// <summary>
		/// A scope arriving from a client is never trusted with the viewer fields: those are set from the caller's
		/// own authorization, or a request could be scoped to see somebody else's groups.
		/// </summary>
		private static RmsRecordQuery Sanitize(RmsRecordQuery scope)
		{
			return new RmsRecordQuery
			{
				States = scope.States,
				DefinitionKey = scope.DefinitionKey,
				Year = scope.Year,
				CallId = scope.CallId,
				StationGroupId = scope.StationGroupId,
				Take = Math.Clamp(scope.Take <= 0 ? 200 : scope.Take, 1, 1000)
			};
		}

		private static RmsRecordQuery ParseScope(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try { return JsonConvert.DeserializeObject<RmsRecordQuery>(json); }
			catch (JsonException ex) { Logging.LogException(ex, "A disclosure scope query could not be parsed."); return null; }
		}

		private async Task<RmsDisclosureRequest> LoadAsync(int departmentId, string requestId)
		{
			var request = await _requests.GetByIdForDepartmentAsync(departmentId, requestId);
			if (request == null || request.DeletedOn.HasValue)
				throw new InvalidOperationException("The disclosure request does not exist.");
			return JsonConvert.DeserializeObject<RmsDisclosureRequest>(JsonConvert.SerializeObject(request));
		}

		private static void RequireOpen(RmsDisclosureRequest request)
		{
			if (request.ClosedOn.HasValue)
				throw new InvalidOperationException("The request is closed; log a new one rather than reopening a released answer.");
		}

		private async Task<string> AllocateNumberAsync(int departmentId, DateTime receivedOn)
		{
			var prefix = NumberPrefix + receivedOn.Year + "-";
			var sequence = await _requests.GetMaxRequestNumberSequenceAsync(departmentId, prefix) + 1;
			return prefix + sequence.ToString("D4");
		}

		private async Task<RecordsDisclosureConfig> SafeConfigAsync(int departmentId)
		{
			try { return await _settings.GetRecordsDisclosureConfigAsync(departmentId) ?? new RecordsDisclosureConfig(); }
			catch (Exception ex) { Logging.LogException(ex); return new RecordsDisclosureConfig(); }
		}

		private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		private async Task GuardRequestAsync(RmsDisclosureRequest request, long expectedVersion, CancellationToken ct)
		{ if (!await _requests.TryBumpRowVersionAsync(request.DepartmentId, request.RmsDisclosureRequestId, expectedVersion, ct)) throw new InvalidOperationException("The disclosure request changed. Reload it before continuing."); }

		private Task AuditAsync(int departmentId, string userId, string recordId, RmsAccessAuditAction action, string purpose, object detail, CancellationToken cancellationToken)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				Action = (int)action,
				ActorUserId = userId,
				Purpose = purpose,
				OriginClient = (int)RmsOriginClient.Web,
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}
	}
}
