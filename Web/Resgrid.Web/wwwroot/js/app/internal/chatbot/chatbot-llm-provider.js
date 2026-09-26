(function () {
    'use strict';
    // Bring-your-own-key provider presets (LlmProviderCatalog): choosing a provider fills in its endpoint and shows an
    // example model. For a template endpoint (Azure OpenAI) the first placeholder is selected so it can be typed over.
    var select = document.getElementById('llm-provider');
    var endpoint = document.getElementById('LlmApiEndpoint');
    var model = document.getElementById('LlmModelName');
    if (!select || !endpoint || !model) return;
    select.addEventListener('change', function () {
        var option = select.options[select.selectedIndex];
        var presetEndpoint = option ? option.getAttribute('data-endpoint') || '' : '';
        model.placeholder = option ? option.getAttribute('data-model') || '' : '';
        if (!presetEndpoint) return;
        endpoint.value = presetEndpoint;
        // The previous provider's model would not exist at the new endpoint; the placeholder shows an example to type.
        model.value = '';
        var start = presetEndpoint.indexOf('{');
        if (start >= 0) {
            endpoint.focus();
            endpoint.setSelectionRange(start, presetEndpoint.indexOf('}', start) + 1);
        }
    });
})();
