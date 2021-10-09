using System.Collections.Generic;

namespace Resgrid.Model
{
	public interface IEntity
	{
		object IdValue { get; set; }

		string TableName { get; }

		string IdName { get; }

		int IdType { get; }

		IEnumerable<string> IgnoredProperties { get; }
	}
}
