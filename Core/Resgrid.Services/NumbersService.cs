using System.Collections.Generic;
using System.Linq;
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
		private readonly IGenericDataRepository<InboundMessageEvent> _inboundMessageEventRepository;

		public NumbersService(INumberProvider numberProvider, IDepartmentSettingsService departmentSettingsService,
			IGenericDataRepository<InboundMessageEvent> inboundMessageEventRepository)
		{
			_numberProvider = numberProvider;
			_departmentSettingsService = departmentSettingsService;
			_inboundMessageEventRepository = inboundMessageEventRepository;
		}

		public List<TextNumber> GetAvailableNumbers(string country, string areaCode)
		{
			return _numberProvider.GetAvailableNumbers(country, areaCode);
		}

		public bool ProvisionNumber(int departmentId, string number, string country)
		{
			var numberProvisioned = _numberProvider.ProvisionNumber(country, number);

			if (numberProvisioned)
			{
				var provisionedNumber = number.Replace("+", "").Replace("-", "").Replace(".", "").Replace(" ", "").Trim();
				_departmentSettingsService.SaveOrUpdateSetting(departmentId, provisionedNumber, DepartmentSettingTypes.TextToCallNumber);
			}

			return numberProvisioned;
		}

		public InboundMessageEvent SaveInboundMessageEvent(InboundMessageEvent messageEvent)
		{
			_inboundMessageEventRepository.SaveOrUpdate(messageEvent);

			return messageEvent;
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