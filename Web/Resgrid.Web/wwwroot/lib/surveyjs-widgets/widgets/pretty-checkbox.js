(function webpackUniversalModuleDefinition(root, factory) {
	if(typeof exports === 'object' && typeof module === 'object')
		module.exports = factory();
	else if(typeof define === 'function' && define.amd)
		define("widgets/pretty-checkbox", [], factory);
	else if(typeof exports === 'object')
		exports["widgets/pretty-checkbox"] = factory();
	else
		root["widgets/pretty-checkbox"] = factory();
})(typeof self !== 'undefined' ? self : this, function() {
return /******/ (function(modules) { // webpackBootstrap
/******/ 	// The module cache
/******/ 	var installedModules = {};
/******/
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/
/******/ 		// Check if module is in cache
/******/ 		if(installedModules[moduleId]) {
/******/ 			return installedModules[moduleId].exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = installedModules[moduleId] = {
/******/ 			i: moduleId,
/******/ 			l: false,
/******/ 			exports: {}
/******/ 		};
/******/
/******/ 		// Execute the module function
/******/ 		modules[moduleId].call(module.exports, module, module.exports, __webpack_require__);
/******/
/******/ 		// Flag the module as loaded
/******/ 		module.l = true;
/******/
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/
/******/
/******/ 	// expose the modules object (__webpack_modules__)
/******/ 	__webpack_require__.m = modules;
/******/
/******/ 	// expose the module cache
/******/ 	__webpack_require__.c = installedModules;
/******/
/******/ 	// define getter function for harmony exports
/******/ 	__webpack_require__.d = function(exports, name, getter) {
/******/ 		if(!__webpack_require__.o(exports, name)) {
/******/ 			Object.defineProperty(exports, name, {
/******/ 				configurable: false,
/******/ 				enumerable: true,
/******/ 				get: getter
/******/ 			});
/******/ 		}
/******/ 	};
/******/
/******/ 	// getDefaultExport function for compatibility with non-harmony modules
/******/ 	__webpack_require__.n = function(module) {
/******/ 		var getter = module && module.__esModule ?
/******/ 			function getDefault() { return module['default']; } :
/******/ 			function getModuleExports() { return module; };
/******/ 		__webpack_require__.d(getter, 'a', getter);
/******/ 		return getter;
/******/ 	};
/******/
/******/ 	// Object.prototype.hasOwnProperty.call
/******/ 	__webpack_require__.o = function(object, property) { return Object.prototype.hasOwnProperty.call(object, property); };
/******/
/******/ 	// __webpack_public_path__
/******/ 	__webpack_require__.p = "";
/******/
/******/ 	// Load entry module and return exports
/******/ 	return __webpack_require__(__webpack_require__.s = 13);
/******/ })
/************************************************************************/
/******/ ({

/***/ 13:
/***/ (function(module, __webpack_exports__, __webpack_require__) {

"use strict";
Object.defineProperty(__webpack_exports__, "__esModule", { value: true });
function init(Survey) {
  var widget = {
    settings: {
      supportedTypes: ["radiogroup", "checkbox", "boolean"],
      radiogroup: {
        rootClass: "pretty p-default p-round",
        inputType: "radio",
        states: [{ stateClass: "state p-success", addOn: "" }],
      },
      checkbox: {
        rootClass: "pretty p-default",
        inputType: "checkbox",
        states: [{ stateClass: "state p-success", addOn: "" }],
      },
      boolean: {
        rootClass: "pretty p-icon p-default p-has-indeterminate",
        inputType: "checkbox",
        states: [
          { stateClass: "state p-success", addOn: "" },
          {
            stateClass: "state p-success p-is-indeterminate",
            iconClass: "icon mdi mdi-minus",
            addOn: "",
          },
        ],
      },
    },
    name: "pretty-checkbox",
    activatedBy: "property",
    widgetIsLoaded: function () {
      return true;
    },
    htmlTemplate: "<fieldset></fieldset>",
    isFit: function (question) {
      var isFitByType =
        widget.settings.supportedTypes.indexOf(question.getType()) !== -1;

      if (widget.activatedBy === "property") {
        return question["renderAs"] === "prettycheckbox" && isFitByType;
      } else if (widget.activatedBy === "type") {
        return isFitByType;
      }

      return false;
    },
    activatedByChanged: function (value) {
      if (this.widgetIsLoaded()) {
        widget.activatedBy = value;
        widget.settings.supportedTypes.forEach(function (supportedType) {
          Survey.JsonObject.metaData.removeProperty(supportedType, "renderAs");

          if (value === "property") {
            Survey.JsonObject.metaData.addProperty(supportedType, {
              name: "renderAs",
              category: "general",
              default: "default",
              choices: ["default", "prettycheckbox"],
            });
          }
        });
      }
    },
    isDefaultRender: false,
    afterRender: function (question, element) {
      var itemInputs = {};
      var questionType = question.getType();
      var options = this.settings[questionType];
      var checkboxType = questionType === "checkbox";
      var radiogroupType = questionType === "radiogroup";
      var booleanType = questionType === "boolean";

      var inChangeHandler = false;
      var changeHandler = function (event) {
        inChangeHandler = true;
        try {
          var target = arguments[0].target;
          var targetValue = target.value;
          var targetChecked = target.checked;

          if (checkboxType) {
            var questionValue = question.createValueCopy() || [];
            var valueIndex = questionValue.indexOf(targetValue);
            if (targetChecked) {
              if (valueIndex === -1) {
                questionValue.push(targetValue);
              }
            } else {
              if (valueIndex !== -1) {
                questionValue.splice(valueIndex, 1);
              }
            }

            question.value = questionValue;
          } else if (radiogroupType) {
            question.value = targetValue;
          } else {
            question.value = targetChecked;
          }
        } finally {
          inChangeHandler = false;
        }
      };

      var itemWidth =
        question.colCount > 0 ? 100 / question.colCount + "%" : "";

      var choices = booleanType
        ? [{ locText: question.locTitle, value: !!question.value }]
        : question.visibleChoices;
      choices.forEach(function (choiceItem, index) {
        var input = document.createElement("input");
        input.type = options.inputType;
        input.name = question.name + (checkboxType ? "" + index : "");
        input.onchange = changeHandler;
        input.value = choiceItem.value;

        if (booleanType && question.value === null) {
          input.indeterminate = question.defaultValue === "indeterminate";
        }

        var controlRoot = document.createElement("div");
        controlRoot.className = options.rootClass;
        controlRoot.appendChild(input);

        options.states.forEach(function (state) {
          var stateRoot = document.createElement("div");
          stateRoot.className = state.stateClass;
          if (!!state.iconClass) {
            var icon = document.createElement("i");
            icon.className = state.iconClass;
            stateRoot.appendChild(icon);
          }

          var label = document.createElement("label");
          if (choiceItem.locText.hasHtml) {
            label.innerHTML = choiceItem.locText.html;
          } else {
            label.textContent = choiceItem.locText.renderedText;
          }
          stateRoot.appendChild(label);

          controlRoot.appendChild(stateRoot);
          if (!!state.addOn) {
            stateRoot.insertAdjacentHTML("afterbegin", state.addOn);
          }
        });

        var itemRoot = document.createElement("div");
        itemRoot.className = "sv_cw_pretty_checkbox_" + questionType;
        itemRoot.style.display = "inline-block";
        itemRoot.style.width = itemWidth;
        itemRoot.appendChild(controlRoot);

        element.appendChild(itemRoot);

        itemInputs[choiceItem.value] = input;
      });

      var updateValueHandler = function (newValue) {
        if (!inChangeHandler) {
          var checkedItems = newValue || [];
          if (radiogroupType || booleanType) {
            checkedItems = [newValue && newValue.toString()];
          }

          Object.values(itemInputs).forEach(function (inputItem) {
            if (checkedItems.indexOf(inputItem.value) !== -1) {
              inputItem.setAttribute("checked", undefined);
            } else {
              inputItem.removeAttribute("checked");
            }
          });
        }
      };
      var readOnlyHandler = function () {
        Object.values(itemInputs).forEach(function (inputItem) {
          if (question.isReadOnly) {
            inputItem.setAttribute("disabled", true);
          } else {
            inputItem.removeAttribute("disabled");
          }
        });
      };

      question.valueChangedCallback = updateValueHandler;
      question.readOnlyChangedCallback = readOnlyHandler;
      updateValueHandler(question.value);
      readOnlyHandler();
    },
    willUnmount: function (question, el) {
      question.valueChangedCallback = undefined;
      question.readOnlyChangedCallback = undefined;
    },
  };

  Survey.CustomWidgetCollection.Instance.addCustomWidget(widget, "property");
}

if (typeof Survey !== "undefined") {
  init(Survey);
}

/* harmony default export */ __webpack_exports__["default"] = (init);


/***/ })

/******/ });
});
//# sourceMappingURL=data:application/json;charset=utf-8;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIndlYnBhY2s6Ly8vd2VicGFjay91bml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uIiwid2VicGFjazovLy93ZWJwYWNrL2Jvb3RzdHJhcCBmOWIyNGFiYzlmODA1NTBmYmQzZiIsIndlYnBhY2s6Ly8vLi9zcmMvcHJldHR5LWNoZWNrYm94LmpzIl0sIm5hbWVzIjpbXSwibWFwcGluZ3MiOiJBQUFBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLENBQUM7QUFDRCxPO1FDVkE7UUFDQTs7UUFFQTtRQUNBOztRQUVBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBOztRQUVBO1FBQ0E7O1FBRUE7UUFDQTs7UUFFQTtRQUNBO1FBQ0E7OztRQUdBO1FBQ0E7O1FBRUE7UUFDQTs7UUFFQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBLEtBQUs7UUFDTDtRQUNBOztRQUVBO1FBQ0E7UUFDQTtRQUNBLDJCQUEyQiwwQkFBMEIsRUFBRTtRQUN2RCxpQ0FBaUMsZUFBZTtRQUNoRDtRQUNBO1FBQ0E7O1FBRUE7UUFDQSxzREFBc0QsK0RBQStEOztRQUVySDtRQUNBOztRQUVBO1FBQ0E7Ozs7Ozs7OztBQzdEQTtBQUFBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0Esa0JBQWtCLDJDQUEyQztBQUM3RCxPQUFPO0FBQ1A7QUFDQTtBQUNBO0FBQ0Esa0JBQWtCLDJDQUEyQztBQUM3RCxPQUFPO0FBQ1A7QUFDQTtBQUNBO0FBQ0E7QUFDQSxXQUFXLDJDQUEyQztBQUN0RDtBQUNBO0FBQ0E7QUFDQTtBQUNBLFdBQVc7QUFDWDtBQUNBLE9BQU87QUFDUCxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLE9BQU87QUFDUDtBQUNBOztBQUVBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsYUFBYTtBQUNiO0FBQ0EsU0FBUztBQUNUO0FBQ0EsS0FBSztBQUNMO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxhQUFhO0FBQ2I7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQSxXQUFXO0FBQ1g7QUFDQSxXQUFXO0FBQ1g7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBLFlBQVksc0RBQXNEO0FBQ2xFO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQSxXQUFXO0FBQ1g7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsU0FBUzs7QUFFVDtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBOztBQUVBO0FBQ0EsT0FBTzs7QUFFUDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0EsYUFBYTtBQUNiO0FBQ0E7QUFDQSxXQUFXO0FBQ1g7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsV0FBVztBQUNYO0FBQ0E7QUFDQSxTQUFTO0FBQ1Q7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0EsS0FBSztBQUNMOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVlLG1FQUFJLEVBQUMiLCJmaWxlIjoid2lkZ2V0cy9wcmV0dHktY2hlY2tib3guanMiLCJzb3VyY2VzQ29udGVudCI6WyIoZnVuY3Rpb24gd2VicGFja1VuaXZlcnNhbE1vZHVsZURlZmluaXRpb24ocm9vdCwgZmFjdG9yeSkge1xuXHRpZih0eXBlb2YgZXhwb3J0cyA9PT0gJ29iamVjdCcgJiYgdHlwZW9mIG1vZHVsZSA9PT0gJ29iamVjdCcpXG5cdFx0bW9kdWxlLmV4cG9ydHMgPSBmYWN0b3J5KCk7XG5cdGVsc2UgaWYodHlwZW9mIGRlZmluZSA9PT0gJ2Z1bmN0aW9uJyAmJiBkZWZpbmUuYW1kKVxuXHRcdGRlZmluZShcIndpZGdldHMvcHJldHR5LWNoZWNrYm94XCIsIFtdLCBmYWN0b3J5KTtcblx0ZWxzZSBpZih0eXBlb2YgZXhwb3J0cyA9PT0gJ29iamVjdCcpXG5cdFx0ZXhwb3J0c1tcIndpZGdldHMvcHJldHR5LWNoZWNrYm94XCJdID0gZmFjdG9yeSgpO1xuXHRlbHNlXG5cdFx0cm9vdFtcIndpZGdldHMvcHJldHR5LWNoZWNrYm94XCJdID0gZmFjdG9yeSgpO1xufSkodHlwZW9mIHNlbGYgIT09ICd1bmRlZmluZWQnID8gc2VsZiA6IHRoaXMsIGZ1bmN0aW9uKCkge1xucmV0dXJuIFxuXG5cbi8vIFdFQlBBQ0sgRk9PVEVSIC8vXG4vLyB3ZWJwYWNrL3VuaXZlcnNhbE1vZHVsZURlZmluaXRpb24iLCIgXHQvLyBUaGUgbW9kdWxlIGNhY2hlXG4gXHR2YXIgaW5zdGFsbGVkTW9kdWxlcyA9IHt9O1xuXG4gXHQvLyBUaGUgcmVxdWlyZSBmdW5jdGlvblxuIFx0ZnVuY3Rpb24gX193ZWJwYWNrX3JlcXVpcmVfXyhtb2R1bGVJZCkge1xuXG4gXHRcdC8vIENoZWNrIGlmIG1vZHVsZSBpcyBpbiBjYWNoZVxuIFx0XHRpZihpbnN0YWxsZWRNb2R1bGVzW21vZHVsZUlkXSkge1xuIFx0XHRcdHJldHVybiBpbnN0YWxsZWRNb2R1bGVzW21vZHVsZUlkXS5leHBvcnRzO1xuIFx0XHR9XG4gXHRcdC8vIENyZWF0ZSBhIG5ldyBtb2R1bGUgKGFuZCBwdXQgaXQgaW50byB0aGUgY2FjaGUpXG4gXHRcdHZhciBtb2R1bGUgPSBpbnN0YWxsZWRNb2R1bGVzW21vZHVsZUlkXSA9IHtcbiBcdFx0XHRpOiBtb2R1bGVJZCxcbiBcdFx0XHRsOiBmYWxzZSxcbiBcdFx0XHRleHBvcnRzOiB7fVxuIFx0XHR9O1xuXG4gXHRcdC8vIEV4ZWN1dGUgdGhlIG1vZHVsZSBmdW5jdGlvblxuIFx0XHRtb2R1bGVzW21vZHVsZUlkXS5jYWxsKG1vZHVsZS5leHBvcnRzLCBtb2R1bGUsIG1vZHVsZS5leHBvcnRzLCBfX3dlYnBhY2tfcmVxdWlyZV9fKTtcblxuIFx0XHQvLyBGbGFnIHRoZSBtb2R1bGUgYXMgbG9hZGVkXG4gXHRcdG1vZHVsZS5sID0gdHJ1ZTtcblxuIFx0XHQvLyBSZXR1cm4gdGhlIGV4cG9ydHMgb2YgdGhlIG1vZHVsZVxuIFx0XHRyZXR1cm4gbW9kdWxlLmV4cG9ydHM7XG4gXHR9XG5cblxuIFx0Ly8gZXhwb3NlIHRoZSBtb2R1bGVzIG9iamVjdCAoX193ZWJwYWNrX21vZHVsZXNfXylcbiBcdF9fd2VicGFja19yZXF1aXJlX18ubSA9IG1vZHVsZXM7XG5cbiBcdC8vIGV4cG9zZSB0aGUgbW9kdWxlIGNhY2hlXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLmMgPSBpbnN0YWxsZWRNb2R1bGVzO1xuXG4gXHQvLyBkZWZpbmUgZ2V0dGVyIGZ1bmN0aW9uIGZvciBoYXJtb255IGV4cG9ydHNcbiBcdF9fd2VicGFja19yZXF1aXJlX18uZCA9IGZ1bmN0aW9uKGV4cG9ydHMsIG5hbWUsIGdldHRlcikge1xuIFx0XHRpZighX193ZWJwYWNrX3JlcXVpcmVfXy5vKGV4cG9ydHMsIG5hbWUpKSB7XG4gXHRcdFx0T2JqZWN0LmRlZmluZVByb3BlcnR5KGV4cG9ydHMsIG5hbWUsIHtcbiBcdFx0XHRcdGNvbmZpZ3VyYWJsZTogZmFsc2UsXG4gXHRcdFx0XHRlbnVtZXJhYmxlOiB0cnVlLFxuIFx0XHRcdFx0Z2V0OiBnZXR0ZXJcbiBcdFx0XHR9KTtcbiBcdFx0fVxuIFx0fTtcblxuIFx0Ly8gZ2V0RGVmYXVsdEV4cG9ydCBmdW5jdGlvbiBmb3IgY29tcGF0aWJpbGl0eSB3aXRoIG5vbi1oYXJtb255IG1vZHVsZXNcbiBcdF9fd2VicGFja19yZXF1aXJlX18ubiA9IGZ1bmN0aW9uKG1vZHVsZSkge1xuIFx0XHR2YXIgZ2V0dGVyID0gbW9kdWxlICYmIG1vZHVsZS5fX2VzTW9kdWxlID9cbiBcdFx0XHRmdW5jdGlvbiBnZXREZWZhdWx0KCkgeyByZXR1cm4gbW9kdWxlWydkZWZhdWx0J107IH0gOlxuIFx0XHRcdGZ1bmN0aW9uIGdldE1vZHVsZUV4cG9ydHMoKSB7IHJldHVybiBtb2R1bGU7IH07XG4gXHRcdF9fd2VicGFja19yZXF1aXJlX18uZChnZXR0ZXIsICdhJywgZ2V0dGVyKTtcbiBcdFx0cmV0dXJuIGdldHRlcjtcbiBcdH07XG5cbiBcdC8vIE9iamVjdC5wcm90b3R5cGUuaGFzT3duUHJvcGVydHkuY2FsbFxuIFx0X193ZWJwYWNrX3JlcXVpcmVfXy5vID0gZnVuY3Rpb24ob2JqZWN0LCBwcm9wZXJ0eSkgeyByZXR1cm4gT2JqZWN0LnByb3RvdHlwZS5oYXNPd25Qcm9wZXJ0eS5jYWxsKG9iamVjdCwgcHJvcGVydHkpOyB9O1xuXG4gXHQvLyBfX3dlYnBhY2tfcHVibGljX3BhdGhfX1xuIFx0X193ZWJwYWNrX3JlcXVpcmVfXy5wID0gXCJcIjtcblxuIFx0Ly8gTG9hZCBlbnRyeSBtb2R1bGUgYW5kIHJldHVybiBleHBvcnRzXG4gXHRyZXR1cm4gX193ZWJwYWNrX3JlcXVpcmVfXyhfX3dlYnBhY2tfcmVxdWlyZV9fLnMgPSAxMyk7XG5cblxuXG4vLyBXRUJQQUNLIEZPT1RFUiAvL1xuLy8gd2VicGFjay9ib290c3RyYXAgZjliMjRhYmM5ZjgwNTUwZmJkM2YiLCJmdW5jdGlvbiBpbml0KFN1cnZleSkge1xuICB2YXIgd2lkZ2V0ID0ge1xuICAgIHNldHRpbmdzOiB7XG4gICAgICBzdXBwb3J0ZWRUeXBlczogW1wicmFkaW9ncm91cFwiLCBcImNoZWNrYm94XCIsIFwiYm9vbGVhblwiXSxcbiAgICAgIHJhZGlvZ3JvdXA6IHtcbiAgICAgICAgcm9vdENsYXNzOiBcInByZXR0eSBwLWRlZmF1bHQgcC1yb3VuZFwiLFxuICAgICAgICBpbnB1dFR5cGU6IFwicmFkaW9cIixcbiAgICAgICAgc3RhdGVzOiBbeyBzdGF0ZUNsYXNzOiBcInN0YXRlIHAtc3VjY2Vzc1wiLCBhZGRPbjogXCJcIiB9XSxcbiAgICAgIH0sXG4gICAgICBjaGVja2JveDoge1xuICAgICAgICByb290Q2xhc3M6IFwicHJldHR5IHAtZGVmYXVsdFwiLFxuICAgICAgICBpbnB1dFR5cGU6IFwiY2hlY2tib3hcIixcbiAgICAgICAgc3RhdGVzOiBbeyBzdGF0ZUNsYXNzOiBcInN0YXRlIHAtc3VjY2Vzc1wiLCBhZGRPbjogXCJcIiB9XSxcbiAgICAgIH0sXG4gICAgICBib29sZWFuOiB7XG4gICAgICAgIHJvb3RDbGFzczogXCJwcmV0dHkgcC1pY29uIHAtZGVmYXVsdCBwLWhhcy1pbmRldGVybWluYXRlXCIsXG4gICAgICAgIGlucHV0VHlwZTogXCJjaGVja2JveFwiLFxuICAgICAgICBzdGF0ZXM6IFtcbiAgICAgICAgICB7IHN0YXRlQ2xhc3M6IFwic3RhdGUgcC1zdWNjZXNzXCIsIGFkZE9uOiBcIlwiIH0sXG4gICAgICAgICAge1xuICAgICAgICAgICAgc3RhdGVDbGFzczogXCJzdGF0ZSBwLXN1Y2Nlc3MgcC1pcy1pbmRldGVybWluYXRlXCIsXG4gICAgICAgICAgICBpY29uQ2xhc3M6IFwiaWNvbiBtZGkgbWRpLW1pbnVzXCIsXG4gICAgICAgICAgICBhZGRPbjogXCJcIixcbiAgICAgICAgICB9LFxuICAgICAgICBdLFxuICAgICAgfSxcbiAgICB9LFxuICAgIG5hbWU6IFwicHJldHR5LWNoZWNrYm94XCIsXG4gICAgYWN0aXZhdGVkQnk6IFwicHJvcGVydHlcIixcbiAgICB3aWRnZXRJc0xvYWRlZDogZnVuY3Rpb24gKCkge1xuICAgICAgcmV0dXJuIHRydWU7XG4gICAgfSxcbiAgICBodG1sVGVtcGxhdGU6IFwiPGZpZWxkc2V0PjwvZmllbGRzZXQ+XCIsXG4gICAgaXNGaXQ6IGZ1bmN0aW9uIChxdWVzdGlvbikge1xuICAgICAgdmFyIGlzRml0QnlUeXBlID1cbiAgICAgICAgd2lkZ2V0LnNldHRpbmdzLnN1cHBvcnRlZFR5cGVzLmluZGV4T2YocXVlc3Rpb24uZ2V0VHlwZSgpKSAhPT0gLTE7XG5cbiAgICAgIGlmICh3aWRnZXQuYWN0aXZhdGVkQnkgPT09IFwicHJvcGVydHlcIikge1xuICAgICAgICByZXR1cm4gcXVlc3Rpb25bXCJyZW5kZXJBc1wiXSA9PT0gXCJwcmV0dHljaGVja2JveFwiICYmIGlzRml0QnlUeXBlO1xuICAgICAgfSBlbHNlIGlmICh3aWRnZXQuYWN0aXZhdGVkQnkgPT09IFwidHlwZVwiKSB7XG4gICAgICAgIHJldHVybiBpc0ZpdEJ5VHlwZTtcbiAgICAgIH1cblxuICAgICAgcmV0dXJuIGZhbHNlO1xuICAgIH0sXG4gICAgYWN0aXZhdGVkQnlDaGFuZ2VkOiBmdW5jdGlvbiAodmFsdWUpIHtcbiAgICAgIGlmICh0aGlzLndpZGdldElzTG9hZGVkKCkpIHtcbiAgICAgICAgd2lkZ2V0LmFjdGl2YXRlZEJ5ID0gdmFsdWU7XG4gICAgICAgIHdpZGdldC5zZXR0aW5ncy5zdXBwb3J0ZWRUeXBlcy5mb3JFYWNoKGZ1bmN0aW9uIChzdXBwb3J0ZWRUeXBlKSB7XG4gICAgICAgICAgU3VydmV5Lkpzb25PYmplY3QubWV0YURhdGEucmVtb3ZlUHJvcGVydHkoc3VwcG9ydGVkVHlwZSwgXCJyZW5kZXJBc1wiKTtcblxuICAgICAgICAgIGlmICh2YWx1ZSA9PT0gXCJwcm9wZXJ0eVwiKSB7XG4gICAgICAgICAgICBTdXJ2ZXkuSnNvbk9iamVjdC5tZXRhRGF0YS5hZGRQcm9wZXJ0eShzdXBwb3J0ZWRUeXBlLCB7XG4gICAgICAgICAgICAgIG5hbWU6IFwicmVuZGVyQXNcIixcbiAgICAgICAgICAgICAgY2F0ZWdvcnk6IFwiZ2VuZXJhbFwiLFxuICAgICAgICAgICAgICBkZWZhdWx0OiBcImRlZmF1bHRcIixcbiAgICAgICAgICAgICAgY2hvaWNlczogW1wiZGVmYXVsdFwiLCBcInByZXR0eWNoZWNrYm94XCJdLFxuICAgICAgICAgICAgfSk7XG4gICAgICAgICAgfVxuICAgICAgICB9KTtcbiAgICAgIH1cbiAgICB9LFxuICAgIGlzRGVmYXVsdFJlbmRlcjogZmFsc2UsXG4gICAgYWZ0ZXJSZW5kZXI6IGZ1bmN0aW9uIChxdWVzdGlvbiwgZWxlbWVudCkge1xuICAgICAgdmFyIGl0ZW1JbnB1dHMgPSB7fTtcbiAgICAgIHZhciBxdWVzdGlvblR5cGUgPSBxdWVzdGlvbi5nZXRUeXBlKCk7XG4gICAgICB2YXIgb3B0aW9ucyA9IHRoaXMuc2V0dGluZ3NbcXVlc3Rpb25UeXBlXTtcbiAgICAgIHZhciBjaGVja2JveFR5cGUgPSBxdWVzdGlvblR5cGUgPT09IFwiY2hlY2tib3hcIjtcbiAgICAgIHZhciByYWRpb2dyb3VwVHlwZSA9IHF1ZXN0aW9uVHlwZSA9PT0gXCJyYWRpb2dyb3VwXCI7XG4gICAgICB2YXIgYm9vbGVhblR5cGUgPSBxdWVzdGlvblR5cGUgPT09IFwiYm9vbGVhblwiO1xuXG4gICAgICB2YXIgaW5DaGFuZ2VIYW5kbGVyID0gZmFsc2U7XG4gICAgICB2YXIgY2hhbmdlSGFuZGxlciA9IGZ1bmN0aW9uIChldmVudCkge1xuICAgICAgICBpbkNoYW5nZUhhbmRsZXIgPSB0cnVlO1xuICAgICAgICB0cnkge1xuICAgICAgICAgIHZhciB0YXJnZXQgPSBhcmd1bWVudHNbMF0udGFyZ2V0O1xuICAgICAgICAgIHZhciB0YXJnZXRWYWx1ZSA9IHRhcmdldC52YWx1ZTtcbiAgICAgICAgICB2YXIgdGFyZ2V0Q2hlY2tlZCA9IHRhcmdldC5jaGVja2VkO1xuXG4gICAgICAgICAgaWYgKGNoZWNrYm94VHlwZSkge1xuICAgICAgICAgICAgdmFyIHF1ZXN0aW9uVmFsdWUgPSBxdWVzdGlvbi5jcmVhdGVWYWx1ZUNvcHkoKSB8fCBbXTtcbiAgICAgICAgICAgIHZhciB2YWx1ZUluZGV4ID0gcXVlc3Rpb25WYWx1ZS5pbmRleE9mKHRhcmdldFZhbHVlKTtcbiAgICAgICAgICAgIGlmICh0YXJnZXRDaGVja2VkKSB7XG4gICAgICAgICAgICAgIGlmICh2YWx1ZUluZGV4ID09PSAtMSkge1xuICAgICAgICAgICAgICAgIHF1ZXN0aW9uVmFsdWUucHVzaCh0YXJnZXRWYWx1ZSk7XG4gICAgICAgICAgICAgIH1cbiAgICAgICAgICAgIH0gZWxzZSB7XG4gICAgICAgICAgICAgIGlmICh2YWx1ZUluZGV4ICE9PSAtMSkge1xuICAgICAgICAgICAgICAgIHF1ZXN0aW9uVmFsdWUuc3BsaWNlKHZhbHVlSW5kZXgsIDEpO1xuICAgICAgICAgICAgICB9XG4gICAgICAgICAgICB9XG5cbiAgICAgICAgICAgIHF1ZXN0aW9uLnZhbHVlID0gcXVlc3Rpb25WYWx1ZTtcbiAgICAgICAgICB9IGVsc2UgaWYgKHJhZGlvZ3JvdXBUeXBlKSB7XG4gICAgICAgICAgICBxdWVzdGlvbi52YWx1ZSA9IHRhcmdldFZhbHVlO1xuICAgICAgICAgIH0gZWxzZSB7XG4gICAgICAgICAgICBxdWVzdGlvbi52YWx1ZSA9IHRhcmdldENoZWNrZWQ7XG4gICAgICAgICAgfVxuICAgICAgICB9IGZpbmFsbHkge1xuICAgICAgICAgIGluQ2hhbmdlSGFuZGxlciA9IGZhbHNlO1xuICAgICAgICB9XG4gICAgICB9O1xuXG4gICAgICB2YXIgaXRlbVdpZHRoID1cbiAgICAgICAgcXVlc3Rpb24uY29sQ291bnQgPiAwID8gMTAwIC8gcXVlc3Rpb24uY29sQ291bnQgKyBcIiVcIiA6IFwiXCI7XG5cbiAgICAgIHZhciBjaG9pY2VzID0gYm9vbGVhblR5cGVcbiAgICAgICAgPyBbeyBsb2NUZXh0OiBxdWVzdGlvbi5sb2NUaXRsZSwgdmFsdWU6ICEhcXVlc3Rpb24udmFsdWUgfV1cbiAgICAgICAgOiBxdWVzdGlvbi52aXNpYmxlQ2hvaWNlcztcbiAgICAgIGNob2ljZXMuZm9yRWFjaChmdW5jdGlvbiAoY2hvaWNlSXRlbSwgaW5kZXgpIHtcbiAgICAgICAgdmFyIGlucHV0ID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudChcImlucHV0XCIpO1xuICAgICAgICBpbnB1dC50eXBlID0gb3B0aW9ucy5pbnB1dFR5cGU7XG4gICAgICAgIGlucHV0Lm5hbWUgPSBxdWVzdGlvbi5uYW1lICsgKGNoZWNrYm94VHlwZSA/IFwiXCIgKyBpbmRleCA6IFwiXCIpO1xuICAgICAgICBpbnB1dC5vbmNoYW5nZSA9IGNoYW5nZUhhbmRsZXI7XG4gICAgICAgIGlucHV0LnZhbHVlID0gY2hvaWNlSXRlbS52YWx1ZTtcblxuICAgICAgICBpZiAoYm9vbGVhblR5cGUgJiYgcXVlc3Rpb24udmFsdWUgPT09IG51bGwpIHtcbiAgICAgICAgICBpbnB1dC5pbmRldGVybWluYXRlID0gcXVlc3Rpb24uZGVmYXVsdFZhbHVlID09PSBcImluZGV0ZXJtaW5hdGVcIjtcbiAgICAgICAgfVxuXG4gICAgICAgIHZhciBjb250cm9sUm9vdCA9IGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoXCJkaXZcIik7XG4gICAgICAgIGNvbnRyb2xSb290LmNsYXNzTmFtZSA9IG9wdGlvbnMucm9vdENsYXNzO1xuICAgICAgICBjb250cm9sUm9vdC5hcHBlbmRDaGlsZChpbnB1dCk7XG5cbiAgICAgICAgb3B0aW9ucy5zdGF0ZXMuZm9yRWFjaChmdW5jdGlvbiAoc3RhdGUpIHtcbiAgICAgICAgICB2YXIgc3RhdGVSb290ID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudChcImRpdlwiKTtcbiAgICAgICAgICBzdGF0ZVJvb3QuY2xhc3NOYW1lID0gc3RhdGUuc3RhdGVDbGFzcztcbiAgICAgICAgICBpZiAoISFzdGF0ZS5pY29uQ2xhc3MpIHtcbiAgICAgICAgICAgIHZhciBpY29uID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudChcImlcIik7XG4gICAgICAgICAgICBpY29uLmNsYXNzTmFtZSA9IHN0YXRlLmljb25DbGFzcztcbiAgICAgICAgICAgIHN0YXRlUm9vdC5hcHBlbmRDaGlsZChpY29uKTtcbiAgICAgICAgICB9XG5cbiAgICAgICAgICB2YXIgbGFiZWwgPSBkb2N1bWVudC5jcmVhdGVFbGVtZW50KFwibGFiZWxcIik7XG4gICAgICAgICAgaWYgKGNob2ljZUl0ZW0ubG9jVGV4dC5oYXNIdG1sKSB7XG4gICAgICAgICAgICBsYWJlbC5pbm5lckhUTUwgPSBjaG9pY2VJdGVtLmxvY1RleHQuaHRtbDtcbiAgICAgICAgICB9IGVsc2Uge1xuICAgICAgICAgICAgbGFiZWwudGV4dENvbnRlbnQgPSBjaG9pY2VJdGVtLmxvY1RleHQucmVuZGVyZWRUZXh0O1xuICAgICAgICAgIH1cbiAgICAgICAgICBzdGF0ZVJvb3QuYXBwZW5kQ2hpbGQobGFiZWwpO1xuXG4gICAgICAgICAgY29udHJvbFJvb3QuYXBwZW5kQ2hpbGQoc3RhdGVSb290KTtcbiAgICAgICAgICBpZiAoISFzdGF0ZS5hZGRPbikge1xuICAgICAgICAgICAgc3RhdGVSb290Lmluc2VydEFkamFjZW50SFRNTChcImFmdGVyYmVnaW5cIiwgc3RhdGUuYWRkT24pO1xuICAgICAgICAgIH1cbiAgICAgICAgfSk7XG5cbiAgICAgICAgdmFyIGl0ZW1Sb290ID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudChcImRpdlwiKTtcbiAgICAgICAgaXRlbVJvb3QuY2xhc3NOYW1lID0gXCJzdl9jd19wcmV0dHlfY2hlY2tib3hfXCIgKyBxdWVzdGlvblR5cGU7XG4gICAgICAgIGl0ZW1Sb290LnN0eWxlLmRpc3BsYXkgPSBcImlubGluZS1ibG9ja1wiO1xuICAgICAgICBpdGVtUm9vdC5zdHlsZS53aWR0aCA9IGl0ZW1XaWR0aDtcbiAgICAgICAgaXRlbVJvb3QuYXBwZW5kQ2hpbGQoY29udHJvbFJvb3QpO1xuXG4gICAgICAgIGVsZW1lbnQuYXBwZW5kQ2hpbGQoaXRlbVJvb3QpO1xuXG4gICAgICAgIGl0ZW1JbnB1dHNbY2hvaWNlSXRlbS52YWx1ZV0gPSBpbnB1dDtcbiAgICAgIH0pO1xuXG4gICAgICB2YXIgdXBkYXRlVmFsdWVIYW5kbGVyID0gZnVuY3Rpb24gKG5ld1ZhbHVlKSB7XG4gICAgICAgIGlmICghaW5DaGFuZ2VIYW5kbGVyKSB7XG4gICAgICAgICAgdmFyIGNoZWNrZWRJdGVtcyA9IG5ld1ZhbHVlIHx8IFtdO1xuICAgICAgICAgIGlmIChyYWRpb2dyb3VwVHlwZSB8fCBib29sZWFuVHlwZSkge1xuICAgICAgICAgICAgY2hlY2tlZEl0ZW1zID0gW25ld1ZhbHVlICYmIG5ld1ZhbHVlLnRvU3RyaW5nKCldO1xuICAgICAgICAgIH1cblxuICAgICAgICAgIE9iamVjdC52YWx1ZXMoaXRlbUlucHV0cykuZm9yRWFjaChmdW5jdGlvbiAoaW5wdXRJdGVtKSB7XG4gICAgICAgICAgICBpZiAoY2hlY2tlZEl0ZW1zLmluZGV4T2YoaW5wdXRJdGVtLnZhbHVlKSAhPT0gLTEpIHtcbiAgICAgICAgICAgICAgaW5wdXRJdGVtLnNldEF0dHJpYnV0ZShcImNoZWNrZWRcIiwgdW5kZWZpbmVkKTtcbiAgICAgICAgICAgIH0gZWxzZSB7XG4gICAgICAgICAgICAgIGlucHV0SXRlbS5yZW1vdmVBdHRyaWJ1dGUoXCJjaGVja2VkXCIpO1xuICAgICAgICAgICAgfVxuICAgICAgICAgIH0pO1xuICAgICAgICB9XG4gICAgICB9O1xuICAgICAgdmFyIHJlYWRPbmx5SGFuZGxlciA9IGZ1bmN0aW9uICgpIHtcbiAgICAgICAgT2JqZWN0LnZhbHVlcyhpdGVtSW5wdXRzKS5mb3JFYWNoKGZ1bmN0aW9uIChpbnB1dEl0ZW0pIHtcbiAgICAgICAgICBpZiAocXVlc3Rpb24uaXNSZWFkT25seSkge1xuICAgICAgICAgICAgaW5wdXRJdGVtLnNldEF0dHJpYnV0ZShcImRpc2FibGVkXCIsIHRydWUpO1xuICAgICAgICAgIH0gZWxzZSB7XG4gICAgICAgICAgICBpbnB1dEl0ZW0ucmVtb3ZlQXR0cmlidXRlKFwiZGlzYWJsZWRcIik7XG4gICAgICAgICAgfVxuICAgICAgICB9KTtcbiAgICAgIH07XG5cbiAgICAgIHF1ZXN0aW9uLnZhbHVlQ2hhbmdlZENhbGxiYWNrID0gdXBkYXRlVmFsdWVIYW5kbGVyO1xuICAgICAgcXVlc3Rpb24ucmVhZE9ubHlDaGFuZ2VkQ2FsbGJhY2sgPSByZWFkT25seUhhbmRsZXI7XG4gICAgICB1cGRhdGVWYWx1ZUhhbmRsZXIocXVlc3Rpb24udmFsdWUpO1xuICAgICAgcmVhZE9ubHlIYW5kbGVyKCk7XG4gICAgfSxcbiAgICB3aWxsVW5tb3VudDogZnVuY3Rpb24gKHF1ZXN0aW9uLCBlbCkge1xuICAgICAgcXVlc3Rpb24udmFsdWVDaGFuZ2VkQ2FsbGJhY2sgPSB1bmRlZmluZWQ7XG4gICAgICBxdWVzdGlvbi5yZWFkT25seUNoYW5nZWRDYWxsYmFjayA9IHVuZGVmaW5lZDtcbiAgICB9LFxuICB9O1xuXG4gIFN1cnZleS5DdXN0b21XaWRnZXRDb2xsZWN0aW9uLkluc3RhbmNlLmFkZEN1c3RvbVdpZGdldCh3aWRnZXQsIFwicHJvcGVydHlcIik7XG59XG5cbmlmICh0eXBlb2YgU3VydmV5ICE9PSBcInVuZGVmaW5lZFwiKSB7XG4gIGluaXQoU3VydmV5KTtcbn1cblxuZXhwb3J0IGRlZmF1bHQgaW5pdDtcblxuXG5cbi8vLy8vLy8vLy8vLy8vLy8vL1xuLy8gV0VCUEFDSyBGT09URVJcbi8vIC4vc3JjL3ByZXR0eS1jaGVja2JveC5qc1xuLy8gbW9kdWxlIGlkID0gMTNcbi8vIG1vZHVsZSBjaHVua3MgPSAwIDgiXSwic291cmNlUm9vdCI6IiJ9