namespace Resgrid.Model
{
	/// <summary>Describes a single template variable available to departments in the Scriban template editor.</summary>
	public sealed class TemplateVariableDescriptor
	{
		public TemplateVariableDescriptor(string name, string description, string dataType, bool isCommon)
		{
			Name = name;
			Description = description;
			DataType = dataType;
			IsCommon = isCommon;
		}

		/// <summary>The Scriban variable path, e.g. <c>call.name</c>.</summary>
		public string Name { get; }

		/// <summary>Human-readable description shown in the template editor side panel.</summary>
		public string Description { get; }

		/// <summary>Data type hint: "string", "int", "bool", "datetime", "decimal", "double", "long".</summary>
		public string DataType { get; }

		/// <summary>True for variables that are always available regardless of event type (department, timestamp, user).</summary>
		public bool IsCommon { get; }
	}
}

