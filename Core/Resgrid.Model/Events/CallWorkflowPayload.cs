using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model.Events
{
	/// <summary>
	/// Round 7C: what of a call's protocols may reach a workflow payload. A call event (CallAdded, CallUpdated, CallClosed and any other
	/// payload that carries a call) serializes <see cref="Call.Protocols"/>; the case answers, the key-question answers, the step log and
	/// the override and removal reasons are what a caller said or a dispatcher wrote about them, so they never enter a workflow input
	/// payload (stored on the run, queued and retried). The protocol's ids, code, content hash, version and determinant stay
	/// (<c>call.determinant</c> reads them).
	/// </summary>
	public static class CallWorkflowPayload
	{
		/// <summary>CallProtocol fields removed from every workflow payload (caller content or free text).</summary>
		public static readonly IReadOnlyCollection<string> StrippedProtocolFields = new[]
		{
			nameof(CallProtocol.CaseJson), nameof(CallProtocol.AnswersJson), nameof(CallProtocol.OverrideReason), nameof(CallProtocol.StepLogJson),
			nameof(CallProtocol.RemovedReason)
		};

		/// <summary>CallProtocol text fields that stay: identifiers, the protocol's code, the content hash and the determinant.</summary>
		public static readonly IReadOnlyCollection<string> KeptProtocolTextFields = new[]
		{
			nameof(CallProtocol.Data), nameof(CallProtocol.ContentHash), nameof(CallProtocol.AttachedByUserId), nameof(CallProtocol.RemovedByUserId),
			nameof(CallProtocol.Determinant)
		};

		/// <summary>Removes the stripped fields from every call protocol anywhere in the payload (an array named Protocols of CallProtocol rows).</summary>
		public static void Trim(JToken root)
		{
			switch (root)
			{
				case JObject obj:
					foreach (var property in obj.Properties().ToList())
					{
						if (string.Equals(property.Name, nameof(Call.Protocols), StringComparison.Ordinal) && property.Value is JArray rows)
						{
							foreach (var row in rows.OfType<JObject>().Where(IsCallProtocol))
								foreach (var field in StrippedProtocolFields)
									row.Remove(field);
						}

						Trim(property.Value);
					}

					break;

				case JArray array:
					foreach (var item in array)
						Trim(item);
					break;
			}
		}

		private static bool IsCallProtocol(JObject row) => row.Property(nameof(CallProtocol.CallProtocolId)) != null || row.Property(nameof(CallProtocol.DispatchProtocolId)) != null;
	}
}
