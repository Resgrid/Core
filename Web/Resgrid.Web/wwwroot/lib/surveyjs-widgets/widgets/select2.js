(function webpackUniversalModuleDefinition(root, factory) {
	if(typeof exports === 'object' && typeof module === 'object')
		module.exports = factory();
	else if(typeof define === 'function' && define.amd)
		define("widgets/select2", [], factory);
	else if(typeof exports === 'object')
		exports["widgets/select2"] = factory();
	else
		root["widgets/select2"] = factory();
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
/******/ 	return __webpack_require__(__webpack_require__.s = 1);
/******/ })
/************************************************************************/
/******/ ([
/* 0 */,
/* 1 */
/***/ (function(module, __webpack_exports__, __webpack_require__) {

"use strict";
Object.defineProperty(__webpack_exports__, "__esModule", { value: true });
function init(Survey, $) {
  $ = $ || window.$;
  var widget = {
    activatedBy: "property",
    name: "select2",
    widgetIsLoaded: function () {
      return typeof $ == "function" && !!$.fn.select2;
    },
    isFit: function (question) {
      if (widget.activatedBy == "property")
        return (
          question["renderAs"] === "select2" &&
          question.getType() === "dropdown"
        );
      if (widget.activatedBy == "type")
        return question.getType() === "dropdown";
      if (widget.activatedBy == "customtype")
        return question.getType() === "select2";
      return false;
    },
    activatedByChanged: function (activatedBy) {
      if (!this.widgetIsLoaded()) return;
      widget.activatedBy = activatedBy;
      Survey.JsonObject.metaData.removeProperty("dropdown", "renderAs");
      if (activatedBy == "property") {
        Survey.JsonObject.metaData.addProperty("dropdown", {
          name: "renderAs",
          category: "general",
          default: "default",
          choices: ["select2", "default"],
        });
        Survey.JsonObject.metaData.addProperty("dropdown", {
          dependsOn: "renderAs",
          category: "general",
          name: "select2Config",
          visibleIf: function (obj) {
            return obj.renderAs == "select2";
          },
        });
      }
      if (activatedBy == "customtype") {
        Survey.JsonObject.metaData.addClass("select2", [], null, "dropdown");
        Survey.JsonObject.metaData.addProperty("select2", {
          name: "select2Config",
          category: "general",
          default: null,
        });
      }
    },
    htmlTemplate:
      "<div><select style='width: 100%;'></select><textarea></textarea></div>",
    afterRender: function (question, el) {
      var select2Config = question.select2Config;
      var settings =
        select2Config && typeof select2Config == "string"
          ? JSON.parse(select2Config)
          : select2Config;
      if (!settings) settings = {};
      var $el = $(el).is("select") ? $(el) : $(el).find("select");
      var $otherElement = $(el).find("textarea");
      $otherElement.addClass(question.cssClasses.other);
      $otherElement.bind("input propertychange", function () {
        if (isSettingValue) return;
        question.comment = $otherElement.val();
      });

      var updateComment = function () {
        $otherElement.val(question.comment);
        if (question.isOtherSelected) {
          $otherElement.show();
        } else {
          $otherElement.hide();
        }
      };
      var isSettingValue = false;
      var updateValueHandler = function () {
        if (isSettingValue) return;
        isSettingValue = true;
        if ($el.find('option[value="' + (question.value || "") + '"]').length) {
          $el.val(question.value).trigger("change");
        } else {
          if (question.value !== null && question.value !== undefined) {
            var newOption = new Option(
              question.value, //TODO if question value is object then need to improve
              question.value,
              true,
              true
            );
            $el.append(newOption).trigger("change");
          }
        }
        updateComment();
        isSettingValue = false;
      };
      var updateChoices = function () {
        $el.select2().empty();
        if (!settings.placeholder && question.showOptionsCaption) {
          settings.placeholder = question.optionsCaption;
          settings.allowClear = true;
        }
        if (!settings.theme) {
          settings.theme = "classic";
        }
        settings.disabled = question.isReadOnly;
        if (settings.ajax) {
          $el.select2(settings);
          question.keepIncorrectValues = true;
        } else {
          var data = [];
          if (!!settings.placeholder || question.showOptionsCaption) {
            data.push({ id: "", text: "" });
          }
          settings.data = data.concat(
            question.visibleChoices.map(function (choice) {
              return {
                id: choice.value,
                text: choice.text,
              };
            })
          );
          question.clearIncorrectValues();
          $el.select2(settings);
        }
        // fixed width accrording to https://stackoverflow.com/questions/45276778/select2-not-responsive-width-larger-than-container
        if (!!el.querySelector(".select2")) {
          el.querySelector(".select2").style.width = "100%";
        }
        if (!!el.nextElementSibling) {
          el.nextElementSibling.style.marginBottom = "1px";
        }
        updateValueHandler();
      };

      $otherElement.prop("disabled", question.isReadOnly);
      question.readOnlyChangedCallback = function () {
        $el.prop("disabled", question.isReadOnly);
        $otherElement.prop("disabled", question.isReadOnly);
      };

      question.registerFunctionOnPropertyValueChanged(
        "visibleChoices",
        function () {
          updateChoices();
        }
      );
      updateChoices();
      $el.on("change", function (e) {
        setTimeout(function() {
          question.renderedValue = e.target.value;
          updateComment();
        }, 1);
      });
      $el.on("select2:select", function (e) {
        setTimeout(function() {
          question.renderedValue = e.target.value;
          updateComment();
        }, 1);
      });
      $el.on('select2:opening', function(e) {
          if ($(this).data('unselecting')) {
              $(this).removeData('unselecting');
              e.preventDefault();
          }
      });
      $el.on("select2:unselecting", function (e) {
        $(this).data('unselecting', true);
        setTimeout(function() {
          question.renderedValue = null;
          updateComment();
        }, 1);
      });
      question.valueChangedCallback = updateValueHandler;
      updateValueHandler();
    },
    willUnmount: function (question, el) {
      question.readOnlyChangedCallback = null;
      question.valueChangedCallback = null;
      var $select2 = $(el).find("select");
      if (!!$select2.data("select2")) {
        $select2
          .off("select2:select")
          .off("select2:unselecting")
          .off("select2:opening")
          .select2("destroy");
      }
    },
  };

  Survey.CustomWidgetCollection.Instance.addCustomWidget(widget);
}

if (typeof Survey !== "undefined") {
  init(Survey, window.$);
}

/* harmony default export */ __webpack_exports__["default"] = (init);


/***/ })
/******/ ]);
});
//# sourceMappingURL=data:application/json;charset=utf-8;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIndlYnBhY2s6Ly8vd2VicGFjay91bml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uIiwid2VicGFjazovLy93ZWJwYWNrL2Jvb3RzdHJhcCBmOWIyNGFiYzlmODA1NTBmYmQzZiIsIndlYnBhY2s6Ly8vLi9zcmMvc2VsZWN0Mi5qcyJdLCJuYW1lcyI6W10sIm1hcHBpbmdzIjoiQUFBQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxDQUFDO0FBQ0QsTztRQ1ZBO1FBQ0E7O1FBRUE7UUFDQTs7UUFFQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTs7UUFFQTtRQUNBOztRQUVBO1FBQ0E7O1FBRUE7UUFDQTtRQUNBOzs7UUFHQTtRQUNBOztRQUVBO1FBQ0E7O1FBRUE7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQSxLQUFLO1FBQ0w7UUFDQTs7UUFFQTtRQUNBO1FBQ0E7UUFDQSwyQkFBMkIsMEJBQTBCLEVBQUU7UUFDdkQsaUNBQWlDLGVBQWU7UUFDaEQ7UUFDQTtRQUNBOztRQUVBO1FBQ0Esc0RBQXNELCtEQUErRDs7UUFFckg7UUFDQTs7UUFFQTtRQUNBOzs7Ozs7Ozs7QUM3REE7QUFBQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLEtBQUs7QUFDTDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFdBQVc7QUFDWCxTQUFTO0FBQ1Q7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxTQUFTO0FBQ1Q7QUFDQSxLQUFLO0FBQ0w7QUFDQSx1Q0FBdUM7QUFDdkM7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxPQUFPOztBQUVQO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0E7QUFDQSx1QkFBdUIsbUJBQW1CO0FBQzFDO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsYUFBYTtBQUNiO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVCxPQUFPO0FBQ1A7QUFDQTtBQUNBO0FBQ0E7QUFDQSxTQUFTO0FBQ1QsT0FBTztBQUNQO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxPQUFPO0FBQ1A7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVCxPQUFPO0FBQ1A7QUFDQTtBQUNBLEtBQUs7QUFDTDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsS0FBSztBQUNMOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVlLG1FQUFJLEVBQUMiLCJmaWxlIjoid2lkZ2V0cy9zZWxlY3QyLmpzIiwic291cmNlc0NvbnRlbnQiOlsiKGZ1bmN0aW9uIHdlYnBhY2tVbml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uKHJvb3QsIGZhY3RvcnkpIHtcblx0aWYodHlwZW9mIGV4cG9ydHMgPT09ICdvYmplY3QnICYmIHR5cGVvZiBtb2R1bGUgPT09ICdvYmplY3QnKVxuXHRcdG1vZHVsZS5leHBvcnRzID0gZmFjdG9yeSgpO1xuXHRlbHNlIGlmKHR5cGVvZiBkZWZpbmUgPT09ICdmdW5jdGlvbicgJiYgZGVmaW5lLmFtZClcblx0XHRkZWZpbmUoXCJ3aWRnZXRzL3NlbGVjdDJcIiwgW10sIGZhY3RvcnkpO1xuXHRlbHNlIGlmKHR5cGVvZiBleHBvcnRzID09PSAnb2JqZWN0Jylcblx0XHRleHBvcnRzW1wid2lkZ2V0cy9zZWxlY3QyXCJdID0gZmFjdG9yeSgpO1xuXHRlbHNlXG5cdFx0cm9vdFtcIndpZGdldHMvc2VsZWN0MlwiXSA9IGZhY3RvcnkoKTtcbn0pKHR5cGVvZiBzZWxmICE9PSAndW5kZWZpbmVkJyA/IHNlbGYgOiB0aGlzLCBmdW5jdGlvbigpIHtcbnJldHVybiBcblxuXG4vLyBXRUJQQUNLIEZPT1RFUiAvL1xuLy8gd2VicGFjay91bml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uIiwiIFx0Ly8gVGhlIG1vZHVsZSBjYWNoZVxuIFx0dmFyIGluc3RhbGxlZE1vZHVsZXMgPSB7fTtcblxuIFx0Ly8gVGhlIHJlcXVpcmUgZnVuY3Rpb25cbiBcdGZ1bmN0aW9uIF9fd2VicGFja19yZXF1aXJlX18obW9kdWxlSWQpIHtcblxuIFx0XHQvLyBDaGVjayBpZiBtb2R1bGUgaXMgaW4gY2FjaGVcbiBcdFx0aWYoaW5zdGFsbGVkTW9kdWxlc1ttb2R1bGVJZF0pIHtcbiBcdFx0XHRyZXR1cm4gaW5zdGFsbGVkTW9kdWxlc1ttb2R1bGVJZF0uZXhwb3J0cztcbiBcdFx0fVxuIFx0XHQvLyBDcmVhdGUgYSBuZXcgbW9kdWxlIChhbmQgcHV0IGl0IGludG8gdGhlIGNhY2hlKVxuIFx0XHR2YXIgbW9kdWxlID0gaW5zdGFsbGVkTW9kdWxlc1ttb2R1bGVJZF0gPSB7XG4gXHRcdFx0aTogbW9kdWxlSWQsXG4gXHRcdFx0bDogZmFsc2UsXG4gXHRcdFx0ZXhwb3J0czoge31cbiBcdFx0fTtcblxuIFx0XHQvLyBFeGVjdXRlIHRoZSBtb2R1bGUgZnVuY3Rpb25cbiBcdFx0bW9kdWxlc1ttb2R1bGVJZF0uY2FsbChtb2R1bGUuZXhwb3J0cywgbW9kdWxlLCBtb2R1bGUuZXhwb3J0cywgX193ZWJwYWNrX3JlcXVpcmVfXyk7XG5cbiBcdFx0Ly8gRmxhZyB0aGUgbW9kdWxlIGFzIGxvYWRlZFxuIFx0XHRtb2R1bGUubCA9IHRydWU7XG5cbiBcdFx0Ly8gUmV0dXJuIHRoZSBleHBvcnRzIG9mIHRoZSBtb2R1bGVcbiBcdFx0cmV0dXJuIG1vZHVsZS5leHBvcnRzO1xuIFx0fVxuXG5cbiBcdC8vIGV4cG9zZSB0aGUgbW9kdWxlcyBvYmplY3QgKF9fd2VicGFja19tb2R1bGVzX18pXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLm0gPSBtb2R1bGVzO1xuXG4gXHQvLyBleHBvc2UgdGhlIG1vZHVsZSBjYWNoZVxuIFx0X193ZWJwYWNrX3JlcXVpcmVfXy5jID0gaW5zdGFsbGVkTW9kdWxlcztcblxuIFx0Ly8gZGVmaW5lIGdldHRlciBmdW5jdGlvbiBmb3IgaGFybW9ueSBleHBvcnRzXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLmQgPSBmdW5jdGlvbihleHBvcnRzLCBuYW1lLCBnZXR0ZXIpIHtcbiBcdFx0aWYoIV9fd2VicGFja19yZXF1aXJlX18ubyhleHBvcnRzLCBuYW1lKSkge1xuIFx0XHRcdE9iamVjdC5kZWZpbmVQcm9wZXJ0eShleHBvcnRzLCBuYW1lLCB7XG4gXHRcdFx0XHRjb25maWd1cmFibGU6IGZhbHNlLFxuIFx0XHRcdFx0ZW51bWVyYWJsZTogdHJ1ZSxcbiBcdFx0XHRcdGdldDogZ2V0dGVyXG4gXHRcdFx0fSk7XG4gXHRcdH1cbiBcdH07XG5cbiBcdC8vIGdldERlZmF1bHRFeHBvcnQgZnVuY3Rpb24gZm9yIGNvbXBhdGliaWxpdHkgd2l0aCBub24taGFybW9ueSBtb2R1bGVzXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLm4gPSBmdW5jdGlvbihtb2R1bGUpIHtcbiBcdFx0dmFyIGdldHRlciA9IG1vZHVsZSAmJiBtb2R1bGUuX19lc01vZHVsZSA/XG4gXHRcdFx0ZnVuY3Rpb24gZ2V0RGVmYXVsdCgpIHsgcmV0dXJuIG1vZHVsZVsnZGVmYXVsdCddOyB9IDpcbiBcdFx0XHRmdW5jdGlvbiBnZXRNb2R1bGVFeHBvcnRzKCkgeyByZXR1cm4gbW9kdWxlOyB9O1xuIFx0XHRfX3dlYnBhY2tfcmVxdWlyZV9fLmQoZ2V0dGVyLCAnYScsIGdldHRlcik7XG4gXHRcdHJldHVybiBnZXR0ZXI7XG4gXHR9O1xuXG4gXHQvLyBPYmplY3QucHJvdG90eXBlLmhhc093blByb3BlcnR5LmNhbGxcbiBcdF9fd2VicGFja19yZXF1aXJlX18ubyA9IGZ1bmN0aW9uKG9iamVjdCwgcHJvcGVydHkpIHsgcmV0dXJuIE9iamVjdC5wcm90b3R5cGUuaGFzT3duUHJvcGVydHkuY2FsbChvYmplY3QsIHByb3BlcnR5KTsgfTtcblxuIFx0Ly8gX193ZWJwYWNrX3B1YmxpY19wYXRoX19cbiBcdF9fd2VicGFja19yZXF1aXJlX18ucCA9IFwiXCI7XG5cbiBcdC8vIExvYWQgZW50cnkgbW9kdWxlIGFuZCByZXR1cm4gZXhwb3J0c1xuIFx0cmV0dXJuIF9fd2VicGFja19yZXF1aXJlX18oX193ZWJwYWNrX3JlcXVpcmVfXy5zID0gMSk7XG5cblxuXG4vLyBXRUJQQUNLIEZPT1RFUiAvL1xuLy8gd2VicGFjay9ib290c3RyYXAgZjliMjRhYmM5ZjgwNTUwZmJkM2YiLCJmdW5jdGlvbiBpbml0KFN1cnZleSwgJCkge1xuICAkID0gJCB8fCB3aW5kb3cuJDtcbiAgdmFyIHdpZGdldCA9IHtcbiAgICBhY3RpdmF0ZWRCeTogXCJwcm9wZXJ0eVwiLFxuICAgIG5hbWU6IFwic2VsZWN0MlwiLFxuICAgIHdpZGdldElzTG9hZGVkOiBmdW5jdGlvbiAoKSB7XG4gICAgICByZXR1cm4gdHlwZW9mICQgPT0gXCJmdW5jdGlvblwiICYmICEhJC5mbi5zZWxlY3QyO1xuICAgIH0sXG4gICAgaXNGaXQ6IGZ1bmN0aW9uIChxdWVzdGlvbikge1xuICAgICAgaWYgKHdpZGdldC5hY3RpdmF0ZWRCeSA9PSBcInByb3BlcnR5XCIpXG4gICAgICAgIHJldHVybiAoXG4gICAgICAgICAgcXVlc3Rpb25bXCJyZW5kZXJBc1wiXSA9PT0gXCJzZWxlY3QyXCIgJiZcbiAgICAgICAgICBxdWVzdGlvbi5nZXRUeXBlKCkgPT09IFwiZHJvcGRvd25cIlxuICAgICAgICApO1xuICAgICAgaWYgKHdpZGdldC5hY3RpdmF0ZWRCeSA9PSBcInR5cGVcIilcbiAgICAgICAgcmV0dXJuIHF1ZXN0aW9uLmdldFR5cGUoKSA9PT0gXCJkcm9wZG93blwiO1xuICAgICAgaWYgKHdpZGdldC5hY3RpdmF0ZWRCeSA9PSBcImN1c3RvbXR5cGVcIilcbiAgICAgICAgcmV0dXJuIHF1ZXN0aW9uLmdldFR5cGUoKSA9PT0gXCJzZWxlY3QyXCI7XG4gICAgICByZXR1cm4gZmFsc2U7XG4gICAgfSxcbiAgICBhY3RpdmF0ZWRCeUNoYW5nZWQ6IGZ1bmN0aW9uIChhY3RpdmF0ZWRCeSkge1xuICAgICAgaWYgKCF0aGlzLndpZGdldElzTG9hZGVkKCkpIHJldHVybjtcbiAgICAgIHdpZGdldC5hY3RpdmF0ZWRCeSA9IGFjdGl2YXRlZEJ5O1xuICAgICAgU3VydmV5Lkpzb25PYmplY3QubWV0YURhdGEucmVtb3ZlUHJvcGVydHkoXCJkcm9wZG93blwiLCBcInJlbmRlckFzXCIpO1xuICAgICAgaWYgKGFjdGl2YXRlZEJ5ID09IFwicHJvcGVydHlcIikge1xuICAgICAgICBTdXJ2ZXkuSnNvbk9iamVjdC5tZXRhRGF0YS5hZGRQcm9wZXJ0eShcImRyb3Bkb3duXCIsIHtcbiAgICAgICAgICBuYW1lOiBcInJlbmRlckFzXCIsXG4gICAgICAgICAgY2F0ZWdvcnk6IFwiZ2VuZXJhbFwiLFxuICAgICAgICAgIGRlZmF1bHQ6IFwiZGVmYXVsdFwiLFxuICAgICAgICAgIGNob2ljZXM6IFtcInNlbGVjdDJcIiwgXCJkZWZhdWx0XCJdLFxuICAgICAgICB9KTtcbiAgICAgICAgU3VydmV5Lkpzb25PYmplY3QubWV0YURhdGEuYWRkUHJvcGVydHkoXCJkcm9wZG93blwiLCB7XG4gICAgICAgICAgZGVwZW5kc09uOiBcInJlbmRlckFzXCIsXG4gICAgICAgICAgY2F0ZWdvcnk6IFwiZ2VuZXJhbFwiLFxuICAgICAgICAgIG5hbWU6IFwic2VsZWN0MkNvbmZpZ1wiLFxuICAgICAgICAgIHZpc2libGVJZjogZnVuY3Rpb24gKG9iaikge1xuICAgICAgICAgICAgcmV0dXJuIG9iai5yZW5kZXJBcyA9PSBcInNlbGVjdDJcIjtcbiAgICAgICAgICB9LFxuICAgICAgICB9KTtcbiAgICAgIH1cbiAgICAgIGlmIChhY3RpdmF0ZWRCeSA9PSBcImN1c3RvbXR5cGVcIikge1xuICAgICAgICBTdXJ2ZXkuSnNvbk9iamVjdC5tZXRhRGF0YS5hZGRDbGFzcyhcInNlbGVjdDJcIiwgW10sIG51bGwsIFwiZHJvcGRvd25cIik7XG4gICAgICAgIFN1cnZleS5Kc29uT2JqZWN0Lm1ldGFEYXRhLmFkZFByb3BlcnR5KFwic2VsZWN0MlwiLCB7XG4gICAgICAgICAgbmFtZTogXCJzZWxlY3QyQ29uZmlnXCIsXG4gICAgICAgICAgY2F0ZWdvcnk6IFwiZ2VuZXJhbFwiLFxuICAgICAgICAgIGRlZmF1bHQ6IG51bGwsXG4gICAgICAgIH0pO1xuICAgICAgfVxuICAgIH0sXG4gICAgaHRtbFRlbXBsYXRlOlxuICAgICAgXCI8ZGl2PjxzZWxlY3Qgc3R5bGU9J3dpZHRoOiAxMDAlOyc+PC9zZWxlY3Q+PHRleHRhcmVhPjwvdGV4dGFyZWE+PC9kaXY+XCIsXG4gICAgYWZ0ZXJSZW5kZXI6IGZ1bmN0aW9uIChxdWVzdGlvbiwgZWwpIHtcbiAgICAgIHZhciBzZWxlY3QyQ29uZmlnID0gcXVlc3Rpb24uc2VsZWN0MkNvbmZpZztcbiAgICAgIHZhciBzZXR0aW5ncyA9XG4gICAgICAgIHNlbGVjdDJDb25maWcgJiYgdHlwZW9mIHNlbGVjdDJDb25maWcgPT0gXCJzdHJpbmdcIlxuICAgICAgICAgID8gSlNPTi5wYXJzZShzZWxlY3QyQ29uZmlnKVxuICAgICAgICAgIDogc2VsZWN0MkNvbmZpZztcbiAgICAgIGlmICghc2V0dGluZ3MpIHNldHRpbmdzID0ge307XG4gICAgICB2YXIgJGVsID0gJChlbCkuaXMoXCJzZWxlY3RcIikgPyAkKGVsKSA6ICQoZWwpLmZpbmQoXCJzZWxlY3RcIik7XG4gICAgICB2YXIgJG90aGVyRWxlbWVudCA9ICQoZWwpLmZpbmQoXCJ0ZXh0YXJlYVwiKTtcbiAgICAgICRvdGhlckVsZW1lbnQuYWRkQ2xhc3MocXVlc3Rpb24uY3NzQ2xhc3Nlcy5vdGhlcik7XG4gICAgICAkb3RoZXJFbGVtZW50LmJpbmQoXCJpbnB1dCBwcm9wZXJ0eWNoYW5nZVwiLCBmdW5jdGlvbiAoKSB7XG4gICAgICAgIGlmIChpc1NldHRpbmdWYWx1ZSkgcmV0dXJuO1xuICAgICAgICBxdWVzdGlvbi5jb21tZW50ID0gJG90aGVyRWxlbWVudC52YWwoKTtcbiAgICAgIH0pO1xuXG4gICAgICB2YXIgdXBkYXRlQ29tbWVudCA9IGZ1bmN0aW9uICgpIHtcbiAgICAgICAgJG90aGVyRWxlbWVudC52YWwocXVlc3Rpb24uY29tbWVudCk7XG4gICAgICAgIGlmIChxdWVzdGlvbi5pc090aGVyU2VsZWN0ZWQpIHtcbiAgICAgICAgICAkb3RoZXJFbGVtZW50LnNob3coKTtcbiAgICAgICAgfSBlbHNlIHtcbiAgICAgICAgICAkb3RoZXJFbGVtZW50LmhpZGUoKTtcbiAgICAgICAgfVxuICAgICAgfTtcbiAgICAgIHZhciBpc1NldHRpbmdWYWx1ZSA9IGZhbHNlO1xuICAgICAgdmFyIHVwZGF0ZVZhbHVlSGFuZGxlciA9IGZ1bmN0aW9uICgpIHtcbiAgICAgICAgaWYgKGlzU2V0dGluZ1ZhbHVlKSByZXR1cm47XG4gICAgICAgIGlzU2V0dGluZ1ZhbHVlID0gdHJ1ZTtcbiAgICAgICAgaWYgKCRlbC5maW5kKCdvcHRpb25bdmFsdWU9XCInICsgKHF1ZXN0aW9uLnZhbHVlIHx8IFwiXCIpICsgJ1wiXScpLmxlbmd0aCkge1xuICAgICAgICAgICRlbC52YWwocXVlc3Rpb24udmFsdWUpLnRyaWdnZXIoXCJjaGFuZ2VcIik7XG4gICAgICAgIH0gZWxzZSB7XG4gICAgICAgICAgaWYgKHF1ZXN0aW9uLnZhbHVlICE9PSBudWxsICYmIHF1ZXN0aW9uLnZhbHVlICE9PSB1bmRlZmluZWQpIHtcbiAgICAgICAgICAgIHZhciBuZXdPcHRpb24gPSBuZXcgT3B0aW9uKFxuICAgICAgICAgICAgICBxdWVzdGlvbi52YWx1ZSwgLy9UT0RPIGlmIHF1ZXN0aW9uIHZhbHVlIGlzIG9iamVjdCB0aGVuIG5lZWQgdG8gaW1wcm92ZVxuICAgICAgICAgICAgICBxdWVzdGlvbi52YWx1ZSxcbiAgICAgICAgICAgICAgdHJ1ZSxcbiAgICAgICAgICAgICAgdHJ1ZVxuICAgICAgICAgICAgKTtcbiAgICAgICAgICAgICRlbC5hcHBlbmQobmV3T3B0aW9uKS50cmlnZ2VyKFwiY2hhbmdlXCIpO1xuICAgICAgICAgIH1cbiAgICAgICAgfVxuICAgICAgICB1cGRhdGVDb21tZW50KCk7XG4gICAgICAgIGlzU2V0dGluZ1ZhbHVlID0gZmFsc2U7XG4gICAgICB9O1xuICAgICAgdmFyIHVwZGF0ZUNob2ljZXMgPSBmdW5jdGlvbiAoKSB7XG4gICAgICAgICRlbC5zZWxlY3QyKCkuZW1wdHkoKTtcbiAgICAgICAgaWYgKCFzZXR0aW5ncy5wbGFjZWhvbGRlciAmJiBxdWVzdGlvbi5zaG93T3B0aW9uc0NhcHRpb24pIHtcbiAgICAgICAgICBzZXR0aW5ncy5wbGFjZWhvbGRlciA9IHF1ZXN0aW9uLm9wdGlvbnNDYXB0aW9uO1xuICAgICAgICAgIHNldHRpbmdzLmFsbG93Q2xlYXIgPSB0cnVlO1xuICAgICAgICB9XG4gICAgICAgIGlmICghc2V0dGluZ3MudGhlbWUpIHtcbiAgICAgICAgICBzZXR0aW5ncy50aGVtZSA9IFwiY2xhc3NpY1wiO1xuICAgICAgICB9XG4gICAgICAgIHNldHRpbmdzLmRpc2FibGVkID0gcXVlc3Rpb24uaXNSZWFkT25seTtcbiAgICAgICAgaWYgKHNldHRpbmdzLmFqYXgpIHtcbiAgICAgICAgICAkZWwuc2VsZWN0MihzZXR0aW5ncyk7XG4gICAgICAgICAgcXVlc3Rpb24ua2VlcEluY29ycmVjdFZhbHVlcyA9IHRydWU7XG4gICAgICAgIH0gZWxzZSB7XG4gICAgICAgICAgdmFyIGRhdGEgPSBbXTtcbiAgICAgICAgICBpZiAoISFzZXR0aW5ncy5wbGFjZWhvbGRlciB8fCBxdWVzdGlvbi5zaG93T3B0aW9uc0NhcHRpb24pIHtcbiAgICAgICAgICAgIGRhdGEucHVzaCh7IGlkOiBcIlwiLCB0ZXh0OiBcIlwiIH0pO1xuICAgICAgICAgIH1cbiAgICAgICAgICBzZXR0aW5ncy5kYXRhID0gZGF0YS5jb25jYXQoXG4gICAgICAgICAgICBxdWVzdGlvbi52aXNpYmxlQ2hvaWNlcy5tYXAoZnVuY3Rpb24gKGNob2ljZSkge1xuICAgICAgICAgICAgICByZXR1cm4ge1xuICAgICAgICAgICAgICAgIGlkOiBjaG9pY2UudmFsdWUsXG4gICAgICAgICAgICAgICAgdGV4dDogY2hvaWNlLnRleHQsXG4gICAgICAgICAgICAgIH07XG4gICAgICAgICAgICB9KVxuICAgICAgICAgICk7XG4gICAgICAgICAgcXVlc3Rpb24uY2xlYXJJbmNvcnJlY3RWYWx1ZXMoKTtcbiAgICAgICAgICAkZWwuc2VsZWN0MihzZXR0aW5ncyk7XG4gICAgICAgIH1cbiAgICAgICAgLy8gZml4ZWQgd2lkdGggYWNjcm9yZGluZyB0byBodHRwczovL3N0YWNrb3ZlcmZsb3cuY29tL3F1ZXN0aW9ucy80NTI3Njc3OC9zZWxlY3QyLW5vdC1yZXNwb25zaXZlLXdpZHRoLWxhcmdlci10aGFuLWNvbnRhaW5lclxuICAgICAgICBpZiAoISFlbC5xdWVyeVNlbGVjdG9yKFwiLnNlbGVjdDJcIikpIHtcbiAgICAgICAgICBlbC5xdWVyeVNlbGVjdG9yKFwiLnNlbGVjdDJcIikuc3R5bGUud2lkdGggPSBcIjEwMCVcIjtcbiAgICAgICAgfVxuICAgICAgICBpZiAoISFlbC5uZXh0RWxlbWVudFNpYmxpbmcpIHtcbiAgICAgICAgICBlbC5uZXh0RWxlbWVudFNpYmxpbmcuc3R5bGUubWFyZ2luQm90dG9tID0gXCIxcHhcIjtcbiAgICAgICAgfVxuICAgICAgICB1cGRhdGVWYWx1ZUhhbmRsZXIoKTtcbiAgICAgIH07XG5cbiAgICAgICRvdGhlckVsZW1lbnQucHJvcChcImRpc2FibGVkXCIsIHF1ZXN0aW9uLmlzUmVhZE9ubHkpO1xuICAgICAgcXVlc3Rpb24ucmVhZE9ubHlDaGFuZ2VkQ2FsbGJhY2sgPSBmdW5jdGlvbiAoKSB7XG4gICAgICAgICRlbC5wcm9wKFwiZGlzYWJsZWRcIiwgcXVlc3Rpb24uaXNSZWFkT25seSk7XG4gICAgICAgICRvdGhlckVsZW1lbnQucHJvcChcImRpc2FibGVkXCIsIHF1ZXN0aW9uLmlzUmVhZE9ubHkpO1xuICAgICAgfTtcblxuICAgICAgcXVlc3Rpb24ucmVnaXN0ZXJGdW5jdGlvbk9uUHJvcGVydHlWYWx1ZUNoYW5nZWQoXG4gICAgICAgIFwidmlzaWJsZUNob2ljZXNcIixcbiAgICAgICAgZnVuY3Rpb24gKCkge1xuICAgICAgICAgIHVwZGF0ZUNob2ljZXMoKTtcbiAgICAgICAgfVxuICAgICAgKTtcbiAgICAgIHVwZGF0ZUNob2ljZXMoKTtcbiAgICAgICRlbC5vbihcImNoYW5nZVwiLCBmdW5jdGlvbiAoZSkge1xuICAgICAgICBzZXRUaW1lb3V0KGZ1bmN0aW9uKCkge1xuICAgICAgICAgIHF1ZXN0aW9uLnJlbmRlcmVkVmFsdWUgPSBlLnRhcmdldC52YWx1ZTtcbiAgICAgICAgICB1cGRhdGVDb21tZW50KCk7XG4gICAgICAgIH0sIDEpO1xuICAgICAgfSk7XG4gICAgICAkZWwub24oXCJzZWxlY3QyOnNlbGVjdFwiLCBmdW5jdGlvbiAoZSkge1xuICAgICAgICBzZXRUaW1lb3V0KGZ1bmN0aW9uKCkge1xuICAgICAgICAgIHF1ZXN0aW9uLnJlbmRlcmVkVmFsdWUgPSBlLnRhcmdldC52YWx1ZTtcbiAgICAgICAgICB1cGRhdGVDb21tZW50KCk7XG4gICAgICAgIH0sIDEpO1xuICAgICAgfSk7XG4gICAgICAkZWwub24oJ3NlbGVjdDI6b3BlbmluZycsIGZ1bmN0aW9uKGUpIHtcbiAgICAgICAgICBpZiAoJCh0aGlzKS5kYXRhKCd1bnNlbGVjdGluZycpKSB7XG4gICAgICAgICAgICAgICQodGhpcykucmVtb3ZlRGF0YSgndW5zZWxlY3RpbmcnKTtcbiAgICAgICAgICAgICAgZS5wcmV2ZW50RGVmYXVsdCgpO1xuICAgICAgICAgIH1cbiAgICAgIH0pO1xuICAgICAgJGVsLm9uKFwic2VsZWN0Mjp1bnNlbGVjdGluZ1wiLCBmdW5jdGlvbiAoZSkge1xuICAgICAgICAkKHRoaXMpLmRhdGEoJ3Vuc2VsZWN0aW5nJywgdHJ1ZSk7XG4gICAgICAgIHNldFRpbWVvdXQoZnVuY3Rpb24oKSB7XG4gICAgICAgICAgcXVlc3Rpb24ucmVuZGVyZWRWYWx1ZSA9IG51bGw7XG4gICAgICAgICAgdXBkYXRlQ29tbWVudCgpO1xuICAgICAgICB9LCAxKTtcbiAgICAgIH0pO1xuICAgICAgcXVlc3Rpb24udmFsdWVDaGFuZ2VkQ2FsbGJhY2sgPSB1cGRhdGVWYWx1ZUhhbmRsZXI7XG4gICAgICB1cGRhdGVWYWx1ZUhhbmRsZXIoKTtcbiAgICB9LFxuICAgIHdpbGxVbm1vdW50OiBmdW5jdGlvbiAocXVlc3Rpb24sIGVsKSB7XG4gICAgICBxdWVzdGlvbi5yZWFkT25seUNoYW5nZWRDYWxsYmFjayA9IG51bGw7XG4gICAgICBxdWVzdGlvbi52YWx1ZUNoYW5nZWRDYWxsYmFjayA9IG51bGw7XG4gICAgICB2YXIgJHNlbGVjdDIgPSAkKGVsKS5maW5kKFwic2VsZWN0XCIpO1xuICAgICAgaWYgKCEhJHNlbGVjdDIuZGF0YShcInNlbGVjdDJcIikpIHtcbiAgICAgICAgJHNlbGVjdDJcbiAgICAgICAgICAub2ZmKFwic2VsZWN0MjpzZWxlY3RcIilcbiAgICAgICAgICAub2ZmKFwic2VsZWN0Mjp1bnNlbGVjdGluZ1wiKVxuICAgICAgICAgIC5vZmYoXCJzZWxlY3QyOm9wZW5pbmdcIilcbiAgICAgICAgICAuc2VsZWN0MihcImRlc3Ryb3lcIik7XG4gICAgICB9XG4gICAgfSxcbiAgfTtcblxuICBTdXJ2ZXkuQ3VzdG9tV2lkZ2V0Q29sbGVjdGlvbi5JbnN0YW5jZS5hZGRDdXN0b21XaWRnZXQod2lkZ2V0KTtcbn1cblxuaWYgKHR5cGVvZiBTdXJ2ZXkgIT09IFwidW5kZWZpbmVkXCIpIHtcbiAgaW5pdChTdXJ2ZXksIHdpbmRvdy4kKTtcbn1cblxuZXhwb3J0IGRlZmF1bHQgaW5pdDtcblxuXG5cbi8vLy8vLy8vLy8vLy8vLy8vL1xuLy8gV0VCUEFDSyBGT09URVJcbi8vIC4vc3JjL3NlbGVjdDIuanNcbi8vIG1vZHVsZSBpZCA9IDFcbi8vIG1vZHVsZSBjaHVua3MgPSAwIDYiXSwic291cmNlUm9vdCI6IiJ9