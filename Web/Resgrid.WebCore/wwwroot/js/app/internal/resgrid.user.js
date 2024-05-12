var resgrid;
(function (resgrid) {
    var user;
    (function (user) {

    })(user = resgrid.user || (resgrid.user = {}));
})(resgrid || (resgrid = {}));
(function (resgrid) {
    var main;
    (function (main) {
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

            if (langCookieValue) {
                if (langCookieValue.indexOf("c=en") > -1) {
                    $('#languageDropdownCurrentLang').attr('src', "/images/flags/32/United-States.png");
                } else if (langCookieValue.indexOf("c=es") > -1) {
                    $('#languageDropdownCurrentLang').attr('src', "/images/flags/32/Mexico.png");
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
