using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GoogleMapsApi.Entities.Common
{
	/// <summary>
	/// An abstract base class for requests that can be authenticated via URL signing.
	/// </summary>
	/// <remarks>
	/// See https://developers.google.com/maps/documentation/business/webservices for details about signing.
	/// </remarks>
	public abstract class SignableRequest : MapsBaseRequest
	{
		/// <summary>
		/// The client ID provided to you by Google Enterprise Support, or null to disable URL signing. All client IDs begin with a "gme-" prefix.
		/// </summary>
		public string ClientID { get; set; }

		/// <summary>
		/// A cryptographic signing key (secret shared key), in base64url format, provided to you by Google Enterprise Support.
		/// The key will only be used if the ClientID property is set, otherwise it will be ignored.
		/// </summary>
		/// <remarks>
		/// This cryptographic signing key is not the same as the (freely available) Maps API key (typically beginning with 'ABQ..') 
		/// that developers without a Maps API for Business license are required to use when loading the Maps JavaScript API V2 and 
		/// Maps API for Flash, or the keys issued by the Google APIs Console for use with the Google Places API.
		/// </remarks>
		public string SigningKey { get; set; }

		public override Uri GetUri()
		{
			if (ClientID != null)
				return Sign(base.GetUri());

			return base.GetUri();
		}

		/// <remarks>
		/// Based on the C# sample from: https://developers.google.com/maps/documentation/business/webservices
		/// </remarks>
		internal Uri Sign(Uri uri)
		{
			if (uri == null)
				throw new ArgumentNullException("uri");
			if (ClientID == null)
				throw new ArgumentNullException("userID");
			if (string.IsNullOrWhiteSpace(SigningKey))
				throw new ArgumentException("Invalid signing key.");
			if (!ClientID.StartsWith("gme-"))
				throw new ArgumentException("A user ID must start with 'gme-'.");

			var urlSegmentToSign = uri.LocalPath + uri.Query + "&client=" + ClientID;
			byte[] privateKey = FromBase64UrlString(SigningKey);
			byte[] signature;

			using (var algorithm = new HMACSHA1(privateKey))
			{
				signature = algorithm.ComputeHash(Encoding.ASCII.GetBytes(urlSegmentToSign));
			}

			return new Uri(uri.Scheme + "://" + uri.Host + urlSegmentToSign + "&signature=" + ToBase64UrlString(signature));
		}

		private static byte[] FromBase64UrlString(string base64UrlString)
		{
			return Convert.FromBase64String(base64UrlString.Replace("-", "+").Replace("_", "/"));
		}

		private static string ToBase64UrlString(byte[] data)
		{
			return Convert.ToBase64String(data).Replace("+", "-").Replace("/", "_");
		}
	}
}
