var _excluded = ["components"];
function _typeof(obj) { "@babel/helpers - typeof"; return _typeof = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) { return typeof obj; } : function (obj) { return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj; }, _typeof(obj); }
function _objectWithoutProperties(source, excluded) { if (source == null) return {}; var target = _objectWithoutPropertiesLoose(source, excluded); var key, i; if (Object.getOwnPropertySymbols) { var sourceSymbolKeys = Object.getOwnPropertySymbols(source); for (i = 0; i < sourceSymbolKeys.length; i++) { key = sourceSymbolKeys[i]; if (excluded.indexOf(key) >= 0) continue; if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue; target[key] = source[key]; } } return target; }
function _objectWithoutPropertiesLoose(source, excluded) { if (source == null) return {}; var target = {}; var sourceKeys = Object.keys(source); var key, i; for (i = 0; i < sourceKeys.length; i++) { key = sourceKeys[i]; if (excluded.indexOf(key) >= 0) continue; target[key] = source[key]; } return target; }
function ownKeys(object, enumerableOnly) { var keys = Object.keys(object); if (Object.getOwnPropertySymbols) { var symbols = Object.getOwnPropertySymbols(object); enumerableOnly && (symbols = symbols.filter(function (sym) { return Object.getOwnPropertyDescriptor(object, sym).enumerable; })), keys.push.apply(keys, symbols); } return keys; }
function _objectSpread(target) { for (var i = 1; i < arguments.length; i++) { var source = null != arguments[i] ? arguments[i] : {}; i % 2 ? ownKeys(Object(source), !0).forEach(function (key) { _defineProperty(target, key, source[key]); }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys(Object(source)).forEach(function (key) { Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key)); }); } return target; }
function _defineProperty(obj, key, value) { key = _toPropertyKey(key); if (key in obj) { Object.defineProperty(obj, key, { value: value, enumerable: true, configurable: true, writable: true }); } else { obj[key] = value; } return obj; }
function _toPropertyKey(arg) { var key = _toPrimitive(arg, "string"); return _typeof(key) === "symbol" ? key : String(key); }
function _toPrimitive(input, hint) { if (_typeof(input) !== "object" || input === null) return input; var prim = input[Symbol.toPrimitive]; if (prim !== undefined) { var res = prim.call(input, hint || "default"); if (_typeof(res) !== "object") return res; throw new TypeError("@@toPrimitive must return a primitive value."); } return (hint === "string" ? String : Number)(input); }
import { createAutocomplete } from '@algolia/autocomplete-core';
import { createRef, debounce, getItemsCount, warn } from '@algolia/autocomplete-shared';
import htm from 'htm';
import { createAutocompleteDom } from './createAutocompleteDom';
import { createEffectWrapper } from './createEffectWrapper';
import { createReactiveWrapper } from './createReactiveWrapper';
import { getDefaultOptions } from './getDefaultOptions';
import { getPanelPlacementStyle } from './getPanelPlacementStyle';
import { renderPanel, renderSearchBox } from './render';
import { userAgents } from './userAgents';
import { mergeDeep, pickBy, setProperties } from './utils';
var instancesCount = 0;
export function autocomplete(options) {
  var _createEffectWrapper = createEffectWrapper(),
    runEffect = _createEffectWrapper.runEffect,
    cleanupEffects = _createEffectWrapper.cleanupEffects,
    runEffects = _createEffectWrapper.runEffects;
  var _createReactiveWrappe = createReactiveWrapper(),
    reactive = _createReactiveWrappe.reactive,
    runReactives = _createReactiveWrappe.runReactives;
  var hasNoResultsSourceTemplateRef = createRef(false);
  var optionsRef = createRef(options);
  var onStateChangeRef = createRef(undefined);
  var props = reactive(function () {
    return getDefaultOptions(optionsRef.current);
  });
  var isDetached = reactive(function () {
    return props.value.core.environment.matchMedia(props.value.renderer.detachedMediaQuery).matches;
  });
  var autocomplete = reactive(function () {
    return createAutocomplete(_objectSpread(_objectSpread({}, props.value.core), {}, {
      onStateChange: function onStateChange(params) {
        var _onStateChangeRef$cur, _props$value$core$onS, _props$value$core;
        hasNoResultsSourceTemplateRef.current = params.state.collections.some(function (collection) {
          return collection.source.templates.noResults;
        });
        (_onStateChangeRef$cur = onStateChangeRef.current) === null || _onStateChangeRef$cur === void 0 ? void 0 : _onStateChangeRef$cur.call(onStateChangeRef, params);
        (_props$value$core$onS = (_props$value$core = props.value.core).onStateChange) === null || _props$value$core$onS === void 0 ? void 0 : _props$value$core$onS.call(_props$value$core, params);
      },
      shouldPanelOpen: optionsRef.current.shouldPanelOpen || function (_ref) {
        var state = _ref.state;
        if (isDetached.value) {
          return true;
        }
        var hasItems = getItemsCount(state) > 0;
        if (!props.value.core.openOnFocus && !state.query) {
          return hasItems;
        }
        var hasNoResultsTemplate = Boolean(hasNoResultsSourceTemplateRef.current || props.value.renderer.renderNoResults);
        return !hasItems && hasNoResultsTemplate || hasItems;
      },
      __autocomplete_metadata: {
        userAgents: userAgents,
        options: options
      }
    }));
  });
  var lastStateRef = createRef(_objectSpread({
    collections: [],
    completion: null,
    context: {},
    isOpen: false,
    query: '',
    activeItemId: null,
    status: 'idle'
  }, props.value.core.initialState));
  var propGetters = {
    getEnvironmentProps: props.value.renderer.getEnvironmentProps,
    getFormProps: props.value.renderer.getFormProps,
    getInputProps: props.value.renderer.getInputProps,
    getItemProps: props.value.renderer.getItemProps,
    getLabelProps: props.value.renderer.getLabelProps,
    getListProps: props.value.renderer.getListProps,
    getPanelProps: props.value.renderer.getPanelProps,
    getRootProps: props.value.renderer.getRootProps
  };
  var autocompleteScopeApi = {
    setActiveItemId: autocomplete.value.setActiveItemId,
    setQuery: autocomplete.value.setQuery,
    setCollections: autocomplete.value.setCollections,
    setIsOpen: autocomplete.value.setIsOpen,
    setStatus: autocomplete.value.setStatus,
    setContext: autocomplete.value.setContext,
    refresh: autocomplete.value.refresh,
    navigator: autocomplete.value.navigator
  };
  var html = reactive(function () {
    return htm.bind(props.value.renderer.renderer.createElement);
  });
  var dom = reactive(function () {
    return createAutocompleteDom({
      autocomplete: autocomplete.value,
      autocompleteScopeApi: autocompleteScopeApi,
      classNames: props.value.renderer.classNames,
      environment: props.value.core.environment,
      isDetached: isDetached.value,
      placeholder: props.value.core.placeholder,
      propGetters: propGetters,
      setIsModalOpen: setIsModalOpen,
      state: lastStateRef.current,
      translations: props.value.renderer.translations
    });
  });
  function setPanelPosition() {
    setProperties(dom.value.panel, {
      style: isDetached.value ? {} : getPanelPlacementStyle({
        panelPlacement: props.value.renderer.panelPlacement,
        container: dom.value.root,
        form: dom.value.form,
        environment: props.value.core.environment
      })
    });
  }
  function scheduleRender(state) {
    lastStateRef.current = state;
    var renderProps = {
      autocomplete: autocomplete.value,
      autocompleteScopeApi: autocompleteScopeApi,
      classNames: props.value.renderer.classNames,
      components: props.value.renderer.components,
      container: props.value.renderer.container,
      html: html.value,
      dom: dom.value,
      panelContainer: isDetached.value ? dom.value.detachedContainer : props.value.renderer.panelContainer,
      propGetters: propGetters,
      state: lastStateRef.current,
      renderer: props.value.renderer.renderer
    };
    var render = !getItemsCount(state) && !hasNoResultsSourceTemplateRef.current && props.value.renderer.renderNoResults || props.value.renderer.render;
    renderSearchBox(renderProps);
    renderPanel(render, renderProps);
  }
  runEffect(function () {
    var environmentProps = autocomplete.value.getEnvironmentProps({
      formElement: dom.value.form,
      panelElement: dom.value.panel,
      inputElement: dom.value.input
    });
    setProperties(props.value.core.environment, environmentProps);
    return function () {
      setProperties(props.value.core.environment, Object.keys(environmentProps).reduce(function (acc, key) {
        return _objectSpread(_objectSpread({}, acc), {}, _defineProperty({}, key, undefined));
      }, {}));
    };
  });
  runEffect(function () {
    var panelContainerElement = isDetached.value ? props.value.core.environment.document.body : props.value.renderer.panelContainer;
    var panelElement = isDetached.value ? dom.value.detachedOverlay : dom.value.panel;
    if (isDetached.value && lastStateRef.current.isOpen) {
      setIsModalOpen(true);
    }
    scheduleRender(lastStateRef.current);
    return function () {
      if (panelContainerElement.contains(panelElement)) {
        panelContainerElement.removeChild(panelElement);
        panelContainerElement.classList.remove('aa-Detached');
      }
    };
  });
  runEffect(function () {
    var containerElement = props.value.renderer.container;
    containerElement.appendChild(dom.value.root);
    return function () {
      containerElement.removeChild(dom.value.root);
    };
  });
  runEffect(function () {
    var debouncedRender = debounce(function (_ref2) {
      var state = _ref2.state;
      scheduleRender(state);
    }, 0);
    onStateChangeRef.current = function (_ref3) {
      var state = _ref3.state,
        prevState = _ref3.prevState;
      if (isDetached.value && prevState.isOpen !== state.isOpen) {
        setIsModalOpen(state.isOpen);
      }

      // The outer DOM might have changed since the last time the panel was
      // positioned. The layout might have shifted vertically for instance.
      // It's therefore safer to re-calculate the panel position before opening
      // it again.
      if (!isDetached.value && state.isOpen && !prevState.isOpen) {
        setPanelPosition();
      }

      // We scroll to the top of the panel whenever the query changes (i.e. new
      // results come in) so that users don't have to.
      if (state.query !== prevState.query) {
        var scrollablePanels = props.value.core.environment.document.querySelectorAll('.aa-Panel--scrollable');
        scrollablePanels.forEach(function (scrollablePanel) {
          if (scrollablePanel.scrollTop !== 0) {
            scrollablePanel.scrollTop = 0;
          }
        });
      }
      debouncedRender({
        state: state
      });
    };
    return function () {
      onStateChangeRef.current = undefined;
    };
  });
  runEffect(function () {
    var onResize = debounce(function () {
      var previousIsDetached = isDetached.value;
      isDetached.value = props.value.core.environment.matchMedia(props.value.renderer.detachedMediaQuery).matches;
      if (previousIsDetached !== isDetached.value) {
        update({});
      } else {
        requestAnimationFrame(setPanelPosition);
      }
    }, 20);
    props.value.core.environment.addEventListener('resize', onResize);
    return function () {
      props.value.core.environment.removeEventListener('resize', onResize);
    };
  });
  runEffect(function () {
    if (!isDetached.value) {
      return function () {};
    }
    function toggleModalClassname(isActive) {
      dom.value.detachedContainer.classList.toggle('aa-DetachedContainer--modal', isActive);
    }
    function onChange(event) {
      toggleModalClassname(event.matches);
    }
    var isModalDetachedMql = props.value.core.environment.matchMedia(getComputedStyle(props.value.core.environment.document.documentElement).getPropertyValue('--aa-detached-modal-media-query'));
    toggleModalClassname(isModalDetachedMql.matches);

    // Prior to Safari 14, `MediaQueryList` isn't based on `EventTarget`,
    // so we must use `addListener` and `removeListener` to observe media query lists.
    // See https://developer.mozilla.org/en-US/docs/Web/API/MediaQueryList/addListener
    var hasModernEventListener = Boolean(isModalDetachedMql.addEventListener);
    hasModernEventListener ? isModalDetachedMql.addEventListener('change', onChange) : isModalDetachedMql.addListener(onChange);
    return function () {
      hasModernEventListener ? isModalDetachedMql.removeEventListener('change', onChange) : isModalDetachedMql.removeListener(onChange);
    };
  });
  runEffect(function () {
    requestAnimationFrame(setPanelPosition);
    return function () {};
  });
  function destroy() {
    instancesCount--;
    cleanupEffects();
  }
  function update() {
    var updatedOptions = arguments.length > 0 && arguments[0] !== undefined ? arguments[0] : {};
    cleanupEffects();
    var _props$value$renderer = props.value.renderer,
      components = _props$value$renderer.components,
      rendererProps = _objectWithoutProperties(_props$value$renderer, _excluded);
    optionsRef.current = mergeDeep(rendererProps, props.value.core, {
      // We need to filter out default components so they can be replaced with
      // a new `renderer`, without getting rid of user components.
      // @MAJOR Deal with registering components with the same name as the
      // default ones. If we disallow overriding default components, we'd just
      // need to pass all `components` here.
      components: pickBy(components, function (_ref4) {
        var value = _ref4.value;
        return !value.hasOwnProperty('__autocomplete_componentName');
      }),
      initialState: lastStateRef.current
    }, updatedOptions);
    runReactives();
    runEffects();
    autocomplete.value.refresh().then(function () {
      scheduleRender(lastStateRef.current);
    });
  }
  function setIsModalOpen(value) {
    var prevValue = props.value.core.environment.document.body.contains(dom.value.detachedOverlay);
    if (value === prevValue) {
      return;
    }
    if (value) {
      props.value.core.environment.document.body.appendChild(dom.value.detachedOverlay);
      props.value.core.environment.document.body.classList.add('aa-Detached');
      dom.value.input.focus();
    } else {
      props.value.core.environment.document.body.removeChild(dom.value.detachedOverlay);
      props.value.core.environment.document.body.classList.remove('aa-Detached');
    }
  }
  process.env.NODE_ENV !== 'production' ? warn(instancesCount === 0, "Autocomplete doesn't support multiple instances running at the same time. Make sure to destroy the previous instance before creating a new one.\n\nSee: https://www.algolia.com/doc/ui-libraries/autocomplete/api-reference/autocomplete-js/autocomplete/#param-destroy") : void 0;
  instancesCount++;
  return _objectSpread(_objectSpread({}, autocompleteScopeApi), {}, {
    update: update,
    destroy: destroy
  });
}