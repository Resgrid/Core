using Resgrid.Model.Providers.Models.PrintNode;
using System.Collections.Generic;

namespace Resgrid.Model.Providers
{
	public interface IPrinterProvider
	{
		Whoami Whoami(string apiKey);
		List<Computer> GetComputers(string apiKey);
		List<Printer> GetPrinters(string apiKey);
		List<PrintJob> GetPrintJobs(string apiKey);
		bool SubmitPrintJob(string apiKey, int printerId, string title, string url);
	}
}
