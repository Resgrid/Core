using System;
using QRCoder;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// A URL as an inline PNG QR code (round 10: the certificate's verification QR), drawn with the same generator, error correction and module
	/// size as the web's WebQrCode, so a certificate downloaded from the app prints the same image the web prints.
	/// </summary>
	public static class ApiQrCode
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
