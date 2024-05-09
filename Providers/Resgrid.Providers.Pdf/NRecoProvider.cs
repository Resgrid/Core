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
		{
			var converter = new HtmlToPdfConverter();

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
