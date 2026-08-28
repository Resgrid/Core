using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Safe projections for unattended consumers. See <see cref="IProtectedProjectionService"/> for
	/// the contract. Redaction is name-based against the protected-field catalog's column names:
	/// deliberately over-broad (a property named like any cataloged column is redacted wherever it
	/// appears in the event graph) because over-redaction is a cosmetic defect while under-redaction
	/// is a disclosure.
	/// </summary>
	public class ProtectedProjectionService : IProtectedProjectionService
	{
		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IProtectedFieldCatalog _catalog;

		private readonly Lazy<(HashSet<string> Scalar, HashSet<string> Binary)> _protectedNames;

		public ProtectedProjectionService(IDepartmentDataProtectionService dataProtectionService,
			IProtectedFieldCatalog catalog)
		{
			_dataProtectionService = dataProtectionService;
			_catalog = catalog;
			_protectedNames = new Lazy<(HashSet<string>, HashSet<string>)>(() =>
			{
				var scalar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var binary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var entry in _catalog.GetAll())
				{
					if (entry.StorageKind == ProtectedFieldStorageKind.Binary)
						binary.Add(entry.ColumnName);
					else
						scalar.Add(entry.ColumnName);
				}

				return (scalar, binary);
			});
		}

		public async Task<string> BuildSafeWorkflowPayloadAsync(int departmentId, object eventPayload)
		{
			if (eventPayload == null)
				return null;

			bool enforced;
			try
			{
				enforced = await _dataProtectionService.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Unknown protection state must not leak plaintext: treat as enforced and redact.
				Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; redacting workflow payload defensively.");
				enforced = true;
			}

			if (!enforced)
				return JsonConvert.SerializeObject(eventPayload);

			try
			{
				var root = JToken.FromObject(eventPayload);
				var redactedFields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
				Redact(root, redactedFields);

				if (root is JObject rootObject)
				{
					rootObject["is_redacted"] = true;
					rootObject["redacted_fields"] = new JArray(redactedFields.Cast<object>().ToArray());
					rootObject["catalog_version"] = _catalog.Version;
				}

				return root.ToString(Formatting.None);
			}
			catch (Exception ex)
			{
				// A redaction fault NEVER falls back to plaintext — degrade to a minimal safe payload.
				Logging.LogException(ex, $"Workflow payload redaction failed for department {departmentId}; emitting the minimal safe payload.");
				return new JObject
				{
					["is_redacted"] = true,
					["redaction_error"] = true,
					["catalog_version"] = _catalog.Version
				}.ToString(Formatting.None);
			}
		}

		/// <summary>
		/// The exact generic line the plan mandates for GenericOnly egress (section 9.1).
		/// </summary>
		public const string GenericDispatchText = "A protected dispatch is available. Sign in to Resgrid to view details.";

		public async Task<Call> BuildNotificationSafeCallAsync(int departmentId, Call call, ProtectedDataEgressChannel channel)
		{
			if (call == null)
				return null;

			bool enforced;
			try
			{
				enforced = await _dataProtectionService.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Unknown protection state must not leak plaintext to a carrier or provider.
				Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; sanitizing the {channel} notification defensively.");
				enforced = true;
			}

			if (!enforced)
				return call;

			if (await ChannelAllowsProtectedContentAsync(departmentId, channel))
				return call;

			// Sanitized clone: only the allowlisted system-generated call number, priority/color,
			// and routing/structural fields survive (plan section 9.1). Every cataloged user-authored
			// field is absent, so any downstream template, provider DTO, or TTS prompt built from
			// this clone is value-free by construction.
			return new Call
			{
				CallId = call.CallId,
				DepartmentId = call.DepartmentId,
				Department = call.Department,
				Number = call.Number,
				Priority = call.Priority,
				CallPriority = call.CallPriority,
				State = call.State,
				IsCritical = call.IsCritical,
				LoggedOn = call.LoggedOn,
				Name = string.IsNullOrWhiteSpace(call.Number) ? "Protected dispatch" : call.Number,
				NatureOfCall = GenericDispatchText
			};
		}

		public async Task<bool> IsChannelSanitizedAsync(int departmentId, ProtectedDataEgressChannel channel)
		{
			bool enforced;
			try
			{
				enforced = await _dataProtectionService.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Unknown protection state must not leak plaintext to a carrier or provider.
				Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; treating the {channel} channel as sanitized defensively.");
				return true;
			}

			if (!enforced)
				return false;

			return !await ChannelAllowsProtectedContentAsync(departmentId, channel);
		}

		private async Task<bool> ChannelAllowsProtectedContentAsync(int departmentId, ProtectedDataEgressChannel channel)
		{
			// Third-party chat platforms have no policy column and are always generic when enforced.
			if (channel == ProtectedDataEgressChannel.ChatPlatform)
				return false;

			try
			{
				var egress = await _dataProtectionService.GetEgressPolicyByDepartmentIdAsync(departmentId);
				var mode = channel switch
				{
					ProtectedDataEgressChannel.Push => egress.PushMode,
					ProtectedDataEgressChannel.Sms => egress.SmsMode,
					ProtectedDataEgressChannel.Email => egress.EmailMode,
					ProtectedDataEgressChannel.Voice => egress.VoiceMode,
					_ => (int)ProtectedDataEgressMode.GenericOnly
				};

				// ProtectedAfterPin degrades to GenericOnly until the PIN-release flow ships.
				return mode == (int)ProtectedDataEgressMode.AllowProtectedContent;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Egress-policy lookup failed for department {departmentId}; treating {channel} as GenericOnly.");
				return false;
			}
		}

		private void Redact(JToken token, ISet<string> redactedFields)
		{
			switch (token)
			{
				case JObject obj:
					// Materialize first: binary properties are removed while iterating.
					foreach (var property in obj.Properties().ToList())
					{
						if (_protectedNames.Value.Binary.Contains(property.Name))
						{
							// Binaries are omitted from safe projections, never inlined or replaced.
							redactedFields.Add(property.Name);
							property.Remove();
							continue;
						}

						if (_protectedNames.Value.Scalar.Contains(property.Name) &&
							property.Value is JValue { Type: not JTokenType.Null })
						{
							property.Value = ProtectedDataEnvelope.RedactionValue;
							redactedFields.Add(property.Name);
							continue;
						}

						Redact(property.Value, redactedFields);
					}

					break;

				case JArray array:
					foreach (var item in array)
						Redact(item, redactedFields);
					break;
			}
		}
	}
}
