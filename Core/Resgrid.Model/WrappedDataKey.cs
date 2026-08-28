namespace Resgrid.Model
{
	/// <summary>
	/// A freshly generated department data encryption key in its KMS-wrapped form, as returned by an
	/// IKeyWrappingProvider. Contains no plaintext key material — the plaintext half of a datakey
	/// operation exists only inside the Protected Data Broker's hardened memory and is never part of
	/// this type.
	/// </summary>
	public sealed class WrappedDataKey
	{
		/// <summary>Base64 wrapped DEK blob, stored verbatim in DepartmentDataProtectionKeys.WrappedKey.</summary>
		public string WrappedKeyBase64 { get; set; }

		/// <summary>Provider discriminator ("OpenBaoTransit", "LocalDev", ...).</summary>
		public string ProviderType { get; set; }

		/// <summary>Provider key reference (for OpenBao Transit: mount and key name).</summary>
		public string ProviderKeyReference { get; set; }

		/// <summary>KEK version at the provider that wrapped this DEK (for rewrap tracking).</summary>
		public int ProviderKeyVersion { get; set; }
	}
}
