Twitter Bootstrap Wizard
============================

This Twitter Bootstrap plugin builds a wizard using a formatted tabbable structure. It allows to build a wizard functionality using buttons to go through the different wizard steps and using events allows to hook into each step individually.

##### Note
 We will not longer be updating the bower version of bootstrap wizard or any other package manager. See <a href="https://github.com/VinceG/twitter-bootstrap-wizard/issues/81#issuecomment-42909528">#81</a> for the reason why. Please use 'master' branch instead.

<a href="http://vadimg.com/twitter-bootstrap-wizard-example/" target="_blank">Website & Demo</a>

<a href='https://twitter.com/gabrielva' target='_blank'>Follow @gabrielva</a>

Requirements
-------------

* Requires jQuery v1.3.2 or later
* Bootstrap 3.x

Install
-------------
```
bower install twitter-bootstrap-wizard --save 
```

Code Examples
-------------

```javascript
//basic wizard
$(document).ready(function() {
	$('#rootwizard').bootstrapWizard();
});
```

```javascript
//wizard with options and events
$(document).ready(function() {
	$('#rootwizard').bootstrapWizard({
		tabClass: 'nav nav-pills',
		onNext: function(tab, navigation, index) {
			alert('next');
  		}
  });
});
```

```javascript
//calling a method
$('#rootwizard').bootstrapWizard('show',3);
```

Options
-------
<table class="table table-bordered table-striped">
	<thead>
		<tr>
			<th>Key</th>
			<th>Default</th>
			<th>Description</th>
		</tr>
	</thead>
	<tbody>
		<tr>
			<td>tabClass</td>
			<td>'nav nav-pills'</td>
			<td>ul navigation class</td>
		</tr>
		<tr>
			<td>nextSelector</td>
			<td>'.wizard li.next'</td>
			<td>next element selector</td>
		</tr>
		<tr>
			<td>previousSelector</td>
			<td>'.wizard li.previous'</td>
			<td>previous element selector</td>
		</tr>
		<tr>
			<td>firstSelector</td>
			<td>'.wizard li.first'</td>
			<td>first element selector</td>
		</tr>
		<tr>
			<td>lastSelector</td>
			<td>'.wizard li.last'</td>
			<td>last element selector</td>
		</tr>
		<tr>
			<td>backSelector</td>
			<td>'.wizard li.back'</td>
			<td>back element selector</td>
		</tr>
		<tr>
			<td>finishSelector</td>
			<td>'.wizard li.finish'</td>
			<td>finish element selector</td>
		</tr>
	</tbody>
</table>

Events
------
<table class="table table-bordered table-striped">
	<thead>
		<tr>
			<th>Key</th>
			<th>Description</th>
		</tr>
	</thead>
	<tbody>
		<tr>
			<td>onInit</td>
			<td>Fired when plugin is initialized</td>
		</tr>
		<tr>
			<td>onShow</td>
			<td>Fired when plugin data is shown</td>
		</tr>
		<tr>
			<td>onNext</td>
			<td>Fired when next button is clicked (return false to disable moving to the next step)</td>
		</tr>
		<tr>
			<td>onPrevious</td>
			<td>Fired when previous button is clicked (return false to disable moving to the previous step)</td>
		</tr>
		<tr>
			<td>onFirst</td>
			<td>Fired when first button is clicked (return false to disable moving to the first step)</td>
		</tr>
		<tr>
			<td>onLast</td>
			<td>Fired when last button is clicked (return false to disable moving to the last step)</td>
		</tr>
		<tr>
			<td>onBack</td>
			<td>Fired when back button is clicked (return false to disable moving backwards in navigation history)</td>
		</tr>
		<tr>
			<td>onFinish</td>
			<td>Fired when finish button is clicked (return value is irrelevant)</td>
		</tr>
		<tr>
			<td>onTabChange</td>
			<td>Fired when a tab is changed (return false to disable moving to that tab and showing its contents)</td>
		</tr>
		<tr>
			<td>onTabClick</td>
			<td>Fired when a tab is clicked (return false to disable moving to that tab and showing its contents)</td>
		</tr>
		<tr>
			<td>onTabShow</td>
			<td>Fired when a tab content is shown (return false to disable showing that tab content)</td>
		</tr>
	</tbody>
</table>

Methods
-------
<table class="table table-bordered table-striped">
	<thead>
		<tr>
			<th>Key</th>
			<th>Parameters</th>
			<th>Description</th>
		</tr>
	</thead>
	<tbody>
		<tr>
			<td>next</td>
			<td></td>
			<td>Moves to the next tab</td>
		</tr>
		<tr>
			<td>previous</td>
			<td></td>
			<td>Moves to the previous tab</td>
		</tr>
		<tr>
			<td>first</td>
			<td></td>
			<td>Jumps to the first tab</td>
		</tr>
		<tr>
			<td>last</td>
			<td></td>
			<td>Jumps to the last tab</td>
		</tr>
		<tr>
			<td>back</td>
			<td></td>
			<td>Moves back in navigation history by jumping to the former tab</td>
		</tr>
		<tr>
			<td>finish</td>
			<td></td>
			<td>"Finishes" the wizard by calling onFinish callback</td>
		</tr>
		<tr>
			<td>show</td>
			<td>zero based index or tab target id</td>
			<td>Jumps to the specified tab</td>
		</tr>
		<tr>
			<td>currentIndex</td>
			<td></td>
			<td>Returns the zero based index number for the current tab</td>
		</tr>
		<tr>
			<td>navigationLength</td>
			<td></td>
			<td>Returns the number of tabs</td>
		</tr>
		<tr>
			<td>enable</td>
			<td>zero based index</td>
			<td>Enables a tab, allows a user to click it (removes .disabled if the item has that class)</td>
		</tr>
		<tr>
			<td>disable</td>
			<td>zero based index</td>
			<td>Disables a tab, disallows a user to click it (adds .disabled to the li element)</td>
		</tr>
		<tr>
			<td>display</td>
			<td>zero based index</td>
			<td>Displays the li element if it was previously hidden</td>
		</tr>
		<tr>
			<td>hide</td>
			<td>zero based index</td>
			<td>Hides the li element (will not remove it from the DOM)</td>
		</tr>
		<tr>
			<td>remove</td>
			<td>zero based index, optinal bool remove tab-pane element or not false by default</td>
			<td>Removes the li element from the DOM if second argument is true will also remove the tab-pane element</td>
		</tr>
	</tbody>
</table>

<p>&copy; <a href='http://vadimg.com' target="_blank">Vadim Vincent Gabriel</a> <a href='https://twitter.com/gabrielva' target='_blank'>Follow @gabrielva</a> 2012</p>

License
===============
The MIT License (MIT)

Copyright (c) 2013 - Vincent Gabriel & Jason Gill

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
