var resgrid;
(function (resgrid) {
    var user;
    (function (user) {

    })(user = resgrid.user || (resgrid.user = {}));
})(resgrid || (resgrid = {}));
(function (resgrid) {
    var main;
    (function (main) {
        // Expired auth cookies leave background polls (DataTables ajax, etc.) failing with 401s;
        // send the user back to the login page instead of surfacing broken-table errors.
        if ($.fn.dataTable) {
            $.fn.dataTable.ext.errMode = 'none';
            $(document).on('error.dt', function (e, settings, techNote, message) {
                if (window.console && console.warn) { console.warn(message); }
            });
        }

        $(document).ajaxError(function (event, jqxhr) {
            if (jqxhr && jqxhr.status === 401) {
                window.location.assign(resgrid.absoluteBaseUrl + '/Account/LogOn?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search));
            }
        });

        $(document).ready(function () {
            $('#top-icons-area').load(resgrid.absoluteBaseUrl + '/User/Department/TopIconsArea');

            const { autocomplete } = window['@algolia/autocomplete-js'];
            if (autocomplete) {
                autocomplete({
                    container: '#autocomplete',
                    placeholder: 'Search Resgrid',
                    getSources() {
                        return [{
                            getItems({ query }) {
                                const queryClean = query.replace(/ |-/g, '')
                                if (!!queryClean) {
                                    return fetch(resgrid.absoluteBaseUrl + `/User/Search/GetSearchResults?query=${queryClean}`)
                                        .then(res => res.json())
                                        .catch(console.log())
                                }
                            },
                            onSelect: function (event) {
                                window.location.assign(event.item.url);
                            },
                            templates: {
                                item({ item, html }) {
                                    return html`
                                    <div><a href="${item.url}" style="text-decoration: none; color: inherit;">
                                        <h4>${item.label}</h4>
                                        <div>${item.summary}</div></a>
                                    </div>`
                                },
                            },
                        }];
                    },
                });
            }

            let lanCookieString = RegExp(".AspNetCore.Culture" + "=[^;]+").exec(document.cookie);
            let langCookieValue = decodeURIComponent(!!lanCookieString ? lanCookieString.toString().replace(/^[^=]+./, "") : "");

            const flagMap = {
                'en': '/images/flags/32/United-States.png',
                'es': '/images/flags/32/Mexico.png',
                'sv': '/images/flags/32/Sweden.png',
                'de': '/images/flags/32/Germany.png',
                'fr': '/images/flags/32/France.png',
                'it': '/images/flags/32/Italy.png',
                'pl': '/images/flags/32/Poland.png',
                'uk': '/images/flags/32/Ukraine.png',
                'ar': '/images/flags/32/Saudi-Arabia.png'
            };

            if (langCookieValue) {
                for (const [locale, flag] of Object.entries(flagMap)) {
                    if (langCookieValue.indexOf('c=' + locale) > -1) {
                        $('#languageDropdownCurrentLang').attr('src', flag);
                        break;
                    }
                }
            }

            $(".langDropdownSelection").click(function (e) {
                if (e) {
                    e.preventDefault();

                    let target = $(e.currentTarget);
                    let locale = target.data("locale");

                    if (locale) {
                        $('#culture').val(locale);
                        $('#returnUrl').val(window.location.pathname);

                        $('#setLanguageForm').submit();
                    }
                }
            });
        });
    })(main = resgrid.main || (resgrid.main = {}));
})(resgrid || (resgrid = {}));
