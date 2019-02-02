using System.Diagnostics;
using NReco.PdfGenerator;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider
{
	public class NRecoProvider : IPdfProvider
	{
		public byte[] ConvertHtmlToPdf(string html)
		{
			var converter = new HtmlToPdfConverter();
			//converter.Quiet = false;
			//converter.LogReceived += (sender, e) =>
			//{
			//	Debug.WriteLine("WkHtmlToPdf Log: {0}", e.Data);
			//};

			return converter.GeneratePdf(html);
		}
	}
}