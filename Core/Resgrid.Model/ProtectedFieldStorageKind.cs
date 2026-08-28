namespace Resgrid.Model
{
	/// <summary>
	/// How a cataloged field's ciphertext is persisted (ADP plan sections 22.2–22.3).
	/// </summary>
	public enum ProtectedFieldStorageKind
	{
		/// <summary>String column carries the rgdp: text envelope in place (column widened to MAX/citext).</summary>
		Text = 1,

		/// <summary>Binary column carries the rgdpb: variant in place (raw nonce|tag|ciphertext, no base64).</summary>
		Binary = 2,

		/// <summary>
		/// Typed (non-string) column such as a decimal coordinate: the typed column is nulled/zeroed
		/// while protected and a Protected{Name}Envelope companion column carries the value
		/// (Appendix B pattern).
		/// </summary>
		CompanionColumn = 3
	}
}
