"use strict";

var _interopRequireWildcard = require("@babel/runtime/helpers/interopRequireWildcard");

var _interopRequireDefault = require("@babel/runtime/helpers/interopRequireDefault");

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports["default"] = void 0;

var _toConsumableArray2 = _interopRequireDefault(require("@babel/runtime/helpers/toConsumableArray"));

var _extends2 = _interopRequireDefault(require("@babel/runtime/helpers/extends"));

var _objectWithoutProperties2 = _interopRequireDefault(require("@babel/runtime/helpers/objectWithoutProperties"));

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
var MTableBodyRow =
/*#__PURE__*/
function (_React$Component) {
  (0, _inherits2["default"])(MTableBodyRow, _React$Component);

  function MTableBodyRow() {
    var _getPrototypeOf2;

    var _this;

    (0, _classCallCheck2["default"])(this, MTableBodyRow);

    for (var _len = arguments.length, args = new Array(_len), _key = 0; _key < _len; _key++) {
      args[_key] = arguments[_key];
    }

    _this = (0, _possibleConstructorReturn2["default"])(this, (_getPrototypeOf2 = (0, _getPrototypeOf3["default"])(MTableBodyRow)).call.apply(_getPrototypeOf2, [this].concat(args)));
    (0, _defineProperty2["default"])((0, _assertThisInitialized2["default"])(_this), "rotateIconStyle", function (isOpen) {
      return {
        transform: isOpen ? 'rotate(90deg)' : 'none'
      };
    });
    return _this;
  }

  (0, _createClass2["default"])(MTableBodyRow, [{
    key: "renderColumns",
    value: function renderColumns() {
      var _this2 = this;

      var mapArr = this.props.columns.filter(function (columnDef) {
        return !columnDef.hidden && !(columnDef.tableData.groupOrder > -1);
      }).sort(function (a, b) {
        return a.tableData.columnOrder - b.tableData.columnOrder;
      }).map(function (columnDef, index) {
        var value = _this2.props.getFieldValue(_this2.props.data, columnDef);

        return React.createElement(_this2.props.components.Cell, {
          icons: _this2.props.icons,
          columnDef: columnDef,
          value: value,
          key: "cell-" + _this2.props.data.tableData.di + "-" + columnDef.tableData.id,
          rowData: _this2.props.data
        });
      });
      return mapArr;
    }
  }, {
    key: "renderActions",
    value: function renderActions() {
      var _this3 = this;

      var actions = this.props.actions.filter(function (a) {
        return !a.isFreeAction && !_this3.props.options.selection;
      });
      return React.createElement(_core.TableCell, {
        padding: "none",
        key: "key-actions-column",
        style: (0, _objectSpread2["default"])({
          width: 48 * actions.length,
          padding: '0px 5px'
        }, this.props.options.actionsCellStyle)
      }, React.createElement("div", {
        style: {
          display: 'flex'
        }
      }, React.createElement(this.props.components.Actions, {
        data: this.props.data,
        actions: actions,
        components: this.props.components
      })));
    }
  }, {
    key: "renderSelectionColumn",
    value: function renderSelectionColumn() {
      var _this4 = this;

      return React.createElement(_core.TableCell, {
        padding: "none",
        key: "key-selection-column",
        style: {
          width: 48 + 12 * (this.props.treeDataMaxLevel - 1)
        }
      }, React.createElement(_core.Checkbox, {
        checked: this.props.data.tableData.checked === true,
        onClick: function onClick(e) {
          return e.stopPropagation();
        },
        value: this.props.data.tableData.id.toString(),
        onChange: function onChange(event) {
          return _this4.props.onRowSelected(event, _this4.props.path, _this4.props.data);
        },
        style: {
          paddingLeft: 12 + this.props.level * 12
        }
      }));
    }
  }, {
    key: "renderDetailPanelColumn",
    value: function renderDetailPanelColumn() {
      var _this5 = this;

      var CustomIcon = function CustomIcon(_ref) {
        var icon = _ref.icon,
            style = _ref.style;
        return typeof icon === "string" ? React.createElement(_core.Icon, {
          style: style
        }, icon) : React.createElement(icon, {
          style: style
        });
      };

      if (typeof this.props.detailPanel == 'function') {
        return React.createElement(_core.TableCell, {
          padding: "none",
          key: "key-detail-panel-column",
          style: {
            width: 48,
            textAlign: 'center'
          }
        }, React.createElement(_core.IconButton, {
          style: (0, _objectSpread2["default"])({
            transition: 'all ease 200ms'
          }, this.rotateIconStyle(this.props.data.tableData.showDetailPanel)),
          onClick: function onClick(event) {
            _this5.props.onToggleDetailPanel(_this5.props.path, _this5.props.detailPanel);

            event.stopPropagation();
          }
        }, React.createElement(this.props.icons.DetailPanel, null)));
      } else {
        return React.createElement(_core.TableCell, {
          padding: "none",
          key: "key-detail-panel-column"
        }, React.createElement("div", {
          style: {
            width: 48 * this.props.detailPanel.length,
            textAlign: 'center',
            display: 'inline-block'
          }
        }, this.props.detailPanel.map(function (panel, index) {
          if (typeof panel === "function") {
            panel = panel(_this5.props.data);
          }

          var isOpen = (_this5.props.data.tableData.showDetailPanel || '').toString() === panel.render.toString();
          var iconButton = React.createElement(_this5.props.icons.DetailPanel, null);
          var animation = true;

          if (isOpen) {
            if (panel.openIcon) {
              iconButton = React.createElement(CustomIcon, {
                icon: panel.openIcon
              });
              animation = false;
            } else if (panel.icon) {
              iconButton = React.createElement(CustomIcon, {
                icon: panel.icon
              });
            }
          } else if (panel.icon) {
            iconButton = React.createElement(CustomIcon, {
              icon: panel.icon
            });
            animation = false;
          }

          iconButton = React.createElement(_core.IconButton, {
            key: "key-detail-panel-" + index,
            style: (0, _objectSpread2["default"])({
              transition: 'all ease 200ms'
            }, _this5.rotateIconStyle(animation && isOpen)),
            disabled: panel.disabled,
            onClick: function onClick(event) {
              _this5.props.onToggleDetailPanel(_this5.props.path, panel.render);

              event.stopPropagation();
            }
          }, iconButton);

          if (panel.tooltip) {
            iconButton = React.createElement(_core.Tooltip, {
              key: "key-detail-panel-" + index,
              title: panel.tooltip
            }, iconButton);
          }

          return iconButton;
        })));
      }
    }
  }, {
    key: "getStyle",
    value: function getStyle() {
      var style = {
        transition: 'all ease 300ms'
      };

      if (typeof this.props.options.rowStyle === "function") {
        style = (0, _objectSpread2["default"])({}, style, this.props.options.rowStyle(this.props.data));
      } else if (this.props.options.rowStyle) {
        style = (0, _objectSpread2["default"])({}, style, this.props.options.rowStyle);
      }

      if (this.props.onRowClick) {
        style.cursor = 'pointer';
      }

      if (this.props.hasAnyEditingRow) {
        style.opacity = 0.2;
      }

      return style;
    }
  }, {
    key: "render",
    value: function render() {
      var _this6 = this;

      var renderColumns = this.renderColumns();

      if (this.props.options.selection) {
        renderColumns.splice(0, 0, this.renderSelectionColumn());
      }

      if (this.props.actions && this.props.actions.filter(function (a) {
        return !a.isFreeAction && !_this6.props.options.selection;
      }).length > 0) {
        if (this.props.options.actionsColumnIndex === -1) {
          renderColumns.push(this.renderActions());
        } else if (this.props.options.actionsColumnIndex >= 0) {
          var endPos = 0;

          if (this.props.options.selection) {
            endPos = 1;
          }

          renderColumns.splice(this.props.options.actionsColumnIndex + endPos, 0, this.renderActions());
        }
      }

      if (this.props.isTreeData) {
        if (this.props.data.tableData.childRows && this.props.data.tableData.childRows.length > 0) {
          renderColumns.splice(0, 0, React.createElement(_core.TableCell, {
            padding: "none",
            key: "key-tree-data-column",
            style: {
              width: 48 + 12 * (this.props.treeDataMaxLevel - 2)
            }
          }, React.createElement(_core.IconButton, {
            style: (0, _objectSpread2["default"])({
              transition: 'all ease 200ms',
              marginLeft: this.props.level * 12
            }, this.rotateIconStyle(this.props.data.tableData.isTreeExpanded)),
            onClick: function onClick(event) {
              _this6.props.onTreeExpandChanged(_this6.props.path, _this6.props.data);

              event.stopPropagation();
            }
          }, React.createElement(this.props.icons.DetailPanel, null))));
        } else {
          renderColumns.splice(0, 0, React.createElement(_core.TableCell, {
            padding: "none",
            key: "key-tree-data-column"
          }));
        }
      } // Lastly we add detail panel icon


      if (this.props.detailPanel) {
        if (this.props.options.detailPanelColumnAlignment === 'right') {
          renderColumns.push(this.renderDetailPanelColumn());
        } else {
          renderColumns.splice(0, 0, this.renderDetailPanelColumn());
        }
      }

      this.props.columns.filter(function (columnDef) {
        return columnDef.tableData.groupOrder > -1;
      }).forEach(function (columnDef) {
        renderColumns.splice(0, 0, React.createElement(_core.TableCell, {
          padding: "none",
          key: "key-group-cell" + columnDef.tableData.id
        }));
      });
      var _this$props = this.props,
          icons = _this$props.icons,
          data = _this$props.data,
          columns = _this$props.columns,
          components = _this$props.components,
          detailPanel = _this$props.detailPanel,
          getFieldValue = _this$props.getFieldValue,
          isTreeData = _this$props.isTreeData,
          onRowClick = _this$props.onRowClick,
          onRowSelected = _this$props.onRowSelected,
          onTreeExpandChanged = _this$props.onTreeExpandChanged,
          onToggleDetailPanel = _this$props.onToggleDetailPanel,
          onEditingCanceled = _this$props.onEditingCanceled,
          onEditingApproved = _this$props.onEditingApproved,
          options = _this$props.options,
          hasAnyEditingRow = _this$props.hasAnyEditingRow,
          treeDataMaxLevel = _this$props.treeDataMaxLevel,
          rowProps = (0, _objectWithoutProperties2["default"])(_this$props, ["icons", "data", "columns", "components", "detailPanel", "getFieldValue", "isTreeData", "onRowClick", "onRowSelected", "onTreeExpandChanged", "onToggleDetailPanel", "onEditingCanceled", "onEditingApproved", "options", "hasAnyEditingRow", "treeDataMaxLevel"]);
      return React.createElement(React.Fragment, null, React.createElement(_core.TableRow, (0, _extends2["default"])({
        selected: hasAnyEditingRow
      }, rowProps, {
        hover: onRowClick ? true : false,
        style: this.getStyle(),
        onClick: function onClick(event) {
          onRowClick && onRowClick(event, _this6.props.data, function (panelIndex) {
            var panel = detailPanel;

            if (Array.isArray(panel)) {
              panel = panel[panelIndex || 0].render;
            }

            onToggleDetailPanel(_this6.props.path, panel);
          });
        }
      }), renderColumns), this.props.data.tableData.childRows && this.props.data.tableData.isTreeExpanded && this.props.data.tableData.childRows.map(function (data, index) {
        if (data.tableData.editing) {
          return React.createElement(_this6.props.components.EditRow, {
            columns: _this6.props.columns.filter(function (columnDef) {
              return !columnDef.hidden;
            }),
            components: _this6.props.components,
            data: data,
            icons: _this6.props.icons,
            localization: _this6.props.localization,
            key: index,
            mode: data.tableData.editing,
            options: _this6.props.options,
            isTreeData: _this6.props.isTreeData,
            detailPanel: _this6.props.detailPanel,
            onEditingCanceled: onEditingCanceled,
            onEditingApproved: onEditingApproved
          });
        } else {
          return React.createElement(_this6.props.components.Row, (0, _extends2["default"])({}, _this6.props, {
            data: data,
            index: index,
            key: index,
            level: _this6.props.level + 1,
            path: [].concat((0, _toConsumableArray2["default"])(_this6.props.path), [index]),
            onEditingCanceled: onEditingCanceled,
            onEditingApproved: onEditingApproved,
            hasAnyEditingRow: _this6.props.hasAnyEditingRow,
            treeDataMaxLevel: treeDataMaxLevel
          }));
        }
      }), this.props.data.tableData && this.props.data.tableData.showDetailPanel && React.createElement(_core.TableRow // selected={this.props.index % 2 === 0}
      , null, React.createElement(_core.TableCell, {
        colSpan: renderColumns.length,
        padding: "none"
      }, this.props.data.tableData.showDetailPanel(this.props.data))));
    }
  }]);
  return MTableBodyRow;
}(React.Component);

exports["default"] = MTableBodyRow;
MTableBodyRow.defaultProps = {
  actions: [],
  index: 0,
  data: {},
  options: {},
  path: []
};
MTableBodyRow.propTypes = {
  actions: _propTypes["default"].array,
  icons: _propTypes["default"].any.isRequired,
  index: _propTypes["default"].number.isRequired,
  data: _propTypes["default"].object.isRequired,
  detailPanel: _propTypes["default"].oneOfType([_propTypes["default"].func, _propTypes["default"].arrayOf(_propTypes["default"].oneOfType([_propTypes["default"].object, _propTypes["default"].func]))]),
  hasAnyEditingRow: _propTypes["default"].bool,
  options: _propTypes["default"].object.isRequired,
  onRowSelected: _propTypes["default"].func,
  path: _propTypes["default"].arrayOf(_propTypes["default"].number),
  treeDataMaxLevel: _propTypes["default"].number,
  getFieldValue: _propTypes["default"].func.isRequired,
  columns: _propTypes["default"].array,
  onToggleDetailPanel: _propTypes["default"].func.isRequired,
  onRowClick: _propTypes["default"].func,
  onEditingApproved: _propTypes["default"].func,
  onEditingCanceled: _propTypes["default"].func
};