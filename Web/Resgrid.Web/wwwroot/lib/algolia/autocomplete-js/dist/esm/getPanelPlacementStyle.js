export function getPanelPlacementStyle(_ref) {
  var panelPlacement = _ref.panelPlacement,
      container = _ref.container,
      form = _ref.form,
      environment = _ref.environment;
  var containerRect = container.getBoundingClientRect(); // Some browsers have specificities to retrieve the document scroll position.
  // See https://stackoverflow.com/a/28633515/9940315

  var scrollTop = environment.pageYOffset || environment.document.documentElement.scrollTop || environment.document.body.scrollTop || 0;
  var top = scrollTop + containerRect.top + containerRect.height;

  switch (panelPlacement) {
    case 'start':
      {
        return {
          top: top,
          left: containerRect.left
        };
      }

    case 'end':
      {
        return {
          top: top,
          right: environment.document.documentElement.clientWidth - (containerRect.left + containerRect.width)
        };
      }

    case 'full-width':
      {
        return {
          top: top,
          left: 0,
          right: 0,
          width: 'unset',
          maxWidth: 'unset'
        };
      }

    case 'input-wrapper-width':
      {
        var formRect = form.getBoundingClientRect();
        return {
          top: top,
          left: formRect.left,
          right: environment.document.documentElement.clientWidth - (formRect.left + formRect.width),
          width: 'unset',
          maxWidth: 'unset'
        };
      }

    default:
      {
        throw new Error("[Autocomplete] The `panelPlacement` value ".concat(JSON.stringify(panelPlacement), " is not valid."));
      }
  }
}