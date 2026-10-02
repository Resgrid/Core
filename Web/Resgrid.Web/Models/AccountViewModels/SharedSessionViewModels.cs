using System.Collections.Generic;

namespace Resgrid.Web.Models.AccountViewModels
{
	/// <summary>
	/// The lock screen of a shared workstation session (passkey plan section 12.5.3): who is signed in, and how that same
	/// operator can unlock it. Nothing from the locked work is shown here.
	/// </summary>
	public class SharedSessionLockedViewModel
	{
		public string Operator { get; set; }
		public string InstallationLabel { get; set; }

		/// <summary>Idle or explicit, for the heading.</summary>
		public bool LockedWhenIdle { get; set; }

		public long LockVersion { get; set; }
		public string ReturnUrl { get; set; }

		/// <summary>The unlock methods the operator has and the department allows (<c>totp</c>, <c>passkey</c>, ...).</summary>
		public List<string> Methods { get; set; } = new();

		/// <summary>Why quick unlock is not offered, when it is not: the operator ends the shift and signs in normally.</summary>
		public string Unavailable { get; set; }

		public string Code { get; set; }
		public string Error { get; set; }

		public bool Offers(string method) => Methods.Contains(method);
	}

	/// <summary>This browser's shared workstation setting: a station label, never a person or a credential.</summary>
	public class SharedWorkstationViewModel
	{
		public bool Available { get; set; }
		public bool Shared { get; set; }
		public string Label { get; set; }
		public string Status { get; set; }
	}
}
