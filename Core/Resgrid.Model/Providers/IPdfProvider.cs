namespace Resgrid.Model.Providers
{
	public interface IPdfProvider
	{
		byte[] ConvertHtmlToPdf(string html);
	}
}