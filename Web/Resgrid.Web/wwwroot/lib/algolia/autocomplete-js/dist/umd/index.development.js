/*! @algolia/autocomplete-js 1.8.3 | MIT License | © Algolia, Inc. and contributors | https://github.com/algolia/autocomplete */
(function (global, factory) {
  typeof exports === 'object' && typeof module !== 'undefined' ? factory(exports) :
  typeof define === 'function' && define.amd ? define(['exports'], factory) :
  (global = typeof globalThis !== 'undefined' ? globalThis : global || self, factory(global["@algolia/autocomplete-js"] = {}));
})(this, (function (exports) { 'use strict';

  function ownKeys$g(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread2(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$g(Object(source), !0).forEach(function (key) {
        _defineProperty$g(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$g(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _typeof$3(obj) {
    "@babel/helpers - typeof";

    return _typeof$3 = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) {
      return typeof obj;
    } : function (obj) {
      return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj;
    }, _typeof$3(obj);
  }

  function _defineProperty$g(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _extends() {
    _extends = Object.assign || function (target) {
      for (var i = 1; i < arguments.length; i++) {
        var source = arguments[i];

        for (var key in source) {
          if (Object.prototype.hasOwnProperty.call(source, key)) {
            target[key] = source[key];
          }
        }
      }

      return target;
    };

    return _extends.apply(this, arguments);
  }

  function _objectWithoutPropertiesLoose$4(source, excluded) {
    if (source == null) return {};
    var target = {};
    var sourceKeys = Object.keys(source);
    var key, i;

    for (i = 0; i < sourceKeys.length; i++) {
      key = sourceKeys[i];
      if (excluded.indexOf(key) >= 0) continue;
      target[key] = source[key];
    }

    return target;
  }

  function _objectWithoutProperties$4(source, excluded) {
    if (source == null) return {};

    var target = _objectWithoutPropertiesLoose$4(source, excluded);

    var key, i;

    if (Object.getOwnPropertySymbols) {
      var sourceSymbolKeys = Object.getOwnPropertySymbols(source);

      for (i = 0; i < sourceSymbolKeys.length; i++) {
        key = sourceSymbolKeys[i];
        if (excluded.indexOf(key) >= 0) continue;
        if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue;
        target[key] = source[key];
      }
    }

    return target;
  }

  function _slicedToArray$1(arr, i) {
    return _arrayWithHoles$1(arr) || _iterableToArrayLimit$1(arr, i) || _unsupportedIterableToArray$6(arr, i) || _nonIterableRest$1();
  }

  function _toConsumableArray$5(arr) {
    return _arrayWithoutHoles$5(arr) || _iterableToArray$5(arr) || _unsupportedIterableToArray$6(arr) || _nonIterableSpread$5();
  }

  function _arrayWithoutHoles$5(arr) {
    if (Array.isArray(arr)) return _arrayLikeToArray$6(arr);
  }

  function _arrayWithHoles$1(arr) {
    if (Array.isArray(arr)) return arr;
  }

  function _iterableToArray$5(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
  }

  function _iterableToArrayLimit$1(arr, i) {
    var _i = arr == null ? null : typeof Symbol !== "undefined" && arr[Symbol.iterator] || arr["@@iterator"];

    if (_i == null) return;
    var _arr = [];
    var _n = true;
    var _d = false;

    var _s, _e;

    try {
      for (_i = _i.call(arr); !(_n = (_s = _i.next()).done); _n = true) {
        _arr.push(_s.value);

        if (i && _arr.length === i) break;
      }
    } catch (err) {
      _d = true;
      _e = err;
    } finally {
      try {
        if (!_n && _i["return"] != null) _i["return"]();
      } finally {
        if (_d) throw _e;
      }
    }

    return _arr;
  }

  function _unsupportedIterableToArray$6(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray$6(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray$6(o, minLen);
  }

  function _arrayLikeToArray$6(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) arr2[i] = arr[i];

    return arr2;
  }

  function _nonIterableSpread$5() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _nonIterableRest$1() {
    throw new TypeError("Invalid attempt to destructure non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function createRef(initialValue) {
    return {
      current: initialValue
    };
  }

  function debounce(fn, time) {
    var timerId = undefined;
    return function () {
      for (var _len = arguments.length, args = new Array(_len), _key = 0; _key < _len; _key++) {
        args[_key] = arguments[_key];
      }

      if (timerId) {
        clearTimeout(timerId);
      }

      timerId = setTimeout(function () {
        return fn.apply(void 0, args);
      }, time);
    };
  }

  function _slicedToArray(arr, i) {
    return _arrayWithHoles(arr) || _iterableToArrayLimit(arr, i) || _unsupportedIterableToArray$5(arr, i) || _nonIterableRest();
  }

  function _nonIterableRest() {
    throw new TypeError("Invalid attempt to destructure non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _unsupportedIterableToArray$5(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray$5(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray$5(o, minLen);
  }

  function _arrayLikeToArray$5(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) {
      arr2[i] = arr[i];
    }

    return arr2;
  }

  function _iterableToArrayLimit(arr, i) {
    var _i = arr == null ? null : typeof Symbol !== "undefined" && arr[Symbol.iterator] || arr["@@iterator"];

    if (_i == null) return;
    var _arr = [];
    var _n = true;
    var _d = false;

    var _s, _e;

    try {
      for (_i = _i.call(arr); !(_n = (_s = _i.next()).done); _n = true) {
        _arr.push(_s.value);

        if (i && _arr.length === i) break;
      }
    } catch (err) {
      _d = true;
      _e = err;
    } finally {
      try {
        if (!_n && _i["return"] != null) _i["return"]();
      } finally {
        if (_d) throw _e;
      }
    }

    return _arr;
  }

  function _arrayWithHoles(arr) {
    if (Array.isArray(arr)) return arr;
  }

  function _typeof$2(obj) {
    "@babel/helpers - typeof";

    return _typeof$2 = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) {
      return typeof obj;
    } : function (obj) {
      return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj;
    }, _typeof$2(obj);
  }
  /**
   * Decycles objects with circular references.
   * This is used to print cyclic structures in development environment only.
   */


  function decycle(obj) {
    var seen = arguments.length > 1 && arguments[1] !== undefined ? arguments[1] : new Set();

    if (!obj || _typeof$2(obj) !== 'object') {
      return obj;
    }

    if (seen.has(obj)) {
      return '[Circular]';
    }

    var newSeen = seen.add(obj);

    if (Array.isArray(obj)) {
      return obj.map(function (x) {
        return decycle(x, newSeen);
      });
    }

    return Object.fromEntries(Object.entries(obj).map(function (_ref) {
      var _ref2 = _slicedToArray(_ref, 2),
          key = _ref2[0],
          value = _ref2[1];

      return [key, decycle(value, newSeen)];
    }));
  }

  function flatten(values) {
    return values.reduce(function (a, b) {
      return a.concat(b);
    }, []);
  }

  var autocompleteId = 0;
  function generateAutocompleteId() {
    return "autocomplete-".concat(autocompleteId++);
  }

  function getAttributeValueByPath(record, path) {
    return path.reduce(function (current, key) {
      return current && current[key];
    }, record);
  }

  function getItemsCount(state) {
    if (state.collections.length === 0) {
      return 0;
    }

    return state.collections.reduce(function (sum, collection) {
      return sum + collection.items.length;
    }, 0);
  }

  /**
   * Throws an error if the condition is not met in development mode.
   * This is used to make development a better experience to provide guidance as
   * to where the error comes from.
   */
  function invariant(condition, message) {

    if (!condition) {
      throw new Error("[Autocomplete] ".concat(typeof message === 'function' ? message() : message));
    }
  }

  var noop = function noop() {};

  var version = '1.8.3';

  var userAgents$1 = [{
    segment: 'autocomplete-core',
    version: version
  }];

  var warnCache = {
    current: {}
  };
  /**
   * Logs a warning if the condition is not met.
   * This is used to log issues in development environment only.
   */

  function warn(condition, message) {

    if (condition) {
      return;
    }

    var sanitizedMessage = message.trim();
    var hasAlreadyPrinted = warnCache.current[sanitizedMessage];

    if (!hasAlreadyPrinted) {
      warnCache.current[sanitizedMessage] = true; // eslint-disable-next-line no-console

      console.warn("[Autocomplete] ".concat(sanitizedMessage));
    }
  }

  function checkOptions(options) {
    "development" !== 'production' ? warn(!options.debug, 'The `debug` option is meant for development debugging and should not be used in production.') : void 0 ;
  }

  function createInternalCancelablePromise(promise, initialState) {
    var state = initialState;
    return {
      then: function then(onfulfilled, onrejected) {
        return createInternalCancelablePromise(promise.then(createCallback(onfulfilled, state, promise), createCallback(onrejected, state, promise)), state);
      },
      catch: function _catch(onrejected) {
        return createInternalCancelablePromise(promise.catch(createCallback(onrejected, state, promise)), state);
      },
      finally: function _finally(onfinally) {
        if (onfinally) {
          state.onCancelList.push(onfinally);
        }

        return createInternalCancelablePromise(promise.finally(createCallback(onfinally && function () {
          state.onCancelList = [];
          return onfinally();
        }, state, promise)), state);
      },
      cancel: function cancel() {
        state.isCanceled = true;
        var callbacks = state.onCancelList;
        state.onCancelList = [];
        callbacks.forEach(function (callback) {
          callback();
        });
      },
      isCanceled: function isCanceled() {
        return state.isCanceled === true;
      }
    };
  }

  function cancelable(promise) {
    return createInternalCancelablePromise(promise, {
      isCanceled: false,
      onCancelList: []
    });
  }

  function createCallback(onResult, state, fallback) {
    if (!onResult) {
      return fallback;
    }

    return function callback(arg) {
      if (state.isCanceled) {
        return arg;
      }

      return onResult(arg);
    };
  }

  function createCancelablePromiseList() {
    var list = [];
    return {
      add: function add(cancelablePromise) {
        list.push(cancelablePromise);
        return cancelablePromise.finally(function () {
          list = list.filter(function (item) {
            return item !== cancelablePromise;
          });
        });
      },
      cancelAll: function cancelAll() {
        list.forEach(function (promise) {
          return promise.cancel();
        });
      },
      isEmpty: function isEmpty() {
        return list.length === 0;
      }
    };
  }

  /**
   * Creates a runner that executes promises in a concurrent-safe way.
   *
   * This is useful to prevent older promises to resolve after a newer promise,
   * otherwise resulting in stale resolved values.
   */
  function createConcurrentSafePromise() {
    var basePromiseId = -1;
    var latestResolvedId = -1;
    var latestResolvedValue = undefined;
    return function runConcurrentSafePromise(promise) {
      basePromiseId++;
      var currentPromiseId = basePromiseId;
      return Promise.resolve(promise).then(function (x) {
        // The promise might take too long to resolve and get outdated. This would
        // result in resolving stale values.
        // When this happens, we ignore the promise value and return the one
        // coming from the latest resolved value.
        //
        // +----------------------------------+
        // |        100ms                     |
        // | run(1) +--->  R1                 |
        // |        300ms                     |
        // | run(2) +-------------> R2 (SKIP) |
        // |        200ms                     |
        // | run(3) +--------> R3             |
        // +----------------------------------+
        if (latestResolvedValue && currentPromiseId < latestResolvedId) {
          return latestResolvedValue;
        }

        latestResolvedId = currentPromiseId;
        latestResolvedValue = x;
        return x;
      });
    };
  }

  /**
   * Returns the next active item ID from the current state.
   *
   * We allow circular keyboard navigation from the base index.
   * The base index can either be `null` (nothing is highlighted) or `0`
   * (the first item is highlighted).
   * The base index is allowed to get assigned `null` only if
   * `props.defaultActiveItemId` is `null`. This pattern allows to "stop"
   * by the actual query before navigating to other suggestions as seen on
   * Google or Amazon.
   *
   * @param moveAmount The offset to increment (or decrement) the last index
   * @param baseIndex The current index to compute the next index from
   * @param itemCount The number of items
   * @param defaultActiveItemId The default active index to fallback to
   */
  function getNextActiveItemId(moveAmount, baseIndex, itemCount, defaultActiveItemId) {
    if (!itemCount) {
      return null;
    }

    if (moveAmount < 0 && (baseIndex === null || defaultActiveItemId !== null && baseIndex === 0)) {
      return itemCount + moveAmount;
    }

    var numericIndex = (baseIndex === null ? -1 : baseIndex) + moveAmount;

    if (numericIndex <= -1 || numericIndex >= itemCount) {
      return defaultActiveItemId === null ? null : 0;
    }

    return numericIndex;
  }

  function ownKeys$f(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$f(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$f(Object(source), !0).forEach(function (key) {
        _defineProperty$f(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$f(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$f(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _typeof$1(obj) {
    "@babel/helpers - typeof";

    return _typeof$1 = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) {
      return typeof obj;
    } : function (obj) {
      return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj;
    }, _typeof$1(obj);
  }
  function getNormalizedSources(getSources, params) {
    var seenSourceIds = [];
    return Promise.resolve(getSources(params)).then(function (sources) {
      invariant(Array.isArray(sources), function () {
        return "The `getSources` function must return an array of sources but returned type ".concat(JSON.stringify(_typeof$1(sources)), ":\n\n").concat(JSON.stringify(decycle(sources), null, 2));
      });
      return Promise.all(sources // We allow `undefined` and `false` sources to allow users to use
      // `Boolean(query) && source` (=> `false`).
      // We need to remove these values at this point.
      .filter(function (maybeSource) {
        return Boolean(maybeSource);
      }).map(function (source) {
        invariant(typeof source.sourceId === 'string', 'A source must provide a `sourceId` string.');

        if (seenSourceIds.includes(source.sourceId)) {
          throw new Error("[Autocomplete] The `sourceId` ".concat(JSON.stringify(source.sourceId), " is not unique."));
        }

        seenSourceIds.push(source.sourceId);
        var defaultSource = {
          getItemInputValue: function getItemInputValue(_ref) {
            var state = _ref.state;
            return state.query;
          },
          getItemUrl: function getItemUrl() {
            return undefined;
          },
          onSelect: function onSelect(_ref2) {
            var setIsOpen = _ref2.setIsOpen;
            setIsOpen(false);
          },
          onActive: noop,
          onResolve: noop
        };
        Object.keys(defaultSource).forEach(function (key) {
          defaultSource[key].__default = true;
        });

        var normalizedSource = _objectSpread$f(_objectSpread$f({}, defaultSource), source);

        return Promise.resolve(normalizedSource);
      }));
    });
  }

  // We don't have access to the autocomplete source when we call `onKeyDown`
  // or `onClick` because those are native browser events.
  // However, we can get the source from the suggestion index.
  function getCollectionFromActiveItemId(state) {
    // Given 3 sources with respectively 1, 2 and 3 suggestions: [1, 2, 3]
    // We want to get the accumulated counts:
    // [1, 1 + 2, 1 + 2 + 3] = [1, 3, 3 + 3] = [1, 3, 6]
    var accumulatedCollectionsCount = state.collections.map(function (collections) {
      return collections.items.length;
    }).reduce(function (acc, collectionsCount, index) {
      var previousValue = acc[index - 1] || 0;
      var nextValue = previousValue + collectionsCount;
      acc.push(nextValue);
      return acc;
    }, []); // Based on the accumulated counts, we can infer the index of the suggestion.

    var collectionIndex = accumulatedCollectionsCount.reduce(function (acc, current) {
      if (current <= state.activeItemId) {
        return acc + 1;
      }

      return acc;
    }, 0);
    return state.collections[collectionIndex];
  }
  /**
   * Gets the highlighted index relative to a suggestion object (not the absolute
   * highlighted index).
   *
   * Example:
   *  [['a', 'b'], ['c', 'd', 'e'], ['f']]
   *                      ↑
   *         (absolute: 3, relative: 1)
   */


  function getRelativeActiveItemId(_ref) {
    var state = _ref.state,
        collection = _ref.collection;
    var isOffsetFound = false;
    var counter = 0;
    var previousItemsOffset = 0;

    while (isOffsetFound === false) {
      var currentCollection = state.collections[counter];

      if (currentCollection === collection) {
        isOffsetFound = true;
        break;
      }

      previousItemsOffset += currentCollection.items.length;
      counter++;
    }

    return state.activeItemId - previousItemsOffset;
  }

  function getActiveItem(state) {
    var collection = getCollectionFromActiveItemId(state);

    if (!collection) {
      return null;
    }

    var item = collection.items[getRelativeActiveItemId({
      state: state,
      collection: collection
    })];
    var source = collection.source;
    var itemInputValue = source.getItemInputValue({
      item: item,
      state: state
    });
    var itemUrl = source.getItemUrl({
      item: item,
      state: state
    });
    return {
      item: item,
      itemInputValue: itemInputValue,
      itemUrl: itemUrl,
      source: source
    };
  }

  function isOrContainsNode(parent, child) {
    return parent === child || parent.contains(child);
  }

  var regex = /((gt|sm)-|galaxy nexus)|samsung[- ]/i;
  function isSamsung(userAgent) {
    return Boolean(userAgent && userAgent.match(regex));
  }

  function ownKeys$e(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$e(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$e(Object(source), !0).forEach(function (key) {
        _defineProperty$e(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$e(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$e(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function mapToAlgoliaResponse(rawResults) {
    var results = rawResults.map(function (result) {
      var _hits;

      return _objectSpread$e(_objectSpread$e({}, result), {}, {
        hits: (_hits = result.hits) === null || _hits === void 0 ? void 0 : _hits.map(function (hit) {
          // Bring support for the Insights plugin.
          return _objectSpread$e(_objectSpread$e({}, hit), {}, {
            __autocomplete_indexName: result.index,
            __autocomplete_queryID: result.queryID
          });
        })
      });
    });
    return {
      results: results,
      hits: results.map(function (result) {
        return result.hits;
      }).filter(Boolean),
      facetHits: results.map(function (result) {
        var _facetHits;

        return (_facetHits = result.facetHits) === null || _facetHits === void 0 ? void 0 : _facetHits.map(function (facetHit) {
          // Bring support for the highlighting components.
          return {
            label: facetHit.value,
            count: facetHit.count,
            _highlightResult: {
              label: {
                value: facetHit.highlighted
              }
            }
          };
        });
      }).filter(Boolean)
    };
  }

  function ownKeys$d(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$d(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$d(Object(source), !0).forEach(function (key) {
        _defineProperty$d(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$d(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$d(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function createStore(reducer, props, onStoreStateChange) {
    var state = props.initialState;
    return {
      getState: function getState() {
        return state;
      },
      dispatch: function dispatch(action, payload) {
        var prevState = _objectSpread$d({}, state);

        state = reducer(state, {
          type: action,
          props: props,
          payload: payload
        });
        onStoreStateChange({
          state: state,
          prevState: prevState
        });
      },
      pendingRequests: createCancelablePromiseList()
    };
  }

  function ownKeys$c(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$c(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$c(Object(source), !0).forEach(function (key) {
        _defineProperty$c(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$c(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$c(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function getAutocompleteSetters(_ref) {
    var store = _ref.store;

    var setActiveItemId = function setActiveItemId(value) {
      store.dispatch('setActiveItemId', value);
    };

    var setQuery = function setQuery(value) {
      store.dispatch('setQuery', value);
    };

    var setCollections = function setCollections(rawValue) {
      var baseItemId = 0;
      var value = rawValue.map(function (collection) {
        return _objectSpread$c(_objectSpread$c({}, collection), {}, {
          // We flatten the stored items to support calling `getAlgoliaResults`
          // from the source itself.
          items: flatten(collection.items).map(function (item) {
            return _objectSpread$c(_objectSpread$c({}, item), {}, {
              __autocomplete_id: baseItemId++
            });
          })
        });
      });
      store.dispatch('setCollections', value);
    };

    var setIsOpen = function setIsOpen(value) {
      store.dispatch('setIsOpen', value);
    };

    var setStatus = function setStatus(value) {
      store.dispatch('setStatus', value);
    };

    var setContext = function setContext(value) {
      store.dispatch('setContext', value);
    };

    return {
      setActiveItemId: setActiveItemId,
      setQuery: setQuery,
      setCollections: setCollections,
      setIsOpen: setIsOpen,
      setStatus: setStatus,
      setContext: setContext
    };
  }

  function _toConsumableArray$4(arr) {
    return _arrayWithoutHoles$4(arr) || _iterableToArray$4(arr) || _unsupportedIterableToArray$4(arr) || _nonIterableSpread$4();
  }

  function _nonIterableSpread$4() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _unsupportedIterableToArray$4(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray$4(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray$4(o, minLen);
  }

  function _iterableToArray$4(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
  }

  function _arrayWithoutHoles$4(arr) {
    if (Array.isArray(arr)) return _arrayLikeToArray$4(arr);
  }

  function _arrayLikeToArray$4(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) {
      arr2[i] = arr[i];
    }

    return arr2;
  }

  function ownKeys$b(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$b(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$b(Object(source), !0).forEach(function (key) {
        _defineProperty$b(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$b(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$b(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function getDefaultProps(props, pluginSubscribers) {
    var _props$id;
    /* eslint-disable no-restricted-globals */


    var environment = typeof window !== 'undefined' ? window : {};
    /* eslint-enable no-restricted-globals */

    var plugins = props.plugins || [];
    return _objectSpread$b(_objectSpread$b({
      debug: false,
      openOnFocus: false,
      placeholder: '',
      autoFocus: false,
      defaultActiveItemId: null,
      stallThreshold: 300,
      environment: environment,
      shouldPanelOpen: function shouldPanelOpen(_ref) {
        var state = _ref.state;
        return getItemsCount(state) > 0;
      },
      reshape: function reshape(_ref2) {
        var sources = _ref2.sources;
        return sources;
      }
    }, props), {}, {
      // Since `generateAutocompleteId` triggers a side effect (it increments
      // an internal counter), we don't want to execute it if unnecessary.
      id: (_props$id = props.id) !== null && _props$id !== void 0 ? _props$id : generateAutocompleteId(),
      plugins: plugins,
      // The following props need to be deeply defaulted.
      initialState: _objectSpread$b({
        activeItemId: null,
        query: '',
        completion: null,
        collections: [],
        isOpen: false,
        status: 'idle',
        context: {}
      }, props.initialState),
      onStateChange: function onStateChange(params) {
        var _props$onStateChange;

        (_props$onStateChange = props.onStateChange) === null || _props$onStateChange === void 0 ? void 0 : _props$onStateChange.call(props, params);
        plugins.forEach(function (x) {
          var _x$onStateChange;

          return (_x$onStateChange = x.onStateChange) === null || _x$onStateChange === void 0 ? void 0 : _x$onStateChange.call(x, params);
        });
      },
      onSubmit: function onSubmit(params) {
        var _props$onSubmit;

        (_props$onSubmit = props.onSubmit) === null || _props$onSubmit === void 0 ? void 0 : _props$onSubmit.call(props, params);
        plugins.forEach(function (x) {
          var _x$onSubmit;

          return (_x$onSubmit = x.onSubmit) === null || _x$onSubmit === void 0 ? void 0 : _x$onSubmit.call(x, params);
        });
      },
      onReset: function onReset(params) {
        var _props$onReset;

        (_props$onReset = props.onReset) === null || _props$onReset === void 0 ? void 0 : _props$onReset.call(props, params);
        plugins.forEach(function (x) {
          var _x$onReset;

          return (_x$onReset = x.onReset) === null || _x$onReset === void 0 ? void 0 : _x$onReset.call(x, params);
        });
      },
      getSources: function getSources(params) {
        return Promise.all([].concat(_toConsumableArray$4(plugins.map(function (plugin) {
          return plugin.getSources;
        })), [props.getSources]).filter(Boolean).map(function (getSources) {
          return getNormalizedSources(getSources, params);
        })).then(function (nested) {
          return flatten(nested);
        }).then(function (sources) {
          return sources.map(function (source) {
            return _objectSpread$b(_objectSpread$b({}, source), {}, {
              onSelect: function onSelect(params) {
                source.onSelect(params);
                pluginSubscribers.forEach(function (x) {
                  var _x$onSelect;

                  return (_x$onSelect = x.onSelect) === null || _x$onSelect === void 0 ? void 0 : _x$onSelect.call(x, params);
                });
              },
              onActive: function onActive(params) {
                source.onActive(params);
                pluginSubscribers.forEach(function (x) {
                  var _x$onActive;

                  return (_x$onActive = x.onActive) === null || _x$onActive === void 0 ? void 0 : _x$onActive.call(x, params);
                });
              },
              onResolve: function onResolve(params) {
                source.onResolve(params);
                pluginSubscribers.forEach(function (x) {
                  var _x$onResolve;

                  return (_x$onResolve = x.onResolve) === null || _x$onResolve === void 0 ? void 0 : _x$onResolve.call(x, params);
                });
              }
            });
          });
        });
      },
      navigator: _objectSpread$b({
        navigate: function navigate(_ref3) {
          var itemUrl = _ref3.itemUrl;
          environment.location.assign(itemUrl);
        },
        navigateNewTab: function navigateNewTab(_ref4) {
          var itemUrl = _ref4.itemUrl;
          var windowReference = environment.open(itemUrl, '_blank', 'noopener');
          windowReference === null || windowReference === void 0 ? void 0 : windowReference.focus();
        },
        navigateNewWindow: function navigateNewWindow(_ref5) {
          var itemUrl = _ref5.itemUrl;
          environment.open(itemUrl, '_blank', 'noopener');
        }
      }, props.navigator)
    });
  }

  function ownKeys$a(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$a(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$a(Object(source), !0).forEach(function (key) {
        _defineProperty$a(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$a(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$a(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function reshape(_ref) {
    var collections = _ref.collections,
        props = _ref.props,
        state = _ref.state; // Sources are grouped by `sourceId` to conveniently pick them via destructuring.
    // Example: `const { recentSearchesPlugin } = sourcesBySourceId`

    var originalSourcesBySourceId = collections.reduce(function (acc, collection) {
      return _objectSpread$a(_objectSpread$a({}, acc), {}, _defineProperty$a({}, collection.source.sourceId, _objectSpread$a(_objectSpread$a({}, collection.source), {}, {
        getItems: function getItems() {
          // We provide the resolved items from the collection to the `reshape` prop.
          return flatten(collection.items);
        }
      })));
    }, {});

    var _props$plugins$reduce = props.plugins.reduce(function (acc, plugin) {
      if (plugin.reshape) {
        return plugin.reshape(acc);
      }

      return acc;
    }, {
      sourcesBySourceId: originalSourcesBySourceId,
      state: state
    }),
        sourcesBySourceId = _props$plugins$reduce.sourcesBySourceId;

    var reshapeSources = props.reshape({
      sourcesBySourceId: sourcesBySourceId,
      sources: Object.values(sourcesBySourceId),
      state: state
    }); // We reconstruct the collections with the items modified by the `reshape` prop.

    return flatten(reshapeSources).filter(Boolean).map(function (source) {
      return {
        source: source,
        items: source.getItems()
      };
    });
  }

  function _typeof(obj) {
    "@babel/helpers - typeof";

    return _typeof = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (obj) {
      return typeof obj;
    } : function (obj) {
      return obj && "function" == typeof Symbol && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj;
    }, _typeof(obj);
  }

  function ownKeys$9(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$9(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$9(Object(source), !0).forEach(function (key) {
        _defineProperty$9(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$9(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$9(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _toConsumableArray$3(arr) {
    return _arrayWithoutHoles$3(arr) || _iterableToArray$3(arr) || _unsupportedIterableToArray$3(arr) || _nonIterableSpread$3();
  }

  function _nonIterableSpread$3() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _unsupportedIterableToArray$3(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray$3(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray$3(o, minLen);
  }

  function _iterableToArray$3(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
  }

  function _arrayWithoutHoles$3(arr) {
    if (Array.isArray(arr)) return _arrayLikeToArray$3(arr);
  }

  function _arrayLikeToArray$3(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) {
      arr2[i] = arr[i];
    }

    return arr2;
  }

  function isDescription(item) {
    return Boolean(item.execute);
  }

  function isRequesterDescription(description) {
    return Boolean(description === null || description === void 0 ? void 0 : description.execute);
  }

  function preResolve(itemsOrDescription, sourceId, state) {
    if (isRequesterDescription(itemsOrDescription)) {
      var contextParameters = itemsOrDescription.requesterId === 'algolia' ? Object.assign.apply(Object, [{}].concat(_toConsumableArray$3(Object.keys(state.context).map(function (key) {
        var _state$context$key;

        return (_state$context$key = state.context[key]) === null || _state$context$key === void 0 ? void 0 : _state$context$key.__algoliaSearchParameters;
      })))) : {};
      return _objectSpread$9(_objectSpread$9({}, itemsOrDescription), {}, {
        requests: itemsOrDescription.queries.map(function (query) {
          return {
            query: itemsOrDescription.requesterId === 'algolia' ? _objectSpread$9(_objectSpread$9({}, query), {}, {
              params: _objectSpread$9(_objectSpread$9({}, contextParameters), query.params)
            }) : query,
            sourceId: sourceId,
            transformResponse: itemsOrDescription.transformResponse
          };
        })
      });
    }

    return {
      items: itemsOrDescription,
      sourceId: sourceId
    };
  }
  function resolve(items) {
    var packed = items.reduce(function (acc, current) {
      if (!isDescription(current)) {
        acc.push(current);
        return acc;
      }

      var searchClient = current.searchClient,
          execute = current.execute,
          requesterId = current.requesterId,
          requests = current.requests;
      var container = acc.find(function (item) {
        return isDescription(current) && isDescription(item) && item.searchClient === searchClient && Boolean(requesterId) && item.requesterId === requesterId;
      });

      if (container) {
        var _container$items;

        (_container$items = container.items).push.apply(_container$items, _toConsumableArray$3(requests));
      } else {
        var request = {
          execute: execute,
          requesterId: requesterId,
          items: requests,
          searchClient: searchClient
        };
        acc.push(request);
      }

      return acc;
    }, []);
    var values = packed.map(function (maybeDescription) {
      if (!isDescription(maybeDescription)) {
        return Promise.resolve(maybeDescription);
      }

      var _ref = maybeDescription,
          execute = _ref.execute,
          items = _ref.items,
          searchClient = _ref.searchClient;
      return execute({
        searchClient: searchClient,
        requests: items
      });
    });
    return Promise.all(values).then(function (responses) {
      return flatten(responses);
    });
  }
  function postResolve(responses, sources, store) {
    return sources.map(function (source) {
      var matches = responses.filter(function (response) {
        return response.sourceId === source.sourceId;
      });
      var results = matches.map(function (_ref2) {
        var items = _ref2.items;
        return items;
      });
      var transform = matches[0].transformResponse;
      var items = transform ? transform(mapToAlgoliaResponse(results)) : results;
      source.onResolve({
        source: source,
        results: results,
        items: items,
        state: store.getState()
      });
      invariant(Array.isArray(items), function () {
        return "The `getItems` function from source \"".concat(source.sourceId, "\" must return an array of items but returned type ").concat(JSON.stringify(_typeof(items)), ":\n\n").concat(JSON.stringify(decycle(items), null, 2), ".\n\nSee: https://www.algolia.com/doc/ui-libraries/autocomplete/core-concepts/sources/#param-getitems");
      });
      invariant(items.every(Boolean), "The `getItems` function from source \"".concat(source.sourceId, "\" must return an array of items but returned ").concat(JSON.stringify(undefined), ".\n\nDid you forget to return items?\n\nSee: https://www.algolia.com/doc/ui-libraries/autocomplete/core-concepts/sources/#param-getitems"));
      return {
        source: source,
        items: items
      };
    });
  }

  var _excluded$7 = ["event", "nextState", "props", "query", "refresh", "store"];

  function ownKeys$8(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$8(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$8(Object(source), !0).forEach(function (key) {
        _defineProperty$8(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$8(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$8(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _objectWithoutProperties$3(source, excluded) {
    if (source == null) return {};

    var target = _objectWithoutPropertiesLoose$3(source, excluded);

    var key, i;

    if (Object.getOwnPropertySymbols) {
      var sourceSymbolKeys = Object.getOwnPropertySymbols(source);

      for (i = 0; i < sourceSymbolKeys.length; i++) {
        key = sourceSymbolKeys[i];
        if (excluded.indexOf(key) >= 0) continue;
        if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue;
        target[key] = source[key];
      }
    }

    return target;
  }

  function _objectWithoutPropertiesLoose$3(source, excluded) {
    if (source == null) return {};
    var target = {};
    var sourceKeys = Object.keys(source);
    var key, i;

    for (i = 0; i < sourceKeys.length; i++) {
      key = sourceKeys[i];
      if (excluded.indexOf(key) >= 0) continue;
      target[key] = source[key];
    }

    return target;
  }
  var lastStalledId = null;
  var runConcurrentSafePromise = createConcurrentSafePromise();
  function onInput(_ref) {
    var event = _ref.event,
        _ref$nextState = _ref.nextState,
        nextState = _ref$nextState === void 0 ? {} : _ref$nextState,
        props = _ref.props,
        query = _ref.query,
        refresh = _ref.refresh,
        store = _ref.store,
        setters = _objectWithoutProperties$3(_ref, _excluded$7);

    if (lastStalledId) {
      props.environment.clearTimeout(lastStalledId);
    }

    var setCollections = setters.setCollections,
        setIsOpen = setters.setIsOpen,
        setQuery = setters.setQuery,
        setActiveItemId = setters.setActiveItemId,
        setStatus = setters.setStatus;
    setQuery(query);
    setActiveItemId(props.defaultActiveItemId);

    if (!query && props.openOnFocus === false) {
      var _nextState$isOpen;

      var collections = store.getState().collections.map(function (collection) {
        return _objectSpread$8(_objectSpread$8({}, collection), {}, {
          items: []
        });
      });
      setStatus('idle');
      setCollections(collections);
      setIsOpen((_nextState$isOpen = nextState.isOpen) !== null && _nextState$isOpen !== void 0 ? _nextState$isOpen : props.shouldPanelOpen({
        state: store.getState()
      })); // We make sure to update the latest resolved value of the tracked
      // promises to keep late resolving promises from "cancelling" the state
      // updates performed in this code path.
      // We chain with a void promise to respect `onInput`'s expected return type.

      var _request = cancelable(runConcurrentSafePromise(collections).then(function () {
        return Promise.resolve();
      }));

      return store.pendingRequests.add(_request);
    }

    setStatus('loading');
    lastStalledId = props.environment.setTimeout(function () {
      setStatus('stalled');
    }, props.stallThreshold); // We track the entire promise chain triggered by `onInput` before mutating
    // the Autocomplete state to make sure that any state manipulation is based on
    // fresh data regardless of when promises individually resolve.
    // We don't track nested promises and only rely on the full chain resolution,
    // meaning we should only ever manipulate the state once this concurrent-safe
    // promise is resolved.

    var request = cancelable(runConcurrentSafePromise(props.getSources(_objectSpread$8({
      query: query,
      refresh: refresh,
      state: store.getState()
    }, setters)).then(function (sources) {
      return Promise.all(sources.map(function (source) {
        return Promise.resolve(source.getItems(_objectSpread$8({
          query: query,
          refresh: refresh,
          state: store.getState()
        }, setters))).then(function (itemsOrDescription) {
          return preResolve(itemsOrDescription, source.sourceId, store.getState());
        });
      })).then(resolve).then(function (responses) {
        return postResolve(responses, sources, store);
      }).then(function (collections) {
        return reshape({
          collections: collections,
          props: props,
          state: store.getState()
        });
      });
    }))).then(function (collections) {
      var _nextState$isOpen2; // Parameters passed to `onInput` could be stale when the following code
      // executes, because `onInput` calls may not resolve in order.
      // If it becomes a problem we'll need to save the last passed parameters.
      // See: https://codesandbox.io/s/agitated-cookies-y290z


      setStatus('idle');
      setCollections(collections);
      var isPanelOpen = props.shouldPanelOpen({
        state: store.getState()
      });
      setIsOpen((_nextState$isOpen2 = nextState.isOpen) !== null && _nextState$isOpen2 !== void 0 ? _nextState$isOpen2 : props.openOnFocus && !query && isPanelOpen || isPanelOpen);
      var highlightedItem = getActiveItem(store.getState());

      if (store.getState().activeItemId !== null && highlightedItem) {
        var item = highlightedItem.item,
            itemInputValue = highlightedItem.itemInputValue,
            itemUrl = highlightedItem.itemUrl,
            source = highlightedItem.source;
        source.onActive(_objectSpread$8({
          event: event,
          item: item,
          itemInputValue: itemInputValue,
          itemUrl: itemUrl,
          refresh: refresh,
          source: source,
          state: store.getState()
        }, setters));
      }
    }).finally(function () {
      setStatus('idle');

      if (lastStalledId) {
        props.environment.clearTimeout(lastStalledId);
      }
    });
    return store.pendingRequests.add(request);
  }

  var _excluded$6 = ["event", "props", "refresh", "store"];

  function ownKeys$7(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$7(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$7(Object(source), !0).forEach(function (key) {
        _defineProperty$7(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$7(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$7(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _objectWithoutProperties$2(source, excluded) {
    if (source == null) return {};

    var target = _objectWithoutPropertiesLoose$2(source, excluded);

    var key, i;

    if (Object.getOwnPropertySymbols) {
      var sourceSymbolKeys = Object.getOwnPropertySymbols(source);

      for (i = 0; i < sourceSymbolKeys.length; i++) {
        key = sourceSymbolKeys[i];
        if (excluded.indexOf(key) >= 0) continue;
        if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue;
        target[key] = source[key];
      }
    }

    return target;
  }

  function _objectWithoutPropertiesLoose$2(source, excluded) {
    if (source == null) return {};
    var target = {};
    var sourceKeys = Object.keys(source);
    var key, i;

    for (i = 0; i < sourceKeys.length; i++) {
      key = sourceKeys[i];
      if (excluded.indexOf(key) >= 0) continue;
      target[key] = source[key];
    }

    return target;
  }
  function onKeyDown(_ref) {
    var event = _ref.event,
        props = _ref.props,
        refresh = _ref.refresh,
        store = _ref.store,
        setters = _objectWithoutProperties$2(_ref, _excluded$6);

    if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
      // eslint-disable-next-line no-inner-declarations
      var triggerScrollIntoView = function triggerScrollIntoView() {
        var nodeItem = props.environment.document.getElementById("".concat(props.id, "-item-").concat(store.getState().activeItemId));

        if (nodeItem) {
          if (nodeItem.scrollIntoViewIfNeeded) {
            nodeItem.scrollIntoViewIfNeeded(false);
          } else {
            nodeItem.scrollIntoView(false);
          }
        }
      }; // eslint-disable-next-line no-inner-declarations


      var triggerOnActive = function triggerOnActive() {
        var highlightedItem = getActiveItem(store.getState());

        if (store.getState().activeItemId !== null && highlightedItem) {
          var item = highlightedItem.item,
              itemInputValue = highlightedItem.itemInputValue,
              itemUrl = highlightedItem.itemUrl,
              source = highlightedItem.source;
          source.onActive(_objectSpread$7({
            event: event,
            item: item,
            itemInputValue: itemInputValue,
            itemUrl: itemUrl,
            refresh: refresh,
            source: source,
            state: store.getState()
          }, setters));
        }
      }; // Default browser behavior changes the caret placement on ArrowUp and
      // ArrowDown.


      event.preventDefault(); // When re-opening the panel, we need to split the logic to keep the actions
      // synchronized as `onInput` returns a promise.

      if (store.getState().isOpen === false && (props.openOnFocus || Boolean(store.getState().query))) {
        onInput(_objectSpread$7({
          event: event,
          props: props,
          query: store.getState().query,
          refresh: refresh,
          store: store
        }, setters)).then(function () {
          store.dispatch(event.key, {
            nextActiveItemId: props.defaultActiveItemId
          });
          triggerOnActive(); // Since we rely on the DOM, we need to wait for all the micro tasks to
          // finish (which include re-opening the panel) to make sure all the
          // elements are available.

          setTimeout(triggerScrollIntoView, 0);
        });
      } else {
        store.dispatch(event.key, {});
        triggerOnActive();
        triggerScrollIntoView();
      }
    } else if (event.key === 'Escape') {
      // This prevents the default browser behavior on `input[type="search"]`
      // from removing the query right away because we first want to close the
      // panel.
      event.preventDefault();
      store.dispatch(event.key, null); // Hitting the `Escape` key signals the end of a user interaction with the
      // autocomplete. At this point, we should ignore any requests that are still
      // pending and could reopen the panel once they resolve, because that would
      // result in an unsolicited UI behavior.

      store.pendingRequests.cancelAll();
    } else if (event.key === 'Tab') {
      store.dispatch('blur', null); // Hitting the `Escape` key signals the end of a user interaction with the
      // autocomplete. At this point, we should ignore any requests that are still
      // pending and could reopen the panel once they resolve, because that would
      // result in an unsolicited UI behavior.

      store.pendingRequests.cancelAll();
    } else if (event.key === 'Enter') {
      // No active item, so we let the browser handle the native `onSubmit` form
      // event.
      if (store.getState().activeItemId === null || store.getState().collections.every(function (collection) {
        return collection.items.length === 0;
      })) {
        // If requests are still pending when the panel closes, they could reopen
        // the panel once they resolve.
        // We want to prevent any subsequent query from reopening the panel
        // because it would result in an unsolicited UI behavior.
        if (!props.debug) {
          store.pendingRequests.cancelAll();
        }

        return;
      } // This prevents the `onSubmit` event to be sent because an item is
      // highlighted.


      event.preventDefault();

      var _ref2 = getActiveItem(store.getState()),
          item = _ref2.item,
          itemInputValue = _ref2.itemInputValue,
          itemUrl = _ref2.itemUrl,
          source = _ref2.source;

      if (event.metaKey || event.ctrlKey) {
        if (itemUrl !== undefined) {
          source.onSelect(_objectSpread$7({
            event: event,
            item: item,
            itemInputValue: itemInputValue,
            itemUrl: itemUrl,
            refresh: refresh,
            source: source,
            state: store.getState()
          }, setters));
          props.navigator.navigateNewTab({
            itemUrl: itemUrl,
            item: item,
            state: store.getState()
          });
        }
      } else if (event.shiftKey) {
        if (itemUrl !== undefined) {
          source.onSelect(_objectSpread$7({
            event: event,
            item: item,
            itemInputValue: itemInputValue,
            itemUrl: itemUrl,
            refresh: refresh,
            source: source,
            state: store.getState()
          }, setters));
          props.navigator.navigateNewWindow({
            itemUrl: itemUrl,
            item: item,
            state: store.getState()
          });
        }
      } else if (event.altKey) ; else {
        if (itemUrl !== undefined) {
          source.onSelect(_objectSpread$7({
            event: event,
            item: item,
            itemInputValue: itemInputValue,
            itemUrl: itemUrl,
            refresh: refresh,
            source: source,
            state: store.getState()
          }, setters));
          props.navigator.navigate({
            itemUrl: itemUrl,
            item: item,
            state: store.getState()
          });
          return;
        }

        onInput(_objectSpread$7({
          event: event,
          nextState: {
            isOpen: false
          },
          props: props,
          query: itemInputValue,
          refresh: refresh,
          store: store
        }, setters)).then(function () {
          source.onSelect(_objectSpread$7({
            event: event,
            item: item,
            itemInputValue: itemInputValue,
            itemUrl: itemUrl,
            refresh: refresh,
            source: source,
            state: store.getState()
          }, setters));
        });
      }
    }
  }

  var _excluded$5 = ["props", "refresh", "store"],
      _excluded2 = ["inputElement", "formElement", "panelElement"],
      _excluded3 = ["inputElement"],
      _excluded4 = ["inputElement", "maxLength"],
      _excluded5 = ["sourceIndex"],
      _excluded6 = ["sourceIndex"],
      _excluded7 = ["item", "source", "sourceIndex"];

  function ownKeys$6(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$6(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$6(Object(source), !0).forEach(function (key) {
        _defineProperty$6(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$6(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$6(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _objectWithoutProperties$1(source, excluded) {
    if (source == null) return {};

    var target = _objectWithoutPropertiesLoose$1(source, excluded);

    var key, i;

    if (Object.getOwnPropertySymbols) {
      var sourceSymbolKeys = Object.getOwnPropertySymbols(source);

      for (i = 0; i < sourceSymbolKeys.length; i++) {
        key = sourceSymbolKeys[i];
        if (excluded.indexOf(key) >= 0) continue;
        if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue;
        target[key] = source[key];
      }
    }

    return target;
  }

  function _objectWithoutPropertiesLoose$1(source, excluded) {
    if (source == null) return {};
    var target = {};
    var sourceKeys = Object.keys(source);
    var key, i;

    for (i = 0; i < sourceKeys.length; i++) {
      key = sourceKeys[i];
      if (excluded.indexOf(key) >= 0) continue;
      target[key] = source[key];
    }

    return target;
  }
  function getPropGetters(_ref) {
    var props = _ref.props,
        refresh = _ref.refresh,
        store = _ref.store,
        setters = _objectWithoutProperties$1(_ref, _excluded$5);

    var getEnvironmentProps = function getEnvironmentProps(providedProps) {
      var inputElement = providedProps.inputElement,
          formElement = providedProps.formElement,
          panelElement = providedProps.panelElement,
          rest = _objectWithoutProperties$1(providedProps, _excluded2);

      function onMouseDownOrTouchStart(event) {
        // The `onTouchStart`/`onMouseDown` events shouldn't trigger the `blur`
        // handler when it's not an interaction with Autocomplete.
        // We detect it with the following heuristics:
        // - the panel is closed AND there are no pending requests
        //   (no interaction with the autocomplete, no future state updates)
        // - OR the touched target is the input element (should open the panel)
        var isAutocompleteInteraction = store.getState().isOpen || !store.pendingRequests.isEmpty();

        if (!isAutocompleteInteraction || event.target === inputElement) {
          return;
        } // @TODO: support cases where there are multiple Autocomplete instances.
        // Right now, a second instance makes this computation return false.


        var isTargetWithinAutocomplete = [formElement, panelElement].some(function (contextNode) {
          return isOrContainsNode(contextNode, event.target);
        });

        if (isTargetWithinAutocomplete === false) {
          store.dispatch('blur', null); // If requests are still pending when the user closes the panel, they
          // could reopen the panel once they resolve.
          // We want to prevent any subsequent query from reopening the panel
          // because it would result in an unsolicited UI behavior.

          if (!props.debug) {
            store.pendingRequests.cancelAll();
          }
        }
      }

      return _objectSpread$6({
        // We do not rely on the native `blur` event of the input to close the
        // panel, but rather on a custom `touchstart`/`mousedown` event outside
        // of the autocomplete elements.
        // This ensures we don't mistakenly interpret interactions within the
        // autocomplete (but outside of the input) as a signal to close the panel.
        // For example, clicking reset button causes an input blur, but if
        // `openOnFocus=true`, it shouldn't close the panel.
        // On touch devices, scrolling results (`touchmove`) causes an input blur
        // but shouldn't close the panel.
        onTouchStart: onMouseDownOrTouchStart,
        onMouseDown: onMouseDownOrTouchStart,
        // When scrolling on touch devices (mobiles, tablets, etc.), we want to
        // mimic the native platform behavior where the input is blurred to
        // hide the virtual keyboard. This gives more vertical space to
        // discover all the suggestions showing up in the panel.
        onTouchMove: function onTouchMove(event) {
          if (store.getState().isOpen === false || inputElement !== props.environment.document.activeElement || event.target === inputElement) {
            return;
          }

          inputElement.blur();
        }
      }, rest);
    };

    var getRootProps = function getRootProps(rest) {
      return _objectSpread$6({
        role: 'combobox',
        'aria-expanded': store.getState().isOpen,
        'aria-haspopup': 'listbox',
        'aria-owns': store.getState().isOpen ? "".concat(props.id, "-list") : undefined,
        'aria-labelledby': "".concat(props.id, "-label")
      }, rest);
    };

    var getFormProps = function getFormProps(providedProps) {
      providedProps.inputElement;
          var rest = _objectWithoutProperties$1(providedProps, _excluded3);

      return _objectSpread$6({
        action: '',
        noValidate: true,
        role: 'search',
        onSubmit: function onSubmit(event) {
          var _providedProps$inputE;

          event.preventDefault();
          props.onSubmit(_objectSpread$6({
            event: event,
            refresh: refresh,
            state: store.getState()
          }, setters));
          store.dispatch('submit', null);
          (_providedProps$inputE = providedProps.inputElement) === null || _providedProps$inputE === void 0 ? void 0 : _providedProps$inputE.blur();
        },
        onReset: function onReset(event) {
          var _providedProps$inputE2;

          event.preventDefault();
          props.onReset(_objectSpread$6({
            event: event,
            refresh: refresh,
            state: store.getState()
          }, setters));
          store.dispatch('reset', null);
          (_providedProps$inputE2 = providedProps.inputElement) === null || _providedProps$inputE2 === void 0 ? void 0 : _providedProps$inputE2.focus();
        }
      }, rest);
    };

    var getInputProps = function getInputProps(providedProps) {
      var _props$environment$na;

      function onFocus(event) {
        // We want to trigger a query when `openOnFocus` is true
        // because the panel should open with the current query.
        if (props.openOnFocus || Boolean(store.getState().query)) {
          onInput(_objectSpread$6({
            event: event,
            props: props,
            query: store.getState().completion || store.getState().query,
            refresh: refresh,
            store: store
          }, setters));
        }

        store.dispatch('focus', null);
      }

      var _ref2 = providedProps || {};
          _ref2.inputElement;
          var _ref2$maxLength = _ref2.maxLength,
          maxLength = _ref2$maxLength === void 0 ? 512 : _ref2$maxLength,
          rest = _objectWithoutProperties$1(_ref2, _excluded4);

      var activeItem = getActiveItem(store.getState());
      var userAgent = ((_props$environment$na = props.environment.navigator) === null || _props$environment$na === void 0 ? void 0 : _props$environment$na.userAgent) || '';
      var shouldFallbackKeyHint = isSamsung(userAgent);
      var enterKeyHint = activeItem !== null && activeItem !== void 0 && activeItem.itemUrl && !shouldFallbackKeyHint ? 'go' : 'search';
      return _objectSpread$6({
        'aria-autocomplete': 'both',
        'aria-activedescendant': store.getState().isOpen && store.getState().activeItemId !== null ? "".concat(props.id, "-item-").concat(store.getState().activeItemId) : undefined,
        'aria-controls': store.getState().isOpen ? "".concat(props.id, "-list") : undefined,
        'aria-labelledby': "".concat(props.id, "-label"),
        value: store.getState().completion || store.getState().query,
        id: "".concat(props.id, "-input"),
        autoComplete: 'off',
        autoCorrect: 'off',
        autoCapitalize: 'off',
        enterKeyHint: enterKeyHint,
        spellCheck: 'false',
        autoFocus: props.autoFocus,
        placeholder: props.placeholder,
        maxLength: maxLength,
        type: 'search',
        onChange: function onChange(event) {
          onInput(_objectSpread$6({
            event: event,
            props: props,
            query: event.currentTarget.value.slice(0, maxLength),
            refresh: refresh,
            store: store
          }, setters));
        },
        onKeyDown: function onKeyDown$1(event) {
          onKeyDown(_objectSpread$6({
            event: event,
            props: props,
            refresh: refresh,
            store: store
          }, setters));
        },
        onFocus: onFocus,
        // We don't rely on the `blur` event.
        // See explanation in `onTouchStart`/`onMouseDown`.
        // @MAJOR See if we need to keep this handler.
        onBlur: noop,
        onClick: function onClick(event) {
          // When the panel is closed and you click on the input while
          // the input is focused, the `onFocus` event is not triggered
          // (default browser behavior).
          // In an autocomplete context, it makes sense to open the panel in this
          // case.
          // We mimic this event by catching the `onClick` event which
          // triggers the `onFocus` for the panel to open.
          if (providedProps.inputElement === props.environment.document.activeElement && !store.getState().isOpen) {
            onFocus(event);
          }
        }
      }, rest);
    };

    var getAutocompleteId = function getAutocompleteId(instanceId, sourceId) {
      return typeof sourceId !== 'undefined' ? "".concat(instanceId, "-").concat(sourceId) : instanceId;
    };

    var getLabelProps = function getLabelProps(providedProps) {
      var _ref3 = providedProps || {},
          sourceIndex = _ref3.sourceIndex,
          rest = _objectWithoutProperties$1(_ref3, _excluded5);

      return _objectSpread$6({
        htmlFor: "".concat(getAutocompleteId(props.id, sourceIndex), "-input"),
        id: "".concat(getAutocompleteId(props.id, sourceIndex), "-label")
      }, rest);
    };

    var getListProps = function getListProps(providedProps) {
      var _ref4 = providedProps || {},
          sourceIndex = _ref4.sourceIndex,
          rest = _objectWithoutProperties$1(_ref4, _excluded6);

      return _objectSpread$6({
        role: 'listbox',
        'aria-labelledby': "".concat(getAutocompleteId(props.id, sourceIndex), "-label"),
        id: "".concat(getAutocompleteId(props.id, sourceIndex), "-list")
      }, rest);
    };

    var getPanelProps = function getPanelProps(rest) {
      return _objectSpread$6({
        onMouseDown: function onMouseDown(event) {
          // Prevents the `activeElement` from being changed to the panel so
          // that the blur event is not triggered, otherwise it closes the
          // panel.
          event.preventDefault();
        },
        onMouseLeave: function onMouseLeave() {
          store.dispatch('mouseleave', null);
        }
      }, rest);
    };

    var getItemProps = function getItemProps(providedProps) {
      var item = providedProps.item,
          source = providedProps.source,
          sourceIndex = providedProps.sourceIndex,
          rest = _objectWithoutProperties$1(providedProps, _excluded7);

      return _objectSpread$6({
        id: "".concat(getAutocompleteId(props.id, sourceIndex), "-item-").concat(item.__autocomplete_id),
        role: 'option',
        'aria-selected': store.getState().activeItemId === item.__autocomplete_id,
        onMouseMove: function onMouseMove(event) {
          if (item.__autocomplete_id === store.getState().activeItemId) {
            return;
          }

          store.dispatch('mousemove', item.__autocomplete_id);
          var activeItem = getActiveItem(store.getState());

          if (store.getState().activeItemId !== null && activeItem) {
            var _item = activeItem.item,
                itemInputValue = activeItem.itemInputValue,
                itemUrl = activeItem.itemUrl,
                _source = activeItem.source;

            _source.onActive(_objectSpread$6({
              event: event,
              item: _item,
              itemInputValue: itemInputValue,
              itemUrl: itemUrl,
              refresh: refresh,
              source: _source,
              state: store.getState()
            }, setters));
          }
        },
        onMouseDown: function onMouseDown(event) {
          // Prevents the `activeElement` from being changed to the item so it
          // can remain with the current `activeElement`.
          event.preventDefault();
        },
        onClick: function onClick(event) {
          var itemInputValue = source.getItemInputValue({
            item: item,
            state: store.getState()
          });
          var itemUrl = source.getItemUrl({
            item: item,
            state: store.getState()
          }); // If `getItemUrl` is provided, it means that the suggestion
          // is a link, not plain text that aims at updating the query.
          // We can therefore skip the state change because it will update
          // the `activeItemId`, resulting in a UI flash, especially
          // noticeable on mobile.

          var runPreCommand = itemUrl ? Promise.resolve() : onInput(_objectSpread$6({
            event: event,
            nextState: {
              isOpen: false
            },
            props: props,
            query: itemInputValue,
            refresh: refresh,
            store: store
          }, setters));
          runPreCommand.then(function () {
            source.onSelect(_objectSpread$6({
              event: event,
              item: item,
              itemInputValue: itemInputValue,
              itemUrl: itemUrl,
              refresh: refresh,
              source: source,
              state: store.getState()
            }, setters));
          });
        }
      }, rest);
    };

    return {
      getEnvironmentProps: getEnvironmentProps,
      getRootProps: getRootProps,
      getFormProps: getFormProps,
      getLabelProps: getLabelProps,
      getInputProps: getInputProps,
      getPanelProps: getPanelProps,
      getListProps: getListProps,
      getItemProps: getItemProps
    };
  }

  function ownKeys$5(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$5(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$5(Object(source), !0).forEach(function (key) {
        _defineProperty$5(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$5(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$5(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function getMetadata(_ref) {
    var _, _options$__autocomple, _options$__autocomple2, _options$__autocomple3;

    var plugins = _ref.plugins,
        options = _ref.options;
    var optionsKey = (_ = (((_options$__autocomple = options.__autocomplete_metadata) === null || _options$__autocomple === void 0 ? void 0 : _options$__autocomple.userAgents) || [])[0]) === null || _ === void 0 ? void 0 : _.segment;
    var extraOptions = optionsKey ? _defineProperty$5({}, optionsKey, Object.keys(((_options$__autocomple2 = options.__autocomplete_metadata) === null || _options$__autocomple2 === void 0 ? void 0 : _options$__autocomple2.options) || {})) : {};
    return {
      plugins: plugins.map(function (plugin) {
        return {
          name: plugin.name,
          options: Object.keys(plugin.__autocomplete_pluginOptions || [])
        };
      }),
      options: _objectSpread$5({
        'autocomplete-core': Object.keys(options)
      }, extraOptions),
      ua: userAgents$1.concat(((_options$__autocomple3 = options.__autocomplete_metadata) === null || _options$__autocomple3 === void 0 ? void 0 : _options$__autocomple3.userAgents) || [])
    };
  }
  function injectMetadata(_ref3) {
    var _environment$navigato, _environment$navigato2;

    var metadata = _ref3.metadata,
        environment = _ref3.environment;
    var isMetadataEnabled = (_environment$navigato = environment.navigator) === null || _environment$navigato === void 0 ? void 0 : (_environment$navigato2 = _environment$navigato.userAgent) === null || _environment$navigato2 === void 0 ? void 0 : _environment$navigato2.includes('Algolia Crawler');

    if (isMetadataEnabled) {
      var metadataContainer = environment.document.createElement('meta');
      var headRef = environment.document.querySelector('head');
      metadataContainer.name = 'algolia:metadata';
      setTimeout(function () {
        metadataContainer.content = JSON.stringify(metadata);
        headRef.appendChild(metadataContainer);
      }, 0);
    }
  }

  function getCompletion(_ref) {
    var _getActiveItem;

    var state = _ref.state;

    if (state.isOpen === false || state.activeItemId === null) {
      return null;
    }

    return ((_getActiveItem = getActiveItem(state)) === null || _getActiveItem === void 0 ? void 0 : _getActiveItem.itemInputValue) || null;
  }

  function ownKeys$4(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$4(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$4(Object(source), !0).forEach(function (key) {
        _defineProperty$4(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$4(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$4(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  var stateReducer = function stateReducer(state, action) {
    switch (action.type) {
      case 'setActiveItemId':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: action.payload
          });
        }

      case 'setQuery':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            query: action.payload,
            completion: null
          });
        }

      case 'setCollections':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            collections: action.payload
          });
        }

      case 'setIsOpen':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            isOpen: action.payload
          });
        }

      case 'setStatus':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            status: action.payload
          });
        }

      case 'setContext':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            context: _objectSpread$4(_objectSpread$4({}, state.context), action.payload)
          });
        }

      case 'ArrowDown':
        {
          var nextState = _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: action.payload.hasOwnProperty('nextActiveItemId') ? action.payload.nextActiveItemId : getNextActiveItemId(1, state.activeItemId, getItemsCount(state), action.props.defaultActiveItemId)
          });

          return _objectSpread$4(_objectSpread$4({}, nextState), {}, {
            completion: getCompletion({
              state: nextState
            })
          });
        }

      case 'ArrowUp':
        {
          var _nextState = _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: getNextActiveItemId(-1, state.activeItemId, getItemsCount(state), action.props.defaultActiveItemId)
          });

          return _objectSpread$4(_objectSpread$4({}, _nextState), {}, {
            completion: getCompletion({
              state: _nextState
            })
          });
        }

      case 'Escape':
        {
          if (state.isOpen) {
            return _objectSpread$4(_objectSpread$4({}, state), {}, {
              activeItemId: null,
              isOpen: false,
              completion: null
            });
          }

          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: null,
            query: '',
            status: 'idle',
            collections: []
          });
        }

      case 'submit':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: null,
            isOpen: false,
            status: 'idle'
          });
        }

      case 'reset':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: // Since we open the panel on reset when openOnFocus=true
            // we need to restore the highlighted index to the defaultActiveItemId. (DocSearch use-case)
            // Since we close the panel when openOnFocus=false
            // we lose track of the highlighted index. (Query-suggestions use-case)
            action.props.openOnFocus === true ? action.props.defaultActiveItemId : null,
            status: 'idle',
            query: ''
          });
        }

      case 'focus':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: action.props.defaultActiveItemId,
            isOpen: (action.props.openOnFocus || Boolean(state.query)) && action.props.shouldPanelOpen({
              state: state
            })
          });
        }

      case 'blur':
        {
          if (action.props.debug) {
            return state;
          }

          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            isOpen: false,
            activeItemId: null
          });
        }

      case 'mousemove':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: action.payload
          });
        }

      case 'mouseleave':
        {
          return _objectSpread$4(_objectSpread$4({}, state), {}, {
            activeItemId: action.props.defaultActiveItemId
          });
        }

      default:
        invariant(false, "The reducer action ".concat(JSON.stringify(action.type), " is not supported."));
        return state;
    }
  };

  function ownKeys$3(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$3(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$3(Object(source), !0).forEach(function (key) {
        _defineProperty$3(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$3(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$3(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function createAutocomplete(options) {
    checkOptions(options);
    var subscribers = [];
    var props = getDefaultProps(options, subscribers);
    var store = createStore(stateReducer, props, onStoreStateChange);
    var setters = getAutocompleteSetters({
      store: store
    });
    var propGetters = getPropGetters(_objectSpread$3({
      props: props,
      refresh: refresh,
      store: store,
      navigator: props.navigator
    }, setters));

    function onStoreStateChange(_ref) {
      var prevState = _ref.prevState,
          state = _ref.state;
      props.onStateChange(_objectSpread$3({
        prevState: prevState,
        state: state,
        refresh: refresh,
        navigator: props.navigator
      }, setters));
    }

    function refresh() {
      return onInput(_objectSpread$3({
        event: new Event('input'),
        nextState: {
          isOpen: store.getState().isOpen
        },
        props: props,
        navigator: props.navigator,
        query: store.getState().query,
        refresh: refresh,
        store: store
      }, setters));
    }

    props.plugins.forEach(function (plugin) {
      var _plugin$subscribe;

      return (_plugin$subscribe = plugin.subscribe) === null || _plugin$subscribe === void 0 ? void 0 : _plugin$subscribe.call(plugin, _objectSpread$3(_objectSpread$3({}, setters), {}, {
        navigator: props.navigator,
        refresh: refresh,
        onSelect: function onSelect(fn) {
          subscribers.push({
            onSelect: fn
          });
        },
        onActive: function onActive(fn) {
          subscribers.push({
            onActive: fn
          });
        },
        onResolve: function onResolve(fn) {
          subscribers.push({
            onResolve: fn
          });
        }
      }));
    });
    injectMetadata({
      metadata: getMetadata({
        plugins: props.plugins,
        options: options
      }),
      environment: props.environment
    });
    return _objectSpread$3(_objectSpread$3({
      refresh: refresh,
      navigator: props.navigator
    }, propGetters), setters);
  }

  var n$1=function(t,s,r,e){var u;s[0]=0;for(var h=1;h<s.length;h++){var p=s[h++],a=s[h]?(s[0]|=p?1:2,r[s[h++]]):s[++h];3===p?e[0]=a:4===p?e[1]=Object.assign(e[1]||{},a):5===p?(e[1]=e[1]||{})[s[++h]]=a:6===p?e[1][s[++h]]+=a+"":p?(u=t.apply(a,n$1(t,a,r,["",null])),e.push(u),a[0]?s[0]|=2:(s[h-2]=0,s[h]=u)):e.push(a);}return e},t$1=new Map;function htm(s){var r=t$1.get(this);return r||(r=new Map,t$1.set(this,r)),(r=n$1(this,r.get(s)||(r.set(s,r=function(n){for(var t,s,r=1,e="",u="",h=[0],p=function(n){1===r&&(n||(e=e.replace(/^\s*\n\s*|\s*\n\s*$/g,"")))?h.push(0,n,e):3===r&&(n||e)?(h.push(3,n,e),r=2):2===r&&"..."===e&&n?h.push(4,n,0):2===r&&e&&!n?h.push(5,0,!0,e):r>=5&&((e||!n&&5===r)&&(h.push(r,0,e,s),r=6),n&&(h.push(r,n,0,s),r=6)),e="";},a=0;a<n.length;a++){a&&(1===r&&p(),p(a));for(var l=0;l<n[a].length;l++)t=n[a][l],1===r?"<"===t?(p(),h=[h],r=3):e+=t:4===r?"--"===e&&">"===t?(r=1,e=""):e=t+e[0]:u?t===u?u="":e+=t:'"'===t||"'"===t?u=t:">"===t?(p(),r=1):r&&("="===t?(r=5,s=e,e=""):"/"===t&&(r<5||">"===n[a][l+1])?(p(),3===r&&(h=h[0]),r=h,(h=h[0]).push(2,0,r),r=0):" "===t||"\t"===t||"\n"===t||"\r"===t?(p(),r=2):e+=t),3===r&&"!--"===e&&(r=4,h=h[0]);}return p(),h}(s)),r),arguments,[])).length>1?r:r[0]}

  var ClearIcon = function ClearIcon(_ref) {
    var environment = _ref.environment;
    var element = environment.document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    element.setAttribute('class', 'aa-ClearIcon');
    element.setAttribute('viewBox', '0 0 24 24');
    element.setAttribute('width', '18');
    element.setAttribute('height', '18');
    element.setAttribute('fill', 'currentColor');
    var path = environment.document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', 'M5.293 6.707l5.293 5.293-5.293 5.293c-0.391 0.391-0.391 1.024 0 1.414s1.024 0.391 1.414 0l5.293-5.293 5.293 5.293c0.391 0.391 1.024 0.391 1.414 0s0.391-1.024 0-1.414l-5.293-5.293 5.293-5.293c0.391-0.391 0.391-1.024 0-1.414s-1.024-0.391-1.414 0l-5.293 5.293-5.293-5.293c-0.391-0.391-1.024-0.391-1.414 0s-0.391 1.024 0 1.414z');
    element.appendChild(path);
    return element;
  };

  function getHTMLElement(environment, value) {
    if (typeof value === 'string') {
      var element = environment.document.querySelector(value);
      invariant(element !== null, "The element ".concat(JSON.stringify(value), " is not in the document."));
      return element;
    }

    return value;
  }

  function mergeClassNames() {
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

  var isPlainObject = function isPlainObject(value) {
    return value && _typeof$3(value) === 'object' && Object.prototype.toString.call(value) === '[object Object]';
  };

  function mergeDeep() {
    for (var _len = arguments.length, values = new Array(_len), _key = 0; _key < _len; _key++) {
      values[_key] = arguments[_key];
    }

    return values.reduce(function (acc, current) {
      Object.keys(current).forEach(function (key) {
        var accValue = acc[key];
        var currentValue = current[key];

        if (Array.isArray(accValue) && Array.isArray(currentValue)) {
          acc[key] = accValue.concat.apply(accValue, _toConsumableArray$5(currentValue));
        } else if (isPlainObject(accValue) && isPlainObject(currentValue)) {
          acc[key] = mergeDeep(accValue, currentValue);
        } else {
          acc[key] = currentValue;
        }
      });
      return acc;
    }, {});
  }

  function pickBy(obj, predicate) {
    return Object.entries(obj).reduce(function (acc, _ref) {
      var _ref2 = _slicedToArray$1(_ref, 2),
          key = _ref2[0],
          value = _ref2[1];

      if (predicate({
        key: key,
        value: value
      })) {
        return _objectSpread2(_objectSpread2({}, acc), {}, _defineProperty$g({}, key, value));
      }

      return acc;
    }, {});
  }

  /* eslint-disable */

  /**
   * Touch-specific event aliases
   *
   * See https://w3c.github.io/touch-events/#extensions-to-the-globaleventhandlers-mixin
   */
  var TOUCH_EVENTS_ALIASES = ['ontouchstart', 'ontouchend', 'ontouchmove', 'ontouchcancel'];
  /*
   * Taken from Preact
   *
   * See https://github.com/preactjs/preact/blob/6ab49d9020740127577bf4af66bf63f4af7f9fee/src/diff/props.js#L58-L151
   */

  function setStyle(style, key, value) {
    if (value === null) {
      style[key] = '';
    } else if (typeof value !== 'number') {
      style[key] = value;
    } else {
      style[key] = value + 'px';
    }
  }
  /**
   * Proxy an event to hooked event handlers
   */


  function eventProxy(event) {
    this._listeners[event.type](event);
  }
  /**
   * Set a property value on a DOM node
   */


  function setProperty(dom, name, value) {
    var useCapture;
    var nameLower;
    var oldValue = dom[name];

    if (name === 'style') {
      if (typeof value == 'string') {
        dom.style = value;
      } else {
        if (value === null) {
          dom.style = '';
        } else {
          for (name in value) {
            if (!oldValue || value[name] !== oldValue[name]) {
              setStyle(dom.style, name, value[name]);
            }
          }
        }
      }
    } // Benchmark for comparison: https://esbench.com/bench/574c954bdb965b9a00965ac6
    else if (name[0] === 'o' && name[1] === 'n') {
      useCapture = name !== (name = name.replace(/Capture$/, ''));
      nameLower = name.toLowerCase();
      if (nameLower in dom || TOUCH_EVENTS_ALIASES.includes(nameLower)) name = nameLower;
      name = name.slice(2);
      if (!dom._listeners) dom._listeners = {};
      dom._listeners[name] = value;

      if (value) {
        if (!oldValue) dom.addEventListener(name, eventProxy, useCapture);
      } else {
        dom.removeEventListener(name, eventProxy, useCapture);
      }
    } else if (name !== 'list' && name !== 'tagName' && // HTMLButtonElement.form and HTMLInputElement.form are read-only but can be set using
    // setAttribute
    name !== 'form' && name !== 'type' && name !== 'size' && name !== 'download' && name !== 'href' && name in dom) {
      dom[name] = value == null ? '' : value;
    } else if (typeof value != 'function' && name !== 'dangerouslySetInnerHTML') {
      if (value == null || value === false && // ARIA-attributes have a different notion of boolean values.
      // The value `false` is different from the attribute not
      // existing on the DOM, so we can't remove it. For non-boolean
      // ARIA-attributes we could treat false as a removal, but the
      // amount of exceptions would cost us too many bytes. On top of
      // that other VDOM frameworks also always stringify `false`.
      !/^ar/.test(name)) {
        dom.removeAttribute(name);
      } else {
        dom.setAttribute(name, value);
      }
    }
  }

  function getNormalizedName(name) {
    switch (name) {
      case 'onChange':
        return 'onInput';

      default:
        return name;
    }
  }

  function setProperties(dom, props) {
    for (var name in props) {
      setProperty(dom, getNormalizedName(name), props[name]);
    }
  }
  function setPropertiesWithoutEvents(dom, props) {
    for (var name in props) {
      if (!(name[0] === 'o' && name[1] === 'n')) {
        setProperty(dom, getNormalizedName(name), props[name]);
      }
    }
  }

  var _excluded$4 = ["children"];
  function getCreateDomElement(environment) {
    return function createDomElement(tagName, _ref) {
      var _ref$children = _ref.children,
          children = _ref$children === void 0 ? [] : _ref$children,
          props = _objectWithoutProperties$4(_ref, _excluded$4);

      var element = environment.document.createElement(tagName);
      setProperties(element, props);
      element.append.apply(element, _toConsumableArray$5(children));
      return element;
    };
  }

  var _excluded$3 = ["autocompleteScopeApi", "environment", "classNames", "getInputProps", "getInputPropsCore", "isDetached", "state"];
  var Input = function Input(_ref) {
    var autocompleteScopeApi = _ref.autocompleteScopeApi,
        environment = _ref.environment;
        _ref.classNames;
        var getInputProps = _ref.getInputProps,
        getInputPropsCore = _ref.getInputPropsCore,
        isDetached = _ref.isDetached,
        state = _ref.state,
        props = _objectWithoutProperties$4(_ref, _excluded$3);

    var createDomElement = getCreateDomElement(environment);
    var element = createDomElement('input', props);
    var inputProps = getInputProps(_objectSpread2({
      state: state,
      props: getInputPropsCore({
        inputElement: element
      }),
      inputElement: element
    }, autocompleteScopeApi));
    setProperties(element, _objectSpread2(_objectSpread2({}, inputProps), {}, {
      onKeyDown: function onKeyDown(event) {
        // In detached mode we don't want to close the panel when hitting `Tab`.
        if (isDetached && event.key === 'Tab') {
          return;
        }

        inputProps.onKeyDown(event);
      }
    }));
    return element;
  };

  var LoadingIcon = function LoadingIcon(_ref) {
    var environment = _ref.environment;
    var element = environment.document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    element.setAttribute('class', 'aa-LoadingIcon');
    element.setAttribute('viewBox', '0 0 100 100');
    element.setAttribute('width', '20');
    element.setAttribute('height', '20');
    element.innerHTML = "<circle\n  cx=\"50\"\n  cy=\"50\"\n  fill=\"none\"\n  r=\"35\"\n  stroke=\"currentColor\"\n  stroke-dasharray=\"164.93361431346415 56.97787143782138\"\n  stroke-width=\"6\"\n>\n  <animateTransform\n    attributeName=\"transform\"\n    type=\"rotate\"\n    repeatCount=\"indefinite\"\n    dur=\"1s\"\n    values=\"0 50 50;90 50 50;180 50 50;360 50 50\"\n    keyTimes=\"0;0.40;0.65;1\"\n  />\n</circle>";
    return element;
  };

  var SearchIcon = function SearchIcon(_ref) {
    var environment = _ref.environment;
    var element = environment.document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    element.setAttribute('class', 'aa-SubmitIcon');
    element.setAttribute('viewBox', '0 0 24 24');
    element.setAttribute('width', '20');
    element.setAttribute('height', '20');
    element.setAttribute('fill', 'currentColor');
    var path = environment.document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', 'M16.041 15.856c-0.034 0.026-0.067 0.055-0.099 0.087s-0.060 0.064-0.087 0.099c-1.258 1.213-2.969 1.958-4.855 1.958-1.933 0-3.682-0.782-4.95-2.050s-2.050-3.017-2.050-4.95 0.782-3.682 2.050-4.95 3.017-2.050 4.95-2.050 3.682 0.782 4.95 2.050 2.050 3.017 2.050 4.95c0 1.886-0.745 3.597-1.959 4.856zM21.707 20.293l-3.675-3.675c1.231-1.54 1.968-3.493 1.968-5.618 0-2.485-1.008-4.736-2.636-6.364s-3.879-2.636-6.364-2.636-4.736 1.008-6.364 2.636-2.636 3.879-2.636 6.364 1.008 4.736 2.636 6.364 3.879 2.636 6.364 2.636c2.125 0 4.078-0.737 5.618-1.968l3.675 3.675c0.391 0.391 1.024 0.391 1.414 0s0.391-1.024 0-1.414z');
    element.appendChild(path);
    return element;
  };

  function createAutocompleteDom(_ref) {
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
    var rootProps = propGetters.getRootProps(_objectSpread2({
      state: state,
      props: autocomplete.getRootProps({})
    }, autocompleteScopeApi));
    var root = createDomElement('div', _objectSpread2({
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
    var labelProps = propGetters.getLabelProps(_objectSpread2({
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
    var label = createDomElement('label', _objectSpread2({
      class: classNames.label,
      children: [submitButton]
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
    var formProps = propGetters.getFormProps(_objectSpread2({
      state: state,
      props: autocomplete.getFormProps({
        inputElement: input
      })
    }, autocompleteScopeApi));
    var form = createDomElement('form', _objectSpread2({
      class: classNames.form,
      children: [inputWrapperPrefix, inputWrapper, inputWrapperSuffix]
    }, formProps));
    var panelProps = propGetters.getPanelProps(_objectSpread2({
      state: state,
      props: autocomplete.getPanelProps({})
    }, autocompleteScopeApi));
    var panel = createDomElement('div', _objectSpread2({
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

    if ("development" === 'test') {
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

  function createEffectWrapper() {
    var effects = [];
    var cleanups = [];

    function runEffect(fn) {
      effects.push(fn);
      var effectCleanup = fn();
      cleanups.push(effectCleanup);
    }

    return {
      runEffect: runEffect,
      cleanupEffects: function cleanupEffects() {
        var currentCleanups = cleanups;
        cleanups = [];
        currentCleanups.forEach(function (cleanup) {
          cleanup();
        });
      },
      runEffects: function runEffects() {
        var currentEffects = effects;
        effects = [];
        currentEffects.forEach(function (effect) {
          runEffect(effect);
        });
      }
    };
  }

  function createReactiveWrapper() {
    var reactives = [];
    return {
      reactive: function reactive(value) {
        var current = value();
        var reactive = {
          _fn: value,
          _ref: {
            current: current
          },

          get value() {
            return this._ref.current;
          },

          set value(value) {
            this._ref.current = value;
          }

        };
        reactives.push(reactive);
        return reactive;
      },
      runReactives: function runReactives() {
        reactives.forEach(function (value) {
          value._ref.current = value._fn();
        });
      }
    };
  }

  var n,l,u,t,r,o,e={},c=[],s=/acit|ex(?:s|g|n|p|$)|rph|grid|ows|mnc|ntw|ine[ch]|zoo|^ord|itera/i;function a(n,l){for(var u in l)n[u]=l[u];return n}function h(n){var l=n.parentNode;l&&l.removeChild(n);}function v(l,u,i){var t,r,o,f={};for(o in u)"key"==o?t=u[o]:"ref"==o?r=u[o]:f[o]=u[o];if(arguments.length>2&&(f.children=arguments.length>3?n.call(arguments,2):i),"function"==typeof l&&null!=l.defaultProps)for(o in l.defaultProps)void 0===f[o]&&(f[o]=l.defaultProps[o]);return y(l,f,t,r,null)}function y(n,i,t,r,o){var f={type:n,props:i,key:t,ref:r,__k:null,__:null,__b:0,__e:null,__d:void 0,__c:null,__h:null,constructor:void 0,__v:null==o?++u:o};return null==o&&null!=l.vnode&&l.vnode(f),f}function d(n){return n.children}function _(n,l){this.props=n,this.context=l;}function k(n,l){if(null==l)return n.__?k(n.__,n.__.__k.indexOf(n)+1):null;for(var u;l<n.__k.length;l++)if(null!=(u=n.__k[l])&&null!=u.__e)return u.__e;return "function"==typeof n.type?k(n):null}function b(n){var l,u;if(null!=(n=n.__)&&null!=n.__c){for(n.__e=n.__c.base=null,l=0;l<n.__k.length;l++)if(null!=(u=n.__k[l])&&null!=u.__e){n.__e=n.__c.base=u.__e;break}return b(n)}}function m(n){(!n.__d&&(n.__d=!0)&&t.push(n)&&!g.__r++||o!==l.debounceRendering)&&((o=l.debounceRendering)||r)(g);}function g(){for(var n;g.__r=t.length;)n=t.sort(function(n,l){return n.__v.__b-l.__v.__b}),t=[],n.some(function(n){var l,u,i,t,r,o;n.__d&&(r=(t=(l=n).__v).__e,(o=l.__P)&&(u=[],(i=a({},t)).__v=t.__v+1,j(o,t,i,l.__n,void 0!==o.ownerSVGElement,null!=t.__h?[r]:null,u,null==r?k(t):r,t.__h),z(u,t),t.__e!=r&&b(t)));});}function w(n,l,u,i,t,r,o,f,s,a){var h,v,p,_,b,m,g,w=i&&i.__k||c,A=w.length;for(u.__k=[],h=0;h<l.length;h++)if(null!=(_=u.__k[h]=null==(_=l[h])||"boolean"==typeof _?null:"string"==typeof _||"number"==typeof _||"bigint"==typeof _?y(null,_,null,null,_):Array.isArray(_)?y(d,{children:_},null,null,null):_.__b>0?y(_.type,_.props,_.key,null,_.__v):_)){if(_.__=u,_.__b=u.__b+1,null===(p=w[h])||p&&_.key==p.key&&_.type===p.type)w[h]=void 0;else for(v=0;v<A;v++){if((p=w[v])&&_.key==p.key&&_.type===p.type){w[v]=void 0;break}p=null;}j(n,_,p=p||e,t,r,o,f,s,a),b=_.__e,(v=_.ref)&&p.ref!=v&&(g||(g=[]),p.ref&&g.push(p.ref,null,_),g.push(v,_.__c||b,_)),null!=b?(null==m&&(m=b),"function"==typeof _.type&&_.__k===p.__k?_.__d=s=x(_,s,n):s=P(n,_,p,w,b,s),"function"==typeof u.type&&(u.__d=s)):s&&p.__e==s&&s.parentNode!=n&&(s=k(p));}for(u.__e=m,h=A;h--;)null!=w[h]&&("function"==typeof u.type&&null!=w[h].__e&&w[h].__e==u.__d&&(u.__d=k(i,h+1)),N(w[h],w[h]));if(g)for(h=0;h<g.length;h++)M(g[h],g[++h],g[++h]);}function x(n,l,u){for(var i,t=n.__k,r=0;t&&r<t.length;r++)(i=t[r])&&(i.__=n,l="function"==typeof i.type?x(i,l,u):P(u,i,i,t,i.__e,l));return l}function P(n,l,u,i,t,r){var o,f,e;if(void 0!==l.__d)o=l.__d,l.__d=void 0;else if(null==u||t!=r||null==t.parentNode)n:if(null==r||r.parentNode!==n)n.appendChild(t),o=null;else {for(f=r,e=0;(f=f.nextSibling)&&e<i.length;e+=2)if(f==t)break n;n.insertBefore(t,r),o=r;}return void 0!==o?o:t.nextSibling}function C(n,l,u,i,t){var r;for(r in u)"children"===r||"key"===r||r in l||H(n,r,null,u[r],i);for(r in l)t&&"function"!=typeof l[r]||"children"===r||"key"===r||"value"===r||"checked"===r||u[r]===l[r]||H(n,r,l[r],u[r],i);}function $(n,l,u){"-"===l[0]?n.setProperty(l,u):n[l]=null==u?"":"number"!=typeof u||s.test(l)?u:u+"px";}function H(n,l,u,i,t){var r;n:if("style"===l)if("string"==typeof u)n.style.cssText=u;else {if("string"==typeof i&&(n.style.cssText=i=""),i)for(l in i)u&&l in u||$(n.style,l,"");if(u)for(l in u)i&&u[l]===i[l]||$(n.style,l,u[l]);}else if("o"===l[0]&&"n"===l[1])r=l!==(l=l.replace(/Capture$/,"")),l=l.toLowerCase()in n?l.toLowerCase().slice(2):l.slice(2),n.l||(n.l={}),n.l[l+r]=u,u?i||n.addEventListener(l,r?T:I,r):n.removeEventListener(l,r?T:I,r);else if("dangerouslySetInnerHTML"!==l){if(t)l=l.replace(/xlink[H:h]/,"h").replace(/sName$/,"s");else if("href"!==l&&"list"!==l&&"form"!==l&&"tabIndex"!==l&&"download"!==l&&l in n)try{n[l]=null==u?"":u;break n}catch(n){}"function"==typeof u||(null!=u&&(!1!==u||"a"===l[0]&&"r"===l[1])?n.setAttribute(l,u):n.removeAttribute(l));}}function I(n){this.l[n.type+!1](l.event?l.event(n):n);}function T(n){this.l[n.type+!0](l.event?l.event(n):n);}function j(n,u,i,t,r,o,f,e,c){var s,h,v,y,p,k,b,m,g,x,A,P=u.type;if(void 0!==u.constructor)return null;null!=i.__h&&(c=i.__h,e=u.__e=i.__e,u.__h=null,o=[e]),(s=l.__b)&&s(u);try{n:if("function"==typeof P){if(m=u.props,g=(s=P.contextType)&&t[s.__c],x=s?g?g.props.value:s.__:t,i.__c?b=(h=u.__c=i.__c).__=h.__E:("prototype"in P&&P.prototype.render?u.__c=h=new P(m,x):(u.__c=h=new _(m,x),h.constructor=P,h.render=O),g&&g.sub(h),h.props=m,h.state||(h.state={}),h.context=x,h.__n=t,v=h.__d=!0,h.__h=[]),null==h.__s&&(h.__s=h.state),null!=P.getDerivedStateFromProps&&(h.__s==h.state&&(h.__s=a({},h.__s)),a(h.__s,P.getDerivedStateFromProps(m,h.__s))),y=h.props,p=h.state,v)null==P.getDerivedStateFromProps&&null!=h.componentWillMount&&h.componentWillMount(),null!=h.componentDidMount&&h.__h.push(h.componentDidMount);else {if(null==P.getDerivedStateFromProps&&m!==y&&null!=h.componentWillReceiveProps&&h.componentWillReceiveProps(m,x),!h.__e&&null!=h.shouldComponentUpdate&&!1===h.shouldComponentUpdate(m,h.__s,x)||u.__v===i.__v){h.props=m,h.state=h.__s,u.__v!==i.__v&&(h.__d=!1),h.__v=u,u.__e=i.__e,u.__k=i.__k,u.__k.forEach(function(n){n&&(n.__=u);}),h.__h.length&&f.push(h);break n}null!=h.componentWillUpdate&&h.componentWillUpdate(m,h.__s,x),null!=h.componentDidUpdate&&h.__h.push(function(){h.componentDidUpdate(y,p,k);});}h.context=x,h.props=m,h.state=h.__s,(s=l.__r)&&s(u),h.__d=!1,h.__v=u,h.__P=n,s=h.render(h.props,h.state,h.context),h.state=h.__s,null!=h.getChildContext&&(t=a(a({},t),h.getChildContext())),v||null==h.getSnapshotBeforeUpdate||(k=h.getSnapshotBeforeUpdate(y,p)),A=null!=s&&s.type===d&&null==s.key?s.props.children:s,w(n,Array.isArray(A)?A:[A],u,i,t,r,o,f,e,c),h.base=u.__e,u.__h=null,h.__h.length&&f.push(h),b&&(h.__E=h.__=null),h.__e=!1;}else null==o&&u.__v===i.__v?(u.__k=i.__k,u.__e=i.__e):u.__e=L(i.__e,u,i,t,r,o,f,c);(s=l.diffed)&&s(u);}catch(n){u.__v=null,(c||null!=o)&&(u.__e=e,u.__h=!!c,o[o.indexOf(e)]=null),l.__e(n,u,i);}}function z(n,u){l.__c&&l.__c(u,n),n.some(function(u){try{n=u.__h,u.__h=[],n.some(function(n){n.call(u);});}catch(n){l.__e(n,u.__v);}});}function L(l,u,i,t,r,o,f,c){var s,a,v,y=i.props,p=u.props,d=u.type,_=0;if("svg"===d&&(r=!0),null!=o)for(;_<o.length;_++)if((s=o[_])&&"setAttribute"in s==!!d&&(d?s.localName===d:3===s.nodeType)){l=s,o[_]=null;break}if(null==l){if(null===d)return document.createTextNode(p);l=r?document.createElementNS("http://www.w3.org/2000/svg",d):document.createElement(d,p.is&&p),o=null,c=!1;}if(null===d)y===p||c&&l.data===p||(l.data=p);else {if(o=o&&n.call(l.childNodes),a=(y=i.props||e).dangerouslySetInnerHTML,v=p.dangerouslySetInnerHTML,!c){if(null!=o)for(y={},_=0;_<l.attributes.length;_++)y[l.attributes[_].name]=l.attributes[_].value;(v||a)&&(v&&(a&&v.__html==a.__html||v.__html===l.innerHTML)||(l.innerHTML=v&&v.__html||""));}if(C(l,p,y,r,c),v)u.__k=[];else if(_=u.props.children,w(l,Array.isArray(_)?_:[_],u,i,t,r&&"foreignObject"!==d,o,f,o?o[0]:i.__k&&k(i,0),c),null!=o)for(_=o.length;_--;)null!=o[_]&&h(o[_]);c||("value"in p&&void 0!==(_=p.value)&&(_!==y.value||_!==l.value||"progress"===d&&!_)&&H(l,"value",_,y.value,!1),"checked"in p&&void 0!==(_=p.checked)&&_!==l.checked&&H(l,"checked",_,y.checked,!1));}return l}function M(n,u,i){try{"function"==typeof n?n(u):n.current=u;}catch(n){l.__e(n,i);}}function N(n,u,i){var t,r;if(l.unmount&&l.unmount(n),(t=n.ref)&&(t.current&&t.current!==n.__e||M(t,null,u)),null!=(t=n.__c)){if(t.componentWillUnmount)try{t.componentWillUnmount();}catch(n){l.__e(n,u);}t.base=t.__P=null;}if(t=n.__k)for(r=0;r<t.length;r++)t[r]&&N(t[r],u,"function"!=typeof n.type);i||null==n.__e||h(n.__e),n.__e=n.__d=void 0;}function O(n,l,u){return this.constructor(n,u)}function S(u,i,t){var r,o,f;l.__&&l.__(u,i),o=(r="function"==typeof t)?null:t&&t.__k||i.__k,f=[],j(i,u=(!r&&t||i).__k=v(d,null,[u]),o||e,e,void 0!==i.ownerSVGElement,!r&&t?[t]:o?null:i.firstChild?n.call(i.childNodes):null,f,!r&&t?t:o?o.__e:i.firstChild,r),z(f,u);}n=c.slice,l={__e:function(n,l){for(var u,i,t;l=l.__;)if((u=l.__c)&&!u.__)try{if((i=u.constructor)&&null!=i.getDerivedStateFromError&&(u.setState(i.getDerivedStateFromError(n)),t=u.__d),null!=u.componentDidCatch&&(u.componentDidCatch(n),t=u.__d),t)return u.__E=u}catch(l){n=l;}throw n}},u=0,_.prototype.setState=function(n,l){var u;u=null!=this.__s&&this.__s!==this.state?this.__s:this.__s=a({},this.state),"function"==typeof n&&(n=n(a({},u),this.props)),n&&a(u,n),null!=n&&this.__v&&(l&&this.__h.push(l),m(this));},_.prototype.forceUpdate=function(n){this.__v&&(this.__e=!0,n&&this.__h.push(n),m(this));},_.prototype.render=d,t=[],r="function"==typeof Promise?Promise.prototype.then.bind(Promise.resolve()):setTimeout,g.__r=0;

  var HIGHLIGHT_PRE_TAG = '__aa-highlight__';
  var HIGHLIGHT_POST_TAG = '__/aa-highlight__';

  /**
   * Creates a data structure that allows to concatenate similar highlighting
   * parts in a single value.
   */

  function createAttributeSet() {
    var initialValue = arguments.length > 0 && arguments[0] !== undefined ? arguments[0] : [];
    var value = initialValue;
    return {
      get: function get() {
        return value;
      },
      add: function add(part) {
        var lastPart = value[value.length - 1];

        if ((lastPart === null || lastPart === void 0 ? void 0 : lastPart.isHighlighted) === part.isHighlighted) {
          value[value.length - 1] = {
            value: lastPart.value + part.value,
            isHighlighted: lastPart.isHighlighted
          };
        } else {
          value.push(part);
        }
      }
    };
  }

  function parseAttribute(_ref) {
    var highlightedValue = _ref.highlightedValue;
    var preTagParts = highlightedValue.split(HIGHLIGHT_PRE_TAG);
    var firstValue = preTagParts.shift();
    var parts = createAttributeSet(firstValue ? [{
      value: firstValue,
      isHighlighted: false
    }] : []);
    preTagParts.forEach(function (part) {
      var postTagParts = part.split(HIGHLIGHT_POST_TAG);
      parts.add({
        value: postTagParts[0],
        isHighlighted: true
      });

      if (postTagParts[1] !== '') {
        parts.add({
          value: postTagParts[1],
          isHighlighted: false
        });
      }
    });
    return parts.get();
  }

  function _toConsumableArray$2(arr) {
    return _arrayWithoutHoles$2(arr) || _iterableToArray$2(arr) || _unsupportedIterableToArray$2(arr) || _nonIterableSpread$2();
  }

  function _nonIterableSpread$2() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _unsupportedIterableToArray$2(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray$2(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray$2(o, minLen);
  }

  function _iterableToArray$2(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
  }

  function _arrayWithoutHoles$2(arr) {
    if (Array.isArray(arr)) return _arrayLikeToArray$2(arr);
  }

  function _arrayLikeToArray$2(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) {
      arr2[i] = arr[i];
    }

    return arr2;
  }
  function parseAlgoliaHitHighlight(_ref) {
    var hit = _ref.hit,
        attribute = _ref.attribute;
    var path = Array.isArray(attribute) ? attribute : [attribute];
    var highlightedValue = getAttributeValueByPath(hit, ['_highlightResult'].concat(_toConsumableArray$2(path), ['value']));

    if (typeof highlightedValue !== 'string') {
      "development" !== 'production' ? warn(false, "The attribute \"".concat(path.join('.'), "\" described by the path ").concat(JSON.stringify(path), " does not exist on the hit. Did you set it in `attributesToHighlight`?") + '\nSee https://www.algolia.com/doc/api-reference/api-parameters/attributesToHighlight/') : void 0 ;
      highlightedValue = getAttributeValueByPath(hit, path) || '';
    }

    return parseAttribute({
      highlightedValue: highlightedValue
    });
  }

  var htmlEscapes = {
    '&amp;': '&',
    '&lt;': '<',
    '&gt;': '>',
    '&quot;': '"',
    '&#39;': "'"
  };
  var hasAlphanumeric = new RegExp(/\w/i);
  var regexEscapedHtml = /&(amp|quot|lt|gt|#39);/g;
  var regexHasEscapedHtml = RegExp(regexEscapedHtml.source);

  function unescape(value) {
    return value && regexHasEscapedHtml.test(value) ? value.replace(regexEscapedHtml, function (character) {
      return htmlEscapes[character];
    }) : value;
  }

  function isPartHighlighted(parts, i) {
    var _parts, _parts2;

    var current = parts[i];
    var isNextHighlighted = ((_parts = parts[i + 1]) === null || _parts === void 0 ? void 0 : _parts.isHighlighted) || true;
    var isPreviousHighlighted = ((_parts2 = parts[i - 1]) === null || _parts2 === void 0 ? void 0 : _parts2.isHighlighted) || true;

    if (!hasAlphanumeric.test(unescape(current.value)) && isPreviousHighlighted === isNextHighlighted) {
      return isPreviousHighlighted;
    }

    return current.isHighlighted;
  }

  function ownKeys$2(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$2(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$2(Object(source), !0).forEach(function (key) {
        _defineProperty$2(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$2(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$2(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }
  function reverseHighlightedParts(parts) {
    // We don't want to highlight the whole word when no parts match.
    if (!parts.some(function (part) {
      return part.isHighlighted;
    })) {
      return parts.map(function (part) {
        return _objectSpread$2(_objectSpread$2({}, part), {}, {
          isHighlighted: false
        });
      });
    }

    return parts.map(function (part, i) {
      return _objectSpread$2(_objectSpread$2({}, part), {}, {
        isHighlighted: !isPartHighlighted(parts, i)
      });
    });
  }

  function parseAlgoliaHitReverseHighlight(props) {
    return reverseHighlightedParts(parseAlgoliaHitHighlight(props));
  }

  function _toConsumableArray$1(arr) {
    return _arrayWithoutHoles$1(arr) || _iterableToArray$1(arr) || _unsupportedIterableToArray$1(arr) || _nonIterableSpread$1();
  }

  function _nonIterableSpread$1() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _unsupportedIterableToArray$1(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray$1(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray$1(o, minLen);
  }

  function _iterableToArray$1(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
  }

  function _arrayWithoutHoles$1(arr) {
    if (Array.isArray(arr)) return _arrayLikeToArray$1(arr);
  }

  function _arrayLikeToArray$1(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) {
      arr2[i] = arr[i];
    }

    return arr2;
  }
  function parseAlgoliaHitSnippet(_ref) {
    var hit = _ref.hit,
        attribute = _ref.attribute;
    var path = Array.isArray(attribute) ? attribute : [attribute];
    var highlightedValue = getAttributeValueByPath(hit, ['_snippetResult'].concat(_toConsumableArray$1(path), ['value']));

    if (typeof highlightedValue !== 'string') {
      "development" !== 'production' ? warn(false, "The attribute \"".concat(path.join('.'), "\" described by the path ").concat(JSON.stringify(path), " does not exist on the hit. Did you set it in `attributesToSnippet`?") + '\nSee https://www.algolia.com/doc/api-reference/api-parameters/attributesToSnippet/') : void 0 ;
      highlightedValue = getAttributeValueByPath(hit, path) || '';
    }

    return parseAttribute({
      highlightedValue: highlightedValue
    });
  }

  function parseAlgoliaHitReverseSnippet(props) {
    return reverseHighlightedParts(parseAlgoliaHitSnippet(props));
  }

  function ownKeys$1(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread$1(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys$1(Object(source), !0).forEach(function (key) {
        _defineProperty$1(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys$1(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty$1(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function createRequester(fetcher, requesterId) {
    function execute(fetcherParams) {
      return fetcher({
        searchClient: fetcherParams.searchClient,
        queries: fetcherParams.requests.map(function (x) {
          return x.query;
        })
      }).then(function (responses) {
        return responses.map(function (response, index) {
          var _fetcherParams$reques = fetcherParams.requests[index],
              sourceId = _fetcherParams$reques.sourceId,
              transformResponse = _fetcherParams$reques.transformResponse;
          return {
            items: response,
            sourceId: sourceId,
            transformResponse: transformResponse
          };
        });
      });
    }

    return function createSpecifiedRequester(requesterParams) {
      return function requester(requestParams) {
        return _objectSpread$1(_objectSpread$1({
          requesterId: requesterId,
          execute: execute
        }, requesterParams), requestParams);
      };
    };
  }

  var _excluded$2 = ["params"];

  function ownKeys(object, enumerableOnly) {
    var keys = Object.keys(object);

    if (Object.getOwnPropertySymbols) {
      var symbols = Object.getOwnPropertySymbols(object);
      enumerableOnly && (symbols = symbols.filter(function (sym) {
        return Object.getOwnPropertyDescriptor(object, sym).enumerable;
      })), keys.push.apply(keys, symbols);
    }

    return keys;
  }

  function _objectSpread(target) {
    for (var i = 1; i < arguments.length; i++) {
      var source = null != arguments[i] ? arguments[i] : {};
      i % 2 ? ownKeys(Object(source), !0).forEach(function (key) {
        _defineProperty(target, key, source[key]);
      }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(target, Object.getOwnPropertyDescriptors(source)) : ownKeys(Object(source)).forEach(function (key) {
        Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
      });
    }

    return target;
  }

  function _defineProperty(obj, key, value) {
    if (key in obj) {
      Object.defineProperty(obj, key, {
        value: value,
        enumerable: true,
        configurable: true,
        writable: true
      });
    } else {
      obj[key] = value;
    }

    return obj;
  }

  function _objectWithoutProperties(source, excluded) {
    if (source == null) return {};

    var target = _objectWithoutPropertiesLoose(source, excluded);

    var key, i;

    if (Object.getOwnPropertySymbols) {
      var sourceSymbolKeys = Object.getOwnPropertySymbols(source);

      for (i = 0; i < sourceSymbolKeys.length; i++) {
        key = sourceSymbolKeys[i];
        if (excluded.indexOf(key) >= 0) continue;
        if (!Object.prototype.propertyIsEnumerable.call(source, key)) continue;
        target[key] = source[key];
      }
    }

    return target;
  }

  function _objectWithoutPropertiesLoose(source, excluded) {
    if (source == null) return {};
    var target = {};
    var sourceKeys = Object.keys(source);
    var key, i;

    for (i = 0; i < sourceKeys.length; i++) {
      key = sourceKeys[i];
      if (excluded.indexOf(key) >= 0) continue;
      target[key] = source[key];
    }

    return target;
  }

  function _toConsumableArray(arr) {
    return _arrayWithoutHoles(arr) || _iterableToArray(arr) || _unsupportedIterableToArray(arr) || _nonIterableSpread();
  }

  function _nonIterableSpread() {
    throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
  }

  function _unsupportedIterableToArray(o, minLen) {
    if (!o) return;
    if (typeof o === "string") return _arrayLikeToArray(o, minLen);
    var n = Object.prototype.toString.call(o).slice(8, -1);
    if (n === "Object" && o.constructor) n = o.constructor.name;
    if (n === "Map" || n === "Set") return Array.from(o);
    if (n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return _arrayLikeToArray(o, minLen);
  }

  function _iterableToArray(iter) {
    if (typeof Symbol !== "undefined" && iter[Symbol.iterator] != null || iter["@@iterator"] != null) return Array.from(iter);
  }

  function _arrayWithoutHoles(arr) {
    if (Array.isArray(arr)) return _arrayLikeToArray(arr);
  }

  function _arrayLikeToArray(arr, len) {
    if (len == null || len > arr.length) len = arr.length;

    for (var i = 0, arr2 = new Array(len); i < len; i++) {
      arr2[i] = arr[i];
    }

    return arr2;
  }
  function fetchAlgoliaResults(_ref) {
    var searchClient = _ref.searchClient,
        queries = _ref.queries,
        _ref$userAgents = _ref.userAgents,
        userAgents = _ref$userAgents === void 0 ? [] : _ref$userAgents;

    if (typeof searchClient.addAlgoliaAgent === 'function') {
      var algoliaAgents = [].concat(_toConsumableArray(userAgents$1), _toConsumableArray(userAgents));
      algoliaAgents.forEach(function (_ref2) {
        var segment = _ref2.segment,
            version = _ref2.version;
        searchClient.addAlgoliaAgent(segment, version);
      });
    }

    return searchClient.search(queries.map(function (searchParameters) {
      var params = searchParameters.params,
          headers = _objectWithoutProperties(searchParameters, _excluded$2);

      return _objectSpread(_objectSpread({}, headers), {}, {
        params: _objectSpread({
          hitsPerPage: 5,
          highlightPreTag: HIGHLIGHT_PRE_TAG,
          highlightPostTag: HIGHLIGHT_POST_TAG
        }, params)
      });
    })).then(function (response) {
      return response.results;
    });
  }

  function createHighlightComponent(_ref) {
    var createElement = _ref.createElement,
        Fragment = _ref.Fragment;

    function Highlight(_ref2) {
      var hit = _ref2.hit,
          attribute = _ref2.attribute,
          _ref2$tagName = _ref2.tagName,
          tagName = _ref2$tagName === void 0 ? 'mark' : _ref2$tagName;
      return createElement(Fragment, {}, parseAlgoliaHitHighlight({
        hit: hit,
        attribute: attribute
      }).map(function (x, index) {
        return x.isHighlighted ? createElement(tagName, {
          key: index
        }, x.value) : x.value;
      }));
    }

    Highlight.__autocomplete_componentName = 'Highlight';
    return Highlight;
  }

  function createReverseHighlightComponent(_ref) {
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

  function createReverseSnippetComponent(_ref) {
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

  function createSnippetComponent(_ref) {
    var createElement = _ref.createElement,
        Fragment = _ref.Fragment;

    function Snippet(_ref2) {
      var hit = _ref2.hit,
          attribute = _ref2.attribute,
          _ref2$tagName = _ref2.tagName,
          tagName = _ref2$tagName === void 0 ? 'mark' : _ref2$tagName;
      return createElement(Fragment, {}, parseAlgoliaHitSnippet({
        hit: hit,
        attribute: attribute
      }).map(function (x, index) {
        return x.isHighlighted ? createElement(tagName, {
          key: index
        }, x.value) : x.value;
      }));
    }

    Snippet.__autocomplete_componentName = 'Snippet';
    return Snippet;
  }

  var _excluded$1 = ["classNames", "container", "getEnvironmentProps", "getFormProps", "getInputProps", "getItemProps", "getLabelProps", "getListProps", "getPanelProps", "getRootProps", "panelContainer", "panelPlacement", "render", "renderNoResults", "renderer", "detachedMediaQuery", "components", "translations"];
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
    createElement: v,
    Fragment: d,
    render: S
  };
  function getDefaultOptions(options) {
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
        core = _objectWithoutProperties$4(options, _excluded$1);
    /* eslint-disable no-restricted-globals */


    var environment = typeof window !== 'undefined' ? window : {};
    /* eslint-enable no-restricted-globals */

    var containerElement = getHTMLElement(environment, container);
    invariant(containerElement.tagName !== 'INPUT', 'The `container` option does not support `input` elements. You need to change the container to a `div`.');
    "development" !== 'production' ? warn(!(render && renderer && !(renderer !== null && renderer !== void 0 && renderer.render)), "You provided the `render` option but did not provide a `renderer.render`. Since v1.6.0, you can provide a `render` function directly in `renderer`." + "\nTo get rid of this warning, do any of the following depending on your use case." + "\n- If you are using the `render` option only to override Autocomplete's default `render` function, pass the `render` function into `renderer` and remove the `render` option." + '\n- If you are using the `render` option to customize the layout, pass your `render` function into `renderer` and use it from the provided parameters of the `render` option.' + '\n- If you are using the `render` option to work with React 18, pass an empty `render` function into `renderer`.' + '\nSee https://www.algolia.com/doc/ui-libraries/autocomplete/api-reference/autocomplete-js/autocomplete/#param-render') : void 0;
    "development" !== 'production' ? warn(!renderer || render || renderer.Fragment && renderer.createElement && renderer.render, "You provided an incomplete `renderer` (missing: ".concat([!(renderer !== null && renderer !== void 0 && renderer.createElement) && '`renderer.createElement`', !(renderer !== null && renderer !== void 0 && renderer.Fragment) && '`renderer.Fragment`', !(renderer !== null && renderer !== void 0 && renderer.render) && '`renderer.render`'].filter(Boolean).join(', '), "). This can cause rendering issues.") + '\nSee https://www.algolia.com/doc/ui-libraries/autocomplete/api-reference/autocomplete-js/autocomplete/#param-renderer') : void 0;

    var defaultedRenderer = _objectSpread2(_objectSpread2({}, defaultRenderer), renderer);

    var defaultComponents = {
      Highlight: createHighlightComponent(defaultedRenderer),
      ReverseHighlight: createReverseHighlightComponent(defaultedRenderer),
      ReverseSnippet: createReverseSnippetComponent(defaultedRenderer),
      Snippet: createSnippetComponent(defaultedRenderer)
    };
    var defaultTranslations = {
      clearButtonTitle: 'Clear',
      detachedCancelButtonText: 'Cancel',
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
        components: _objectSpread2(_objectSpread2({}, defaultComponents), components),
        translations: _objectSpread2(_objectSpread2({}, defaultTranslations), translations)
      },
      core: _objectSpread2(_objectSpread2({}, core), {}, {
        id: (_core$id = core.id) !== null && _core$id !== void 0 ? _core$id : generateAutocompleteId(),
        environment: environment
      })
    };
  }

  function getPanelPlacementStyle(_ref) {
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

  function renderSearchBox(_ref) {
    var autocomplete = _ref.autocomplete,
        autocompleteScopeApi = _ref.autocompleteScopeApi,
        dom = _ref.dom,
        propGetters = _ref.propGetters,
        state = _ref.state;
    setPropertiesWithoutEvents(dom.root, propGetters.getRootProps(_objectSpread2({
      state: state,
      props: autocomplete.getRootProps({})
    }, autocompleteScopeApi)));
    setPropertiesWithoutEvents(dom.input, propGetters.getInputProps(_objectSpread2({
      state: state,
      props: autocomplete.getInputProps({
        inputElement: dom.input
      }),
      inputElement: dom.input
    }, autocompleteScopeApi)));
    setProperties(dom.label, {
      hidden: state.status === 'stalled'
    });
    setProperties(dom.loadingIndicator, {
      hidden: state.status !== 'stalled'
    });
    setProperties(dom.clearButton, {
      hidden: !state.query
    });
    setProperties(dom.detachedSearchButtonQuery, {
      textContent: state.query
    });
    setProperties(dom.detachedSearchButtonPlaceholder, {
      hidden: Boolean(state.query)
    });
  }
  function renderPanel(render, _ref2) {
    var autocomplete = _ref2.autocomplete,
        autocompleteScopeApi = _ref2.autocompleteScopeApi,
        classNames = _ref2.classNames,
        html = _ref2.html,
        dom = _ref2.dom,
        panelContainer = _ref2.panelContainer,
        propGetters = _ref2.propGetters,
        state = _ref2.state,
        components = _ref2.components,
        renderer = _ref2.renderer;

    if (!state.isOpen) {
      if (panelContainer.contains(dom.panel)) {
        panelContainer.removeChild(dom.panel);
      }

      return;
    } // We add the panel element to the DOM when it's not yet appended and that the
    // items are fetched.


    if (!panelContainer.contains(dom.panel) && state.status !== 'loading') {
      panelContainer.appendChild(dom.panel);
    }

    dom.panel.classList.toggle('aa-Panel--stalled', state.status === 'stalled');
    var sections = state.collections.filter(function (_ref3) {
      var source = _ref3.source,
          items = _ref3.items;
      return source.templates.noResults || items.length > 0;
    }).map(function (_ref4, sourceIndex) {
      var source = _ref4.source,
          items = _ref4.items;
      return renderer.createElement("section", {
        key: sourceIndex,
        className: classNames.source,
        "data-autocomplete-source-id": source.sourceId
      }, source.templates.header && renderer.createElement("div", {
        className: classNames.sourceHeader
      }, source.templates.header({
        components: components,
        createElement: renderer.createElement,
        Fragment: renderer.Fragment,
        items: items,
        source: source,
        state: state,
        html: html
      })), source.templates.noResults && items.length === 0 ? renderer.createElement("div", {
        className: classNames.sourceNoResults
      }, source.templates.noResults({
        components: components,
        createElement: renderer.createElement,
        Fragment: renderer.Fragment,
        source: source,
        state: state,
        html: html
      })) : renderer.createElement("ul", _extends({
        className: classNames.list
      }, propGetters.getListProps(_objectSpread2({
        state: state,
        props: autocomplete.getListProps({
          sourceIndex: sourceIndex
        })
      }, autocompleteScopeApi))), items.map(function (item) {
        var itemProps = autocomplete.getItemProps({
          item: item,
          source: source,
          sourceIndex: sourceIndex
        });
        return renderer.createElement("li", _extends({
          key: itemProps.id,
          className: classNames.item
        }, propGetters.getItemProps(_objectSpread2({
          state: state,
          props: itemProps
        }, autocompleteScopeApi))), source.templates.item({
          components: components,
          createElement: renderer.createElement,
          Fragment: renderer.Fragment,
          item: item,
          state: state,
          html: html
        }));
      })), source.templates.footer && renderer.createElement("div", {
        className: classNames.sourceFooter
      }, source.templates.footer({
        components: components,
        createElement: renderer.createElement,
        Fragment: renderer.Fragment,
        items: items,
        source: source,
        state: state,
        html: html
      })));
    });
    var children = renderer.createElement(renderer.Fragment, null, renderer.createElement("div", {
      className: classNames.panelLayout
    }, sections), renderer.createElement("div", {
      className: "aa-GradientBottom"
    }));
    var elements = sections.reduce(function (acc, current) {
      acc[current.props['data-autocomplete-source-id']] = current;
      return acc;
    }, {});
    render(_objectSpread2(_objectSpread2({
      children: children,
      state: state,
      sections: sections,
      elements: elements
    }, renderer), {}, {
      components: components,
      html: html
    }, autocompleteScopeApi), dom.panel);
  }

  var userAgents = [{
    segment: 'autocomplete-js',
    version: version
  }];

  var _excluded = ["components"];
  function autocomplete(options) {
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
      return createAutocomplete(_objectSpread2(_objectSpread2({}, props.value.core), {}, {
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
    var lastStateRef = createRef(_objectSpread2({
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
          return _objectSpread2(_objectSpread2({}, acc), {}, _defineProperty$g({}, key, undefined));
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
        } // The outer DOM might have changed since the last time the panel was
        // positioned. The layout might have shifted vertically for instance.
        // It's therefore safer to re-calculate the panel position before opening
        // it again.


        if (!isDetached.value && state.isOpen && !prevState.isOpen) {
          setPanelPosition();
        } // We scroll to the top of the panel whenever the query changes (i.e. new
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
      toggleModalClassname(isModalDetachedMql.matches); // Prior to Safari 14, `MediaQueryList` isn't based on `EventTarget`,
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
      cleanupEffects();
    }

    function update() {
      var updatedOptions = arguments.length > 0 && arguments[0] !== undefined ? arguments[0] : {};
      cleanupEffects();

      var _props$value$renderer = props.value.renderer,
          components = _props$value$renderer.components,
          rendererProps = _objectWithoutProperties$4(_props$value$renderer, _excluded);

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
      requestAnimationFrame(function () {
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
      });
    }

    return _objectSpread2(_objectSpread2({}, autocompleteScopeApi), {}, {
      update: update,
      destroy: destroy
    });
  }

  var createAlgoliaRequester = createRequester(function (params) {
    return fetchAlgoliaResults(_objectSpread2(_objectSpread2({}, params), {}, {
      userAgents: userAgents
    }));
  }, 'algolia');

  /**
   * Retrieves Algolia facet hits from multiple indices.
   */

  function getAlgoliaFacets(requestParams) {
    var requester = createAlgoliaRequester({
      transformResponse: function transformResponse(response) {
        return response.facetHits;
      }
    });
    var queries = requestParams.queries.map(function (query) {
      return _objectSpread2(_objectSpread2({}, query), {}, {
        type: 'facet'
      });
    });
    return requester(_objectSpread2(_objectSpread2({}, requestParams), {}, {
      queries: queries
    }));
  }

  /**
   * Retrieves Algolia results from multiple indices.
   */

  var getAlgoliaResults = createAlgoliaRequester({
    transformResponse: function transformResponse(response) {
      return response.hits;
    }
  });

  exports.autocomplete = autocomplete;
  exports.getAlgoliaFacets = getAlgoliaFacets;
  exports.getAlgoliaResults = getAlgoliaResults;

  Object.defineProperty(exports, '__esModule', { value: true });

}));
//# sourceMappingURL=index.development.js.map
