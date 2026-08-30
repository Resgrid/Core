namespace Resgrid.Model.Services
{
	/// <summary>
	/// AEAD field cryptography for ADP envelopes (plan section 4.1). Pure and stateless: the caller
	/// supplies the unwrapped DEK (pinned memory, zeroed by its owner after use) and the AAD binding
	/// components; this service never touches key management, storage, or the KMS. AAD binds
	/// DepartmentId, the stable catalog field id, the stable per-row key, and the envelope format
	/// version — moving ciphertext between tenants, rows, or fields fails authentication rather
	/// than decrypting.
	///
	/// The CATALOG version is deliberately NOT an AAD component. Field ids are stable forever, so
	/// the catalog version adds no binding the field id does not already provide — while including
	/// it would make a department's whole corpus undecryptable the moment its pinned catalog
	/// version advanced, turning every catalog addition into a full decrypt/re-encrypt of every
	/// protected row. Do not add it back.
	/// </summary>
	public interface IProtectedFieldCryptoService
	{
		/// <summary>
		/// Encrypts a text field into an rgdp: envelope. Throws if the value already carries an
		/// envelope prefix — the double-encryption guard belongs to the caller, and reaching this
		/// method with enveloped input is a caller bug, never something to encrypt again.
		/// </summary>
		string EncryptText(byte[] dek, int departmentKeyVersion, string plaintext,
			int departmentId, string catalogFieldId, string rowKey);

		/// <summary>
		/// Decrypts an rgdp: envelope back to text. Throws on AAD mismatch (foreign ciphertext), a
		/// malformed envelope, or an unsupported format version — never returns garbage.
		/// </summary>
		string DecryptText(byte[] dek, string envelope,
			int departmentId, string catalogFieldId, string rowKey);

		/// <summary>Encrypts a binary field into the rgdpb variant (raw header + nonce|tag|ciphertext, no base64).</summary>
		byte[] EncryptBinary(byte[] dek, int departmentKeyVersion, byte[] plaintext,
			int departmentId, string catalogFieldId, string rowKey);

		/// <summary>Decrypts an rgdpb blob back to bytes; throws on AAD mismatch or malformed input.</summary>
		byte[] DecryptBinary(byte[] dek, byte[] envelope,
			int departmentId, string catalogFieldId, string rowKey);

		/// <summary>True when the blob starts with the rgdpb binary envelope header.</summary>
		bool IsBinaryEnveloped(byte[] value);

		/// <summary>
		/// Reads the department key version from an rgdpb header without decrypting (the decrypt path
		/// resolves the DEK per envelope version). False for anything that is not a well-formed
		/// supported-version binary envelope.
		/// </summary>
		bool TryGetBinaryEnvelopeKeyVersion(byte[] value, out int departmentKeyVersion);
	}
}
