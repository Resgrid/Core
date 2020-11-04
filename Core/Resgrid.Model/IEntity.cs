using System.Collections.Generic;

namespace Resgrid.Model
{
	public interface IEntity
	{
		object IdValue { get; set; }

		string TableName { get; }

		string IdName { get; }

		IEnumerable<string> IgnoredProperties { get; }
	}
}
