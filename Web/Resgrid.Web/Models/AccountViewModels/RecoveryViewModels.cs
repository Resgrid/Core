using System;
using System.Collections.Generic;

namespace Resgrid.Web.Models.AccountViewModels
{
	/// <summary>Setting up the authenticator a required-MFA sign-in needs (passkey plan section 6.2). The key is shown, never stored here.</summary>
	public class LoginMfaSetupViewModel
	{
		public string Code { get; set; }
		public string ReturnUrl { get; set; }
		public string SharedKey { get; set; }
		public string AuthenticatorUri { get; set; }
		public string QrCodeDataUrl { get; set; }
	}

	/// <summary>"I lost my authenticator" (passkey plan section 6.3): a recovery code opens a recovery; without one, who can help.</summary>
	public class LostFactorViewModel
	{
		public string Code { get; set; }
		public string ReturnUrl { get; set; }

		/// <summary>Whether this browser is part-way through a sign-in, so a recovery code can open a recovery.</summary>
		public bool CanRecover { get; set; }
	}

	/// <summary>The open recovery: the replacement authenticator to set up, and the passkeys the user may remove as lost.</summary>
	public class FactorRecoveryViewModel
	{
		public string Code { get; set; }
		public List<string> RemovePasskeyIds { get; set; } = new();
		public string SharedKey { get; set; }
		public string AuthenticatorUri { get; set; }
		public string QrCodeDataUrl { get; set; }
		public List<RecoveryPasskeyView> Passkeys { get; set; } = new();
	}

	public class RecoveryPasskeyView
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public int ClientApplication { get; set; }
		public DateTime CreatedOnUtc { get; set; }
	}

	/// <summary>The recovery finished: the new recovery codes, shown once, and every session signed out.</summary>
	public class RecoveryCompleteViewModel
	{
		public IReadOnlyList<string> RecoveryCodes { get; set; } = Array.Empty<string>();
	}
}
