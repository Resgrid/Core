"use strict";

var _interopRequireWildcard = require("@babel/runtime/helpers/interopRequireWildcard");

var _interopRequireDefault = require("@babel/runtime/helpers/interopRequireDefault");

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports["default"] = exports.styles = exports.MTableToolbar = void 0;

var _objectSpread2 = _interopRequireDefault(require("@babel/runtime/helpers/objectSpread"));

var _classCallCheck2 = _interopRequireDefault(require("@babel/runtime/helpers/classCallCheck"));

var _createClass2 = _interopRequireDefault(require("@babel/runtime/helpers/createClass"));

var _possibleConstructorReturn2 = _interopRequireDefault(require("@babel/runtime/helpers/possibleConstructorReturn"));

var _getPrototypeOf2 = _interopRequireDefault(require("@babel/runtime/helpers/getPrototypeOf"));

var _assertThisInitialized2 = _interopRequireDefault(require("@babel/runtime/helpers/assertThisInitialized"));

var _inherits2 = _interopRequireDefault(require("@babel/runtime/helpers/inherits"));

var _defineProperty2 = _interopRequireDefault(require("@babel/runtime/helpers/defineProperty"));

var _core = require("@material-ui/core");

var _colorManipulator = require("@material-ui/core/styles/colorManipulator");

var _classnames = _interopRequireDefault(require("classnames"));

var _filefy = require("filefy");

var _propTypes = _interopRequireWildcard(require("prop-types"));

var React = _interopRequireWildcard(require("react"));

/* eslint-disable no-unused-vars */

