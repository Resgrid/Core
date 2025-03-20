"use strict";

var _interopRequireWildcard = require("@babel/runtime/helpers/interopRequireWildcard");

var _interopRequireDefault = require("@babel/runtime/helpers/interopRequireDefault");

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports["default"] = void 0;

var _objectSpread2 = _interopRequireDefault(require("@babel/runtime/helpers/objectSpread"));

var _classCallCheck2 = _interopRequireDefault(require("@babel/runtime/helpers/classCallCheck"));

var _createClass2 = _interopRequireDefault(require("@babel/runtime/helpers/createClass"));

var _possibleConstructorReturn2 = _interopRequireDefault(require("@babel/runtime/helpers/possibleConstructorReturn"));

var _getPrototypeOf3 = _interopRequireDefault(require("@babel/runtime/helpers/getPrototypeOf"));

var _assertThisInitialized2 = _interopRequireDefault(require("@babel/runtime/helpers/assertThisInitialized"));

var _inherits2 = _interopRequireDefault(require("@babel/runtime/helpers/inherits"));

var _defineProperty2 = _interopRequireDefault(require("@babel/runtime/helpers/defineProperty"));

var _core = require("@material-ui/core");

var _propTypes = _interopRequireDefault(require("prop-types"));

var React = _interopRequireWildcard(require("react"));

/* eslint-disable no-unused-vars */

/* eslint-enable no-unused-vars */
var MTablePaginationInner =
/*#__PURE__*/
function (_React$Component) {
  (0, _inherits2["default"])(MTablePaginationInner, _React$Component);

  function MTablePaginationInner() {
    var _getPrototypeOf2;

    var _this;

    (0, _classCallCheck2["default"])(this, MTablePaginationInner);

    for (var _len = arguments.length, args = new Array(_len), _key = 0; _key < _len; _key++) {
      args[_key] = arguments[_key];
    }

    _this = (0, _possibleConstructorReturn2["default"])(this, (_getPrototypeOf2 = (0, _getPrototypeOf3["default"])(MTablePaginationInner)).call.apply(_getPrototypeOf2, [this].concat(args)));
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "handleFirstPageButtonClick", function (event) {
      _this.props.onChangePage(event, 0);
    });
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "handleBackButtonClick", function (event) {
      _this.props.onChangePage(event, _this.props.page - 1);
    });
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "handleNextButtonClick", function (event) {
      _this.props.onChangePage(event, _this.props.page + 1);
    });
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "handleNumberButtonClick", function (number) {
      return function (event) {
        _this.props.onChangePage(event, number);
      };
    });
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "handleLastPageButtonClick", function (event) {
      _this.props.onChangePage(event, Math.max(0, Math.ceil(_this.props.count / _this.props.rowsPerPage) - 1));
    });
    return _this;
  }

  (0, _createClass2["default"])(MTablePaginationInner, [{
    key: "renderPagesButton",
    value: function renderPagesButton(start, end) {
      var buttons = [];

      for (var p = start; p <= end; p++) {
        var buttonVariant = p === this.props.page ? "contained" : "default";
        buttons.push(React.createElement(_core.Button, {
          size: "small",
          style: {
            boxShadow: 'none',
            maxWidth: '30px',
            maxHeight: '30px',
            minWidth: '30px',
            minHeight: '30px'
          },
          disabled: p === this.props.page,
          variant: buttonVariant,
          onClick: this.handleNumberButtonClick(p)
        }, p + 1));
      }

      return React.createElement("span", null, buttons);
    }
  }, {
    key: "render",
    value: function render() {
      var _this$props = this.props,
          classes = _this$props.classes,
          count = _this$props.count,
          page = _this$props.page,
          rowsPerPage = _this$props.rowsPerPage;
      var localization = (0, _objectSpread2["default"])({}, MTablePaginationInner.defaultProps.localization, this.props.localization);
      var maxPages = Math.ceil(count / rowsPerPage) - 1;
      var pageStart = Math.max(page - 1, 0);
      var pageEnd = Math.min(maxPages, page + 1);
      return React.createElement("div", {
        className: classes.root
      }, React.createElement(_core.Tooltip, {
        title: localization.previousTooltip
      }, React.createElement("span", null, React.createElement(_core.IconButton, {
        onClick: this.handleBackButtonClick,
        disabled: page === 0,
        "aria-label": localization.previousAriaLabel
      }, React.createElement(this.props.icons.PreviousPage, null)))), React.createElement(_core.Hidden, {
        smDown: true
      }, this.renderPagesButton(pageStart, pageEnd)), React.createElement(_core.Tooltip, {
        title: localization.nextTooltip
      }, React.createElement("span", null, React.createElement(_core.IconButton, {
        onClick: this.handleNextButtonClick,
        disabled: page >= maxPages,
        "aria-label": localization.nextAriaLabel
      }, React.createElement(this.props.icons.NextPage, null)))));
    }
  }]);
  return MTablePaginationInner;
}(React.Component);

var actionsStyles = function actionsStyles(theme) {
  return {
    root: {
      flexShrink: 0,
      color: theme.palette.text.secondary,
      marginLeft: theme.spacing.unit * 2.5
    }
  };
};

MTablePaginationInner.propTypes = {
  onChangePage: _propTypes["default"].func,
  page: _propTypes["default"].number,
  count: _propTypes["default"].number,
  rowsPerPage: _propTypes["default"].number,
  classes: _propTypes["default"].object,
  localization: _propTypes["default"].object
};
MTablePaginationInner.defaultProps = {
  localization: {
    previousTooltip: 'Previous Page',
    nextTooltip: 'Next Page',
    labelDisplayedRows: '{from}-{to} of {count}',
    labelRowsPerPage: 'Rows per page:'
  }
};
var MTablePagination = (0, _core.withStyles)(actionsStyles, {
  withTheme: true
})(MTablePaginationInner);
var _default = MTablePagination;
exports["default"] = _default;