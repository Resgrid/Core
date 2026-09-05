using System;
using NReco.PdfGenerator;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider
{
	public class NRecoProvider : IPdfProvider
	{
		public byte[] ConvertHtmlToPdf(string html)
			=> Convert(html, null);

		public byte[] ConvertHtmlToPdf(string html, string pageSize)
			=> Convert(html, pageSize);

		private byte[] Convert(string html, string pageSize)
		{
			var converter = new HtmlToPdfConverter();
			if (pageSize != null)
			{
				converter.Size = string.Equals(pageSize, "A4", StringComparison.OrdinalIgnoreCase) ? PageSize.A4 : PageSize.Letter;
				// RMS generates escaped, self-contained HTML. Disable script execution and local-file access; the generated template contains no remote assets.
				converter.CustomWkHtmlArgs = "--disable-javascript --disable-local-file-access --footer-right \"Page [page] of [topage]\" --footer-font-size 8";
				converter.ExecutionTimeout = TimeSpan.FromSeconds(60);
			}

			if (OS.IsLinux() || OS.IsMacOS())
			{
				converter.WkHtmlToPdfExeName = "wkhtmltopdf";
				converter.PdfToolPath = "/usr/local/bin/";
			}
			else
			{
				converter.WkHtmlToPdfExeName = "wkhtmltopdf.exe";
				converter.PdfToolPath = "C:\\Program Files\\wkhtmltopdf\\bin\\";
			}

			if (!String.IsNullOrWhiteSpace(PrintConfig.NRecoPdfOwner) && !String.IsNullOrWhiteSpace(PrintConfig.NRecoPdfKey))
				converter.License.SetLicenseKey(PrintConfig.NRecoPdfOwner, PrintConfig.NRecoPdfKey);

			//converter.Quiet = false;
			//converter.LogReceived += (sender, e) =>
			//{
			//	Debug.WriteLine("WkHtmlToPdf Log: {0}", e.Data);
			//};

			return converter.GeneratePdf(html);
		}
	}
}
