using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	/// <summary>Body of POST Mapping/SignMapResourceUrls.</summary>
	public class SignMapResourceUrlsInput
	{
		/// <summary>Custom map tile/image or indoor floor image URLs (templates with {z}/{x}/{y} allowed).</summary>
		public List<string> Urls { get; set; } = new List<string>();
	}

	public class SignMapResourceUrlsResult : StandardApiResponseV4Base
	{
		public List<SignedMapResourceUrl> Data { get; set; } = new List<SignedMapResourceUrl>();
	}

	public class SignedMapResourceUrl
	{
		/// <summary>The URL as sent.</summary>
		public string Url { get; set; }

		/// <summary>The URL with an expiring signature, or null when it is not a map resource of the caller's department.</summary>
		public string SignedUrl { get; set; }
	}
}
