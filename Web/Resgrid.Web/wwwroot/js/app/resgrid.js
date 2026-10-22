var resgrid;
(function (resgrid) {
    resgrid.absoluteBaseUrl = "";
    resgrid.absoluteApiBaseUrl = "";
    resgrid.absoluteEventingBaseUrl = "";

    function init() {
        $.validator.setDefaults({
            //errorElement: "span",
            errorClass: "has-error",
            highlight: function (element) {
                $(element).closest('.form-group').removeClass('has-success').addClass('has-error');
            },
            unhighlight: function (element) {
                $(element).closest('.form-group').removeClass('has-error').addClass('has-success');
            } //,
        });
    }
    resgrid.init = init;

    /**
     * Show or hide a loading overlay on the given jQuery selector element.
     * Replaces kendo.ui.progress($(sel), show).
     */
    function showProgress(selector, show) {
        var el = typeof selector === 'string' ? $(selector) : selector;
        if (show) {
            if (el.find('.rg-loading-overlay').length === 0) {
                el.css('position', 'relative').append(
                    '<div class="rg-loading-overlay" style="position:absolute;top:0;left:0;width:100%;height:100%;background:rgba(255,255,255,0.7);z-index:9999;display:flex;align-items:center;justify-content:center;">' +
                    '<div class="sk-spinner sk-spinner-wave"><div class="sk-rect1"></div><div class="sk-rect2"></div><div class="sk-rect3"></div><div class="sk-rect4"></div><div class="sk-rect5"></div></div>' +
                    '</div>'
                );
            }
        } else {
            el.find('.rg-loading-overlay').remove();
        }
    }
    resgrid.showProgress = showProgress;
})(resgrid || (resgrid = {}));
(function (resgrid) {
    var helpers;
    (function (helpers) {
        function GetURLParameter(sParam) {
            if (sParam = (new RegExp('[?&]' + encodeURIComponent(sParam) + '=([^&]*)')).exec(location.search))
                return decodeURIComponent(sParam[1]);
            return "";
        }
        helpers.GetURLParameter = GetURLParameter;
    })(helpers = resgrid.helpers || (resgrid.helpers = {}));
})(resgrid || (resgrid = {}));
(function (resgrid) {
    var terms;
    (function (terms) {
        // The signed-in department's terminology for text built in script (Department -> Terminology). The layout calls
        // setup() with rules the server derived from the department's names; without them apply() returns the text as is.
        // Mirrors DepartmentTermSubstitution for the English text in our scripts: whole words, the case of the word replaced,
        // protected phrases left alone.
        var matcher = null;
        var shield = null;
        var byForm = {};

        function escapeRegex(text) {
            return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        }

        function formKey(text) {
            return text.trim().replace(/\s+/g, ' ').toLowerCase();
        }

        function setup(data) {
            matcher = null;
            shield = null;
            byForm = {};
            if (!data || !data.rules || !data.rules.length) {
                return;
            }

            var forms = [];
            data.rules.forEach(function (rule) {
                (rule.forms || []).forEach(function (form) {
                    var key = formKey(form);
                    if (!byForm[key] && key !== formKey(rule.text)) {
                        byForm[key] = rule;
                        forms.push(form);
                    }
                });
            });
            if (!forms.length) {
                return;
            }

            forms.sort(function (a, b) { return b.length - a.length; });
            var edgeBefore = '(?<![\\p{L}\\p{M}\\p{N}_])';
            var edgeAfter = '(?![\\p{L}\\p{M}\\p{N}_])';
            matcher = new RegExp(edgeBefore + '(' + forms.map(function (f) {
                return f.trim().split(/\s+/).map(escapeRegex).join('\\s+');
            }).join('|') + ')' + edgeAfter, 'giu');

            var shielded = (data.phrases || []).map(function (p) {
                return p.trim().split(/\s+/).map(escapeRegex).join('\\s+');
            }).concat((data.patterns || []).map(function (p) { return '(?:' + p + ')'; }));
            if (shielded.length) {
                shield = new RegExp(edgeBefore + '(?:' + shielded.join('|') + ')' + edgeAfter, 'giu');
            }
        }
        terms.setup = setup;

        function isCapitalized(word) {
            return word.length > 0 && word.charAt(0) !== word.charAt(0).toLowerCase() && word.substring(1) === word.substring(1).toLowerCase();
        }

        function matchCase(source, target) {
            var letters = source.replace(/[^\p{L}]/gu, '');
            if (!letters.length) {
                return target;
            }
            if (letters.length > 1 && letters === letters.toUpperCase() && letters !== letters.toLowerCase()) {
                return target.toUpperCase();
            }
            var first = letters.charAt(0);
            if (first === first.toLowerCase() && first !== first.toUpperCase()) {
                return target.split(' ').map(function (w) {
                    return isCapitalized(w) ? w.charAt(0).toLowerCase() + w.substring(1) : w;
                }).join(' ');
            }
            var firstWord = target.split(' ')[0];
            var t0 = target.charAt(0);
            if (t0 === t0.toLowerCase() && t0 !== t0.toUpperCase() && firstWord.substring(1) === firstWord.substring(1).toLowerCase()) {
                return t0.toUpperCase() + target.substring(1);
            }
            return target;
        }

        /**
         * Text written in script with the department's words in place of renamed terms. Pass the term key of the page's
         * module (e.g. 'module.logs') so that module's own words are replaced too; without it only the terms renamed
         * throughout Resgrid are.
         */
        function apply(text, moduleKey) {
            if (!matcher || text === null || text === undefined) {
                return text;
            }
            text = String(text);

            var spans = [];
            if (shield) {
                shield.lastIndex = 0;
                var p;
                while ((p = shield.exec(text)) !== null) {
                    spans.push([p.index, p.index + p[0].length]);
                    if (p[0].length === 0) {
                        shield.lastIndex++;
                    }
                }
            }

            matcher.lastIndex = 0;
            return text.replace(matcher, function (match, word, offset) {
                var rule = byForm[formKey(word)];
                if (!rule || !(rule.everywhere || rule.key === moduleKey)) {
                    return match;
                }
                for (var i = 0; i < spans.length; i++) {
                    if (offset < spans[i][1] && spans[i][0] < offset + match.length) {
                        return match;
                    }
                }
                return matchCase(word, rule.text);
            });
        }
        terms.apply = apply;
    })(terms = resgrid.terms || (resgrid.terms = {}));
})(resgrid || (resgrid = {}));
