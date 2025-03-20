using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.WebCore.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Models
{
	public abstract class GoogleReCaptchaModelBase
	{
		[Required]
		[GoogleReCaptchaValidation]
		[JsonProperty("g-recaptcha-response")]
		public String GoogleReCaptchaResponse { get; set; }
	}
}
