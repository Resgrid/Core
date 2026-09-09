using System;
using System.Collections.Generic;

namespace Resgrid.Model.Checklists
{
	public sealed class ChecklistMobileQuery
	{
		public int Page { get; set; }
		public string UnitId { get; set; }
		public string AssetId { get; set; }
		public bool ForCurrentUser { get; set; }
		public string DefinitionId { get; set; }
		public string UserId { get; set; }
		public DateTime? Start { get; set; }
		public DateTime? End { get; set; }
	}
	public sealed class ChecklistMobileDefinition
	{
		public ChecklistDefinitionView Definition { get; set; }
		public List<ChecklistTarget> Targets { get; set; } = new List<ChecklistTarget>();
	}
	public sealed class ChecklistMobilePage
	{
		public List<ChecklistOccurrenceView> Occurrences { get; set; } = new List<ChecklistOccurrenceView>();
		public List<ChecklistMobileDefinition> Definitions { get; set; } = new List<ChecklistMobileDefinition>();
		public bool HasMoreOccurrences { get; set; }
		public bool HasMoreDefinitions { get; set; }
	}
}
