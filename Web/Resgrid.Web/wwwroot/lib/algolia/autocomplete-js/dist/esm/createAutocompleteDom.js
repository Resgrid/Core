function _typeof(obj) { "@babel/helpers - typeof"; return _typeof = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) { return typeof obj; } : function (obj) { return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj; }, _typeof(obj); }
function ownKeys(object, enumerableOnly) { var keys = Object.keys(object); if (Object.getOwnPropertySymbols) { var symbols = Object.getOwnPropertySymbols(object); enumerableOnly && (symbols = symbols.filter(function (sym) { return Object.getOwnPropertyDescriptor(object, sym).enumerable; })), keys.push.apply(keys, symbols); } return keys; }
function _objectSpread(target) { for (var i = 1; i < arguments.length; i++) { var source = null != arguments[i] ? arguments[i] : {}; i % 2 ? ownKeys(Object(source), !0).forEach(function (key) { _defineProperty(target, key, source[key]); }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys(Object(source)).forEach(function (key) { Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key)); }); } return target; }
function _defineProperty(obj, key, value) { key = _toPropertyKey(key); if (key in obj) { Object.defineProperty(obj, key, { value: value, enumerable: true, configurable: true, writable: true }); } else { obj[key] = value; } return obj; }
function _toPropertyKey(arg) { var key = _toPrimitive(arg, "string"); return _typeof(key) === "symbol" ? key : String(key); }
function _toPrimitive(input, hint) { if (_typeof(input) !== "object" || input === null) return input; var prim = input[Symbol.toPrimitive]; if (prim !== undefined) { var res = prim.call(input, hint || "default"); if (_typeof(res) !== "object") return res; throw new TypeError("@@toPrimitive must return a primitive value."); } return (hint === "string" ? String : Number)(input); }
import { ClearIcon, Input, LoadingIcon, SearchIcon } from './elements';
import { getCreateDomElement } from './getCreateDomElement';
import { setProperties } from './utils';
export function createAutocompleteDom(_ref) {
  var autocomplete = _ref.autocomplete,
    autocompleteScopeApi = _ref.autocompleteScopeApi,
    classNames = _ref.classNames,
    environment = _ref.environment,
    isDetached = _ref.isDetached,
    _ref$placeholder = _ref.placeholder,
    placeholder = _ref$placeholder === void 0 ? 'Search' : _ref$placeholder,
    propGetters = _ref.propGetters,
    setIsModalOpen = _ref.setIsModalOpen,
    state = _ref.state,
    translations = _ref.translations;
  var createDomElement = getCreateDomElement(environment);
  var rootProps = propGetters.getRootProps(_objectSpread({
    state: state,
    props: autocomplete.getRootProps({})
  }, autocompleteScopeApi));
  var root = createDomElement('div', _objectSpread({
    class: classNames.root
  }, rootProps));
  var detachedContainer = createDomElement('div', {
    class: classNames.detachedContainer,
    onMouseDown: function onMouseDown(event) {
      event.stopPropagation();
    }
  });
  var detachedOverlay = createDomElement('div', {
    class: classNames.detachedOverlay,
    children: [detachedContainer],
    onMouseDown: function onMouseDown() {
      setIsModalOpen(false);
      autocomplete.setIsOpen(false);
    }
  });
  var labelProps = propGetters.getLabelProps(_objectSpread({
    state: state,
    props: autocomplete.getLabelProps({})
  }, autocompleteScopeApi));
  var submitButton = createDomElement('button', {
    class: classNames.submitButton,
    type: 'submit',
    title: translations.submitButtonTitle,
    children: [SearchIcon({
      environment: environment
    })]
  });
  // @MAJOR Remove the label wrapper for the submit button.
  // The submit button is sufficient for accessibility purposes, and
  // wrapping it with the label actually makes it less accessible (see CR-6077).
  var label = createDomElement('label', _objectSpread({
    class: classNames.label,
    children: [submitButton],
    ariaLabel: translations.submitButtonTitle
  }, labelProps));
  var clearButton = createDomElement('button', {
    class: classNames.clearButton,
    type: 'reset',
    title: translations.clearButtonTitle,
    children: [ClearIcon({
      environment: environment
    })]
  });
  var loadingIndicator = createDomElement('div', {
    class: classNames.loadingIndicator,
    children: [LoadingIcon({
      environment: environment
    })]
  });
  var input = Input({
    class: classNames.input,
    environment: environment,
    state: state,
    getInputProps: propGetters.getInputProps,
    getInputPropsCore: autocomplete.getInputProps,
    autocompleteScopeApi: autocompleteScopeApi,
    isDetached: isDetached
  });
  var inputWrapperPrefix = createDomElement('div', {
    class: classNames.inputWrapperPrefix,
    children: [label, loadingIndicator]
  });
  var inputWrapperSuffix = createDomElement('div', {
    class: classNames.inputWrapperSuffix,
    children: [clearButton]
  });
  var inputWrapper = createDomElement('div', {
    class: classNames.inputWrapper,
    children: [input]
  });
  var formProps = propGetters.getFormProps(_objectSpread({
    state: state,
    props: autocomplete.getFormProps({
      inputElement: input
    })
  }, autocompleteScopeApi));
  var form = createDomElement('form', _objectSpread({
    class: classNames.form,
    children: [inputWrapperPrefix, inputWrapper, inputWrapperSuffix]
  }, formProps));
  var panelProps = propGetters.getPanelProps(_objectSpread({
    state: state,
    props: autocomplete.getPanelProps({})
  }, autocompleteScopeApi));
  var panel = createDomElement('div', _objectSpread({
    class: classNames.panel
  }, panelProps));
  var detachedSearchButtonQuery = createDomElement('div', {
    class: classNames.detachedSearchButtonQuery,
    textContent: state.query
  });
  var detachedSearchButtonPlaceholder = createDomElement('div', {
    class: classNames.detachedSearchButtonPlaceholder,
    hidden: Boolean(state.query),
    textContent: placeholder
  });
  if (process.env.NODE_ENV === 'test') {
    setProperties(panel, {
      'data-testid': 'panel'
    });
  }
  if (isDetached) {
    var detachedSearchButtonIcon = createDomElement('div', {
      class: classNames.detachedSearchButtonIcon,
      children: [SearchIcon({
        environment: environment
      })]
    });
    var detachedSearchButton = createDomElement('button', {
      type: 'button',
      class: classNames.detachedSearchButton,
      title: translations.detachedSearchButtonTitle,
      id: labelProps.id,
      onClick: function onClick() {
        setIsModalOpen(true);
      },
      children: [detachedSearchButtonIcon, detachedSearchButtonPlaceholder, detachedSearchButtonQuery]
    });
    var detachedCancelButton = createDomElement('button', {
      type: 'button',
      class: classNames.detachedCancelButton,
      textContent: translations.detachedCancelButtonText,
      // Prevent `onTouchStart` from closing the panel
      // since it should be initiated by `onClick` only
      onTouchStart: function onTouchStart(event) {
        event.stopPropagation();
      },
      onClick: function onClick() {
        autocomplete.setIsOpen(false);
        setIsModalOpen(false);
      }
    });
    var detachedFormContainer = createDomElement('div', {
      class: classNames.detachedFormContainer,
      children: [form, detachedCancelButton]
    });
    detachedContainer.appendChild(detachedFormContainer);
    root.appendChild(detachedSearchButton);
  } else {
    root.appendChild(form);
  }
  return {
    detachedContainer: detachedContainer,
    detachedOverlay: detachedOverlay,
    detachedSearchButtonQuery: detachedSearchButtonQuery,
    detachedSearchButtonPlaceholder: detachedSearchButtonPlaceholder,
    inputWrapper: inputWrapper,
    input: input,
    root: root,
    form: form,
    label: label,
    submitButton: submitButton,
    clearButton: clearButton,
    loadingIndicator: loadingIndicator,
    panel: panel
  };
}