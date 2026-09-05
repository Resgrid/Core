namespace Resgrid.Model.Providers
{
	public interface IPdfProvider
	{
		byte[] ConvertHtmlToPdf(string html);
		/// <summary>Departmental Records require an explicit page size; older providers retain their existing default.</summary>
		byte[] ConvertHtmlToPdf(string html, string pageSize) => ConvertHtmlToPdf(html);
	}
}
