using System;
using System.Collections.Generic;

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
		public bool Passed { get; set; }
		public string SubmissionHash { get; set; }
	}
	public sealed class ChecklistCompletionItem : ChecklistRow { public string ItemId { get; set; } public bool IsFailure { get; set; } }
	public sealed class ChecklistCompletionFile : ChecklistRow
	{
		public string ItemId { get; set; }
		public string ContentType { get; set; }
		public int Size { get; set; }
		public string Sha256 { get; set; }
		public byte[] Data { get; set; }
		public int ScanState { get; set; }
	}
	public sealed class DepartmentChecklistSettings : ChecklistRow { }
	public static class ChecklistTables
	{
		public static readonly IReadOnlyDictionary<Type, string> All = new Dictionary<Type, string>
		{
			[typeof(ChecklistDefinition)] = "ChecklistDefinitions", [typeof(ChecklistDefinitionVersion)] = "ChecklistDefinitionVersions",
			[typeof(ChecklistOccurrence)] = "ChecklistOccurrences", [typeof(ChecklistCompletion)] = "ChecklistCompletions",
			[typeof(ChecklistCompletionItem)] = "ChecklistCompletionItems", [typeof(ChecklistCompletionFile)] = "ChecklistCompletionFiles",
			[typeof(DepartmentChecklistSettings)] = "DepartmentChecklistSettings"
		};
		public static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Fields<T>() where T : ChecklistRow =>
			new Dictionary<string, (Func<T, string>, Action<T, string>)> { [All[typeof(T)].ToLowerInvariant() + ".content"] = (x => x.Content, (x, v) => x.Content = v) };
	}
}