/* eslint-enable no-unused-vars */
var MTableToolbar =
/*#__PURE__*/
function (_React$Component) {
  (0, _inherits2["default"])(MTableToolbar, _React$Component);

  function MTableToolbar(props) {
    var _this;

    (0, _classCallCheck2["default"])(this, MTableToolbar);
    _this = (0, _possibleConstructorReturn2["default"])(this, (0, _getPrototypeOf2["default"])(MTableToolbar).call(this, props));
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "defaultExportCsv", function () {
      var columns = _this.props.columns.filter(function (columnDef) {
        return !columnDef.hidden && columnDef.field && columnDef["export"] !== false;
      });

      var dataToExport = _this.props.exportAllData ? _this.props.data : _this.props.renderData;
      var data = dataToExport.map(function (rowData) {
        return columns.map(function (columnDef) {
          return _this.props.getFieldValue(rowData, columnDef);
        });
      });
      var builder = new _filefy.CsvBuilder((_this.props.exportFileName || _this.props.title || 'data') + '.csv');
      builder.setDelimeter(_this.props.exportDelimiter).setColumns(columns.map(function (columnDef) {
        return columnDef.title;
      })).addRows(data).exportFile();
    });
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "exportCsv", function () {
      if (_this.props.exportCsv) {
        _this.props.exportCsv(_this.props.columns, _this.props.data);
      } else {
        _this.defaultExportCsv();
      }

      _this.setState({
        exportButtonAnchorEl: null
      });
    });
    _this.state = {
      columnsButtonAnchorEl: null,
      exportButtonAnchorEl: null
    };
    return _this;
  }

  (0, _createClass2["default"])(MTableToolbar, [{
    key: "renderSearch",
    value: function renderSearch() {
      var _this2 = this;

      var localization = (0, _objectSpread2["default"])({}, MTableToolbar.defaultProps.localization, this.props.localization);

      if (this.props.search) {
        return React.createElement(_core.TextField, {
          className: this.props.searchFieldAlignment === 'left' && this.props.showTitle === false ? null : this.props.classes.searchField,
          value: this.props.searchText,
          onChange: function onChange(event) {
            return _this2.props.onSearchChanged(event.target.value);
          },
          placeholder: localization.searchPlaceholder,
          color: "inherit",
          InputProps: {
            startAdornment: React.createElement(_core.InputAdornment, {
              position: "start"
            }, React.createElement(_core.Tooltip, {
              title: localization.searchTooltip
            }, React.createElement(this.props.icons.Search, {
              color: "inherit",
              fontSize: "small"
            }))),
            endAdornment: React.createElement(_core.InputAdornment, {
              position: "end"
            }, React.createElement(_core.IconButton, {
              disabled: !this.props.searchText,
              onClick: function onClick() {
                return _this2.props.onSearchChanged("");
              }
            }, React.createElement(this.props.icons.ResetSearch, {
              color: "inherit",
              fontSize: "small"
            }))),
            style: this.props.searchFieldStyle
          }
        });
      } else {
        return null;
      }
    }
  }, {
    key: "renderDefaultActions",
    value: function renderDefaultActions() {
      var _this3 = this;

      var localization = (0, _objectSpread2["default"])({}, MTableToolbar.defaultProps.localization, this.props.localization);
      return React.createElement("div", null, this.props.columnsButton && React.createElement("span", null, React.createElement(_core.Tooltip, {
        title: localization.showColumnsTitle
      }, React.createElement(_core.IconButton, {
        color: "inherit",
        onClick: function onClick(event) {
          return _this3.setState({
            columnsButtonAnchorEl: event.currentTarget
          });
        },
        "aria-label": localization.showColumnsAriaLabel
      }, React.createElement(this.props.icons.ViewColumn, null))), React.createElement(_core.Menu, {
        anchorEl: this.state.columnsButtonAnchorEl,
        open: Boolean(this.state.columnsButtonAnchorEl),
        onClose: function onClose() {
          return _this3.setState({
            columnsButtonAnchorEl: null
          });
        }
      }, React.createElement(_core.MenuItem, {
        key: "text",
        disabled: true,
        style: {
          opacity: 1,
          fontWeight: 600,
          fontSize: 12
        }
      }, localization.addRemoveColumns), this.props.columns.map(function (col, index) {
        return React.createElement(_core.MenuItem, {
          key: col.tableData.id,
          disabled: col.removable === false
        }, React.createElement(_core.FormControlLabel, {
          label: col.title,
          control: React.createElement(_core.Checkbox, {
            checked: !col.hidden,
            onChange: function onChange(event, checked) {
              _this3.props.onColumnsChanged(col.tableData.id, !checked);
            }
          })
        }));
      }))), this.props.exportButton && React.createElement("span", null, React.createElement(_core.Tooltip, {
        title: localization.exportTitle
      }, React.createElement(_core.IconButton, {
        color: "inherit",
        onClick: function onClick(event) {
          return _this3.setState({
            exportButtonAnchorEl: event.currentTarget
          });
        },
        "aria-label": localization.exportAriaLabel
      }, React.createElement(this.props.icons.Export, null))), React.createElement(_core.Menu, {
        anchorEl: this.state.exportButtonAnchorEl,
        open: Boolean(this.state.exportButtonAnchorEl),
        onClose: function onClose() {
          return _this3.setState({
            exportButtonAnchorEl: null
          });
        }
      }, React.createElement(_core.MenuItem, {
        key: "export-csv",
        onClick: this.exportCsv
      }, localization.exportName))), React.createElement(this.props.components.Actions, {
        actions: this.props.actions && this.props.actions.filter(function (a) {
          return a.isFreeAction;
        }),
        components: this.props.components
      }));
    }
  }, {
    key: "renderSelectedActions",
    value: function renderSelectedActions() {
      return React.createElement(React.Fragment, null, React.createElement(this.props.components.Actions, {
        actions: this.props.actions.filter(function (a) {
          return !a.isFreeAction;
        }),
        data: this.props.selectedRows,
        components: this.props.components
      }));
    }
  }, {
    key: "renderActions",
    value: function renderActions() {
      var classes = this.props.classes;
      return React.createElement("div", {
        className: classes.actions
      }, React.createElement("div", null, this.props.selectedRows && this.props.selectedRows.length > 0 ? this.renderSelectedActions() : this.renderDefaultActions()));
    }
  }, {
    key: "render",
    value: function render() {
      var classes = this.props.classes;
      var localization = (0, _objectSpread2["default"])({}, MTableToolbar.defaultProps.localization, this.props.localization);
      var title = this.props.showTextRowsSelected && this.props.selectedRows && this.props.selectedRows.length > 0 ? localization.nRowsSelected.replace('{0}', this.props.selectedRows.length) : this.props.showTitle ? this.props.title : null;
      return React.createElement(_core.Toolbar, {
        className: (0, _classnames["default"])(classes.root, (0, _defineProperty2["default"])({}, classes.highlight, this.props.showTextRowsSelected && this.props.selectedRows && this.props.selectedRows.length > 0))
      }, title && React.createElement("div", {
        className: classes.title
      }, React.createElement(_core.Typography, {
        variant: "h6"
      }, title)), this.props.searchFieldAlignment === 'left' && this.renderSearch(), this.props.toolbarButtonAlignment === 'left' && this.renderActions(), React.createElement("div", {
        className: classes.spacer
      }), this.props.searchFieldAlignment === 'right' && this.renderSearch(), this.props.toolbarButtonAlignment === 'right' && this.renderActions());
    }
  }]);
  return MTableToolbar;
}(React.Component);

