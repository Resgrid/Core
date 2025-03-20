export function mergeClassNames() {
  for (var _len = arguments.length, values = new Array(_len), _key = 0; _key < _len; _key++) {
    values[_key] = arguments[_key];
  }

  return values.reduce(function (acc, current) {
    Object.keys(current).forEach(function (key) {
      var accValue = acc[key];
      var currentValue = current[key];

      if (accValue !== currentValue) {
        acc[key] = [accValue, currentValue].filter(Boolean).join(' ');
      }
    });
    return acc;
  }, {});
}