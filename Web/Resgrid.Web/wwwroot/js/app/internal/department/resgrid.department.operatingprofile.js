var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var operatingprofile;
        (function (operatingprofile) {
            // February allows the 29th, matching the profile's server-side validation.
            function daysIn(month) {
                return month === 2 ? 29 : (month === 4 || month === 6 || month === 9 || month === 11) ? 30 : 31;
            }

            // The day list follows the chosen month, and a season is all four picks or none: once any is chosen the browser
            // requires the rest, so a half-entered season cannot be submitted.
            function syncSeason() {
                var $pickers = $('.op-monthday');
                $pickers.each(function () {
                    var $month = $(this).find('select[data-part=month]');
                    var $day = $(this).find('select[data-part=day]');
                    var max = daysIn(parseInt($month.val(), 10) || 0);
                    // The native value, not jQuery's: .val() reports null once the chosen option is disabled below.
                    var chosen = parseInt($day[0].value, 10) || 0;
                    $day.find('option').each(function () {
                        var day = parseInt(this.value, 10);
                        if (day) {
                            this.disabled = day > max;
                            this.hidden = day > max;
                        }
                    });
                    if (chosen > max) {
                        $day[0].value = ('0' + max).slice(-2);
                    }
                });
                var $selects = $pickers.find('select');
                var any = $selects.filter(function () { return this.value !== ''; }).length > 0;
                $selects.prop('required', any);
                $('.op-clear-season').prop('disabled', !any);
            }

            $(document).ready(function () {
                $('.op-monthday select').on('change', syncSeason);
                $('.op-clear-season').on('click', function () {
                    $('.op-monthday select').val('');
                    syncSeason();
                });
                syncSeason();

                $('select.op-picker').each(function () {
                    var $select = $(this);
                    $select.select2({
                        width: '100%',
                        placeholder: $select.data('placeholder'),
                        maximumSelectionLength: 25,
                        closeOnSelect: false
                    });
                });

                $('select.op-tags').each(function () {
                    var $select = $(this);
                    $select.select2({
                        width: '100%',
                        tags: true,
                        tokenSeparators: [',', ' '],
                        placeholder: $select.data('placeholder'),
                        maximumSelectionLength: 25,
                        // Labels are short identifiers only; the save enforces the same rule.
                        createTag: function (params) {
                            var term = $.trim(params.term);
                            return /^[A-Za-z0-9._-]{1,128}$/.test(term) ? { id: term, text: term, newTag: true } : null;
                        }
                    });
                });
            });
        })(operatingprofile = department.operatingprofile || (department.operatingprofile = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
