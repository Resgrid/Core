using Newtonsoft.Json;
using Resgrid.Model.Providers;
using Resgrid.Model.Providers.Models.PrintNode;
using RestSharp;
using RestSharp.Authenticators;
using System.Collections.Generic;
using System.Net;

namespace Resgrid.Providers.PdfProvider
{
	public class PrintNodeProvider: IPrinterProvider
	{
		public Whoami Whoami(string apiKey)
		{
			var client = new RestClient(Config.PrintConfig.PrintNodeBaseUrl);
			client.Authenticator = new HttpBasicAuthenticator(apiKey, "");

			var request = new RestRequest("/whoami", Method.GET);

			var response = client.Execute(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<Whoami>(response.Content);

			return null;
		}

		public List<Computer> GetComputers(string apiKey)
		{
			var client = new RestClient(Config.PrintConfig.PrintNodeBaseUrl);
			client.Authenticator = new HttpBasicAuthenticator(apiKey, "");

			var request = new RestRequest("/computers", Method.GET);

			var response = client.Execute(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<List<Computer>>(response.Content);

			return null;
		}

		public List<Printer> GetPrinters(string apiKey)
		{
			var client = new RestClient(Config.PrintConfig.PrintNodeBaseUrl);
			client.Authenticator = new HttpBasicAuthenticator(apiKey, "");

			var request = new RestRequest("/printers", Method.GET);

			var response = client.Execute(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<List<Printer>>(response.Content);

			return null;
		}

		public List<PrintJob> GetPrintJobs(string apiKey)
		{
			var client = new RestClient(Config.PrintConfig.PrintNodeBaseUrl);
			client.Authenticator = new HttpBasicAuthenticator(apiKey, "");

			var request = new RestRequest("/printjobs", Method.GET);

			var response = client.Execute(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<List<PrintJob>>(response.Content);

			return null;

			//return "";//Get("PrintJobs");
		}

		public bool SubmitPrintJob(string apiKey, int printerId, string title, string url)
		{
			var client = new RestClient(Config.PrintConfig.PrintNodeBaseUrl);
			client.Authenticator = new HttpBasicAuthenticator(apiKey, "");

			var request = new RestRequest("/printjobs", Method.POST);
			//request.AddHeader("Accept", "application/json");
			//request.AddHeader("Content-Type", "application/json; charset=utf-8");
			request.AddHeader("Content-type", "application/json");

			var body = JsonConvert.SerializeObject(new
			{
				printerId = printerId,
				title = title,
				contentType = "pdf_uri",
				content = url,
				source = "Resgrid Print Job"
			});

			request.AddParameter("application/json", body, "application/json; charset=utf-8", ParameterType.RequestBody);

			//request.AddJsonBody(new
			//{
			//	printerId = printerId,
			//	title = title,
			//	contentType = "raw_uri",
			//	content = url,
			//	source = "Resgrid Print Job"
			//});

			var response = client.Execute(request);


			if (response.StatusCode == HttpStatusCode.Created)
				return true;

			return false;
		}

	}
}
