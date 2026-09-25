using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// The section 12 disclosure items every Advanced Data Protection enrollment must acknowledge
	/// (department-protected-data-implementation-plan.md §12, §18.1 step 2). One list for the web Enrollment Wizard, the v4
	/// API and the enrollment command gate, so no caller can queue an enrollment on a shorter or older list. Item keys are
	/// stable identifiers recorded in the policy's acknowledgement JSON; bump <see cref="Version"/> whenever an item is
	/// added or its disclosure text changes.
	/// </summary>
	public static class AdpEnrollmentAcknowledgements
	{
		/// <summary>ADP-ACK-2 (2026-09-25) added own_ai_provider: a protected department cannot use its own AI provider (bring your own key).</summary>
		public const string Version = "ADP-ACK-2";

		public static readonly IReadOnlyList<string> Items = new[]
		{
			"catalog_scope",
			"plaintext_metadata",
			"authorized_server_access",
			"step_up_window",
			"bigboard_reduction",
			"workflow_redaction",
			"default_egress",
			"own_ai_provider",
			"search_report_limitations",
			"migration_disable",
			"key_loss_support",
			"not_hipaa_compliance"
		};

		/// <summary>Largest acknowledgement record accepted. The web wizard's record, sizing scan included, is far smaller.</summary>
		public const int MaxRecordLength = 64 * 1024;

		/// <summary>
		/// True when the record is a JSON object whose <c>version</c> is <see cref="Version"/>, whose
		/// <c>acknowledgedItems</c> contains every item in <see cref="Items"/>, and whose <c>lockConsent</c> is true
		/// (§18.1 step 7). Property names match case-insensitively; item keys match exactly. A record made for an older
		/// version is incomplete: its reader was never shown the items added since.
		/// </summary>
		public static bool IsComplete(string acknowledgementsJson)
		{
			if (string.IsNullOrWhiteSpace(acknowledgementsJson) || acknowledgementsJson.Length > MaxRecordLength)
				return false;

			try
			{
				using var document = JsonDocument.Parse(acknowledgementsJson);
				var root = document.RootElement;
				if (root.ValueKind != JsonValueKind.Object)
					return false;

				if (!TryGetProperty(root, "version", out var version) || version.ValueKind != JsonValueKind.String ||
					!string.Equals(version.GetString(), Version, StringComparison.Ordinal))
					return false;

				if (!TryGetProperty(root, "lockConsent", out var lockConsent) || lockConsent.ValueKind != JsonValueKind.True)
					return false;

				if (!TryGetProperty(root, "acknowledgedItems", out var items) || items.ValueKind != JsonValueKind.Array)
					return false;

				var acknowledged = items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String)
					.Select(i => i.GetString()).ToHashSet(StringComparer.Ordinal);
				return Items.All(acknowledged.Contains);
			}
			catch (JsonException)
			{
				return false;
			}
		}

		private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					value = property.Value;
					return true;
				}
			}

			value = default;
			return false;
		}
	}
}
