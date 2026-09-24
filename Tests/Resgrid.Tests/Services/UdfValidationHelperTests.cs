using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Services
{
	namespace UdfValidationHelperTests
	{
		// ── Shared helpers ────────────────────────────────────────────────────────
		internal static class UdfTestHelpers
		{
			internal static UdfField MakeField(UdfFieldDataType type, bool required = false) =>
				new UdfField
				{
					UdfFieldId = "test-field",
					Name = "testField",
					Label = "Test Field",
					FieldDataType = (int)type,
					IsRequired = required,
					IsEnabled = true
				};

			internal static UdfField MakeFieldWithRules(UdfFieldDataType type, UdfValidationRules rules) =>
				new UdfField
				{
					UdfFieldId = "test-field",
					Name = "testField",
					Label = "Test Field",
					FieldDataType = (int)type,
					IsRequired = false,
					IsEnabled = true,
					ValidationRules = JsonConvert.SerializeObject(rules)
				};
		}

		// ── Required ─────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_required_fields
		{
			[Test]
			public void should_fail_when_required_text_is_empty()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: true);
				UdfValidationHelper.ValidateFieldValue(field, "").Should().NotBeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, null).Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_when_required_text_has_value()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: true);
				UdfValidationHelper.ValidateFieldValue(field, "hello").Should().BeEmpty();
			}

			[Test]
			public void should_pass_when_not_required_and_empty()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: false);
				UdfValidationHelper.ValidateFieldValue(field, "").Should().BeEmpty();
			}
		}

		// ── Text length ──────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_text_length_rules
		{
			[Test]
			public void should_fail_below_min_length()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { MinLength = 5 });
				UdfValidationHelper.ValidateFieldValue(field, "ab").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_at_min_length()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { MinLength = 3 });
				UdfValidationHelper.ValidateFieldValue(field, "abc").Should().BeEmpty();
			}

			[Test]
			public void should_fail_above_max_length()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { MaxLength = 5 });
				UdfValidationHelper.ValidateFieldValue(field, "toolongvalue").Should().NotBeEmpty();
			}
		}

		// ── Numeric range ────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_numeric_range_rules
		{
			[Test]
			public void should_fail_number_below_min()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Number, new UdfValidationRules { MinValue = 10 });
				UdfValidationHelper.ValidateFieldValue(field, "5").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_number_within_range()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Number, new UdfValidationRules { MinValue = 1, MaxValue = 100 });
				UdfValidationHelper.ValidateFieldValue(field, "50").Should().BeEmpty();
			}

			[Test]
			public void should_fail_decimal_above_max()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Decimal, new UdfValidationRules { MaxValue = 9.99m });
				UdfValidationHelper.ValidateFieldValue(field, "10.50").Should().NotBeEmpty();
			}

			[Test]
			public void should_fail_non_numeric_for_number_field()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Number);
				UdfValidationHelper.ValidateFieldValue(field, "abc").Should().NotBeEmpty();
			}
		}

		// ── Regex ────────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_regex_rules
		{
			[Test]
			public void should_fail_when_value_does_not_match_regex()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { Regex = "^[A-Z]{3}$" });
				UdfValidationHelper.ValidateFieldValue(field, "abc").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_when_value_matches_regex()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { Regex = "^[A-Z]{3}$" });
				UdfValidationHelper.ValidateFieldValue(field, "ABC").Should().BeEmpty();
			}
		}

		// ── Email / Phone / Url ──────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_format_fields
		{
			[Test]
			public void should_fail_invalid_email()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Email);
				UdfValidationHelper.ValidateFieldValue(field, "notanemail").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_valid_email()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Email);
				UdfValidationHelper.ValidateFieldValue(field, "test@example.com").Should().BeEmpty();
			}

			[Test]
			public void should_fail_invalid_url()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Url);
				UdfValidationHelper.ValidateFieldValue(field, "not-a-url").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_valid_https_url()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Url);
				UdfValidationHelper.ValidateFieldValue(field, "https://resgrid.com").Should().BeEmpty();
			}

			[Test]
			public void should_fail_invalid_phone()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Phone);
				UdfValidationHelper.ValidateFieldValue(field, "not-phone!!").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_valid_phone()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Phone);
				UdfValidationHelper.ValidateFieldValue(field, "+1 (555) 867-5309").Should().BeEmpty();
			}
		}

		// ── Dropdown / MultiSelect ───────────────────────────────────────────────

		[TestFixture]
		public class when_validating_dropdown_fields
		{
			[Test]
			public void should_fail_dropdown_with_invalid_key()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Dropdown, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "fire", Label = "Fire" },
						new UdfDropdownOption { Key = "ems", Label = "EMS" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "police").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_dropdown_with_valid_key()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Dropdown, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "fire", Label = "Fire" },
						new UdfDropdownOption { Key = "ems", Label = "EMS" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "fire").Should().BeEmpty();
			}

			[Test]
			public void should_fail_multiselect_with_invalid_key()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.MultiSelect, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "a", Label = "A" },
						new UdfDropdownOption { Key = "b", Label = "B" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "a,c").Should().NotBeEmpty();
			}

			[Test]
			public void should_pass_multiselect_with_all_valid_keys()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.MultiSelect, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "a", Label = "A" },
						new UdfDropdownOption { Key = "b", Label = "B" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "a,b").Should().BeEmpty();
			}

			[Test]
			public void should_pass_dropdown_key_containing_a_comma()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Dropdown, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "Transported, ALS", Label = "Transported, ALS" }
					}
				});
				UdfValidationHelper.ValidateFieldValue(field, "Transported, ALS").Should().BeEmpty();
			}
		}

		// ── ADP REDACTED sentinel ────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_the_redacted_sentinel
		{
			private static readonly UdfValidationRules Options = new UdfValidationRules
			{
				Options = new List<UdfDropdownOption>
				{
					new UdfDropdownOption { Key = "a", Label = "A" },
					new UdfDropdownOption { Key = "b", Label = "B" }
				}
			};

			[Test]
			public void should_pass_for_dropdown_and_multiselect()
			{
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Dropdown, Options),
					ProtectedDataEnvelope.RedactionValue).Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.MultiSelect, Options),
					ProtectedDataEnvelope.RedactionValue).Should().BeEmpty();
			}

			[Test]
			public void should_pass_for_typed_fields()
			{
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeField(UdfFieldDataType.Boolean), ProtectedDataEnvelope.RedactionValue).Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeField(UdfFieldDataType.Number), ProtectedDataEnvelope.RedactionValue).Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeField(UdfFieldDataType.Date), ProtectedDataEnvelope.RedactionValue).Should().BeEmpty();
			}

			[Test]
			public void should_satisfy_a_required_field()
			{
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeField(UdfFieldDataType.Dropdown, required: true),
					ProtectedDataEnvelope.RedactionValue).Should().BeEmpty();
			}

			[Test]
			public void should_still_reject_the_sentinel_mixed_into_a_multiselect()
			{
				UdfValidationHelper.ValidateFieldValue(UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.MultiSelect, Options),
					$"{ProtectedDataEnvelope.RedactionValue},a").Should().NotBeEmpty();
			}
		}

		// ── Option lists (definition time) ───────────────────────────────────────

		[TestFixture]
		public class when_validating_field_options
		{
			private static UdfField OptionField(UdfFieldDataType type, params string[] keys) =>
				UdfTestHelpers.MakeFieldWithRules(type, new UdfValidationRules
				{
					Options = keys.Select(k => new UdfDropdownOption { Key = k, Label = k }).ToList()
				});

			[Test]
			public void should_pass_for_valid_dropdown_and_multiselect()
			{
				UdfValidationHelper.ValidateFieldOptions(new[]
				{
					OptionField(UdfFieldDataType.Dropdown, "transported", "Transported, ALS"),
					OptionField(UdfFieldDataType.MultiSelect, "a", "b")
				}).Should().BeEmpty();
			}

			[Test]
			public void should_ignore_non_option_fields()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { UdfTestHelpers.MakeField(UdfFieldDataType.Text) })
					.Should().BeEmpty();
			}

			[Test]
			public void should_fail_when_an_option_field_has_no_options()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { UdfTestHelpers.MakeField(UdfFieldDataType.Dropdown) })
					.Should().ContainSingle().Which.Should().Contain("at least one option");
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.MultiSelect) })
					.Should().ContainSingle().Which.Should().Contain("at least one option");
			}

			[Test]
			public void should_fail_for_empty_or_duplicate_keys()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.Dropdown, "a", " ") })
					.Should().ContainSingle().Which.Should().Contain("needs a key");
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.Dropdown, "a", "a") })
					.Should().ContainSingle().Which.Should().Contain("more than once");
			}

			[Test]
			public void should_fail_for_a_comma_in_a_multiselect_key()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.MultiSelect, "a,b") })
					.Should().ContainSingle().Which.Should().Contain("commas");
			}

			[Test]
			public void should_fail_for_the_reserved_sentinel_key()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.Dropdown, ProtectedDataEnvelope.RedactionValue) })
					.Should().ContainSingle().Which.Should().Contain("reserved");
			}

			[Test]
			public void should_require_options_for_a_combo_box()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { UdfTestHelpers.MakeField(UdfFieldDataType.ComboBox) })
					.Should().ContainSingle().Which.Should().Contain("at least one option");
			}

			[Test]
			public void should_allow_a_comma_in_a_combo_box_option()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.ComboBox, "Transported, ALS", "Refused") })
					.Should().BeEmpty();
			}

			[Test]
			public void should_fail_for_combo_box_keys_or_labels_differing_only_by_case()
			{
				UdfValidationHelper.ValidateFieldOptions(new[] { OptionField(UdfFieldDataType.ComboBox, "tx", "TX") })
					.Should().Contain(e => e.Contains("key(s)")).And.Contain(e => e.Contains("label(s)"));
			}

			[Test]
			public void should_fail_for_a_blank_or_reserved_combo_box_label()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.ComboBox, new UdfValidationRules
				{
					Options = new List<UdfDropdownOption>
					{
						new UdfDropdownOption { Key = "a", Label = "" },
						new UdfDropdownOption { Key = "b", Label = ProtectedDataEnvelope.RedactionValue }
					}
				});

				UdfValidationHelper.ValidateFieldOptions(new[] { field })
					.Should().Contain(e => e.Contains("needs a label")).And.Contain(e => e.Contains("reserved"));
			}

			[Test]
			public void should_fail_for_a_combo_box_label_that_breaks_the_fields_own_rules()
			{
				// The browser enforces maxlength/pattern on the input, so it would block this choice.
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.ComboBox, new UdfValidationRules
				{
					MaxLength = 5,
					Options = new List<UdfDropdownOption> { new UdfDropdownOption { Key = "tx", Label = "Transported" } }
				});

				UdfValidationHelper.ValidateFieldOptions(new[] { field })
					.Should().ContainSingle().Which.Should().Contain("'Transported'");
			}
		}

		// ── Combo box ────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_combo_box_fields
		{
			private static UdfField Combo(UdfValidationRules rules = null, bool required = false)
			{
				rules ??= new UdfValidationRules();
				rules.Options = new List<UdfDropdownOption>
				{
					new UdfDropdownOption { Key = "TX-ALS", Label = "transported als" },
					new UdfDropdownOption { Key = "refused", Label = "Refused care" }
				};
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.ComboBox, rules);
				field.IsRequired = required;
				return field;
			}

			[Test]
			public void should_pass_free_text()
			{
				UdfValidationHelper.ValidateFieldValue(Combo(), "Referred to crisis line").Should().BeEmpty();
			}

			[Test]
			public void should_pass_an_option_by_key_or_label_in_any_case()
			{
				UdfValidationHelper.ValidateFieldValue(Combo(), "TX-ALS").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(Combo(), "REFUSED CARE").Should().BeEmpty();
			}

			[Test]
			public void should_hold_free_text_to_the_length_and_format_rules()
			{
				var rules = new UdfValidationRules { MaxLength = 20, Regex = "^[a-z ]+$" };
				UdfValidationHelper.ValidateFieldValue(Combo(rules), "this free text is far too long").Should().NotBeEmpty();
				UdfValidationHelper.ValidateFieldValue(Combo(rules), "Capitalised").Should().NotBeEmpty();
				UdfValidationHelper.ValidateFieldValue(Combo(rules), "left at scene").Should().BeEmpty();
			}

			[Test]
			public void should_exempt_a_listed_option_from_the_text_rules()
			{
				// "TX-ALS" is the stored key a mobile picker sends; it fails the lowercase pattern.
				var rules = new UdfValidationRules { Regex = "^[a-z ]+$" };
				UdfValidationHelper.ValidateFieldValue(Combo(rules), "TX-ALS").Should().BeEmpty();
			}

			[Test]
			public void should_fail_when_required_and_empty()
			{
				UdfValidationHelper.ValidateFieldValue(Combo(required: true), "").Should().NotBeEmpty();
			}
		}

		[TestFixture]
		public class when_normalizing_field_values
		{
			private static readonly UdfField Combo = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.ComboBox, new UdfValidationRules
			{
				Options = new List<UdfDropdownOption>
				{
					new UdfDropdownOption { Key = "tx", Label = "Transported" },
					new UdfDropdownOption { Key = "refused", Label = "Refused care" }
				}
			});

			[Test]
			public void should_store_an_option_label_as_its_key()
			{
				UdfValidationHelper.NormalizeFieldValue(Combo, "Transported").Should().Be("tx");
				UdfValidationHelper.NormalizeFieldValue(Combo, "  refused CARE ").Should().Be("refused");
			}

			[Test]
			public void should_store_an_option_key_as_itself()
			{
				UdfValidationHelper.NormalizeFieldValue(Combo, "tx").Should().Be("tx");
				UdfValidationHelper.NormalizeFieldValue(Combo, "TX").Should().Be("tx");
			}

			[Test]
			public void should_store_free_text_trimmed()
			{
				UdfValidationHelper.NormalizeFieldValue(Combo, "  Referred to crisis line ").Should().Be("Referred to crisis line");
			}

			[Test]
			public void should_leave_protected_values_alone()
			{
				UdfValidationHelper.NormalizeFieldValue(Combo, ProtectedDataEnvelope.RedactionValue).Should().Be(ProtectedDataEnvelope.RedactionValue);
				UdfValidationHelper.NormalizeFieldValue(Combo, ProtectedDataEnvelope.Prefix + "sealed").Should().Be(ProtectedDataEnvelope.Prefix + "sealed");
			}

			[Test]
			public void should_leave_other_types_alone()
			{
				UdfValidationHelper.NormalizeFieldValue(UdfTestHelpers.MakeField(UdfFieldDataType.Text), "  Transported ").Should().Be("  Transported ");
			}
		}

		// ── Boolean ──────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_validating_boolean_fields
		{
			[Test]
			public void should_pass_true_false_values()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Boolean);
				UdfValidationHelper.ValidateFieldValue(field, "true").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, "false").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, "1").Should().BeEmpty();
				UdfValidationHelper.ValidateFieldValue(field, "0").Should().BeEmpty();
			}

			[Test]
			public void should_fail_non_boolean_value()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Boolean);
				UdfValidationHelper.ValidateFieldValue(field, "maybe").Should().NotBeEmpty();
			}
		}

		// ── HTML attributes ──────────────────────────────────────────────────────

		[TestFixture]
		public class when_generating_html_attributes
		{
			[Test]
			public void should_include_required_attribute_for_required_field()
			{
				var field = UdfTestHelpers.MakeField(UdfFieldDataType.Text, required: true);
				var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
				attrs.Should().ContainKey("required");
			}

			[Test]
			public void should_include_min_max_for_number_field_with_rules()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Number, new UdfValidationRules { MinValue = 1, MaxValue = 100 });
				var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
				attrs.Should().ContainKey("min");
				attrs.Should().ContainKey("max");
				attrs["min"].Should().Be("1");
				attrs["max"].Should().Be("100");
			}

			[Test]
			public void should_include_pattern_for_regex_rule()
			{
				var field = UdfTestHelpers.MakeFieldWithRules(UdfFieldDataType.Text, new UdfValidationRules { Regex = "^[A-Z]+$" });
				var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
				attrs.Should().ContainKey("pattern");
			}
		}
	}
}

