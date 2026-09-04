using System;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// What an operational email needs to render a department masthead (RMS plan section 4.10.1, "Replacing the
	/// Resgrid logo in emails"). The identity fields are always filled from the profile or the Department row;
	/// the masthead itself is only <see cref="Enabled"/> when the department has a logo AND opted in with
	/// UseDepartmentBrandingInEmails. A disabled instance renders today's Resgrid chrome unchanged.
	/// </summary>
	[ProtoContract]
	public class DepartmentEmailBranding
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		/// <summary>True only when an EmailMasthead rendition exists and the department opted in.</summary>
		[ProtoMember(2)]
		public bool Enabled { get; set; }

		/// <summary>Profile display name, falling back to the Department row name.</summary>
		[ProtoMember(3)]
		public string DisplayName { get; set; }

		/// <summary>Anonymous, MediaKey-addressed EmailMasthead URL; null when the masthead is not enabled.</summary>
		[ProtoMember(4)]
		public string LogoUrl { get; set; }

		/// <summary>Absolute http(s) website from the profile, or null when none is set or it is not a web URL.</summary>
		[ProtoMember(5)]
		public string Website { get; set; }

		public static DepartmentEmailBranding Disabled(int departmentId, string displayName = null)
		{
			return new DepartmentEmailBranding { DepartmentId = departmentId, DisplayName = displayName };
		}

		/// <summary>
		/// Turns whatever an admin typed into the profile Website field into an absolute http(s) URL, or null
		/// when it is not one. The value lands in an href, so anything that is not a plain web address
		/// (other schemes, credentials in the authority, bare words) is dropped rather than linked.
		/// </summary>
		public static string NormalizeWebsite(string website)
		{
			if (string.IsNullOrWhiteSpace(website))
				return null;

			var text = website.Trim();

			if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !IsWebScheme(uri))
			{
				if (text.Contains("://") || text.StartsWith("//"))
					return null;

				if (!Uri.TryCreate("https://" + text, UriKind.Absolute, out uri) || !IsWebScheme(uri))
					return null;
			}

			if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host) || !uri.Host.Contains('.'))
				return null;

			return uri.AbsoluteUri;
		}

		private static bool IsWebScheme(Uri uri)
		{
			return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
		}
	}
}
