using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The RMS protected-field candidate registry made executable (RMS plan section 5.9.2, ADP catalog v10):
	/// for every RMS entity that holds a cataloged column, the stable catalog field id mapped to the property
	/// that carries it. The catalog (Resgrid.Services.ProtectedFieldCatalog) and the migration bindings
	/// (AdpTableBindings) name the same ids; a test pins the three in step so a column cannot be cataloged
	/// without a write seam, or seamed without a migration binding.
	/// <para>
	/// Field ids are AAD components and stable forever: <c>{table}.{column}</c> in lower case, the same
	/// convention every other family uses.
	/// </para>
	/// </summary>
	public static class RmsProtectedFields
	{
		public const string Family = "Records";

		private static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Map<T>(params (string Column, Func<T, string> Get, Action<T, string> Set)[] entries) where T : IEntity
		{
			var result = new Dictionary<string, (Func<T, string>, Action<T, string>)>(StringComparer.Ordinal);
			var table = TableOf<T>();
			foreach (var entry in entries)
				result[FieldId(table, entry.Column)] = (entry.Get, entry.Set);
			return result;
		}

		public static string FieldId(string table, string column) => $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}";

		private static string TableOf<T>() where T : IEntity
		{
			// Every RMS entity exposes TableName as an instance expression; a throwaway instance reads it.
			var instance = (T)Activator.CreateInstance(typeof(T));
			return instance.TableName;
		}

		/// <summary>Locked Logs-parity typed detail (RmsOperationalRecordDetails): the Log catalog v3 set carried forward, plus the Call snapshot columns that mirror the Calls catalog.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsOperationalRecordDetail, string> Get, Action<RmsOperationalRecordDetail, string> Set)> Details = Map<RmsOperationalRecordDetail>(
			("Narrative", d => d.Narrative, (d, v) => d.Narrative = v),
			("InitialReport", d => d.InitialReport, (d, v) => d.InitialReport = v),
			("Cause", d => d.Cause, (d, v) => d.Cause = v),
			("ContactName", d => d.ContactName, (d, v) => d.ContactName = v),
			("ContactNumber", d => d.ContactNumber, (d, v) => d.ContactNumber = v),
			("OtherPersonnel", d => d.OtherPersonnel, (d, v) => d.OtherPersonnel = v),
			("Location", d => d.Location, (d, v) => d.Location = v),
			("BodyLocation", d => d.BodyLocation, (d, v) => d.BodyLocation = v),
			("PronouncedDeceasedBy", d => d.PronouncedDeceasedBy, (d, v) => d.PronouncedDeceasedBy = v),
			("CaseNumber", d => d.CaseNumber, (d, v) => d.CaseNumber = v),
			("Destination", d => d.Destination, (d, v) => d.Destination = v),
			("CallName", d => d.CallName, (d, v) => d.CallName = v),
			("CallAddress", d => d.CallAddress, (d, v) => d.CallAddress = v),
			("CallNature", d => d.CallNature, (d, v) => d.CallNature = v));

		/// <summary>NERIS narrative sections.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsNarrative, string> Get, Action<RmsNarrative, string> Set)> Narratives = Map<RmsNarrative>(
			("Narrative", n => n.Narrative, (n, v) => n.Narrative = v),
			("ImpedimentNarrative", n => n.ImpedimentNarrative, (n, v) => n.ImpedimentNarrative = v),
			("OutcomeNarrative", n => n.OutcomeNarrative, (n, v) => n.OutcomeNarrative = v),
			("SupplementalJson", n => n.SupplementalJson, (n, v) => n.SupplementalJson = v));

		/// <summary>NERIS location text columns; coordinates ride the companion envelopes (see <see cref="LocationCompanions"/>).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsLocation, string> Get, Action<RmsLocation, string> Set)> Locations = Map<RmsLocation>(
			("AddressText", l => l.AddressText, (l, v) => l.AddressText = v),
			("Number", l => l.Number, (l, v) => l.Number = v),
			("NumberPrefix", l => l.NumberPrefix, (l, v) => l.NumberPrefix = v),
			("NumberSuffix", l => l.NumberSuffix, (l, v) => l.NumberSuffix = v),
			("Street", l => l.Street, (l, v) => l.Street = v),
			("UnitValue", l => l.UnitValue, (l, v) => l.UnitValue = v),
			("CrossStreet1", l => l.CrossStreet1, (l, v) => l.CrossStreet1 = v),
			("CrossStreet2", l => l.CrossStreet2, (l, v) => l.CrossStreet2 = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsLocation, decimal?> Get, Action<RmsLocation, decimal?> Set, Func<RmsLocation, string> GetEnvelope, Action<RmsLocation, string> SetEnvelope)> LocationCompanions =
			new Dictionary<string, (Func<RmsLocation, decimal?>, Action<RmsLocation, decimal?>, Func<RmsLocation, string>, Action<RmsLocation, string>)>(StringComparer.Ordinal)
			{
				[FieldId("RmsLocations", "Latitude")] = (l => l.Latitude, (l, v) => l.Latitude = v, l => l.ProtectedLatitudeEnvelope, (l, v) => l.ProtectedLatitudeEnvelope = v),
				[FieldId("RmsLocations", "Longitude")] = (l => l.Longitude, (l, v) => l.Longitude = v, l => l.ProtectedLongitudeEnvelope, (l, v) => l.ProtectedLongitudeEnvelope = v)
			};

		/// <summary>Source facts: dispatch comments and every prefilled value are user/dispatcher-authored text.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsSourceFact, string> Get, Action<RmsSourceFact, string> Set)> SourceFacts = Map<RmsSourceFact>(
			("SourceValue", f => f.SourceValue, (f, v) => f.SourceValue = v),
			("CurrentValue", f => f.CurrentValue, (f, v) => f.CurrentValue = v));

		/// <summary>Casualties and rescues: restricted class, every value column (Tier 1).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsCasualtyRescue, string> Get, Action<RmsCasualtyRescue, string> Set)> Casualties = Map<RmsCasualtyRescue>(
			("PersonnelUserId", c => c.PersonnelUserId, (c, v) => c.PersonnelUserId = v),
			("Rank", c => c.Rank, (c, v) => c.Rank = v),
			("JobClassification", c => c.JobClassification, (c, v) => c.JobClassification = v),
			("BirthMonthYear", c => c.BirthMonthYear, (c, v) => c.BirthMonthYear = v),
			("Gender", c => c.Gender, (c, v) => c.Gender = v),
			("Race", c => c.Race, (c, v) => c.Race = v),
			("CasualtyCause", c => c.CasualtyCause, (c, v) => c.CasualtyCause = v),
			("CasualtyAction", c => c.CasualtyAction, (c, v) => c.CasualtyAction = v),
			("CasualtyTimeline", c => c.CasualtyTimeline, (c, v) => c.CasualtyTimeline = v),
			("InjuryDetailJson", c => c.InjuryDetailJson, (c, v) => c.InjuryDetailJson = v),
			("DetailJson", c => c.DetailJson, (c, v) => c.DetailJson = v));

		/// <summary>Exposures: address and free detail; coordinates ride companions.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsExposure, string> Get, Action<RmsExposure, string> Set)> Exposures = Map<RmsExposure>(
			("AddressText", e => e.AddressText, (e, v) => e.AddressText = v),
			("Street", e => e.Street, (e, v) => e.Street = v),
			("DetailJson", e => e.DetailJson, (e, v) => e.DetailJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsExposure, decimal?> Get, Action<RmsExposure, decimal?> Set, Func<RmsExposure, string> GetEnvelope, Action<RmsExposure, string> SetEnvelope)> ExposureCompanions =
			new Dictionary<string, (Func<RmsExposure, decimal?>, Action<RmsExposure, decimal?>, Func<RmsExposure, string>, Action<RmsExposure, string>)>(StringComparer.Ordinal)
			{
				[FieldId("RmsExposures", "Latitude")] = (e => e.Latitude, (e, v) => e.Latitude = v, e => e.ProtectedLatitudeEnvelope, (e, v) => e.ProtectedLatitudeEnvelope = v),
				[FieldId("RmsExposures", "Longitude")] = (e => e.Longitude, (e, v) => e.Longitude = v, e => e.ProtectedLongitudeEnvelope, (e, v) => e.ProtectedLongitudeEnvelope = v)
			};

		/// <summary>Conditional incident sections carry contract-shaped free detail.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsIncidentModule, string> Get, Action<RmsIncidentModule, string> Set)> Modules = Map<RmsIncidentModule>(
			("DetailJson", m => m.DetailJson, (m, v) => m.DetailJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsIncidentProperty, string> Get, Action<RmsIncidentProperty, string> Set)> Properties = Map<RmsIncidentProperty>(
			("DetailJson", p => p.DetailJson, (p, v) => p.DetailJson = v));

		/// <summary>Vehicle identity (VIN/plate) is personal data; make/model are not.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsIncidentVehicle, string> Get, Action<RmsIncidentVehicle, string> Set)> Vehicles = Map<RmsIncidentVehicle>(
			("Vin", x => x.Vin, (x, v) => x.Vin = v),
			("LicensePlate", x => x.LicensePlate, (x, v) => x.LicensePlate = v),
			("DetailJson", x => x.DetailJson, (x, v) => x.DetailJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsIncidentResource, string> Get, Action<RmsIncidentResource, string> Set)> Resources = Map<RmsIncidentResource>(
			("Detail", r => r.Detail, (r, v) => r.Detail = v));

		/// <summary>The immutable revision snapshot is the record's whole content; it inherits the highest source classification.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsRevision, string> Get, Action<RmsRevision, string> Set)> Revisions = Map<RmsRevision>(
			("SnapshotJson", r => r.SnapshotJson, (r, v) => r.SnapshotJson = v));

		/// <summary>Destination payload and response artifacts are generated copies of protected content.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsSubmission, string> Get, Action<RmsSubmission, string> Set)> Submissions = Map<RmsSubmission>(
			("PayloadJson", s => s.PayloadJson, (s, v) => s.PayloadJson = v),
			("ResponseJson", s => s.ResponseJson, (s, v) => s.ResponseJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsSignature, string> Get, Action<RmsSignature, string> Set)> Signatures = Map<RmsSignature>(
			("StatementText", s => s.StatementText, (s, v) => s.StatementText = v));

		/// <summary>Evidence: the manifest is the captured content; title/reason are author-typed free text.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsEvidenceArtifact, string> Get, Action<RmsEvidenceArtifact, string> Set)> Evidence = Map<RmsEvidenceArtifact>(
			("Title", e => e.Title, (e, v) => e.Title = v),
			("CaptureReason", e => e.CaptureReason, (e, v) => e.CaptureReason = v),
			("ManifestJson", e => e.ManifestJson, (e, v) => e.ManifestJson = v));

		/// <summary>Disclosure requester identity is restricted; the scope narrative is free text.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsDisclosureRequest, string> Get, Action<RmsDisclosureRequest, string> Set)> DisclosureRequests = Map<RmsDisclosureRequest>(
			("RequesterName", r => r.RequesterName, (r, v) => r.RequesterName = v),
			("RequesterOrganization", r => r.RequesterOrganization, (r, v) => r.RequesterOrganization = v),
			("RequesterContact", r => r.RequesterContact, (r, v) => r.RequesterContact = v),
			("ScopeNarrative", r => r.ScopeNarrative, (r, v) => r.ScopeNarrative = v),
			("DispositionReason", r => r.DispositionReason, (r, v) => r.DispositionReason = v));

		/// <summary>The produced packet is a generated copy of released content.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsDisclosureProduction, string> Get, Action<RmsDisclosureProduction, string> Set)> DisclosureProductions = Map<RmsDisclosureProduction>(
			("ArtifactJson", p => p.ArtifactJson, (p, v) => p.ArtifactJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsRecordLegalHold, string> Get, Action<RmsRecordLegalHold, string> Set)> LegalHolds = Map<RmsRecordLegalHold>(
			("ReferenceNumber", h => h.ReferenceNumber, (h, v) => h.ReferenceNumber = v),
			("Notes", h => h.Notes, (h, v) => h.Notes = v),
			("ReleaseNotes", h => h.ReleaseNotes, (h, v) => h.ReleaseNotes = v));

		/// <summary>Attachment text metadata; the payload itself is the binary field <see cref="AttachmentDataFieldId"/>.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsRecordAttachment, string> Get, Action<RmsRecordAttachment, string> Set)> Attachments = Map<RmsRecordAttachment>(
			("FileName", a => a.FileName, (a, v) => a.FileName = v),
			("Description", a => a.Description, (a, v) => a.Description = v));

		public static readonly string AttachmentDataFieldId = FieldId("RmsRecordAttachments", "Data");

		/// <summary>
		/// Typed values of department definitions (catalog v11): one virtual field per row. The accessor packs the
		/// typed sibling columns to seal and unpacks them to reveal, so the seam's generic text path needs nothing new.
		/// A REDACTED sentinel leaves a sealed row exactly as stored (its siblings are already null), which is what
		/// RecordTypedValuesService.Shape renders as the withheld cell.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsRecordValue, string> Get, Action<RmsRecordValue, string> Set)> Values = Map<RmsRecordValue>(
			("ProtectedEnvelope",
				v => !string.IsNullOrEmpty(v.ProtectedEnvelope) ? v.ProtectedEnvelope : (v.ProtectionRequired ? RmsRecordValuePack.Pack(v) : null),
				(v, text) =>
				{
					if (ProtectedDataEnvelope.HasEnvelopePrefix(text)) { v.ProtectedEnvelope = text; RmsRecordValuePack.Clear(v); }
					else if (text != null && text != ProtectedDataEnvelope.RedactionValue) { RmsRecordValuePack.Unpack(v, text); v.ProtectedEnvelope = null; }
				}));

		public static readonly string ValueFieldId = FieldId("RmsRecordValues", "ProtectedEnvelope");

		/// <summary>The rendered export artifact (RmsExportRuns.Data) is a generated copy of record content.</summary>
		public static readonly string ExportRunDataFieldId = FieldId("RmsExportRuns", "Data");

		// ---- RMS-5 prevention and investigations, catalog v13 ----------------------------------------------------

		/// <summary>Occupancy master (RMS-5): access secrets, hazard/tactical text, utility notes, the on-site emergency contact. Structural codes and flags stay plaintext.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsOccupancy, string> Get, Action<RmsOccupancy, string> Set)> Occupancies = Map<RmsOccupancy>(
			("OccupantsNeedingAssistanceNotes", o => o.OccupantsNeedingAssistanceNotes, (o, v) => o.OccupantsNeedingAssistanceNotes = v),
			("UtilityNotes", o => o.UtilityNotes, (o, v) => o.UtilityNotes = v),
			("KnoxBoxLocation", o => o.KnoxBoxLocation, (o, v) => o.KnoxBoxLocation = v),
			("GateCode", o => o.GateCode, (o, v) => o.GateCode = v),
			("AlarmPanelLocation", o => o.AlarmPanelLocation, (o, v) => o.AlarmPanelLocation = v),
			("AlarmCompany", o => o.AlarmCompany, (o, v) => o.AlarmCompany = v),
			("AlarmCompanyPhone", o => o.AlarmCompanyPhone, (o, v) => o.AlarmCompanyPhone = v),
			("AccessNotes", o => o.AccessNotes, (o, v) => o.AccessNotes = v),
			("WaterSupplyNotes", o => o.WaterSupplyNotes, (o, v) => o.WaterSupplyNotes = v),
			("EmergencyContactName", o => o.EmergencyContactName, (o, v) => o.EmergencyContactName = v),
			("EmergencyContactPhone", o => o.EmergencyContactPhone, (o, v) => o.EmergencyContactPhone = v),
			("GeneralHazardNotes", o => o.GeneralHazardNotes, (o, v) => o.GeneralHazardNotes = v),
			("TacticalSummary", o => o.TacticalSummary, (o, v) => o.TacticalSummary = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsOccupancyHazard, string> Get, Action<RmsOccupancyHazard, string> Set)> OccupancyHazards = Map<RmsOccupancyHazard>(
			("Description", h => h.Description, (h, v) => h.Description = v),
			("LocationDescription", h => h.LocationDescription, (h, v) => h.LocationDescription = v),
			("GpsCoordinates", h => h.GpsCoordinates, (h, v) => h.GpsCoordinates = v));

		/// <summary>Inspection notes and the occupant representative's printed name.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsInspection, string> Get, Action<RmsInspection, string> Set)> Inspections = Map<RmsInspection>(
			("Notes", i => i.Notes, (i, v) => i.Notes = v),
			("SignatureName", i => i.SignatureName, (i, v) => i.SignatureName = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsViolation, string> Get, Action<RmsViolation, string> Set)> Violations = Map<RmsViolation>(
			("Description", x => x.Description, (x, v) => x.Description = v),
			("CorrectiveAction", x => x.CorrectiveAction, (x, v) => x.CorrectiveAction = v));

		/// <summary>Permit applicant identity and the reviewer's notes.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsPermit, string> Get, Action<RmsPermit, string> Set)> Permits = Map<RmsPermit>(
			("ApplicantName", p => p.ApplicantName, (p, v) => p.ApplicantName = v),
			("ApplicantPhone", p => p.ApplicantPhone, (p, v) => p.ApplicantPhone = v),
			("ApplicantEmail", p => p.ApplicantEmail, (p, v) => p.ApplicantEmail = v),
			("ReviewNotes", p => p.ReviewNotes, (p, v) => p.ReviewNotes = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsPlanReview, string> Get, Action<RmsPlanReview, string> Set)> PlanReviews = Map<RmsPlanReview>(
			("Comments", p => p.Comments, (p, v) => p.Comments = v));

		/// <summary>Investigation case narrative (Tier 1: restricted and encrypted).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsInvestigationCase, string> Get, Action<RmsInvestigationCase, string> Set)> InvestigationCases = Map<RmsInvestigationCase>(
			("IncidentSummary", c => c.IncidentSummary, (c, v) => c.IncidentSummary = v),
			("CauseDetail", c => c.CauseDetail, (c, v) => c.CauseDetail = v),
			("OriginDescription", c => c.OriginDescription, (c, v) => c.OriginDescription = v),
			("Findings", c => c.Findings, (c, v) => c.Findings = v),
			("ClosureReason", c => c.ClosureReason, (c, v) => c.ClosureReason = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsInvestigationNote, string> Get, Action<RmsInvestigationNote, string> Set)> InvestigationNotes = Map<RmsInvestigationNote>(
			("Subject", n => n.Subject, (n, v) => n.Subject = v),
			("Body", n => n.Body, (n, v) => n.Body = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsInvestigationEvidence, string> Get, Action<RmsInvestigationEvidence, string> Set)> InvestigationEvidence = Map<RmsInvestigationEvidence>(
			("Description", e => e.Description, (e, v) => e.Description = v),
			("CollectedFrom", e => e.CollectedFrom, (e, v) => e.CollectedFrom = v),
			("CurrentCustodianExternal", e => e.CurrentCustodianExternal, (e, v) => e.CurrentCustodianExternal = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsInvestigationCustody, string> Get, Action<RmsInvestigationCustody, string> Set)> InvestigationCustody = Map<RmsInvestigationCustody>(
			("FromExternal", c => c.FromExternal, (c, v) => c.FromExternal = v),
			("ToExternal", c => c.ToExternal, (c, v) => c.ToExternal = v),
			("Reason", c => c.Reason, (c, v) => c.Reason = v));

		public static readonly IReadOnlyDictionary<string, (Func<RmsInvestigationReferral, string> Get, Action<RmsInvestigationReferral, string> Set)> InvestigationReferrals = Map<RmsInvestigationReferral>(
			("Reason", r => r.Reason, (r, v) => r.Reason = v));

		/// <summary>QA findings and notes quote the record they review, so they inherit its sensitivity.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsQualityReview, string> Get, Action<RmsQualityReview, string> Set)> QualityReviews = Map<RmsQualityReview>(
			("FindingsJson", q => q.FindingsJson, (q, v) => q.FindingsJson = v),
			("Note", q => q.Note, (q, v) => q.Note = v));

		/// <summary>Prevention/investigation attachment text columns; the bytes ride <see cref="PreventionAttachmentDataFieldId"/>.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<RmsPreventionAttachment, string> Get, Action<RmsPreventionAttachment, string> Set)> PreventionAttachments = Map<RmsPreventionAttachment>(
			("FileName", a => a.FileName, (a, v) => a.FileName = v),
			("Description", a => a.Description, (a, v) => a.Description = v));

		public static readonly string PreventionAttachmentDataFieldId = FieldId("RmsPreventionAttachments", "Data");

		/// <summary>Every RMS field id the catalog must carry, for the pin test.</summary>
		public static IEnumerable<string> AllFieldIds()
		{
			foreach (var k in Details.Keys) yield return k;
			foreach (var k in Narratives.Keys) yield return k;
			foreach (var k in Locations.Keys) yield return k;
			foreach (var k in LocationCompanions.Keys) yield return k;
			foreach (var k in Occupancies.Keys) yield return k;
			foreach (var k in OccupancyHazards.Keys) yield return k;
			foreach (var k in Inspections.Keys) yield return k;
			foreach (var k in Violations.Keys) yield return k;
			foreach (var k in Permits.Keys) yield return k;
			foreach (var k in PlanReviews.Keys) yield return k;
			foreach (var k in InvestigationCases.Keys) yield return k;
			foreach (var k in InvestigationNotes.Keys) yield return k;
			foreach (var k in InvestigationEvidence.Keys) yield return k;
			foreach (var k in InvestigationCustody.Keys) yield return k;
			foreach (var k in InvestigationReferrals.Keys) yield return k;
			foreach (var k in QualityReviews.Keys) yield return k;
			foreach (var k in PreventionAttachments.Keys) yield return k;
			yield return PreventionAttachmentDataFieldId;
			foreach (var k in SourceFacts.Keys) yield return k;
			foreach (var k in Casualties.Keys) yield return k;
			foreach (var k in Exposures.Keys) yield return k;
			foreach (var k in ExposureCompanions.Keys) yield return k;
			foreach (var k in Modules.Keys) yield return k;
			foreach (var k in Properties.Keys) yield return k;
			foreach (var k in Vehicles.Keys) yield return k;
			foreach (var k in Resources.Keys) yield return k;
			foreach (var k in Revisions.Keys) yield return k;
			foreach (var k in Submissions.Keys) yield return k;
			foreach (var k in Signatures.Keys) yield return k;
			foreach (var k in Evidence.Keys) yield return k;
			foreach (var k in DisclosureRequests.Keys) yield return k;
			foreach (var k in DisclosureProductions.Keys) yield return k;
			foreach (var k in LegalHolds.Keys) yield return k;
			foreach (var k in Attachments.Keys) yield return k;
			yield return AttachmentDataFieldId;
			yield return ExportRunDataFieldId;
			foreach (var k in Values.Keys) yield return k;
		}
	}
}
