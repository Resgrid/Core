"use strict";

var _interopRequireWildcard = require("@babel/runtime/helpers/interopRequireWildcard");

var _interopRequireDefault = require("@babel/runtime/helpers/interopRequireDefault");

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports.defaultProps = void 0;

var _react = _interopRequireDefault(require("react"));

var _core = require("@material-ui/core");

var MComponents = _interopRequireWildcard(require("./components"));

var _propTypes = _interopRequireDefault(require("prop-types"));

var _colorManipulator = require("@material-ui/core/styles/colorManipulator");

var OverlayLoading = function OverlayLoading(props) {
  return _react["default"].createElement("div", {
    style: {
      display: 'table',
      width: '100%',
      height: '100%',
      backgroundColor: (0, _colorManipulator.fade)(props.theme.palette.background.paper, 0.7)
    }
  }, _react["default"].createElement("div", {
    style: {
      display: 'table-cell',
      width: '100%',
      height: '100%',
      verticalAlign: 'middle',
      textAlign: 'center'
    }
  }, _react["default"].createElement(_core.CircularProgress, null)));
};

OverlayLoading.propTypes = {
  theme: _propTypes["default"].any
};

var Container = function Container(props) {
  return _react["default"].createElement(_core.Paper, props);
};

var defaultProps = {
  actions: [],
  classes: {},
  columns: [],
  components: {
    Action: MComponents.MTableAction,
    Actions: MComponents.MTableActions,
    Body: MComponents.MTableBody,
    Cell: MComponents.MTableCell,
    Container: Container,
    EditField: MComponents.MTableEditField,
    EditRow: MComponents.MTableEditRow,
    FilterRow: MComponents.MTableFilterRow,
    Groupbar: MComponents.MTableGroupbar,
    GroupRow: MComponents.MTableGroupRow,
    Header: MComponents.MTableHeader,
    OverlayLoading: OverlayLoading,
    Pagination: _core.TablePagination,
    Row: MComponents.MTableBodyRow,
    Toolbar: MComponents.MTableToolbar
  },
  data: [],
  icons: {
    /* eslint-disable react/display-name */
    Add: function Add(props) {
      return _react["default"].createElement(_core.Icon, props, "add_box");
    },
    Check: function Check(props) {
      return _react["default"].createElement(_core.Icon, props, "check");
    },
    Clear: function Clear(props) {
      return _react["default"].createElement(_core.Icon, props, "clear");
    },
    Delete: function Delete(props) {
      return _react["default"].createElement(_core.Icon, props, "delete_outline");
    },
    DetailPanel: function DetailPanel(props) {
      return _react["default"].createElement(_core.Icon, props, "chevron_right");
    },
    Edit: function Edit(props) {
      return _react["default"].createElement(_core.Icon, props, "edit");
    },
    Export: function Export(props) {
      return _react["default"].createElement(_core.Icon, props, "save_alt");
    },
    Filter: function Filter(props) {
      return _react["default"].createElement(_core.Icon, props, "filter_list");
    },
    FirstPage: function FirstPage(props) {
      return _react["default"].createElement(_core.Icon, props, "first_page");
    },
    LastPage: function LastPage(props) {
      return _react["default"].createElement(_core.Icon, props, "last_page");
    },
    NextPage: function NextPage(props) {
      return _react["default"].createElement(_core.Icon, props, "chevron_right");
    },
    PreviousPage: function PreviousPage(props) {
      return _react["default"].createElement(_core.Icon, props, "chevron_left");
    },
    ResetSearch: function ResetSearch(props) {
      return _react["default"].createElement(_core.Icon, props, "clear");
    },
    Search: function Search(props) {
      return _react["default"].createElement(_core.Icon, props, "search");
    },
    SortArrow: function SortArrow(props) {
      return _react["default"].createElement(_core.Icon, props, "arrow_upward");
    },
    ThirdStateCheck: function ThirdStateCheck(props) {
      return _react["default"].createElement(_core.Icon, props, "remove");
    },
    ViewColumn: function ViewColumn(props) {
      return _react["default"].createElement(_core.Icon, props, "view_column");
    }
    /* eslint-enable react/display-name */

  },
  isLoading: false,
  title: 'Table Title',
  options: {
    actionsColumnIndex: 0,
    addRowPosition: 'last',
    columnsButton: false,
    detailPanelType: 'multiple',
    debounceInterval: 200,
    doubleHorizontalScroll: false,
    emptyRowsWhenPaging: true,
    exportAllData: false,
    exportButton: false,
    exportDelimiter: ',',
    filtering: false,
    header: true,
    loadingType: 'overlay',
    paging: true,
    pageSize: 5,
    pageSizeOptions: [5, 10, 20],
    paginationType: 'normal',
    showEmptyDataSourceMessage: true,
    showSelectAllCheckbox: true,
    search: true,
    showTitle: true,
    showTextRowsSelected: true,
    toolbarButtonAlignment: 'right',
    searchFieldAlignment: 'right',
    searchFieldStyle: {},
    selection: false,
    sorting: true,
    toolbar: true,
    defaultExpanded: false,
    detailPanelColumnAlignment: 'left'
  },
  localization: {
    grouping: {
      groupedBy: 'Grouped By:',
      placeholder: 'Drag headers here to group by'
    },
    pagination: {
      labelDisplayedRows: '{from}-{to} of {count}',
      labelRowsPerPage: 'Rows per page:',
      labelRowsSelect: 'rows'
    },
    toolbar: {},
    header: {},
    body: {
      filterRow: {},
      editRow: {
        saveTooltip: 'Save',
        cancelTooltip: 'Cancel',
        deleteText: 'Are you sure delete this row?'
      },
      addTooltip: 'Add',
      deleteTooltip: 'Delete',
      editTooltip: 'Edit'
    }
  }
};
exports.defaultProps = defaultProps;