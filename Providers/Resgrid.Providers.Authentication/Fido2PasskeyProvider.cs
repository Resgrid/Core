using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Providers.Authentication
{
	/// <summary>
	/// The WebAuthn adapter over Fido2 4.1.0 (passkey plan section 4; Phase 0 workbook section 3). One Fido2 instance per
	/// client relying party, so a ceremony is only ever checked against the RP ID and exact origins of the client that
	/// asked for it. Every ceremony requires user verification; registration asks for a discoverable credential with
	/// attestation "none". The library is stateless: the caller supplies the single-use options and the bound credential.
	/// </summary>
	public sealed class Fido2PasskeyProvider : IPasskeyProvider
	{
		// Clock drift allowed on the client data timestamp; the challenge lifetime is what bounds a ceremony.
		private const int TimestampDriftToleranceMs = 300000;

		private readonly Dictionary<UserSessionClientApplication, Fido2> _servers = new();

		public Fido2PasskeyProvider(IRelyingPartyRegistry registry)
		{
			if (!registry.Readiness.IsReady)
				return;

			foreach (var client in Enum.GetValues<UserSessionClientApplication>())
			{
				var party = registry.Get(client);
				if (party == null)
					continue;

				_servers[client] = new Fido2(new Fido2Configuration
				{
					RPID = party.RpId,
					RPName = string.IsNullOrWhiteSpace(PasskeyConfig.RelyingPartyName) ? "Resgrid" : PasskeyConfig.RelyingPartyName,
					Origins = new HashSet<string>(party.Origins, StringComparer.Ordinal),
					TimestampDriftTolerance = TimestampDriftToleranceMs
				});
			}
		}

		public bool IsAvailableFor(UserSessionClientApplication client) => _servers.ContainsKey(client);

		public string CreateRegistrationOptions(UserSessionClientApplication client, byte[] userHandle, string userName, string displayName,
			IReadOnlyList<byte[]> excludeCredentialIds, bool preferRoaming)
		{
			var server = ServerFor(client);
			var options = server.RequestNewCredential(new RequestNewCredentialParams
			{
				User = new Fido2User { Id = userHandle, Name = userName, DisplayName = displayName },
				ExcludeCredentials = (excludeCredentialIds ?? Array.Empty<byte[]>()).Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
				AuthenticatorSelection = new AuthenticatorSelection
				{
					ResidentKey = ResidentKeyRequirement.Required,
					UserVerification = UserVerificationRequirement.Required,
					// A shared installation asks for a security key or phone (plan section 6.5); the response is still checked.
					AuthenticatorAttachment = preferRoaming ? AuthenticatorAttachment.CrossPlatform : null
				},
				AttestationPreference = AttestationConveyancePreference.None
			});

			if (preferRoaming)
				options.Hints = new[] { PublicKeyCredentialHint.SecurityKey, PublicKeyCredentialHint.Hybrid };

			return options.ToJson();
		}

		public async Task<PasskeyRegistrationVerification> VerifyRegistrationAsync(UserSessionClientApplication client, string optionsJson,
			string attestationResponseJson, CancellationToken cancellationToken = default)
		{
			if (!_servers.TryGetValue(client, out var server) || string.IsNullOrWhiteSpace(optionsJson) || string.IsNullOrWhiteSpace(attestationResponseJson))
				return PasskeyRegistrationVerification.Failed();

			try
			{
				var options = CredentialCreateOptions.FromJson(optionsJson);
				var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationResponseJson);
				if (response?.Response == null)
					return PasskeyRegistrationVerification.Failed();

				// Uniqueness is enforced by the (RP, credential id) key when the row is inserted, which is atomic across
				// nodes; a check here could only race it.
				var credential = await server.MakeNewCredentialAsync(new MakeNewCredentialParams
				{
					AttestationResponse = response,
					OriginalOptions = options,
					IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)
				}, cancellationToken);

				return new PasskeyRegistrationVerification
				{
					Succeeded = true,
					CredentialId = credential.Id,
					PublicKey = credential.PublicKey,
					Algorithm = ReadCoseAlgorithm(credential.PublicKey),
					UserHandle = options.User.Id,
					SignCount = credential.SignCount,
					IsBackupEligible = credential.IsBackupEligible,
					IsBackedUp = credential.IsBackedUp,
					Transports = (credential.Transports ?? Array.Empty<AuthenticatorTransport>()).Select(TransportName).Where(t => t != null).ToList(),
					Aaguid = credential.AaGuid,
					AttestationFormat = credential.AttestationFormat,
					Attachment = ReadAttachment(attestationResponseJson)
				};
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// The reason stays in the log (value-free); the client only learns that verification failed.
				Framework.Logging.LogDebug($"Passkey registration verification failed for {client}: {ex.GetType().Name}: {ex.Message}");
				return PasskeyRegistrationVerification.Failed();
			}
		}

		public string CreateAssertionOptions(UserSessionClientApplication client, IReadOnlyList<byte[]> allowCredentialIds)
		{
			var server = ServerFor(client);
			var options = server.GetAssertionOptions(new GetAssertionOptionsParams
			{
				AllowedCredentials = (allowCredentialIds ?? Array.Empty<byte[]>()).Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
				UserVerification = UserVerificationRequirement.Required
			});
			return options.ToJson();
		}

		public byte[] ReadCredentialId(string assertionResponseJson)
		{
			if (string.IsNullOrWhiteSpace(assertionResponseJson))
				return null;

			try
			{
				var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionResponseJson);
				return response?.RawId is { Length: > 0 } rawId ? rawId : null;
			}
			catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is ArgumentException || ex is NotSupportedException)
			{
				return null;
			}
		}

		public async Task<PasskeyAssertionVerification> VerifyAssertionAsync(UserSessionClientApplication client, string optionsJson,
			string assertionResponseJson, PasskeyAssertionCredential credential, CancellationToken cancellationToken = default)
		{
			if (!_servers.TryGetValue(client, out var server) || credential?.PublicKey == null || credential.CredentialId == null
				|| string.IsNullOrWhiteSpace(optionsJson) || string.IsNullOrWhiteSpace(assertionResponseJson))
				return PasskeyAssertionVerification.Failed();

			try
			{
				var options = AssertionOptions.FromJson(optionsJson);
				var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionResponseJson);
				if (response?.Response == null || response.RawId == null || !response.RawId.AsSpan().SequenceEqual(credential.CredentialId))
					return PasskeyAssertionVerification.Failed();

				// The library verifies against whatever key it is handed (Phase 0 spike): the caller has already loaded the
				// credential by id for this user and client, and a returned user handle must be that credential's.
				var result = await server.MakeAssertionAsync(new MakeAssertionParams
				{
					AssertionResponse = response,
					OriginalOptions = options,
					StoredPublicKey = credential.PublicKey,
					StoredSignatureCounter = (uint)Math.Clamp(credential.SignCount, 0, uint.MaxValue),
					IsUserHandleOwnerOfCredentialIdCallback = (owner, _) => Task.FromResult(
						owner.CredentialId != null && owner.CredentialId.AsSpan().SequenceEqual(credential.CredentialId) &&
						owner.UserHandle != null && credential.UserHandle != null && owner.UserHandle.AsSpan().SequenceEqual(credential.UserHandle))
				}, cancellationToken);

				return new PasskeyAssertionVerification { Succeeded = true, SignCount = result.SignCount, IsBackedUp = result.IsBackedUp };
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogDebug($"Passkey assertion verification failed for {client}: {ex.GetType().Name}: {ex.Message}");
				return PasskeyAssertionVerification.Failed();
			}
		}

		private Fido2 ServerFor(UserSessionClientApplication client) =>
			_servers.TryGetValue(client, out var server)
				? server
				: throw new InvalidOperationException($"No relying party is configured for {client}.");

		private static string TransportName(AuthenticatorTransport transport) => transport switch
		{
			AuthenticatorTransport.Usb => "usb",
			AuthenticatorTransport.Nfc => "nfc",
			AuthenticatorTransport.Ble => "ble",
			AuthenticatorTransport.SmartCard => "smart-card",
			AuthenticatorTransport.Hybrid => "hybrid",
			AuthenticatorTransport.Internal => "internal",
			_ => null
		};

		/// <summary>The COSE "alg" (label 3) of a verified public key; null when it cannot be read.</summary>
		internal static int? ReadCoseAlgorithm(byte[] coseKey)
		{
			if (coseKey == null || coseKey.Length == 0)
				return null;

			try
			{
				var reader = new CborReader(coseKey, CborConformanceMode.Lax);
				var entries = reader.ReadStartMap();
				for (var i = 0; entries == null || i < entries; i++)
				{
					if (reader.PeekState() == CborReaderState.EndMap)
						break;

					if (reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger)
					{
						var label = reader.ReadInt64();
						if (label == 3 && reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger)
							return (int)reader.ReadInt64();
					}
					else
					{
						reader.SkipValue();
					}

					reader.SkipValue();
				}
			}
			catch (Exception ex) when (ex is CborContentException || ex is InvalidOperationException || ex is OverflowException)
			{
			}

			return null;
		}

		/// <summary>The client-reported authenticatorAttachment of a registration response: a hint, never proof.</summary>
		private static string ReadAttachment(string responseJson)
		{
			try
			{
				using var document = JsonDocument.Parse(responseJson);
				if (document.RootElement.TryGetProperty("authenticatorAttachment", out var value) && value.ValueKind == JsonValueKind.String)
				{
					var attachment = value.GetString();
					if (attachment == "platform" || attachment == "cross-platform")
						return attachment;
				}
			}
			catch (JsonException)
			{
			}

			return null;
		}
	}
}
