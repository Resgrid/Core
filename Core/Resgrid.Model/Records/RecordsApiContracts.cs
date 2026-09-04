using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The v4 Records client contract (RMS plan sections 5.3, 5.4, 5.9.1): a versioned capability manifest, ETag
	/// conflicts that name the changed field paths, scoped idempotency for creates and commands, a delta cursor with
	/// tombstones, and resumable, checksummed attachment sessions. Web and the field apps share this contract.
	/// </summary>
	public static class RecordsApiContract
	{
		/// <summary>Bumped when a field or rule type is added that older clients cannot render; the manifest carries it.</summary>
		public const string Version = "records.v1";

		/// <summary>The minimum capability every locked Logs-parity definition needs; department definitions (RMS-1B designer) derive their own.</summary>
		public const string LockedDefinitionCapability = "records.v1";

		public const string ETagHeader = "ETag";
		public const string IfMatchHeader = "If-Match";
		public const string IdempotencyKeyHeader = "Idempotency-Key";
		public const string ClientHeader = "X-Resgrid-Client";

		/// <summary>Weak ETag from a row version: <c>W/"7"</c>.</summary>
		public static string ToETag(long rowVersion) => $"W/\"{rowVersion}\"";

		/// <summary>Parses <c>W/"7"</c>, <c>"7"</c> or <c>7</c>; null when absent or malformed.</summary>
		public static long? ParseETag(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var text = value.Trim();
			if (text.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
				text = text.Substring(2);
			text = text.Trim('"', ' ');
			return long.TryParse(text, out var parsed) && parsed >= 0 ? parsed : (long?)null;
		}
	}

	/// <summary>One field of a locked definition as the manifest describes it.</summary>
	public class RecordFieldDescriptor
	{
		public string Key { get; set; }
		public string Section { get; set; }
		/// <summary>text, longtext, datetime, int, user, unit, bool.</summary>
		public string Type { get; set; }
		public bool Required { get; set; }
		public bool RequiredToFinalize { get; set; }
		/// <summary>Restricted-section field: withheld without RecordRestricted_View, never in search or summaries.</summary>
		public bool Restricted { get; set; }
	}

	/// <summary>A published definition as the capability manifest lists it (plan section 5.4).</summary>
	public class RecordDefinitionDescriptor
	{
		public string Key { get; set; }
		public int Version { get; set; }
		public string Name { get; set; }
		/// <summary>RmsOperationalRecordType for the locked Logs-parity definitions; null for the NERIS incident report.</summary>
		public int? RecordType { get; set; }
		public string RecordKind { get; set; }
		public int LifecyclePreset { get; set; }
		public string LifecyclePresetName { get; set; }
		public string Cardinality { get; set; }
		public bool Restricted { get; set; }
		public string NumberPrefix { get; set; }
		public bool RequiresCall { get; set; }
		public bool SupportsParticipants { get; set; }
		public bool SupportsUnits { get; set; }
		public bool SupportsAttachments { get; set; }
		public string MinimumClientCapability { get; set; }
		public bool Locked { get; set; } = true;
		public List<RecordFieldDescriptor> Fields { get; set; } = new List<RecordFieldDescriptor>();
	}

	/// <summary>
	/// The locked definition catalog the manifest publishes. Field applicability mirrors the Records authoring form:
	/// a narrative on every type, the type-specific columns, and the Coroner restricted section.
	/// </summary>
	public static class RecordDefinitionCatalog
	{
		public static List<RecordDefinitionDescriptor> Describe()
		{
			var list = new List<RecordDefinitionDescriptor>();
			foreach (var kv in RmsDefinitionKeys.LockedTypes)
			{
				var type = kv.Value;
				var restricted = RmsDefinitionKeys.RestrictedClass.Contains(kv.Key);
				var descriptor = new RecordDefinitionDescriptor
				{
					Key = kv.Key,
					Version = RmsDefinitionKeys.LockedDefinitionVersion,
					Name = type.ToString(),
					RecordType = (int)type,
					RecordKind = RmsRecordKind.Operational.ToString(),
					LifecyclePreset = (int)RmsDefinitionKeys.LockedDefaultPreset,
					LifecyclePresetName = RmsDefinitionKeys.LockedDefaultPreset.ToString(),
					Cardinality = RmsDefinitionKeys.CardinalityFor(kv.Key).ToString(),
					Restricted = restricted,
					NumberPrefix = RmsDefinitionKeys.DefaultNumberPrefix(kv.Key),
					RequiresCall = false,
					SupportsParticipants = type != RmsOperationalRecordType.UnitActivity,
					SupportsUnits = true,
					SupportsAttachments = true,
					MinimumClientCapability = RecordsApiContract.LockedDefinitionCapability
				};
				descriptor.Fields.AddRange(FieldsFor(type));
				list.Add(descriptor);
			}

			list.Add(new RecordDefinitionDescriptor
			{
				Key = RmsDefinitionKeys.NerisIncidentReport,
				Version = RmsDefinitionKeys.LockedDefinitionVersion,
				Name = "NERIS incident report",
				RecordType = null,
				RecordKind = RmsRecordKind.IncidentReport.ToString(),
				LifecyclePreset = (int)RmsDefinitionKeys.LockedDefaultPreset,
				LifecyclePresetName = RmsDefinitionKeys.LockedDefaultPreset.ToString(),
				Cardinality = RmsDefinitionKeys.CardinalityFor(RmsDefinitionKeys.NerisIncidentReport).ToString(),
				Restricted = false,
				NumberPrefix = "INC",
				RequiresCall = true,
				SupportsParticipants = false,
				SupportsUnits = true,
				SupportsAttachments = false,
				MinimumClientCapability = RecordsApiContract.LockedDefinitionCapability,
				Fields = new List<RecordFieldDescriptor>
				{
					F("IncidentNumber", "Dispatch", "text", true, true), F("CallCreatedOn", "Dispatch", "datetime", true, true), F("CallAnsweredOn", "Dispatch", "datetime", true, true),
					F("CallArrivalOn", "Dispatch", "datetime", false, true), F("IncidentClearedOn", "Dispatch", "datetime"), F("DispatchIncidentCode", "Dispatch", "text"),
					F("Location", "Location", "location", true, true), F("Types", "Incident", "codes", true, true), F("Units", "Units", "unit-responses", true, true),
					F("Aids", "Incident", "aids"), F("Tactics", "Incident", "tactics"), F("Narrative", "Narrative", "longtext"), F("SpecialModifiers", "Incident", "codes")
				}
			});

			return list;
		}

		public static List<RecordFieldDescriptor> FieldsFor(RmsOperationalRecordType type)
		{
			var fields = new List<RecordFieldDescriptor>
			{
				F("StartedOn", "Record", "datetime"), F("EndedOn", "Record", "datetime"), F("StationGroupId", "Record", "group"), F("ExternalId", "Record", "text"),
				F("Narrative", "Details", "longtext", false, true)
			};
			switch (type)
			{
				case RmsOperationalRecordType.Training:
					fields.AddRange(new[] { F("Course", "Details", "text"), F("CourseCode", "Details", "text"), F("Instructors", "Details", "text") });
					break;
				case RmsOperationalRecordType.Meeting:
					fields.AddRange(new[] { F("Type", "Details", "text"), F("Facilitator", "Details", "text") });
					break;
				case RmsOperationalRecordType.Run:
				case RmsOperationalRecordType.Callback:
					fields.AddRange(new[] { F("CallId", "Record", "call"), F("InitialReport", "Details", "text"), F("Cause", "Details", "text"), F("InvestigatedByUserId", "Details", "user"), F("OtherAgencies", "Details", "text"), F("OtherUnits", "Details", "text") });
					break;
				case RmsOperationalRecordType.UnitActivity:
					fields.AddRange(new[] { F("UnitId", "Details", "unit", true, true), F("ActivityOn", "Details", "datetime", false, true) });
					break;
				case RmsOperationalRecordType.Coroner:
					fields.AddRange(new[]
					{
						F("CaseNumber", "Restricted", "text", false, false, true), F("PronouncedDeceasedBy", "Restricted", "text", false, false, true), F("BodyLocation", "Restricted", "text", false, false, true),
						F("Destination", "Restricted", "text", false, false, true), F("OtherPersonnel", "Restricted", "text", false, false, true), F("ContactName", "Restricted", "text"), F("ContactNumber", "Restricted", "text")
					});
					break;
			}
			if (type != RmsOperationalRecordType.Coroner)
				fields.AddRange(new[] { F("Location", "Details", "text"), F("OtherPersonnel", "Details", "text") });
			return fields;
		}

		private static RecordFieldDescriptor F(string key, string section, string type, bool required = false, bool requiredToFinalize = false, bool restricted = false)
		{
			return new RecordFieldDescriptor { Key = key, Section = section, Type = type, Required = required, RequiredToFinalize = requiredToFinalize, Restricted = restricted };
		}
	}

	/// <summary>Draft save refused because the client's row version is stale: what changed, so the client reconciles deliberately (plan section 5.3).</summary>
	public class RecordDraftConflict
	{
		public string RecordId { get; set; }
		public long ExpectedRowVersion { get; set; }
		public long CurrentRowVersion { get; set; }
		public RmsRecordState CurrentState { get; set; }
		public string CurrentRevisionId { get; set; }
		public List<string> ChangedFieldPaths { get; set; } = new List<string>();
	}

	public enum RecordUploadSessionState
	{
		Open = 1,
		Completed = 2,
		Aborted = 3
	}

	/// <summary>A resumable attachment upload (plan section 5.3): declared size and SHA-256 up front, chunks in order, finalize only after checksum, hygiene and scanning pass.</summary>
	public class RecordAttachmentUploadSession
	{
		public string UploadId { get; set; }
		public int DepartmentId { get; set; }
		public string RecordId { get; set; }
		public string UserId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long DeclaredSize { get; set; }
		/// <summary>Lower-case hex SHA-256 of the whole file, declared by the client and verified at completion.</summary>
		public string Sha256 { get; set; }
		public long ReceivedBytes { get; set; }
		public int ChunkSize { get; set; }
		public int ChunkCount { get; set; }
		public RecordUploadSessionState State { get; set; } = RecordUploadSessionState.Open;
		public string AttachmentId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public bool IsComplete => ReceivedBytes >= DeclaredSize;
	}

	public class RecordUploadSessionException : InvalidOperationException
	{
		public RecordUploadSessionException(string code, string message) : base(message)
		{
			Code = code;
		}

		/// <summary>not_found, expired, closed, bad_offset, too_large, checksum_mismatch, incomplete, rejected.</summary>
		public string Code { get; }
	}
}

