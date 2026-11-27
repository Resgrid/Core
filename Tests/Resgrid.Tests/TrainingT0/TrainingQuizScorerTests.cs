using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// Server-side quiz scoring (R-T11): posted ids are validated (T-D4), each question counts once whatever the key spelling
	/// and the score is capped (T-D11), multiple choice is all-or-nothing and true/false single (R-S9), and the revision hash
	/// identifies the quiz an attempt was taken against (R-T6).
	/// </summary>
	[TestFixture]
	public class TrainingQuizScorerTests
	{
		internal static Training Quiz(double minimum = 50, params TrainingQuestion[] questions) => new Training
		{
			TrainingId = 1,
			DepartmentId = 10,
			MinimumScore = minimum,
			Questions = questions.ToList()
		};

		internal static TrainingQuestion Single(int id, string text = null, int correctOption = 1, int options = 3)
		{
			return new TrainingQuestion
			{
				TrainingQuestionId = id,
				Question = text ?? "Question " + id,
				Type = (int)TrainingQuestionTypes.SingleChoice,
				Answers = Enumerable.Range(1, options).Select(o => new TrainingQuestionAnswer
				{
					TrainingQuestionAnswerId = id * 10 + o,
					TrainingQuestionId = id,
					Answer = "Option " + o,
					Correct = o == correctOption
				}).ToList()
			};
		}

		internal static TrainingQuestion Multi(int id, params int[] correctOptions)
		{
			return new TrainingQuestion
			{
				TrainingQuestionId = id,
				Question = "Multi " + id,
				Type = (int)TrainingQuestionTypes.MultiChoice,
				Answers = Enumerable.Range(1, 4).Select(o => new TrainingQuestionAnswer
				{
					TrainingQuestionAnswerId = id * 10 + o,
					TrainingQuestionId = id,
					Answer = "Option " + o,
					Correct = correctOptions.Contains(o)
				}).ToList()
			};
		}

		internal static TrainingQuestion TrueFalse(int id, bool answerIsTrue)
		{
			return new TrainingQuestion
			{
				TrainingQuestionId = id,
				Question = "Statement " + id,
				Type = (int)TrainingQuestionTypes.TrueFalse,
				Answers = new List<TrainingQuestionAnswer>
				{
					new TrainingQuestionAnswer { TrainingQuestionAnswerId = id * 10 + 1, TrainingQuestionId = id, Answer = "True", Correct = answerIsTrue },
					new TrainingQuestionAnswer { TrainingQuestionAnswerId = id * 10 + 2, TrainingQuestionId = id, Answer = "False", Correct = !answerIsTrue }
				}
			};
		}

		private static Dictionary<int, List<int>> Answers(params (int Question, int[] Options)[] answers) =>
			answers.ToDictionary(a => a.Question, a => a.Options.ToList());

		private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> Form(params (string Key, string Value)[] fields) =>
			fields.GroupBy(f => f.Key).Select(g => new KeyValuePair<string, IEnumerable<string>>(g.Key, g.Select(f => f.Value).ToList()));

		[Test]
		public void A_submission_with_nothing_right_scores_zero_and_fails()
		{
			var result = TrainingQuizScorer.Score(Quiz(50, Single(1), Single(2)), Answers((1, new[] { 12 }), (2, new[] { 23 })));

			result.CorrectCount.Should().Be(0);
			result.Score.Should().Be(0);
			result.Passed.Should().BeFalse();
		}

		[Test]
		public void An_option_id_from_another_question_or_training_does_not_count()
		{
			// Question 1's correct option is 11; 21 is question 2's correct option and 999 belongs to no question here.
			var result = TrainingQuizScorer.Score(Quiz(0, Single(1), Single(2)), Answers((1, new[] { 21 }), (2, new[] { 999 })));

			result.CorrectCount.Should().Be(0);
			result.Snapshot.Questions.Single(q => q.QuestionId == 1).ChosenOptionIds.Should().BeEmpty();
			result.Snapshot.Questions.Single(q => q.QuestionId == 2).ChosenOptionIds.Should().BeEmpty();
		}

		[Test]
		public void Answers_for_questions_that_are_not_the_trainings_are_ignored()
		{
			var result = TrainingQuizScorer.Score(Quiz(0, Single(1)), Answers((1, new[] { 11 }), (77, new[] { 771 })));

			result.QuestionCount.Should().Be(1);
			result.CorrectCount.Should().Be(1);
			result.Score.Should().Be(100);
		}

		[Test]
		public void Respelt_and_repeated_question_keys_count_once_and_the_score_never_exceeds_100()
		{
			var parsed = TrainingQuizScorer.ParsePostedAnswers(Form(
				("question_1", "11"), ("question_01", "11"), ("question_+1", "11"), ("question_ 1", "11"), ("question_1", "11"), ("question_2", "21")));

			parsed.Keys.Should().BeEquivalentTo(new[] { 1, 2 });
			parsed[1].Should().Equal(11);

			var result = TrainingQuizScorer.Score(Quiz(0, Single(1), Single(2)), parsed);
			result.Score.Should().Be(100);
			result.CorrectCount.Should().Be(2);
		}

		[Test]
		public void Malformed_option_ids_are_dropped_instead_of_failing()
		{
			var parsed = TrainingQuizScorer.ParsePostedAnswers(Form(("question_1", "abc"), ("question_1", "011"), ("question_1", "-11"), ("question_2", "21,22"), ("other", "1"), (null, "x")));

			parsed[1].Should().BeEmpty();
			parsed[2].Should().Equal(21, 22);
			parsed.Should().NotContainKey(0);
		}

		[Test]
		public void At_most_100_questions_are_read_from_a_post()
		{
			var fields = Enumerable.Range(1, 150).Select(i => ("question_" + i, "1")).ToArray();

			TrainingQuizScorer.ParsePostedAnswers(Form(fields)).Count.Should().Be(TrainingQuizScorer.MaxQuestions);
		}

		[Test]
		public void A_multiple_choice_question_is_right_only_when_exactly_its_correct_options_are_chosen()
		{
			var quiz = Quiz(0, Multi(1, 1, 3));

			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 11, 13 }))).CorrectCount.Should().Be(1);
			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 13, 11 }))).CorrectCount.Should().Be(1);
			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 11 }))).CorrectCount.Should().Be(0, "one correct option missing");
			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 11, 13, 14 }))).CorrectCount.Should().Be(0, "a wrong option chosen too");
			TrainingQuizScorer.Score(quiz, Answers()).CorrectCount.Should().Be(0);
		}

		[Test]
		public void A_single_choice_question_with_two_options_picked_is_wrong()
		{
			TrainingQuizScorer.Score(Quiz(0, Single(1)), Answers((1, new[] { 11, 12 }))).CorrectCount.Should().Be(0);
		}

		[Test]
		public void A_true_false_question_is_scored_as_a_single_choice()
		{
			var quiz = Quiz(0, TrueFalse(1, answerIsTrue: false));

			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 12 }))).CorrectCount.Should().Be(1);
			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 11 }))).CorrectCount.Should().Be(0);
		}

		[Test]
		public void The_pass_mark_decides_passed_and_the_score_is_rounded_to_two_decimals()
		{
			var quiz = Quiz(66.67, Single(1), Single(2), Single(3));

			var two = TrainingQuizScorer.Score(quiz, Answers((1, new[] { 11 }), (2, new[] { 21 }), (3, new[] { 32 })));
			two.Score.Should().Be(66.67);
			two.Passed.Should().BeTrue();

			quiz.MinimumScore = 70;
			TrainingQuizScorer.Score(quiz, Answers((1, new[] { 11 }), (2, new[] { 21 }), (3, new[] { 32 }))).Passed.Should().BeFalse();
		}

		[Test]
		public void The_snapshot_keeps_the_questions_options_answer_key_and_choices()
		{
			var result = TrainingQuizScorer.Score(Quiz(50, Single(1, "Which hose?"), Multi(2, 2, 4)), Answers((1, new[] { 12 }), (2, new[] { 22, 24 })));

			result.Snapshot.MinimumScore.Should().Be(50);
			result.Snapshot.Questions.Should().HaveCount(2);
			var first = result.Snapshot.Questions[0];
			first.Text.Should().Be("Which hose?");
			first.Options.Select(o => o.Text).Should().Equal("Option 1", "Option 2", "Option 3");
			first.Options.Single(o => o.Correct).OptionId.Should().Be(11);
			first.ChosenOptionIds.Should().Equal(12);
			first.AnsweredCorrectly.Should().BeFalse();
			result.Snapshot.Questions[1].Type.Should().Be((int)TrainingQuestionTypes.MultiChoice);
			result.Snapshot.Questions[1].AnsweredCorrectly.Should().BeTrue();
			result.QuizRevisionHash.Should().MatchRegex("^[0-9a-f]{64}$");
		}

		[Test]
		public void The_revision_hash_changes_with_question_text_answer_key_type_and_pass_mark_only()
		{
			var hash = TrainingQuizScorer.RevisionHash(Quiz(50, Single(1), Single(2)));

			TrainingQuizScorer.RevisionHash(Quiz(50, Single(1), Single(2))).Should().Be(hash, "an unchanged quiz keeps its hash");
			TrainingQuizScorer.RevisionHash(Quiz(60, Single(1), Single(2))).Should().NotBe(hash, "pass mark");
			TrainingQuizScorer.RevisionHash(Quiz(50, Single(1, "Reworded"), Single(2))).Should().NotBe(hash, "question text");
			TrainingQuizScorer.RevisionHash(Quiz(50, Single(1, correctOption: 2), Single(2))).Should().NotBe(hash, "answer key");
			TrainingQuizScorer.RevisionHash(Quiz(50, Single(1), Single(2, options: 4))).Should().NotBe(hash, "an option added");
		}

		[Test]
		public void A_resave_with_new_ids_but_the_same_content_is_not_a_quiz_change()
		{
			var stored = Quiz(50, Single(1), Single(2));
			var edited = Quiz(50, Single(1), Single(2));
			// The second question posted as new (no ids yet), same text and options in the same order.
			edited.Questions.Last().TrainingQuestionId = 0;
			foreach (var answer in edited.Questions.Last().Answers)
				answer.TrainingQuestionAnswerId = 0;

			TrainingQuizScorer.ChangesQuiz(stored, edited).Should().BeFalse();

			edited.MinimumScore = 80;
			TrainingQuizScorer.ChangesQuiz(stored, edited).Should().BeTrue();
		}

		[Test]
		public void Validate_checks_the_answer_key_per_type_and_the_pass_mark_range()
		{
			TrainingQuizScorer.Validate(Quiz(50, Single(1), Multi(2, 1, 2), TrueFalse(3, true))).Should().BeEmpty();

			var noKey = Single(1, correctOption: 0);
			TrainingQuizScorer.Validate(Quiz(50, noKey)).Should().Contain("QuestionAnswerKey");

			var twoKeys = Single(1);
			twoKeys.Answers.Last().Correct = true;
			TrainingQuizScorer.Validate(Quiz(50, twoKeys)).Should().Contain("QuestionAnswerKey");

			TrainingQuizScorer.Validate(Quiz(50, Multi(1))).Should().Contain("QuestionAnswerKey", "a multiple choice question needs at least one correct option");

			var oneOption = Single(1, options: 1);
			TrainingQuizScorer.Validate(Quiz(50, oneOption)).Should().Contain("QuestionOptions");

			var tf = TrueFalse(1, true);
			tf.Answers.Add(new TrainingQuestionAnswer { Answer = "Maybe" });
			TrainingQuizScorer.Validate(Quiz(50, tf)).Should().Contain("TrueFalseOptions");

			var blank = Single(1, text: " ");
			TrainingQuizScorer.Validate(Quiz(50, blank)).Should().Contain("QuestionText");

			var badType = Single(1);
			badType.Type = 42;
			TrainingQuizScorer.Validate(Quiz(50, badType)).Should().Contain("QuestionType");

			TrainingQuizScorer.Validate(Quiz(101, Single(1))).Should().Contain("MinimumScoreRange");
			TrainingQuizScorer.Validate(Quiz(-1, Single(1))).Should().Contain("MinimumScoreRange");
			TrainingQuizScorer.Validate(Quiz(double.NaN, Single(1))).Should().Contain("MinimumScoreRange");
			TrainingQuizScorer.Validate(Quiz(50, Enumerable.Range(1, 101).Select(i => Single(i)).ToArray())).Should().Contain("TooManyQuestions");
		}
	}
}
