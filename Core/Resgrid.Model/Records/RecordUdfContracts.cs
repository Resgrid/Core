using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Values only: labels, types, visibility and classification come from the pinned published definition.</summary>
	public class RecordUdfInput
	{
		public string DefinitionId { get; set; }
		public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
	}
	public class RecordUdfSection
	{
		public string DefinitionId { get; set; }
		public string RecordDefinitionKey { get; set; }
		public int RecordDefinitionVersion { get; set; }
		public int ExtensionVersion { get; set; }
		public List<RecordUdfField> Fields { get; set; } = new List<RecordUdfField>();
	}
	public class RecordUdfField
	{
		public UdfField Field { get; set; }
		public string Value { get; set; }
	}
}