namespace Resgrid.Model.Services
{
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>Short-lived, keyed state behind the v4 Records contract (upload sessions, command idempotency). Redis when caching is on; process memory otherwise.</summary>
	public interface IRecordsApiStateStore
	{
		Task<string> GetAsync(string key);
		Task SetAsync(string key, string value, TimeSpan timeToLive);
		Task RemoveAsync(string key);
	}

	/// <summary>Resumable, checksummed attachment sessions for Records (plan section 5.3).</summary>
	public interface IRecordAttachmentUploadService
	{
		int ChunkSize { get; }
		Task<RecordAttachmentUploadSession> BeginAsync(int departmentId, string userId, string recordId, string fileName, string contentType, long declaredSize, string sha256);
		Task<RecordAttachmentUploadSession> GetAsync(int departmentId, string userId, string uploadId);
		Task<RecordAttachmentUploadSession> AppendAsync(int departmentId, string userId, string uploadId, long offset, byte[] data);
		/// <summary>Verifies size and SHA-256, then stores the attachment through the same hygiene/scanner path as a direct upload.</summary>
		Task<RmsRecordAttachment> CompleteAsync(int departmentId, string userId, string uploadId, string description, CancellationToken cancellationToken = default);
		Task<bool> AbortAsync(int departmentId, string userId, string uploadId);
	}

	/// <summary>Scoped idempotency for v4 Records commands: the same (department, user, key) replays the first outcome instead of re-running the transition.</summary>
	public interface IRecordsApiIdempotencyService
	{
		Task<string> TryGetRecordIdAsync(int departmentId, string userId, string idempotencyKey);
		Task RememberAsync(int departmentId, string userId, string idempotencyKey, string recordId);
	}
}
