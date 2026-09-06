using System;
using System.Collections.Generic;

namespace Resgrid.Tests.Rms.Parity
{
	/// <summary>
	/// The executable field inventory (RMS plan section 6, RMS-0): every persisted column of the legacy Log
	/// family and where Records carries it. <see cref="RecordsParityTests.Every_legacy_column_is_in_the_inventory"/>
	/// reflects over the entities, so a column added to <see cref="Resgrid.Model.Log"/> without a decision here
	/// fails CI rather than silently losing a field at activation. "Not carried" entries are deliberate
	/// decisions with their reason, never omissions.
	/// </summary>
	public static class LegacyFieldMap
	{
		public const string NotCarried = "not carried: ";

		public static readonly IReadOnlyDictionary<string, string> Log = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			{ "LogId", NotCarried + "legacy identity stays in the read-only Logs module; Records allocates RmsOperationalRecordId and nothing is migrated" },
			{ "DepartmentId", "RmsOperationalRecord.DepartmentId (tenant scope)" },
			{ "Narrative", "RmsOperationalRecordDetail.Narrative" },
			{ "LogType", "RmsOperationalRecord.DefinitionKey + RecordType (RmsDefinitionKeys.ForType)" },
			{ "ExternalId", "RmsOperationalRecord.ExternalId" },
			{ "InitialReport", "RmsOperationalRecordDetail.InitialReport" },
			{ "Type", "RmsOperationalRecordDetail.Type" },
			{ "StationGroupId", "RmsOperationalRecord.StationGroupId" },
			{ "Course", "RmsOperationalRecordDetail.Course" },
			{ "CourseCode", "RmsOperationalRecordDetail.CourseCode" },
			{ "Instructors", "RmsOperationalRecordDetail.Instructors" },
			{ "Cause", "RmsOperationalRecordDetail.Cause" },
			{ "InvestigatedByUserId", "RmsOperationalRecordDetail.InvestigatedByUserId" },
			{ "ContactName", "RmsOperationalRecordDetail.ContactName" },
			{ "ContactNumber", "RmsOperationalRecordDetail.ContactNumber" },
			{ "StartedOn", "RmsOperationalRecord.StartedOn" },
			{ "EndedOn", "RmsOperationalRecord.EndedOn" },
			{ "LoggedOn", "RmsOperationalRecord.CreatedOn (server clock; a Record is never back-dated by the client)" },
			{ "LoggedByUserId", "RmsOperationalRecord.AuthorUserId (the acting user)" },
			{ "OfficerUserId", "RmsRecordParticipant with Role = Officer" },
			{ "CallId", "RmsOperationalRecord.CallId + RmsOperationalRecordDetail.Call* snapshot" },
			{ "OtherPersonnel", "RmsOperationalRecordDetail.OtherPersonnel (restricted section)" },
			{ "Location", "RmsOperationalRecordDetail.Location" },
			{ "OtherAgencies", "RmsOperationalRecordDetail.OtherAgencies" },
			{ "OtherUnits", "RmsOperationalRecordDetail.OtherUnits" },
			{ "BodyLocation", "RmsOperationalRecordDetail.BodyLocation (restricted section)" },
			{ "PronouncedDeceasedBy", "RmsOperationalRecordDetail.PronouncedDeceasedBy (restricted section)" },
			{ "Units", "RmsRecordUnitResponse rows" },
			{ "Users", "RmsRecordParticipant rows" }
		};

		public static readonly IReadOnlyDictionary<string, string> LogUser = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			{ "LogUserId", NotCarried + "legacy row identity" },
			{ "LogId", "RmsRecordParticipant.RecordId (parent)" },
			{ "UnitId", "RmsRecordParticipant.UnitId" },
			{ "UserId", "RmsRecordParticipant.UserId + DisplayNameSnapshot/GroupIdSnapshot" }
		};

		public static readonly IReadOnlyDictionary<string, string> LogUnit = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			{ "LogUnitId", NotCarried + "legacy row identity" },
			{ "LogId", "RmsRecordUnitResponse.RecordId (parent)" },
			{ "UnitId", "RmsRecordUnitResponse.UnitId + UnitNameSnapshot/UnitTypeSnapshot" },
			{ "Dispatched", "RmsRecordUnitResponse.Dispatched" },
			{ "Enroute", "RmsRecordUnitResponse.Enroute" },
			{ "OnScene", "RmsRecordUnitResponse.OnScene" },
			{ "Released", "RmsRecordUnitResponse.Released" },
			{ "InQuarters", "RmsRecordUnitResponse.InQuarters" }
		};

		public static readonly IReadOnlyDictionary<string, string> LogAttachment = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			{ "LogAttachmentId", NotCarried + "legacy row identity" },
			{ "LogId", "RmsRecordAttachment.RecordId (parent)" },
			{ "FileName", "RmsRecordAttachment.FileName" },
			{ "Type", "RmsRecordAttachment.ContentType" },
			{ "Data", "RmsRecordAttachment.Data + Checksum (SHA-256)" },
			{ "UserId", "RmsRecordAttachment.UploadedByUserId (the acting user)" },
			{ "Timestamp", "RmsRecordAttachment.UploadedOn (server clock)" },
			{ "Size", "RmsRecordAttachment.ByteSize" }
		};

		public static readonly IReadOnlyDictionary<string, string> UnitLog = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			{ "UnitLogId", NotCarried + "legacy row identity" },
			{ "UnitId", "RmsOperationalRecordDetail.UnitId + one RmsRecordUnitResponse (Unit Activity subject)" },
			{ "Timestamp", "RmsOperationalRecordDetail.ActivityOn" },
			{ "Narrative", "RmsOperationalRecordDetail.Narrative" },
			{ "IsProtected", "RmsOperationalRecordDetail.IsProtected / ProtectedEnvelope (inert until enrollment)" }
		};
	}
}
