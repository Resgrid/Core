using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Checklists
{
	/// <summary>Immutable starter content. Applying a template must create new department-owned IDs.</summary>
	public sealed class ChecklistTemplate
	{
		public string Id { get; }
		public string Name { get; }
		public string Sector { get; }
		public string Description { get; }
		public ChecklistCategory SuggestedCategory { get; }
		public ChecklistScheduleFrequency SuggestedFrequency { get; }
		public ChecklistTargetType SuggestedTargetType { get; }
		public bool RequiresIndependentWitness { get; }
		public IReadOnlyList<ChecklistTemplateSection> Sections { get; }
		public IReadOnlyList<string> Keywords { get; }

		[JsonIgnore]
		public string SearchText { get; }

		public ChecklistTemplate(string id, string name, string sector, string description,
			ChecklistCategory category, ChecklistScheduleFrequency frequency, ChecklistTargetType target,
			bool requiresIndependentWitness, IEnumerable<string> keywords, IEnumerable<ChecklistTemplateSection> sections)
		{
			Id = id;
			Name = name;
			Sector = sector;
			Description = description;
			SuggestedCategory = category;
			SuggestedFrequency = frequency;
			SuggestedTargetType = target;
			RequiresIndependentWitness = requiresIndependentWitness;
			Keywords = keywords.ToList().AsReadOnly();
			Sections = sections.ToList().AsReadOnly();
			SearchText = string.Join(" ", new[] { Name, Sector, Description }
				.Concat(Keywords).Concat(Sections.SelectMany(s => s.Items).Select(i => i.Name))).ToLowerInvariant();
		}
	}

	public sealed class ChecklistTemplateSection
	{
		public string SectionId { get; }
		public string Name { get; }
		public IReadOnlyList<ChecklistTemplateItem> Items { get; }

		public ChecklistTemplateSection(string sectionId, string name, IEnumerable<ChecklistTemplateItem> items)
		{
			SectionId = sectionId;
			Name = name;
			Items = items.ToList().AsReadOnly();
		}
	}

	public sealed class ChecklistTemplateItem
	{
		public string ItemId { get; }
		public string Name { get; }
		public ChecklistItemType Type { get; }
		public bool Required { get; }
		public bool Critical { get; }
		public bool AllowNotApplicable { get; }
		public bool RequireNoteOnFail { get; }

		public ChecklistTemplateItem(string itemId, string name, ChecklistItemType type, bool required,
			bool critical, bool allowNotApplicable, bool requireNoteOnFail)
		{
			ItemId = itemId;
			Name = name;
			Type = type;
			Required = required;
			Critical = critical;
			AllowNotApplicable = allowNotApplicable;
			RequireNoteOnFail = requireNoteOnFail;
		}
	}
}
