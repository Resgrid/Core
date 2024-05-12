/*! Bulma integration for DataTables' SearchPanes
 * © SpryMedia Ltd - datatables.net/license
 */

(function( factory ){
	if ( typeof define === 'function' && define.amd ) {
		// AMD
		define( ['jquery', 'datatables.net-bm', 'datatables.net-searchpanes'], function ( $ ) {
			return factory( $, window, document );
		} );
	}
	else if ( typeof exports === 'object' ) {
		// CommonJS
		var jq = require('jquery');
		var cjsRequires = function (root, $) {
			if ( ! $.fn.dataTable ) {
				require('datatables.net-bm')(root, $);
			}

			if ( ! $.fn.dataTable.SearchPanes ) {
				require('datatables.net-searchpanes')(root, $);
			}
		};

		if (typeof window === 'undefined') {
			module.exports = function (root, $) {
				if ( ! root ) {
					// CommonJS environments without a window global must pass a
					// root. This will give an error otherwise
					root = window;
				}

				if ( ! $ ) {
					$ = jq( root );
				}

				cjsRequires( root, $ );
				return factory( $, root, root.document );
			};
		}
		else {
			cjsRequires( window, jq );
			module.exports = factory( jq, window, window.document );
		}
	}
	else {
		// Browser
		factory( jQuery, window, document );
	}
}(function( $, window, document, undefined ) {
'use strict';
var DataTable = $.fn.dataTable;


$.extend(true, DataTable.SearchPane.classes, {
    disabledButton: 'is-disabled',
    paneButton: 'button dtsp-paneButton is-white',
    search: 'input search'
});
$.extend(true, DataTable.SearchPanes.classes, {
    clearAll: 'dtsp-clearAll button',
    collapseAll: 'dtsp-collapseAll button',
    disabledButton: 'is-disabled',
    search: DataTable.SearchPane.classes.search,
    showAll: 'dtsp-showAll button'
});


return DataTable;
}));
