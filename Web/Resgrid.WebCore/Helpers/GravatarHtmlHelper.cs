using System;
using System.Text;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Reflection;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Web;

namespace Resgrid.Web.Helpers
{

	/// <summary>
	/// Globally Recognised Avatar - http://gravatar.com
	/// </summary>
	/// <remarks>
	/// This implementation by Andrew Freemantle - http://www.fatlemon.co.uk/
	/// <para>Source, Wiki and Issues: https://github.com/AndrewFreemantle/Gravatar-HtmlHelper </para>
	/// </remarks>
	public static class GravatarHtmlHelper
	{


		/// <summary>
		/// In addition to allowing you to use your own image, Gravatar has a number of built in options which you can also use as defaults. Most of these work by taking the requested email hash and using it to generate a themed image that is unique to that email address
		/// </summary>
		public enum DefaultImage
		{
			/// <summary>Default Gravatar logo</summary>
			[DescriptionAttribute("")] Default,

			/// <summary>404 - do not load any image if none is associated with the email hash, instead return an HTTP 404 (File Not Found) response</summary>
			[DescriptionAttribute("404")] Http404,

			/// <summary>Mystery-Man - a simple, cartoon-style silhouetted outline of a person (does not vary by email hash)</summary>
			[DescriptionAttribute("mm")] MysteryMan,

			/// <summary>Identicon - a geometric pattern based on an email hash</summary>
			[DescriptionAttribute("identicon")] Identicon,

			/// <summary>MonsterId - a generated 'monster' with different colors, faces, etc</summary>
			[DescriptionAttribute("monsterid")] MonsterId,

			/// <summary>Wavatar - generated faces with differing features and backgrounds</summary>
			[DescriptionAttribute("wavatar")] Wavatar,

			/// <summary>Retro - awesome generated, 8-bit arcade-style pixelated faces</summary>
			[DescriptionAttribute("retro")] Retro
		}


		/// <summary>
		/// Gravatar allows users to self-rate their images so that they can indicate if an image is appropriate for a certain audience. By default, only 'G' rated images are displayed unless you indicate that you would like to see higher ratings
		/// </summary>
		public enum Rating
		{
			/// <summary>Suitable for display on all websites with any audience type</summary>
			[DescriptionAttribute("g")] G,

			/// <summary>May contain rude gestures, provocatively dressed individuals, the lesser swear words, or mild violence</summary>
			[DescriptionAttribute("pg")] PG,

			/// <summary>May contain such things as harsh profanity, intense violence, nudity, or hard drug use</summary>
			[DescriptionAttribute("r")] R,

			/// <summary>May contain hardcore sexual imagery or extremely disturbing violence</summary>
			[DescriptionAttribute("x")] X
		}


		/// <summary>
		/// Returns a Globally Recognised Avatar as an &lt;img /&gt; - http://gravatar.com
		/// </summary>
		/// <param name="emailAddress">Email Address for the Gravatar</param>
		/// <param name="defaultImage">Default image if user hasn't created a Gravatar</param>
		/// <param name="size">Size in pixels (default: 80)</param>
		/// <param name="defaultImageUrl">URL to a custom default image (e.g: 'Url.Content("~/images/no-grvatar.png")' )</param>
		/// <param name="forceDefaultImage">Prefer the default image over the users own Gravatar</param>
		/// <param name="rating">Gravatar content rating (note that Gravatars are self-rated)</param>
		/// <param name="forceSecureRequest">Always do secure (https) requests</param>
		public static HtmlString GravatarImage(
			this HtmlHelper htmlHelper,
			string emailAddress,
			int size = 80,
			DefaultImage defaultImage = DefaultImage.Default,
			string defaultImageUrl = "",
			bool forceDefaultImage = false,
			Rating rating = Rating.G,
			bool forceSecureRequest = false)
		{

			var imgTag = new TagBuilder("img");

			emailAddress = string.IsNullOrEmpty(emailAddress) ? string.Empty : emailAddress.Trim().ToLower();

			imgTag.Attributes.Add("src",
			                      string.Format("https://secure.gravatar.com/avatar/{0}?s={1}{2}{3}{4}",
			                                    GetMd5Hash(emailAddress),
			                                    size.ToString(),
			                                    "&d=" +
			                                    (!string.IsNullOrEmpty(defaultImageUrl)
				                                     ? HttpUtility.UrlEncode(defaultImageUrl)
				                                     : defaultImage.GetDescription()),
			                                    forceDefaultImage ? "&f=y" : "",
			                                    "&r=" + rating.GetDescription()
				                      )
				);

			imgTag.Attributes.Add("class", "gravatar");
			imgTag.Attributes.Add("alt", "Gravatar image");
			return new HtmlString(imgTag.ToString());
		}




		/// <summary>
		/// Generates an MD5 hash of the given string
		/// </summary>
		/// <remarks>Source: http://msdn.microsoft.com/en-us/library/system.security.cryptography.md5.aspx </remarks>
		private static string GetMd5Hash(string input)
		{

			// Convert the input string to a byte array and compute the hash.
			byte[] data = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(input));

			// Create a new Stringbuilder to collect the bytes
			// and create a string.
			StringBuilder sBuilder = new StringBuilder();

			// Loop through each byte of the hashed data
			// and format each one as a hexadecimal string.
			for (int i = 0; i < data.Length; i++)
			{
				sBuilder.Append(data[i].ToString("x2"));
			}

			// Return the hexadecimal string.
			return sBuilder.ToString();
		}



		/// <summary>
		/// Returns the value of a DescriptionAttribute for a given Enum value
		/// </summary>
		/// <remarks>Source: http://blogs.msdn.com/b/abhinaba/archive/2005/10/21/483337.aspx </remarks>
		/// <param name="en"></param>
		/// <returns></returns>
		private static string GetDescription(this Enum en)
		{

			Type type = en.GetType();
			MemberInfo[] memInfo = type.GetMember(en.ToString());

			if (memInfo != null && memInfo.Length > 0)
			{
				object[] attrs = memInfo[0].GetCustomAttributes(typeof (DescriptionAttribute), false);

				if (attrs != null && attrs.Length > 0)
					return ((DescriptionAttribute) attrs[0]).Description;
			}

			return en.ToString();

		}
	}
}
