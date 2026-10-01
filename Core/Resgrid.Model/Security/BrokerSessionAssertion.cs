using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Model.Providers;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// The identity tier's short-lived statement to the Protected Data Broker about the end user behind one attended
	/// request (passkey workbook section 6.2). The calling Web or API host mints it after its session validation passes;
	/// the broker checks it against the grant and the live session. It is internal, lasts about a minute, is bound to one
	/// request by <see cref="RequestDigest"/>, and is never a substitute for a user or API credential.
	/// </summary>
	public sealed class BrokerSessionAssertion
	{
		public const string HeaderName = "X-Resgrid-Session-Assertion";

		public string UserId { get; init; }
		public string SessionId { get; init; }
		public long AuthenticationGeneration { get; init; }
		public int DepartmentId { get; init; }
		public int ClientApplication { get; init; }
		public long? SessionLockVersion { get; init; }

		/// <summary>The caller credential's issue time that session validation checked (cred_iat), when known.</summary>
		public DateTime? CredentialIssuedOnUtc { get; init; }

		/// <summary>SHA-256 of the request id, operation and items (<see cref="BrokerRequestDigest"/>).</summary>
		public string RequestDigest { get; init; }

		/// <summary>jti: single use at the broker.</summary>
		public string AssertionId { get; init; }

		public DateTime IssuedAtUtc { get; init; }
		public DateTime ExpiresOnUtc { get; init; }
	}

	public enum BrokerSessionAssertionOutcome
	{
		Valid = 0,

		/// <summary>No assertion certificate is configured on this host.</summary>
		NotConfigured = 1,

		/// <summary>Unparseable, wrong algorithm, issuer or audience, bad signature, or a missing claim.</summary>
		Invalid = 2,

		/// <summary>Outside its lifetime (bounded skew).</summary>
		Expired = 3,

		/// <summary>Minted for a different request, operation or item list.</summary>
		RequestMismatch = 4
	}

	/// <summary>
	/// The canonical digest that binds a session assertion to exactly one broker request. Both sides compute it from the
	/// same values, so any change to the department, request id, operation or any item invalidates the assertion.
	/// </summary>
	public static class BrokerRequestDigest
	{
		public static string Compute(string operation, int departmentId, string requestId, IReadOnlyList<ProtectedFieldOperationItem> items)
		{
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			Append(hash, "resgrid-broker-request/1");
			Append(hash, operation);
			Append(hash, departmentId.ToString(CultureInfo.InvariantCulture));
			Append(hash, requestId);

			var count = items?.Count ?? 0;
			Append(hash, count.ToString(CultureInfo.InvariantCulture));
			for (var i = 0; i < count; i++)
			{
				var item = items[i];
				if (item == null)
				{
					Append(hash, null);
					continue;
				}

				Append(hash, item.FieldId);
				Append(hash, item.RowKey);
				Append(hash, item.IsBinary ? "1" : "0");
				Append(hash, item.CatalogVersion.ToString(CultureInfo.InvariantCulture));
				Append(hash, item.Value);
			}

			return Convert.ToHexString(hash.GetHashAndReset());
		}

		// Length-prefixed, so no two different inputs can produce the same byte stream; null is distinct from "".
		private static void Append(IncrementalHash hash, string value)
		{
			Span<byte> length = stackalloc byte[4];
			if (value == null)
			{
				BinaryPrimitives.WriteInt32BigEndian(length, -1);
				hash.AppendData(length);
				return;
			}

			var bytes = Encoding.UTF8.GetBytes(value);
			BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
			hash.AppendData(length);
			hash.AppendData(bytes);
		}
	}
}
