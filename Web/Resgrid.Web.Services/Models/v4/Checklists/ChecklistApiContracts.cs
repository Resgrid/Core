using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Checklists;

namespace Resgrid.Web.Services.Models.v4.Checklists
{
	public sealed class ChecklistApiResult<T> : StandardApiResponseV4Base
	{
		public T Data { get; set; }
		public bool HasMore { get; set; }
		public int ContractVersion { get; set; } = 1;
	}
	public class ChecklistCommandInput { public string Id { get; set; } public int Revision { get; set; } }
	public sealed class ChecklistDefinitionInput : ChecklistCommandInput { public ChecklistForm Form { get; set; } }
	public sealed class ChecklistStartInput
	{
		public string OccurrenceId { get; set; }
		public string DefinitionId { get; set; }
		public string VersionId { get; set; }
		public string TargetId { get; set; }
		public string CompletionId { get; set; }
	}
	public sealed class ChecklistProgressInput { public string Id { get; set; } public ChecklistRunInput Input { get; set; } }
	public sealed class ChecklistWitnessInput { public string Id { get; set; } public string SubmissionHash { get; set; } public string Attestation { get; set; } }
	public sealed class ChecklistSkipInput : ChecklistCommandInput { public string Reason { get; set; } }
	public sealed class ChecklistEvidenceInput : ChecklistCommandInput
	{
		public string ItemId { get; set; }
		public string Name { get; set; }
		public string ContentType { get; set; }
		public byte[] Data { get; set; }
	}
	public sealed class ChecklistDefinitionData
	{
		public string Id { get; set; }
		public int Revision { get; set; }
		public string VersionId { get; set; }
		public int VersionNumber { get; set; }
		public bool Retired { get; set; }
		public bool IsProtected { get; set; }
		public DateTime UpdatedOn { get; set; }
		public ChecklistForm Form { get; set; }
		public ChecklistForm PublishedForm { get; set; }
		public List<ChecklistTarget> Targets { get; set; }
		public static ChecklistDefinitionData From(ChecklistDefinitionView view) => new ChecklistDefinitionData { Id = view.Definition.Id, Revision = view.Definition.Revision,
			VersionId = view.Definition.CurrentVersionId, VersionNumber = view.Definition.PublishedVersion, Retired = view.Definition.Retired,
			IsProtected = view.Definition.IsProtected, UpdatedOn = view.Definition.UpdatedOn, Form = view.Form, PublishedForm = view.PublishedForm };
	}
	public sealed class ChecklistFileData
	{
		public string Id { get; set; }
		public string ItemId { get; set; }
		public string Name { get; set; }
		public string ContentType { get; set; }
		public int Size { get; set; }
		public int ScanState { get; set; }
		public static ChecklistFileData From(ChecklistCompletionFile f) => new ChecklistFileData { Id = f.Id, ItemId = f.ItemId, Name = f.Content, ContentType = f.ContentType, Size = f.Size, ScanState = f.ScanState };
	}
	public sealed class ChecklistRunData
	{
		public string Id { get; set; }
		public string OccurrenceId { get; set; }
		public string DefinitionId { get; set; }
		public string VersionId { get; set; }
		public int VersionNumber { get; set; }
		public int Revision { get; set; }
		public int State { get; set; }
		public bool IsProtected { get; set; }
		public bool IsPreview { get; set; }
		public bool CanEdit { get; set; }
		public string CreatedBy { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public string SubmissionHash { get; set; }
		public string WitnessUserId { get; set; }
		public string WitnessAttestation { get; set; }
		public decimal? Score { get; set; }
		public bool? Passed { get; set; }
		public ChecklistTarget Target { get; set; }
		public ChecklistForm Form { get; set; }
		public ChecklistRunInput Input { get; set; }
		public List<ChecklistFileData> Files { get; set; }
		public static ChecklistRunData From(ChecklistRunView v, string userId) => new ChecklistRunData { Id = v.Completion.Id, OccurrenceId = v.Completion.OccurrenceId, DefinitionId = v.Completion.ParentId,
			VersionId = v.Completion.VersionId, VersionNumber = v.VersionNumber, Revision = v.Completion.Revision, State = v.Completion.State, IsProtected = v.Completion.IsProtected,
			IsPreview = v.Completion.Revision == 0, CanEdit = v.Completion.State == 0 && v.Completion.CreatedBy == userId, CreatedBy = v.Completion.CreatedBy,
			UpdatedOn = v.Completion.UpdatedOn, SubmittedOn = v.Completion.SubmittedOn, SubmissionHash = v.Completion.SubmissionHash, WitnessUserId = v.Completion.WitnessUserId,
			WitnessAttestation = v.WitnessAttestation, Score = v.Completion.Score, Passed = v.Completion.Passed, Target = v.Target, Form = v.Form, Input = v.Input, Files = v.Files.Select(ChecklistFileData.From).ToList() };
	}
	public sealed class ChecklistDueData
	{
		public string Id { get; set; }
		public string CompletionId { get; set; }
		public string DefinitionId { get; set; }
		public string VersionId { get; set; }
		public string Name { get; set; }
		public ChecklistTarget Target { get; set; }
		public int State { get; set; }
		public int Revision { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime? PeriodStartUtc { get; set; }
		public DateTime? WindowEndUtc { get; set; }
		public bool CanStart { get; set; }
		public static ChecklistDueData From(ChecklistOccurrenceView v) => new ChecklistDueData { Id = v.Occurrence.Id, CompletionId = v.Occurrence.CompletionId, DefinitionId = v.Occurrence.ParentId,
			VersionId = v.Occurrence.VersionId, Name = v.Name, Target = v.Target, State = v.Occurrence.State, Revision = v.Occurrence.Revision, UpdatedOn = v.Occurrence.UpdatedOn,
			PeriodStartUtc = v.Occurrence.PeriodStartUtc, WindowEndUtc = v.Occurrence.WindowEndUtc, CanStart = v.CanStart };
	}
	public sealed class ChecklistDuePageData
	{
		public List<ChecklistDueData> Occurrences { get; set; }
		public List<ChecklistDefinitionData> Definitions { get; set; }
		public bool HasMoreOccurrences { get; set; }
		public bool HasMoreDefinitions { get; set; }
	}
	public sealed class ChecklistHistoryData
	{
		public string Id { get; set; }
		public string DefinitionId { get; set; }
		public string VersionId { get; set; }
		public int TargetType { get; set; }
		public string TargetId { get; set; }
		public string TargetName { get; set; }
		public string CreatedBy { get; set; }
		public int State { get; set; }
		public int Revision { get; set; }
		public bool IsProtected { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public decimal? Score { get; set; }
		public bool? Passed { get; set; }
		public static ChecklistHistoryData From(ChecklistHistoryEntry v) => new ChecklistHistoryData { Id = v.Completion.Id, DefinitionId = v.Completion.ParentId, VersionId = v.Completion.VersionId,
			TargetType = v.Completion.TargetType, TargetId = v.Completion.TargetId, TargetName = v.TargetName, CreatedBy = v.Completion.CreatedBy, State = v.Completion.State, Revision = v.Completion.Revision,
			IsProtected = v.Completion.IsProtected, UpdatedOn = v.Completion.UpdatedOn, SubmittedOn = v.Completion.SubmittedOn, Score = v.Completion.Score, Passed = v.Completion.Passed };
	}
}
