function _typeof(obj) { "@babel/helpers - typeof"; return _typeof = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) { return typeof obj; } : function (obj) { return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj; }, _typeof(obj); }
var _excluded = ["classNames", "container", "getEnvironmentProps", "getFormProps", "getInputProps", "getItemProps", "getLabelProps", "getListProps", "getPanelProps", "getRootProps", "panelContainer", "panelPlacement", "render", "renderNoResults", "renderer", "detachedMediaQuery", "components", "translations"];
function ownKeys(object, enumerableOnly) { var keys = Object.keys(object); if (Object.getOwnPropertySymbols) { var symbols = Object.getOwnPropertySymbols(object); enumerableOnly && (symbols = symbols.filter(function (sym) { return Object.getOwnPropertyDescriptor(object, sym).enumerable; })), keys.push.apply(keys, symbols); } return keys; }
function _objectSpread(target) { for (var i = 1; i < arguments.length; i++) { var source = null != arguments[i] ? arguments[i] : {}; i % 2 ? ownKeys(Object(source), !0).forEach(function (key) { _defineProperty(target, key, source[key]); }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys(Object(source)).forEach(function (key) { Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key)); }); } return target; }
function _defineProperty(obj, key, value) { key = _toPropertyKey(key); if (key in obj) { Object.defineProperty(obj, key, { value: value, enumerable: true, configurable: true, writable: true }); } else { obj[key] = value; } return obj; }
function _toPropertyKey(arg) { var key = _toPrimitive(arg, "string"); return _typeof(key) === "symbol" ? key : String(key); }
function _toPrimitive(input, hint) { if (_typeof(input) !== "object" || input === null) return input; var prim = input[Symbol.toPrimitive]; if (prim !== undefined) { var res = prim.call(input, hint || "default"); if (_typeof(res) !== "object") return res; throw new TypeError("@@toPrimitive must return a primitive value."); } return (hint === "string" ? String : Number)(input); }
function _objectWithoutProperties(source, excluded) { if (source == null) return {}; var target = _objectWithoutPropertiesLoose(source, excluded); var key, i; if (Object.getOwnPropertySymbols) { var sourceSymbolKeys = Object.getOwnPropertySymbols(source); for (i = 0; i < sourceSymbolKeys.length; i++) { key = sourceSymbolKeys[i]; if (excluded.indexOf(key) >= 0) continue; if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue; target[key] = source[key]; } } return target; }
function _objectWithoutPropertiesLoose(source, excluded) { if (source == null) return {}; var target = {}; var sourceKeys = Object.keys(source); var key, i; for (i = 0; i < sourceKeys.length; i++) { key = sourceKeys[i]; if (excluded.indexOf(key) >= 0) continue; target[key] = source[key]; } return target; }
import { generateAutocompleteId, invariant, warn } from '@algolia/autocomplete-shared';
import { createElement as preactCreateElement, Fragment as PreactFragment, render } from 'preact';
import { createHighlightComponent, createReverseHighlightComponent, createReverseSnippetComponent, createSnippetComponent } from './components';
import { getHTMLElement, mergeClassNames } from './utils';
var defaultClassNames = {
  clearButton: 'aa-ClearButton',
  detachedCancelButton: 'aa-DetachedCancelButton',
  detachedContainer: 'aa-DetachedContainer',
  detachedFormContainer: 'aa-DetachedFormContainer',
  detachedOverlay: 'aa-DetachedOverlay',
  detachedSearchButton: 'aa-DetachedSearchButton',
  detachedSearchButtonIcon: 'aa-DetachedSearchButtonIcon',
  detachedSearchButtonPlaceholder: 'aa-DetachedSearchButtonPlaceholder',
  detachedSearchButtonQuery: 'aa-DetachedSearchButtonQuery',
  form: 'aa-Form',
  input: 'aa-Input',
  inputWrapper: 'aa-InputWrapper',
  inputWrapperPrefix: 'aa-InputWrapperPrefix',
  inputWrapperSuffix: 'aa-InputWrapperSuffix',
  item: 'aa-Item',
  label: 'aa-Label',
  list: 'aa-List',
  loadingIndicator: 'aa-LoadingIndicator',
  panel: 'aa-Panel',
  panelLayout: 'aa-PanelLayout aa-Panel--scrollable',
  root: 'aa-Autocomplete',
  source: 'aa-Source',
  sourceFooter: 'aa-SourceFooter',
  sourceHeader: 'aa-SourceHeader',
  sourceNoResults: 'aa-SourceNoResults',
  submitButton: 'aa-SubmitButton'
};
var defaultRender = function defaultRender(_ref, root) {
  var children = _ref.children,
    render = _ref.render;
  render(children, root);
};
var defaultRenderer = {
  createElement: preactCreateElement,
  Fragment: PreactFragment,
  render: render
};
export function getDefaultOptions(options) {
  var _core$id;
  var classNames = options.classNames,
    container = options.container,
    getEnvironmentProps = options.getEnvironmentProps,
    getFormProps = options.getFormProps,
    getInputProps = options.getInputProps,
    getItemProps = options.getItemProps,
    getLabelProps = options.getLabelProps,
    getListProps = options.getListProps,
    getPanelProps = options.getPanelProps,
    getRootProps = options.getRootProps,
    panelContainer = options.panelContainer,
    panelPlacement = options.panelPlacement,
    render = options.render,
    renderNoResults = options.renderNoResults,
    renderer = options.renderer,
    detachedMediaQuery = options.detachedMediaQuery,
    components = options.components,
    translations = options.translations,
    core = _objectWithoutProperties(options, _excluded);

  /* eslint-disable no-restricted-globals */
  var environment = typeof window !== 'undefined' ? window : {};
  /* eslint-enable no-restricted-globals */
  var containerElement = getHTMLElement(environment, container);
  invariant(containerElement.tagName !== 'INPUT', 'The `container` option does not support `input` elements. You need to change the container to a `div`.');
  process.env.NODE_ENV !== 'production' ? warn(!(render && renderer && !(renderer !== null && renderer !== void 0 && renderer.render)), "You provided the `render` option but did not provide a `renderer.render`. Since v1.6.0, you can provide a `render` function directly in `renderer`." + "\nTo get rid of this warning, do any of the following depending on your use case." + "\n- If you are using the `render` option only to override Autocomplete's default `render` function, pass the `render` function into `renderer` and remove the `render` option." + '\n- If you are using the `render` option to customize the layout, pass your `render` function into `renderer` and use it from the provided parameters of the `render` option.' + '\n- If you are using the `render` option to work with React 18, pass an empty `render` function into `renderer`.' + '\nSee https://www.algolia.com/doc/ui-libraries/autocomplete/api-reference/autocomplete-js/autocomplete/#param-render') : void 0;
  process.env.NODE_ENV !== 'production' ? warn(!renderer || render || renderer.Fragment && renderer.createElement && renderer.render, "You provided an incomplete `renderer` (missing: ".concat([!(renderer !== null && renderer !== void 0 && renderer.createElement) && '`renderer.createElement`', !(renderer !== null && renderer !== void 0 && renderer.Fragment) && '`renderer.Fragment`', !(renderer !== null && renderer !== void 0 && renderer.render) && '`renderer.render`'].filter(Boolean).join(', '), "). This can cause rendering issues.") + '\nSee https://www.algolia.com/doc/ui-libraries/autocomplete/api-reference/autocomplete-js/autocomplete/#param-renderer') : void 0;
  var defaultedRenderer = _objectSpread(_objectSpread({}, defaultRenderer), renderer);
  var defaultComponents = {
    Highlight: createHighlightComponent(defaultedRenderer),
    ReverseHighlight: createReverseHighlightComponent(defaultedRenderer),
    ReverseSnippet: createReverseSnippetComponent(defaultedRenderer),
    Snippet: createSnippetComponent(defaultedRenderer)
  };
  var defaultTranslations = {
    clearButtonTitle: 'Clear',
    detachedCancelButtonText: 'Cancel',
    detachedSearchButtonTitle: 'Search',
    submitButtonTitle: 'Submit'
  };
  return {
    renderer: {
      classNames: mergeClassNames(defaultClassNames, classNames !== null && classNames !== void 0 ? classNames : {}),
      container: containerElement,
      getEnvironmentProps: getEnvironmentProps !== null && getEnvironmentProps !== void 0 ? getEnvironmentProps : function (_ref2) {
        var props = _ref2.props;
        return props;
      },
      getFormProps: getFormProps !== null && getFormProps !== void 0 ? getFormProps : function (_ref3) {
        var props = _ref3.props;
        return props;
      },
      getInputProps: getInputProps !== null && getInputProps !== void 0 ? getInputProps : function (_ref4) {
        var props = _ref4.props;
        return props;
      },
      getItemProps: getItemProps !== null && getItemProps !== void 0 ? getItemProps : function (_ref5) {
        var props = _ref5.props;
        return props;
      },
      getLabelProps: getLabelProps !== null && getLabelProps !== void 0 ? getLabelProps : function (_ref6) {
        var props = _ref6.props;
        return props;
      },
      getListProps: getListProps !== null && getListProps !== void 0 ? getListProps : function (_ref7) {
        var props = _ref7.props;
        return props;
      },
      getPanelProps: getPanelProps !== null && getPanelProps !== void 0 ? getPanelProps : function (_ref8) {
        var props = _ref8.props;
        return props;
      },
      getRootProps: getRootProps !== null && getRootProps !== void 0 ? getRootProps : function (_ref9) {
        var props = _ref9.props;
        return props;
      },
      panelContainer: panelContainer ? getHTMLElement(environment, panelContainer) : environment.document.body,
      panelPlacement: panelPlacement !== null && panelPlacement !== void 0 ? panelPlacement : 'input-wrapper-width',
      render: render !== null && render !== void 0 ? render : defaultRender,
      renderNoResults: renderNoResults,
      renderer: defaultedRenderer,
      detachedMediaQuery: detachedMediaQuery !== null && detachedMediaQuery !== void 0 ? detachedMediaQuery : getComputedStyle(environment.document.documentElement).getPropertyValue('--aa-detached-media-query'),
      components: _objectSpread(_objectSpread({}, defaultComponents), components),
      translations: _objectSpread(_objectSpread({}, defaultTranslations), translations)
    },
    core: _objectSpread(_objectSpread({}, core), {}, {
      id: (_core$id = core.id) !== null && _core$id !== void 0 ? _core$id : generateAutocompleteId(),
      environment: environment
    })
  };
}