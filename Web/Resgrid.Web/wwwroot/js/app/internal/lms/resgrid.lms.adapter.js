/*
 * Resgrid LMS SCORM runtime adapter (Training T5). Injected by /lms-content/{token}/ into every HTML page of a SCORM package, which runs in a
 * sandboxed frame with an opaque origin. It defines window.API (SCORM 1.2) or window.API_1484_11 (SCORM 2004) in the SCO's own window, answers
 * GetValue from the values the launch started with (window.RG_LMS, written by __rg/config.js), and commits what the SCO set to the launch's
 * commit URL (text/plain JSON, no cookies; the token in the URL is the authority). The server validates every value again.
 */
(function (window) {
    'use strict';

    var config = window.RG_LMS || { version: '1.2', values: {}, commitUrl: null };
    var is2004 = config.version === '2004';
    var values = {};
    var dirty = {};
    var dirtyCount = 0;
    var state = 0; // 0 not initialized, 1 running, 2 finished
    var lastError = '0';
    var key;

    for (key in (config.values || {})) {
        if (Object.prototype.hasOwnProperty.call(config.values, key)) {
            values[key] = String(config.values[key]);
        }
    }

    var errors12 = {
        '0': 'No error', '101': 'General exception', '201': 'Invalid argument error', '202': 'Element cannot have children',
        '203': 'Element not an array. Cannot have count.', '301': 'Not initialized', '401': 'Not implemented error',
        '402': 'Invalid set value, element is a keyword', '403': 'Element is read only', '404': 'Element is write only', '405': 'Incorrect Data Type'
    };
    var errors2004 = {
        '0': 'No Error', '101': 'General Exception', '102': 'General Initialization Failure', '103': 'Already Initialized',
        '104': 'Content Instance Terminated', '111': 'General Termination Failure', '112': 'Termination Before Initialization',
        '113': 'Termination After Termination', '122': 'Retrieve Data Before Initialization', '123': 'Retrieve Data After Termination',
        '132': 'Store Data Before Initialization', '133': 'Store Data After Termination', '142': 'Commit Before Initialization',
        '143': 'Commit After Termination', '201': 'General Argument Error', '301': 'General Get Failure', '351': 'General Set Failure',
        '391': 'General Commit Failure', '401': 'Undefined Data Model Element', '402': 'Unimplemented Data Model Element',
        '403': 'Data Model Element Value Not Initialized', '404': 'Data Model Element Is Read Only', '405': 'Data Model Element Is Write Only',
        '406': 'Data Model Element Type Mismatch', '407': 'Data Model Element Value Out Of Range', '408': 'Data Model Dependency Not Established'
    };

    var readOnly12 = /^cmi\.(core\.(student_id|student_name|credit|entry|lesson_mode|total_time|_children)|launch_data|student_data\..*|objectives\._count|interactions\._count|_version|.*\._children|.*\._count)$/;
    var writeOnly12 = /^cmi\.(core\.(exit|session_time)|interactions\.\d+\.(id|objectives\.\d+\.id|time|type|correct_responses\.\d+\.pattern|weighting|student_response|result|latency))$/;
    var known12 = /^cmi\.(core\.|suspend_data$|launch_data$|comments$|comments_from_lms$|objectives\.|student_data\.|student_preference\.|interactions\.|_version$)/;
    var readOnly2004 = /^cmi\.(_version|learner_id|learner_name|credit|entry|mode|total_time|launch_data|scaled_passing_score|completion_threshold|max_time_allowed|time_limit_action|comments_from_lms\..*|.*\._children|.*\._count)$/;
    var writeOnly2004 = /^cmi\.(exit|session_time)$/;
    var known2004 = /^(cmi\.|adl\.nav\.)/;

    var children12 = {
        'cmi.core._children': 'student_id,student_name,lesson_location,credit,lesson_status,entry,score,total_time,lesson_mode,exit,session_time',
        'cmi.core.score._children': 'raw,min,max',
        'cmi.objectives._children': 'id,score,status',
        'cmi.student_data._children': 'mastery_score,max_time_allowed,time_limit_action',
        'cmi.student_preference._children': 'audio,language,speed,text',
        'cmi.interactions._children': 'id,objectives,time,type,correct_responses,weighting,student_response,result,latency'
    };
    var children2004 = {
        'cmi.score._children': 'scaled,raw,min,max',
        'cmi.objectives._children': 'id,score,success_status,completion_status,progress_measure,description',
        'cmi.learner_preference._children': 'audio_level,language,delivery_speed,audio_captioning',
        'cmi.interactions._children': 'id,type,objectives,timestamp,correct_responses,weighting,learner_response,result,latency,description',
        'cmi.comments_from_learner._children': 'comment,location,timestamp',
        'cmi.comments_from_lms._children': 'comment,location,timestamp'
    };

    function setError(code) {
        lastError = String(code);
    }

    function count(prefix) {
        var max = -1, name, rest, dot, n;
        for (name in values) {
            if (Object.prototype.hasOwnProperty.call(values, name) && name.indexOf(prefix) === 0) {
                rest = name.substring(prefix.length);
                dot = rest.indexOf('.');
                n = parseInt(dot >= 0 ? rest.substring(0, dot) : rest, 10);
                if (!isNaN(n) && n > max) {
                    max = n;
                }
            }
        }
        return max + 1;
    }

    function send(body, beacon) {
        if (!config.commitUrl) {
            return;
        }
        try {
            if (beacon && window.navigator && window.navigator.sendBeacon && body.length < 60000) {
                if (window.navigator.sendBeacon(config.commitUrl, new window.Blob([body], { type: 'text/plain' }))) {
                    return;
                }
            }
            if (window.fetch) {
                window.fetch(config.commitUrl, {
                    method: 'POST', body: body, headers: { 'Content-Type': 'text/plain' }, credentials: 'omit', mode: 'cors',
                    keepalive: body.length < 60000
                })['catch'](function () { });
                return;
            }
            var xhr = new window.XMLHttpRequest();
            xhr.open('POST', config.commitUrl, true);
            xhr.setRequestHeader('Content-Type', 'text/plain');
            xhr.send(body);
        } catch (e) {
            // The next commit carries the values again only if they change; the server keeps what it already accepted.
        }
    }

    function commit(finish, beacon) {
        if (dirtyCount === 0 && !finish) {
            return;
        }
        var body = JSON.stringify({ values: dirty, finish: !!finish });
        dirty = {};
        dirtyCount = 0;
        send(body, beacon);
    }

    function getValue(element) {
        element = String(element || '');
        if (state === 0) { setError(is2004 ? 122 : 301); return ''; }
        if (state === 2 && is2004) { setError(123); return ''; }
        if (element === '') { setError(is2004 ? 301 : 201); return ''; }
        var children = is2004 ? children2004 : children12;
        if (Object.prototype.hasOwnProperty.call(children, element)) { setError(0); return children[element]; }
        if (/\._count$/.test(element)) {
            var prefix = element.substring(0, element.length - '_count'.length);
            if (prefix === 'cmi.objectives.' || prefix === 'cmi.interactions.' || prefix === 'cmi.comments_from_learner.' || prefix === 'cmi.comments_from_lms.' ||
                /^cmi\.interactions\.\d+\.(objectives|correct_responses)\.$/.test(prefix)) {
                setError(0);
                return String(count(prefix));
            }
            setError(is2004 ? 301 : 203);
            return '';
        }
        if ((is2004 ? writeOnly2004 : writeOnly12).test(element)) { setError(is2004 ? 405 : 404); return ''; }
        if (!(is2004 ? known2004 : known12).test(element)) { setError(is2004 ? 401 : 401); return ''; }
        if (Object.prototype.hasOwnProperty.call(values, element)) { setError(0); return values[element]; }
        setError(is2004 ? 403 : 0);
        return '';
    }

    function setValue(element, value) {
        element = String(element || '');
        if (state === 0) { setError(is2004 ? 132 : 301); return 'false'; }
        if (state === 2) { setError(is2004 ? 133 : 101); return 'false'; }
        if (element === '') { setError(is2004 ? 351 : 201); return 'false'; }
        if (/\._(children|count|version)$/.test(element)) { setError(is2004 ? 404 : 402); return 'false'; }
        if ((is2004 ? readOnly2004 : readOnly12).test(element)) { setError(is2004 ? 404 : 403); return 'false'; }
        if (!(is2004 ? known2004 : known12).test(element)) { setError(is2004 ? 401 : 401); return 'false'; }
        value = value === undefined || value === null ? '' : String(value);
        values[element] = value;
        if (!Object.prototype.hasOwnProperty.call(dirty, element)) {
            dirtyCount++;
        }
        dirty[element] = value;
        setError(0);
        return 'true';
    }

    function initialize(param) {
        if (param !== undefined && param !== null && String(param) !== '') { setError(is2004 ? 201 : 201); return 'false'; }
        if (state === 1) { setError(is2004 ? 103 : 101); return 'false'; }
        if (state === 2) { setError(is2004 ? 104 : 101); return 'false'; }
        state = 1;
        setError(0);
        return 'true';
    }

    function finish(param) {
        if (param !== undefined && param !== null && String(param) !== '') { setError(201); return 'false'; }
        if (state === 0) { setError(is2004 ? 112 : 301); return 'false'; }
        if (state === 2) { setError(is2004 ? 113 : 101); return 'false'; }
        commit(true, false);
        state = 2;
        setError(0);
        return 'true';
    }

    function commitCall(param) {
        if (param !== undefined && param !== null && String(param) !== '') { setError(201); return 'false'; }
        if (state === 0) { setError(is2004 ? 142 : 301); return 'false'; }
        if (state === 2) { setError(is2004 ? 143 : 101); return 'false'; }
        commit(false, false);
        setError(0);
        return 'true';
    }

    function errorString(code) {
        var table = is2004 ? errors2004 : errors12;
        return table[String(code)] || '';
    }

    if (is2004) {
        window.API_1484_11 = {
            Initialize: initialize,
            Terminate: finish,
            GetValue: getValue,
            SetValue: setValue,
            Commit: commitCall,
            GetLastError: function () { return lastError; },
            GetErrorString: errorString,
            GetDiagnostic: function (code) { return errorString(code === undefined || code === '' ? lastError : code); }
        };
    } else {
        window.API = {
            LMSInitialize: initialize,
            LMSFinish: finish,
            LMSGetValue: getValue,
            LMSSetValue: setValue,
            LMSCommit: commitCall,
            LMSGetLastError: function () { return lastError; },
            LMSGetErrorString: errorString,
            LMSGetDiagnostic: function (code) { return errorString(code === undefined || code === '' ? lastError : code); }
        };
    }

    // Unsaved values go out when the page is left (a beacon survives the navigation) and at most a minute after they were set.
    window.setInterval(function () {
        if (state === 1) {
            commit(false, false);
        }
    }, 60000);
    window.addEventListener('pagehide', function () {
        if (state === 1) {
            commit(false, true);
        }
    });
})(window);
