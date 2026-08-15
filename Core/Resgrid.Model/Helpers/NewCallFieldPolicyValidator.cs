using System;
using System.Collections.Generic;

namespace Resgrid.Model.Helpers
{
	/// <summary>A field the policy requires that the submitted call did not supply.</summary>
	public class NewCallFieldViolation
	{
		public string Key { get; set; }
	}

	/// <summary>
	/// Values pulled off whichever call-creation payload is being validated. Every call-creation path
	/// (web form, v4 API, chatbot, email import, TTC) maps its own shape onto this once, so the
	/// policy is enforced identically everywhere instead of being re-implemented per surface.
	/// </summary>
	public class NewCallFieldValues
	{
		public string Note { get; set; }
		public string Address { get; set; }
		public string Geolocation { get; set; }
		public string What3Words { get; set; }
		public string PlusCode { get; set; }
		public string ContactName { get; set; }
		public string ContactInfo { get; set; }
		public string ExternalId { get; set; }
		public string IncidentId { get; set; }
		public string ReferenceId { get; set; }
		public int? DestinationPoiId { get; set; }
		public string IndoorMapZoneId { get; set; }
		public bool HasProtocols { get; set; }
		public bool HasLinkedCall { get; set; }
		public DateTime? DispatchOn { get; set; }
		public bool HasDispatchList { get; set; }
	}

	/// <summary>
	/// Checks a call against its department's <see cref="NewCallFieldPolicy"/>.
	/// </summary>
	/// <remarks>
	/// This is the point of the whole feature for the customer who asked for it: a call-taker must not
	/// be able to forward an incident to the field until the information the crews need is actually
	/// on it. Clients enforce the same policy for usability, but a client can be old, offline-queued
	/// or simply another integration -- so the server decides.
	/// </remarks>
	public static class NewCallFieldPolicyValidator
	{
		/// <summary>
		/// Returns the required fields the submitted call left blank. Empty means the call may proceed.
		/// </summary>
		public static List<NewCallFieldViolation> Validate(NewCallFieldPolicy policy, NewCallFieldValues values)
		{
			var violations = new List<NewCallFieldViolation>();

			if (policy == null || policy.IsEmpty || values == null)
				return violations;

			void RequireText(string key, string value)
			{
				if (policy.IsRequired(key) && string.IsNullOrWhiteSpace(value))
					violations.Add(new NewCallFieldViolation { Key = key });
			}

			void Require(string key, bool hasValue)
			{
				if (policy.IsRequired(key) && !hasValue)
					violations.Add(new NewCallFieldViolation { Key = key });
			}

			RequireText(NewCallFieldKeys.Note, values.Note);
			RequireText(NewCallFieldKeys.Address, values.Address);
			RequireText(NewCallFieldKeys.Geolocation, values.Geolocation);
			RequireText(NewCallFieldKeys.What3Words, values.What3Words);
			RequireText(NewCallFieldKeys.PlusCode, values.PlusCode);
			RequireText(NewCallFieldKeys.ContactName, values.ContactName);
			RequireText(NewCallFieldKeys.ContactInfo, values.ContactInfo);
			RequireText(NewCallFieldKeys.ExternalId, values.ExternalId);
			RequireText(NewCallFieldKeys.IncidentId, values.IncidentId);
			RequireText(NewCallFieldKeys.ReferenceId, values.ReferenceId);
			RequireText(NewCallFieldKeys.IndoorLocation, values.IndoorMapZoneId);

			Require(NewCallFieldKeys.DestinationPoi, values.DestinationPoiId.HasValue && values.DestinationPoiId.Value > 0);
			Require(NewCallFieldKeys.Protocols, values.HasProtocols);
			Require(NewCallFieldKeys.LinkedCall, values.HasLinkedCall);
			Require(NewCallFieldKeys.DispatchOn, values.DispatchOn.HasValue);
			Require(NewCallFieldKeys.DispatchList, values.HasDispatchList);

			return violations;
		}

		/// <summary>
		/// One-line summary for an API error body or a form validation message.
		/// </summary>
		public static string DescribeViolations(List<NewCallFieldViolation> violations)
		{
			if (violations == null || violations.Count == 0)
				return string.Empty;

			return "Required call fields are missing: " + string.Join(", ", violations.ConvertAll(x => x.Key));
		}
	}
}
