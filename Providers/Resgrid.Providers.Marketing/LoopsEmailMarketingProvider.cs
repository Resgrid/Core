using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using RestSharp;

namespace Resgrid.Providers.Marketing
{
	/// <summary>
	/// Email marketing through Loops (loops.so). Contacts are upserted with PUT contacts/update, which creates the
	/// contact when neither its userId nor its email matches one Loops already holds.
	/// </summary>
	public class LoopsEmailMarketingProvider : IEmailMarketingProvider
	{
		private const string ContactSource = "Resgrid";
		private const int MaxAttempts = 3;
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MarketingConfig.LoopsApiKey);

		public async Task<bool> UpsertContactAsync(EmailMarketingContact contact, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured || contact == null)
				return false;

			if (string.IsNullOrWhiteSpace(contact.UserId) && string.IsNullOrWhiteSpace(contact.EmailAddress))
				return false;

			try
			{
				var response = await SendAsync(Method.Put, "contacts/update", BuildContactBody(contact, true), cancellationToken);

				// The stored address is one Loops will never take, so the fallback below would be rejected the same way.
				if (IsInvalidEmailRejection(response.StatusCode, response.Content))
				{
					Logging.LogWarning($"Loops will not accept the email address of user {contact.UserId}, so the contact was not synced: {response.Content}");
					return false;
				}

				// Loops requires custom properties and mailing lists to exist before they are used. If a missing one gets
				// the call rejected, that must not cost us the contact itself, so retry with the default properties only.
				if (response.StatusCode == HttpStatusCode.BadRequest && HasAccountSpecificFields(contact))
				{
					Logging.LogError($"Loops rejected the contact for user {contact.UserId}: {response.Content}. Retrying without the custom properties and mailing list, which must exist in Loops (departmentId, departmentName, isManagingMember).");
					response = await SendAsync(Method.Put, "contacts/update", BuildContactBody(contact, false), cancellationToken);
				}

				if (response.IsSuccessful)
					return true;

				Logging.LogError($"Loops contact update failed for user {contact.UserId}: {(int)response.StatusCode} {response.Content}");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Loops contact update failed for user {contact.UserId}");
			}

			return false;
		}

		public async Task<bool> ContactExistsAsync(string userId, string emailAddress, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured)
				return false;

			try
			{
				// Loops takes one lookup key per call. The user id follows the person across an email change; the email
				// finds a contact that was created before it carried a user id.
				if (!string.IsNullOrWhiteSpace(userId) && await FindAsync("userId", userId, cancellationToken))
					return true;

				if (!string.IsNullOrWhiteSpace(emailAddress) && await FindAsync("email", emailAddress, cancellationToken))
					return true;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Loops contact lookup failed for user {userId}");
			}

			return false;
		}

		private async Task<bool> FindAsync(string key, string value, CancellationToken cancellationToken)
		{
			var response = await SendAsync(Method.Get, "contacts/find", null, cancellationToken, (key, value));

			// No contact can exist under an address Loops will not accept, so this is a clean miss, not a failure.
			if (IsInvalidEmailRejection(response.StatusCode, response.Content))
				return false;

			if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
			{
				Logging.LogError($"Loops contact lookup by {key} failed: {(int)response.StatusCode} {response.Content}");
				return false;
			}

			// Found or not, Loops answers 200 with an array of matching contacts.
			using (var document = JsonDocument.Parse(response.Content))
				return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0;
		}

		internal static Dictionary<string, object> BuildContactBody(EmailMarketingContact contact, bool includeAccountSpecificFields)
		{
			var body = new Dictionary<string, object>
			{
				["source"] = ContactSource
			};

			AddIfPresent(body, "userId", contact.UserId);
			AddIfPresent(body, "email", contact.EmailAddress?.Trim());
			AddIfPresent(body, "firstName", contact.FirstName);
			AddIfPresent(body, "lastName", contact.LastName);
			AddIfPresent(body, "userGroup", contact.UserGroup);

			// Omitted unless the value is meant: Loops re-subscribes a contact on subscribed = true.
			if (contact.Subscribed.HasValue)
				body["subscribed"] = contact.Subscribed.Value;

			if (includeAccountSpecificFields)
			{
				if (contact.DepartmentId.HasValue)
					body["departmentId"] = contact.DepartmentId.Value;

				AddIfPresent(body, "departmentName", contact.DepartmentName);

				if (contact.IsManagingMember.HasValue)
					body["isManagingMember"] = contact.IsManagingMember.Value;

				var mailingLists = contact.MailingListIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
				if (mailingLists != null && mailingLists.Length > 0)
					body["mailingLists"] = mailingLists.ToDictionary(x => x.Trim(), x => true);
			}

			return body;
		}

		/// <summary>
		/// Loops validates address syntax itself and answers 400 "Invalid email address" for one it will not accept, such as
		/// a member's placeholder address or the masked one a deleted login carries. That is the stored data, not a fault
		/// here, and no retry will change it.
		/// </summary>
		internal static bool IsInvalidEmailRejection(HttpStatusCode statusCode, string content)
		{
			return statusCode == HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(content) &&
				content.IndexOf("invalid email", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool HasAccountSpecificFields(EmailMarketingContact contact)
		{
			return contact.DepartmentId.HasValue || !string.IsNullOrWhiteSpace(contact.DepartmentName) || contact.IsManagingMember.HasValue ||
				(contact.MailingListIds != null && contact.MailingListIds.Any(x => !string.IsNullOrWhiteSpace(x)));
		}

		private static void AddIfPresent(Dictionary<string, object> body, string key, string value)
		{
			if (!string.IsNullOrWhiteSpace(value))
				body[key] = value;
		}

		private static async Task<RestResponse> SendAsync(Method method, string resource, Dictionary<string, object> body, CancellationToken cancellationToken,
			(string Key, string Value)? query = null)
		{
			using var client = new RestClient(new RestClientOptions(MarketingConfig.LoopsApiUrl) { Timeout = RequestTimeout });

			RestResponse response = null;
			for (var attempt = 1; attempt <= MaxAttempts; attempt++)
			{
				var request = new RestRequest(resource, method);
				request.AddHeader("Authorization", $"Bearer {MarketingConfig.LoopsApiKey}");

				if (query.HasValue)
					request.AddQueryParameter(query.Value.Key, query.Value.Value);

				if (body != null)
					request.AddJsonBody(body);

				response = await client.ExecuteAsync(request, cancellationToken);

				// Loops allows 10 requests a second per team and asks for a backed-off retry on 429.
				var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500 ||
					response.ResponseStatus != ResponseStatus.Completed;

				if (!transient || attempt == MaxAttempts)
					break;

				await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
			}

			return response;
		}
	}
}
