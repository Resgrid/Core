using System;

namespace Resgrid.Model.Security
{
	public sealed class SessionCreationDeniedException : Exception
	{
		public SessionCreationDeniedException(string failureCode)
			: base("The authentication session could not be created.")
		{
			FailureCode = failureCode;
		}

		public string FailureCode { get; }
	}
}
