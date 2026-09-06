using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Create Deployment from External Order (RMS plan section 4.1 "external-order fill contract", RMS-1C, Preview).
	/// The ordering system (IROC, CIFFC, a member agency, a local compact) stays authoritative: the order arrives as a
	/// manually entered, checksummed snapshot; each supplied resource links to its exact request/fill number; later
	/// snapshots are versioned, never overwriting signed history; and a resource is returned only when the
	/// department says so, never because the external system marked it released. The deployment itself is a Record
	/// on the Mutual Aid pack's deployment definition, so revisions, audit, retention and Workflow events are the
	/// ordinary Records ones.
	/// </summary>
	public class RecordDeploymentsService : IRecordDeploymentsService
	{
		public const string DeploymentTemplateKey = "pack.mutual-aid.deployment";
		public const string DefaultDefinitionKey = "mutual-aid.deployment";

		private readonly IRmsExternalOrdersRepository _orders;
		private readonly IRmsExternalOrderFillsRepository _fills;
		private readonly IRmsExternalReferencesRepository _references;
		private readonly IRecordsService _records;
		private readonly IRecordDefinitionsService _definitions;
		private readonly IRecordTemplatePacksService _packs;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IUnitOfWork _unitOfWork;

		public RecordDeploymentsService(IRmsExternalOrdersRepository orders, IRmsExternalOrderFillsRepository fills, IRmsExternalReferencesRepository references, IRecordsService records,
			IRecordDefinitionsService definitions, IRecordTemplatePacksService packs, IRecordsAuthorizationService authorization, IRmsAccessAuditsRepository audits, IUnitOfWork unitOfWork)
		{
			_orders = orders;
			_fills = fills;
			_references = references;
			_records = records;
			_definitions = definitions;
			_packs = packs;
			_authorization = authorization;
			_audits = audits;
			_unitOfWork = unitOfWork;
		}

		public async Task<string> EnsureDeploymentDefinitionAsync(int departmentId, string userId, string profileKey, CancellationToken cancellationToken = default)
		{
			var existing = (await _definitions.ListAsync(departmentId)).FirstOrDefault(d => string.Equals(d.TemplateKey, DeploymentTemplateKey, StringComparison.OrdinalIgnoreCase) && d.PublishedVersion.HasValue && !d.Retired);
			if (existing != null) return existing.Key;
			var draftOnly = (await _definitions.ListAsync(departmentId)).FirstOrDefault(d => string.Equals(d.TemplateKey, DeploymentTemplateKey, StringComparison.OrdinalIgnoreCase) && !d.Retired);
			RecordDefinitionAggregate aggregate;
			if (draftOnly != null)
				aggregate = await _definitions.GetAsync(departmentId, draftOnly.Key);
			else
			{
				var profile = RmsDeploymentProfiles.IsKnown(profileKey) ? ProfileFor(profileKey) : "generic";
				aggregate = await _definitions.CreateAsync(departmentId, userId, new RecordDefinitionCreateInput { DefinitionKey = DefaultDefinitionKey, Name = "Deployment (External Order)", Category = "Mutual aid", TemplateKey = DeploymentTemplateKey, JurisdictionProfileKey = profile }, cancellationToken);
			}
			var draft = aggregate.Draft ?? throw new InvalidOperationException("The deployment definition has no draft to publish.");
			await _definitions.PublishAsync(departmentId, userId, aggregate.Definition.DefinitionKey, draft.Version, draft.RowVersion, cancellationToken);
			return aggregate.Definition.DefinitionKey;
		}

		private static string ProfileFor(string deploymentProfile)
		{
			switch ((deploymentProfile ?? string.Empty).ToLowerInvariant())
			{
				case RmsDeploymentProfiles.UsWildland: case RmsDeploymentProfiles.Compact: return "us";
				case RmsDeploymentProfiles.CaWildland: return "ca";
				case RmsDeploymentProfiles.CrossBorder: return "us-ca";
				default: return "generic";
			}
		}

		public async Task<RecordDeploymentAggregate> CreateFromExternalOrderAsync(int departmentId, string userId, RecordDeploymentCreateInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (!RmsDeploymentProfiles.IsKnown(input.ProfileKey)) throw new ArgumentException($"'{input.ProfileKey}' is not a deployment profile ({string.Join(", ", RmsDeploymentProfiles.All)}).", nameof(input));
			if (string.IsNullOrWhiteSpace(input.OrderNumber)) throw new ArgumentException("The external order number is required.", nameof(input));
			if (string.IsNullOrWhiteSpace(input.IncidentName)) throw new ArgumentException("The incident name is required.", nameof(input));
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord)) throw new UnauthorizedAccessException("Creating a deployment is not authorized.");
			if (input.ArtifactData != null && input.ArtifactData.Length > 25 * 1024 * 1024) throw new ArgumentException("The order artifact exceeds 25 MB.", nameof(input));
			if (!string.IsNullOrWhiteSpace(input.CurrencyCode) && !RmsCurrencies.IsSupported(input.CurrencyCode)) throw new ArgumentException($"'{input.CurrencyCode}' is not a supported currency.", nameof(input));

			var profileKey = input.ProfileKey.ToLowerInvariant();
			var homeProfile = input.HomeProfileKey ?? (profileKey == RmsDeploymentProfiles.CrossBorder ? "us" : ProfileFor(profileKey));
			var hostProfile = input.HostProfileKey ?? (profileKey == RmsDeploymentProfiles.CrossBorder ? "ca" : ProfileFor(profileKey));
			var profile = await _packs.GetProfileAsync(ProfileFor(profileKey)) ?? await _packs.GetProfileAsync("generic");

			var definitionKey = await EnsureDeploymentDefinitionAsync(departmentId, userId, profileKey, cancellationToken);
			var draft = new RecordDraftInput
			{
				DefinitionKey = definitionKey, StationGroupId = input.StationGroupId, IdempotencyKey = input.IdempotencyKey, OriginClient = input.OriginClient,
				StartedOn = DateTime.UtcNow, ExternalId = input.OrderNumber.Trim(), Values = BuildValues(input, userId, profileKey)
			};
			var record = await _records.CreateDraftAsync(departmentId, userId, draft, cancellationToken);

			var now = DateTime.UtcNow;
			var order = new RmsExternalOrder
			{
				RmsExternalOrderId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = record.Record.RmsOperationalRecordId,
				ProfileKey = profileKey, ProfileVersion = profile?.Version ?? 1, HomeProfileKey = homeProfile, HostProfileKey = hostProfile,
				SourceScheme = (input.SourceScheme ?? DefaultScheme(profileKey)).Trim(), SourceSystem = input.SourceSystem?.Trim(), OrderNumber = input.OrderNumber.Trim(),
				IncidentName = input.IncidentName.Trim(), IncidentNumber = input.IncidentNumber?.Trim(), IncidentCountry = input.IncidentCountry?.Trim().ToUpperInvariant(), IncidentSubdivision = input.IncidentSubdivision?.Trim().ToUpperInvariant(),
				OrderingOffice = input.OrderingOffice?.Trim(), DispatchOffice = input.DispatchOffice?.Trim(), RequestingAgency = input.RequestingAgency?.Trim(), ReceivingAgency = input.ReceivingAgency?.Trim(), SendingAgency = input.SendingAgency?.Trim(),
				DepartmentRole = string.IsNullOrWhiteSpace(input.DepartmentRole) ? "filling" : input.DepartmentRole.Trim().ToLowerInvariant(), CostCode = input.CostCode?.Trim(), AgreementReference = input.AgreementReference?.Trim(),
				CurrencyCode = (input.CurrencyCode ?? profile?.CurrencyCode ?? "USD").ToUpperInvariant(), MeasurementSystem = input.MeasurementSystem ?? profile?.MeasurementSystem ?? "metric",
				TimeZoneId = input.TimeZoneId, CapturedOffsetMinutes = input.CapturedOffsetMinutes, SourceCapturedOn = input.SourceCapturedOn ?? now, SourceVersion = input.SourceVersion ?? "1",
				ArtifactFileName = input.ArtifactData == null ? null : input.ArtifactFileName, ArtifactContentType = input.ArtifactData == null ? null : input.ArtifactContentType,
				ArtifactChecksum = input.ArtifactData == null ? null : RecordSnapshotSerializer.Checksum(input.ArtifactData), ArtifactData = input.ArtifactData, ArtifactSafeUrl = SafeUrl(input.ArtifactSafeUrl),
				Status = (int)RmsExternalOrderStatus.Open, CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, ModifiedByUserId = userId, RowVersion = 1
			};
			var fills = (input.Fills ?? new List<RecordDeploymentFillInput>()).Select(f => ToFill(order, f, userId, now)).ToList();

			await InTransactionAsync(async () =>
			{
				await _orders.InsertAsync(order, cancellationToken, true);
				foreach (var fill in fills) await _fills.InsertAsync(fill, cancellationToken, true);
				await AuditAsync(departmentId, userId, order, $"Create deployment from external order {order.OrderNumber} ({profileKey})", cancellationToken);
			});
			return await GetAsync(departmentId, userId, order.RmsExternalOrderId);
		}

		private static List<RecordValueInput> BuildValues(RecordDeploymentCreateInput input, string userId, string profileKey)
		{
			var values = new List<RecordValueInput>
			{
				new RecordValueInput { SectionKey = "order", FieldKey = "profile", Value = ProfileOptionKey(profileKey) },
				new RecordValueInput { SectionKey = "order", FieldKey = "order_number", ReferenceId = input.OrderNumber?.Trim(), ReferenceType = input.SourceScheme ?? DefaultScheme(profileKey) },
				new RecordValueInput { SectionKey = "order", FieldKey = "incident_name", Value = input.IncidentName?.Trim() },
				new RecordValueInput { SectionKey = "mobilization", FieldKey = "coordinator", ReferenceId = userId }
			};
			if (!string.IsNullOrWhiteSpace(input.IncidentNumber)) values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "incident_number", ReferenceId = input.IncidentNumber.Trim(), ReferenceType = input.SourceScheme ?? DefaultScheme(profileKey) });
			if (!string.IsNullOrWhiteSpace(input.IncidentCountry))
				values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "incident_subdivision", Value = string.IsNullOrWhiteSpace(input.IncidentSubdivision) ? input.IncidentCountry.Trim().ToUpperInvariant() : input.IncidentCountry.Trim().ToUpperInvariant() + "-" + input.IncidentSubdivision.Trim().ToUpperInvariant() });
			if (!string.IsNullOrWhiteSpace(input.OrderingOffice)) values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "ordering_office", Value = input.OrderingOffice.Trim() });
			if (!string.IsNullOrWhiteSpace(input.RequestingAgency)) values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "requesting_agency", Value = input.RequestingAgency.Trim() });
			if (!string.IsNullOrWhiteSpace(input.SendingAgency)) values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "sending_agency", Value = input.SendingAgency.Trim() });
			if (!string.IsNullOrWhiteSpace(input.AgreementReference)) values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "agreement", ReferenceId = input.AgreementReference.Trim(), ReferenceType = "agreement" });
			if (!string.IsNullOrWhiteSpace(input.CostCode)) values.Add(new RecordValueInput { SectionKey = "order", FieldKey = "cost_code", ReferenceId = input.CostCode.Trim(), ReferenceType = "cost-code" });
			var ordinal = 0;
			foreach (var fill in input.Fills ?? new List<RecordDeploymentFillInput>())
			{
				var rowKey = "fill-" + (fill.RequestNumber ?? ordinal.ToString(CultureInfo.InvariantCulture));
				if (!string.IsNullOrWhiteSpace(fill.AssignedUserId)) values.Add(new RecordValueInput { SectionKey = "roster", FieldKey = "member", RowKey = rowKey, Ordinal = ordinal, ReferenceId = fill.AssignedUserId });
				if (!string.IsNullOrWhiteSpace(fill.Position)) values.Add(new RecordValueInput { SectionKey = "roster", FieldKey = "position", RowKey = rowKey, Ordinal = ordinal, Value = fill.Position.Trim() });
				values.Add(new RecordValueInput { SectionKey = "roster", FieldKey = "trainee", RowKey = rowKey, Ordinal = ordinal, Value = fill.IsTrainee ? "true" : "false" });
				if (!string.IsNullOrWhiteSpace(fill.RequestNumber)) values.Add(new RecordValueInput { SectionKey = "roster", FieldKey = "request_number", RowKey = rowKey, Ordinal = ordinal, ReferenceId = fill.RequestNumber.Trim(), ReferenceType = "request" });
				if (fill.AssignedUnitId.HasValue) values.Add(new RecordValueInput { SectionKey = "roster", FieldKey = "unit", RowKey = rowKey, Ordinal = ordinal, ReferenceId = fill.AssignedUnitId.Value.ToString(CultureInfo.InvariantCulture) });
				ordinal++;
			}
			return values;
		}

		public static string ProfileOptionKey(string profileKey)
		{
			switch ((profileKey ?? string.Empty).ToLowerInvariant())
			{
				case RmsDeploymentProfiles.UsWildland: return "us-wildland";
				case RmsDeploymentProfiles.CaWildland: return "ca-wildland";
				case RmsDeploymentProfiles.CrossBorder: return "us-ca-cross-border";
				case RmsDeploymentProfiles.Compact: return "emac-compact";
				case RmsDeploymentProfiles.LocalMutualAid: return "local-mutual-aid";
				default: return "generic";
			}
		}

		public static string DefaultScheme(string profileKey)
		{
			switch ((profileKey ?? string.Empty).ToLowerInvariant())
			{
				case RmsDeploymentProfiles.UsWildland: return "iroc";
				case RmsDeploymentProfiles.CaWildland: return "ciffc";
				case RmsDeploymentProfiles.CrossBorder: return "iroc-ciffc";
				case RmsDeploymentProfiles.Compact: return "emac";
				default: return "local";
			}
		}

		private static string SafeUrl(string url)
		{
			// A safe URL is a plain https link the coordinator typed; never a capability-bearing download link stored from a source.
			if (string.IsNullOrWhiteSpace(url)) return null;
			return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Query) ? uri.ToString() : null;
		}

		private static RmsExternalOrderFill ToFill(RmsExternalOrder order, RecordDeploymentFillInput input, string userId, DateTime now)
		{
			if (string.IsNullOrWhiteSpace(input?.RequestNumber)) throw new ArgumentException("Every fill needs the external request number it answers.", nameof(input));
			return new RmsExternalOrderFill
			{
				RmsExternalOrderFillId = Guid.NewGuid().ToString(), DepartmentId = order.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RmsExternalOrderId = order.RmsExternalOrderId, RecordId = order.RecordId,
				RequestNumber = input.RequestNumber.Trim(), ParentRequestNumber = input.ParentRequestNumber?.Trim(), RequestCategory = input.RequestCategory?.Trim().ToLowerInvariant() ?? "other", FillNumber = input.FillNumber?.Trim(),
				ResourceKind = input.ResourceKind?.Trim(), ResourceType = input.ResourceType?.Trim(), ResourceTypeScheme = input.ResourceTypeScheme?.Trim() ?? order.SourceScheme, Position = input.Position?.Trim(), PositionScheme = input.PositionScheme?.Trim() ?? order.SourceScheme,
				IsTrainee = input.IsTrainee, HomeUnit = input.HomeUnit?.Trim(), HostAgency = input.HostAgency?.Trim() ?? order.ReceivingAgency, AgencyUnitId = input.AgencyUnitId?.Trim(), PointOfHire = input.PointOfHire?.Trim(),
				CostCode = input.CostCode?.Trim() ?? order.CostCode, AgreementReference = input.AgreementReference?.Trim() ?? order.AgreementReference, AssignedUserId = input.AssignedUserId, AssignedUnitId = input.AssignedUnitId,
				Status = (int)RmsDeploymentFillStatus.Requested, RequestedOn = input.RequestedOn, NeededOn = input.NeededOn, FilledOn = input.FilledOn, CapturedOffsetMinutes = input.CapturedOffsetMinutes ?? order.CapturedOffsetMinutes, Notes = input.Notes,
				CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, ModifiedByUserId = userId, RowVersion = 1
			};
		}

		public async Task<RecordDeploymentAggregate> GetAsync(int departmentId, string userId, string orderId, bool includeArtifact = false)
		{
			var order = await _orders.GetByIdForDepartmentAsync(departmentId, orderId, includeArtifact);
			if (order == null) return null;
			if (!await _authorization.CanUserViewRecordAsync(userId, order.RecordId, departmentId)) throw new UnauthorizedAccessException("Deployment access is not authorized.");
			return await BuildAsync(departmentId, order);
		}

		public async Task<RecordDeploymentAggregate> GetForRecordAsync(int departmentId, string userId, string recordId)
		{
			var order = await _orders.GetForRecordAsync(departmentId, recordId);
			if (order == null) return null;
			if (!await _authorization.CanUserViewRecordAsync(userId, order.RecordId, departmentId)) throw new UnauthorizedAccessException("Deployment access is not authorized.");
			return await BuildAsync(departmentId, order);
		}

		private async Task<RecordDeploymentAggregate> BuildAsync(int departmentId, RmsExternalOrder order)
		{
			return new RecordDeploymentAggregate
			{
				Order = order,
				Fills = (await _fills.GetForOrderAsync(departmentId, order.RmsExternalOrderId))?.OrderBy(f => f.RequestNumber, StringComparer.Ordinal).ToList() ?? new List<RmsExternalOrderFill>(),
				Record = await _records.GetAsync(departmentId, order.RecordId, true),
				Profile = await _packs.GetProfileAsync(ProfileFor(order.ProfileKey)),
				HomeProfile = await _packs.GetProfileAsync(order.HomeProfileKey),
				HostProfile = await _packs.GetProfileAsync(order.HostProfileKey)
			};
		}

		public async Task<List<RmsExternalOrder>> ListAsync(int departmentId, string userId, bool includeClosed)
		{
			var orders = (await _orders.GetForDepartmentAsync(departmentId, includeClosed))?.ToList() ?? new List<RmsExternalOrder>();
			var visible = new List<RmsExternalOrder>();
			foreach (var order in orders)
				if (await _authorization.CanUserViewRecordAsync(userId, order.RecordId, departmentId)) visible.Add(order);
			return visible;
		}

		public async Task<RmsExternalOrderFill> AddFillAsync(int departmentId, string userId, string orderId, RecordDeploymentFillInput input, CancellationToken cancellationToken = default)
		{
			var order = await RequireEditableAsync(departmentId, userId, orderId);
			var fill = ToFill(order, input, userId, DateTime.UtcNow);
			await InTransactionAsync(async () =>
			{
				await _fills.InsertAsync(fill, cancellationToken, true);
				await TouchAsync(order, userId, cancellationToken);
				await AuditAsync(departmentId, userId, order, $"Add fill {fill.RequestNumber}", cancellationToken);
			});
			return fill;
		}

		private static readonly Dictionary<RmsDeploymentFillStatus, RmsDeploymentFillStatus[]> Allowed = new Dictionary<RmsDeploymentFillStatus, RmsDeploymentFillStatus[]>
		{
			[RmsDeploymentFillStatus.Requested] = new[] { RmsDeploymentFillStatus.Accepted, RmsDeploymentFillStatus.Declined },
			[RmsDeploymentFillStatus.Accepted] = new[] { RmsDeploymentFillStatus.Mobilized, RmsDeploymentFillStatus.Declined, RmsDeploymentFillStatus.Released },
			[RmsDeploymentFillStatus.Mobilized] = new[] { RmsDeploymentFillStatus.CheckedIn, RmsDeploymentFillStatus.Released },
			[RmsDeploymentFillStatus.CheckedIn] = new[] { RmsDeploymentFillStatus.Assigned, RmsDeploymentFillStatus.Released },
			[RmsDeploymentFillStatus.Assigned] = new[] { RmsDeploymentFillStatus.Assigned, RmsDeploymentFillStatus.Released },
			[RmsDeploymentFillStatus.Released] = new[] { RmsDeploymentFillStatus.Demobilized, RmsDeploymentFillStatus.Returned },
			[RmsDeploymentFillStatus.Demobilized] = new[] { RmsDeploymentFillStatus.Returned },
			[RmsDeploymentFillStatus.Declined] = new RmsDeploymentFillStatus[0],
			[RmsDeploymentFillStatus.Returned] = new RmsDeploymentFillStatus[0]
		};

		public async Task<RmsExternalOrderFill> TransitionFillAsync(int departmentId, string userId, string fillId, RecordDeploymentFillTransitionInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var fill = await _fills.GetByIdForDepartmentAsync(departmentId, fillId) ?? throw new ArgumentException("Unknown fill.", nameof(fillId));
			var order = await RequireEditableAsync(departmentId, userId, fill.RmsExternalOrderId);
			var from = (RmsDeploymentFillStatus)fill.Status;
			if (!Allowed.TryGetValue(from, out var next) || !next.Contains(input.Status))
				throw new InvalidOperationException($"A fill cannot move from {from} to {input.Status}.");
			if (input.Status == RmsDeploymentFillStatus.Declined && string.IsNullOrWhiteSpace(input.Reason)) throw new ArgumentException("Declining a request needs a reason.", nameof(input));

			var when = input.OccurredOn ?? DateTime.UtcNow;
			fill.Status = (int)input.Status;
			fill.CapturedOffsetMinutes = input.CapturedOffsetMinutes ?? fill.CapturedOffsetMinutes;
			if (!string.IsNullOrWhiteSpace(input.Notes)) fill.Notes = string.IsNullOrWhiteSpace(fill.Notes) ? input.Notes : fill.Notes + "\n" + input.Notes;
			if (!string.IsNullOrWhiteSpace(input.RosterJson)) fill.RosterJson = input.RosterJson;
			if (!string.IsNullOrWhiteSpace(input.TravelJson)) fill.TravelJson = input.TravelJson;
			switch (input.Status)
			{
				case RmsDeploymentFillStatus.Accepted: fill.FilledOn ??= when; break;
				case RmsDeploymentFillStatus.Declined: fill.DeclineReason = input.Reason; break;
				case RmsDeploymentFillStatus.Mobilized: fill.MobilizedOn = when; break;
				case RmsDeploymentFillStatus.CheckedIn: fill.CheckedInOn = when; break;
				case RmsDeploymentFillStatus.Assigned: fill.AssignedOn = when; break;
				case RmsDeploymentFillStatus.Released: fill.ReleasedOn = when; break;
				case RmsDeploymentFillStatus.Demobilized: fill.DemobilizedOn = when; break;
				case RmsDeploymentFillStatus.Returned: fill.ReturnedOn = when; break;
			}
			fill.ModifiedOn = DateTime.UtcNow; fill.ModifiedByUserId = userId; fill.RowVersion += 1;

			await InTransactionAsync(async () =>
			{
				await _fills.UpdateAsync(fill, cancellationToken, true);
				var fills = (await _fills.GetForOrderAsync(departmentId, order.RmsExternalOrderId))?.ToList() ?? new List<RmsExternalOrderFill>();
				var active = fills.Where(f => f.Status != (int)RmsDeploymentFillStatus.Declined).ToList();
				if (active.Any(f => f.Status >= (int)RmsDeploymentFillStatus.Mobilized) && order.Status == (int)RmsExternalOrderStatus.Open) { order.Status = (int)RmsExternalOrderStatus.Mobilized; order.MobilizedOn ??= when; }
				if (active.Count > 0 && active.All(f => f.Status >= (int)RmsDeploymentFillStatus.Released) && order.Status < (int)RmsExternalOrderStatus.Released) { order.Status = (int)RmsExternalOrderStatus.Released; order.ReleasedOn ??= when; }
				await TouchAsync(order, userId, cancellationToken);
				await AuditAsync(departmentId, userId, order, $"Fill {fill.RequestNumber}: {from} -> {input.Status}", cancellationToken);
			});
			return fill;
		}

		public async Task<RmsExternalOrder> RecordSourceSnapshotAsync(int departmentId, string userId, string orderId, string sourceVersion, byte[] artifact, string fileName, string contentType, CancellationToken cancellationToken = default)
		{
			var order = await RequireEditableAsync(departmentId, userId, orderId, true);
			if (artifact == null || artifact.Length == 0) throw new ArgumentException("A snapshot needs the source artifact.", nameof(artifact));
			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				if (order.ArtifactChecksum != null)
				{
					// The previous snapshot stays on record as a versioned reference; a later import never erases what was signed against.
					await _references.InsertAsync(new RmsExternalReference
					{
						RmsExternalReferenceId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = order.RecordId, RecordKind = (int)RmsRecordKind.Operational,
						SourceSubsystem = "external-order", SourceEntityType = "order-snapshot", SourceEntityId = order.OrderNumber, IdentifierScheme = order.SourceScheme, SourceVersion = order.SourceVersion, SemanticRole = "superseded-snapshot",
						CapturedOn = order.SourceCapturedOn ?? order.CreatedOn, CapturedByUserId = order.ModifiedByUserId, Checksum = order.ArtifactChecksum,
						SnapshotJson = JsonConvert.SerializeObject(new { order.ArtifactFileName, order.ArtifactContentType, order.SourceVersion, supersededOn = now, supersededBy = userId }),
						CreatedOn = now, ModifiedOn = now, RowVersion = 1
					}, cancellationToken, true);
				}
				order.ArtifactData = artifact; order.ArtifactFileName = fileName; order.ArtifactContentType = contentType; order.ArtifactChecksum = RecordSnapshotSerializer.Checksum(artifact);
				order.SourceVersion = string.IsNullOrWhiteSpace(sourceVersion) ? (int.TryParse(order.SourceVersion, out var v) ? (v + 1).ToString(CultureInfo.InvariantCulture) : now.ToString("yyyyMMddHHmmss")) : sourceVersion.Trim();
				order.SourceCapturedOn = now;
				await TouchAsync(order, userId, cancellationToken);
				await AuditAsync(departmentId, userId, order, $"Record source snapshot v{order.SourceVersion}", cancellationToken);
			});
			return order;
		}

		public async Task<RmsExternalOrder> CloseoutAsync(int departmentId, string userId, string orderId, long expectedRowVersion, string notes, CancellationToken cancellationToken = default)
		{
			var order = await RequireEditableAsync(departmentId, userId, orderId);
			if (order.RowVersion != expectedRowVersion) throw new RecordConcurrencyException(order.RmsExternalOrderId, expectedRowVersion, order.RowVersion);
			var fills = (await _fills.GetForOrderAsync(departmentId, orderId))?.ToList() ?? new List<RmsExternalOrderFill>();
			var active = fills.Where(f => f.Status != (int)RmsDeploymentFillStatus.Declined).ToList();
			if (active.Count == 0) throw new InvalidOperationException("A deployment with no accepted fill has nothing to close out.");
			var notReturned = active.Where(f => f.Status != (int)RmsDeploymentFillStatus.Returned).Select(f => f.RequestNumber).ToList();
			if (notReturned.Count > 0) throw new InvalidOperationException("Closeout needs every resource back at its home unit; still out: " + string.Join(", ", notReturned) + ". An external release flag does not return a resource.");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				order.Status = (int)RmsExternalOrderStatus.ClosedOut; order.ClosedOutOn = now; order.ClosedOutByUserId = userId; order.CloseoutNotes = notes;
				await TouchAsync(order, userId, cancellationToken);
				await AuditAsync(departmentId, userId, order, "Closeout deployment", cancellationToken);
			});
			return order;
		}

		private async Task<RmsExternalOrder> RequireEditableAsync(int departmentId, string userId, string orderId, bool includeArtifact = false)
		{
			var order = await _orders.GetByIdForDepartmentAsync(departmentId, orderId, includeArtifact) ?? throw new ArgumentException("Unknown deployment.", nameof(orderId));
			if (!await _authorization.CanUserViewRecordAsync(userId, order.RecordId, departmentId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord))
				throw new UnauthorizedAccessException("Editing this deployment is not authorized.");
			if (order.Status == (int)RmsExternalOrderStatus.ClosedOut) throw new InvalidOperationException("The deployment is closed out; amend the Record to change it.");
			return order;
		}

		private async Task TouchAsync(RmsExternalOrder order, string userId, CancellationToken cancellationToken)
		{
			order.ModifiedOn = DateTime.UtcNow; order.ModifiedByUserId = userId; order.RowVersion += 1;
			await _orders.UpdateAsync(order, cancellationToken, true);
		}

		private Task AuditAsync(int departmentId, string userId, RmsExternalOrder order, string purpose, CancellationToken cancellationToken)
			=> _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = order.RecordId, Action = (int)RmsAccessAuditAction.Change, ActorUserId = userId, Purpose = purpose, OriginClient = (int)RmsOriginClient.Web, Successful = true, OccurredOn = DateTime.UtcNow, CorrelationId = order.RmsExternalOrderId }, cancellationToken, true);

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try { await work(); _unitOfWork.CommitChanges(); }
			catch { _unitOfWork.DiscardChanges(); throw; }
		}
	}
}