exports.MTableToolbar = MTableToolbar;
MTableToolbar.defaultProps = {
  actions: [],
  columns: [],
  columnsButton: false,
  localization: {
    addRemoveColumns: 'Add or remove columns',
    nRowsSelected: '{0} row(s) selected',
    showColumnsTitle: 'Show Columns',
    showColumnsAriaLabel: 'Show Columns',
    exportTitle: 'Export',
    exportAriaLabel: 'Export',
    exportName: 'Export as CSV',
    searchTooltip: 'Search',
    searchPlaceholder: 'Search'
  },
  search: true,
  showTitle: true,
  showTextRowsSelected: true,
  toolbarButtonAlignment: 'right',
  searchFieldAlignment: 'right',
  searchText: '',
  selectedRows: [],
  title: 'No Title!'
};
MTableToolbar.propTypes = {
  actions: _propTypes["default"].array,
  columns: _propTypes["default"].array,
  columnsButton: _propTypes["default"].bool,
  components: _propTypes["default"].object.isRequired,
  getFieldValue: _propTypes["default"].func.isRequired,
  localization: _propTypes["default"].object.isRequired,
  onColumnsChanged: _propTypes["default"].func.isRequired,
  onSearchChanged: _propTypes["default"].func.isRequired,
  search: _propTypes["default"].bool.isRequired,
  searchFieldStyle: _propTypes["default"].object,
  searchText: _propTypes["default"].string.isRequired,
  selectedRows: _propTypes["default"].array,
  title: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].string]),
  showTitle: _propTypes["default"].bool.isRequired,
  showTextRowsSelected: _propTypes["default"].bool.isRequired,
  toolbarButtonAlignment: _propTypes["default"].string.isRequired,
  searchFieldAlignment: _propTypes["default"].string.isRequired,
  renderData: _propTypes["default"].array,
  data: _propTypes["default"].array,
  exportAllData: _propTypes["default"].bool,
  exportButton: _propTypes["default"].bool,
  exportDelimiter: _propTypes["default"].string,
  exportFileName: _propTypes["default"].string,
  exportCsv: _propTypes["default"].func,
  classes: _propTypes["default"].object
};

var styles = function styles(theme) {
  return {
    root: {
      paddingRight: theme.spacing.unit
    },
    highlight: theme.palette.type === 'light' ? {
      color: theme.palette.secondary.main,
      backgroundColor: (0, _colorManipulator.lighten)(theme.palette.secondary.light, 0.85)
    } : {
      color: theme.palette.text.primary,
      backgroundColor: theme.palette.secondary.dark
    },
    spacer: {
      flex: '1 1 10%'
    },
    actions: {
      color: theme.palette.text.secondary
    },
    title: {
      flex: '0 0 auto'
    },
    searchField: {
      paddingLeft: theme.spacing.unit * 2
    }
  };
};

exports.styles = styles;

var _default = (0, _core.withStyles)(styles)(MTableToolbar);

exports["default"] = _default;