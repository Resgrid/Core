using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.WebCore.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Models
{
	public abstract class GoogleReCaptchaModelBase
	{
		// No [Required]: the validation below refuses an empty answer itself, with a message a person can act on (Required added a
		// second one naming the field, "The GoogleReCaptchaResponse field is required.").
		[GoogleReCaptchaValidation]
		[JsonProperty("g-recaptcha-response")]
		public String GoogleReCaptchaResponse { get; set; }
	}
}
