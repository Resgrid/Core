using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// A software WebAuthn authenticator (ES256, "none" attestation), from the Phase 0 spike. It signs whatever RP ID and
	/// origin it is told, so tests can play a legitimate platform or an attacker; the server must tell them apart.
	/// </summary>
	internal sealed class SoftPasskeyAuthenticator
	{
		private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		private readonly bool _backupEligible;
		private uint _counter;

		public SoftPasskeyAuthenticator(bool backupEligible = true, bool countSignatures = true, byte[] credentialId = null)
		{
			_backupEligible = backupEligible;
			CountSignatures = countSignatures;
			CredentialId = credentialId ?? RandomNumberGenerator.GetBytes(32);
		}

		public byte[] CredentialId { get; }

		/// <summary>False for a synced passkey that always reports a zero counter.</summary>
		public bool CountSignatures { get; }

		/// <summary>A registration response to the server's creation options, as PublicKeyCredential.toJSON() would send it.</summary>
		public string Register(string creationOptionsJson, string origin, bool userVerified = true, string rpIdOverride = null,
			string attachment = "platform")
		{
			var options = CredentialCreateOptions.FromJson(creationOptionsJson);
			var clientData = ClientData("webauthn.create", options.Challenge, origin);
			var p = _key.ExportParameters(false);
			var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
			cose.WriteStartMap(5);
			cose.WriteInt32(1); cose.WriteInt32(2);      // kty: EC2
			cose.WriteInt32(3); cose.WriteInt32(-7);     // alg: ES256
			cose.WriteInt32(-1); cose.WriteInt32(1);     // crv: P-256
			cose.WriteInt32(-2); cose.WriteByteString(p.Q.X!);
			cose.WriteInt32(-3); cose.WriteByteString(p.Q.Y!);
			cose.WriteEndMap();

			var authData = new List<byte>(AuthDataHeader(rpIdOverride ?? options.Rp.Id, userVerified, attested: true, counter: 0));
			authData.AddRange(new byte[16]);                                   // AAGUID (none attestation)
			authData.Add((byte)(CredentialId.Length >> 8)); authData.Add((byte)CredentialId.Length);
			authData.AddRange(CredentialId);
			authData.AddRange(cose.Encode());

			var att = new CborWriter(CborConformanceMode.Ctap2Canonical);
			att.WriteStartMap(3);
			att.WriteTextString("fmt"); att.WriteTextString("none");
			att.WriteTextString("attStmt"); att.WriteStartMap(0); att.WriteEndMap();
			att.WriteTextString("authData"); att.WriteByteString(authData.ToArray());
			att.WriteEndMap();

			var response = new AuthenticatorAttestationRawResponse
			{
				Id = B64(CredentialId), RawId = CredentialId, Type = PublicKeyCredentialType.PublicKey,
				Response = new AuthenticatorAttestationRawResponse.AttestationResponse
				{
					AttestationObject = att.Encode(), ClientDataJson = clientData, Transports = [AuthenticatorTransport.Internal, AuthenticatorTransport.Hybrid]
				}
			};
			return WithAttachment(JsonSerializer.Serialize(response), attachment);
		}

		/// <summary>An assertion response to the server's request options.</summary>
		public string Assert(string requestOptionsJson, string origin, byte[] userHandle, bool userVerified = true, string rpIdOverride = null,
			uint? counterOverride = null)
		{
			var options = AssertionOptions.FromJson(requestOptionsJson);
			var clientData = ClientData("webauthn.get", options.Challenge, origin);
			var counter = counterOverride ?? (CountSignatures ? ++_counter : 0);
			var authData = AuthDataHeader(rpIdOverride ?? options.RpId, userVerified, attested: false, counter);
			var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
			var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
			var response = new AuthenticatorAssertionRawResponse
			{
				Id = B64(CredentialId), RawId = CredentialId, Type = PublicKeyCredentialType.PublicKey,
				Response = new AuthenticatorAssertionRawResponse.AssertionResponse
				{
					AuthenticatorData = authData, Signature = signature, ClientDataJson = clientData, UserHandle = userHandle
				}
			};
			return JsonSerializer.Serialize(response);
		}

		private byte[] AuthDataHeader(string rpId, bool userVerified, bool attested, uint counter)
		{
			byte flags = 0x01;                               // UP
			if (userVerified) flags |= 0x04;                 // UV
			if (_backupEligible) flags |= 0x08 | 0x10;       // BE + BS (synced passkey)
			if (attested) flags |= 0x40;                     // AT
			var header = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(rpId))) { flags };
			header.AddRange([(byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter]);
			return header.ToArray();
		}

		private static string WithAttachment(string json, string attachment)
		{
			if (attachment == null)
				return json;

			var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
			node["authenticatorAttachment"] = attachment;
			return node.ToJsonString();
		}

		private static byte[] ClientData(string type, byte[] challenge, string origin) =>
			JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
			{
				["type"] = type, ["challenge"] = B64(challenge), ["origin"] = origin, ["crossOrigin"] = false
			});

		public static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
