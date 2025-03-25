(function webpackUniversalModuleDefinition(root, factory) {
	if(typeof exports === 'object' && typeof module === 'object')
		module.exports = factory();
	else if(typeof define === 'function' && define.amd)
		define("widgets/jquery-bar-rating", [], factory);
	else if(typeof exports === 'object')
		exports["widgets/jquery-bar-rating"] = factory();
	else
		root["widgets/jquery-bar-rating"] = factory();
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
/******/ 	return __webpack_require__(__webpack_require__.s = 4);
/******/ })
/************************************************************************/
/******/ ({

/***/ 4:
/***/ (function(module, __webpack_exports__, __webpack_require__) {

"use strict";
Object.defineProperty(__webpack_exports__, "__esModule", { value: true });
function init(Survey, $) {
  $ = $ || window.$;
  var widget = {
    name: "barrating",
    title: "Bar rating",
    iconName: "icon-barrating",
    widgetIsLoaded: function () {
      return typeof $ == "function" && !!$.fn.barrating;
    },
    defaultJSON: { choices: [1, 2, 3, 4, 5] },
    isFit: function (question) {
      return question.getType() === "barrating";
    },
    isDefaultRender: true,
    activatedByChanged: function (activatedBy) {
      Survey.JsonObject.metaData.addClass(
        "barrating",
        [
          { name: "hasOther", visible: false },
          { name: "otherText", visible: false },
          { name: "optionsCaption", visible: false },
          { name: "otherErrorText", visible: false },
          { name: "storeOthersAsComment", visible: false },
          { name: "renderAs", visible: false },
          { name: "select2Config", visible: false },
        ],
        null,
        "dropdown"
      );
      Survey.JsonObject.metaData.addProperty("barrating", {
        name: "showValues:boolean",
        default: false,
        category: "general",
      });
      Survey.JsonObject.metaData.addProperty("barrating", {
        name: "ratingTheme",
        category: "general",
        default: "css-stars",
        choices: [
          "fontawesome-stars",
          "css-stars",
          "bars-pill",
          "bars-1to10",
          "bars-movie",
          "bars-reversed",
          "bars-horizontal",
          "fontawesome-stars-o",
        ],
      });
    },
    afterRender: function (question, el) {
      var $customSelect;
      var $questionInput;
      var contentContainer = $(el).is("select")
        ? $(el).parent().parent()[0]
        : $(el).parent()[0];
      var renderCustomSelect = function () {
        $customSelect = $("<select class='sv-widget-select'></select>");
        question.visibleChoices.forEach(function (choice) {
          $customSelect.append(
            '<option value="{0}">{1}</option>'.format(choice.value, choice.text)
          );
        });
        $questionInput = $(contentContainer).find(
          '[id="{0}"]'.format(question.inputId)
        );

        $questionInput.css("display", "none");
        $questionInput.after($customSelect);
        $customSelect[0].selectedIndex = -1;
      };
      var removeCustomSelect = function () {
        $questionInput.css("display", "");
        $customSelect.barrating("destroy");
        $customSelect.remove();
      };
      var renderBarrating = function () {
        $customSelect.barrating("show", {
          theme: question.ratingTheme,
          initialRating: question.value,
          showValues: question.showValues,
          showSelectedRating: false,
          readonly: question.isReadOnly,
          onSelect: function (value, text) {
            valueChangingByWidget = true;
            question.value = value;
            valueChangingByWidget = false;
          },
        });
      };
      renderCustomSelect();
      renderBarrating();
      if (!!$customSelect.parents()[0])
        $customSelect.parents()[0].style.marginBottom = "3px";
      var valueChangingByWidget = false;

      question.valueChangedCallback = function () {
        if (
          !valueChangingByWidget &&
          $(contentContainer).find("select.sv-widget-select")[0].value !==
            question.value
        ) {
          $(contentContainer)
            .find("select.sv-widget-select")
            .barrating("set", question.value);
        }
      };
      question.__barratingOnPropertyChangedCallback = function (
        sender,
        options
      ) {
        if (options.name == "ratingTheme") {
          $customSelect.barrating("destroy");
          renderBarrating();
        }
      };
      question.onPropertyChanged.add(
        question.__barratingOnPropertyChangedCallback
      );
      question.readOnlyChangedCallback = function () {
        removeCustomSelect();
        renderCustomSelect();
        renderBarrating();
      };
      question.visibleChoicesChangedCallback = function () {
        renderBarrating();
      };
    },
    willUnmount: function (question, el) {
      var $contentContainer = $(el).is("select")
        ? $(el).parent().parent()
        : $(el).parent();
      var $el = $contentContainer.find("select.sv-widget-select");
      $el.barrating("destroy");
      $el.remove();
      question.valueChangedCallback = undefined;
      question.onPropertyChanged.remove(
        question.__barratingOnPropertyChangedCallback
      );
      question.__barratingOnPropertyChangedCallback = undefined;
    },
    pdfQuestionType: "dropdown",
  };

  Survey.CustomWidgetCollection.Instance.addCustomWidget(widget, "customtype");
}

if (typeof Survey !== "undefined") {
  init(Survey, window.$);
}

/* harmony default export */ __webpack_exports__["default"] = (init);


/***/ })

/******/ });
});
//# sourceMappingURL=data:application/json;charset=utf-8;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIndlYnBhY2s6Ly8vd2VicGFjay91bml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uIiwid2VicGFjazovLy93ZWJwYWNrL2Jvb3RzdHJhcCBmOWIyNGFiYzlmODA1NTBmYmQzZiIsIndlYnBhY2s6Ly8vLi9zcmMvanF1ZXJ5LWJhci1yYXRpbmcuanMiXSwibmFtZXMiOltdLCJtYXBwaW5ncyI6IkFBQUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsQ0FBQztBQUNELE87UUNWQTtRQUNBOztRQUVBO1FBQ0E7O1FBRUE7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7O1FBRUE7UUFDQTs7UUFFQTtRQUNBOztRQUVBO1FBQ0E7UUFDQTs7O1FBR0E7UUFDQTs7UUFFQTtRQUNBOztRQUVBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0EsS0FBSztRQUNMO1FBQ0E7O1FBRUE7UUFDQTtRQUNBO1FBQ0EsMkJBQTJCLDBCQUEwQixFQUFFO1FBQ3ZELGlDQUFpQyxlQUFlO1FBQ2hEO1FBQ0E7UUFDQTs7UUFFQTtRQUNBLHNEQUFzRCwrREFBK0Q7O1FBRXJIO1FBQ0E7O1FBRUE7UUFDQTs7Ozs7Ozs7O0FDN0RBO0FBQUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLEtBQUs7QUFDTCxrQkFBa0IsMkJBQTJCO0FBQzdDO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFdBQVcsbUNBQW1DO0FBQzlDLFdBQVcsb0NBQW9DO0FBQy9DLFdBQVcseUNBQXlDO0FBQ3BELFdBQVcseUNBQXlDO0FBQ3BELFdBQVcsK0NBQStDO0FBQzFELFdBQVcsbUNBQW1DO0FBQzlDLFdBQVcsd0NBQXdDO0FBQ25EO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxPQUFPO0FBQ1A7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLE9BQU87QUFDUCxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSw2QkFBNkIsRUFBRSxHQUFHLEVBQUU7QUFDcEM7QUFDQSxTQUFTO0FBQ1Q7QUFDQSxpQkFBaUIsRUFBRTtBQUNuQjs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsV0FBVztBQUNYLFNBQVM7QUFDVDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRWUsbUVBQUksRUFBQyIsImZpbGUiOiJ3aWRnZXRzL2pxdWVyeS1iYXItcmF0aW5nLmpzIiwic291cmNlc0NvbnRlbnQiOlsiKGZ1bmN0aW9uIHdlYnBhY2tVbml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uKHJvb3QsIGZhY3RvcnkpIHtcblx0aWYodHlwZW9mIGV4cG9ydHMgPT09ICdvYmplY3QnICYmIHR5cGVvZiBtb2R1bGUgPT09ICdvYmplY3QnKVxuXHRcdG1vZHVsZS5leHBvcnRzID0gZmFjdG9yeSgpO1xuXHRlbHNlIGlmKHR5cGVvZiBkZWZpbmUgPT09ICdmdW5jdGlvbicgJiYgZGVmaW5lLmFtZClcblx0XHRkZWZpbmUoXCJ3aWRnZXRzL2pxdWVyeS1iYXItcmF0aW5nXCIsIFtdLCBmYWN0b3J5KTtcblx0ZWxzZSBpZih0eXBlb2YgZXhwb3J0cyA9PT0gJ29iamVjdCcpXG5cdFx0ZXhwb3J0c1tcIndpZGdldHMvanF1ZXJ5LWJhci1yYXRpbmdcIl0gPSBmYWN0b3J5KCk7XG5cdGVsc2Vcblx0XHRyb290W1wid2lkZ2V0cy9qcXVlcnktYmFyLXJhdGluZ1wiXSA9IGZhY3RvcnkoKTtcbn0pKHR5cGVvZiBzZWxmICE9PSAndW5kZWZpbmVkJyA/IHNlbGYgOiB0aGlzLCBmdW5jdGlvbigpIHtcbnJldHVybiBcblxuXG4vLyBXRUJQQUNLIEZPT1RFUiAvL1xuLy8gd2VicGFjay91bml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uIiwiIFx0Ly8gVGhlIG1vZHVsZSBjYWNoZVxuIFx0dmFyIGluc3RhbGxlZE1vZHVsZXMgPSB7fTtcblxuIFx0Ly8gVGhlIHJlcXVpcmUgZnVuY3Rpb25cbiBcdGZ1bmN0aW9uIF9fd2VicGFja19yZXF1aXJlX18obW9kdWxlSWQpIHtcblxuIFx0XHQvLyBDaGVjayBpZiBtb2R1bGUgaXMgaW4gY2FjaGVcbiBcdFx0aWYoaW5zdGFsbGVkTW9kdWxlc1ttb2R1bGVJZF0pIHtcbiBcdFx0XHRyZXR1cm4gaW5zdGFsbGVkTW9kdWxlc1ttb2R1bGVJZF0uZXhwb3J0cztcbiBcdFx0fVxuIFx0XHQvLyBDcmVhdGUgYSBuZXcgbW9kdWxlIChhbmQgcHV0IGl0IGludG8gdGhlIGNhY2hlKVxuIFx0XHR2YXIgbW9kdWxlID0gaW5zdGFsbGVkTW9kdWxlc1ttb2R1bGVJZF0gPSB7XG4gXHRcdFx0aTogbW9kdWxlSWQsXG4gXHRcdFx0bDogZmFsc2UsXG4gXHRcdFx0ZXhwb3J0czoge31cbiBcdFx0fTtcblxuIFx0XHQvLyBFeGVjdXRlIHRoZSBtb2R1bGUgZnVuY3Rpb25cbiBcdFx0bW9kdWxlc1ttb2R1bGVJZF0uY2FsbChtb2R1bGUuZXhwb3J0cywgbW9kdWxlLCBtb2R1bGUuZXhwb3J0cywgX193ZWJwYWNrX3JlcXVpcmVfXyk7XG5cbiBcdFx0Ly8gRmxhZyB0aGUgbW9kdWxlIGFzIGxvYWRlZFxuIFx0XHRtb2R1bGUubCA9IHRydWU7XG5cbiBcdFx0Ly8gUmV0dXJuIHRoZSBleHBvcnRzIG9mIHRoZSBtb2R1bGVcbiBcdFx0cmV0dXJuIG1vZHVsZS5leHBvcnRzO1xuIFx0fVxuXG5cbiBcdC8vIGV4cG9zZSB0aGUgbW9kdWxlcyBvYmplY3QgKF9fd2VicGFja19tb2R1bGVzX18pXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLm0gPSBtb2R1bGVzO1xuXG4gXHQvLyBleHBvc2UgdGhlIG1vZHVsZSBjYWNoZVxuIFx0X193ZWJwYWNrX3JlcXVpcmVfXy5jID0gaW5zdGFsbGVkTW9kdWxlcztcblxuIFx0Ly8gZGVmaW5lIGdldHRlciBmdW5jdGlvbiBmb3IgaGFybW9ueSBleHBvcnRzXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLmQgPSBmdW5jdGlvbihleHBvcnRzLCBuYW1lLCBnZXR0ZXIpIHtcbiBcdFx0aWYoIV9fd2VicGFja19yZXF1aXJlX18ubyhleHBvcnRzLCBuYW1lKSkge1xuIFx0XHRcdE9iamVjdC5kZWZpbmVQcm9wZXJ0eShleHBvcnRzLCBuYW1lLCB7XG4gXHRcdFx0XHRjb25maWd1cmFibGU6IGZhbHNlLFxuIFx0XHRcdFx0ZW51bWVyYWJsZTogdHJ1ZSxcbiBcdFx0XHRcdGdldDogZ2V0dGVyXG4gXHRcdFx0fSk7XG4gXHRcdH1cbiBcdH07XG5cbiBcdC8vIGdldERlZmF1bHRFeHBvcnQgZnVuY3Rpb24gZm9yIGNvbXBhdGliaWxpdHkgd2l0aCBub24taGFybW9ueSBtb2R1bGVzXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLm4gPSBmdW5jdGlvbihtb2R1bGUpIHtcbiBcdFx0dmFyIGdldHRlciA9IG1vZHVsZSAmJiBtb2R1bGUuX19lc01vZHVsZSA/XG4gXHRcdFx0ZnVuY3Rpb24gZ2V0RGVmYXVsdCgpIHsgcmV0dXJuIG1vZHVsZVsnZGVmYXVsdCddOyB9IDpcbiBcdFx0XHRmdW5jdGlvbiBnZXRNb2R1bGVFeHBvcnRzKCkgeyByZXR1cm4gbW9kdWxlOyB9O1xuIFx0XHRfX3dlYnBhY2tfcmVxdWlyZV9fLmQoZ2V0dGVyLCAnYScsIGdldHRlcik7XG4gXHRcdHJldHVybiBnZXR0ZXI7XG4gXHR9O1xuXG4gXHQvLyBPYmplY3QucHJvdG90eXBlLmhhc093blByb3BlcnR5LmNhbGxcbiBcdF9fd2VicGFja19yZXF1aXJlX18ubyA9IGZ1bmN0aW9uKG9iamVjdCwgcHJvcGVydHkpIHsgcmV0dXJuIE9iamVjdC5wcm90b3R5cGUuaGFzT3duUHJvcGVydHkuY2FsbChvYmplY3QsIHByb3BlcnR5KTsgfTtcblxuIFx0Ly8gX193ZWJwYWNrX3B1YmxpY19wYXRoX19cbiBcdF9fd2VicGFja19yZXF1aXJlX18ucCA9IFwiXCI7XG5cbiBcdC8vIExvYWQgZW50cnkgbW9kdWxlIGFuZCByZXR1cm4gZXhwb3J0c1xuIFx0cmV0dXJuIF9fd2VicGFja19yZXF1aXJlX18oX193ZWJwYWNrX3JlcXVpcmVfXy5zID0gNCk7XG5cblxuXG4vLyBXRUJQQUNLIEZPT1RFUiAvL1xuLy8gd2VicGFjay9ib290c3RyYXAgZjliMjRhYmM5ZjgwNTUwZmJkM2YiLCJmdW5jdGlvbiBpbml0KFN1cnZleSwgJCkge1xuICAkID0gJCB8fCB3aW5kb3cuJDtcbiAgdmFyIHdpZGdldCA9IHtcbiAgICBuYW1lOiBcImJhcnJhdGluZ1wiLFxuICAgIHRpdGxlOiBcIkJhciByYXRpbmdcIixcbiAgICBpY29uTmFtZTogXCJpY29uLWJhcnJhdGluZ1wiLFxuICAgIHdpZGdldElzTG9hZGVkOiBmdW5jdGlvbiAoKSB7XG4gICAgICByZXR1cm4gdHlwZW9mICQgPT0gXCJmdW5jdGlvblwiICYmICEhJC5mbi5iYXJyYXRpbmc7XG4gICAgfSxcbiAgICBkZWZhdWx0SlNPTjogeyBjaG9pY2VzOiBbMSwgMiwgMywgNCwgNV0gfSxcbiAgICBpc0ZpdDogZnVuY3Rpb24gKHF1ZXN0aW9uKSB7XG4gICAgICByZXR1cm4gcXVlc3Rpb24uZ2V0VHlwZSgpID09PSBcImJhcnJhdGluZ1wiO1xuICAgIH0sXG4gICAgaXNEZWZhdWx0UmVuZGVyOiB0cnVlLFxuICAgIGFjdGl2YXRlZEJ5Q2hhbmdlZDogZnVuY3Rpb24gKGFjdGl2YXRlZEJ5KSB7XG4gICAgICBTdXJ2ZXkuSnNvbk9iamVjdC5tZXRhRGF0YS5hZGRDbGFzcyhcbiAgICAgICAgXCJiYXJyYXRpbmdcIixcbiAgICAgICAgW1xuICAgICAgICAgIHsgbmFtZTogXCJoYXNPdGhlclwiLCB2aXNpYmxlOiBmYWxzZSB9LFxuICAgICAgICAgIHsgbmFtZTogXCJvdGhlclRleHRcIiwgdmlzaWJsZTogZmFsc2UgfSxcbiAgICAgICAgICB7IG5hbWU6IFwib3B0aW9uc0NhcHRpb25cIiwgdmlzaWJsZTogZmFsc2UgfSxcbiAgICAgICAgICB7IG5hbWU6IFwib3RoZXJFcnJvclRleHRcIiwgdmlzaWJsZTogZmFsc2UgfSxcbiAgICAgICAgICB7IG5hbWU6IFwic3RvcmVPdGhlcnNBc0NvbW1lbnRcIiwgdmlzaWJsZTogZmFsc2UgfSxcbiAgICAgICAgICB7IG5hbWU6IFwicmVuZGVyQXNcIiwgdmlzaWJsZTogZmFsc2UgfSxcbiAgICAgICAgICB7IG5hbWU6IFwic2VsZWN0MkNvbmZpZ1wiLCB2aXNpYmxlOiBmYWxzZSB9LFxuICAgICAgICBdLFxuICAgICAgICBudWxsLFxuICAgICAgICBcImRyb3Bkb3duXCJcbiAgICAgICk7XG4gICAgICBTdXJ2ZXkuSnNvbk9iamVjdC5tZXRhRGF0YS5hZGRQcm9wZXJ0eShcImJhcnJhdGluZ1wiLCB7XG4gICAgICAgIG5hbWU6IFwic2hvd1ZhbHVlczpib29sZWFuXCIsXG4gICAgICAgIGRlZmF1bHQ6IGZhbHNlLFxuICAgICAgICBjYXRlZ29yeTogXCJnZW5lcmFsXCIsXG4gICAgICB9KTtcbiAgICAgIFN1cnZleS5Kc29uT2JqZWN0Lm1ldGFEYXRhLmFkZFByb3BlcnR5KFwiYmFycmF0aW5nXCIsIHtcbiAgICAgICAgbmFtZTogXCJyYXRpbmdUaGVtZVwiLFxuICAgICAgICBjYXRlZ29yeTogXCJnZW5lcmFsXCIsXG4gICAgICAgIGRlZmF1bHQ6IFwiY3NzLXN0YXJzXCIsXG4gICAgICAgIGNob2ljZXM6IFtcbiAgICAgICAgICBcImZvbnRhd2Vzb21lLXN0YXJzXCIsXG4gICAgICAgICAgXCJjc3Mtc3RhcnNcIixcbiAgICAgICAgICBcImJhcnMtcGlsbFwiLFxuICAgICAgICAgIFwiYmFycy0xdG8xMFwiLFxuICAgICAgICAgIFwiYmFycy1tb3ZpZVwiLFxuICAgICAgICAgIFwiYmFycy1yZXZlcnNlZFwiLFxuICAgICAgICAgIFwiYmFycy1ob3Jpem9udGFsXCIsXG4gICAgICAgICAgXCJmb250YXdlc29tZS1zdGFycy1vXCIsXG4gICAgICAgIF0sXG4gICAgICB9KTtcbiAgICB9LFxuICAgIGFmdGVyUmVuZGVyOiBmdW5jdGlvbiAocXVlc3Rpb24sIGVsKSB7XG4gICAgICB2YXIgJGN1c3RvbVNlbGVjdDtcbiAgICAgIHZhciAkcXVlc3Rpb25JbnB1dDtcbiAgICAgIHZhciBjb250ZW50Q29udGFpbmVyID0gJChlbCkuaXMoXCJzZWxlY3RcIilcbiAgICAgICAgPyAkKGVsKS5wYXJlbnQoKS5wYXJlbnQoKVswXVxuICAgICAgICA6ICQoZWwpLnBhcmVudCgpWzBdO1xuICAgICAgdmFyIHJlbmRlckN1c3RvbVNlbGVjdCA9IGZ1bmN0aW9uICgpIHtcbiAgICAgICAgJGN1c3RvbVNlbGVjdCA9ICQoXCI8c2VsZWN0IGNsYXNzPSdzdi13aWRnZXQtc2VsZWN0Jz48L3NlbGVjdD5cIik7XG4gICAgICAgIHF1ZXN0aW9uLnZpc2libGVDaG9pY2VzLmZvckVhY2goZnVuY3Rpb24gKGNob2ljZSkge1xuICAgICAgICAgICRjdXN0b21TZWxlY3QuYXBwZW5kKFxuICAgICAgICAgICAgJzxvcHRpb24gdmFsdWU9XCJ7MH1cIj57MX08L29wdGlvbj4nLmZvcm1hdChjaG9pY2UudmFsdWUsIGNob2ljZS50ZXh0KVxuICAgICAgICAgICk7XG4gICAgICAgIH0pO1xuICAgICAgICAkcXVlc3Rpb25JbnB1dCA9ICQoY29udGVudENvbnRhaW5lcikuZmluZChcbiAgICAgICAgICAnW2lkPVwiezB9XCJdJy5mb3JtYXQocXVlc3Rpb24uaW5wdXRJZClcbiAgICAgICAgKTtcblxuICAgICAgICAkcXVlc3Rpb25JbnB1dC5jc3MoXCJkaXNwbGF5XCIsIFwibm9uZVwiKTtcbiAgICAgICAgJHF1ZXN0aW9uSW5wdXQuYWZ0ZXIoJGN1c3RvbVNlbGVjdCk7XG4gICAgICAgICRjdXN0b21TZWxlY3RbMF0uc2VsZWN0ZWRJbmRleCA9IC0xO1xuICAgICAgfTtcbiAgICAgIHZhciByZW1vdmVDdXN0b21TZWxlY3QgPSBmdW5jdGlvbiAoKSB7XG4gICAgICAgICRxdWVzdGlvbklucHV0LmNzcyhcImRpc3BsYXlcIiwgXCJcIik7XG4gICAgICAgICRjdXN0b21TZWxlY3QuYmFycmF0aW5nKFwiZGVzdHJveVwiKTtcbiAgICAgICAgJGN1c3RvbVNlbGVjdC5yZW1vdmUoKTtcbiAgICAgIH07XG4gICAgICB2YXIgcmVuZGVyQmFycmF0aW5nID0gZnVuY3Rpb24gKCkge1xuICAgICAgICAkY3VzdG9tU2VsZWN0LmJhcnJhdGluZyhcInNob3dcIiwge1xuICAgICAgICAgIHRoZW1lOiBxdWVzdGlvbi5yYXRpbmdUaGVtZSxcbiAgICAgICAgICBpbml0aWFsUmF0aW5nOiBxdWVzdGlvbi52YWx1ZSxcbiAgICAgICAgICBzaG93VmFsdWVzOiBxdWVzdGlvbi5zaG93VmFsdWVzLFxuICAgICAgICAgIHNob3dTZWxlY3RlZFJhdGluZzogZmFsc2UsXG4gICAgICAgICAgcmVhZG9ubHk6IHF1ZXN0aW9uLmlzUmVhZE9ubHksXG4gICAgICAgICAgb25TZWxlY3Q6IGZ1bmN0aW9uICh2YWx1ZSwgdGV4dCkge1xuICAgICAgICAgICAgdmFsdWVDaGFuZ2luZ0J5V2lkZ2V0ID0gdHJ1ZTtcbiAgICAgICAgICAgIHF1ZXN0aW9uLnZhbHVlID0gdmFsdWU7XG4gICAgICAgICAgICB2YWx1ZUNoYW5naW5nQnlXaWRnZXQgPSBmYWxzZTtcbiAgICAgICAgICB9LFxuICAgICAgICB9KTtcbiAgICAgIH07XG4gICAgICByZW5kZXJDdXN0b21TZWxlY3QoKTtcbiAgICAgIHJlbmRlckJhcnJhdGluZygpO1xuICAgICAgaWYgKCEhJGN1c3RvbVNlbGVjdC5wYXJlbnRzKClbMF0pXG4gICAgICAgICRjdXN0b21TZWxlY3QucGFyZW50cygpWzBdLnN0eWxlLm1hcmdpbkJvdHRvbSA9IFwiM3B4XCI7XG4gICAgICB2YXIgdmFsdWVDaGFuZ2luZ0J5V2lkZ2V0ID0gZmFsc2U7XG5cbiAgICAgIHF1ZXN0aW9uLnZhbHVlQ2hhbmdlZENhbGxiYWNrID0gZnVuY3Rpb24gKCkge1xuICAgICAgICBpZiAoXG4gICAgICAgICAgIXZhbHVlQ2hhbmdpbmdCeVdpZGdldCAmJlxuICAgICAgICAgICQoY29udGVudENvbnRhaW5lcikuZmluZChcInNlbGVjdC5zdi13aWRnZXQtc2VsZWN0XCIpWzBdLnZhbHVlICE9PVxuICAgICAgICAgICAgcXVlc3Rpb24udmFsdWVcbiAgICAgICAgKSB7XG4gICAgICAgICAgJChjb250ZW50Q29udGFpbmVyKVxuICAgICAgICAgICAgLmZpbmQoXCJzZWxlY3Quc3Ytd2lkZ2V0LXNlbGVjdFwiKVxuICAgICAgICAgICAgLmJhcnJhdGluZyhcInNldFwiLCBxdWVzdGlvbi52YWx1ZSk7XG4gICAgICAgIH1cbiAgICAgIH07XG4gICAgICBxdWVzdGlvbi5fX2JhcnJhdGluZ09uUHJvcGVydHlDaGFuZ2VkQ2FsbGJhY2sgPSBmdW5jdGlvbiAoXG4gICAgICAgIHNlbmRlcixcbiAgICAgICAgb3B0aW9uc1xuICAgICAgKSB7XG4gICAgICAgIGlmIChvcHRpb25zLm5hbWUgPT0gXCJyYXRpbmdUaGVtZVwiKSB7XG4gICAgICAgICAgJGN1c3RvbVNlbGVjdC5iYXJyYXRpbmcoXCJkZXN0cm95XCIpO1xuICAgICAgICAgIHJlbmRlckJhcnJhdGluZygpO1xuICAgICAgICB9XG4gICAgICB9O1xuICAgICAgcXVlc3Rpb24ub25Qcm9wZXJ0eUNoYW5nZWQuYWRkKFxuICAgICAgICBxdWVzdGlvbi5fX2JhcnJhdGluZ09uUHJvcGVydHlDaGFuZ2VkQ2FsbGJhY2tcbiAgICAgICk7XG4gICAgICBxdWVzdGlvbi5yZWFkT25seUNoYW5nZWRDYWxsYmFjayA9IGZ1bmN0aW9uICgpIHtcbiAgICAgICAgcmVtb3ZlQ3VzdG9tU2VsZWN0KCk7XG4gICAgICAgIHJlbmRlckN1c3RvbVNlbGVjdCgpO1xuICAgICAgICByZW5kZXJCYXJyYXRpbmcoKTtcbiAgICAgIH07XG4gICAgICBxdWVzdGlvbi52aXNpYmxlQ2hvaWNlc0NoYW5nZWRDYWxsYmFjayA9IGZ1bmN0aW9uICgpIHtcbiAgICAgICAgcmVuZGVyQmFycmF0aW5nKCk7XG4gICAgICB9O1xuICAgIH0sXG4gICAgd2lsbFVubW91bnQ6IGZ1bmN0aW9uIChxdWVzdGlvbiwgZWwpIHtcbiAgICAgIHZhciAkY29udGVudENvbnRhaW5lciA9ICQoZWwpLmlzKFwic2VsZWN0XCIpXG4gICAgICAgID8gJChlbCkucGFyZW50KCkucGFyZW50KClcbiAgICAgICAgOiAkKGVsKS5wYXJlbnQoKTtcbiAgICAgIHZhciAkZWwgPSAkY29udGVudENvbnRhaW5lci5maW5kKFwic2VsZWN0LnN2LXdpZGdldC1zZWxlY3RcIik7XG4gICAgICAkZWwuYmFycmF0aW5nKFwiZGVzdHJveVwiKTtcbiAgICAgICRlbC5yZW1vdmUoKTtcbiAgICAgIHF1ZXN0aW9uLnZhbHVlQ2hhbmdlZENhbGxiYWNrID0gdW5kZWZpbmVkO1xuICAgICAgcXVlc3Rpb24ub25Qcm9wZXJ0eUNoYW5nZWQucmVtb3ZlKFxuICAgICAgICBxdWVzdGlvbi5fX2JhcnJhdGluZ09uUHJvcGVydHlDaGFuZ2VkQ2FsbGJhY2tcbiAgICAgICk7XG4gICAgICBxdWVzdGlvbi5fX2JhcnJhdGluZ09uUHJvcGVydHlDaGFuZ2VkQ2FsbGJhY2sgPSB1bmRlZmluZWQ7XG4gICAgfSxcbiAgICBwZGZRdWVzdGlvblR5cGU6IFwiZHJvcGRvd25cIixcbiAgfTtcblxuICBTdXJ2ZXkuQ3VzdG9tV2lkZ2V0Q29sbGVjdGlvbi5JbnN0YW5jZS5hZGRDdXN0b21XaWRnZXQod2lkZ2V0LCBcImN1c3RvbXR5cGVcIik7XG59XG5cbmlmICh0eXBlb2YgU3VydmV5ICE9PSBcInVuZGVmaW5lZFwiKSB7XG4gIGluaXQoU3VydmV5LCB3aW5kb3cuJCk7XG59XG5cbmV4cG9ydCBkZWZhdWx0IGluaXQ7XG5cblxuXG4vLy8vLy8vLy8vLy8vLy8vLy9cbi8vIFdFQlBBQ0sgRk9PVEVSXG4vLyAuL3NyYy9qcXVlcnktYmFyLXJhdGluZy5qc1xuLy8gbW9kdWxlIGlkID0gNFxuLy8gbW9kdWxlIGNodW5rcyA9IDAgMTAiXSwic291cmNlUm9vdCI6IiJ9