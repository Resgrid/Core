using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resgrid.Model.Checklists
{
	/// <summary>All content is a cataloged ADP slot. IDs and lifecycle metadata remain queryable.</summary>
	public abstract class ChecklistRow : IEntity
	{
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public object IdValue { get => Id; set => Id = (string)value; }
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public string TableName => ChecklistTables.All[GetType()];
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public string IdName => "Id";
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public int IdType => 1;
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "TableName", "IdName", "IdType", "IgnoredProperties" };
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public int DepartmentId { get; set; }
		public string ParentId { get; set; }
		public string Content { get; set; }
		public int Revision { get; set; } = 1;
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public string CreatedBy { get; set; }
		public bool IsProtected { get; set; }
	}
	public sealed class ChecklistDefinition : ChecklistRow
	{
		public string CurrentVersionId { get; set; }
		public int PublishedVersion { get; set; }
		public bool Retired { get; set; }
		public DateTime? DeletedOn { get; set; }
	}
	public sealed class ChecklistDefinitionVersion : ChecklistRow { public int Version { get; set; } }
	public sealed class ChecklistOccurrence : ChecklistRow
	{
		public string ScheduleId { get; set; }
		public int? ScheduleRevision { get; set; }
		public DateTime? PeriodStartUtc { get; set; }
		public DateTime? WindowEndUtc { get; set; }
		public DateTime? MissedOn { get; set; }
		public string VersionId { get; set; }
		public string CompletionId { get; set; }
		public int TargetType { get; set; }
		public string TargetId { get; set; }
		public int State { get; set; }
	}
	public sealed class ChecklistCompletion : ChecklistRow
	{
		public string VersionId { get; set; }
		public string OccurrenceId { get; set; }
		public int TargetType { get; set; }
		public string TargetId { get; set; }
		public int State { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public int? TargetGroupId { get; set; }
		public string WitnessUserId { get; set; }
		public DateTime? WitnessedOn { get; set; }
		public decimal? Score { get; set; }
		public bool? Passed { get; set; }
		public string ProtectedScoreEnvelope { get; set; }
		public string ProtectedPassedEnvelope { get; set; }
		public string SubmissionHash { get; set; }
	}
	public sealed class ChecklistCompletionItem : ChecklistRow
	{
		public string ItemId { get; set; }
		public bool? IsFailure { get; set; }
		public string ProtectedIsFailureEnvelope { get; set; }
	}
	public sealed class ChecklistCompletionFile : ChecklistRow
	{
		public string ItemId { get; set; }
		public string ContentType { get; set; }
		public int Size { get; set; }
		public string Sha256 { get; set; }
		public byte[] Data { get; set; }
		public int ScanState { get; set; }
	}
	public sealed class DepartmentChecklistSettings : ChecklistRow
	{
		public bool NotifyAtShiftStart { get; set; } = true;
		public int? FixedDigestMinute { get; set; }
		public DateTime? LastDigestSweepUtc { get; set; }
		public DateTime? DigestActiveFromUtc { get; set; }
		public bool RemindersEnabled { get; set; }
		public int NotifyBeforeMinutes { get; set; } = 60;
		public bool NotifyMissed { get; set; } = true;
		public bool DigestMode { get; set; } = true;
		public int? EscalateAfterMinutes { get; set; }
		public DateTime? RemindersActiveFromUtc { get; set; }
	}
	public static class ChecklistTables
	{
		public static readonly IReadOnlyDictionary<Type, string> All = new Dictionary<Type, string>
		{
			[typeof(ChecklistDefinition)] = "ChecklistDefinitions", [typeof(ChecklistDefinitionVersion)] = "ChecklistDefinitionVersions",
			[typeof(ChecklistOccurrence)] = "ChecklistOccurrences", [typeof(ChecklistCompletion)] = "ChecklistCompletions",
			[typeof(ChecklistCompletionItem)] = "ChecklistCompletionItems", [typeof(ChecklistCompletionFile)] = "ChecklistCompletionFiles",
			[typeof(DepartmentChecklistSettings)] = "DepartmentChecklistSettings",
			[typeof(ChecklistSchedule)] = "ChecklistSchedules"
		};
		public static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Fields<T>() where T : ChecklistRow
		{
			var fields = new Dictionary<string, (Func<T, string>, Action<T, string>)>
			{ [All[typeof(T)].ToLowerInvariant() + ".content"] = (x => x.Content, (x, v) => x.Content = v) };
			// Use the same grant/AAD boundary as Content. Revealing clears the in-memory envelope,
			// so a subsequent save seals the current value and clears its queryable column again.
			if (typeof(T) == typeof(ChecklistCompletion))
			{
				fields["checklistcompletions.score"] = (x => ((ChecklistCompletion)(ChecklistRow)x).ProtectedScoreEnvelope ?? ((ChecklistCompletion)(ChecklistRow)x).Score?.ToString(CultureInfo.InvariantCulture),
					(x, v) => { var row = (ChecklistCompletion)(ChecklistRow)x; row.ProtectedScoreEnvelope = Envelope(v); row.Score = decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var score) ? score : null; });
				fields["checklistcompletions.passed"] = (x => ((ChecklistCompletion)(ChecklistRow)x).ProtectedPassedEnvelope ?? ((ChecklistCompletion)(ChecklistRow)x).Passed?.ToString(),
					(x, v) => { var row = (ChecklistCompletion)(ChecklistRow)x; row.ProtectedPassedEnvelope = Envelope(v); row.Passed = Boolean(v); });
			}
			if (typeof(T) == typeof(ChecklistCompletionItem))
				fields["checklistcompletionitems.isfailure"] = (x => ((ChecklistCompletionItem)(ChecklistRow)x).ProtectedIsFailureEnvelope ?? ((ChecklistCompletionItem)(ChecklistRow)x).IsFailure?.ToString(),
					(x, v) => { var row = (ChecklistCompletionItem)(ChecklistRow)x; row.ProtectedIsFailureEnvelope = Envelope(v); row.IsFailure = Boolean(v); });
			return fields;
		}
		private static string Envelope(string value) => ProtectedDataEnvelope.HasEnvelopePrefix(value) ? value : null;
		// Migration readers may represent a database boolean as either text or 0/1.
		private static bool? Boolean(string value) => bool.TryParse(value, out var parsed) ? parsed : value == "1" ? true : value == "0" ? false : null;
	}
}
