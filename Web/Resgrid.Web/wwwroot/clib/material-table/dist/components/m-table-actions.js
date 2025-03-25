"use strict";

var _interopRequireWildcard = require("@babel/runtime/helpers/interopRequireWildcard");

var _interopRequireDefault = require("@babel/runtime/helpers/interopRequireDefault");

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports["default"] = void 0;

var _classCallCheck2 = _interopRequireDefault(require("@babel/runtime/helpers/classCallCheck"));

var _createClass2 = _interopRequireDefault(require("@babel/runtime/helpers/createClass"));

var _possibleConstructorReturn2 = _interopRequireDefault(require("@babel/runtime/helpers/possibleConstructorReturn"));

var _getPrototypeOf2 = _interopRequireDefault(require("@babel/runtime/helpers/getPrototypeOf"));

var _inherits2 = _interopRequireDefault(require("@babel/runtime/helpers/inherits"));

var React = _interopRequireWildcard(require("react"));

var _propTypes = _interopRequireDefault(require("prop-types"));

/* eslint-disable no-unused-vars */

/* eslint-enable no-unused-vars */
var MTableActions =
/*#__PURE__*/
function (_React$Component) {
  (0, _inherits2["default"])(MTableActions, _React$Component);

  function MTableActions() {
    (0, _classCallCheck2["default"])(this, MTableActions);
    return (0, _possibleConstructorReturn2["default"])(this, (0, _getPrototypeOf2["default"])(MTableActions).apply(this, arguments));
  }

  (0, _createClass2["default"])(MTableActions, [{
    key: "render",
    value: function render() {
      var _this = this;

      if (this.props.actions) {
        return this.props.actions.map(function (action, index) {
          return React.createElement(_this.props.components.Action, {
            action: action,
            key: "action-" + index,
            data: _this.props.data
          });
        });
      }

      return null;
    }
  }]);
  return MTableActions;
}(React.Component);

MTableActions.defaultProps = {
  actions: [],
  data: {}
};
MTableActions.propTypes = {
  components: _propTypes["default"].object.isRequired,
  actions: _propTypes["default"].array.isRequired,
  data: _propTypes["default"].oneOfType([_propTypes["default"].object, _propTypes["default"].arrayOf(_propTypes["default"].object)])
};
var _default = MTableActions;
exports["default"] = _default;