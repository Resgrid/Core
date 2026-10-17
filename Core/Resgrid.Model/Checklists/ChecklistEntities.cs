using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace Resgrid.Model.Checklists
{
	/// <summary>Free text about people lives in a cataloged slot: ProtectedContent on a mixed table, Content on a wholly private one. IDs and lifecycle metadata remain queryable.</summary>
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
	/// <summary>
	/// A mixed table (ADP catalog review §8, option a): <c>Content</c> keeps plaintext metadata and <c>ProtectedContent</c>,
	/// the row's ADP slot, keeps the private members listed in <see cref="ChecklistTables.PrivateMembers"/>.
	/// </summary>
	public abstract class ChecklistSplitRow : ChecklistRow, ISplitContentRow
	{
		public string ProtectedContent { get; set; }
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public IReadOnlyCollection<string> PrivateMemberPaths => ChecklistTables.PrivatePaths(TableName, Content);
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public IReadOnlyCollection<string> PrivateTextMemberPaths => ChecklistTables.PrivatePaths(TableName, Content);
	}
	public sealed class ChecklistOccurrence : ChecklistSplitRow
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
	public sealed class ChecklistCompletion : ChecklistSplitRow
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
	public sealed class ChecklistCompletionItem : ChecklistSplitRow
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
		/// <summary>
		/// Tables whose Content can hold free text about people (skip reasons, completion notes, witness attestations,
		/// free-text answers, signatures, photo file names) and stays ADP-protected. Definitions and their versions are
		/// configuration, and department settings never use Content; they are not protected (2026-10-17).
		/// </summary>
		public static readonly IReadOnlyCollection<string> ProtectedContent = new HashSet<string>(StringComparer.Ordinal)
		{
			"ChecklistOccurrences", "ChecklistCompletions", "ChecklistCompletionItems", "ChecklistCompletionFiles", "ChecklistSchedules"
		};
		public static bool IsProtected(Type type) => ProtectedContent.Contains(All[type]);
		/// <summary>
		/// The private members of each mixed table's Content (ADP catalog review §8, option a, 2026-10-17): skip reasons,
		/// completion notes, witness attestations, item notes and not-applicable reasons, and the value of a free-text,
		/// photo or signature answer. They are stored, and sealed, in ProtectedContent; the rest of Content (target
		/// snapshot, location description and coordinates, item status, typed answers, schedule names) is plaintext
		/// metadata. Completion files stay wholly protected in Content. M0274 split the existing rows with a frozen copy
		/// of this map: change it only together with a migration.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string[]> PrivateMembers = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["ChecklistOccurrences"] = new[] { "Reason" },
			["ChecklistCompletions"] = new[] { "Note", "WitnessAttestation" },
			["ChecklistCompletionItems"] = new[] { "Value", "Note", "NotApplicableReason" },
			["ChecklistSchedules"] = new[] { "Notes" }
		};
		/// <summary>Completion item member naming the answered item's type; written with the answer so the split can tell typed values from free text.</summary>
		public const string AnswerValueTypeMember = "ValueType";
		private static readonly HashSet<int> TypedAnswers = new HashSet<int>
		{
			(int)ChecklistItemType.PassFail, (int)ChecklistItemType.YesNo, (int)ChecklistItemType.Checkbox, (int)ChecklistItemType.NumericReading,
			(int)ChecklistItemType.Quantity, (int)ChecklistItemType.SelectList, (int)ChecklistItemType.DateValue
		};
		private static readonly string[] TypedAnswerPaths = { "Note", "NotApplicableReason" };
		/// <summary>
		/// A completion item's Value is private unless the answer records a typed item (pass/fail, yes/no, checkbox,
		/// reading, quantity, list choice or date), whose value the validator constrains. A free-text, photo or signature
		/// answer, and one stored before the type was recorded, keeps its Value private.
		/// </summary>
		public static IReadOnlyCollection<string> PrivatePaths(string table, string content = null)
		{
			if (table == null || !PrivateMembers.TryGetValue(table, out var paths))
				return Array.Empty<string>();
			return table == "ChecklistCompletionItems" && IsTypedAnswer(content) ? TypedAnswerPaths : paths;
		}
		/// <summary>The stored form of an answer: the answer itself plus the item type it was given for.</summary>
		public static string StoredAnswer(ChecklistAnswer answer, ChecklistItemType? type) => Newtonsoft.Json.JsonConvert.SerializeObject(new
		{
			answer.ItemId, answer.Status, answer.Value, answer.Note, answer.NotApplicableReason, ValueType = (int?)type
		});
		private static bool IsTypedAnswer(string content)
		{
			if (string.IsNullOrEmpty(content) || content[0] != '{' || content.IndexOf(AnswerValueTypeMember, StringComparison.Ordinal) < 0)
				return false;
			try
			{
				var type = Newtonsoft.Json.Linq.JObject.Parse(content)[AnswerValueTypeMember];
				return type != null && type.Type == Newtonsoft.Json.Linq.JTokenType.Integer && TypedAnswers.Contains((int)type);
			}
			catch (Newtonsoft.Json.JsonReaderException)
			{
				return false;
			}
		}
		/// <summary>The column a protected table seals: ProtectedContent for a mixed table, Content for a wholly private one.</summary>
		public static string SealedColumn(string table) => PrivateMembers.ContainsKey(table) ? "ProtectedContent" : "Content";
		public static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Fields<T>() where T : ChecklistRow
		{
			var fields = new Dictionary<string, (Func<T, string>, Action<T, string>)>();
			var table = All[typeof(T)];
			if (IsProtected(typeof(T)) && PrivateMembers.ContainsKey(table))
				fields[table.ToLowerInvariant() + ".protectedcontent"] = (x => ((ISplitContentRow)x).ProtectedContent, (x, v) => ((ISplitContentRow)x).ProtectedContent = v);
			else if (IsProtected(typeof(T)))
				fields[table.ToLowerInvariant() + ".content"] = (x => x.Content, (x, v) => x.Content = v);
			// Completion Score / Passed and item IsFailure are outcome state, not private, and stay in their queryable
			// columns (catalog 15 retired 2026-10-17); only the private members (and file Content and Data) are sealed.
			return fields;
		}
	}
}
