using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
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
	public class RecordsDisclosureService : IRecordsDisclosureService
	{
		public const string NumberPrefix = "PRR-";

		private readonly IRmsDisclosureRequestsRepository _requests;
		private readonly IRmsDisclosureProductionsRepository _productions;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsDisclosureService(IRmsDisclosureRequestsRepository requests, IRmsDisclosureProductionsRepository productions,
			IRmsOperationalRecordsRepository records, IRmsRevisionsRepository revisions, IRmsAccessAuditsRepository audits,
			IRecordsAuthorizationService authorization, IDepartmentSettingsService settings, IUnitOfWork unitOfWork)
		{
			_requests = requests;
			_productions = productions;
			_records = records;
			_revisions = revisions;
			_audits = audits;
			_authorization = authorization;
			_settings = settings;
			_unitOfWork = unitOfWork;
		}

		public async Task<RmsDisclosureRequest> CreateRequestAsync(int departmentId, string userId, RmsDisclosureRequest request, CancellationToken cancellationToken = default)
		{
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

		public Task<RmsDisclosureRequest> GetAsync(int departmentId, string requestId)
		{
			return _requests.GetByIdForDepartmentAsync(departmentId, requestId);
		}

		public async Task<List<RmsDisclosureRequest>> QueryAsync(int departmentId, IEnumerable<RmsDisclosureState> states, int skip = 0, int take = 50)
		{
			var stateValues = states?.Select(s => (int)s).ToList();
			return (await _requests.GetForDepartmentAsync(departmentId, stateValues, skip, take))?.ToList() ?? new List<RmsDisclosureRequest>();
		}

		public async Task<RmsDisclosureRequest> SaveScopeAsync(int departmentId, string userId, string requestId, string scopeNarrative, RmsRecordQuery scope, string redactionProfile, CancellationToken cancellationToken = default)
		{
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
				await _requests.UpdateAsync(request, cancellationToken, true);
				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Admin, "Disclosure scope saved", new { requestId, request.RedactionProfile }, cancellationToken);
			});

			return request;
		}

		public async Task<RmsDisclosureScopePreview> PreviewScopeAsync(int departmentId, string userId, string requestId, int take = 200)
		{
			var request = await LoadAsync(departmentId, requestId);
			var preview = new RmsDisclosureScopePreview();

			var scope = ParseScope(request.ScopeQueryJson);
			if (scope == null)
				return preview;

			// The same authorization and group-scope path as the Records queue. A disclosure officer does not get
			// a wider view of the department than they have anywhere else in the product.
			scope.VisibleGroupIds = await _authorization.GetVisibleGroupIdsAsync(userId, departmentId);
			scope.ViewerUserId = userId;
			scope.Skip = 0;
			scope.Take = Math.Clamp(take, 1, 1000);

			var matched = (await _records.GetByDepartmentAndStatesAsync(departmentId, scope.States, scope.Year, scope.Skip, scope.Take + 1))?.ToList()
				?? new List<RmsOperationalRecord>();

			preview.Truncated = matched.Count > scope.Take;
			foreach (var record in matched.Take(scope.Take))
			{
				if (!string.IsNullOrWhiteSpace(scope.DefinitionKey) && !string.Equals(record.DefinitionKey, scope.DefinitionKey, StringComparison.Ordinal))
					continue;

				preview.MatchedCount++;

				// Group scoping still applies to a disclosure preview; an officer outside the group sees the same
				// nothing they would see in the queue.
				if (scope.VisibleGroupIds != null && !await _authorization.CanUserViewRecordAsync(userId, record.RmsOperationalRecordId, departmentId))
				{
					preview.WithheldWholeRecordCount++;
					continue;
				}

				var producible = IsProducible(record, out var reason);
				if (producible)
					preview.ProducibleCount++;

				preview.Items.Add(new RmsDisclosureScopeItem
				{
					RecordId = record.RmsOperationalRecordId,
					RecordNumber = record.RecordNumber ?? record.DraftReference,
					DefinitionKey = record.DefinitionKey,
					Summary = record.DisplaySummary,
					OccurredOn = record.StartedOn ?? record.CreatedOn,
					CurrentRevisionId = record.CurrentRevisionId,
					Producible = producible,
					NotProducibleReason = reason
				});
			}

			return preview;
		}

		public async Task<RmsDisclosureProduction> ProduceAsync(int departmentId, string userId, string requestId, string redactionProfile = null, CancellationToken cancellationToken = default)
		{
			var request = await LoadAsync(departmentId, requestId);
			RequireOpen(request);

			var profile = Blank(redactionProfile) ?? request.RedactionProfile ?? RmsRedactionProfiles.Standard;
			var preview = await PreviewScopeAsync(departmentId, userId, requestId, 1000);
			var producible = preview.Items.Where(i => i.Producible).ToList();
			if (producible.Count == 0)
				throw new InvalidOperationException("The scope resolves to nothing that can be produced; a draft record is not a public record.");

			var withheld = new List<RmsRedactionEntry>();
			var produced = new List<object>();
			var documents = new List<object>();

			foreach (var item in producible)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var revision = string.IsNullOrWhiteSpace(item.CurrentRevisionId)
					? null
					: await _revisions.GetByIdForDepartmentAsync(departmentId, item.CurrentRevisionId);

				if (revision == null || string.IsNullOrWhiteSpace(revision.SnapshotJson))
				{
					withheld.Add(new RmsRedactionEntry { RecordId = item.RecordId, Section = "Record", Field = "*", Basis = "No finalized revision to produce from." });
					continue;
				}

				var snapshot = RecordSnapshotSerializer.Deserialize(revision.SnapshotJson);
				documents.Add(Redact(snapshot, item, profile, withheld));

				// The produced set is the point: exactly which revision, and its checksum at release time.
				produced.Add(new
				{
					record_id = item.RecordId,
					record_number = item.RecordNumber,
					revision_id = revision.RmsRevisionId,
					revision_number = revision.RevisionNumber,
					revision_checksum = revision.Checksum
				});
			}

			if (documents.Count == 0)
				throw new InvalidOperationException("Nothing in scope has a finalized revision to produce from.");

			var now = DateTime.UtcNow;
			var artifactJson = RecordsEvidenceService.Serialize(new
			{
				request_number = request.RequestNumber,
				jurisdiction_profile = request.JurisdictionProfile,
				redaction_profile = profile,
				produced_on = now,
				// The manifest and page numbering the plan asks for; a packet a requester can navigate.
				manifest = documents.Select((d, i) => new { page = i + 1, record = produced[i] }).ToList(),
				documents
			});

			var production = new RmsDisclosureProduction
			{
				RmsDisclosureProductionId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				DisclosureRequestId = requestId,
				RedactionProfile = profile,
				ProducedSetJson = RecordsEvidenceService.Serialize(produced),
				ArtifactJson = artifactJson,
				Checksum = RecordSnapshotSerializer.Checksum(artifactJson),
				ByteSize = Encoding.UTF8.GetByteCount(artifactJson),
				RecordCount = documents.Count,
				WithheldFieldsJson = RecordsEvidenceService.Serialize(withheld),
				WithheldFieldCount = withheld.Count,
				PreparedByUserId = userId,
				PreparedOn = now,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};

			await InTransactionAsync(async () =>
			{
				production.ProductionNumber = await _productions.GetMaxProductionNumberAsync(departmentId, requestId) + 1;
				await _productions.InsertAsync(production, cancellationToken, true);

				request.State = (int)RmsDisclosureState.Produced;
				request.ModifiedOn = now;
				request.ModifiedByUserId = userId;
				request.RowVersion += 1;
				await _requests.UpdateAsync(request, cancellationToken, true);

				// Every produced record is audited individually: "what did we hand out about this record" has to
				// be answerable from the record, not only from the request.
				foreach (var item in producible.Take(documents.Count))
					await AuditAsync(departmentId, userId, item.RecordId, RmsAccessAuditAction.Export, "Disclosure production " + request.RequestNumber,
						new { production.RmsDisclosureProductionId, production.ProductionNumber, profile }, cancellationToken);

				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Export, "Disclosure produced",
					new { requestId, production.RmsDisclosureProductionId, production.RecordCount, production.WithheldFieldCount, production.Checksum }, cancellationToken);
			});

			return production;
		}

		public async Task<RmsDisclosureProduction> ReleaseAsync(int departmentId, string userId, string productionId, CancellationToken cancellationToken = default)
		{
			var production = await _productions.GetByIdForDepartmentAsync(departmentId, productionId)
				?? throw new InvalidOperationException("The production does not exist.");

			if (production.ReleasedOn.HasValue)
				throw new InvalidOperationException("The production has already been released.");

			var request = await LoadAsync(departmentId, production.DisclosureRequestId);
			var now = DateTime.UtcNow;

			await InTransactionAsync(async () =>
			{
				production.ReleasedByUserId = userId;
				production.ReleasedOn = now;
				production.ModifiedOn = now;
				production.RowVersion += 1;
				await _productions.UpdateAsync(production, cancellationToken, true);

				request.State = (int)RmsDisclosureState.Released;
				request.ClosedOn = now;
				request.ClosedByUserId = userId;
				request.ModifiedOn = now;
				request.ModifiedByUserId = userId;
				request.RowVersion += 1;
				await _requests.UpdateAsync(request, cancellationToken, true);

				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Share, "Disclosure released",
					new { request.RmsDisclosureRequestId, request.RequestNumber, production.RmsDisclosureProductionId, production.Checksum }, cancellationToken);
			});

			return production;
		}

		public async Task<List<RmsDisclosureProduction>> GetProductionsAsync(int departmentId, string requestId)
		{
			return (await _productions.GetForRequestAsync(departmentId, requestId))?.ToList() ?? new List<RmsDisclosureProduction>();
		}

		public async Task<RmsDisclosureRequest> CloseAsync(int departmentId, string userId, string requestId, RmsDisclosureState disposition, string reason, CancellationToken cancellationToken = default)
		{
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
		/// Redacts one revision snapshot for release. Restricted detail fields come out under the standard
		/// profile; participant identity comes out as well under the no-identifiers profile. Withholding is
		/// logged rather than silent, because a requester is entitled to know something was withheld even when
		/// they are not entitled to the content.
		/// </summary>
		private static object Redact(RecordSnapshot snapshot, RmsDisclosureScopeItem item, string profile, List<RmsRedactionEntry> withheld)
		{
			var full = string.Equals(profile, RmsRedactionProfiles.FullDisclosure, StringComparison.Ordinal);
			var hideIdentities = string.Equals(profile, RmsRedactionProfiles.NoPersonalIdentifiers, StringComparison.Ordinal);

			var details = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var field in RecordSnapshotSerializer.DetailFieldOrder)
			{
				var value = ReadDetail(snapshot?.Details, field);
				if (value == null)
					continue;

				if (!full && RecordSnapshotSerializer.RestrictedDetailFields.Contains(field))
				{
					withheld.Add(new RmsRedactionEntry { RecordId = item.RecordId, Section = "Details", Field = field, Basis = "Restricted class" });
					continue;
				}

				details[field] = value;
			}

			var participants = new List<object>();
			foreach (var participant in snapshot?.Participants ?? new List<RmsRecordParticipant>())
			{
				if (hideIdentities)
				{
					withheld.Add(new RmsRedactionEntry { RecordId = item.RecordId, Section = "Participants", Field = "Identity", Basis = "Personal identifiers withheld by profile" });
					continue;
				}

				participants.Add(new { name = participant.DisplayNameSnapshot, role = participant.Role, group = participant.GroupNameSnapshot });
			}

			return new
			{
				record_id = item.RecordId,
				record_number = item.RecordNumber,
				definition_key = item.DefinitionKey,
				occurred_on = item.OccurredOn,
				summary = item.Summary,
				details,
				participants,
				units = (snapshot?.Units ?? new List<RmsRecordUnitResponse>()).Select(u => new { unit = u.UnitNameSnapshot }).ToList()
			};
		}

		private static string ReadDetail(RmsOperationalRecordDetail details, string field)
		{
			if (details == null)
				return null;

			var property = typeof(RmsOperationalRecordDetail).GetProperty(field);
			return property?.PropertyType == typeof(string) ? (string)property.GetValue(details) : null;
		}

		/// <summary>A public record is a finalized one. Drafts and voided records are listed, never produced.</summary>
		private static bool IsProducible(RmsOperationalRecord record, out string reason)
		{
			var state = (RmsRecordState)record.State;
			if (state == RmsRecordState.Finalized || state == RmsRecordState.Amended)
			{
				reason = null;
				return true;
			}

			reason = state == RmsRecordState.Voided || state == RmsRecordState.Cancelled
				? "The record was voided or cancelled."
				: "The record is not finalized; a draft is not a public record.";
			return false;
		}

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
			return request;
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
