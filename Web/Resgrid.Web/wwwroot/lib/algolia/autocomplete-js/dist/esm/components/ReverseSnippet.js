import { parseAlgoliaHitReverseSnippet } from '@algolia/autocomplete-preset-algolia';
export function createReverseSnippetComponent(_ref) {
  var createElement = _ref.createElement,
      Fragment = _ref.Fragment;

  function ReverseSnippet(_ref2) {
    var hit = _ref2.hit,
        attribute = _ref2.attribute,
        _ref2$tagName = _ref2.tagName,
        tagName = _ref2$tagName === void 0 ? 'mark' : _ref2$tagName;
    return createElement(Fragment, {}, parseAlgoliaHitReverseSnippet({
      hit: hit,
      attribute: attribute
    }).map(function (x, index) {
      return x.isHighlighted ? createElement(tagName, {
        key: index
      }, x.value) : x.value;
    }));
  }

  ReverseSnippet.__autocomplete_componentName = 'ReverseSnippet';
  return ReverseSnippet;
}