using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// v4 mapping for the RMS-3 surfaces: the incident-analysis filing, evidence artifacts, public-records
	/// disclosures and the Records dashboard. Kept apart from <see cref="IncidentReportsApiMapper"/> because these
	/// are separate aggregates with their own lifecycles, not sections of the incident report.
	/// </summary>
	public static class IncidentAnalysisApiMapper
	{
		public static IncidentAnalysisData ToAnalysis(IncidentAnalysisAggregate a, bool canViewRestricted = true)
		{
			var withheld = new List<string>();
			var analysis = a.Analysis;
			var state = (RmsIncidentAnalysisState)analysis.State;

			return new IncidentAnalysisData
			{
				AnalysisId = analysis.RmsIncidentAnalysisId, IncidentReportId = analysis.IncidentReportId, ReportingEntityId = analysis.ReportingEntityId,
				ProfileVersion = analysis.ProfileVersion, State = analysis.State, StateName = state.ToString(), GeneralCause = analysis.GeneralCause,
				InvestigationTypes = SplitCodes(analysis.InvestigationTypesCsv), EstimatedValueTotal = analysis.EstimatedValueTotal,
				EstimatedLossTotal = analysis.EstimatedLossTotal, CurrencyCode = analysis.CurrencyCode, AuthorUserId = analysis.AuthorUserId,
				OwnerUserId = analysis.OwnerUserId, FinalizedOn = analysis.FinalizedOn, CurrentRevisionId = analysis.CurrentRevisionId,
				RevisionCount = analysis.RevisionCount, NerisAnalysisId = analysis.NerisAnalysisId, LastSubmissionState = analysis.LastSubmissionState,
				LastSubmissionStateName = analysis.LastSubmissionState.HasValue ? ((RmsSubmissionState)analysis.LastSubmissionState.Value).ToString() : null,
				LastSubmittedOn = analysis.LastSubmittedOn, AcceptedOn = analysis.AcceptedOn, RejectedOn = analysis.RejectedOn,
				RejectionSummary = analysis.RejectionSummary, VoidedOn = analysis.VoidedOn, VoidReasonCode = analysis.VoidReasonCode,
				CreatedOn = analysis.CreatedOn, ModifiedOn = analysis.ModifiedOn, RowVersion = analysis.RowVersion,
				ETag = RecordsApiContract.ToETag(analysis.RowVersion),
				IsEditable = state == RmsIncidentAnalysisState.Draft || state == RmsIncidentAnalysisState.Rejected,
				IncidentIsFiled = a.IncidentIsFiled,
				Modules = a.Modules.OrderBy(m => m.Ordinal).Select(IncidentReportsApiMapper.ToModule).ToList(),
				Properties = a.Properties.OrderBy(p => p.Ordinal).Select(ToProperty).ToList(),
				Vehicles = a.Vehicles.OrderBy(v => v.Ordinal).Select(v => ToVehicle(v, canViewRestricted, withheld)).ToList(),
				Submissions = a.Submissions.OrderByDescending(s => s.QueuedOn).Select(s => new IncidentSubmissionData
				{
					SubmissionId = s.RmsSubmissionId, RevisionId = s.RevisionId, Destination = s.Destination, DestinationVersion = s.DestinationVersion,
					State = s.State, StateName = ((RmsSubmissionState)s.State).ToString(), ExternalId = s.ExternalId, ExternalStatus = s.ExternalStatus,
					Attempts = s.Attempts, MaxAttempts = s.MaxAttempts, ErrorSummary = s.ErrorSummary, PayloadChecksum = s.PayloadChecksum,
					QueuedOn = s.QueuedOn, SentOn = s.SentOn, CompletedOn = s.CompletedOn, NextAttemptOn = s.NextAttemptOn
				}).ToList(),
				Revisions = a.Revisions.OrderByDescending(r => r.RevisionNumber).Select(RecordsApiMapper.ToRevision).ToList(),
				WithheldFields = withheld
			};
		}

		public static IncidentPropertyData ToProperty(RmsIncidentProperty p)
		{
			return new IncidentPropertyData
			{
				PropertyId = p.RmsIncidentPropertyId, LocationUse = p.LocationUse, ConstructionType = p.ConstructionType, Foundation = p.Foundation,
				ExteriorFinish = p.ExteriorFinish, RoofMaterial = p.RoofMaterial, StoriesAboveGrade = p.StoriesAboveGrade, StoriesBelowGrade = p.StoriesBelowGrade,
				YearBuilt = p.YearBuilt, Vacancy = p.Vacancy, DamageType = p.DamageType, FireSpread = p.FireSpread, EstimatedValue = p.EstimatedValue,
				EstimatedLoss = p.EstimatedLoss, ContentsValue = p.ContentsValue, ContentsLoss = p.ContentsLoss, CurrencyCode = p.CurrencyCode, Ordinal = p.Ordinal
			};
		}

		/// <summary>
		/// The vehicle row stays visible without the restricted grant — that a vehicle burned is part of the
		/// incident — but VIN, plate and registration state identify a person's property, so those three are
		/// dropped and named instead of the row disappearing.
		/// </summary>
		public static IncidentVehicleData ToVehicle(RmsIncidentVehicle v, bool canViewRestricted, List<string> withheld)
		{
			var data = new IncidentVehicleData
			{
				VehicleId = v.RmsIncidentVehicleId, VehicleKind = v.VehicleKind, Make = v.Make, Model = v.Model, ModelYear = v.ModelYear,
				BodyStyle = v.BodyStyle, Powertrain = v.Powertrain, DamageType = v.DamageType, WasOccupied = v.WasOccupied,
				EstimatedValue = v.EstimatedValue, EstimatedLoss = v.EstimatedLoss, CurrencyCode = v.CurrencyCode, Ordinal = v.Ordinal
			};

			if (canViewRestricted)
			{
				data.Vin = v.Vin;
				data.LicensePlate = v.LicensePlate;
				data.LicenseState = v.LicenseState;
			}
			else
			{
				foreach (var field in new[] { "Vin", "LicensePlate", "LicenseState" })
					withheld.Add("Vehicles." + field);
			}

			return data;
		}

		public static IncidentAnalysisDraftInput ToDraftInput(SaveIncidentAnalysisDraftInput input, RmsOriginClient origin)
		{
			return new IncidentAnalysisDraftInput
			{
				GeneralCause = input.GeneralCause,
				InvestigationTypes = input.InvestigationTypes ?? new List<string>(),
				CurrencyCode = input.CurrencyCode,
				// Null stays null: the service reads absence as "leave this section alone".
				Modules = input.Modules?.Select(m => new IncidentModuleInput
				{
					Kind = (RmsIncidentModuleKind)m.Kind, PrimaryCode = m.PrimaryCode, SecondaryCode = m.SecondaryCode, Quantity = m.Quantity,
					QuantityUnit = m.QuantityUnit, OccurredOn = RecordsApiHelper.Utc(m.OccurredOn), DetailJson = m.DetailJson
				}).ToList(),
				Properties = input.Properties?.Select(p => new IncidentPropertyInput
				{
					LocationUse = p.LocationUse, ConstructionType = p.ConstructionType, Foundation = p.Foundation, ExteriorFinish = p.ExteriorFinish,
					RoofMaterial = p.RoofMaterial, StoriesAboveGrade = p.StoriesAboveGrade, StoriesBelowGrade = p.StoriesBelowGrade, YearBuilt = p.YearBuilt,
					Vacancy = p.Vacancy, DamageType = p.DamageType, FireSpread = p.FireSpread, EstimatedValue = p.EstimatedValue, EstimatedLoss = p.EstimatedLoss,
					ContentsValue = p.ContentsValue, ContentsLoss = p.ContentsLoss, DetailJson = p.DetailJson
				}).ToList(),
				Vehicles = input.Vehicles?.Select(v => new IncidentVehicleInput
				{
					VehicleKind = v.VehicleKind, Make = v.Make, Model = v.Model, ModelYear = v.ModelYear, BodyStyle = v.BodyStyle, Powertrain = v.Powertrain,
					DamageType = v.DamageType, Vin = v.Vin, LicensePlate = v.LicensePlate, LicenseState = v.LicenseState, WasOccupied = v.WasOccupied,
					EstimatedValue = v.EstimatedValue, EstimatedLoss = v.EstimatedLoss, DetailJson = v.DetailJson
				}).ToList(),
				OriginClient = origin
			};
		}

		private static List<string> SplitCodes(string csv)
		{
			return string.IsNullOrWhiteSpace(csv)
				? new List<string>()
				: csv.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
		}
	}

	/// <summary>Evidence artifacts (RMS plan section 4.5).</summary>
	public static class RecordEvidenceApiMapper
	{
		public static RecordEvidenceSourceData ToSource(RecordEvidenceSourceState state)
		{
			return new RecordEvidenceSourceData { Kind = (int)state.Kind, KindName = state.Kind.ToString(), Available = state.Available, Reason = state.Reason };
		}

		/// <summary>
		/// <paramref name="includeManifest"/> is only ever true for a single-artifact read. A restricted artifact's
		/// manifest is withheld and flagged rather than omitted silently, so the caller knows there is content they
		/// are not seeing.
		/// </summary>
		public static RecordEvidenceArtifactData ToArtifact(RmsEvidenceArtifact a, bool canViewRestricted, bool includeManifest = false)
		{
			var classification = (RmsEvidenceClassification)a.Classification;
			var mayReadContent = canViewRestricted || classification == RmsEvidenceClassification.Unrestricted;

			return new RecordEvidenceArtifactData
			{
				ArtifactId = a.RmsEvidenceArtifactId, RecordId = a.RecordId, RecordKind = a.RecordKind, RevisionId = a.RevisionId, Kind = a.Kind,
				KindName = ((RmsEvidenceKind)a.Kind).ToString(), Title = a.Title, CaptureReason = a.CaptureReason, SourceSubsystem = a.SourceSubsystem,
				SourceEntityType = a.SourceEntityType, SourceEntityId = a.SourceEntityId, IdentifierScheme = a.IdentifierScheme, SourceVersion = a.SourceVersion,
				CoverageStart = a.CoverageStart, CoverageEnd = a.CoverageEnd, Checksum = a.Checksum, ByteSize = a.ByteSize, SourceItemCount = a.SourceItemCount,
				Classification = a.Classification, ClassificationName = classification.ToString(), RetentionYears = a.RetentionYears,
				CapturedByUserId = a.CapturedByUserId, CapturedOn = a.CapturedOn, OriginClient = a.OriginClient,
				SupersededByArtifactId = a.SupersededByArtifactId, SupersededOn = a.SupersededOn, IsCurrent = a.IsCurrent,
				ManifestJson = includeManifest && mayReadContent ? a.ManifestJson : null,
				ManifestWithheld = includeManifest && !mayReadContent
			};
		}

		public static RecordEvidenceCaptureRequest ToCaptureRequest(CaptureRecordEvidenceInput input, int departmentId, string userId, RmsOriginClient origin)
		{
			return new RecordEvidenceCaptureRequest
			{
				DepartmentId = departmentId,
				RecordId = input.RecordId,
				RecordKind = input.RecordKind.HasValue ? (RmsRecordKind)input.RecordKind.Value : RmsRecordKind.Operational,
				Kind = (RmsEvidenceKind)input.Kind,
				CaptureReason = input.CaptureReason,
				CallId = input.CallId,
				CoverageStart = RecordsApiHelper.Utc(input.CoverageStart),
				CoverageEnd = RecordsApiHelper.Utc(input.CoverageEnd),
				SourceIds = input.SourceIds ?? new List<string>(),
				UnitIds = input.UnitIds ?? new List<int>(),
				UserIds = input.UserIds ?? new List<string>(),
				CapturedByUserId = userId,
				OriginClient = origin
			};
		}
	}

	/// <summary>Public-records requests and productions (RMS plan section 4.7).</summary>
	public static class DisclosuresApiMapper
	{
		public static DisclosureRequestData ToRequest(RmsDisclosureRequest r, bool canViewRestricted)
		{
			var data = new DisclosureRequestData
			{
				RequestId = r.RmsDisclosureRequestId, RequestNumber = r.RequestNumber, ReceivedOn = r.ReceivedOn, StatutoryDueOn = r.StatutoryDueOn,
				IsOverdue = r.IsOverdue, JurisdictionProfile = r.JurisdictionProfile, ScopeNarrative = r.ScopeNarrative, ScopeQueryJson = r.ScopeQueryJson,
				State = r.State, StateName = ((RmsDisclosureState)r.State).ToString(), AssignedToUserId = r.AssignedToUserId, RedactionProfile = r.RedactionProfile,
				ClosedOn = r.ClosedOn, ClosedByUserId = r.ClosedByUserId, DispositionReason = r.DispositionReason, CreatedOn = r.CreatedOn,
				ModifiedOn = r.ModifiedOn, RowVersion = r.RowVersion, ETag = RecordsApiContract.ToETag(r.RowVersion)
			};

			// Who asked is not itself public in most jurisdictions, and it must never ride into a produced packet.
			if (canViewRestricted)
			{
				data.RequesterName = r.RequesterName;
				data.RequesterOrganization = r.RequesterOrganization;
				data.RequesterContact = r.RequesterContact;
			}
			else
			{
				data.WithheldFields.AddRange(new[] { "RequesterName", "RequesterOrganization", "RequesterContact" });
			}

			return data;
		}

		public static DisclosureScopePreviewData ToPreview(RmsDisclosureScopePreview preview)
		{
			return new DisclosureScopePreviewData
			{
				MatchedCount = preview.MatchedCount, ProducibleCount = preview.ProducibleCount, WithheldWholeRecordCount = preview.WithheldWholeRecordCount,
				Truncated = preview.Truncated,
				Items = preview.Items.Select(i => new DisclosureScopeItemData
				{
					RecordId = i.RecordId, RecordNumber = i.RecordNumber, DefinitionKey = i.DefinitionKey, Summary = i.Summary, OccurredOn = i.OccurredOn,
					CurrentRevisionId = i.CurrentRevisionId, Producible = i.Producible, NotProducibleReason = i.NotProducibleReason
				}).ToList()
			};
		}

		/// <summary>The redacted artifact is only carried on a single-production read; a listing would ship the whole release.</summary>
		public static DisclosureProductionData ToProduction(RmsDisclosureProduction p, bool includeArtifact = false)
		{
			return new DisclosureProductionData
			{
				ProductionId = p.RmsDisclosureProductionId, RequestId = p.DisclosureRequestId, ProductionNumber = p.ProductionNumber,
				RedactionProfile = p.RedactionProfile, Checksum = p.Checksum, ByteSize = p.ByteSize, RecordCount = p.RecordCount,
				WithheldFieldCount = p.WithheldFieldCount, PreparedByUserId = p.PreparedByUserId, PreparedOn = p.PreparedOn,
				ReleasedByUserId = p.ReleasedByUserId, ReleasedOn = p.ReleasedOn, IsReleased = p.IsReleased,
				ProducedSetJson = p.ProducedSetJson, WithheldFieldsJson = p.WithheldFieldsJson,
				ArtifactJson = includeArtifact ? p.ArtifactJson : null
			};
		}

		/// <summary>
		/// The scope query as the Records queue runs it. The viewer fields are deliberately not accepted from the
		/// client — the service sets them from the caller, so a scope can never be widened past what its author
		/// may see.
		/// </summary>
		public static RmsRecordQuery ToScopeQuery(DisclosureScopeQueryInput input)
		{
			if (input == null)
				return null;

			return new RmsRecordQuery
			{
				States = input.States != null && input.States.Count > 0 ? input.States.ToList() : null,
				DefinitionKey = string.IsNullOrWhiteSpace(input.DefinitionKey) ? null : input.DefinitionKey,
				Year = input.Year,
				CallId = input.CallId,
				AuthorUserId = string.IsNullOrWhiteSpace(input.AuthorUserId) ? null : input.AuthorUserId,
				OwnerUserId = string.IsNullOrWhiteSpace(input.OwnerUserId) ? null : input.OwnerUserId,
				StationGroupId = input.StationGroupId,
				IncludeLegacy = input.IncludeLegacy,
				Take = Math.Max(1, Math.Min(500, input.Take ?? 200))
			};
		}
	}

	/// <summary>Dashboard counts and crosswalk coverage.</summary>
	public static class RecordsDashboardApiMapper
	{
		public static RecordsDashboardData ToDashboard(RecordsDashboard d)
		{
			return new RecordsDashboardData
			{
				GeneratedOn = d.GeneratedOn, OperationalDrafts = d.OperationalDrafts, OperationalAwaitingReview = d.OperationalAwaitingReview,
				OperationalReturned = d.OperationalReturned, IncidentIncomplete = d.IncidentIncomplete, IncidentAwaitingReview = d.IncidentAwaitingReview,
				IncidentSubmitted = d.IncidentSubmitted, IncidentAccepted = d.IncidentAccepted, IncidentRejected = d.IncidentRejected, Overdue = d.Overdue,
				AnalysesAwaitingFiling = d.AnalysesAwaitingFiling, DisclosuresOpen = d.DisclosuresOpen, DisclosuresOverdue = d.DisclosuresOverdue,
				Warnings = d.Warnings.ToList()
			};
		}

		public static NerisCrosswalkCoverageData ToCoverage(NerisCrosswalkCoverage c)
		{
			return new NerisCrosswalkCoverageData
			{
				ContractVersion = c.ContractVersion, TotalLocalCodes = c.TotalLocalCodes, MappedCount = c.MappedCount, UnmappedCount = c.UnmappedCount,
				StaleMappingCount = c.StaleMappingCount,
				Items = c.Items.Select(i => new NerisCrosswalkCoverageItemData { SetKey = i.SetKey, LocalCode = i.LocalCode, NerisCode = i.NerisCode, Mapped = i.Mapped }).ToList(),
				Warnings = c.Warnings.ToList()
			};
		}
	}
}
