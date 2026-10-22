var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var terminology;
        (function (terminology) {
            // Same clean-up as DepartmentTerms.Normalize: control/format characters out, white space collapsed. Built from a
            // string so the minifier cannot turn the escapes into raw line breaks inside a regex literal.
            var invisible = new RegExp('[\\u0000-\\u0008\\u000E-\\u001F\\u007F-\\u0084\\u0086-\\u009F\\u00AD\\u200B-\\u200F\\u202A-\\u202E\\u2060-\\u206F\\uFEFF]', 'g');

            function normalize(text) {
                if (!text) {
                    return null;
                }
                var cleaned = text.replace(invisible, '')
                    .replace(/\s+/g, ' ')
                    .trim();
                return cleaned.length ? cleaned : null;
            }

            // Mirrors DepartmentTerms.DeriveSingular: the item word Resgrid uses when the box is left empty.
            function deriveSingular(pluralName) {
                var name = normalize(pluralName);
                if (!name) {
                    return null;
                }

                var lastSpace = name.lastIndexOf(' ');
                var head = lastSpace < 0 ? '' : name.substring(0, lastSpace + 1);
                var word = lastSpace < 0 ? name : name.substring(lastSpace + 1);

                // Acronyms and short words ("EMS", "Ops", "Bus") are left alone.
                if (word.length < 4 || word === word.toUpperCase()) {
                    return name;
                }

                var lower = word.toLowerCase();
                var singular;
                if (/ies$/.test(lower) && word.length > 4) {
                    singular = word.substring(0, word.length - 3) + (word.charAt(word.length - 3) === 'I' ? 'Y' : 'y');
                } else if (/(sses|xes|ches|shes|zzes)$/.test(lower)) {
                    singular = word.substring(0, word.length - 2);
                } else if (/s$/.test(lower) && !/(ss|us|is)$/.test(lower)) {
                    singular = word.substring(0, word.length - 1);
                } else {
                    singular = word;
                }

                return head + singular;
            }
            terminology.deriveSingular = deriveSingular;

            // An empty item box shows what will be used: the guess from the department's name, else Resgrid's word.
            function showSingularGuess(nameInput) {
                var singularInput = document.getElementById(nameInput.getAttribute('data-singular-input'));
                if (!singularInput) {
                    return;
                }
                var guess = deriveSingular(nameInput.value);
                singularInput.setAttribute('placeholder', guess || singularInput.getAttribute('data-default-singular') || '');
            }

            $(document).ready(function () {
                $('input[data-singular-input]').each(function () {
                    showSingularGuess(this);
                }).on('input', function () {
                    showSingularGuess(this);
                });
            });
        })(terminology = department.terminology || (department.terminology = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
