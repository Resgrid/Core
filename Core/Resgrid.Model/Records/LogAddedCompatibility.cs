using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Events;

namespace Resgrid.Model
{
	/// <summary>
	/// The legacy <c>LogAdded</c> workflow compatibility projection (RMS plan section 5.6). During the migration
	/// window an eligible Logs-parity Record projects the exact, pre-existing <c>log.*</c> contract once, at
	/// finalize, through the DomainEventOutbox, so LogAdded workflows keep firing without a legacy Logs row ever
	/// being written. Unit Activity never emitted LogAdded and does not start now; department definitions,
	/// incident reports and amendments never use this path.
	/// </summary>
	public static class LogAddedCompatibility
	{
		public static readonly IReadOnlyCollection<RmsOperationalRecordType> EligibleTypes = new HashSet<RmsOperationalRecordType>
		{
			RmsOperationalRecordType.Run,
			RmsOperationalRecordType.Training,
			RmsOperationalRecordType.Work,
			RmsOperationalRecordType.Meeting,
			RmsOperationalRecordType.Coroner,
			RmsOperationalRecordType.Callback
		};

		public static bool IsEligible(RmsOperationalRecord record)
		{
			if (record == null || string.IsNullOrWhiteSpace(record.DefinitionKey))
				return false;

			return RmsDefinitionKeys.LockedTypes.TryGetValue(record.DefinitionKey, out var type) && EligibleTypes.Contains(type);
		}

		/// <summary>
		/// Builds the LogAddedEvent-shaped payload. <c>LogId</c> is 0 because no legacy row exists; every other
		/// documented <c>log.*</c> field is filled from the Record snapshot. Restricted-section fields
		/// (BodyLocation, PronouncedDeceasedBy, CaseNumber, Destination) are not part of the legacy contract and
		/// are deliberately never copied.
		/// </summary>
		public static LogAddedEvent Build(RmsOperationalRecord record, RmsOperationalRecordDetail details, DateTime loggedOn)
		{
			if (record == null)
				throw new ArgumentNullException(nameof(record));

			var type = (RmsOperationalRecordType)record.RecordType.GetValueOrDefault();

			return new LogAddedEvent
			{
				DepartmentId = record.DepartmentId,
				Log = new Log
				{
					LogId = 0,
					DepartmentId = record.DepartmentId,
					LogType = (int)type,
					Type = type.ToString(),
					Narrative = details?.Narrative,
					ExternalId = record.ExternalId,
					InitialReport = details?.InitialReport,
					StationGroupId = record.StationGroupId,
					Course = details?.Course,
					CourseCode = details?.CourseCode,
					Instructors = details?.Instructors,
					Cause = details?.Cause,
					InvestigatedByUserId = details?.InvestigatedByUserId,
					ContactName = details?.ContactName,
					ContactNumber = details?.ContactNumber,
					StartedOn = record.StartedOn,
					EndedOn = record.EndedOn,
					LoggedOn = record.FinalizedOn ?? loggedOn,
					LoggedByUserId = record.AuthorUserId,
					CallId = record.CallId,
					OtherPersonnel = details?.OtherPersonnel,
					Location = details?.Location,
					OtherAgencies = details?.OtherAgencies,
					OtherUnits = details?.OtherUnits
				}
			};
		}
	}
}
