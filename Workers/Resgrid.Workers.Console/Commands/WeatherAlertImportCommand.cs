using Quidjibo.Commands;
using System;
using System.Collections.Generic;

namespace Resgrid.Workers.Console.Commands
{
	public class WeatherAlertImportCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public WeatherAlertImportCommand(int id)
		{
			Id = id;
		}
	}
}
