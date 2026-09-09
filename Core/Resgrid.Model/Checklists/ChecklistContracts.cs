using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Checklists
{
	public enum ChecklistRunState { InProgress = 0, AwaitingWitness = 1, Submitted = 2 }
	public enum ChecklistAnswerStatus { Unanswered = 0, Answered = 1, NotApplicable = 2 }

	public sealed class ChecklistForm
	{
		public string Name { get; set; }
		public string Instructions { get; set; }
		public ChecklistCategory Category { get; set; }
		public ChecklistTargetType TargetType { get; set; }
		public decimal PassThreshold { get; set; } = 100;
		public bool RequireLocation { get; set; }
		public bool RequiresIndependentWitness { get; set; }
		public List<ChecklistSection> Sections { get; set; } = new List<ChecklistSection>();
		public static ChecklistForm FromTemplate(ChecklistTemplate template, bool assetsAvailable = false)
		{
			var form = new ChecklistForm
			{
			Name = template.Name, Instructions = template.Description, Category = template.SuggestedCategory,
			TargetType = !assetsAvailable && template.SuggestedTargetType == ChecklistTargetType.InventoryAsset ? ChecklistTargetType.Department : template.SuggestedTargetType,
			RequiresIndependentWitness = template.RequiresIndependentWitness,
			Sections = template.Sections.Select(s => new ChecklistSection { Name = s.Name,
				Items = s.Items.Select(i => new ChecklistItem { Name = i.Name, Type = i.Type, Required = i.Required,
					Critical = i.Critical, AllowNotApplicable = i.AllowNotApplicable, RequireNoteOnFail = i.RequireNoteOnFail, Weight = i.Type == ChecklistItemType.FreeText && !i.Required ? 0 : 1 }).ToList() }).ToList()
			};
			// Without serialized inventory, retain a required equipment identifier and responsible target.
			if (!assetsAvailable && template.SuggestedTargetType == ChecklistTargetType.InventoryAsset)
				form.Sections[0].Items.Insert(0, new ChecklistItem { Name = "Equipment identifier", Instructions = "Record the equipment label or serial number.", Type = ChecklistItemType.FreeText, Required = true, Weight = 0, RequireNoteOnFail = false });
			return form;
		}
	}
	public sealed class ChecklistSection
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public string Name { get; set; }
		public List<ChecklistItem> Items { get; set; } = new List<ChecklistItem>();
	}
	public sealed class ChecklistCondition
	{
		/// <summary>Only earlier items can be referenced; the evaluator never executes expressions.</summary>
		public string ItemId { get; set; }
		public string EqualsValue { get; set; }
	}
	public sealed class ChecklistItem
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public string Name { get; set; }
		public string Instructions { get; set; }
		public ChecklistItemType Type { get; set; }
		public bool Required { get; set; } = true;
		public bool Critical { get; set; }
		public bool AllowNotApplicable { get; set; }
		public bool RequireNoteOnFail { get; set; } = true;
		public bool RequirePhotoOnFail { get; set; }
		public decimal Weight { get; set; } = 1;
		public string Units { get; set; }
		public decimal? Minimum { get; set; }
		public decimal? Maximum { get; set; }
		/// <summary>YesNo, Checkbox and SelectList use this explicit passing value.</summary>
		public string PassingValue { get; set; } = "true";
		public List<string> Options { get; set; } = new List<string>();
		public ChecklistCondition VisibleWhen { get; set; }
		public ChecklistCondition RequiredWhen { get; set; }
	}
	public sealed class ChecklistAnswer
	{
		public string ItemId { get; set; }
		public ChecklistAnswerStatus Status { get; set; }
		public string Value { get; set; }
		public string Note { get; set; }
		public string NotApplicableReason { get; set; }
	}
	public sealed class ChecklistRunInput
	{
		public int Revision { get; set; }
		public List<ChecklistAnswer> Answers { get; set; } = new List<ChecklistAnswer>();
		public string Note { get; set; }
		public string LocationDescription { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public DateTime? ClientCompletedOn { get; set; }
	}
	public sealed class ChecklistEvaluation
	{
		public decimal? Score { get; set; }
		public bool Passed { get; set; }
		public List<string> FailedItemIds { get; set; } = new List<string>();
		public List<string> Errors { get; set; } = new List<string>();
	}
	public sealed class ChecklistTarget { public ChecklistTargetType Type { get; set; } public string Id { get; set; } public string Name { get; set; } public int? GroupId { get; set; } }
	public sealed class ChecklistActor { public int DepartmentId { get; set; } public string UserId { get; set; } public string GrantToken { get; set; } }
	public sealed class ChecklistDefinitionView { public ChecklistDefinition Definition { get; set; } public ChecklistForm Form { get; set; } public ChecklistForm PublishedForm { get; set; } }
	public sealed class ChecklistHistoryEntry { public ChecklistCompletion Completion { get; set; } public string TargetName { get; set; } }
	public sealed class ChecklistRunView
	{
		public int VersionNumber { get; set; }
		public string WitnessAttestation { get; set; }
		public ChecklistCompletion Completion { get; set; }
		public ChecklistForm Form { get; set; }
		public ChecklistTarget Target { get; set; }
		public ChecklistRunInput Input { get; set; }
		public List<ChecklistCompletionFile> Files { get; set; } = new List<ChecklistCompletionFile>();
	}
	public sealed class ChecklistException : Exception
	{
		public int StatusCode { get; }
		public ChecklistException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
	}
}
