using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;

namespace Resgrid.Providers.Import.Helpdesk
{
	/// <summary>
	/// Freshdesk REST v2 client for Department Import support requests (wizard plan section 17.5). Tickets carry metadata
	/// only (conformance I15): the caller builds the description from the department, category, the administrator's own
	/// text, issue codes and counts and the BackOffice link, never source values, file names or staged data. Without
	/// SupportConfig.FreshdeskDomain and FreshdeskApiKey the provider reports itself unconfigured and requests stay in-app.
	/// </summary>
	public sealed class FreshdeskHelpdeskProvider : IHelpdeskProvider
	{
		private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		private static readonly Regex DomainPattern = new Regex(@"^[a-z0-9][a-z0-9-]*\.freshdesk\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private readonly HttpClient _http;

		public FreshdeskHelpdeskProvider() : this(SharedHttpClient) { }

		public FreshdeskHelpdeskProvider(HttpClient http)
		{
			_http = http;
		}

		/// <summary>Configured only for a *.freshdesk.com domain: the API key is never sent anywhere else.</summary>
		public bool IsConfigured => !string.IsNullOrWhiteSpace(SupportConfig.FreshdeskApiKey) && DomainPattern.IsMatch((SupportConfig.FreshdeskDomain ?? string.Empty).Trim());

		public async Task<HelpdeskTicketResult> CreateTicketAsync(HelpdeskTicketRequest request, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured)
				return new HelpdeskTicketResult { ErrorCode = "not-configured" };

			var body = new
			{
				email = request.RequesterEmail,
				name = request.RequesterName,
				subject = Truncate(request.Subject, 255),
				// Freshdesk renders the description as HTML: encode the plain text and keep its line breaks.
				description = WebUtility.HtmlEncode(request.Description ?? string.Empty).Replace("\n", "<br>"),
				priority = Math.Clamp(request.Priority + 1, 1, 4),
				status = 2,
				source = 2,
				tags = request.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Take(10).ToArray() ?? Array.Empty<string>(),
				group_id = long.TryParse(SupportConfig.FreshdeskGroupId, out var group) ? group : (long?)null
			};

			try
			{
				using var message = Request(HttpMethod.Post, "/api/v2/tickets", body);
				using var response = await _http.SendAsync(message, cancellationToken);
				if (!response.IsSuccessStatusCode)
					return Failure(response.StatusCode);

				using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
				var id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;
				return new HelpdeskTicketResult
				{
					Success = id != null,
					TicketId = id,
					TicketUrl = id == null ? null : $"https://{SupportConfig.FreshdeskDomain.Trim()}/a/tickets/{id}",
					ErrorCode = id == null ? "no-ticket-id" : null
				};
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return new HelpdeskTicketResult { Transient = true, ErrorCode = "timeout" };
			}
			catch (HttpRequestException ex)
			{
				Logging.LogException(ex, "Freshdesk ticket creation failed");
				return new HelpdeskTicketResult { Transient = true, ErrorCode = "unreachable" };
			}
		}

		public async Task<bool> AddNoteAsync(string ticketId, string body, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured || string.IsNullOrWhiteSpace(ticketId) || !long.TryParse(ticketId, out _))
				return false;

			try
			{
				using var message = Request(HttpMethod.Post, $"/api/v2/tickets/{ticketId}/notes",
					new { body = WebUtility.HtmlEncode(body ?? string.Empty).Replace("\n", "<br>"), @private = true });
				using var response = await _http.SendAsync(message, cancellationToken);
				return response.IsSuccessStatusCode;
			}
			catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
			{
				Logging.LogException(ex, "Freshdesk note failed");
				return false;
			}
		}

		private static HttpRequestMessage Request(HttpMethod method, string path, object body)
		{
			var message = new HttpRequestMessage(method, $"https://{SupportConfig.FreshdeskDomain.Trim()}{path}")
			{
				Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
			};
			// Freshdesk API authentication: the API key as the basic-auth user with any password.
			var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(SupportConfig.FreshdeskApiKey.Trim() + ":X"));
			message.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
			return message;
		}

		private static HelpdeskTicketResult Failure(HttpStatusCode status) => new HelpdeskTicketResult
		{
			Transient = status == HttpStatusCode.TooManyRequests || (int)status >= 500,
			ErrorCode = "http-" + (int)status
		};

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
