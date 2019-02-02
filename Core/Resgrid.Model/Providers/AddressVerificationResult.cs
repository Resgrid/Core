using System;

namespace Resgrid.Model.Providers
{
	public class AddressVerificationResult
	{
		public bool ServiceSuccess { get; set; }
		public bool AddressValid { get; set; }
		public int Score { get; set; }
		public Exception Exception { get; set; }
	}
}