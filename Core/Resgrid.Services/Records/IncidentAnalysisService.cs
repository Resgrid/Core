using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The NERIS incident-analysis filing (RMS-3): the fire/hazmat investigation posted to
	/// <c>/incident_analysis/{neris_id_incident}</c> once the incident itself exists at the destination.
	/// <para>
	/// Deliberately separate from <see cref="IncidentReportsService"/>. The analysis is a second submittable
	/// artifact for the same incident, and keeping it apart is what guarantees the property the plan cares about:
	/// an analysis that will not validate can never block the incident report, and an incident still awaiting its
	/// destination id can never lose the analysis an investigator already finished.
	/// </para>
	/// </summary>
	public class IncidentAnalysisService : IIncidentAnalysisService
	{
		public const string AnalysisAggregate = "RmsIncidentAnalysis";

		private readonly IRmsIncidentAnalysesRepository _analyses;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsIncidentModulesRepository _modules;
		private readonly IRmsIncidentPropertiesRepository _properties;
		private readonly IRmsIncidentVehiclesRepository _vehicles;
		private readonly IRmsValidationIssuesRepository _issues;
		private readonly IRmsSubmissionsRepository _submissions;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IUnitOfWork _unitOfWork;
		private readonly INerisProfileService _neris;
		private readonly INerisMappingService _mapping;
		private readonly INerisValidationService _validation;
		private readonly IRecordsAuthorizationService _authorization;

		public IncidentAnalysisService(IRmsIncidentAnalysesRepository analyses, IRmsIncidentReportsRepository reports,
			IRmsIncidentModulesRepository modules, IRmsIncidentPropertiesRepository properties, IRmsIncidentVehiclesRepository vehicles,
			IRmsValidationIssuesRepository issues, IRmsSubmissionsRepository submissions, IRmsRevisionsRepository revisions,
			IRmsAccessAuditsRepository audits, IUnitOfWork unitOfWork, INerisProfileService neris, INerisMappingService mapping,
			INerisValidationService validation, IRecordsAuthorizationService authorization)
		{
			_analyses = analyses;
			_reports = reports;
			_modules = modules;
			_properties = properties;
			_vehicles = vehicles;
			_issues = issues;
			_submissions = submissions;
			_revisions = revisions;
			_audits = audits;
			_unitOfWork = unitOfWork;
			_neris = neris;
			_mapping = mapping;
			_validation = validation;
			_authorization = authorization;
		}

		public async Task<IncidentAnalysisAggregate> StartForReportAsync(int departmentId, string userId, string incidentReportId, RmsOriginClient origin = RmsOriginClient.Web, CancellationToken cancellationToken = default)
		{
			await RequirePermissionAsync(departmentId, userId, incidentReportId, PermissionTypes.CreateRecord);
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, incidentReportId);
			if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue)
				throw new InvalidOperationException("The incident report does not exist.");

			// One analysis per report; a second start returns the first, exactly like starting a report from a Call.
			var existing = await _analyses.GetForReportAsync(departmentId, incidentReportId);
			if (existing != null)
				return await HydrateAsync(existing, report, null, false);

			var now = DateTime.UtcNow;
			var analysis = new RmsIncidentAnalysis
			{
				RmsIncidentAnalysisId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				IncidentReportId = incidentReportId,
				ReportingEntityId = report.ReportingEntityId,
				ProfileVersion = report.ProfileVersion ?? _neris.ContractVersion,
				State = (int)RmsIncidentAnalysisState.Draft,
				AuthorUserId = userId,
				OwnerUserId = userId,
				CreatedOn = now,
				CreatedByUserId = userId,
				ModifiedOn = now,
				ModifiedByUserId = userId,
				RowVersion = 1
			};

			await InTransactionAsync(async () =>
			{
				await _analyses.InsertAsync(analysis, cancellationToken, true);
				await AuditAsync(departmentId, userId, analysis.RmsIncidentAnalysisId, null, RmsAccessAuditAction.Change, "Start incident analysis", origin, cancellationToken, new { incidentReportId });
			});

			return await HydrateAsync(analysis, report, null, false);
		}

		public async Task<IncidentAnalysisAggregate> GetAsync(int departmentId, string analysisId, bool includeHistory = false)
		{
			var analysis = await _analyses.GetByIdForDepartmentAsync(departmentId, analysisId);
			if (analysis == null || analysis.DeletedOn.HasValue)
				return null;

			var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis.IncidentReportId);
			return report == null || report.PurgedOn.HasValue || report.DeletedOn.HasValue ? null : await HydrateAsync(analysis, report, null, includeHistory);
		}

		public async Task<IncidentAnalysisAggregate> GetForReportAsync(int departmentId, string incidentReportId, bool includeHistory = false)
		{
			var analysis = await _analyses.GetForReportAsync(departmentId, incidentReportId);
			if (analysis == null || analysis.DeletedOn.HasValue)
				return null;

			var report = await _reports.GetByIdForDepartmentAsync(departmentId, incidentReportId);
			return report == null || report.PurgedOn.HasValue || report.DeletedOn.HasValue ? null : await HydrateAsync(analysis, report, null, includeHistory);
		}

		public async Task<IncidentAnalysisAggregate> SaveDraftAsync(int departmentId, string userId, string analysisId, long expectedRowVersion, IncidentAnalysisDraftInput input, bool canWriteRestricted = false, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));

			var analysis = await LoadAsync(departmentId, analysisId);
			await RequirePermissionAsync(departmentId, userId, analysis.IncidentReportId, PermissionTypes.CreateRecord);
			canWriteRestricted = canWriteRestricted && await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			if ((RmsIncidentAnalysisState)analysis.State != RmsIncidentAnalysisState.Draft && (RmsIncidentAnalysisState)analysis.State != RmsIncidentAnalysisState.Rejected)
				throw new InvalidOperationException("Only a draft or rejected analysis can be edited.");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				if (analysis.RowVersion != expectedRowVersion || !await _analyses.TryBumpRowVersionAsync(departmentId, analysisId, expectedRowVersion, cancellationToken))
					throw new RecordConcurrencyException(analysisId, expectedRowVersion, analysis.RowVersion);

				analysis.GeneralCause = Trim(input.GeneralCause)?.ToUpperInvariant();
				analysis.InvestigationTypesCsv = JoinCodes(input.InvestigationTypes);
				analysis.CurrencyCode = Trim(input.CurrencyCode)?.ToUpperInvariant();

				var properties = await ReplacePropertiesAsync(analysis, input.Properties, now, cancellationToken);
				var vehicles = await ReplaceVehiclesAsync(analysis, input.Vehicles, canWriteRestricted, now, cancellationToken);
				await ReplaceModulesAsync(analysis, input.Modules, now, cancellationToken);

				// The analysis's headline totals are the sum of what it enumerates; a department never types them.
				analysis.EstimatedValueTotal = Sum(properties.Select(p => PropertyTotal(p, p.EstimatedValue, "estimated_property_value")), properties.Select(p => PropertyTotal(p, p.ContentsValue, "estimated_contents_value")), vehicles.Select(v => v.EstimatedValue));
				analysis.EstimatedLossTotal = Sum(properties.Select(p => PropertyTotal(p, p.EstimatedLoss, "estimated_property_loss_value")), properties.Select(p => PropertyTotal(p, p.ContentsLoss, "estimated_contents_loss_value")), vehicles.Select(v => v.EstimatedLoss));

				analysis.ModifiedOn = now;
				analysis.ModifiedByUserId = userId;
				analysis.RowVersion += 1;
				await _analyses.UpdateAsync(analysis, cancellationToken, true);
				await AuditAsync(departmentId, userId, analysisId, null, RmsAccessAuditAction.Change, "Save analysis draft", input.OriginClient, cancellationToken);
			});

			return await GetAsync(departmentId, analysisId, false);
		}

		public async Task<List<RmsValidationIssue>> ValidateAsync(int departmentId, string analysisId, CancellationToken cancellationToken = default)
		{
			var snapshot = await BuildSnapshotAsync(departmentId, analysisId);
			if (snapshot == null)
				return new List<RmsValidationIssue>();

			var profile = await _neris.GetProfileAsync(departmentId);
			var issues = _validation.ValidateAnalysisLocal(snapshot, profile);
			await _issues.ReplaceForRecordAsync(departmentId, analysisId, RmsValidationSource.Local, issues, cancellationToken);
			return (await _issues.GetForRecordAsync(departmentId, analysisId))?.ToList() ?? new List<RmsValidationIssue>();
		}

		public async Task<IncidentAnalysisAggregate> FinalizeAsync(int departmentId, string userId, string analysisId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			var analysis = await LoadAsync(departmentId, analysisId);
			await RequirePermissionAsync(departmentId, userId, analysis.IncidentReportId, PermissionTypes.FinalizeRecords);
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis.IncidentReportId);

			var issues = await ValidateAsync(departmentId, analysisId, cancellationToken);
			// The one rule that is not the destination's: an analysis cannot be filed before its incident. That is
			// a wait, not an error, so it is dropped from the blocking set here and enforced at submission instead.
			var blocking = issues.Where(i => i.Severity == (int)RmsValidationSeverity.Error && i.RuleKey != "neris.analysis.incident").ToList();
			if (blocking.Count > 0)
				throw new IncidentReportValidationException(analysisId, blocking);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();

			await InTransactionAsync(async () =>
			{
				if (analysis.RowVersion != expectedRowVersion || !await _analyses.TryBumpRowVersionAsync(departmentId, analysisId, expectedRowVersion, cancellationToken))
					throw new RecordConcurrencyException(analysisId, expectedRowVersion, analysis.RowVersion);

				var aggregate = await HydrateAsync(analysis, report, null, false);
				var revision = await WriteRevisionAsync(analysis, aggregate, userId, now, cancellationToken);

				analysis.State = (int)RmsIncidentAnalysisState.Finalized;
				analysis.CurrentRevisionId = revision.RmsRevisionId;
				analysis.RevisionCount = revision.RevisionNumber;
				analysis.FinalizedOn = now;
				analysis.FinalizedByUserId = userId;
				analysis.ModifiedOn = now;
				analysis.ModifiedByUserId = userId;
				analysis.RowVersion += 1;
				await _analyses.UpdateAsync(analysis, cancellationToken, true);

				// Queue immediately when the incident is already filed; otherwise worker 41 picks it up when it is.
				if (report != null && !string.IsNullOrWhiteSpace(report.NerisIncidentId) && await _neris.IsSubmissionEnabledAsync(departmentId))
					await QueueCoreAsync(analysis, report, revision, userId, now, cancellationToken);
				await _analyses.UpdateAsync(analysis, cancellationToken, true);

				await AuditAsync(departmentId, userId, analysisId, revision.RmsRevisionId, RmsAccessAuditAction.Change, "Finalize incident analysis", RmsOriginClient.Web, cancellationToken, new { revision.Checksum });
			});

			await Task.CompletedTask;
			return await GetAsync(departmentId, analysisId, true);
		}

		public async Task<IncidentAnalysisAggregate> QueueSubmissionAsync(int departmentId, string userId, string analysisId, CancellationToken cancellationToken = default)
		{
			var analysis = await LoadAsync(departmentId, analysisId);
			await RequirePermissionAsync(departmentId, userId, analysis.IncidentReportId, PermissionTypes.SubmitRecords);
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis.IncidentReportId);

			if (string.IsNullOrWhiteSpace(analysis.CurrentRevisionId))
				throw new InvalidOperationException("The analysis has not been finalized.");
			if (report == null || string.IsNullOrWhiteSpace(report.NerisIncidentId))
				throw new InvalidOperationException("The incident must be filed with NERIS before its analysis can be.");
			if (!await _neris.IsSubmissionEnabledAsync(departmentId))
				throw new InvalidOperationException("NERIS submission is not enabled for this department.");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, analysis.CurrentRevisionId)
					?? throw new InvalidOperationException("The finalized revision this analysis points at is missing; it cannot be filed.");
				if (!await _analyses.TryBumpRowVersionAsync(departmentId, analysisId, analysis.RowVersion, cancellationToken))
					throw new RecordConcurrencyException(analysisId, analysis.RowVersion, analysis.RowVersion + 1);
				await QueueCoreAsync(analysis, report, revision, userId, now, cancellationToken);
				analysis.ModifiedOn = now;
				analysis.ModifiedByUserId = userId;
				analysis.RowVersion += 1;
				await _analyses.UpdateAsync(analysis, cancellationToken, true);
			});

			return await GetAsync(departmentId, analysisId, true);
		}

		public async Task<int> QueueAwaitingIncidentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			if (!await _neris.IsSubmissionEnabledAsync(departmentId))
				return 0;

			var queued = 0;
			foreach (var analysis in (await _analyses.GetAwaitingIncidentAsync(departmentId, 100))?.ToList() ?? new List<RmsIncidentAnalysis>())
			{
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis.IncidentReportId);
					if (report == null || string.IsNullOrWhiteSpace(report.NerisIncidentId))
						continue;

					// Already queued or in flight under this revision: leave it to the submission worker.
					var key = IdempotencyKey(departmentId, analysis.RmsIncidentAnalysisId, analysis.CurrentRevisionId);
					var existing = await _submissions.GetByIdempotencyKeyAsync(key);
					if (existing != null && existing.State != (int)RmsSubmissionState.Failed && existing.State != (int)RmsSubmissionState.Superseded)
						continue;

					await QueueSubmissionAsync(departmentId, null, analysis.RmsIncidentAnalysisId, cancellationToken);
					queued++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Incident analysis {analysis.RmsIncidentAnalysisId} could not be queued.");
				}
			}

			return queued;
		}

		public async Task<IncidentAnalysisAggregate> VoidAsync(int departmentId, string userId, string analysisId, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reasonCode))
				throw new ArgumentException("A void reason is required.", nameof(reasonCode));

			var analysis = await LoadAsync(departmentId, analysisId);
			await RequirePermissionAsync(departmentId, userId, analysis.IncidentReportId, PermissionTypes.DeleteRecord);
			if ((RmsIncidentAnalysisState)analysis.State == RmsIncidentAnalysisState.Submitted)
				throw new InvalidOperationException("The analysis is in flight to the destination and cannot be voided until it settles.");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				analysis.State = (int)RmsIncidentAnalysisState.Voided;
				if (!await _analyses.TryBumpRowVersionAsync(departmentId, analysisId, analysis.RowVersion, cancellationToken))
					throw new RecordConcurrencyException(analysisId, analysis.RowVersion, analysis.RowVersion + 1);
				analysis.VoidedOn = now;
				analysis.VoidedByUserId = userId;
				analysis.VoidReasonCode = reasonCode;
				analysis.VoidReasonText = reasonText;
				analysis.ModifiedOn = now;
				analysis.ModifiedByUserId = userId;
				analysis.RowVersion += 1;
				await _analyses.UpdateAsync(analysis, cancellationToken, true);
				await _submissions.SupersedeOpenForRecordAsync(departmentId, analysisId, null, now, cancellationToken);
				await AuditAsync(departmentId, userId, analysisId, null, RmsAccessAuditAction.Change, "Void incident analysis", RmsOriginClient.Web, cancellationToken, new { reasonCode });
			});

			return await GetAsync(departmentId, analysisId, true);
		}

		public async Task<NerisIncidentAnalysisSnapshot> BuildSnapshotAsync(int departmentId, string analysisId, string revisionId = null)
		{
			var analysis = await _analyses.GetByIdForDepartmentAsync(departmentId, analysisId);
			if (analysis == null || analysis.DeletedOn.HasValue)
				return null;

			var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis.IncidentReportId);
			if (report == null || report.PurgedOn.HasValue || report.DeletedOn.HasValue) return null;
			if (revisionId != null)
			{
				var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, revisionId);
				if (revision == null || revision.RecordId != analysisId || revision.RecordKind != (int)RmsRecordKind.IncidentAnalysis) return null;
				if (RecordSnapshotSerializer.Checksum(revision.SnapshotJson) != revision.Checksum) throw new InvalidOperationException("The analysis revision checksum does not match.");
				var frozen = JsonConvert.DeserializeObject<NerisIncidentAnalysisSnapshot>(revision.SnapshotJson);
				if (frozen?.Analysis == null || frozen.Report == null) throw new InvalidOperationException("This legacy analysis revision did not capture its headers. Finalize a corrected revision before submitting it.");
				if (frozen.Analysis.RmsIncidentAnalysisId != analysisId || frozen.Analysis.DepartmentId != departmentId || frozen.Report.RmsIncidentReportId != analysis.IncidentReportId || frozen.Report.DepartmentId != departmentId) throw new InvalidOperationException("The analysis revision does not belong to this incident.");
				return frozen;
			}
			var aggregate = await HydrateAsync(analysis, report, revisionId, false);
			return ToSnapshot(aggregate);
		}

		public static NerisIncidentAnalysisSnapshot ToSnapshot(IncidentAnalysisAggregate aggregate)
		{
			return new NerisIncidentAnalysisSnapshot
			{
				Analysis = aggregate.Analysis,
				Report = aggregate.Report,
				Modules = aggregate.Modules,
				Properties = aggregate.Properties,
				Vehicles = aggregate.Vehicles
			};
		}

		public static string IdempotencyKey(int departmentId, string analysisId, string revisionId)
		{
			return "neris-analysis:" + RecordSnapshotSerializer.Checksum($"{departmentId}:{analysisId}:{revisionId}");
		}

		// ── internals ────────────────────────────────────────────────────────────────

		private async Task<RmsIncidentAnalysis> LoadAsync(int departmentId, string analysisId)
		{
			var analysis = await _analyses.GetByIdForDepartmentAsync(departmentId, analysisId);
			if (analysis == null || analysis.DeletedOn.HasValue)
				throw new InvalidOperationException("The incident analysis does not exist.");
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis.IncidentReportId);
			if (report == null || report.PurgedOn.HasValue || report.DeletedOn.HasValue) throw new InvalidOperationException("The parent incident report is no longer available.");
			return JsonConvert.DeserializeObject<RmsIncidentAnalysis>(JsonConvert.SerializeObject(analysis));
		}

		private async Task<IncidentAnalysisAggregate> HydrateAsync(RmsIncidentAnalysis analysis, RmsIncidentReport report, string revisionId, bool includeHistory)
		{
			var dept = analysis.DepartmentId;
			var id = analysis.RmsIncidentAnalysisId;
			var aggregate = new IncidentAnalysisAggregate
			{
				Analysis = analysis,
				Report = report,
				Modules = (await _modules.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsIncidentModule>(),
				Properties = (await _properties.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsIncidentProperty>(),
				Vehicles = (await _vehicles.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsIncidentVehicle>()
			};

			if (includeHistory)
			{
				aggregate.Submissions = (await _submissions.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsSubmission>();
				aggregate.Revisions = (await _revisions.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsRevision>();
			}

			return aggregate;
		}

		private async Task<List<RmsIncidentProperty>> ReplacePropertiesAsync(RmsIncidentAnalysis analysis, List<IncidentPropertyInput> inputs, DateTime now, CancellationToken cancellationToken)
		{
			if (inputs == null)
				return (await _properties.GetForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, null))?.ToList() ?? new List<RmsIncidentProperty>();

			await _properties.DeleteDraftForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, cancellationToken);
			var result = new List<RmsIncidentProperty>();
			var ordinal = 0;
			foreach (var input in inputs)
			{
				var row = new RmsIncidentProperty
				{
					RmsIncidentPropertyId = Guid.NewGuid().ToString(), DepartmentId = analysis.DepartmentId, ProtectionId = Guid.NewGuid().ToString(),
					RecordId = analysis.RmsIncidentAnalysisId,
					LocationUse = Trim(input.LocationUse)?.ToUpperInvariant(), ConstructionType = Trim(input.ConstructionType)?.ToUpperInvariant(),
					Foundation = Trim(input.Foundation)?.ToUpperInvariant(), ExteriorFinish = Trim(input.ExteriorFinish)?.ToUpperInvariant(),
					RoofMaterial = Trim(input.RoofMaterial)?.ToUpperInvariant(), StoriesAboveGrade = input.StoriesAboveGrade, StoriesBelowGrade = input.StoriesBelowGrade,
					YearBuilt = input.YearBuilt, Vacancy = Trim(input.Vacancy)?.ToUpperInvariant(), DamageType = Trim(input.DamageType)?.ToUpperInvariant(),
					FireSpread = Trim(input.FireSpread)?.ToUpperInvariant(), EstimatedValue = input.EstimatedValue, EstimatedLoss = input.EstimatedLoss,
					ContentsValue = input.ContentsValue, ContentsLoss = input.ContentsLoss, CurrencyCode = analysis.CurrencyCode,
					DetailJson = Trim(input.DetailJson), Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				await _properties.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			return result;
		}

		private async Task<List<RmsIncidentVehicle>> ReplaceVehiclesAsync(RmsIncidentAnalysis analysis, List<IncidentVehicleInput> inputs, bool canWriteRestricted, DateTime now, CancellationToken cancellationToken)
		{
			var existing = (await _vehicles.GetForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, null))?.ToList() ?? new List<RmsIncidentVehicle>();
			if (inputs == null)
				return existing;

			var byId = existing.ToDictionary(v => v.RmsIncidentVehicleId, StringComparer.Ordinal);
			var suppliedIds = inputs.Where(v => !string.IsNullOrWhiteSpace(v.VehicleId)).Select(v => v.VehicleId).ToList();
			if (suppliedIds.Distinct(StringComparer.Ordinal).Count() != suppliedIds.Count || suppliedIds.Any(id => !byId.ContainsKey(id)))
				throw new ArgumentException("A vehicle row does not belong to this draft or was supplied more than once.");
			if (!canWriteRestricted && existing.Any(v => !suppliedIds.Contains(v.RmsIncidentVehicleId)))
				throw new UnauthorizedAccessException("Existing vehicles must be retained by their row identifiers when restricted fields are hidden.");
			await _vehicles.DeleteDraftForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, cancellationToken);
			var result = new List<RmsIncidentVehicle>();
			var ordinal = 0;
			foreach (var input in inputs)
			{
				var prior = !string.IsNullOrWhiteSpace(input.VehicleId) ? byId[input.VehicleId] : null;
				var row = new RmsIncidentVehicle
				{
					RmsIncidentVehicleId = prior?.RmsIncidentVehicleId ?? Guid.NewGuid().ToString(), DepartmentId = analysis.DepartmentId, ProtectionId = prior?.ProtectionId ?? Guid.NewGuid().ToString(),
					RecordId = analysis.RmsIncidentAnalysisId,
					VehicleKind = Trim(input.VehicleKind)?.ToUpperInvariant() ?? "AUTOMOBILE",
					Make = Trim(input.Make)?.ToUpperInvariant(), Model = Trim(input.Model), ModelYear = input.ModelYear,
					BodyStyle = Trim(input.BodyStyle)?.ToUpperInvariant(), Powertrain = Trim(input.Powertrain)?.ToUpperInvariant(),
					DamageType = Trim(input.DamageType)?.ToUpperInvariant(), WasOccupied = input.WasOccupied,
					EstimatedValue = input.EstimatedValue, EstimatedLoss = input.EstimatedLoss, CurrencyCode = analysis.CurrencyCode,
					DetailJson = canWriteRestricted ? Trim(input.DetailJson) : prior?.DetailJson, Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};

				// VIN, plate and registration state identify a person's vehicle: restricted, so a caller without the
				// grant carries the stored values forward instead of writing or erasing them.
				row.Vin = canWriteRestricted ? Trim(input.Vin)?.ToUpperInvariant() : prior?.Vin;
				row.LicensePlate = canWriteRestricted ? Trim(input.LicensePlate)?.ToUpperInvariant() : prior?.LicensePlate;
				row.LicenseState = canWriteRestricted ? Trim(input.LicenseState)?.ToUpperInvariant() : prior?.LicenseState;

				await _vehicles.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			return result;
		}

		private async Task<List<RmsIncidentModule>> ReplaceModulesAsync(RmsIncidentAnalysis analysis, List<IncidentModuleInput> inputs, DateTime now, CancellationToken cancellationToken)
		{
			if (inputs == null)
				return (await _modules.GetForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, null))?.ToList() ?? new List<RmsIncidentModule>();

			await _modules.DeleteDraftForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, cancellationToken);
			var result = new List<RmsIncidentModule>();
			var ordinal = 0;
			foreach (var input in inputs)
			{
				var descriptor = RmsIncidentModuleCatalog.Get(input.Kind);
				// An incident-payload section on the analysis could never be submitted; dropping it is honest.
				if (descriptor == null || !descriptor.BelongsToAnalysis)
					continue;

				var row = new RmsIncidentModule
				{
					RmsIncidentModuleId = Guid.NewGuid().ToString(), DepartmentId = analysis.DepartmentId, ProtectionId = Guid.NewGuid().ToString(),
					RecordId = analysis.RmsIncidentAnalysisId, RecordKind = (int)RmsRecordKind.IncidentAnalysis,
					ModuleKind = (int)input.Kind, SchemaName = descriptor.SchemaName, ProfileVersion = analysis.ProfileVersion,
					PrimaryCode = Trim(input.PrimaryCode)?.ToUpperInvariant(), SecondaryCode = Trim(input.SecondaryCode)?.ToUpperInvariant(),
					Quantity = input.Quantity, QuantityUnit = Trim(input.QuantityUnit)?.ToUpperInvariant(), OccurredOn = input.OccurredOn,
					DetailJson = Trim(input.DetailJson), Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				await _modules.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			return result;
		}

		private async Task<RmsRevision> WriteRevisionAsync(RmsIncidentAnalysis analysis, IncidentAnalysisAggregate draft, string userId, DateTime now, CancellationToken cancellationToken)
		{
			var snapshotJson = JsonConvert.SerializeObject(new
			{
				SnapshotVersion = 2,
				Analysis = analysis,
				Report = draft.Report,
				analysis.RmsIncidentAnalysisId,
				analysis.IncidentReportId,
				analysis.GeneralCause,
				analysis.InvestigationTypesCsv,
				analysis.EstimatedValueTotal,
				analysis.EstimatedLossTotal,
				Modules = draft.Modules.OrderBy(m => m.Ordinal),
				Properties = draft.Properties.OrderBy(p => p.Ordinal),
				Vehicles = draft.Vehicles.OrderBy(v => v.Ordinal)
			});

			var revision = new RmsRevision
			{
				RmsRevisionId = Guid.NewGuid().ToString(),
				DepartmentId = analysis.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = analysis.RmsIncidentAnalysisId,
				RecordKind = (int)RmsRecordKind.IncidentAnalysis,
				RevisionNumber = analysis.RevisionCount + 1,
				Transition = analysis.RevisionCount == 0 ? (int)RmsRevisionTransition.Finalized : (int)RmsRevisionTransition.Amended,
				PriorRevisionId = analysis.CurrentRevisionId,
				DefinitionKey = RmsDefinitionKeys.NerisIncidentReport,
				DefinitionVersion = 1,
				SnapshotJson = snapshotJson,
				Checksum = RecordSnapshotSerializer.Checksum(snapshotJson),
				ActorUserId = userId,
				OriginClient = (int)RmsOriginClient.Web,
				CreatedOn = now
			};
			await _revisions.InsertAsync(revision, cancellationToken, true);

			var id = revision.RmsRevisionId;
			foreach (var m in draft.Modules) await _modules.InsertAsync(CopyTo(m, x => x.RmsIncidentModuleId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var p in draft.Properties) await _properties.InsertAsync(CopyTo(p, x => x.RmsIncidentPropertyId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var v in draft.Vehicles) await _vehicles.InsertAsync(CopyTo(v, x => x.RmsIncidentVehicleId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);

			return revision;
		}

		private async Task QueueCoreAsync(RmsIncidentAnalysis analysis, RmsIncidentReport report, RmsRevision revision, string userId, DateTime now, CancellationToken cancellationToken)
		{
			var priorSubmissions = (await _submissions.GetForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId))?.ToList() ?? new List<RmsSubmission>();
			RecordsSubmissionService.RequireResolvedCreates(priorSubmissions);
			var profile = await _neris.GetProfileAsync(analysis.DepartmentId);
			var snapshot = await BuildSnapshotAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, revision.RmsRevisionId) ?? throw new InvalidOperationException("The analysis revision is unavailable.");
			// The parent receipt may arrive after local attestation. It is destination identity,
			// not authored incident content; all incident numbers and analysis facts stay frozen.
			if (string.IsNullOrWhiteSpace(snapshot.Report.NerisIncidentId)) snapshot.Report.NerisIncidentId = report.NerisIncidentId;
			var payload = _mapping.BuildIncidentAnalysisPayloadJson(snapshot, profile);
			var key = IdempotencyKey(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, revision.RmsRevisionId);

			var submission = await _submissions.GetByIdempotencyKeyAsync(key);
			if (submission?.RequiresReconciliation == true)
				throw new InvalidOperationException("Reconcile the prior destination delivery before retrying this revision.");
			var destination = _neris.GetDestinationIdentity(profile);
			var externalId = RecordsSubmissionService.ResolveDestinationId(priorSubmissions, destination, analysis.NerisAnalysisId);
			if (submission != null && submission.DestinationIdentity != destination)
				throw new InvalidOperationException("This analysis revision was queued for another destination.");
			if (submission != null && submission.State != (int)RmsSubmissionState.Failed && submission.State != (int)RmsSubmissionState.Superseded && submission.State != (int)RmsSubmissionState.Rejected)
				throw new InvalidOperationException("This analysis revision is already queued or delivered.");
			if (submission == null)
			{
				submission = new RmsSubmission
				{
					RmsSubmissionId = Guid.NewGuid().ToString(),
					DepartmentId = analysis.DepartmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					RecordId = analysis.RmsIncidentAnalysisId,
					RecordKind = (int)RmsRecordKind.IncidentAnalysis,
					RevisionId = revision.RmsRevisionId,
					Destination = RmsSubmissionDestinations.NerisIncidentAnalysis,
					DestinationVersion = profile?.ContractVersion ?? _neris.ContractVersion,
					DestinationIdentity = destination,
					ExternalId = externalId,
					IdempotencyKey = key,
					MaxAttempts = Math.Max(1, Config.NerisConfig.MaxAttempts),
					PayloadJson = payload,
					PayloadChecksum = RecordSnapshotSerializer.Checksum(payload),
					QueuedOn = now,
					CreatedByUserId = userId,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				};
				await _submissions.SupersedeOpenForRecordAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, submission.RmsSubmissionId, now, cancellationToken);
				await _submissions.InsertAsync(submission, cancellationToken, true);
			}
			else
			{
				// Same revision, same key: a retry, never a new payload.
				submission.State = (int)RmsSubmissionState.Queued;
				submission.MaxAttempts = submission.Attempts + Math.Max(1, Config.NerisConfig.MaxAttempts);
				submission.NextAttemptOn = null;
				submission.LeaseOwner = null;
				submission.LeaseExpiresOn = null;
				submission.ErrorSummary = null;
				submission.QueuedOn = now;
				submission.ModifiedOn = now;
				submission.RowVersion += 1;
				await _submissions.UpdateAsync(submission, cancellationToken, true);
			}

			analysis.State = (int)RmsIncidentAnalysisState.Submitted;
			analysis.LastSubmissionId = submission.RmsSubmissionId;
			analysis.LastSubmissionState = submission.State;
			analysis.LastSubmittedOn = now;

			await AuditAsync(analysis.DepartmentId, userId, analysis.RmsIncidentAnalysisId, revision.RmsRevisionId, RmsAccessAuditAction.Submit,
				"Queue analysis submission", RmsOriginClient.System, cancellationToken, new { submission.RmsSubmissionId, submission.IdempotencyKey, submission.PayloadChecksum });
		}

		private static T CopyTo<T>(T source, Action<T> assignId, string revisionId, DateTime now) where T : class
		{
			var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source));
			assignId(copy);
			var type = typeof(T);
			type.GetProperty("RevisionId")?.SetValue(copy, revisionId);
			type.GetProperty("CreatedOn")?.SetValue(copy, now);
			type.GetProperty("ModifiedOn")?.SetValue(copy, now);
			type.GetProperty("RowVersion")?.SetValue(copy, 1L);
			return copy;
		}

		private async Task RequirePermissionAsync(int departmentId, string userId, string reportId, PermissionTypes permission)
		{
			if (!await _authorization.HasPermissionAsync(userId, departmentId, permission)
				|| !await _authorization.CanUserViewRecordAsync(userId, reportId, departmentId)) throw new UnauthorizedAccessException("Incident analysis access is not authorized.");
		}

		private static decimal? PropertyTotal(RmsIncidentProperty property, decimal? firstValue, string key)
		{
			if (string.IsNullOrWhiteSpace(property.DetailJson)) return firstValue;
			try
			{
				var structures = (Newtonsoft.Json.Linq.JObject.Parse(property.DetailJson)["structures"] as Newtonsoft.Json.Linq.JArray)?.OfType<Newtonsoft.Json.Linq.JObject>().ToList();
				if (structures == null || structures.Count == 0) return firstValue;
				return Sum(new[] { firstValue ?? (decimal?)structures[0][key] }, structures.Skip(1).Select(s => (decimal?)s[key]));
			}
			catch (Newtonsoft.Json.JsonException) { throw new ArgumentException("The property fields could not be read."); }
		}

		private static decimal? Sum(params IEnumerable<decimal?>[] sets)
		{
			decimal? total = null;
			foreach (var set in sets)
			{
				foreach (var value in set)
				{
					if (!value.HasValue)
						continue;
					total = (total ?? 0m) + value.Value;
				}
			}
			return total;
		}

		private static string JoinCodes(List<string> codes)
		{
			if (codes == null || codes.Count == 0)
				return null;

			var cleaned = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
			return cleaned.Count == 0 ? null : string.Join(",", cleaned);
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

		private Task AuditAsync(int departmentId, string userId, string recordId, string revisionId, RmsAccessAuditAction action, string purpose, RmsOriginClient origin, CancellationToken cancellationToken, object detail = null)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				RevisionId = revisionId,
				Action = (int)action,
				ActorUserId = userId,
				Purpose = purpose,
				OriginClient = (int)origin,
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}
	}
}
