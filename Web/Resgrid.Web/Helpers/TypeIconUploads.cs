using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Reads the custom icon posted by the unit type, call type, POI type and personnel role pages. The browser picker
	/// checks the same limits first; this is the server-side check, with the same localized messages.
	/// </summary>
	public static class TypeIconUploads
	{
		/// <summary>The form field the icon pages post the file in.</summary>
		public const string FileField = "IconFile";

		/// <summary>
		/// The posted icon's bytes, or null when none was posted. A file that cannot be an icon adds a localized model error
		/// on <paramref name="field"/> and returns null, so nothing is saved.
		/// </summary>
		public static async Task<byte[]> ReadAsync(IFormFile file, ITypeIconsService typeIconsService, ModelStateDictionary modelState,
			IStringLocalizer localizer, CancellationToken cancellationToken, string field = FileField)
		{
			if (file == null || file.Length == 0)
				return null;

			// Refused before it is buffered: the form size limit is far above an icon's.
			if (file.Length > TypeIcons.MaxUploadBytes)
			{
				modelState.AddModelError(field, RejectionMessage(localizer, TypeIconRejections.TooManyBytes));
				return null;
			}

			byte[] data;
			using (var stream = new MemoryStream((int)file.Length))
			{
				await file.CopyToAsync(stream, cancellationToken);
				data = stream.ToArray();
			}

			var rejection = typeIconsService.ValidateIcon(data);
			if (rejection != TypeIconRejections.None)
			{
				modelState.AddModelError(field, RejectionMessage(localizer, rejection));
				return null;
			}

			return data;
		}

		/// <summary>The localized message for a rejected upload.</summary>
		public static string RejectionMessage(IStringLocalizer localizer, TypeIconRejections rejection)
		{
			var key = rejection switch
			{
				TypeIconRejections.Required => "IconRejectRequired",
				TypeIconRejections.TooManyBytes => "IconRejectTooManyBytes",
				TypeIconRejections.UnsupportedFormat => "IconRejectUnsupportedFormat",
				TypeIconRejections.TooLarge => "IconRejectTooLarge",
				TypeIconRejections.TooSmall => "IconRejectTooSmall",
				_ => "IconRejectUnreadable"
			};

			return localizer[key].Value;
		}
	}
}
