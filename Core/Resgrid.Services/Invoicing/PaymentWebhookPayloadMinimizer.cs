using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Strips the payer-identifying parts of a provider webhook body before it is kept on the payment event ledger
	/// (PaymentConnectEvents.PayloadJson). The ledger exists for replay and reconciliation — event ids, types,
	/// amounts, intents, statuses — and never needs the payer's name, e-mail, phone, address or card details, which
	/// would otherwise sit outside Advanced Data Protection (the ledger row has no department at receipt time, so it
	/// cannot be enveloped per department). Unparseable bodies are dropped rather than stored raw.
	/// </summary>
	public static class PaymentWebhookPayloadMinimizer
	{
		/// <summary>Property names removed wherever they appear (Stripe checkout/session/charge/payment-intent shapes and their Paddle equivalents).</summary>
		public static readonly IReadOnlyCollection<string> DroppedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"email", "name", "phone", "address", "billing_details", "customer_details", "customer_email", "customer_name", "receipt_email",
			"shipping", "shipping_details", "individual", "tax_ids", "payment_method_details", "payment_method_options", "card", "owner",
			"customer", "billing_address", "ip_address", "user_agent", "description", "statement_descriptor", "receipt_url"
		};

		public static string Minimize(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			JToken token;
			try { token = JToken.Parse(json); }
			catch (JsonException) { return null; }
			Strip(token);
			return token.ToString(Formatting.None);
		}

		private static void Strip(JToken token)
		{
			switch (token)
			{
				case JObject obj:
					foreach (var property in obj.Properties().ToList())
					{
						// A bare "customer" id string is kept (reconciliation); an expanded customer object is payer PII.
						if (DroppedProperties.Contains(property.Name) && !(property.Name.Equals("customer", StringComparison.OrdinalIgnoreCase) && property.Value.Type == JTokenType.String))
							property.Remove();
						else
							Strip(property.Value);
					}
					break;
				case JArray array:
					foreach (var item in array) Strip(item);
					break;
			}
		}
	}
}
