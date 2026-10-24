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

    /**
     * DataTables render for a call number column (give the column type 'string'). Shows the number, escaped, and sorts on
     * the row's server-built NumberSort key, so numbers order as issued ("26-5" before "26-10") under any numbering pattern.
     */
    function callNumberCell(data, type, row) {
        if (type === 'sort' || type === 'type') { return (row && row.NumberSort) || ''; }
        return $('<div/>').text(data == null ? '' : data).html();
    }
    resgrid.callNumberCell = callNumberCell;

    /**
     * A value from the server (a name, subject, note, label...) made safe to splice into markup built as a string, as
     * element text or inside a quoted attribute. Every user-entered value that goes into .html(), .append('<...>'),
     * innerHTML, a template literal or a Leaflet tooltip/popup string goes through this first.
     */
    function escapeHtml(value) {
        return value == null ? '' : String(value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    resgrid.escapeHtml = escapeHtml;

    /**
     * DataTables render for a plain-text column ({ data: 'Name', render: resgrid.textCell }). DataTables writes cells with
     * innerHTML, hidden columns included, so a column without a render inserts the value as markup.
     */
    function textCell(data, type) {
        if (type === 'display') { return escapeHtml(data); }
        return data == null ? '' : data;
    }
    resgrid.textCell = textCell;
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
        var keptValues = {};

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
            keptValues = {};
            if (!data || !data.rules || !data.rules.length) {
                return;
            }

            // Whole strings that keep their wording ("On Call" the staffing level), compared as the server does.
            (data.values || []).forEach(function (value) {
                keptValues[value.trim().toLowerCase()] = true;
            });

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
            if (keptValues[text.trim().toLowerCase()]) {
                return text;
            }

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
