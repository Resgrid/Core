"use strict";

var _interopRequireDefault = require("@babel/runtime/helpers/interopRequireDefault");

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports.propTypes = void 0;

var _propTypes = _interopRequireDefault(require("prop-types"));

var propTypes = {
  actions: _propTypes["default"].arrayOf(_propTypes["default"].oneOfType([_propTypes["default"].func, _propTypes["default"].shape({
    icon: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func, _propTypes["default"].string]).isRequired,
    isFreeAction: _propTypes["default"].bool,
    tooltip: _propTypes["default"].string,
    onClick: _propTypes["default"].func.isRequired,
    iconProps: _propTypes["default"].object
  })])),
  columns: _propTypes["default"].arrayOf(_propTypes["default"].shape({
    cellStyle: _propTypes["default"].oneOfType([_propTypes["default"].object, _propTypes["default"].func]),
    currencySetting: _propTypes["default"].shape({
      locale: _propTypes["default"].string,
      currencyCode: _propTypes["default"].string,
      minimumFractionDigits: _propTypes["default"].number,
      maximumFractionDigits: _propTypes["default"].number
    }),
    customFilterAndSearch: _propTypes["default"].func,
    customSort: _propTypes["default"].func,
    defaultFilter: _propTypes["default"].any,
    defaultSort: _propTypes["default"].oneOf(['asc', 'desc']),
    editComponent: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    emptyValue: _propTypes["default"].oneOfType([_propTypes["default"].string, _propTypes["default"].node, _propTypes["default"].func]),
    "export": _propTypes["default"].bool,
    field: _propTypes["default"].string,
    filtering: _propTypes["default"].bool,
    filterCellStyle: _propTypes["default"].object,
    grouping: _propTypes["default"].bool,
    headerStyle: _propTypes["default"].object,
    hidden: _propTypes["default"].bool,
    lookup: _propTypes["default"].object,
    editable: _propTypes["default"].oneOf(['always', 'onUpdate', 'onAdd', 'never']),
    removable: _propTypes["default"].bool,
    render: _propTypes["default"].func,
    searchable: _propTypes["default"].bool,
    sorting: _propTypes["default"].bool,
    title: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].string]),
    type: _propTypes["default"].oneOf(['string', 'boolean', 'numeric', 'date', 'datetime', 'time', 'currency'])
  })).isRequired,
  components: _propTypes["default"].shape({
    Action: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Actions: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Body: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Cell: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Container: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    EditField: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    EditRow: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    FilterRow: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Groupbar: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    GroupRow: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Header: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    OverlayLoading: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Pagination: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Row: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Toolbar: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func])
  }),
  data: _propTypes["default"].oneOfType([_propTypes["default"].arrayOf(_propTypes["default"].object), _propTypes["default"].func]).isRequired,
  editable: _propTypes["default"].shape({
    onRowAdd: _propTypes["default"].func,
    onRowUpdate: _propTypes["default"].func,
    onRowDelete: _propTypes["default"].func
  }),
  detailPanel: _propTypes["default"].oneOfType([_propTypes["default"].func, _propTypes["default"].arrayOf(_propTypes["default"].oneOfType([_propTypes["default"].func, _propTypes["default"].shape({
    disabled: _propTypes["default"].bool,
    icon: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func, _propTypes["default"].string]),
    openIcon: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func, _propTypes["default"].string]),
    tooltip: _propTypes["default"].string,
    render: _propTypes["default"].func.isRequired
  })]))]),
  icons: _propTypes["default"].shape({
    Add: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Check: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Clear: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Delete: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    DetailPanel: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Edit: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Export: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Filter: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    FirstPage: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    LastPage: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    NextPage: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    PreviousPage: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    ResetSearch: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    Search: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    SortArrow: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    ThirdStateCheck: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func]),
    ViewColumn: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].func])
  }),
  isLoading: _propTypes["default"].bool,
  title: _propTypes["default"].oneOfType([_propTypes["default"].element, _propTypes["default"].string]),
  options: _propTypes["default"].shape({
    actionsCellStyle: _propTypes["default"].object,
    actionsColumnIndex: _propTypes["default"].number,
    addRowPosition: _propTypes["default"].oneOf(['first', 'last']),
    columnsButton: _propTypes["default"].bool,
    defaultExpanded: _propTypes["default"].bool,
    debounceInterval: _propTypes["default"].number,
    detailPanelType: _propTypes["default"].oneOf(['single', 'multiple']),
    doubleHorizontalScroll: _propTypes["default"].bool,
    emptyRowsWhenPaging: _propTypes["default"].bool,
    exportAllData: _propTypes["default"].bool,
    exportButton: _propTypes["default"].bool,
    exportDelimiter: _propTypes["default"].string,
    exportFileName: _propTypes["default"].string,
    exportCsv: _propTypes["default"].func,
    filtering: _propTypes["default"].bool,
    filterCellStyle: _propTypes["default"].object,
    header: _propTypes["default"].bool,
    headerStyle: _propTypes["default"].object,
    initialPage: _propTypes["default"].number,
    maxBodyHeight: _propTypes["default"].oneOfType([_propTypes["default"].number, _propTypes["default"].string]),
    loadingType: _propTypes["default"].oneOf(['overlay', 'linear']),
    paging: _propTypes["default"].bool,
    pageSize: _propTypes["default"].number,
    pageSizeOptions: _propTypes["default"].arrayOf(_propTypes["default"].number),
    paginationType: _propTypes["default"].oneOf(['normal', 'stepped']),
    rowStyle: _propTypes["default"].oneOfType([_propTypes["default"].object, _propTypes["default"].func]),
    showEmptyDataSourceMessage: _propTypes["default"].bool,
    showSelectAllCheckbox: _propTypes["default"].bool,
    search: _propTypes["default"].bool,
    showTitle: _propTypes["default"].bool,
    showTextRowsSelected: _propTypes["default"].bool,
    toolbarButtonAlignment: _propTypes["default"].oneOf(['left', 'right']),
    searchFieldAlignment: _propTypes["default"].oneOf(['left', 'right']),
    searchFieldStyle: _propTypes["default"].object,
    selection: _propTypes["default"].bool,
    sorting: _propTypes["default"].bool,
    toolbar: _propTypes["default"].bool
  }),
  localization: _propTypes["default"].shape({
    grouping: _propTypes["default"].shape({
      groupedBy: _propTypes["default"].string,
      placeholder: _propTypes["default"].string
    }),
    pagination: _propTypes["default"].object,
    toolbar: _propTypes["default"].object,
    header: _propTypes["default"].object,
    body: _propTypes["default"].object
  }),
  initialFormData: _propTypes["default"].object,
  onSelectionChange: _propTypes["default"].func,
  onChangeRowsPerPage: _propTypes["default"].func,
  onChangePage: _propTypes["default"].func,
  onOrderChange: _propTypes["default"].func,
  onRowClick: _propTypes["default"].func,
  onTreeExpandChange: _propTypes["default"].func,
  tableRef: _propTypes["default"].any
};
exports.propTypes = propTypes;