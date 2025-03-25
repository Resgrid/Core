using Newtonsoft.Json.Linq;
using Resgrid.Config;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;

namespace Resgrid.WebCore.Attributes
{
	public class GoogleReCaptchaValidationAttribute : ValidationAttribute
	{

		protected override ValidationResult IsValid(object value, ValidationContext validationContext)
		{
			Lazy<ValidationResult> errorResult = new Lazy<ValidationResult>(() => new ValidationResult("Google reCAPTCHA validation failed", new String[] { validationContext.MemberName }));

			if (value == null || String.IsNullOrWhiteSpace(value.ToString()))
			{
				return errorResult.Value;
			}

			String reCaptchResponse = value.ToString();
			String reCaptchaSecret = WebConfig.RecaptchaPrivateKey;


			HttpClient httpClient = new HttpClient();
			var httpResponse = httpClient.GetAsync($"https://www.google.com/recaptcha/api/siteverify?secret={reCaptchaSecret}&response={reCaptchResponse}").Result;
			if (httpResponse.StatusCode != HttpStatusCode.OK)
			{
				return errorResult.Value;
			}

			String jsonResponse = httpResponse.Content.ReadAsStringAsync().Result;
			dynamic jsonData = JObject.Parse(jsonResponse);
			if (jsonData.success != true.ToString().ToLower())
			{
				return errorResult.Value;
			}

			return ValidationResult.Success;

		}
	}
}
