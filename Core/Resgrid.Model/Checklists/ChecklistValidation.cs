using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.Checklists
{
	public static class ChecklistValidation
	{
		public static List<string> Validate(ChecklistForm form)
		{
			var errors = new List<string>();
			if (form == null) { errors.Add("A definition is required."); return errors; }
			if (string.IsNullOrWhiteSpace(form.Name) || form.Name.Length > 200) errors.Add("Name is required and must be at most 200 characters.");
			if (form.Instructions?.Length > 10000) errors.Add("Instructions are too long.");
			if (!Enum.IsDefined(typeof(ChecklistCategory), form.Category) || !new[] { ChecklistTargetType.Department, ChecklistTargetType.Unit, ChecklistTargetType.Group, ChecklistTargetType.Personnel }.Contains(form.TargetType)) errors.Add("Choose a supported category and target type.");
			if (form.PassThreshold < 0 || form.PassThreshold > 100) errors.Add("Pass threshold must be between 0 and 100.");
			if (form.Sections == null || form.Sections.Count == 0 || form.Sections.Count > 30) { errors.Add("Use between 1 and 30 sections."); return errors; }
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var prior = new Dictionary<string, ChecklistItem>(StringComparer.OrdinalIgnoreCase);
			int count = 0;
			foreach (var section in form.Sections)
			{
				if (section == null || !Guid.TryParseExact(section.Id, "D", out _) || !seen.Add(section.Id) || string.IsNullOrWhiteSpace(section.Name) || section.Name.Length > 200) { errors.Add("Sections need unique GUIDs and names (at most 200 characters)."); continue; }
				if (section.Items == null || section.Items.Count == 0) { errors.Add("Every section needs an item."); continue; }
				foreach (var item in section.Items)
				{
					count++;
					if (item == null || !Guid.TryParseExact(item.Id, "D", out _) || !seen.Add(item.Id)) { errors.Add("Items need unique GUIDs."); continue; }
					if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 300 || item.Instructions?.Length > 5000 || item.Units?.Length > 50) errors.Add("Item names, instructions or units exceed their limits.");
					if (!Enum.IsDefined(typeof(ChecklistItemType), item.Type) || item.Weight < 0 || item.Weight > 1000 || item.Critical && item.Weight == 0) errors.Add("Choose a valid item type and weight; critical items need a positive weight.");
					if (item.Minimum > item.Maximum) errors.Add("Minimum must not exceed maximum.");
					if ((item.Type == ChecklistItemType.NumericReading || item.Type == ChecklistItemType.Quantity) && !item.Minimum.HasValue && !item.Maximum.HasValue) errors.Add("Numeric items need at least one passing bound.");
					if ((item.Type == ChecklistItemType.YesNo || item.Type == ChecklistItemType.Checkbox) && item.PassingValue != "true" && item.PassingValue != "false") errors.Add("Boolean items need an explicit true or false passing value.");
					if (item.Options == null || item.Options.Count > 50 || item.Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 200) || item.Options.Distinct(StringComparer.Ordinal).Count() != item.Options.Count) errors.Add("Options must be distinct, nonempty and bounded.");
					else if (item.Type == ChecklistItemType.SelectList && (item.Options.Count < 2 || !item.Options.Contains(item.PassingValue))) errors.Add("Select lists need options and a passing option.");
					foreach (var condition in new[] { item.VisibleWhen, item.RequiredWhen }.Where(c => c != null))
					{
						if (condition.ItemId == null || !prior.TryGetValue(condition.ItemId, out var dependency) || string.IsNullOrEmpty(condition.EqualsValue) || condition.EqualsValue.Length > 200) errors.Add("Conditions must reference an earlier item and an explicit value.");
						else if (dependency.VisibleWhen != null || dependency.RequiredWhen != null) errors.Add("Conditional chains are limited to one level.");
						else if (!ConditionValueValid(dependency, condition.EqualsValue)) errors.Add("A condition must match a value its source item can produce.");
					}
					prior[item.Id] = item;
				}
			}
			if (count > 250) errors.Add("A checklist can have at most 250 items.");
			return errors.Distinct().ToList();
		}

		private static bool ConditionValueValid(ChecklistItem item, string value)
		{
			switch (item.Type)
			{
				case ChecklistItemType.PassFail: return value == "pass" || value == "fail";
				case ChecklistItemType.YesNo: case ChecklistItemType.Checkbox: return value == "true" || value == "false";
				case ChecklistItemType.SelectList: return item.Options?.Contains(value) == true;
				case ChecklistItemType.NumericReading: case ChecklistItemType.Quantity: return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && (item.Type != ChecklistItemType.Quantity || number >= 0);
				case ChecklistItemType.DateValue: return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
				case ChecklistItemType.FreeText: return !string.IsNullOrWhiteSpace(value);
				default: return false;
			}
		}

		public static bool Matches(ChecklistCondition condition, IReadOnlyDictionary<string, ChecklistAnswer> answers) => condition == null ||
			answers.TryGetValue(condition.ItemId, out var answer) && answer.Status == ChecklistAnswerStatus.Answered && string.Equals(answer.Value, condition.EqualsValue, StringComparison.Ordinal);

		public static ChecklistEvaluation Evaluate(ChecklistForm form, ChecklistRunInput input, ISet<string> evidenceItems, bool final)
		{
			var result = new ChecklistEvaluation();
			if (input?.Answers == null || input.Answers.Count > 250 || input.Answers.Any(a => a == null || a.ItemId == null) || input.Answers.Select(a => a.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Answers.Count)
			{ result.Errors.Add("Answers must have distinct item IDs and contain at most 250 items."); return result; }
			var answers = input.Answers.ToDictionary(a => a.ItemId, StringComparer.OrdinalIgnoreCase);
			var items = form.Sections.SelectMany(s => s.Items).ToList();
			if (answers.Keys.Any(id => !items.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)))) result.Errors.Add("An answer does not belong to this published version.");
			if (input.Note?.Length > 10000 || input.LocationDescription?.Length > 500 || input.Latitude < -90 || input.Latitude > 90 || input.Longitude < -180 || input.Longitude > 180 || input.Latitude.HasValue != input.Longitude.HasValue) result.Errors.Add("Notes or coordinates are invalid.");
			if (final && form.RequireLocation && !input.Latitude.HasValue) result.Errors.Add("Location is required; it is recorded as user-supplied evidence.");
			decimal denominator = 0, numerator = 0, answeredWeight = 0; bool criticalFailure = false;
			foreach (var item in items)
			{
				answers.TryGetValue(item.Id, out var answer);
				if (!Matches(item.VisibleWhen, answers))
				{
					if (answer != null && (answer.Status != ChecklistAnswerStatus.Unanswered || !string.IsNullOrEmpty(answer.Value))) result.Errors.Add(item.Name + ": hidden items cannot be answered.");
					continue;
				}
				var required = item.Required || item.RequiredWhen != null && Matches(item.RequiredWhen, answers);
				if (answer != null && (!Enum.IsDefined(typeof(ChecklistAnswerStatus), answer.Status) || answer.Value?.Length > 10000 || answer.Note?.Length > 5000 || answer.NotApplicableReason?.Length > 2000)) { result.Errors.Add(item.Name + ": answer exceeds its limits."); continue; }
				if (answer?.Status == ChecklistAnswerStatus.NotApplicable)
				{
					if (!item.AllowNotApplicable || string.IsNullOrWhiteSpace(answer.NotApplicableReason) || !string.IsNullOrEmpty(answer.Value)) result.Errors.Add(item.Name + ": N/A needs permission, a reason and no value.");
					continue;
				}
				denominator += item.Weight;
				if (answer == null || answer.Status == ChecklistAnswerStatus.Unanswered)
				{
					if (final && (required || item.Critical)) result.Errors.Add(item.Name + ": an answer is required.");
					if (answer != null && !string.IsNullOrEmpty(answer.Value)) result.Errors.Add(item.Name + ": select Answered before entering a value.");
					continue;
				}
				bool passed = false, valid = true;
				switch (item.Type)
				{
					case ChecklistItemType.PassFail: valid = answer.Value == "pass" || answer.Value == "fail"; passed = answer.Value == "pass"; break;
					case ChecklistItemType.YesNo: case ChecklistItemType.Checkbox: valid = answer.Value == "true" || answer.Value == "false"; passed = answer.Value == item.PassingValue; break;
					case ChecklistItemType.NumericReading: case ChecklistItemType.Quantity:
						valid = decimal.TryParse(answer.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value);
						if (item.Type == ChecklistItemType.Quantity) valid &= value >= 0;
						passed = valid && (!item.Minimum.HasValue || value >= item.Minimum) && (!item.Maximum.HasValue || value <= item.Maximum); break;
					case ChecklistItemType.SelectList: valid = item.Options.Contains(answer.Value); passed = answer.Value == item.PassingValue; break;
					case ChecklistItemType.DateValue: valid = DateTime.TryParseExact(answer.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _); passed = valid; break;
					case ChecklistItemType.Photo: case ChecklistItemType.Signature: valid = evidenceItems.Contains(item.Id); passed = valid; break;
					case ChecklistItemType.FreeText: valid = !string.IsNullOrWhiteSpace(answer.Value); passed = valid; break;
					default: valid = false; break;
				}
				if (!valid) result.Errors.Add(item.Name + ": provide a valid value or accepted evidence.");
				if (valid) answeredWeight += item.Weight;
				if (valid && passed) numerator += item.Weight;
				else if (valid)
				{
					result.FailedItemIds.Add(item.Id); criticalFailure |= item.Critical;
					if (final && item.RequireNoteOnFail && string.IsNullOrWhiteSpace(answer.Note)) result.Errors.Add(item.Name + ": explain the failure.");
					if (final && item.RequirePhotoOnFail && !evidenceItems.Contains(item.Id)) result.Errors.Add(item.Name + ": failure photo is required.");
				}
			}
			result.Score = denominator > 0 ? Math.Round(100 * numerator / denominator, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
			result.Passed = result.Errors.Count == 0 && !criticalFailure && answeredWeight > 0 && result.Score.HasValue && result.Score >= form.PassThreshold;
			return result;
		}
	}
}
