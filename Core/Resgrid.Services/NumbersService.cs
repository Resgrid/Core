using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class NumbersService : INumbersService
	{
		private readonly INumberProvider _numberProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IInboundMessageEventRepository _inboundMessageEventRepository;

		public NumbersService(INumberProvider numberProvider, IDepartmentSettingsService departmentSettingsService,
			IInboundMessageEventRepository inboundMessageEventRepository)
		{
			_numberProvider = numberProvider;
			_departmentSettingsService = departmentSettingsService;
			_inboundMessageEventRepository = inboundMessageEventRepository;
		}

		public async Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode)
		{
			return await _numberProvider.GetAvailableNumbers(country, areaCode);
		}

		public async Task<bool> ProvisionNumberAsync(int departmentId, string number, string country)
		{
			var numberProvisioned = await _numberProvider.ProvisionNumber(country, number);

			if (numberProvisioned)
			{
				var provisionedNumber = number.Replace("+", "").Replace("-", "").Replace(".", "").Replace(" ", "").Trim();
				await _departmentSettingsService.SaveOrUpdateSettingAsync(departmentId, provisionedNumber, DepartmentSettingTypes.TextToCallNumber);
			}

			return numberProvisioned;
		}

		public async Task<InboundMessageEvent> SaveInboundMessageEventAsync(InboundMessageEvent messageEvent, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _inboundMessageEventRepository.SaveOrUpdateAsync(messageEvent, cancellationToken);
		}

		public bool DoesNumberMatchAnyPattern(List<string> patterns, string number)
		{
			var values = patterns.Select(pattern => IsNumberPatternValid(pattern, number)).ToList();

			if (values.Any(x => x == true))
				return true;

			return false;
		}

		public bool IsNumberPatternValid(string pattern, string number)
		{
			var returnValue = true;

			string numberToTest =
				number.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Replace("+", "").Trim();
			string patternToTest =
				pattern.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Replace("+", "").Trim();


			// Lengths should be equal
			if (numberToTest.Length == patternToTest.Length)
			{
				returnValue = TestNumber(numberToTest, patternToTest);
			}
			else
			{
				if (numberToTest.Length == 11 && numberToTest[0] == char.Parse("1"))
				{
					numberToTest = numberToTest.Remove(0, 1);
					returnValue = TestNumber(numberToTest, patternToTest);
				}
				else
					returnValue = false;
			}

			return returnValue;
		}

		private bool TestNumber(string numberToTest, string patternToTest)
		{
			if (numberToTest.Length == patternToTest.Length)
			{
				for (int i = 0; i < numberToTest.Length; i++)
				{
					var patternChar = patternToTest[i];
					var numberChar = numberToTest[i];

					if (patternChar != char.Parse("*") && patternChar != numberChar)
						return false;
				}
			}
			else
				return false;

			return true;
		}
	}
}
