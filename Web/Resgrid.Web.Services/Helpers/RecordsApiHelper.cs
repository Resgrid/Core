using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Resgrid.Model;
using Resgrid.Services.Records;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>Shared plumbing for the v4 Records contract: origin/field-flag resolution, ETag headers, UTC normalization.</summary>
	public static class RecordsApiHelper
	{
		public static RmsOriginClient ResolveOrigin(int? value)
		{
			if (!value.HasValue || !Enum.IsDefined(typeof(RmsOriginClient), value.Value))
				return RmsOriginClient.Api;
			var origin = (RmsOriginClient)value.Value;
			// Web and System are not API origins; a caller claiming either is recorded as Api.
			return origin == RmsOriginClient.Web || origin == RmsOriginClient.System ? RmsOriginClient.Api : origin;
		}

		/// <summary>The Records.Field.* flag a field client must have on to create or edit; null for non-field origins.</summary>
		public static string FieldFlagFor(RmsOriginClient origin)
		{
			switch (origin)
			{
				case RmsOriginClient.Responder: return FeatureFlagKeys.RecordsFieldResponder;
				case RmsOriginClient.Unit: return FeatureFlagKeys.RecordsFieldUnit;
				case RmsOriginClient.IncidentCommand: return FeatureFlagKeys.RecordsFieldIncidentCommand;
				case RmsOriginClient.Dispatch: return FeatureFlagKeys.RecordsFieldDispatch;
				default: return null;
			}
		}

		/// <summary>Body RowVersion wins; otherwise If-Match; null when neither is usable.</summary>
		public static long? ResolveRowVersion(long? bodyValue, HttpRequest request)
		{
			if (bodyValue.HasValue && bodyValue.Value >= 0)
				return bodyValue.Value;
			if (request == null)
				return null;
			return RecordsApiContract.ParseETag(request.Headers[HeaderNames.IfMatch].ToString());
		}

		public static string ResolveIdempotencyKey(string bodyValue, HttpRequest request)
		{
			if (!string.IsNullOrWhiteSpace(bodyValue))
				return bodyValue.Trim();
			var header = request?.Headers[RecordsApiContract.IdempotencyKeyHeader].ToString();
			return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
		}

		public static void SetETag(HttpResponse response, long rowVersion)
		{
			if (response != null)
				response.Headers[HeaderNames.ETag] = RecordsApiContract.ToETag(rowVersion);
		}

		/// <summary>Clients send UTC; a local-kind value is converted, an unspecified one is taken as UTC.</summary>
		public static DateTime? Utc(DateTime? value)
		{
			if (!value.HasValue || value.Value == DateTime.MinValue)
				return null;
			var v = value.Value;
			return v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc);
		}

		public static long ToUnixMs(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

		public static DateTime? FromUnixMs(long since) => since <= 0 ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime;
	}

	/// <summary>Entity to DTO mapping for the v4 Records controller. Restricted detail fields are withheld here, never downstream.</summary>
	public static class RecordsApiMapper
	{
		public static RecordSummaryData ToSummary(RmsRecordSearchProjection p)
		{
			var state = (RmsRecordState)p.State;
			return new RecordSummaryData
			{
				RecordId = p.RmsRecordSearchProjectionId,
				RecordKind = ((RmsRecordKind)p.RecordKind).ToString(),
				RecordNumber = p.RecordNumber,
				DraftReference = p.DraftReference,
				DefinitionKey = p.DefinitionKey,
				DefinitionVersion = p.DefinitionVersion,
				RecordType = p.RecordType,
				State = p.State,
				StateName = state.ToString(),
				OccurredOn = p.OccurredOn,
				CreatedOn = p.RecordCreatedOn,
				FinalizedOn = p.FinalizedOn,
				ModifiedOn = p.ModifiedOn,
				StationGroupId = p.StationGroupId,
				CallId = p.CallId,
				CallNumber = p.CallNumber,
				AuthorUserId = p.AuthorUserId,
				OwnerUserId = p.OwnerUserId,
				ReviewerUserId = p.ReviewerUserId,
				DisplaySummary = p.DisplaySummary,
				IsLegacy = p.IsLegacy,
				RowVersion = p.RowVersion,
				IsTombstone = p.DeletedOn.HasValue || state == RmsRecordState.Cancelled || state == RmsRecordState.Voided,
				DeletedOn = p.DeletedOn
			};
		}

		public static RecordData ToRecord(RecordAggregate aggregate, bool canViewRestricted)
		{
			var r = aggregate.Record;
			var state = (RmsRecordState)r.State;
			var preset = (RmsLifecyclePreset)r.LifecyclePreset;
			var restricted = RmsDefinitionKeys.RestrictedClass.Contains(r.DefinitionKey ?? string.Empty);
			var data = new RecordData
			{
				RecordId = r.RmsOperationalRecordId,
				RecordKind = RmsRecordKind.Operational.ToString(),
				DefinitionKey = r.DefinitionKey,
				DefinitionVersion = r.DefinitionVersion,
				RecordType = r.RecordType,
				RecordTypeName = r.RecordType.HasValue ? ((RmsOperationalRecordType)r.RecordType.Value).ToString() : null,
				LifecyclePreset = r.LifecyclePreset,
				LifecyclePresetName = preset.ToString(),
				State = r.State,
				StateName = state.ToString(),
				RecordNumber = r.RecordNumber,
				DraftReference = r.DraftReference,
				DisplaySummary = r.DisplaySummary,
				StationGroupId = r.StationGroupId,
				CallId = r.CallId,
				ExternalId = r.ExternalId,
				AuthorUserId = r.AuthorUserId,
				OwnerUserId = r.OwnerUserId,
				ReviewerUserId = r.ReviewerUserId,
				ApproverUserId = r.ApproverUserId,
				StartedOn = r.StartedOn,
				EndedOn = r.EndedOn,
				ReviewDueOn = r.ReviewDueOn,
				SubmittedForReviewOn = r.SubmittedForReviewOn,
				ReturnedOn = r.ReturnedOn,
				ReturnReasonCode = r.ReturnReasonCode,
				ReturnReasonText = r.ReturnReasonText,
				ReturnCount = r.ReturnCount,
				ApprovedOn = r.ApprovedOn,
				FinalizedOn = r.FinalizedOn,
				FinalizedByUserId = r.FinalizedByUserId,
				CurrentRevisionId = r.CurrentRevisionId,
				RevisionCount = r.RevisionCount,
				AmendsRevisionId = r.AmendsRevisionId,
				VoidedOn = r.VoidedOn,
				VoidReasonCode = r.VoidReasonCode,
				VoidReasonText = r.VoidReasonText,
				CancelledOn = r.CancelledOn,
				OriginClient = r.OriginClient,
				CreatedOn = r.CreatedOn,
				ModifiedOn = r.ModifiedOn,
				RowVersion = r.RowVersion,
				ETag = RecordsApiContract.ToETag(r.RowVersion),
				IsEditable = RmsLifecycle.IsEditable(state) || r.AmendsRevisionId != null,
				IsRestricted = restricted,
				AvailableTransitions = RmsLifecycle.NextStates(preset, state).Select(s => s.ToString()).ToList(),
				Details = ToDetails(aggregate.Details, restricted && !canViewRestricted, out var withheld),
				WithheldFields = withheld,
				Participants = aggregate.Participants.Select(p => new RecordParticipantData { UserId = p.UserId, DisplayName = p.DisplayNameSnapshot, GroupId = p.GroupIdSnapshot, GroupName = p.GroupNameSnapshot, UnitId = p.UnitId, Role = p.Role }).ToList(),
				Units = aggregate.Units.Select(ToUnit).ToList(),
				Attachments = aggregate.Attachments.Where(a => a.DeletedOn == null).Select(ToAttachment).ToList(),
				Revisions = aggregate.Revisions.OrderByDescending(x => x.RevisionNumber).Select(ToRevision).ToList(),
				GroupScopeIds = aggregate.GroupScope.Select(g => g.DepartmentGroupId).Distinct().ToList()
			};
			return data;
		}

		public static RecordData ToRecord(RecordSnapshot snapshot, bool canViewRestricted)
		{
			var restricted = RmsDefinitionKeys.RestrictedClass.Contains(snapshot.DefinitionKey ?? string.Empty);
			return new RecordData
			{
				RecordId = snapshot.RecordId,
				RecordKind = RmsRecordKind.Operational.ToString(),
				DefinitionKey = snapshot.DefinitionKey,
				DefinitionVersion = snapshot.DefinitionVersion,
				RecordType = snapshot.RecordType,
				RecordTypeName = snapshot.RecordType.HasValue ? ((RmsOperationalRecordType)snapshot.RecordType.Value).ToString() : null,
				RecordNumber = snapshot.RecordNumber,
				DraftReference = snapshot.DraftReference,
				StationGroupId = snapshot.StationGroupId,
				CallId = snapshot.CallId,
				ExternalId = snapshot.ExternalId,
				AuthorUserId = snapshot.AuthorUserId,
				StartedOn = snapshot.StartedOn,
				EndedOn = snapshot.EndedOn,
				IsRestricted = restricted,
				Details = ToDetails(snapshot.Details, restricted && !canViewRestricted, out var withheld),
				WithheldFields = withheld,
				Participants = snapshot.Participants.Select(p => new RecordParticipantData { UserId = p.UserId, DisplayName = p.DisplayNameSnapshot, GroupId = p.GroupIdSnapshot, GroupName = p.GroupNameSnapshot, UnitId = p.UnitId, Role = p.Role }).ToList(),
				Units = snapshot.Units.Select(ToUnit).ToList(),
				Attachments = snapshot.Attachments.Select(ToAttachment).ToList()
			};
		}

		public static RecordDetailsData ToDetails(RmsOperationalRecordDetail d, bool withholdRestricted, out List<string> withheld)
		{
			withheld = new List<string>();
			d = d ?? new RmsOperationalRecordDetail();
			var data = new RecordDetailsData
			{
				Narrative = d.Narrative, InitialReport = d.InitialReport, Type = d.Type, Course = d.Course, CourseCode = d.CourseCode, Instructors = d.Instructors, Cause = d.Cause,
				InvestigatedByUserId = d.InvestigatedByUserId, ContactName = d.ContactName, ContactNumber = d.ContactNumber, OtherPersonnel = d.OtherPersonnel, Location = d.Location,
				OtherAgencies = d.OtherAgencies, OtherUnits = d.OtherUnits, BodyLocation = d.BodyLocation, PronouncedDeceasedBy = d.PronouncedDeceasedBy, CaseNumber = d.CaseNumber,
				Destination = d.Destination, Facilitator = d.Facilitator, UnitId = d.UnitId, ActivityOn = d.ActivityOn, CallNumber = d.CallNumber, CallName = d.CallName, CallType = d.CallType,
				CallPriority = d.CallPriority, CallLoggedOn = d.CallLoggedOn, CallAddress = d.CallAddress, CallNature = d.CallNature
			};
			if (!withholdRestricted)
				return data;

			foreach (var field in RecordSnapshotSerializer.RestrictedDetailFields)
			{
				var property = typeof(RecordDetailsData).GetProperty(field);
				if (property == null || property.GetValue(data) == null)
					continue;
				property.SetValue(data, null);
				withheld.Add(field);
			}
			return data;
		}

		public static RecordUnitResponseData ToUnit(RmsRecordUnitResponse u)
		{
			return new RecordUnitResponseData { UnitId = u.UnitId, UnitName = u.UnitNameSnapshot, UnitType = u.UnitTypeSnapshot, StationGroupId = u.StationGroupIdSnapshot, Dispatched = u.Dispatched, Enroute = u.Enroute, OnScene = u.OnScene, Released = u.Released, InQuarters = u.InQuarters };
		}

		public static RecordAttachmentData ToAttachment(RmsRecordAttachment a)
		{
			return new RecordAttachmentData
			{
				AttachmentId = a.RmsRecordAttachmentId, RecordId = a.RecordId, FileName = a.FileName, ContentType = a.ContentType, ByteSize = a.ByteSize, Checksum = a.Checksum,
				Description = a.Description, UploadedByUserId = a.UploadedByUserId, UploadedOn = a.UploadedOn, ScanState = a.ScanState, ScanStateName = ((RmsAttachmentScanState)a.ScanState).ToString()
			};
		}

		public static RecordRevisionData ToRevision(RmsRevision r)
		{
			return new RecordRevisionData
			{
				RevisionId = r.RmsRevisionId, RevisionNumber = r.RevisionNumber, Transition = r.Transition, TransitionName = ((RmsRevisionTransition)r.Transition).ToString(), PriorRevisionId = r.PriorRevisionId,
				Checksum = r.Checksum, ActorUserId = r.ActorUserId, ReasonCode = r.ReasonCode, ReasonText = r.ReasonText, AttestationStatementVersion = r.AttestationStatementVersion, AttestedOn = r.AttestedOn, CreatedOn = r.CreatedOn
			};
		}

		public static RecordUploadData ToUpload(RecordAttachmentUploadSession s)
		{
			return new RecordUploadData
			{
				UploadId = s.UploadId, RecordId = s.RecordId, FileName = s.FileName, ContentType = s.ContentType, DeclaredSize = s.DeclaredSize, ReceivedBytes = s.ReceivedBytes, ChunkSize = s.ChunkSize,
				ChunkCount = s.ChunkCount, State = (int)s.State, StateName = s.State.ToString(), ExpiresOn = s.ExpiresOn, AttachmentId = s.AttachmentId
			};
		}

		public static RecordDraftInput ToDraftInput(SaveRecordDraftInput input, RmsOriginClient origin)
		{
			var d = input.Details ?? new RecordDetailsInput();
			return new RecordDraftInput
			{
				DefinitionKey = input.DefinitionKey,
				CallId = input.CallId,
				StationGroupId = input.StationGroupId,
				ExternalId = input.ExternalId,
				StartedOn = RecordsApiHelper.Utc(input.StartedOn),
				EndedOn = RecordsApiHelper.Utc(input.EndedOn),
				Details = new RmsOperationalRecordDetail
				{
					Narrative = d.Narrative, InitialReport = d.InitialReport, Type = d.Type, Course = d.Course, CourseCode = d.CourseCode, Instructors = d.Instructors, Cause = d.Cause,
					InvestigatedByUserId = d.InvestigatedByUserId, ContactName = d.ContactName, ContactNumber = d.ContactNumber, OtherPersonnel = d.OtherPersonnel, Location = d.Location,
					OtherAgencies = d.OtherAgencies, OtherUnits = d.OtherUnits, BodyLocation = d.BodyLocation, PronouncedDeceasedBy = d.PronouncedDeceasedBy, CaseNumber = d.CaseNumber,
					Destination = d.Destination, Facilitator = d.Facilitator, UnitId = d.UnitId, ActivityOn = RecordsApiHelper.Utc(d.ActivityOn)
				},
				Participants = (input.Participants ?? new List<RecordParticipantInputData>()).Where(p => !string.IsNullOrWhiteSpace(p.UserId)).Select(p => new RecordParticipantInput { UserId = p.UserId, UnitId = p.UnitId, Role = p.Role }).ToList(),
				Units = (input.Units ?? new List<RecordUnitResponseInputData>()).Where(u => u.UnitId > 0).Select(u => new RecordUnitResponseInput
				{
					UnitId = u.UnitId, Dispatched = RecordsApiHelper.Utc(u.Dispatched), Enroute = RecordsApiHelper.Utc(u.Enroute), OnScene = RecordsApiHelper.Utc(u.OnScene), Released = RecordsApiHelper.Utc(u.Released), InQuarters = RecordsApiHelper.Utc(u.InQuarters)
				}).ToList(),
				ClientRecordId = input.ClientRecordId,
				IdempotencyKey = input.IdempotencyKey,
				OriginClient = origin,
				DuplicateContinueReason = input.DuplicateContinueReason
			};
		}

		public static RecordConflictData ToConflict(RecordDraftConflict conflict, RecordAggregate current, bool canViewRestricted)
		{
			return new RecordConflictData
			{
				RecordId = conflict.RecordId,
				ExpectedRowVersion = conflict.ExpectedRowVersion,
				CurrentRowVersion = conflict.CurrentRowVersion,
				CurrentState = (int)conflict.CurrentState,
				CurrentStateName = conflict.CurrentState.ToString(),
				CurrentRevisionId = conflict.CurrentRevisionId,
				ChangedFieldPaths = conflict.ChangedFieldPaths,
				Current = current == null ? null : ToRecord(current, canViewRestricted)
			};
		}

		public static List<RecordDefinitionData> ToDefinitions()
		{
			return RecordDefinitionCatalog.Describe().Select(d => new RecordDefinitionData
			{
				Key = d.Key, Version = d.Version, Name = d.Name, RecordType = d.RecordType, RecordKind = d.RecordKind, LifecyclePreset = d.LifecyclePreset, LifecyclePresetName = d.LifecyclePresetName,
				Cardinality = d.Cardinality, Restricted = d.Restricted, NumberPrefix = d.NumberPrefix, RequiresCall = d.RequiresCall, SupportsParticipants = d.SupportsParticipants, SupportsUnits = d.SupportsUnits,
				SupportsAttachments = d.SupportsAttachments, MinimumClientCapability = d.MinimumClientCapability, Locked = d.Locked,
				Fields = d.Fields.Select(f => new RecordFieldData { Key = f.Key, Section = f.Section, Type = f.Type, Required = f.Required, RequiredToFinalize = f.RequiredToFinalize, Restricted = f.Restricted }).ToList()
			}).ToList();
		}
	}

	/// <summary>Entity to DTO mapping for the v4 IncidentReports controller. Submissions are sanitized: no payload, no response body.</summary>
	public static class IncidentReportsApiMapper
	{
		public static IncidentReportSummaryData ToSummary(RmsIncidentReport r)
		{
			return new IncidentReportSummaryData
			{
				ReportId = r.RmsIncidentReportId, RecordNumber = r.RecordNumber, DraftReference = r.DraftReference, CallId = r.CallId, IncidentNumber = r.IncidentNumber, NerisIncidentId = r.NerisIncidentId,
				State = r.State, StateName = ((RmsRecordState)r.State).ToString(), LastSubmissionState = r.LastSubmissionState,
				LastSubmissionStateName = r.LastSubmissionState.HasValue ? ((RmsSubmissionState)r.LastSubmissionState.Value).ToString() : null,
				DisplaySummary = r.DisplaySummary, StationGroupId = r.StationGroupId, AuthorUserId = r.AuthorUserId, OwnerUserId = r.OwnerUserId, CallCreatedOn = r.CallCreatedOn,
				CreatedOn = r.CreatedOn, ModifiedOn = r.ModifiedOn, FinalizedOn = r.FinalizedOn, RowVersion = r.RowVersion
			};
		}

		public static IncidentReportData ToReport(IncidentReportAggregate a, bool submissionEnabled, bool canViewRestricted = true,
			IEnumerable<NerisSectionRequirement> sections = null, string incidentAnalysisId = null)
		{
			var withheld = new List<string>();
			var r = a.Report;
			var state = (RmsRecordState)r.State;
			var preset = (RmsLifecyclePreset)r.LifecyclePreset;
			var canQueue = submissionEnabled && r.AmendsRevisionId == null && !string.IsNullOrWhiteSpace(r.CurrentRevisionId)
				&& (state == RmsRecordState.Finalized || state == RmsRecordState.Amended || state == RmsRecordState.Corrected
					// A destination rejection is stored as RmsSubmissionState.Rejected, and QueueSubmissionCoreAsync
					// re-queues Failed, Superseded and Rejected alike; reporting only Failed hid the retry action.
					|| (state == RmsRecordState.Rejected && a.Submissions.Any(s => s.State == (int)RmsSubmissionState.Failed || s.State == (int)RmsSubmissionState.Rejected)));
			return new IncidentReportData
			{
				ReportId = r.RmsIncidentReportId, CallId = r.CallId, ReportingEntityId = r.ReportingEntityId, DefinitionKey = r.DefinitionKey, DefinitionVersion = r.DefinitionVersion, ProfileVersion = r.ProfileVersion,
				LifecyclePreset = r.LifecyclePreset, State = r.State, StateName = state.ToString(), RecordNumber = r.RecordNumber, DraftReference = r.DraftReference, DisplaySummary = r.DisplaySummary,
				IncidentNumber = r.IncidentNumber, NerisIncidentId = r.NerisIncidentId, LastSubmissionId = r.LastSubmissionId, LastSubmissionState = r.LastSubmissionState,
				LastSubmissionStateName = r.LastSubmissionState.HasValue ? ((RmsSubmissionState)r.LastSubmissionState.Value).ToString() : null, LastSubmittedOn = r.LastSubmittedOn,
				AcceptedOn = r.AcceptedOn, RejectedOn = r.RejectedOn, RejectionSummary = r.RejectionSummary, StationGroupId = r.StationGroupId, AuthorUserId = r.AuthorUserId, OwnerUserId = r.OwnerUserId,
				ReviewerUserId = r.ReviewerUserId, CallCreatedOn = r.CallCreatedOn, CallAnsweredOn = r.CallAnsweredOn, CallArrivalOn = r.CallArrivalOn, IncidentClearedOn = r.IncidentClearedOn,
				DispatchCenterId = r.DispatchCenterId, DeterminantCode = r.DeterminantCode, DispatchIncidentCode = r.DispatchIncidentCode, Disposition = r.Disposition, PeoplePresent = r.PeoplePresent,
				DisplacementCount = r.DisplacementCount, AnimalsRescued = r.AnimalsRescued,
				SpecialModifiers = string.IsNullOrWhiteSpace(r.SpecialModifiersCsv) ? new List<string>() : r.SpecialModifiersCsv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
				ReviewDueOn = r.ReviewDueOn, SubmittedForReviewOn = r.SubmittedForReviewOn, ReturnedOn = r.ReturnedOn, ReturnReasonCode = r.ReturnReasonCode, ReturnReasonText = r.ReturnReasonText,
				FinalizedOn = r.FinalizedOn, CurrentRevisionId = r.CurrentRevisionId, RevisionCount = r.RevisionCount, AmendsRevisionId = r.AmendsRevisionId, VoidedOn = r.VoidedOn, VoidReasonCode = r.VoidReasonCode,
				CancelledOn = r.CancelledOn, OriginClient = r.OriginClient, CreatedOn = r.CreatedOn, ModifiedOn = r.ModifiedOn, RowVersion = r.RowVersion, ETag = RecordsApiContract.ToETag(r.RowVersion),
				IsEditable = RmsLifecycle.IsEditable(state) || state == RmsRecordState.Rejected || r.AmendsRevisionId != null,
				SubmissionEnabled = submissionEnabled, CanQueueSubmission = canQueue, HasBlockingIssues = a.HasBlockingIssues,
				AvailableTransitions = RmsLifecycle.NextStates(preset, state).Select(s => s.ToString()).ToList(),
				Location = a.Location == null ? null : new IncidentLocationData
				{
					AddressText = a.Location.AddressText, Number = a.Location.Number, NumberPrefix = a.Location.NumberPrefix, NumberSuffix = a.Location.NumberSuffix, Street = a.Location.Street, UnitValue = a.Location.UnitValue,
					Municipality = a.Location.Municipality, County = a.Location.County, State = a.Location.State, PostalCode = a.Location.PostalCode, Country = a.Location.Country, PlaceType = a.Location.PlaceType,
					LocationUse = a.Location.LocationUse, CrossStreet1 = a.Location.CrossStreet1, CrossStreet2 = a.Location.CrossStreet2, Latitude = a.Location.Latitude, Longitude = a.Location.Longitude,
					Jurisdiction = a.Location.Jurisdiction, SourceKind = a.Location.SourceKind, SourceKindName = ((RmsSourceKind)a.Location.SourceKind).ToString()
				},
				Types = a.Types.OrderBy(t => t.Ordinal).Select(t => new IncidentTypeData { TypeCode = t.TypeCode, IsPrimary = t.IsPrimary, LocalCode = t.LocalCode, Ordinal = t.Ordinal }).ToList(),
				Units = a.Units.OrderBy(u => u.Ordinal).Select(u => new IncidentUnitResponseData
				{
					UnitId = u.UnitId, UnitName = u.UnitNameSnapshot, UnitType = u.UnitTypeSnapshot, StationGroupId = u.StationGroupIdSnapshot, UnitNerisId = u.UnitNerisId, Staffing = u.Staffing, UnableToDispatch = u.UnableToDispatch,
					DispatchedOn = u.DispatchedOn, EnrouteOn = u.EnrouteOn, OnSceneOn = u.OnSceneOn, StagingOn = u.StagingOn, CanceledEnrouteOn = u.CanceledEnrouteOn, ClearedOn = u.ClearedOn, ResponseMode = u.ResponseMode,
					TransportMode = u.TransportMode, TimesSourceKind = u.TimesSourceKind, TimesSourceKindName = ((RmsSourceKind)u.TimesSourceKind).ToString(), Ordinal = u.Ordinal
				}).ToList(),
				Aids = a.Aids.OrderBy(x => x.Ordinal).Select(x => new IncidentAidData { Direction = x.Direction, AidType = x.AidType, CounterpartNerisId = x.CounterpartNerisId, CounterpartName = x.CounterpartName, IsNonFireDepartment = x.IsNonFireDepartment, NonFdType = x.NonFdType, Ordinal = x.Ordinal }).ToList(),
				Tactics = a.Tactics.OrderBy(t => t.Ordinal).Select(t => new IncidentTacticData { TacticCode = t.TacticCode, ActorUnitId = t.ActorUnitId, OccurredOn = t.OccurredOn, Ordinal = t.Ordinal }).ToList(),
				Narrative = a.Narrative?.Narrative, ImpedimentNarrative = a.Narrative?.ImpedimentNarrative, OutcomeNarrative = a.Narrative?.OutcomeNarrative, SupplementalJson = a.Narrative?.SupplementalJson,
				Facts = a.Facts.OrderBy(f => f.FactKey).Select(f => new IncidentFactData
				{
					FactKey = f.FactKey, SourceKind = f.SourceKind, SourceKindName = ((RmsSourceKind)f.SourceKind).ToString(), SourceSystem = f.SourceSystem, SourceEntityType = f.SourceEntityType, SourceEntityId = f.SourceEntityId,
					SourceValue = f.SourceValue, CurrentValue = f.CurrentValue, SourceTime = f.SourceTime, CorrectedOn = f.CorrectedOn, CorrectedByUserId = f.CorrectedByUserId
				}).ToList(),
				Sections = BuildSections(a, sections),
				Modules = a.Modules.OrderBy(m => m.Ordinal).Select(ToModule).ToList(),
				Resources = a.Resources.OrderBy(r => r.Ordinal).Select(r => new IncidentResourceData
				{
					ResourceId = r.RmsIncidentResourceId, ResourceCode = r.ResourceCode, Quantity = r.Quantity, Detail = r.Detail, Ordinal = r.Ordinal
				}).ToList(),
				Casualties = a.Casualties.OrderBy(c => c.Ordinal).Select(c => ToCasualty(c, canViewRestricted, withheld)).ToList(),
				Exposures = a.Exposures.OrderBy(e => e.Ordinal).Select(ToExposure).ToList(),
				WithheldFields = withheld,
				IncidentAnalysisId = incidentAnalysisId,
				Issues = a.Issues.Select(ToIssue).ToList(),
				Submissions = a.Submissions.OrderByDescending(s => s.QueuedOn).Select(s => new IncidentSubmissionData
				{
					SubmissionId = s.RmsSubmissionId, RevisionId = s.RevisionId, Destination = s.Destination, DestinationVersion = s.DestinationVersion, State = s.State, StateName = ((RmsSubmissionState)s.State).ToString(),
					ExternalId = s.ExternalId, ExternalStatus = s.ExternalStatus, Attempts = s.Attempts, MaxAttempts = s.MaxAttempts, ErrorSummary = s.ErrorSummary, PayloadChecksum = s.PayloadChecksum,
					QueuedOn = s.QueuedOn, SentOn = s.SentOn, CompletedOn = s.CompletedOn, NextAttemptOn = s.NextAttemptOn
				}).ToList(),
				Signatures = a.Signatures.OrderByDescending(s => s.SignedOn).Select(s => new IncidentSignatureData
				{
					SignatureId = s.RmsSignatureId, RevisionId = s.RevisionId, SignerUserId = s.SignerUserId, SignerName = s.SignerNameSnapshot, SignerRole = s.SignerRoleSnapshot, Intent = s.Intent,
					StatementVersion = s.StatementVersion, StatementText = s.StatementText, SignedOn = s.SignedOn, ArtifactChecksum = s.ArtifactChecksum
				}).ToList(),
				Revisions = a.Revisions.OrderByDescending(x => x.RevisionNumber).Select(RecordsApiMapper.ToRevision).ToList(),
				GroupScopeIds = a.GroupScope.Select(g => g.DepartmentGroupId).Distinct().ToList()
			};
		}

		/// <summary>
		/// The progressive section requirements, each marked with whether the report already carries that section,
		/// plus the contract's payload path and schema so a client can render and post the right shape.
		/// </summary>
		private static List<IncidentSectionRequirementData> BuildSections(IncidentReportAggregate a, IEnumerable<NerisSectionRequirement> sections)
		{
			var present = new HashSet<int>(a.Modules.Select(m => m.ModuleKind));
			return (sections ?? Enumerable.Empty<NerisSectionRequirement>()).Select(r =>
			{
				var descriptor = RmsIncidentModuleCatalog.Get(r.Kind);
				return new IncidentSectionRequirementData
				{
					Kind = (int)r.Kind,
					KindName = r.Kind.ToString(),
					PayloadPath = descriptor?.PayloadPath,
					SchemaName = descriptor?.SchemaName,
					IsCollection = descriptor?.IsCollection ?? false,
					Required = r.Required,
					Reason = r.Reason,
					PrimaryCodeSet = r.PrimaryCodeSet,
					SecondaryCodeSet = r.SecondaryCodeSet,
					Present = present.Contains((int)r.Kind)
				};
			}).ToList();
		}

		public static IncidentModuleData ToModule(RmsIncidentModule m)
		{
			var kind = (RmsIncidentModuleKind)m.ModuleKind;
			var descriptor = RmsIncidentModuleCatalog.Get(kind);
			return new IncidentModuleData
			{
				ModuleId = m.RmsIncidentModuleId, Kind = m.ModuleKind, KindName = kind.ToString(),
				PayloadPath = descriptor?.PayloadPath, SchemaName = m.SchemaName ?? descriptor?.SchemaName,
				PrimaryCode = m.PrimaryCode, SecondaryCode = m.SecondaryCode, Quantity = m.Quantity, QuantityUnit = m.QuantityUnit,
				OccurredOn = m.OccurredOn, DetailJson = m.DetailJson, Ordinal = m.Ordinal
			};
		}

		/// <summary>
		/// A casualty or rescue. The entry itself is not secret — that somebody was hurt is part of the report —
		/// but demographics, the personnel link and the injury detail are restricted, so they are dropped and named
		/// rather than the whole row disappearing, which would misrepresent the incident.
		/// </summary>
		public static IncidentCasualtyData ToCasualty(RmsCasualtyRescue c, bool canViewRestricted, List<string> withheld)
		{
			var data = new IncidentCasualtyData
			{
				CasualtyId = c.RmsCasualtyRescueId, Kind = c.Kind, KindName = ((RmsCasualtyRescueKind)c.Kind).ToString(), PersonType = c.PersonType,
				YearsOfService = c.YearsOfService, JobClassification = c.JobClassification, WasInjured = c.WasInjured, WasFatal = c.WasFatal,
				CasualtyCause = c.CasualtyCause, CasualtyAction = c.CasualtyAction, CasualtyTimeline = c.CasualtyTimeline, DutyType = c.DutyType,
				Ppe = SplitCodes(c.PpeCsv), RescueType = c.RescueType, RescueActions = SplitCodes(c.RescueActionsCsv),
				RescueImpediments = SplitCodes(c.RescueImpedimentsCsv), RescueMode = c.RescueMode, RescuePath = c.RescuePath,
				RescueElevation = c.RescueElevation, PresenceKnown = c.PresenceKnown, OccurredOn = c.OccurredOn, Ordinal = c.Ordinal
			};

			if (canViewRestricted)
			{
				data.PersonnelUserId = c.PersonnelUserId;
				data.Rank = c.Rank;
				data.BirthMonthYear = c.BirthMonthYear;
				data.Gender = c.Gender;
				data.Race = c.Race;
				data.InjuryDetailJson = c.InjuryDetailJson;
			}
			else
			{
				foreach (var field in new[] { "PersonnelUserId", "Rank", "BirthMonthYear", "Gender", "Race", "InjuryDetailJson" })
					withheld.Add("Casualties." + field);
			}

			return data;
		}

		public static IncidentExposureData ToExposure(RmsExposure e)
		{
			return new IncidentExposureData
			{
				ExposureId = e.RmsExposureId, LocationKind = e.LocationKind, ItemType = e.ItemType, DamageType = e.DamageType, LocationUse = e.LocationUse,
				PeoplePresent = e.PeoplePresent, DisplacementCount = e.DisplacementCount, DisplacementCauses = SplitCodes(e.DisplacementCausesCsv),
				AddressText = e.AddressText, Street = e.Street, Municipality = e.Municipality, State = e.State, PostalCode = e.PostalCode,
				Latitude = e.Latitude, Longitude = e.Longitude, EstimatedValue = e.EstimatedValue, EstimatedLoss = e.EstimatedLoss,
				CurrencyCode = e.CurrencyCode, Ordinal = e.Ordinal
			};
		}

		private static List<string> SplitCodes(string csv)
		{
			return string.IsNullOrWhiteSpace(csv)
				? new List<string>()
				: csv.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
		}

		public static IncidentIssueData ToIssue(RmsValidationIssue i)
		{
			return new IncidentIssueData { RuleKey = i.RuleKey, Severity = i.Severity, SeverityName = ((RmsValidationSeverity)i.Severity).ToString(), FieldPath = i.FieldPath, Message = i.Message, Source = i.Source, SourceName = ((RmsValidationSource)i.Source).ToString() };
		}

		public static IncidentReportDraftInput ToDraftInput(SaveIncidentReportDraftInput input, RmsOriginClient origin)
		{
			return new IncidentReportDraftInput
			{
				IncidentNumber = input.IncidentNumber,
				CallCreatedOn = RecordsApiHelper.Utc(input.CallCreatedOn),
				CallAnsweredOn = RecordsApiHelper.Utc(input.CallAnsweredOn),
				CallArrivalOn = RecordsApiHelper.Utc(input.CallArrivalOn),
				IncidentClearedOn = RecordsApiHelper.Utc(input.IncidentClearedOn),
				DispatchCenterId = input.DispatchCenterId,
				DeterminantCode = input.DeterminantCode,
				DispatchIncidentCode = input.DispatchIncidentCode,
				Disposition = input.Disposition,
				PeoplePresent = input.PeoplePresent,
				DisplacementCount = input.DisplacementCount,
				AnimalsRescued = input.AnimalsRescued,
				SpecialModifiers = input.SpecialModifiers ?? new List<string>(),
				StationGroupId = input.StationGroupId,
				Location = input.Location,
				Types = input.Types ?? new List<IncidentTypeInput>(),
				Units = (input.Units ?? new List<IncidentUnitResponseInput>()).Select(u => new IncidentUnitResponseInput
				{
					UnitId = u.UnitId, UnitNerisId = u.UnitNerisId, ReportedUnitId = u.ReportedUnitId, Staffing = u.Staffing, UnableToDispatch = u.UnableToDispatch,
					DispatchedOn = RecordsApiHelper.Utc(u.DispatchedOn), EnrouteOn = RecordsApiHelper.Utc(u.EnrouteOn), OnSceneOn = RecordsApiHelper.Utc(u.OnSceneOn), CanceledEnrouteOn = RecordsApiHelper.Utc(u.CanceledEnrouteOn),
					StagingOn = RecordsApiHelper.Utc(u.StagingOn), ClearedOn = RecordsApiHelper.Utc(u.ClearedOn), ResponseMode = u.ResponseMode, TransportMode = u.TransportMode
				}).ToList(),
				Aids = input.Aids ?? new List<IncidentAidInput>(),
				Tactics = (input.Tactics ?? new List<IncidentTacticInput>()).Select(t => new IncidentTacticInput { TacticCode = t.TacticCode, ActorUnitId = t.ActorUnitId, OccurredOn = RecordsApiHelper.Utc(t.OccurredOn) }).ToList(),
				Narrative = input.Narrative,
				ImpedimentNarrative = input.ImpedimentNarrative,
				OutcomeNarrative = input.OutcomeNarrative,
				SupplementalJson = input.SupplementalJson,
				// Null stays null: the service reads absence as "leave this section alone".
				Modules = input.Modules?.Select(m => new IncidentModuleInput
				{
					Kind = (RmsIncidentModuleKind)m.Kind, PrimaryCode = m.PrimaryCode, SecondaryCode = m.SecondaryCode,
					Quantity = m.Quantity, QuantityUnit = m.QuantityUnit, OccurredOn = RecordsApiHelper.Utc(m.OccurredOn), DetailJson = m.DetailJson
				}).ToList(),
				Resources = input.Resources?.Select(r => new IncidentResourceInput { ResourceCode = r.ResourceCode, Quantity = r.Quantity, Detail = r.Detail }).ToList(),
				Casualties = input.Casualties?.Select(c => new IncidentCasualtyRescueInput
				{
					Kind = (RmsCasualtyRescueKind)c.Kind, PersonType = c.PersonType, PersonnelUserId = c.PersonnelUserId, Rank = c.Rank,
					YearsOfService = c.YearsOfService, JobClassification = c.JobClassification, BirthMonthYear = c.BirthMonthYear,
					Gender = c.Gender, Race = c.Race, WasInjured = c.WasInjured, WasFatal = c.WasFatal,
					CasualtyCause = c.CasualtyCause, CasualtyAction = c.CasualtyAction, CasualtyTimeline = c.CasualtyTimeline, DutyType = c.DutyType,
					Ppe = c.Ppe ?? new List<string>(), InjuryDetailJson = c.InjuryDetailJson, RescueType = c.RescueType,
					RescueActions = c.RescueActions ?? new List<string>(), RescueImpediments = c.RescueImpediments ?? new List<string>(),
					RescueMode = c.RescueMode, RescuePath = c.RescuePath, RescueElevation = c.RescueElevation, PresenceKnown = c.PresenceKnown,
					OccurredOn = RecordsApiHelper.Utc(c.OccurredOn), DetailJson = c.DetailJson
				}).ToList(),
				Exposures = input.Exposures?.Select(e => new IncidentExposureInput
				{
					LocationKind = e.LocationKind, ItemType = e.ItemType, DamageType = e.DamageType, LocationUse = e.LocationUse,
					PeoplePresent = e.PeoplePresent, DisplacementCount = e.DisplacementCount, DisplacementCauses = e.DisplacementCauses ?? new List<string>(),
					AddressText = e.AddressText, Street = e.Street, Municipality = e.Municipality, State = e.State, PostalCode = e.PostalCode,
					Latitude = e.Latitude, Longitude = e.Longitude, EstimatedValue = e.EstimatedValue, EstimatedLoss = e.EstimatedLoss,
					CurrencyCode = e.CurrencyCode, DetailJson = e.DetailJson
				}).ToList(),
				OriginClient = origin
			};
		}
	}
}
