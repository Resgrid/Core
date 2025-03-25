import { parseAlgoliaHitReverseHighlight } from '@algolia/autocomplete-preset-algolia';
export function createReverseHighlightComponent(_ref) {
  var createElement = _ref.createElement,
      Fragment = _ref.Fragment;

  function ReverseHighlight(_ref2) {
    var hit = _ref2.hit,
        attribute = _ref2.attribute,
        _ref2$tagName = _ref2.tagName,
        tagName = _ref2$tagName === void 0 ? 'mark' : _ref2$tagName;
    return createElement(Fragment, {}, parseAlgoliaHitReverseHighlight({
      hit: hit,
      attribute: attribute
    }).map(function (x, index) {
      return x.isHighlighted ? createElement(tagName, {
        key: index
      }, x.value) : x.value;
    }));
  }

  ReverseHighlight.__autocomplete_componentName = 'ReverseHighlight';
  return ReverseHighlight;
}