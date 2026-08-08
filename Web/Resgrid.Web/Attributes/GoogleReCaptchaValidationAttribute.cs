using Newtonsoft.Json.Linq;
using Resgrid.Config;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Resgrid.WebCore.Attributes
{
	public class GoogleReCaptchaValidationAttribute : ValidationAttribute
	{
		// Shared client: a new HttpClient per validation leaks sockets under load ("Resource
		// temporarily unavailable" on the register form). Validation attributes are synchronous,
		// so the call is bounded by a short timeout instead of the 100-second default.
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(10)
		};

		protected override ValidationResult IsValid(object value, ValidationContext validationContext)
		{
			Lazy<ValidationResult> errorResult = new Lazy<ValidationResult>(() => new ValidationResult("Google reCAPTCHA validation failed", new String[] { validationContext.MemberName }));

			if (value == null || String.IsNullOrWhiteSpace(value.ToString()))
			{
				return errorResult.Value;
			}

			String reCaptchResponse = value.ToString();
			String reCaptchaSecret = WebConfig.RecaptchaPrivateKey;

			try
			{
				// POST keeps the secret out of URLs (request logs, proxies).
				using var content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["secret"] = reCaptchaSecret,
					["response"] = reCaptchResponse
				});

				var httpResponse = _httpClient.PostAsync("https://www.google.com/recaptcha/api/siteverify", content).GetAwaiter().GetResult();
				if (httpResponse.StatusCode != HttpStatusCode.OK)
				{
					return errorResult.Value;
				}

				String jsonResponse = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				dynamic jsonData = JObject.Parse(jsonResponse);
				if (jsonData.success != true.ToString().ToLower())
				{
					return errorResult.Value;
				}

				return ValidationResult.Success;
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
			{
				// Transient network/DNS failure reaching Google: fail closed with a retryable
				// validation message instead of letting the exception 500 the register page.
				Framework.Logging.LogException(ex);
				return new ValidationResult("We couldn't verify the reCAPTCHA right now. Please try again.", new String[] { validationContext.MemberName });
			}
		}
	}
}
