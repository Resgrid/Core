
var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var quiz;
        (function (quiz) {
            var i18n = (typeof resgridQuizI18n !== 'undefined') ? resgridQuizI18n : {};

            // Optional one question per page: the questions are all in the form (and all post); this only pages them.
            $(document).ready(function () {
                resgrid.common.analytics.track('Training - Quiz');

                var form = $('#quizForm');
                if (form.attr('data-one-per-page') !== 'true') {
                    return;
                }

                var questions = form.find('.quiz-question');
                if (questions.length < 2) {
                    return;
                }

                var current = 0;
                var previous = form.find('.quiz-previous');
                var next = form.find('.quiz-next');
                var submit = form.find('.quiz-submit');
                var position = form.find('.quiz-position');

                function show(index) {
                    current = index;
                    questions.hide().eq(index).show();
                    previous.toggle(index > 0);
                    next.toggle(index < questions.length - 1);
                    submit.toggle(index === questions.length - 1);
                    position.text(String(i18n.position || '{0} / {1}').replace('{0}', index + 1).replace('{1}', questions.length));
                    questions.eq(index).find('input').first().focus();
                }

                previous.on('click', function () { if (current > 0) show(current - 1); });
                next.on('click', function () { if (current < questions.length - 1) show(current + 1); });
                show(0);
            });
        })(quiz = training.quiz || (training.quiz = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));
