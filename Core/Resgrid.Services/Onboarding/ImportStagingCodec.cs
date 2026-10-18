using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Per-artifact data keys (wizard plan sections 8.7 and 14.4). Each artifact has its own 256-bit key, wrapped by
	/// EncryptionService.EncryptForDepartment and stored on ImportArtifacts; the same key seals that artifact's staged
	/// rows. Unwrapped keys live only in memory for the lifetime of the scope.
	/// </summary>
	public interface IImportKeyService
	{
		Task<(string Wrapped, string Plain)> CreateKeyAsync(int departmentId);

		Task<string> UnwrapAsync(int departmentId, string wrapped);
	}

	public class ImportKeyService : IImportKeyService
	{
		private readonly IEncryptionService _encryption;
		private readonly IDepartmentsService _departments;
		private readonly IImportArtifactCipher _cipher;
		private readonly ConcurrentDictionary<string, string> _unwrapped = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<int, string> _codes = new ConcurrentDictionary<int, string>();

		public ImportKeyService(IEncryptionService encryption, IDepartmentsService departments, IImportArtifactCipher cipher)
		{
			_encryption = encryption;
			_departments = departments;
			_cipher = cipher;
		}

		public async Task<(string Wrapped, string Plain)> CreateKeyAsync(int departmentId)
		{
			var plain = _cipher.NewDataKey();
			var wrapped = _encryption.EncryptForDepartment(plain, departmentId, await DepartmentCodeAsync(departmentId));
			_unwrapped[departmentId + ":" + wrapped] = plain;
			return (wrapped, plain);
		}

		public async Task<string> UnwrapAsync(int departmentId, string wrapped)
		{
			if (string.IsNullOrWhiteSpace(wrapped))
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-KEY-MISSING");

			var cacheKey = departmentId + ":" + wrapped;
			if (_unwrapped.TryGetValue(cacheKey, out var plain))
				return plain;

			try
			{
				plain = _encryption.DecryptForDepartment(wrapped, departmentId, await DepartmentCodeAsync(departmentId));
			}
			catch (CryptographicException)
			{
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-KEY-INVALID");
			}

			_unwrapped[cacheKey] = plain;
			return plain;
		}

		private async Task<string> DepartmentCodeAsync(int departmentId)
		{
			if (_codes.TryGetValue(departmentId, out var code))
				return code;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false)
				?? throw new DepartmentImportException("import_department_missing", 404);
			code = department.Code ?? string.Empty;
			_codes[departmentId] = code;
			return code;
		}
	}

	/// <summary>
	/// Seals staged-row payloads (section 14.4): every staged row is encrypted with its artifact's key, bound to the
	/// dataset and row so a payload cannot be moved between rows. The content hash is an HMAC under the same key, so a
	/// hash of a low-entropy row cannot be guessed offline.
	/// </summary>
	public sealed class ImportStagingCodec
	{
		private readonly IImportArtifactCipher _cipher;

		public ImportStagingCodec(IImportArtifactCipher cipher)
		{
			_cipher = cipher;
		}

		public static string Binding(string datasetId, int rowNumber, string part) => $"row:{datasetId}:{rowNumber}:{part}";

		public string Seal(string dataKey, string binding, object payload)
		{
			if (payload == null)
				return null;
			var json = JsonConvert.SerializeObject(payload);
			return Convert.ToBase64String(_cipher.EncryptWhole(dataKey, binding, Encoding.UTF8.GetBytes(json)));
		}

		public T Open<T>(string dataKey, string binding, string sealedPayload)
		{
			if (string.IsNullOrEmpty(sealedPayload))
				return default;
			var json = Encoding.UTF8.GetString(_cipher.DecryptWhole(dataKey, binding, Convert.FromBase64String(sealedPayload)));
			return JsonConvert.DeserializeObject<T>(json);
		}

		/// <summary>HMAC-SHA256 of the canonical values in key order (deterministic for the approval digest).</summary>
		public static string Hash(string dataKey, IDictionary<string, object> canonical)
		{
			var ordered = new SortedDictionary<string, object>(canonical ?? new Dictionary<string, object>(), StringComparer.Ordinal);
			using var hmac = new HMACSHA256(Convert.FromBase64String(dataKey));
			return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ordered)))).ToLowerInvariant();
		}
	}
}
