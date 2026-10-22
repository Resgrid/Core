using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A saved map visualization (migration M0288): a named <see cref="MapVisualizationDefinition"/> kept by a member for
	/// themselves, or shared with the whole department. Only the definition is stored; the data is read fresh every time it
	/// is opened, under the viewer's own permissions, so sharing a visualization never shares data a viewer could not see.
	/// No foreign keys: the department purge removes the rows.
	/// </summary>
	[Table("MapVisualizations")]
	public class MapVisualization : IEntity
	{
		[Key]
		[Required]
		public string MapVisualizationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(MapVisualizationLimits.MaxNameLength)]
		public string Name { get; set; }

		[MaxLength(MapVisualizationLimits.MaxDescriptionLength)]
		public string Description { get; set; }

		/// <summary>The <see cref="MapVisualizationDefinition"/> as JSON.</summary>
		public string DefinitionJson { get; set; }

		/// <summary>The library template it was started from, if any (<see cref="MapVisualizationLibrary"/> key).</summary>
		public string TemplateKey { get; set; }

		/// <summary>Visible to every member who can open visualizations; otherwise only to its creator.</summary>
		public bool IsShared { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public MapVisualizationDefinition Definition
		{
			get
			{
				if (string.IsNullOrWhiteSpace(DefinitionJson))
					return MapVisualizationDefinitionValidator.Normalize(null);

				try
				{
					return MapVisualizationDefinitionValidator.Normalize(JsonConvert.DeserializeObject<MapVisualizationDefinition>(DefinitionJson));
				}
				catch (JsonException)
				{
					return MapVisualizationDefinitionValidator.Normalize(null);
				}
			}
			set { DefinitionJson = JsonConvert.SerializeObject(MapVisualizationDefinitionValidator.Normalize(value)); }
		}

		/// <summary>True when <paramref name="userId"/> created it.</summary>
		public bool IsOwnedBy(string userId) =>
			!string.IsNullOrWhiteSpace(userId) && string.Equals(CreatedByUserId, userId, StringComparison.OrdinalIgnoreCase);

		/// <summary>Visible to <paramref name="userId"/>: shared with the department, or their own.</summary>
		public bool IsVisibleTo(string userId) => IsShared || IsOwnedBy(userId);

		/// <summary>
		/// Who may change or delete it: its creator while it is personal, and department admins once it is shared (a shared
		/// visualization is department configuration).
		/// </summary>
		public bool CanBeEditedBy(string userId, bool isDepartmentAdmin) => IsShared ? isDepartmentAdmin : IsOwnedBy(userId);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return MapVisualizationId; }
			set { MapVisualizationId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "MapVisualizations";

		[NotMapped]
		public string IdName => "MapVisualizationId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Definition" };
	}
}
