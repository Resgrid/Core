using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A run card or response package save was refused for reasons the editor can show as they are (a decision graph
	/// with an unreachable node, a row with no resource types). Other ArgumentExceptions from a save stay generic,
	/// because they name ids the department does not own.
	/// </summary>
	public class RunCardValidationException : ArgumentException
	{
		public RunCardValidationException(IEnumerable<string> errors)
			: base(string.Join(" ", (errors ?? Enumerable.Empty<string>()).Distinct()))
		{
			Errors = (errors ?? Enumerable.Empty<string>()).Distinct().ToList();
		}

		public List<string> Errors { get; }
	}
}
