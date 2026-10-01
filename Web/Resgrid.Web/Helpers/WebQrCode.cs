using System;
using QRCoder;

namespace Resgrid.Web.Helpers
{
	/// <summary>An authenticator setup URI as an inline PNG QR code, rendered on the server so the secret never leaves the page.</summary>
	public static class WebQrCode
	{
		public static string DataUrl(string uri)
		{
			using var generator = new QRCodeGenerator();
			using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
			using var code = new PngByteQRCode(data);
			return $"data:image/png;base64,{Convert.ToBase64String(code.GetGraphic(5))}";
		}
	}
}
