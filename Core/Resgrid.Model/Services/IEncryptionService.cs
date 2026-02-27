namespace Resgrid.Model.Services
{
	/// <summary>
	/// Generic AES-256 encryption service. Can be used anywhere in the system that requires
	/// encryption at rest — not limited to the workflow engine.
	/// </summary>
	public interface IEncryptionService
	{
		/// <summary>Encrypts <paramref name="plainText"/> using the system-wide master key.</summary>
		string Encrypt(string plainText);

		/// <summary>Decrypts <paramref name="cipherText"/> using the system-wide master key.</summary>
		string Decrypt(string cipherText);

		/// <summary>
		/// Encrypts <paramref name="plainText"/> using a department-specific key derived from
		/// the global key combined with <paramref name="departmentId"/> and <paramref name="departmentCode"/>.
		/// </summary>
		string EncryptForDepartment(string plainText, int departmentId, string departmentCode);

		/// <summary>
		/// Decrypts <paramref name="cipherText"/> using the same department-specific key.
		/// Throws if the key does not match (wrong departmentId or departmentCode).
		/// </summary>
		string DecryptForDepartment(string cipherText, int departmentId, string departmentCode);
	}
}

