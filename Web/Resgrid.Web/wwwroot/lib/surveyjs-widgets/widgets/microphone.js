(function webpackUniversalModuleDefinition(root, factory) {
	if(typeof exports === 'object' && typeof module === 'object')
		module.exports = factory();
	else if(typeof define === 'function' && define.amd)
		define("widgets/microphone", [], factory);
	else if(typeof exports === 'object')
		exports["widgets/microphone"] = factory();
	else
		root["widgets/microphone"] = factory();
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
/******/ 	return __webpack_require__(__webpack_require__.s = 16);
/******/ })
/************************************************************************/
/******/ ({

/***/ 16:
/***/ (function(module, __webpack_exports__, __webpack_require__) {

"use strict";
Object.defineProperty(__webpack_exports__, "__esModule", { value: true });
/* harmony import */ var __WEBPACK_IMPORTED_MODULE_0_recordrtc__ = __webpack_require__(17);
/* harmony import */ var __WEBPACK_IMPORTED_MODULE_0_recordrtc___default = __webpack_require__.n(__WEBPACK_IMPORTED_MODULE_0_recordrtc__);


function init(Survey) {
  var widget = {
    name: "microphone",
    title: "Microphone",
    iconName: "icon-microphone",
    widgetIsLoaded: function() {
      return typeof __WEBPACK_IMPORTED_MODULE_0_recordrtc___default.a != "undefined";
    },
    isFit: function(question) {
      return question.getType() === "microphone";
    },
    htmlTemplate:
      "<div style='height: 39px'>" +
      "<button type='button'  title='Record' style='vertical-align: top; margin-top: 3px' ><i class='fa fa-microphone' aria-hidden='true'></i></button>" +
      "&nbsp;<button type='button' title='Save' style='vertical-align: top; margin-top: 3px'><i class='fa fa-cloud' aria-hidden='true' ></i></button>" +
      "&nbsp;<audio style='" +
      "vertical-align: top;" +
      "margin-left: 10px;" +
      "height:35px;" +
      "-moz-box-shadow: 2px 2px 4px 0px #006773;" +
      "-webkit-box-shadow:  2px 2px 4px 0px #006773;" +
      "box-shadow: 2px 2px 4px 0px #006773;" +
      "' " +
      "controls='true' >" +
      "</audio>" +
      "</div>",
    activatedByChanged: function(activatedBy) {
      Survey.JsonObject.metaData.addClass("microphone", [], null, "empty");
    },

    afterRender: function(question, el) {
      var rootWidget = this;
      var buttonStartEl = el.getElementsByTagName("button")[0];
      var buttonStopEl = el.getElementsByTagName("button")[1];
      var audioEl = el.getElementsByTagName("audio")[0];

      //////////  RecordRTC logic

      var successCallback = function(stream) {
        var options = {
          type: "audio",
          mimeType: "audio/webm",
          audioBitsPerSecond: 44100,
          sampleRate: 44100,
          bufferSize: 16384,
          numberOfAudioChannels: 1
        };
        console.log("successCallback");
        question.survey.mystream = stream;
        question.survey.recordRTC = __WEBPACK_IMPORTED_MODULE_0_recordrtc___default()(
          question.survey.mystream,
          options
        );
        if (typeof question.survey.recordRTC != "undefined") {
          console.log("startRecording");
          question.survey.recordRTC.startRecording();
        }
      };

      var errorCallback = function() {
        alert("No microphone");
        question.survey.recordRTC = undefined;
        question.survey.mystream = undefined;
      };

      var processAudio = function(audioVideoWebMURL) {
        console.log("processAudio");
        var recordedBlob = question.survey.recordRTC.getBlob();

        var fileReader = new FileReader();
        fileReader.onload = function(event) {
          var dataUri = event.target.result;
          console.log("dataUri: " + dataUri);
          question.value = dataUri;
          audioEl.src = dataUri;

          console.log("cleaning");
          question.survey.recordRTC = undefined;
          question.survey.mystream = undefined;
        };
        fileReader.readAsDataURL(recordedBlob);
      };

      var startRecording = function() {
        // erase previous data
        question.value = undefined;

        // if recorder open on another question	- try to stop recording
        if (typeof question.survey.recordRTC != "undefined") {
          question.survey.recordRTC.stopRecording(doNothingHandler);
          if (typeof question.survey.mystream != "undefined") {
            question.survey.mystream.getAudioTracks().forEach(function(track) {
              track.stop();
            });
          }
        }

        var mediaConstraints = {
          video: false,
          audio: true
        };

        navigator.mediaDevices
          .getUserMedia(mediaConstraints)
          .then(successCallback.bind(this), errorCallback.bind(this));
      };

      var stopRecording = function() {
        console.log("stopRecording");
        if (typeof question.survey.recordRTC != "undefined") {
          question.survey.recordRTC.stopRecording(processAudio.bind(this));
          if (typeof question.survey.mystream != "undefined") {
            question.survey.mystream.getAudioTracks().forEach(function(track) {
              track.stop();
            });
          }
        }
      };

      //////////////  end RTC logic //////////////////

      if (!question.isReadOnly) {
        buttonStartEl.onclick = startRecording;
      } else {
        buttonStartEl.parentNode.removeChild(buttonStartEl);
      }

      if (!question.isReadOnly) {
        buttonStopEl.onclick = stopRecording;
      } else {
        buttonStopEl.parentNode.removeChild(buttonStopEl);
      }

      audioEl.src = question.value;

      var updateValueHandler = function() {};

      var doNothingHandler = function() {};

      question.valueChangedCallback = updateValueHandler;
      updateValueHandler();
    },
    willUnmount: function(question, el) {
      console.log("unmount microphone no record ");
      if (typeof question.survey.recordRTC != "undefined") {
        question.survey.recordRTC.stopRecording(doNothingHandler);
        if (typeof question.survey.mystream != "undefined") {
          question.survey.mystream.getAudioTracks().forEach(function(track) {
            track.stop();
          });
        }
        question.value = undefined;
        question.survey.recordRTC = undefined;
        question.survey.mystream = undefined;
      }
    }
  };

  Survey.CustomWidgetCollection.Instance.addCustomWidget(widget, "customtype");
}

if (typeof Survey !== "undefined") {
  init(Survey);
}

/* harmony default export */ __webpack_exports__["default"] = (init);


/***/ }),

/***/ 17:
/***/ (function(module, exports, __webpack_require__) {

"use strict";
/* WEBPACK VAR INJECTION */(function(global, process) {var __WEBPACK_AMD_DEFINE_ARRAY__, __WEBPACK_AMD_DEFINE_RESULT__;var __WEBPACK_AMD_DEFINE_ARRAY__, __WEBPACK_AMD_DEFINE_RESULT__;

// Last time updated: 2020-02-26 1:11:47 PM UTC

// ________________
// RecordRTC v5.5.9

// Open-Sourced: https://github.com/muaz-khan/RecordRTC

// --------------------------------------------------
// Muaz Khan     - www.MuazKhan.com
// MIT License   - www.WebRTC-Experiment.com/licence
// --------------------------------------------------

// ____________
// RecordRTC.js

/**
 * {@link https://github.com/muaz-khan/RecordRTC|RecordRTC} is a WebRTC JavaScript library for audio/video as well as screen activity recording. It supports Chrome, Firefox, Opera, Android, and Microsoft Edge. Platforms: Linux, Mac and Windows. 
 * @summary Record audio, video or screen inside the browser.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef RecordRTC
 * @class
 * @example
 * var recorder = RecordRTC(mediaStream or [arrayOfMediaStream], {
 *     type: 'video', // audio or video or gif or canvas
 *     recorderType: MediaStreamRecorder || CanvasRecorder || StereoAudioRecorder || Etc
 * });
 * recorder.startRecording();
 * @see For further information:
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - Single media-stream object, array of media-streams, html-canvas-element, etc.
 * @param {object} config - {type:"video", recorderType: MediaStreamRecorder, disableLogs: true, numberOfAudioChannels: 1, bufferSize: 0, sampleRate: 0, desiredSampRate: 16000, video: HTMLVideoElement, etc.}
 */

function RecordRTC(mediaStream, config) {
    if (!mediaStream) {
        throw 'First parameter is required.';
    }

    config = config || {
        type: 'video'
    };

    config = new RecordRTCConfiguration(mediaStream, config);

    // a reference to user's recordRTC object
    var self = this;

    function startRecording(config2) {
        if (!config.disableLogs) {
            console.log('RecordRTC version: ', self.version);
        }

        if (!!config2) {
            // allow users to set options using startRecording method
            // config2 is similar to main "config" object (second parameter over RecordRTC constructor)
            config = new RecordRTCConfiguration(mediaStream, config2);
        }

        if (!config.disableLogs) {
            console.log('started recording ' + config.type + ' stream.');
        }

        if (mediaRecorder) {
            mediaRecorder.clearRecordedData();
            mediaRecorder.record();

            setState('recording');

            if (self.recordingDuration) {
                handleRecordingDuration();
            }
            return self;
        }

        initRecorder(function() {
            if (self.recordingDuration) {
                handleRecordingDuration();
            }
        });

        return self;
    }

    function initRecorder(initCallback) {
        if (initCallback) {
            config.initCallback = function() {
                initCallback();
                initCallback = config.initCallback = null; // recorder.initRecorder should be call-backed once.
            };
        }

        var Recorder = new GetRecorderType(mediaStream, config);

        mediaRecorder = new Recorder(mediaStream, config);
        mediaRecorder.record();

        setState('recording');

        if (!config.disableLogs) {
            console.log('Initialized recorderType:', mediaRecorder.constructor.name, 'for output-type:', config.type);
        }
    }

    function stopRecording(callback) {
        callback = callback || function() {};

        if (!mediaRecorder) {
            warningLog();
            return;
        }

        if (self.state === 'paused') {
            self.resumeRecording();

            setTimeout(function() {
                stopRecording(callback);
            }, 1);
            return;
        }

        if (self.state !== 'recording' && !config.disableLogs) {
            console.warn('Recording state should be: "recording", however current state is: ', self.state);
        }

        if (!config.disableLogs) {
            console.log('Stopped recording ' + config.type + ' stream.');
        }

        if (config.type !== 'gif') {
            mediaRecorder.stop(_callback);
        } else {
            mediaRecorder.stop();
            _callback();
        }

        setState('stopped');

        function _callback(__blob) {
            if (!mediaRecorder) {
                if (typeof callback.call === 'function') {
                    callback.call(self, '');
                } else {
                    callback('');
                }
                return;
            }

            Object.keys(mediaRecorder).forEach(function(key) {
                if (typeof mediaRecorder[key] === 'function') {
                    return;
                }

                self[key] = mediaRecorder[key];
            });

            var blob = mediaRecorder.blob;

            if (!blob) {
                if (__blob) {
                    mediaRecorder.blob = blob = __blob;
                } else {
                    throw 'Recording failed.';
                }
            }

            if (blob && !config.disableLogs) {
                console.log(blob.type, '->', bytesToSize(blob.size));
            }

            if (callback) {
                var url;

                try {
                    url = URL.createObjectURL(blob);
                } catch (e) {}

                if (typeof callback.call === 'function') {
                    callback.call(self, url);
                } else {
                    callback(url);
                }
            }

            if (!config.autoWriteToDisk) {
                return;
            }

            getDataURL(function(dataURL) {
                var parameter = {};
                parameter[config.type + 'Blob'] = dataURL;
                DiskStorage.Store(parameter);
            });
        }
    }

    function pauseRecording() {
        if (!mediaRecorder) {
            warningLog();
            return;
        }

        if (self.state !== 'recording') {
            if (!config.disableLogs) {
                console.warn('Unable to pause the recording. Recording state: ', self.state);
            }
            return;
        }

        setState('paused');

        mediaRecorder.pause();

        if (!config.disableLogs) {
            console.log('Paused recording.');
        }
    }

    function resumeRecording() {
        if (!mediaRecorder) {
            warningLog();
            return;
        }

        if (self.state !== 'paused') {
            if (!config.disableLogs) {
                console.warn('Unable to resume the recording. Recording state: ', self.state);
            }
            return;
        }

        setState('recording');

        // not all libs have this method yet
        mediaRecorder.resume();

        if (!config.disableLogs) {
            console.log('Resumed recording.');
        }
    }

    function readFile(_blob) {
        postMessage(new FileReaderSync().readAsDataURL(_blob));
    }

    function getDataURL(callback, _mediaRecorder) {
        if (!callback) {
            throw 'Pass a callback function over getDataURL.';
        }

        var blob = _mediaRecorder ? _mediaRecorder.blob : (mediaRecorder || {}).blob;

        if (!blob) {
            if (!config.disableLogs) {
                console.warn('Blob encoder did not finish its job yet.');
            }

            setTimeout(function() {
                getDataURL(callback, _mediaRecorder);
            }, 1000);
            return;
        }

        if (typeof Worker !== 'undefined' && !navigator.mozGetUserMedia) {
            var webWorker = processInWebWorker(readFile);

            webWorker.onmessage = function(event) {
                callback(event.data);
            };

            webWorker.postMessage(blob);
        } else {
            var reader = new FileReader();
            reader.readAsDataURL(blob);
            reader.onload = function(event) {
                callback(event.target.result);
            };
        }

        function processInWebWorker(_function) {
            try {
                var blob = URL.createObjectURL(new Blob([_function.toString(),
                    'this.onmessage =  function (eee) {' + _function.name + '(eee.data);}'
                ], {
                    type: 'application/javascript'
                }));

                var worker = new Worker(blob);
                URL.revokeObjectURL(blob);
                return worker;
            } catch (e) {}
        }
    }

    function handleRecordingDuration(counter) {
        counter = counter || 0;

        if (self.state === 'paused') {
            setTimeout(function() {
                handleRecordingDuration(counter);
            }, 1000);
            return;
        }

        if (self.state === 'stopped') {
            return;
        }

        if (counter >= self.recordingDuration) {
            stopRecording(self.onRecordingStopped);
            return;
        }

        counter += 1000; // 1-second

        setTimeout(function() {
            handleRecordingDuration(counter);
        }, 1000);
    }

    function setState(state) {
        if (!self) {
            return;
        }

        self.state = state;

        if (typeof self.onStateChanged.call === 'function') {
            self.onStateChanged.call(self, state);
        } else {
            self.onStateChanged(state);
        }
    }

    var WARNING = 'It seems that recorder is destroyed or "startRecording" is not invoked for ' + config.type + ' recorder.';

    function warningLog() {
        if (config.disableLogs === true) {
            return;
        }

        console.warn(WARNING);
    }

    var mediaRecorder;

    var returnObject = {
        /**
         * This method starts the recording.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * var recorder = RecordRTC(mediaStream, {
         *     type: 'video'
         * });
         * recorder.startRecording();
         */
        startRecording: startRecording,

        /**
         * This method stops the recording. It is strongly recommended to get "blob" or "URI" inside the callback to make sure all recorders finished their job.
         * @param {function} callback - Callback to get the recorded blob.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.stopRecording(function() {
         *     // use either "this" or "recorder" object; both are identical
         *     video.src = this.toURL();
         *     var blob = this.getBlob();
         * });
         */
        stopRecording: stopRecording,

        /**
         * This method pauses the recording. You can resume recording using "resumeRecording" method.
         * @method
         * @memberof RecordRTC
         * @instance
         * @todo Firefox is unable to pause the recording. Fix it.
         * @example
         * recorder.pauseRecording();  // pause the recording
         * recorder.resumeRecording(); // resume again
         */
        pauseRecording: pauseRecording,

        /**
         * This method resumes the recording.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.pauseRecording();  // first of all, pause the recording
         * recorder.resumeRecording(); // now resume it
         */
        resumeRecording: resumeRecording,

        /**
         * This method initializes the recording.
         * @method
         * @memberof RecordRTC
         * @instance
         * @todo This method should be deprecated.
         * @example
         * recorder.initRecorder();
         */
        initRecorder: initRecorder,

        /**
         * Ask RecordRTC to auto-stop the recording after 5 minutes.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * var fiveMinutes = 5 * 1000 * 60;
         * recorder.setRecordingDuration(fiveMinutes, function() {
         *    var blob = this.getBlob();
         *    video.src = this.toURL();
         * });
         * 
         * // or otherwise
         * recorder.setRecordingDuration(fiveMinutes).onRecordingStopped(function() {
         *    var blob = this.getBlob();
         *    video.src = this.toURL();
         * });
         */
        setRecordingDuration: function(recordingDuration, callback) {
            if (typeof recordingDuration === 'undefined') {
                throw 'recordingDuration is required.';
            }

            if (typeof recordingDuration !== 'number') {
                throw 'recordingDuration must be a number.';
            }

            self.recordingDuration = recordingDuration;
            self.onRecordingStopped = callback || function() {};

            return {
                onRecordingStopped: function(callback) {
                    self.onRecordingStopped = callback;
                }
            };
        },

        /**
         * This method can be used to clear/reset all the recorded data.
         * @method
         * @memberof RecordRTC
         * @instance
         * @todo Figure out the difference between "reset" and "clearRecordedData" methods.
         * @example
         * recorder.clearRecordedData();
         */
        clearRecordedData: function() {
            if (!mediaRecorder) {
                warningLog();
                return;
            }

            mediaRecorder.clearRecordedData();

            if (!config.disableLogs) {
                console.log('Cleared old recorded data.');
            }
        },

        /**
         * Get the recorded blob. Use this method inside the "stopRecording" callback.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.stopRecording(function() {
         *     var blob = this.getBlob();
         *
         *     var file = new File([blob], 'filename.webm', {
         *         type: 'video/webm'
         *     });
         *
         *     var formData = new FormData();
         *     formData.append('file', file); // upload "File" object rather than a "Blob"
         *     uploadToServer(formData);
         * });
         * @returns {Blob} Returns recorded data as "Blob" object.
         */
        getBlob: function() {
            if (!mediaRecorder) {
                warningLog();
                return;
            }

            return mediaRecorder.blob;
        },

        /**
         * Get data-URI instead of Blob.
         * @param {function} callback - Callback to get the Data-URI.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.stopRecording(function() {
         *     recorder.getDataURL(function(dataURI) {
         *         video.src = dataURI;
         *     });
         * });
         */
        getDataURL: getDataURL,

        /**
         * Get virtual/temporary URL. Usage of this URL is limited to current tab.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.stopRecording(function() {
         *     video.src = this.toURL();
         * });
         * @returns {String} Returns a virtual/temporary URL for the recorded "Blob".
         */
        toURL: function() {
            if (!mediaRecorder) {
                warningLog();
                return;
            }

            return URL.createObjectURL(mediaRecorder.blob);
        },

        /**
         * Get internal recording object (i.e. internal module) e.g. MutliStreamRecorder, MediaStreamRecorder, StereoAudioRecorder or WhammyRecorder etc.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * var internalRecorder = recorder.getInternalRecorder();
         * if(internalRecorder instanceof MultiStreamRecorder) {
         *     internalRecorder.addStreams([newAudioStream]);
         *     internalRecorder.resetVideoStreams([screenStream]);
         * }
         * @returns {Object} Returns internal recording object.
         */
        getInternalRecorder: function() {
            return mediaRecorder;
        },

        /**
         * Invoke save-as dialog to save the recorded blob into your disk.
         * @param {string} fileName - Set your own file name.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.stopRecording(function() {
         *     this.save('file-name');
         *
         *     // or manually:
         *     invokeSaveAsDialog(this.getBlob(), 'filename.webm');
         * });
         */
        save: function(fileName) {
            if (!mediaRecorder) {
                warningLog();
                return;
            }

            invokeSaveAsDialog(mediaRecorder.blob, fileName);
        },

        /**
         * This method gets a blob from indexed-DB storage.
         * @param {function} callback - Callback to get the recorded blob.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.getFromDisk(function(dataURL) {
         *     video.src = dataURL;
         * });
         */
        getFromDisk: function(callback) {
            if (!mediaRecorder) {
                warningLog();
                return;
            }

            RecordRTC.getFromDisk(config.type, callback);
        },

        /**
         * This method appends an array of webp images to the recorded video-blob. It takes an "array" object.
         * @type {Array.<Array>}
         * @param {Array} arrayOfWebPImages - Array of webp images.
         * @method
         * @memberof RecordRTC
         * @instance
         * @todo This method should be deprecated.
         * @example
         * var arrayOfWebPImages = [];
         * arrayOfWebPImages.push({
         *     duration: index,
         *     image: 'data:image/webp;base64,...'
         * });
         * recorder.setAdvertisementArray(arrayOfWebPImages);
         */
        setAdvertisementArray: function(arrayOfWebPImages) {
            config.advertisement = [];

            var length = arrayOfWebPImages.length;
            for (var i = 0; i < length; i++) {
                config.advertisement.push({
                    duration: i,
                    image: arrayOfWebPImages[i]
                });
            }
        },

        /**
         * It is equivalent to <code class="str">"recorder.getBlob()"</code> method. Usage of "getBlob" is recommended, though.
         * @property {Blob} blob - Recorded Blob can be accessed using this property.
         * @memberof RecordRTC
         * @instance
         * @readonly
         * @example
         * recorder.stopRecording(function() {
         *     var blob = this.blob;
         *
         *     // below one is recommended
         *     var blob = this.getBlob();
         * });
         */
        blob: null,

        /**
         * This works only with {recorderType:StereoAudioRecorder}. Use this property on "stopRecording" to verify the encoder's sample-rates.
         * @property {number} bufferSize - Buffer-size used to encode the WAV container
         * @memberof RecordRTC
         * @instance
         * @readonly
         * @example
         * recorder.stopRecording(function() {
         *     alert('Recorder used this buffer-size: ' + this.bufferSize);
         * });
         */
        bufferSize: 0,

        /**
         * This works only with {recorderType:StereoAudioRecorder}. Use this property on "stopRecording" to verify the encoder's sample-rates.
         * @property {number} sampleRate - Sample-rates used to encode the WAV container
         * @memberof RecordRTC
         * @instance
         * @readonly
         * @example
         * recorder.stopRecording(function() {
         *     alert('Recorder used these sample-rates: ' + this.sampleRate);
         * });
         */
        sampleRate: 0,

        /**
         * {recorderType:StereoAudioRecorder} returns ArrayBuffer object.
         * @property {ArrayBuffer} buffer - Audio ArrayBuffer, supported only in Chrome.
         * @memberof RecordRTC
         * @instance
         * @readonly
         * @example
         * recorder.stopRecording(function() {
         *     var arrayBuffer = this.buffer;
         *     alert(arrayBuffer.byteLength);
         * });
         */
        buffer: null,

        /**
         * This method resets the recorder. So that you can reuse single recorder instance many times.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.reset();
         * recorder.startRecording();
         */
        reset: function() {
            if (self.state === 'recording' && !config.disableLogs) {
                console.warn('Stop an active recorder.');
            }

            if (mediaRecorder && typeof mediaRecorder.clearRecordedData === 'function') {
                mediaRecorder.clearRecordedData();
            }
            mediaRecorder = null;
            setState('inactive');
            self.blob = null;
        },

        /**
         * This method is called whenever recorder's state changes. Use this as an "event".
         * @property {String} state - A recorder's state can be: recording, paused, stopped or inactive.
         * @method
         * @memberof RecordRTC
         * @instance
         * @example
         * recorder.onStateChanged = function(state) {
         *     console.log('Recorder state: ', state);
         * };
         */
        onStateChanged: function(state) {
            if (!config.disableLogs) {
                console.log('Recorder state changed:', state);
            }
        },

        /**
         * A recorder can have inactive, recording, paused or stopped states.
         * @property {String} state - A recorder's state can be: recording, paused, stopped or inactive.
         * @memberof RecordRTC
         * @static
         * @readonly
         * @example
         * // this looper function will keep you updated about the recorder's states.
         * (function looper() {
         *     document.querySelector('h1').innerHTML = 'Recorder\'s state is: ' + recorder.state;
         *     if(recorder.state === 'stopped') return; // ignore+stop
         *     setTimeout(looper, 1000); // update after every 3-seconds
         * })();
         * recorder.startRecording();
         */
        state: 'inactive',

        /**
         * Get recorder's readonly state.
         * @method
         * @memberof RecordRTC
         * @example
         * var state = recorder.getState();
         * @returns {String} Returns recording state.
         */
        getState: function() {
            return self.state;
        },

        /**
         * Destroy RecordRTC instance. Clear all recorders and objects.
         * @method
         * @memberof RecordRTC
         * @example
         * recorder.destroy();
         */
        destroy: function() {
            var disableLogsCache = config.disableLogs;

            config = {
                disableLogs: true
            };
            self.reset();
            setState('destroyed');
            returnObject = self = null;

            if (Storage.AudioContextConstructor) {
                Storage.AudioContextConstructor.close();
                Storage.AudioContextConstructor = null;
            }

            config.disableLogs = disableLogsCache;

            if (!config.disableLogs) {
                console.log('RecordRTC is destroyed.');
            }
        },

        /**
         * RecordRTC version number
         * @property {String} version - Release version number.
         * @memberof RecordRTC
         * @static
         * @readonly
         * @example
         * alert(recorder.version);
         */
        version: '5.5.9'
    };

    if (!this) {
        self = returnObject;
        return returnObject;
    }

    // if someone wants to use RecordRTC with the "new" keyword.
    for (var prop in returnObject) {
        this[prop] = returnObject[prop];
    }

    self = this;

    return returnObject;
}

RecordRTC.version = '5.5.9';

if (true /* && !!module.exports*/ ) {
    module.exports = RecordRTC;
}

if (true) {
    !(__WEBPACK_AMD_DEFINE_ARRAY__ = [], __WEBPACK_AMD_DEFINE_RESULT__ = (function() {
        return RecordRTC;
    }).apply(exports, __WEBPACK_AMD_DEFINE_ARRAY__),
				__WEBPACK_AMD_DEFINE_RESULT__ !== undefined && (module.exports = __WEBPACK_AMD_DEFINE_RESULT__));
}

RecordRTC.getFromDisk = function(type, callback) {
    if (!callback) {
        throw 'callback is mandatory.';
    }

    console.log('Getting recorded ' + (type === 'all' ? 'blobs' : type + ' blob ') + ' from disk!');
    DiskStorage.Fetch(function(dataURL, _type) {
        if (type !== 'all' && _type === type + 'Blob' && callback) {
            callback(dataURL);
        }

        if (type === 'all' && callback) {
            callback(dataURL, _type.replace('Blob', ''));
        }
    });
};

/**
 * This method can be used to store recorded blobs into IndexedDB storage.
 * @param {object} options - {audio: Blob, video: Blob, gif: Blob}
 * @method
 * @memberof RecordRTC
 * @example
 * RecordRTC.writeToDisk({
 *     audio: audioBlob,
 *     video: videoBlob,
 *     gif  : gifBlob
 * });
 */
RecordRTC.writeToDisk = function(options) {
    console.log('Writing recorded blob(s) to disk!');
    options = options || {};
    if (options.audio && options.video && options.gif) {
        options.audio.getDataURL(function(audioDataURL) {
            options.video.getDataURL(function(videoDataURL) {
                options.gif.getDataURL(function(gifDataURL) {
                    DiskStorage.Store({
                        audioBlob: audioDataURL,
                        videoBlob: videoDataURL,
                        gifBlob: gifDataURL
                    });
                });
            });
        });
    } else if (options.audio && options.video) {
        options.audio.getDataURL(function(audioDataURL) {
            options.video.getDataURL(function(videoDataURL) {
                DiskStorage.Store({
                    audioBlob: audioDataURL,
                    videoBlob: videoDataURL
                });
            });
        });
    } else if (options.audio && options.gif) {
        options.audio.getDataURL(function(audioDataURL) {
            options.gif.getDataURL(function(gifDataURL) {
                DiskStorage.Store({
                    audioBlob: audioDataURL,
                    gifBlob: gifDataURL
                });
            });
        });
    } else if (options.video && options.gif) {
        options.video.getDataURL(function(videoDataURL) {
            options.gif.getDataURL(function(gifDataURL) {
                DiskStorage.Store({
                    videoBlob: videoDataURL,
                    gifBlob: gifDataURL
                });
            });
        });
    } else if (options.audio) {
        options.audio.getDataURL(function(audioDataURL) {
            DiskStorage.Store({
                audioBlob: audioDataURL
            });
        });
    } else if (options.video) {
        options.video.getDataURL(function(videoDataURL) {
            DiskStorage.Store({
                videoBlob: videoDataURL
            });
        });
    } else if (options.gif) {
        options.gif.getDataURL(function(gifDataURL) {
            DiskStorage.Store({
                gifBlob: gifDataURL
            });
        });
    }
};

// __________________________
// RecordRTC-Configuration.js

/**
 * {@link RecordRTCConfiguration} is an inner/private helper for {@link RecordRTC}.
 * @summary It configures the 2nd parameter passed over {@link RecordRTC} and returns a valid "config" object.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef RecordRTCConfiguration
 * @class
 * @example
 * var options = RecordRTCConfiguration(mediaStream, options);
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @param {object} config - {type:"video", disableLogs: true, numberOfAudioChannels: 1, bufferSize: 0, sampleRate: 0, video: HTMLVideoElement, getNativeBlob:true, etc.}
 */

function RecordRTCConfiguration(mediaStream, config) {
    if (!config.recorderType && !config.type) {
        if (!!config.audio && !!config.video) {
            config.type = 'video';
        } else if (!!config.audio && !config.video) {
            config.type = 'audio';
        }
    }

    if (config.recorderType && !config.type) {
        if (config.recorderType === WhammyRecorder || config.recorderType === CanvasRecorder || (typeof WebAssemblyRecorder !== 'undefined' && config.recorderType === WebAssemblyRecorder)) {
            config.type = 'video';
        } else if (config.recorderType === GifRecorder) {
            config.type = 'gif';
        } else if (config.recorderType === StereoAudioRecorder) {
            config.type = 'audio';
        } else if (config.recorderType === MediaStreamRecorder) {
            if (getTracks(mediaStream, 'audio').length && getTracks(mediaStream, 'video').length) {
                config.type = 'video';
            } else if (!getTracks(mediaStream, 'audio').length && getTracks(mediaStream, 'video').length) {
                config.type = 'video';
            } else if (getTracks(mediaStream, 'audio').length && !getTracks(mediaStream, 'video').length) {
                config.type = 'audio';
            } else {
                // config.type = 'UnKnown';
            }
        }
    }

    if (typeof MediaStreamRecorder !== 'undefined' && typeof MediaRecorder !== 'undefined' && 'requestData' in MediaRecorder.prototype) {
        if (!config.mimeType) {
            config.mimeType = 'video/webm';
        }

        if (!config.type) {
            config.type = config.mimeType.split('/')[0];
        }

        if (!config.bitsPerSecond) {
            // config.bitsPerSecond = 128000;
        }
    }

    // consider default type=audio
    if (!config.type) {
        if (config.mimeType) {
            config.type = config.mimeType.split('/')[0];
        }
        if (!config.type) {
            config.type = 'audio';
        }
    }

    return config;
}

// __________________
// GetRecorderType.js

/**
 * {@link GetRecorderType} is an inner/private helper for {@link RecordRTC}.
 * @summary It returns best recorder-type available for your browser.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef GetRecorderType
 * @class
 * @example
 * var RecorderType = GetRecorderType(options);
 * var recorder = new RecorderType(options);
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @param {object} config - {type:"video", disableLogs: true, numberOfAudioChannels: 1, bufferSize: 0, sampleRate: 0, video: HTMLVideoElement, etc.}
 */

function GetRecorderType(mediaStream, config) {
    var recorder;

    // StereoAudioRecorder can work with all three: Edge, Firefox and Chrome
    // todo: detect if it is Edge, then auto use: StereoAudioRecorder
    if (isChrome || isEdge || isOpera) {
        // Media Stream Recording API has not been implemented in chrome yet;
        // That's why using WebAudio API to record stereo audio in WAV format
        recorder = StereoAudioRecorder;
    }

    if (typeof MediaRecorder !== 'undefined' && 'requestData' in MediaRecorder.prototype && !isChrome) {
        recorder = MediaStreamRecorder;
    }

    // video recorder (in WebM format)
    if (config.type === 'video' && (isChrome || isOpera)) {
        recorder = WhammyRecorder;

        if (typeof WebAssemblyRecorder !== 'undefined' && typeof ReadableStream !== 'undefined') {
            recorder = WebAssemblyRecorder;
        }
    }

    // video recorder (in Gif format)
    if (config.type === 'gif') {
        recorder = GifRecorder;
    }

    // html2canvas recording!
    if (config.type === 'canvas') {
        recorder = CanvasRecorder;
    }

    if (isMediaRecorderCompatible() && recorder !== CanvasRecorder && recorder !== GifRecorder && typeof MediaRecorder !== 'undefined' && 'requestData' in MediaRecorder.prototype) {
        if (getTracks(mediaStream, 'video').length || getTracks(mediaStream, 'audio').length) {
            // audio-only recording
            if (config.type === 'audio') {
                if (typeof MediaRecorder.isTypeSupported === 'function' && MediaRecorder.isTypeSupported('audio/webm')) {
                    recorder = MediaStreamRecorder;
                }
                // else recorder = StereoAudioRecorder;
            } else {
                // video or screen tracks
                if (typeof MediaRecorder.isTypeSupported === 'function' && MediaRecorder.isTypeSupported('video/webm')) {
                    recorder = MediaStreamRecorder;
                }
            }
        }
    }

    if (mediaStream instanceof Array && mediaStream.length) {
        recorder = MultiStreamRecorder;
    }

    if (config.recorderType) {
        recorder = config.recorderType;
    }

    if (!config.disableLogs && !!recorder && !!recorder.name) {
        console.log('Using recorderType:', recorder.name || recorder.constructor.name);
    }

    if (!recorder && isSafari) {
        recorder = MediaStreamRecorder;
    }

    return recorder;
}

// _____________
// MRecordRTC.js

/**
 * MRecordRTC runs on top of {@link RecordRTC} to bring multiple recordings in a single place, by providing simple API.
 * @summary MRecordRTC stands for "Multiple-RecordRTC".
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef MRecordRTC
 * @class
 * @example
 * var recorder = new MRecordRTC();
 * recorder.addStream(MediaStream);
 * recorder.mediaType = {
 *     audio: true, // or StereoAudioRecorder or MediaStreamRecorder
 *     video: true, // or WhammyRecorder or MediaStreamRecorder or WebAssemblyRecorder or CanvasRecorder
 *     gif: true    // or GifRecorder
 * };
 * // mimeType is optional and should be set only in advance cases.
 * recorder.mimeType = {
 *     audio: 'audio/wav',
 *     video: 'video/webm',
 *     gif:   'image/gif'
 * };
 * recorder.startRecording();
 * @see For further information:
 * @see {@link https://github.com/muaz-khan/RecordRTC/tree/master/MRecordRTC|MRecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @requires {@link RecordRTC}
 */

function MRecordRTC(mediaStream) {

    /**
     * This method attaches MediaStream object to {@link MRecordRTC}.
     * @param {MediaStream} mediaStream - A MediaStream object, either fetched using getUserMedia API, or generated using captureStreamUntilEnded or WebAudio API.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.addStream(MediaStream);
     */
    this.addStream = function(_mediaStream) {
        if (_mediaStream) {
            mediaStream = _mediaStream;
        }
    };

    /**
     * This property can be used to set the recording type e.g. audio, or video, or gif, or canvas.
     * @property {object} mediaType - {audio: true, video: true, gif: true}
     * @memberof MRecordRTC
     * @example
     * var recorder = new MRecordRTC();
     * recorder.mediaType = {
     *     audio: true, // TRUE or StereoAudioRecorder or MediaStreamRecorder
     *     video: true, // TRUE or WhammyRecorder or MediaStreamRecorder or WebAssemblyRecorder or CanvasRecorder
     *     gif  : true  // TRUE or GifRecorder
     * };
     */
    this.mediaType = {
        audio: true,
        video: true
    };

    /**
     * This method starts recording.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.startRecording();
     */
    this.startRecording = function() {
        var mediaType = this.mediaType;
        var recorderType;
        var mimeType = this.mimeType || {
            audio: null,
            video: null,
            gif: null
        };

        if (typeof mediaType.audio !== 'function' && isMediaRecorderCompatible() && !getTracks(mediaStream, 'audio').length) {
            mediaType.audio = false;
        }

        if (typeof mediaType.video !== 'function' && isMediaRecorderCompatible() && !getTracks(mediaStream, 'video').length) {
            mediaType.video = false;
        }

        if (typeof mediaType.gif !== 'function' && isMediaRecorderCompatible() && !getTracks(mediaStream, 'video').length) {
            mediaType.gif = false;
        }

        if (!mediaType.audio && !mediaType.video && !mediaType.gif) {
            throw 'MediaStream must have either audio or video tracks.';
        }

        if (!!mediaType.audio) {
            recorderType = null;
            if (typeof mediaType.audio === 'function') {
                recorderType = mediaType.audio;
            }

            this.audioRecorder = new RecordRTC(mediaStream, {
                type: 'audio',
                bufferSize: this.bufferSize,
                sampleRate: this.sampleRate,
                numberOfAudioChannels: this.numberOfAudioChannels || 2,
                disableLogs: this.disableLogs,
                recorderType: recorderType,
                mimeType: mimeType.audio,
                timeSlice: this.timeSlice,
                onTimeStamp: this.onTimeStamp
            });

            if (!mediaType.video) {
                this.audioRecorder.startRecording();
            }
        }

        if (!!mediaType.video) {
            recorderType = null;
            if (typeof mediaType.video === 'function') {
                recorderType = mediaType.video;
            }

            var newStream = mediaStream;

            if (isMediaRecorderCompatible() && !!mediaType.audio && typeof mediaType.audio === 'function') {
                var videoTrack = getTracks(mediaStream, 'video')[0];

                if (isFirefox) {
                    newStream = new MediaStream();
                    newStream.addTrack(videoTrack);

                    if (recorderType && recorderType === WhammyRecorder) {
                        // Firefox does NOT supports webp-encoding yet
                        // But Firefox do supports WebAssemblyRecorder
                        recorderType = MediaStreamRecorder;
                    }
                } else {
                    newStream = new MediaStream();
                    newStream.addTrack(videoTrack);
                }
            }

            this.videoRecorder = new RecordRTC(newStream, {
                type: 'video',
                video: this.video,
                canvas: this.canvas,
                frameInterval: this.frameInterval || 10,
                disableLogs: this.disableLogs,
                recorderType: recorderType,
                mimeType: mimeType.video,
                timeSlice: this.timeSlice,
                onTimeStamp: this.onTimeStamp,
                workerPath: this.workerPath,
                webAssemblyPath: this.webAssemblyPath,
                frameRate: this.frameRate, // used by WebAssemblyRecorder; values: usually 30; accepts any.
                bitrate: this.bitrate // used by WebAssemblyRecorder; values: 0 to 1000+
            });

            if (!mediaType.audio) {
                this.videoRecorder.startRecording();
            }
        }

        if (!!mediaType.audio && !!mediaType.video) {
            var self = this;

            var isSingleRecorder = isMediaRecorderCompatible() === true;

            if (mediaType.audio instanceof StereoAudioRecorder && !!mediaType.video) {
                isSingleRecorder = false;
            } else if (mediaType.audio !== true && mediaType.video !== true && mediaType.audio !== mediaType.video) {
                isSingleRecorder = false;
            }

            if (isSingleRecorder === true) {
                self.audioRecorder = null;
                self.videoRecorder.startRecording();
            } else {
                self.videoRecorder.initRecorder(function() {
                    self.audioRecorder.initRecorder(function() {
                        // Both recorders are ready to record things accurately
                        self.videoRecorder.startRecording();
                        self.audioRecorder.startRecording();
                    });
                });
            }
        }

        if (!!mediaType.gif) {
            recorderType = null;
            if (typeof mediaType.gif === 'function') {
                recorderType = mediaType.gif;
            }
            this.gifRecorder = new RecordRTC(mediaStream, {
                type: 'gif',
                frameRate: this.frameRate || 200,
                quality: this.quality || 10,
                disableLogs: this.disableLogs,
                recorderType: recorderType,
                mimeType: mimeType.gif
            });
            this.gifRecorder.startRecording();
        }
    };

    /**
     * This method stops recording.
     * @param {function} callback - Callback function is invoked when all encoders finished their jobs.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.stopRecording(function(recording){
     *     var audioBlob = recording.audio;
     *     var videoBlob = recording.video;
     *     var gifBlob   = recording.gif;
     * });
     */
    this.stopRecording = function(callback) {
        callback = callback || function() {};

        if (this.audioRecorder) {
            this.audioRecorder.stopRecording(function(blobURL) {
                callback(blobURL, 'audio');
            });
        }

        if (this.videoRecorder) {
            this.videoRecorder.stopRecording(function(blobURL) {
                callback(blobURL, 'video');
            });
        }

        if (this.gifRecorder) {
            this.gifRecorder.stopRecording(function(blobURL) {
                callback(blobURL, 'gif');
            });
        }
    };

    /**
     * This method pauses recording.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.pauseRecording();
     */
    this.pauseRecording = function() {
        if (this.audioRecorder) {
            this.audioRecorder.pauseRecording();
        }

        if (this.videoRecorder) {
            this.videoRecorder.pauseRecording();
        }

        if (this.gifRecorder) {
            this.gifRecorder.pauseRecording();
        }
    };

    /**
     * This method resumes recording.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.resumeRecording();
     */
    this.resumeRecording = function() {
        if (this.audioRecorder) {
            this.audioRecorder.resumeRecording();
        }

        if (this.videoRecorder) {
            this.videoRecorder.resumeRecording();
        }

        if (this.gifRecorder) {
            this.gifRecorder.resumeRecording();
        }
    };

    /**
     * This method can be used to manually get all recorded blobs.
     * @param {function} callback - All recorded blobs are passed back to the "callback" function.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.getBlob(function(recording){
     *     var audioBlob = recording.audio;
     *     var videoBlob = recording.video;
     *     var gifBlob   = recording.gif;
     * });
     * // or
     * var audioBlob = recorder.getBlob().audio;
     * var videoBlob = recorder.getBlob().video;
     */
    this.getBlob = function(callback) {
        var output = {};

        if (this.audioRecorder) {
            output.audio = this.audioRecorder.getBlob();
        }

        if (this.videoRecorder) {
            output.video = this.videoRecorder.getBlob();
        }

        if (this.gifRecorder) {
            output.gif = this.gifRecorder.getBlob();
        }

        if (callback) {
            callback(output);
        }

        return output;
    };

    /**
     * Destroy all recorder instances.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.destroy();
     */
    this.destroy = function() {
        if (this.audioRecorder) {
            this.audioRecorder.destroy();
            this.audioRecorder = null;
        }

        if (this.videoRecorder) {
            this.videoRecorder.destroy();
            this.videoRecorder = null;
        }

        if (this.gifRecorder) {
            this.gifRecorder.destroy();
            this.gifRecorder = null;
        }
    };

    /**
     * This method can be used to manually get all recorded blobs' DataURLs.
     * @param {function} callback - All recorded blobs' DataURLs are passed back to the "callback" function.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.getDataURL(function(recording){
     *     var audioDataURL = recording.audio;
     *     var videoDataURL = recording.video;
     *     var gifDataURL   = recording.gif;
     * });
     */
    this.getDataURL = function(callback) {
        this.getBlob(function(blob) {
            if (blob.audio && blob.video) {
                getDataURL(blob.audio, function(_audioDataURL) {
                    getDataURL(blob.video, function(_videoDataURL) {
                        callback({
                            audio: _audioDataURL,
                            video: _videoDataURL
                        });
                    });
                });
            } else if (blob.audio) {
                getDataURL(blob.audio, function(_audioDataURL) {
                    callback({
                        audio: _audioDataURL
                    });
                });
            } else if (blob.video) {
                getDataURL(blob.video, function(_videoDataURL) {
                    callback({
                        video: _videoDataURL
                    });
                });
            }
        });

        function getDataURL(blob, callback00) {
            if (typeof Worker !== 'undefined') {
                var webWorker = processInWebWorker(function readFile(_blob) {
                    postMessage(new FileReaderSync().readAsDataURL(_blob));
                });

                webWorker.onmessage = function(event) {
                    callback00(event.data);
                };

                webWorker.postMessage(blob);
            } else {
                var reader = new FileReader();
                reader.readAsDataURL(blob);
                reader.onload = function(event) {
                    callback00(event.target.result);
                };
            }
        }

        function processInWebWorker(_function) {
            var blob = URL.createObjectURL(new Blob([_function.toString(),
                'this.onmessage =  function (eee) {' + _function.name + '(eee.data);}'
            ], {
                type: 'application/javascript'
            }));

            var worker = new Worker(blob);
            var url;
            if (typeof URL !== 'undefined') {
                url = URL;
            } else if (typeof webkitURL !== 'undefined') {
                url = webkitURL;
            } else {
                throw 'Neither URL nor webkitURL detected.';
            }
            url.revokeObjectURL(blob);
            return worker;
        }
    };

    /**
     * This method can be used to ask {@link MRecordRTC} to write all recorded blobs into IndexedDB storage.
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.writeToDisk();
     */
    this.writeToDisk = function() {
        RecordRTC.writeToDisk({
            audio: this.audioRecorder,
            video: this.videoRecorder,
            gif: this.gifRecorder
        });
    };

    /**
     * This method can be used to invoke a save-as dialog for all recorded blobs.
     * @param {object} args - {audio: 'audio-name', video: 'video-name', gif: 'gif-name'}
     * @method
     * @memberof MRecordRTC
     * @example
     * recorder.save({
     *     audio: 'audio-file-name',
     *     video: 'video-file-name',
     *     gif  : 'gif-file-name'
     * });
     */
    this.save = function(args) {
        args = args || {
            audio: true,
            video: true,
            gif: true
        };

        if (!!args.audio && this.audioRecorder) {
            this.audioRecorder.save(typeof args.audio === 'string' ? args.audio : '');
        }

        if (!!args.video && this.videoRecorder) {
            this.videoRecorder.save(typeof args.video === 'string' ? args.video : '');
        }
        if (!!args.gif && this.gifRecorder) {
            this.gifRecorder.save(typeof args.gif === 'string' ? args.gif : '');
        }
    };
}

/**
 * This method can be used to get all recorded blobs from IndexedDB storage.
 * @param {string} type - 'all' or 'audio' or 'video' or 'gif'
 * @param {function} callback - Callback function to get all stored blobs.
 * @method
 * @memberof MRecordRTC
 * @example
 * MRecordRTC.getFromDisk('all', function(dataURL, type){
 *     if(type === 'audio') { }
 *     if(type === 'video') { }
 *     if(type === 'gif')   { }
 * });
 */
MRecordRTC.getFromDisk = RecordRTC.getFromDisk;

/**
 * This method can be used to store recorded blobs into IndexedDB storage.
 * @param {object} options - {audio: Blob, video: Blob, gif: Blob}
 * @method
 * @memberof MRecordRTC
 * @example
 * MRecordRTC.writeToDisk({
 *     audio: audioBlob,
 *     video: videoBlob,
 *     gif  : gifBlob
 * });
 */
MRecordRTC.writeToDisk = RecordRTC.writeToDisk;

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.MRecordRTC = MRecordRTC;
}

var browserFakeUserAgent = 'Fake/5.0 (FakeOS) AppleWebKit/123 (KHTML, like Gecko) Fake/12.3.4567.89 Fake/123.45';

(function(that) {
    if (!that) {
        return;
    }

    if (typeof window !== 'undefined') {
        return;
    }

    if (typeof global === 'undefined') {
        return;
    }

    global.navigator = {
        userAgent: browserFakeUserAgent,
        getUserMedia: function() {}
    };

    if (!global.console) {
        global.console = {};
    }

    if (typeof global.console.log === 'undefined' || typeof global.console.error === 'undefined') {
        global.console.error = global.console.log = global.console.log || function() {
            console.log(arguments);
        };
    }

    if (typeof document === 'undefined') {
        /*global document:true */
        that.document = {
            documentElement: {
                appendChild: function() {
                    return '';
                }
            }
        };

        document.createElement = document.captureStream = document.mozCaptureStream = function() {
            var obj = {
                getContext: function() {
                    return obj;
                },
                play: function() {},
                pause: function() {},
                drawImage: function() {},
                toDataURL: function() {
                    return '';
                },
                style: {}
            };
            return obj;
        };

        that.HTMLVideoElement = function() {};
    }

    if (typeof location === 'undefined') {
        /*global location:true */
        that.location = {
            protocol: 'file:',
            href: '',
            hash: ''
        };
    }

    if (typeof screen === 'undefined') {
        /*global screen:true */
        that.screen = {
            width: 0,
            height: 0
        };
    }

    if (typeof URL === 'undefined') {
        /*global screen:true */
        that.URL = {
            createObjectURL: function() {
                return '';
            },
            revokeObjectURL: function() {
                return '';
            }
        };
    }

    /*global window:true */
    that.window = global;
})(typeof global !== 'undefined' ? global : null);

// _____________________________
// Cross-Browser-Declarations.js

// animation-frame used in WebM recording

/*jshint -W079 */
var requestAnimationFrame = window.requestAnimationFrame;
if (typeof requestAnimationFrame === 'undefined') {
    if (typeof webkitRequestAnimationFrame !== 'undefined') {
        /*global requestAnimationFrame:true */
        requestAnimationFrame = webkitRequestAnimationFrame;
    } else if (typeof mozRequestAnimationFrame !== 'undefined') {
        /*global requestAnimationFrame:true */
        requestAnimationFrame = mozRequestAnimationFrame;
    } else if (typeof msRequestAnimationFrame !== 'undefined') {
        /*global requestAnimationFrame:true */
        requestAnimationFrame = msRequestAnimationFrame;
    } else if (typeof requestAnimationFrame === 'undefined') {
        // via: https://gist.github.com/paulirish/1579671
        var lastTime = 0;

        /*global requestAnimationFrame:true */
        requestAnimationFrame = function(callback, element) {
            var currTime = new Date().getTime();
            var timeToCall = Math.max(0, 16 - (currTime - lastTime));
            var id = setTimeout(function() {
                callback(currTime + timeToCall);
            }, timeToCall);
            lastTime = currTime + timeToCall;
            return id;
        };
    }
}

/*jshint -W079 */
var cancelAnimationFrame = window.cancelAnimationFrame;
if (typeof cancelAnimationFrame === 'undefined') {
    if (typeof webkitCancelAnimationFrame !== 'undefined') {
        /*global cancelAnimationFrame:true */
        cancelAnimationFrame = webkitCancelAnimationFrame;
    } else if (typeof mozCancelAnimationFrame !== 'undefined') {
        /*global cancelAnimationFrame:true */
        cancelAnimationFrame = mozCancelAnimationFrame;
    } else if (typeof msCancelAnimationFrame !== 'undefined') {
        /*global cancelAnimationFrame:true */
        cancelAnimationFrame = msCancelAnimationFrame;
    } else if (typeof cancelAnimationFrame === 'undefined') {
        /*global cancelAnimationFrame:true */
        cancelAnimationFrame = function(id) {
            clearTimeout(id);
        };
    }
}

// WebAudio API representer
var AudioContext = window.AudioContext;

if (typeof AudioContext === 'undefined') {
    if (typeof webkitAudioContext !== 'undefined') {
        /*global AudioContext:true */
        AudioContext = webkitAudioContext;
    }

    if (typeof mozAudioContext !== 'undefined') {
        /*global AudioContext:true */
        AudioContext = mozAudioContext;
    }
}

/*jshint -W079 */
var URL = window.URL;

if (typeof URL === 'undefined' && typeof webkitURL !== 'undefined') {
    /*global URL:true */
    URL = webkitURL;
}

if (typeof navigator !== 'undefined' && typeof navigator.getUserMedia === 'undefined') { // maybe window.navigator?
    if (typeof navigator.webkitGetUserMedia !== 'undefined') {
        navigator.getUserMedia = navigator.webkitGetUserMedia;
    }

    if (typeof navigator.mozGetUserMedia !== 'undefined') {
        navigator.getUserMedia = navigator.mozGetUserMedia;
    }
}

var isEdge = navigator.userAgent.indexOf('Edge') !== -1 && (!!navigator.msSaveBlob || !!navigator.msSaveOrOpenBlob);
var isOpera = !!window.opera || navigator.userAgent.indexOf('OPR/') !== -1;
var isFirefox = navigator.userAgent.toLowerCase().indexOf('firefox') > -1 && ('netscape' in window) && / rv:/.test(navigator.userAgent);
var isChrome = (!isOpera && !isEdge && !!navigator.webkitGetUserMedia) || isElectron() || navigator.userAgent.toLowerCase().indexOf('chrome/') !== -1;

var isSafari = /^((?!chrome|android).)*safari/i.test(navigator.userAgent);

if (isSafari && !isChrome && navigator.userAgent.indexOf('CriOS') !== -1) {
    isSafari = false;
    isChrome = true;
}

var MediaStream = window.MediaStream;

if (typeof MediaStream === 'undefined' && typeof webkitMediaStream !== 'undefined') {
    MediaStream = webkitMediaStream;
}

/*global MediaStream:true */
if (typeof MediaStream !== 'undefined') {
    // override "stop" method for all browsers
    if (typeof MediaStream.prototype.stop === 'undefined') {
        MediaStream.prototype.stop = function() {
            this.getTracks().forEach(function(track) {
                track.stop();
            });
        };
    }
}

// below function via: http://goo.gl/B3ae8c
/**
 * Return human-readable file size.
 * @param {number} bytes - Pass bytes and get formatted string.
 * @returns {string} - formatted string
 * @example
 * bytesToSize(1024*1024*5) === '5 GB'
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 */
function bytesToSize(bytes) {
    var k = 1000;
    var sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    if (bytes === 0) {
        return '0 Bytes';
    }
    var i = parseInt(Math.floor(Math.log(bytes) / Math.log(k)), 10);
    return (bytes / Math.pow(k, i)).toPrecision(3) + ' ' + sizes[i];
}

/**
 * @param {Blob} file - File or Blob object. This parameter is required.
 * @param {string} fileName - Optional file name e.g. "Recorded-Video.webm"
 * @example
 * invokeSaveAsDialog(blob or file, [optional] fileName);
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 */
function invokeSaveAsDialog(file, fileName) {
    if (!file) {
        throw 'Blob object is required.';
    }

    if (!file.type) {
        try {
            file.type = 'video/webm';
        } catch (e) {}
    }

    var fileExtension = (file.type || 'video/webm').split('/')[1];

    if (fileName && fileName.indexOf('.') !== -1) {
        var splitted = fileName.split('.');
        fileName = splitted[0];
        fileExtension = splitted[1];
    }

    var fileFullName = (fileName || (Math.round(Math.random() * 9999999999) + 888888888)) + '.' + fileExtension;

    if (typeof navigator.msSaveOrOpenBlob !== 'undefined') {
        return navigator.msSaveOrOpenBlob(file, fileFullName);
    } else if (typeof navigator.msSaveBlob !== 'undefined') {
        return navigator.msSaveBlob(file, fileFullName);
    }

    var hyperlink = document.createElement('a');
    hyperlink.href = URL.createObjectURL(file);
    hyperlink.download = fileFullName;

    hyperlink.style = 'display:none;opacity:0;color:transparent;';
    (document.body || document.documentElement).appendChild(hyperlink);

    if (typeof hyperlink.click === 'function') {
        hyperlink.click();
    } else {
        hyperlink.target = '_blank';
        hyperlink.dispatchEvent(new MouseEvent('click', {
            view: window,
            bubbles: true,
            cancelable: true
        }));
    }

    URL.revokeObjectURL(hyperlink.href);
}

/**
 * from: https://github.com/cheton/is-electron/blob/master/index.js
 **/
function isElectron() {
    // Renderer process
    if (typeof window !== 'undefined' && typeof window.process === 'object' && window.process.type === 'renderer') {
        return true;
    }

    // Main process
    if (typeof process !== 'undefined' && typeof process.versions === 'object' && !!process.versions.electron) {
        return true;
    }

    // Detect the user agent when the `nodeIntegration` option is set to true
    if (typeof navigator === 'object' && typeof navigator.userAgent === 'string' && navigator.userAgent.indexOf('Electron') >= 0) {
        return true;
    }

    return false;
}

function getTracks(stream, kind) {
    if (!stream || !stream.getTracks) {
        return [];
    }

    return stream.getTracks().filter(function(t) {
        return t.kind === (kind || 'audio');
    });
}

function setSrcObject(stream, element) {
    if ('srcObject' in element) {
        element.srcObject = stream;
    } else if ('mozSrcObject' in element) {
        element.mozSrcObject = stream;
    } else {
        element.srcObject = stream;
    }
}

/**
 * @param {Blob} file - File or Blob object.
 * @param {function} callback - Callback function.
 * @example
 * getSeekableBlob(blob or file, callback);
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 */
function getSeekableBlob(inputBlob, callback) {
    // EBML.js copyrights goes to: https://github.com/legokichi/ts-ebml
    if (typeof EBML === 'undefined') {
        throw new Error('Please link: https://www.webrtc-experiment.com/EBML.js');
    }

    var reader = new EBML.Reader();
    var decoder = new EBML.Decoder();
    var tools = EBML.tools;

    var fileReader = new FileReader();
    fileReader.onload = function(e) {
        var ebmlElms = decoder.decode(this.result);
        ebmlElms.forEach(function(element) {
            reader.read(element);
        });
        reader.stop();
        var refinedMetadataBuf = tools.makeMetadataSeekable(reader.metadatas, reader.duration, reader.cues);
        var body = this.result.slice(reader.metadataSize);
        var newBlob = new Blob([refinedMetadataBuf, body], {
            type: 'video/webm'
        });

        callback(newBlob);
    };
    fileReader.readAsArrayBuffer(inputBlob);
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.invokeSaveAsDialog = invokeSaveAsDialog;
    RecordRTC.getTracks = getTracks;
    RecordRTC.getSeekableBlob = getSeekableBlob;
    RecordRTC.bytesToSize = bytesToSize;
    RecordRTC.isElectron = isElectron;
}

// __________ (used to handle stuff like http://goo.gl/xmE5eg) issue #129
// Storage.js

/**
 * Storage is a standalone object used by {@link RecordRTC} to store reusable objects e.g. "new AudioContext".
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @example
 * Storage.AudioContext === webkitAudioContext
 * @property {webkitAudioContext} AudioContext - Keeps a reference to AudioContext object.
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 */

var Storage = {};

if (typeof AudioContext !== 'undefined') {
    Storage.AudioContext = AudioContext;
} else if (typeof webkitAudioContext !== 'undefined') {
    Storage.AudioContext = webkitAudioContext;
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.Storage = Storage;
}

function isMediaRecorderCompatible() {
    if (isFirefox || isSafari || isEdge) {
        return true;
    }

    var nVer = navigator.appVersion;
    var nAgt = navigator.userAgent;
    var fullVersion = '' + parseFloat(navigator.appVersion);
    var majorVersion = parseInt(navigator.appVersion, 10);
    var nameOffset, verOffset, ix;

    if (isChrome || isOpera) {
        verOffset = nAgt.indexOf('Chrome');
        fullVersion = nAgt.substring(verOffset + 7);
    }

    // trim the fullVersion string at semicolon/space if present
    if ((ix = fullVersion.indexOf(';')) !== -1) {
        fullVersion = fullVersion.substring(0, ix);
    }

    if ((ix = fullVersion.indexOf(' ')) !== -1) {
        fullVersion = fullVersion.substring(0, ix);
    }

    majorVersion = parseInt('' + fullVersion, 10);

    if (isNaN(majorVersion)) {
        fullVersion = '' + parseFloat(navigator.appVersion);
        majorVersion = parseInt(navigator.appVersion, 10);
    }

    return majorVersion >= 49;
}

// ______________________
// MediaStreamRecorder.js

/**
 * MediaStreamRecorder is an abstraction layer for {@link https://w3c.github.io/mediacapture-record/MediaRecorder.html|MediaRecorder API}. It is used by {@link RecordRTC} to record MediaStream(s) in both Chrome and Firefox.
 * @summary Runs top over {@link https://w3c.github.io/mediacapture-record/MediaRecorder.html|MediaRecorder API}.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://github.com/muaz-khan|Muaz Khan}
 * @typedef MediaStreamRecorder
 * @class
 * @example
 * var config = {
 *     mimeType: 'video/webm', // vp8, vp9, h264, mkv, opus/vorbis
 *     audioBitsPerSecond : 256 * 8 * 1024,
 *     videoBitsPerSecond : 256 * 8 * 1024,
 *     bitsPerSecond: 256 * 8 * 1024,  // if this is provided, skip above two
 *     checkForInactiveTracks: true,
 *     timeSlice: 1000, // concatenate intervals based blobs
 *     ondataavailable: function() {} // get intervals based blobs
 * }
 * var recorder = new MediaStreamRecorder(mediaStream, config);
 * recorder.record();
 * recorder.stop(function(blob) {
 *     video.src = URL.createObjectURL(blob);
 *
 *     // or
 *     var blob = recorder.blob;
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @param {object} config - {disableLogs:true, initCallback: function, mimeType: "video/webm", timeSlice: 1000}
 * @throws Will throw an error if first argument "MediaStream" is missing. Also throws error if "MediaRecorder API" are not supported by the browser.
 */

function MediaStreamRecorder(mediaStream, config) {
    var self = this;

    if (typeof mediaStream === 'undefined') {
        throw 'First argument "MediaStream" is required.';
    }

    if (typeof MediaRecorder === 'undefined') {
        throw 'Your browser does not support the Media Recorder API. Please try other modules e.g. WhammyRecorder or StereoAudioRecorder.';
    }

    config = config || {
        // bitsPerSecond: 256 * 8 * 1024,
        mimeType: 'video/webm'
    };

    if (config.type === 'audio') {
        if (getTracks(mediaStream, 'video').length && getTracks(mediaStream, 'audio').length) {
            var stream;
            if (!!navigator.mozGetUserMedia) {
                stream = new MediaStream();
                stream.addTrack(getTracks(mediaStream, 'audio')[0]);
            } else {
                // webkitMediaStream
                stream = new MediaStream(getTracks(mediaStream, 'audio'));
            }
            mediaStream = stream;
        }

        if (!config.mimeType || config.mimeType.toString().toLowerCase().indexOf('audio') === -1) {
            config.mimeType = isChrome ? 'audio/webm' : 'audio/ogg';
        }

        if (config.mimeType && config.mimeType.toString().toLowerCase() !== 'audio/ogg' && !!navigator.mozGetUserMedia) {
            // forcing better codecs on Firefox (via #166)
            config.mimeType = 'audio/ogg';
        }
    }

    var arrayOfBlobs = [];

    /**
     * This method returns array of blobs. Use only with "timeSlice". Its useful to preview recording anytime, without using the "stop" method.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * var arrayOfBlobs = recorder.getArrayOfBlobs();
     * @returns {Array} Returns array of recorded blobs.
     */
    this.getArrayOfBlobs = function() {
        return arrayOfBlobs;
    };

    /**
     * This method records MediaStream.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        // set defaults
        self.blob = null;
        self.clearRecordedData();
        self.timestamps = [];
        allStates = [];
        arrayOfBlobs = [];

        var recorderHints = config;

        if (!config.disableLogs) {
            console.log('Passing following config over MediaRecorder API.', recorderHints);
        }

        if (mediaRecorder) {
            // mandatory to make sure Firefox doesn't fails to record streams 3-4 times without reloading the page.
            mediaRecorder = null;
        }

        if (isChrome && !isMediaRecorderCompatible()) {
            // to support video-only recording on stable
            recorderHints = 'video/vp8';
        }

        if (typeof MediaRecorder.isTypeSupported === 'function' && recorderHints.mimeType) {
            if (!MediaRecorder.isTypeSupported(recorderHints.mimeType)) {
                if (!config.disableLogs) {
                    console.warn('MediaRecorder API seems unable to record mimeType:', recorderHints.mimeType);
                }

                recorderHints.mimeType = config.type === 'audio' ? 'audio/webm' : 'video/webm';
            }
        }

        // using MediaRecorder API here
        try {
            mediaRecorder = new MediaRecorder(mediaStream, recorderHints);

            // reset
            config.mimeType = recorderHints.mimeType;
        } catch (e) {
            // chrome-based fallback
            mediaRecorder = new MediaRecorder(mediaStream);
        }

        // old hack?
        if (recorderHints.mimeType && !MediaRecorder.isTypeSupported && 'canRecordMimeType' in mediaRecorder && mediaRecorder.canRecordMimeType(recorderHints.mimeType) === false) {
            if (!config.disableLogs) {
                console.warn('MediaRecorder API seems unable to record mimeType:', recorderHints.mimeType);
            }
        }

        // Dispatching OnDataAvailable Handler
        mediaRecorder.ondataavailable = function(e) {
            if (e.data) {
                allStates.push('ondataavailable: ' + bytesToSize(e.data.size));
            }

            if (typeof config.timeSlice === 'number') {
                if (e.data && e.data.size && e.data.size > 100) {
                    arrayOfBlobs.push(e.data);
                    updateTimeStamp();

                    if (typeof config.ondataavailable === 'function') {
                        // intervals based blobs
                        var blob = config.getNativeBlob ? e.data : new Blob([e.data], {
                            type: getMimeType(recorderHints)
                        });
                        config.ondataavailable(blob);
                    }
                }
                return;
            }

            if (!e.data || !e.data.size || e.data.size < 100 || self.blob) {
                // make sure that stopRecording always getting fired
                // even if there is invalid data
                if (self.recordingCallback) {
                    self.recordingCallback(new Blob([], {
                        type: getMimeType(recorderHints)
                    }));
                    self.recordingCallback = null;
                }
                return;
            }

            self.blob = config.getNativeBlob ? e.data : new Blob([e.data], {
                type: getMimeType(recorderHints)
            });

            if (self.recordingCallback) {
                self.recordingCallback(self.blob);
                self.recordingCallback = null;
            }
        };

        mediaRecorder.onstart = function() {
            allStates.push('started');
        };

        mediaRecorder.onpause = function() {
            allStates.push('paused');
        };

        mediaRecorder.onresume = function() {
            allStates.push('resumed');
        };

        mediaRecorder.onstop = function() {
            allStates.push('stopped');
        };

        mediaRecorder.onerror = function(error) {
            if (!error) {
                return;
            }

            if (!error.name) {
                error.name = 'UnknownError';
            }

            allStates.push('error: ' + error);

            if (!config.disableLogs) {
                // via: https://w3c.github.io/mediacapture-record/MediaRecorder.html#exception-summary
                if (error.name.toString().toLowerCase().indexOf('invalidstate') !== -1) {
                    console.error('The MediaRecorder is not in a state in which the proposed operation is allowed to be executed.', error);
                } else if (error.name.toString().toLowerCase().indexOf('notsupported') !== -1) {
                    console.error('MIME type (', recorderHints.mimeType, ') is not supported.', error);
                } else if (error.name.toString().toLowerCase().indexOf('security') !== -1) {
                    console.error('MediaRecorder security error', error);
                }

                // older code below
                else if (error.name === 'OutOfMemory') {
                    console.error('The UA has exhaused the available memory. User agents SHOULD provide as much additional information as possible in the message attribute.', error);
                } else if (error.name === 'IllegalStreamModification') {
                    console.error('A modification to the stream has occurred that makes it impossible to continue recording. An example would be the addition of a Track while recording is occurring. User agents SHOULD provide as much additional information as possible in the message attribute.', error);
                } else if (error.name === 'OtherRecordingError') {
                    console.error('Used for an fatal error other than those listed above. User agents SHOULD provide as much additional information as possible in the message attribute.', error);
                } else if (error.name === 'GenericError') {
                    console.error('The UA cannot provide the codec or recording option that has been requested.', error);
                } else {
                    console.error('MediaRecorder Error', error);
                }
            }

            (function(looper) {
                if (!self.manuallyStopped && mediaRecorder && mediaRecorder.state === 'inactive') {
                    delete config.timeslice;

                    // 10 minutes, enough?
                    mediaRecorder.start(10 * 60 * 1000);
                    return;
                }

                setTimeout(looper, 1000);
            })();

            if (mediaRecorder.state !== 'inactive' && mediaRecorder.state !== 'stopped') {
                mediaRecorder.stop();
            }
        };

        if (typeof config.timeSlice === 'number') {
            updateTimeStamp();
            mediaRecorder.start(config.timeSlice);
        } else {
            // default is 60 minutes; enough?
            // use config => {timeSlice: 1000} otherwise

            mediaRecorder.start(3.6e+6);
        }

        if (config.initCallback) {
            config.initCallback(); // old code
        }
    };

    /**
     * @property {Array} timestamps - Array of time stamps
     * @memberof MediaStreamRecorder
     * @example
     * console.log(recorder.timestamps);
     */
    this.timestamps = [];

    function updateTimeStamp() {
        self.timestamps.push(new Date().getTime());

        if (typeof config.onTimeStamp === 'function') {
            config.onTimeStamp(self.timestamps[self.timestamps.length - 1], self.timestamps);
        }
    }

    function getMimeType(secondObject) {
        if (mediaRecorder && mediaRecorder.mimeType) {
            return mediaRecorder.mimeType;
        }

        return secondObject.mimeType || 'video/webm';
    }

    /**
     * This method stops recording MediaStream.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * recorder.stop(function(blob) {
     *     video.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        callback = callback || function() {};

        self.manuallyStopped = true; // used inside the mediaRecorder.onerror

        if (!mediaRecorder) {
            return;
        }

        this.recordingCallback = callback;

        if (mediaRecorder.state === 'recording') {
            mediaRecorder.stop();
        }

        if (typeof config.timeSlice === 'number') {
            setTimeout(function() {
                self.blob = new Blob(arrayOfBlobs, {
                    type: getMimeType(config)
                });

                self.recordingCallback(self.blob);
            }, 100);
        }
    };

    /**
     * This method pauses the recording process.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        if (!mediaRecorder) {
            return;
        }

        if (mediaRecorder.state === 'recording') {
            mediaRecorder.pause();
        }
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        if (!mediaRecorder) {
            return;
        }

        if (mediaRecorder.state === 'paused') {
            mediaRecorder.resume();
        }
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        if (mediaRecorder && mediaRecorder.state === 'recording') {
            self.stop(clearRecordedDataCB);
        }

        clearRecordedDataCB();
    };

    function clearRecordedDataCB() {
        arrayOfBlobs = [];
        mediaRecorder = null;
        self.timestamps = [];
    }

    // Reference to "MediaRecorder" object
    var mediaRecorder;

    /**
     * Access to native MediaRecorder API
     * @method
     * @memberof MediaStreamRecorder
     * @instance
     * @example
     * var internal = recorder.getInternalRecorder();
     * internal.ondataavailable = function() {}; // override
     * internal.stream, internal.onpause, internal.onstop, etc.
     * @returns {Object} Returns internal recording object.
     */
    this.getInternalRecorder = function() {
        return mediaRecorder;
    };

    function isMediaStreamActive() {
        if ('active' in mediaStream) {
            if (!mediaStream.active) {
                return false;
            }
        } else if ('ended' in mediaStream) { // old hack
            if (mediaStream.ended) {
                return false;
            }
        }
        return true;
    }

    /**
     * @property {Blob} blob - Recorded data as "Blob" object.
     * @memberof MediaStreamRecorder
     * @example
     * recorder.stop(function() {
     *     var blob = recorder.blob;
     * });
     */
    this.blob = null;


    /**
     * Get MediaRecorder readonly state.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * var state = recorder.getState();
     * @returns {String} Returns recording state.
     */
    this.getState = function() {
        if (!mediaRecorder) {
            return 'inactive';
        }

        return mediaRecorder.state || 'inactive';
    };

    // list of all recording states
    var allStates = [];

    /**
     * Get MediaRecorder all recording states.
     * @method
     * @memberof MediaStreamRecorder
     * @example
     * var state = recorder.getAllStates();
     * @returns {Array} Returns all recording states
     */
    this.getAllStates = function() {
        return allStates;
    };

    // if any Track within the MediaStream is muted or not enabled at any time, 
    // the browser will only record black frames 
    // or silence since that is the content produced by the Track
    // so we need to stopRecording as soon as any single track ends.
    if (typeof config.checkForInactiveTracks === 'undefined') {
        config.checkForInactiveTracks = false; // disable to minimize CPU usage
    }

    var self = this;

    // this method checks if media stream is stopped
    // or if any track is ended.
    (function looper() {
        if (!mediaRecorder || config.checkForInactiveTracks === false) {
            return;
        }

        if (isMediaStreamActive() === false) {
            if (!config.disableLogs) {
                console.log('MediaStream seems stopped.');
            }
            self.stop();
            return;
        }

        setTimeout(looper, 1000); // check every second
    })();

    // for debugging
    this.name = 'MediaStreamRecorder';
    this.toString = function() {
        return this.name;
    };
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.MediaStreamRecorder = MediaStreamRecorder;
}

// source code from: http://typedarray.org/wp-content/projects/WebAudioRecorder/script.js
// https://github.com/mattdiamond/Recorderjs#license-mit
// ______________________
// StereoAudioRecorder.js

/**
 * StereoAudioRecorder is a standalone class used by {@link RecordRTC} to bring "stereo" audio-recording in chrome.
 * @summary JavaScript standalone object for stereo audio recording.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef StereoAudioRecorder
 * @class
 * @example
 * var recorder = new StereoAudioRecorder(MediaStream, {
 *     sampleRate: 44100,
 *     bufferSize: 4096
 * });
 * recorder.record();
 * recorder.stop(function(blob) {
 *     video.src = URL.createObjectURL(blob);
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @param {object} config - {sampleRate: 44100, bufferSize: 4096, numberOfAudioChannels: 1, etc.}
 */

function StereoAudioRecorder(mediaStream, config) {
    if (!getTracks(mediaStream, 'audio').length) {
        throw 'Your stream has no audio tracks.';
    }

    config = config || {};

    var self = this;

    // variables
    var leftchannel = [];
    var rightchannel = [];
    var recording = false;
    var recordingLength = 0;
    var jsAudioNode;

    var numberOfAudioChannels = 2;

    /**
     * Set sample rates such as 8K or 16K. Reference: http://stackoverflow.com/a/28977136/552182
     * @property {number} desiredSampRate - Desired Bits per sample * 1000
     * @memberof StereoAudioRecorder
     * @instance
     * @example
     * var recorder = StereoAudioRecorder(mediaStream, {
     *   desiredSampRate: 16 * 1000 // bits-per-sample * 1000
     * });
     */
    var desiredSampRate = config.desiredSampRate;

    // backward compatibility
    if (config.leftChannel === true) {
        numberOfAudioChannels = 1;
    }

    if (config.numberOfAudioChannels === 1) {
        numberOfAudioChannels = 1;
    }

    if (!numberOfAudioChannels || numberOfAudioChannels < 1) {
        numberOfAudioChannels = 2;
    }

    if (!config.disableLogs) {
        console.log('StereoAudioRecorder is set to record number of channels: ' + numberOfAudioChannels);
    }

    // if any Track within the MediaStream is muted or not enabled at any time, 
    // the browser will only record black frames 
    // or silence since that is the content produced by the Track
    // so we need to stopRecording as soon as any single track ends.
    if (typeof config.checkForInactiveTracks === 'undefined') {
        config.checkForInactiveTracks = true;
    }

    function isMediaStreamActive() {
        if (config.checkForInactiveTracks === false) {
            // always return "true"
            return true;
        }

        if ('active' in mediaStream) {
            if (!mediaStream.active) {
                return false;
            }
        } else if ('ended' in mediaStream) { // old hack
            if (mediaStream.ended) {
                return false;
            }
        }
        return true;
    }

    /**
     * This method records MediaStream.
     * @method
     * @memberof StereoAudioRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        if (isMediaStreamActive() === false) {
            throw 'Please make sure MediaStream is active.';
        }

        resetVariables();

        isAudioProcessStarted = isPaused = false;
        recording = true;

        if (typeof config.timeSlice !== 'undefined') {
            looper();
        }
    };

    function mergeLeftRightBuffers(config, callback) {
        function mergeAudioBuffers(config, cb) {
            var numberOfAudioChannels = config.numberOfAudioChannels;

            // todo: "slice(0)" --- is it causes loop? Should be removed?
            var leftBuffers = config.leftBuffers.slice(0);
            var rightBuffers = config.rightBuffers.slice(0);
            var sampleRate = config.sampleRate;
            var internalInterleavedLength = config.internalInterleavedLength;
            var desiredSampRate = config.desiredSampRate;

            if (numberOfAudioChannels === 2) {
                leftBuffers = mergeBuffers(leftBuffers, internalInterleavedLength);
                rightBuffers = mergeBuffers(rightBuffers, internalInterleavedLength);

                if (desiredSampRate) {
                    leftBuffers = interpolateArray(leftBuffers, desiredSampRate, sampleRate);
                    rightBuffers = interpolateArray(rightBuffers, desiredSampRate, sampleRate);
                }
            }

            if (numberOfAudioChannels === 1) {
                leftBuffers = mergeBuffers(leftBuffers, internalInterleavedLength);

                if (desiredSampRate) {
                    leftBuffers = interpolateArray(leftBuffers, desiredSampRate, sampleRate);
                }
            }

            // set sample rate as desired sample rate
            if (desiredSampRate) {
                sampleRate = desiredSampRate;
            }

            // for changing the sampling rate, reference:
            // http://stackoverflow.com/a/28977136/552182
            function interpolateArray(data, newSampleRate, oldSampleRate) {
                var fitCount = Math.round(data.length * (newSampleRate / oldSampleRate));
                var newData = [];
                var springFactor = Number((data.length - 1) / (fitCount - 1));
                newData[0] = data[0];
                for (var i = 1; i < fitCount - 1; i++) {
                    var tmp = i * springFactor;
                    var before = Number(Math.floor(tmp)).toFixed();
                    var after = Number(Math.ceil(tmp)).toFixed();
                    var atPoint = tmp - before;
                    newData[i] = linearInterpolate(data[before], data[after], atPoint);
                }
                newData[fitCount - 1] = data[data.length - 1];
                return newData;
            }

            function linearInterpolate(before, after, atPoint) {
                return before + (after - before) * atPoint;
            }

            function mergeBuffers(channelBuffer, rLength) {
                var result = new Float64Array(rLength);
                var offset = 0;
                var lng = channelBuffer.length;

                for (var i = 0; i < lng; i++) {
                    var buffer = channelBuffer[i];
                    result.set(buffer, offset);
                    offset += buffer.length;
                }

                return result;
            }

            function interleave(leftChannel, rightChannel) {
                var length = leftChannel.length + rightChannel.length;

                var result = new Float64Array(length);

                var inputIndex = 0;

                for (var index = 0; index < length;) {
                    result[index++] = leftChannel[inputIndex];
                    result[index++] = rightChannel[inputIndex];
                    inputIndex++;
                }
                return result;
            }

            function writeUTFBytes(view, offset, string) {
                var lng = string.length;
                for (var i = 0; i < lng; i++) {
                    view.setUint8(offset + i, string.charCodeAt(i));
                }
            }

            // interleave both channels together
            var interleaved;

            if (numberOfAudioChannels === 2) {
                interleaved = interleave(leftBuffers, rightBuffers);
            }

            if (numberOfAudioChannels === 1) {
                interleaved = leftBuffers;
            }

            var interleavedLength = interleaved.length;

            // create wav file
            var resultingBufferLength = 44 + interleavedLength * 2;

            var buffer = new ArrayBuffer(resultingBufferLength);

            var view = new DataView(buffer);

            // RIFF chunk descriptor/identifier 
            writeUTFBytes(view, 0, 'RIFF');

            // RIFF chunk length
            // changed "44" to "36" via #401
            view.setUint32(4, 36 + interleavedLength * 2, true);

            // RIFF type 
            writeUTFBytes(view, 8, 'WAVE');

            // format chunk identifier 
            // FMT sub-chunk
            writeUTFBytes(view, 12, 'fmt ');

            // format chunk length 
            view.setUint32(16, 16, true);

            // sample format (raw)
            view.setUint16(20, 1, true);

            // stereo (2 channels)
            view.setUint16(22, numberOfAudioChannels, true);

            // sample rate 
            view.setUint32(24, sampleRate, true);

            // byte rate (sample rate * block align)
            view.setUint32(28, sampleRate * 2, true);

            // block align (channel count * bytes per sample) 
            view.setUint16(32, numberOfAudioChannels * 2, true);

            // bits per sample 
            view.setUint16(34, 16, true);

            // data sub-chunk
            // data chunk identifier 
            writeUTFBytes(view, 36, 'data');

            // data chunk length 
            view.setUint32(40, interleavedLength * 2, true);

            // write the PCM samples
            var lng = interleavedLength;
            var index = 44;
            var volume = 1;
            for (var i = 0; i < lng; i++) {
                view.setInt16(index, interleaved[i] * (0x7FFF * volume), true);
                index += 2;
            }

            if (cb) {
                return cb({
                    buffer: buffer,
                    view: view
                });
            }

            postMessage({
                buffer: buffer,
                view: view
            });
        }

        if (config.noWorker) {
            mergeAudioBuffers(config, function(data) {
                callback(data.buffer, data.view);
            });
            return;
        }


        var webWorker = processInWebWorker(mergeAudioBuffers);

        webWorker.onmessage = function(event) {
            callback(event.data.buffer, event.data.view);

            // release memory
            URL.revokeObjectURL(webWorker.workerURL);

            // kill webworker (or Chrome will kill your page after ~25 calls)
            webWorker.terminate();
        };

        webWorker.postMessage(config);
    }

    function processInWebWorker(_function) {
        var workerURL = URL.createObjectURL(new Blob([_function.toString(),
            ';this.onmessage =  function (eee) {' + _function.name + '(eee.data);}'
        ], {
            type: 'application/javascript'
        }));

        var worker = new Worker(workerURL);
        worker.workerURL = workerURL;
        return worker;
    }

    /**
     * This method stops recording MediaStream.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof StereoAudioRecorder
     * @example
     * recorder.stop(function(blob) {
     *     video.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        callback = callback || function() {};

        // stop recording
        recording = false;

        mergeLeftRightBuffers({
            desiredSampRate: desiredSampRate,
            sampleRate: sampleRate,
            numberOfAudioChannels: numberOfAudioChannels,
            internalInterleavedLength: recordingLength,
            leftBuffers: leftchannel,
            rightBuffers: numberOfAudioChannels === 1 ? [] : rightchannel,
            noWorker: config.noWorker
        }, function(buffer, view) {
            /**
             * @property {Blob} blob - The recorded blob object.
             * @memberof StereoAudioRecorder
             * @example
             * recorder.stop(function(){
             *     var blob = recorder.blob;
             * });
             */
            self.blob = new Blob([view], {
                type: 'audio/wav'
            });

            /**
             * @property {ArrayBuffer} buffer - The recorded buffer object.
             * @memberof StereoAudioRecorder
             * @example
             * recorder.stop(function(){
             *     var buffer = recorder.buffer;
             * });
             */
            self.buffer = new ArrayBuffer(view.buffer.byteLength);

            /**
             * @property {DataView} view - The recorded data-view object.
             * @memberof StereoAudioRecorder
             * @example
             * recorder.stop(function(){
             *     var view = recorder.view;
             * });
             */
            self.view = view;

            self.sampleRate = desiredSampRate || sampleRate;
            self.bufferSize = bufferSize;

            // recorded audio length
            self.length = recordingLength;

            isAudioProcessStarted = false;

            if (callback) {
                callback(self.blob);
            }
        });
    };

    if (typeof RecordRTC.Storage === 'undefined') {
        RecordRTC.Storage = {
            AudioContextConstructor: null,
            AudioContext: window.AudioContext || window.webkitAudioContext
        };
    }

    if (!RecordRTC.Storage.AudioContextConstructor || RecordRTC.Storage.AudioContextConstructor.state === 'closed') {
        RecordRTC.Storage.AudioContextConstructor = new RecordRTC.Storage.AudioContext();
    }

    var context = RecordRTC.Storage.AudioContextConstructor;

    // creates an audio node from the microphone incoming stream
    var audioInput = context.createMediaStreamSource(mediaStream);

    var legalBufferValues = [0, 256, 512, 1024, 2048, 4096, 8192, 16384];

    /**
     * From the spec: This value controls how frequently the audioprocess event is
     * dispatched and how many sample-frames need to be processed each call.
     * Lower values for buffer size will result in a lower (better) latency.
     * Higher values will be necessary to avoid audio breakup and glitches
     * The size of the buffer (in sample-frames) which needs to
     * be processed each time onprocessaudio is called.
     * Legal values are (256, 512, 1024, 2048, 4096, 8192, 16384).
     * @property {number} bufferSize - Buffer-size for how frequently the audioprocess event is dispatched.
     * @memberof StereoAudioRecorder
     * @example
     * recorder = new StereoAudioRecorder(mediaStream, {
     *     bufferSize: 4096
     * });
     */

    // "0" means, let chrome decide the most accurate buffer-size for current platform.
    var bufferSize = typeof config.bufferSize === 'undefined' ? 4096 : config.bufferSize;

    if (legalBufferValues.indexOf(bufferSize) === -1) {
        if (!config.disableLogs) {
            console.log('Legal values for buffer-size are ' + JSON.stringify(legalBufferValues, null, '\t'));
        }
    }

    if (context.createJavaScriptNode) {
        jsAudioNode = context.createJavaScriptNode(bufferSize, numberOfAudioChannels, numberOfAudioChannels);
    } else if (context.createScriptProcessor) {
        jsAudioNode = context.createScriptProcessor(bufferSize, numberOfAudioChannels, numberOfAudioChannels);
    } else {
        throw 'WebAudio API has no support on this browser.';
    }

    // connect the stream to the script processor
    audioInput.connect(jsAudioNode);

    if (!config.bufferSize) {
        bufferSize = jsAudioNode.bufferSize; // device buffer-size
    }

    /**
     * The sample rate (in sample-frames per second) at which the
     * AudioContext handles audio. It is assumed that all AudioNodes
     * in the context run at this rate. In making this assumption,
     * sample-rate converters or "varispeed" processors are not supported
     * in real-time processing.
     * The sampleRate parameter describes the sample-rate of the
     * linear PCM audio data in the buffer in sample-frames per second.
     * An implementation must support sample-rates in at least
     * the range 22050 to 96000.
     * @property {number} sampleRate - Buffer-size for how frequently the audioprocess event is dispatched.
     * @memberof StereoAudioRecorder
     * @example
     * recorder = new StereoAudioRecorder(mediaStream, {
     *     sampleRate: 44100
     * });
     */
    var sampleRate = typeof config.sampleRate !== 'undefined' ? config.sampleRate : context.sampleRate || 44100;

    if (sampleRate < 22050 || sampleRate > 96000) {
        // Ref: http://stackoverflow.com/a/26303918/552182
        if (!config.disableLogs) {
            console.log('sample-rate must be under range 22050 and 96000.');
        }
    }

    if (!config.disableLogs) {
        if (config.desiredSampRate) {
            console.log('Desired sample-rate: ' + config.desiredSampRate);
        }
    }

    var isPaused = false;
    /**
     * This method pauses the recording process.
     * @method
     * @memberof StereoAudioRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        isPaused = true;
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof StereoAudioRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        if (isMediaStreamActive() === false) {
            throw 'Please make sure MediaStream is active.';
        }

        if (!recording) {
            if (!config.disableLogs) {
                console.log('Seems recording has been restarted.');
            }
            this.record();
            return;
        }

        isPaused = false;
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof StereoAudioRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        config.checkForInactiveTracks = false;

        if (recording) {
            this.stop(clearRecordedDataCB);
        }

        clearRecordedDataCB();
    };

    function resetVariables() {
        leftchannel = [];
        rightchannel = [];
        recordingLength = 0;
        isAudioProcessStarted = false;
        recording = false;
        isPaused = false;
        context = null;

        self.leftchannel = leftchannel;
        self.rightchannel = rightchannel;
        self.numberOfAudioChannels = numberOfAudioChannels;
        self.desiredSampRate = desiredSampRate;
        self.sampleRate = sampleRate;
        self.recordingLength = recordingLength;

        intervalsBasedBuffers = {
            left: [],
            right: [],
            recordingLength: 0
        };
    }

    function clearRecordedDataCB() {
        if (jsAudioNode) {
            jsAudioNode.onaudioprocess = null;
            jsAudioNode.disconnect();
            jsAudioNode = null;
        }

        if (audioInput) {
            audioInput.disconnect();
            audioInput = null;
        }

        resetVariables();
    }

    // for debugging
    this.name = 'StereoAudioRecorder';
    this.toString = function() {
        return this.name;
    };

    var isAudioProcessStarted = false;

    function onAudioProcessDataAvailable(e) {
        if (isPaused) {
            return;
        }

        if (isMediaStreamActive() === false) {
            if (!config.disableLogs) {
                console.log('MediaStream seems stopped.');
            }
            jsAudioNode.disconnect();
            recording = false;
        }

        if (!recording) {
            if (audioInput) {
                audioInput.disconnect();
                audioInput = null;
            }
            return;
        }

        /**
         * This method is called on "onaudioprocess" event's first invocation.
         * @method {function} onAudioProcessStarted
         * @memberof StereoAudioRecorder
         * @example
         * recorder.onAudioProcessStarted: function() { };
         */
        if (!isAudioProcessStarted) {
            isAudioProcessStarted = true;
            if (config.onAudioProcessStarted) {
                config.onAudioProcessStarted();
            }

            if (config.initCallback) {
                config.initCallback();
            }
        }

        var left = e.inputBuffer.getChannelData(0);

        // we clone the samples
        var chLeft = new Float32Array(left);
        leftchannel.push(chLeft);

        if (numberOfAudioChannels === 2) {
            var right = e.inputBuffer.getChannelData(1);
            var chRight = new Float32Array(right);
            rightchannel.push(chRight);
        }

        recordingLength += bufferSize;

        // export raw PCM
        self.recordingLength = recordingLength;

        if (typeof config.timeSlice !== 'undefined') {
            intervalsBasedBuffers.recordingLength += bufferSize;
            intervalsBasedBuffers.left.push(chLeft);

            if (numberOfAudioChannels === 2) {
                intervalsBasedBuffers.right.push(chRight);
            }
        }
    }

    jsAudioNode.onaudioprocess = onAudioProcessDataAvailable;

    // to prevent self audio to be connected with speakers
    if (context.createMediaStreamDestination) {
        jsAudioNode.connect(context.createMediaStreamDestination());
    } else {
        jsAudioNode.connect(context.destination);
    }

    // export raw PCM
    this.leftchannel = leftchannel;
    this.rightchannel = rightchannel;
    this.numberOfAudioChannels = numberOfAudioChannels;
    this.desiredSampRate = desiredSampRate;
    this.sampleRate = sampleRate;
    self.recordingLength = recordingLength;

    // helper for intervals based blobs
    var intervalsBasedBuffers = {
        left: [],
        right: [],
        recordingLength: 0
    };

    // this looper is used to support intervals based blobs (via timeSlice+ondataavailable)
    function looper() {
        if (!recording || typeof config.ondataavailable !== 'function' || typeof config.timeSlice === 'undefined') {
            return;
        }

        if (intervalsBasedBuffers.left.length) {
            mergeLeftRightBuffers({
                desiredSampRate: desiredSampRate,
                sampleRate: sampleRate,
                numberOfAudioChannels: numberOfAudioChannels,
                internalInterleavedLength: intervalsBasedBuffers.recordingLength,
                leftBuffers: intervalsBasedBuffers.left,
                rightBuffers: numberOfAudioChannels === 1 ? [] : intervalsBasedBuffers.right
            }, function(buffer, view) {
                var blob = new Blob([view], {
                    type: 'audio/wav'
                });
                config.ondataavailable(blob);

                setTimeout(looper, config.timeSlice);
            });

            intervalsBasedBuffers = {
                left: [],
                right: [],
                recordingLength: 0
            };
        } else {
            setTimeout(looper, config.timeSlice);
        }
    }
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.StereoAudioRecorder = StereoAudioRecorder;
}

// _________________
// CanvasRecorder.js

/**
 * CanvasRecorder is a standalone class used by {@link RecordRTC} to bring HTML5-Canvas recording into video WebM. It uses HTML2Canvas library and runs top over {@link Whammy}.
 * @summary HTML2Canvas recording into video WebM.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef CanvasRecorder
 * @class
 * @example
 * var recorder = new CanvasRecorder(htmlElement, { disableLogs: true, useWhammyRecorder: true });
 * recorder.record();
 * recorder.stop(function(blob) {
 *     video.src = URL.createObjectURL(blob);
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {HTMLElement} htmlElement - querySelector/getElementById/getElementsByTagName[0]/etc.
 * @param {object} config - {disableLogs:true, initCallback: function}
 */

function CanvasRecorder(htmlElement, config) {
    if (typeof html2canvas === 'undefined') {
        throw 'Please link: https://www.webrtc-experiment.com/screenshot.js';
    }

    config = config || {};
    if (!config.frameInterval) {
        config.frameInterval = 10;
    }

    // via DetectRTC.js
    var isCanvasSupportsStreamCapturing = false;
    ['captureStream', 'mozCaptureStream', 'webkitCaptureStream'].forEach(function(item) {
        if (item in document.createElement('canvas')) {
            isCanvasSupportsStreamCapturing = true;
        }
    });

    var _isChrome = (!!window.webkitRTCPeerConnection || !!window.webkitGetUserMedia) && !!window.chrome;

    var chromeVersion = 50;
    var matchArray = navigator.userAgent.match(/Chrom(e|ium)\/([0-9]+)\./);
    if (_isChrome && matchArray && matchArray[2]) {
        chromeVersion = parseInt(matchArray[2], 10);
    }

    if (_isChrome && chromeVersion < 52) {
        isCanvasSupportsStreamCapturing = false;
    }

    if (config.useWhammyRecorder) {
        isCanvasSupportsStreamCapturing = false;
    }

    var globalCanvas, mediaStreamRecorder;

    if (isCanvasSupportsStreamCapturing) {
        if (!config.disableLogs) {
            console.log('Your browser supports both MediRecorder API and canvas.captureStream!');
        }

        if (htmlElement instanceof HTMLCanvasElement) {
            globalCanvas = htmlElement;
        } else if (htmlElement instanceof CanvasRenderingContext2D) {
            globalCanvas = htmlElement.canvas;
        } else {
            throw 'Please pass either HTMLCanvasElement or CanvasRenderingContext2D.';
        }
    } else if (!!navigator.mozGetUserMedia) {
        if (!config.disableLogs) {
            console.error('Canvas recording is NOT supported in Firefox.');
        }
    }

    var isRecording;

    /**
     * This method records Canvas.
     * @method
     * @memberof CanvasRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        isRecording = true;

        if (isCanvasSupportsStreamCapturing && !config.useWhammyRecorder) {
            // CanvasCaptureMediaStream
            var canvasMediaStream;
            if ('captureStream' in globalCanvas) {
                canvasMediaStream = globalCanvas.captureStream(25); // 25 FPS
            } else if ('mozCaptureStream' in globalCanvas) {
                canvasMediaStream = globalCanvas.mozCaptureStream(25);
            } else if ('webkitCaptureStream' in globalCanvas) {
                canvasMediaStream = globalCanvas.webkitCaptureStream(25);
            }

            try {
                var mdStream = new MediaStream();
                mdStream.addTrack(getTracks(canvasMediaStream, 'video')[0]);
                canvasMediaStream = mdStream;
            } catch (e) {}

            if (!canvasMediaStream) {
                throw 'captureStream API are NOT available.';
            }

            // Note: Jan 18, 2016 status is that, 
            // Firefox MediaRecorder API can't record CanvasCaptureMediaStream object.
            mediaStreamRecorder = new MediaStreamRecorder(canvasMediaStream, {
                mimeType: config.mimeType || 'video/webm'
            });
            mediaStreamRecorder.record();
        } else {
            whammy.frames = [];
            lastTime = new Date().getTime();
            drawCanvasFrame();
        }

        if (config.initCallback) {
            config.initCallback();
        }
    };

    this.getWebPImages = function(callback) {
        if (htmlElement.nodeName.toLowerCase() !== 'canvas') {
            callback();
            return;
        }

        var framesLength = whammy.frames.length;
        whammy.frames.forEach(function(frame, idx) {
            var framesRemaining = framesLength - idx;
            if (!config.disableLogs) {
                console.log(framesRemaining + '/' + framesLength + ' frames remaining');
            }

            if (config.onEncodingCallback) {
                config.onEncodingCallback(framesRemaining, framesLength);
            }

            var webp = frame.image.toDataURL('image/webp', 1);
            whammy.frames[idx].image = webp;
        });

        if (!config.disableLogs) {
            console.log('Generating WebM');
        }

        callback();
    };

    /**
     * This method stops recording Canvas.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof CanvasRecorder
     * @example
     * recorder.stop(function(blob) {
     *     video.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        isRecording = false;

        var that = this;

        if (isCanvasSupportsStreamCapturing && mediaStreamRecorder) {
            mediaStreamRecorder.stop(callback);
            return;
        }

        this.getWebPImages(function() {
            /**
             * @property {Blob} blob - Recorded frames in video/webm blob.
             * @memberof CanvasRecorder
             * @example
             * recorder.stop(function() {
             *     var blob = recorder.blob;
             * });
             */
            whammy.compile(function(blob) {
                if (!config.disableLogs) {
                    console.log('Recording finished!');
                }

                that.blob = blob;

                if (that.blob.forEach) {
                    that.blob = new Blob([], {
                        type: 'video/webm'
                    });
                }

                if (callback) {
                    callback(that.blob);
                }

                whammy.frames = [];
            });
        });
    };

    var isPausedRecording = false;

    /**
     * This method pauses the recording process.
     * @method
     * @memberof CanvasRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        isPausedRecording = true;

        if (mediaStreamRecorder instanceof MediaStreamRecorder) {
            mediaStreamRecorder.pause();
            return;
        }
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof CanvasRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        isPausedRecording = false;

        if (mediaStreamRecorder instanceof MediaStreamRecorder) {
            mediaStreamRecorder.resume();
            return;
        }

        if (!isRecording) {
            this.record();
        }
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof CanvasRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        if (isRecording) {
            this.stop(clearRecordedDataCB);
        }
        clearRecordedDataCB();
    };

    function clearRecordedDataCB() {
        whammy.frames = [];
        isRecording = false;
        isPausedRecording = false;
    }

    // for debugging
    this.name = 'CanvasRecorder';
    this.toString = function() {
        return this.name;
    };

    function cloneCanvas() {
        //create a new canvas
        var newCanvas = document.createElement('canvas');
        var context = newCanvas.getContext('2d');

        //set dimensions
        newCanvas.width = htmlElement.width;
        newCanvas.height = htmlElement.height;

        //apply the old canvas to the new one
        context.drawImage(htmlElement, 0, 0);

        //return the new canvas
        return newCanvas;
    }

    function drawCanvasFrame() {
        if (isPausedRecording) {
            lastTime = new Date().getTime();
            return setTimeout(drawCanvasFrame, 500);
        }

        if (htmlElement.nodeName.toLowerCase() === 'canvas') {
            var duration = new Date().getTime() - lastTime;
            // via #206, by Jack i.e. @Seymourr
            lastTime = new Date().getTime();

            whammy.frames.push({
                image: cloneCanvas(),
                duration: duration
            });

            if (isRecording) {
                setTimeout(drawCanvasFrame, config.frameInterval);
            }
            return;
        }

        html2canvas(htmlElement, {
            grabMouse: typeof config.showMousePointer === 'undefined' || config.showMousePointer,
            onrendered: function(canvas) {
                var duration = new Date().getTime() - lastTime;
                if (!duration) {
                    return setTimeout(drawCanvasFrame, config.frameInterval);
                }

                // via #206, by Jack i.e. @Seymourr
                lastTime = new Date().getTime();

                whammy.frames.push({
                    image: canvas.toDataURL('image/webp', 1),
                    duration: duration
                });

                if (isRecording) {
                    setTimeout(drawCanvasFrame, config.frameInterval);
                }
            }
        });
    }

    var lastTime = new Date().getTime();

    var whammy = new Whammy.Video(100);
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.CanvasRecorder = CanvasRecorder;
}

// _________________
// WhammyRecorder.js

/**
 * WhammyRecorder is a standalone class used by {@link RecordRTC} to bring video recording in Chrome. It runs top over {@link Whammy}.
 * @summary Video recording feature in Chrome.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef WhammyRecorder
 * @class
 * @example
 * var recorder = new WhammyRecorder(mediaStream);
 * recorder.record();
 * recorder.stop(function(blob) {
 *     video.src = URL.createObjectURL(blob);
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @param {object} config - {disableLogs: true, initCallback: function, video: HTMLVideoElement, etc.}
 */

function WhammyRecorder(mediaStream, config) {

    config = config || {};

    if (!config.frameInterval) {
        config.frameInterval = 10;
    }

    if (!config.disableLogs) {
        console.log('Using frames-interval:', config.frameInterval);
    }

    /**
     * This method records video.
     * @method
     * @memberof WhammyRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        if (!config.width) {
            config.width = 320;
        }

        if (!config.height) {
            config.height = 240;
        }

        if (!config.video) {
            config.video = {
                width: config.width,
                height: config.height
            };
        }

        if (!config.canvas) {
            config.canvas = {
                width: config.width,
                height: config.height
            };
        }

        canvas.width = config.canvas.width || 320;
        canvas.height = config.canvas.height || 240;

        context = canvas.getContext('2d');

        // setting defaults
        if (config.video && config.video instanceof HTMLVideoElement) {
            video = config.video.cloneNode();

            if (config.initCallback) {
                config.initCallback();
            }
        } else {
            video = document.createElement('video');

            setSrcObject(mediaStream, video);

            video.onloadedmetadata = function() { // "onloadedmetadata" may NOT work in FF?
                if (config.initCallback) {
                    config.initCallback();
                }
            };

            video.width = config.video.width;
            video.height = config.video.height;
        }

        video.muted = true;
        video.play();

        lastTime = new Date().getTime();
        whammy = new Whammy.Video();

        if (!config.disableLogs) {
            console.log('canvas resolutions', canvas.width, '*', canvas.height);
            console.log('video width/height', video.width || canvas.width, '*', video.height || canvas.height);
        }

        drawFrames(config.frameInterval);
    };

    /**
     * Draw and push frames to Whammy
     * @param {integer} frameInterval - set minimum interval (in milliseconds) between each time we push a frame to Whammy
     */
    function drawFrames(frameInterval) {
        frameInterval = typeof frameInterval !== 'undefined' ? frameInterval : 10;

        var duration = new Date().getTime() - lastTime;
        if (!duration) {
            return setTimeout(drawFrames, frameInterval, frameInterval);
        }

        if (isPausedRecording) {
            lastTime = new Date().getTime();
            return setTimeout(drawFrames, 100);
        }

        // via #206, by Jack i.e. @Seymourr
        lastTime = new Date().getTime();

        if (video.paused) {
            // via: https://github.com/muaz-khan/WebRTC-Experiment/pull/316
            // Tweak for Android Chrome
            video.play();
        }

        context.drawImage(video, 0, 0, canvas.width, canvas.height);
        whammy.frames.push({
            duration: duration,
            image: canvas.toDataURL('image/webp')
        });

        if (!isStopDrawing) {
            setTimeout(drawFrames, frameInterval, frameInterval);
        }
    }

    function asyncLoop(o) {
        var i = -1,
            length = o.length;

        (function loop() {
            i++;
            if (i === length) {
                o.callback();
                return;
            }

            // "setTimeout" added by Jim McLeod
            setTimeout(function() {
                o.functionToLoop(loop, i);
            }, 1);
        })();
    }


    /**
     * remove black frames from the beginning to the specified frame
     * @param {Array} _frames - array of frames to be checked
     * @param {number} _framesToCheck - number of frame until check will be executed (-1 - will drop all frames until frame not matched will be found)
     * @param {number} _pixTolerance - 0 - very strict (only black pixel color) ; 1 - all
     * @param {number} _frameTolerance - 0 - very strict (only black frame color) ; 1 - all
     * @returns {Array} - array of frames
     */
    // pull#293 by @volodalexey
    function dropBlackFrames(_frames, _framesToCheck, _pixTolerance, _frameTolerance, callback) {
        var localCanvas = document.createElement('canvas');
        localCanvas.width = canvas.width;
        localCanvas.height = canvas.height;
        var context2d = localCanvas.getContext('2d');
        var resultFrames = [];

        var checkUntilNotBlack = _framesToCheck === -1;
        var endCheckFrame = (_framesToCheck && _framesToCheck > 0 && _framesToCheck <= _frames.length) ?
            _framesToCheck : _frames.length;
        var sampleColor = {
            r: 0,
            g: 0,
            b: 0
        };
        var maxColorDifference = Math.sqrt(
            Math.pow(255, 2) +
            Math.pow(255, 2) +
            Math.pow(255, 2)
        );
        var pixTolerance = _pixTolerance && _pixTolerance >= 0 && _pixTolerance <= 1 ? _pixTolerance : 0;
        var frameTolerance = _frameTolerance && _frameTolerance >= 0 && _frameTolerance <= 1 ? _frameTolerance : 0;
        var doNotCheckNext = false;

        asyncLoop({
            length: endCheckFrame,
            functionToLoop: function(loop, f) {
                var matchPixCount, endPixCheck, maxPixCount;

                var finishImage = function() {
                    if (!doNotCheckNext && maxPixCount - matchPixCount <= maxPixCount * frameTolerance) {
                        // console.log('removed black frame : ' + f + ' ; frame duration ' + _frames[f].duration);
                    } else {
                        // console.log('frame is passed : ' + f);
                        if (checkUntilNotBlack) {
                            doNotCheckNext = true;
                        }
                        resultFrames.push(_frames[f]);
                    }
                    loop();
                };

                if (!doNotCheckNext) {
                    var image = new Image();
                    image.onload = function() {
                        context2d.drawImage(image, 0, 0, canvas.width, canvas.height);
                        var imageData = context2d.getImageData(0, 0, canvas.width, canvas.height);
                        matchPixCount = 0;
                        endPixCheck = imageData.data.length;
                        maxPixCount = imageData.data.length / 4;

                        for (var pix = 0; pix < endPixCheck; pix += 4) {
                            var currentColor = {
                                r: imageData.data[pix],
                                g: imageData.data[pix + 1],
                                b: imageData.data[pix + 2]
                            };
                            var colorDifference = Math.sqrt(
                                Math.pow(currentColor.r - sampleColor.r, 2) +
                                Math.pow(currentColor.g - sampleColor.g, 2) +
                                Math.pow(currentColor.b - sampleColor.b, 2)
                            );
                            // difference in color it is difference in color vectors (r1,g1,b1) <=> (r2,g2,b2)
                            if (colorDifference <= maxColorDifference * pixTolerance) {
                                matchPixCount++;
                            }
                        }
                        finishImage();
                    };
                    image.src = _frames[f].image;
                } else {
                    finishImage();
                }
            },
            callback: function() {
                resultFrames = resultFrames.concat(_frames.slice(endCheckFrame));

                if (resultFrames.length <= 0) {
                    // at least one last frame should be available for next manipulation
                    // if total duration of all frames will be < 1000 than ffmpeg doesn't work well...
                    resultFrames.push(_frames[_frames.length - 1]);
                }
                callback(resultFrames);
            }
        });
    }

    var isStopDrawing = false;

    /**
     * This method stops recording video.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof WhammyRecorder
     * @example
     * recorder.stop(function(blob) {
     *     video.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        callback = callback || function() {};

        isStopDrawing = true;

        var _this = this;
        // analyse of all frames takes some time!
        setTimeout(function() {
            // e.g. dropBlackFrames(frames, 10, 1, 1) - will cut all 10 frames
            // e.g. dropBlackFrames(frames, 10, 0.5, 0.5) - will analyse 10 frames
            // e.g. dropBlackFrames(frames, 10) === dropBlackFrames(frames, 10, 0, 0) - will analyse 10 frames with strict black color
            dropBlackFrames(whammy.frames, -1, null, null, function(frames) {
                whammy.frames = frames;

                // to display advertisement images!
                if (config.advertisement && config.advertisement.length) {
                    whammy.frames = config.advertisement.concat(whammy.frames);
                }

                /**
                 * @property {Blob} blob - Recorded frames in video/webm blob.
                 * @memberof WhammyRecorder
                 * @example
                 * recorder.stop(function() {
                 *     var blob = recorder.blob;
                 * });
                 */
                whammy.compile(function(blob) {
                    _this.blob = blob;

                    if (_this.blob.forEach) {
                        _this.blob = new Blob([], {
                            type: 'video/webm'
                        });
                    }

                    if (callback) {
                        callback(_this.blob);
                    }
                });
            });
        }, 10);
    };

    var isPausedRecording = false;

    /**
     * This method pauses the recording process.
     * @method
     * @memberof WhammyRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        isPausedRecording = true;
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof WhammyRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        isPausedRecording = false;

        if (isStopDrawing) {
            this.record();
        }
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof WhammyRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        if (!isStopDrawing) {
            this.stop(clearRecordedDataCB);
        }
        clearRecordedDataCB();
    };

    function clearRecordedDataCB() {
        whammy.frames = [];
        isStopDrawing = true;
        isPausedRecording = false;
    }

    // for debugging
    this.name = 'WhammyRecorder';
    this.toString = function() {
        return this.name;
    };

    var canvas = document.createElement('canvas');
    var context = canvas.getContext('2d');

    var video;
    var lastTime;
    var whammy;
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.WhammyRecorder = WhammyRecorder;
}

// https://github.com/antimatter15/whammy/blob/master/LICENSE
// _________
// Whammy.js

// todo: Firefox now supports webp for webm containers!
// their MediaRecorder implementation works well!
// should we provide an option to record via Whammy.js or MediaRecorder API is a better solution?

/**
 * Whammy is a standalone class used by {@link RecordRTC} to bring video recording in Chrome. It is written by {@link https://github.com/antimatter15|antimatter15}
 * @summary A real time javascript webm encoder based on a canvas hack.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef Whammy
 * @class
 * @example
 * var recorder = new Whammy().Video(15);
 * recorder.add(context || canvas || dataURL);
 * var output = recorder.compile();
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 */

var Whammy = (function() {
    // a more abstract-ish API

    function WhammyVideo(duration) {
        this.frames = [];
        this.duration = duration || 1;
        this.quality = 0.8;
    }

    /**
     * Pass Canvas or Context or image/webp(string) to {@link Whammy} encoder.
     * @method
     * @memberof Whammy
     * @example
     * recorder = new Whammy().Video(0.8, 100);
     * recorder.add(canvas || context || 'image/webp');
     * @param {string} frame - Canvas || Context || image/webp
     * @param {number} duration - Stick a duration (in milliseconds)
     */
    WhammyVideo.prototype.add = function(frame, duration) {
        if ('canvas' in frame) { //CanvasRenderingContext2D
            frame = frame.canvas;
        }

        if ('toDataURL' in frame) {
            frame = frame.toDataURL('image/webp', this.quality);
        }

        if (!(/^data:image\/webp;base64,/ig).test(frame)) {
            throw 'Input must be formatted properly as a base64 encoded DataURI of type image/webp';
        }
        this.frames.push({
            image: frame,
            duration: duration || this.duration
        });
    };

    function processInWebWorker(_function) {
        var blob = URL.createObjectURL(new Blob([_function.toString(),
            'this.onmessage =  function (eee) {' + _function.name + '(eee.data);}'
        ], {
            type: 'application/javascript'
        }));

        var worker = new Worker(blob);
        URL.revokeObjectURL(blob);
        return worker;
    }

    function whammyInWebWorker(frames) {
        function ArrayToWebM(frames) {
            var info = checkFrames(frames);
            if (!info) {
                return [];
            }

            var clusterMaxDuration = 30000;

            var EBML = [{
                'id': 0x1a45dfa3, // EBML
                'data': [{
                    'data': 1,
                    'id': 0x4286 // EBMLVersion
                }, {
                    'data': 1,
                    'id': 0x42f7 // EBMLReadVersion
                }, {
                    'data': 4,
                    'id': 0x42f2 // EBMLMaxIDLength
                }, {
                    'data': 8,
                    'id': 0x42f3 // EBMLMaxSizeLength
                }, {
                    'data': 'webm',
                    'id': 0x4282 // DocType
                }, {
                    'data': 2,
                    'id': 0x4287 // DocTypeVersion
                }, {
                    'data': 2,
                    'id': 0x4285 // DocTypeReadVersion
                }]
            }, {
                'id': 0x18538067, // Segment
                'data': [{
                    'id': 0x1549a966, // Info
                    'data': [{
                        'data': 1e6, //do things in millisecs (num of nanosecs for duration scale)
                        'id': 0x2ad7b1 // TimecodeScale
                    }, {
                        'data': 'whammy',
                        'id': 0x4d80 // MuxingApp
                    }, {
                        'data': 'whammy',
                        'id': 0x5741 // WritingApp
                    }, {
                        'data': doubleToString(info.duration),
                        'id': 0x4489 // Duration
                    }]
                }, {
                    'id': 0x1654ae6b, // Tracks
                    'data': [{
                        'id': 0xae, // TrackEntry
                        'data': [{
                            'data': 1,
                            'id': 0xd7 // TrackNumber
                        }, {
                            'data': 1,
                            'id': 0x73c5 // TrackUID
                        }, {
                            'data': 0,
                            'id': 0x9c // FlagLacing
                        }, {
                            'data': 'und',
                            'id': 0x22b59c // Language
                        }, {
                            'data': 'V_VP8',
                            'id': 0x86 // CodecID
                        }, {
                            'data': 'VP8',
                            'id': 0x258688 // CodecName
                        }, {
                            'data': 1,
                            'id': 0x83 // TrackType
                        }, {
                            'id': 0xe0, // Video
                            'data': [{
                                'data': info.width,
                                'id': 0xb0 // PixelWidth
                            }, {
                                'data': info.height,
                                'id': 0xba // PixelHeight
                            }]
                        }]
                    }]
                }]
            }];

            //Generate clusters (max duration)
            var frameNumber = 0;
            var clusterTimecode = 0;
            while (frameNumber < frames.length) {

                var clusterFrames = [];
                var clusterDuration = 0;
                do {
                    clusterFrames.push(frames[frameNumber]);
                    clusterDuration += frames[frameNumber].duration;
                    frameNumber++;
                } while (frameNumber < frames.length && clusterDuration < clusterMaxDuration);

                var clusterCounter = 0;
                var cluster = {
                    'id': 0x1f43b675, // Cluster
                    'data': getClusterData(clusterTimecode, clusterCounter, clusterFrames)
                }; //Add cluster to segment
                EBML[1].data.push(cluster);
                clusterTimecode += clusterDuration;
            }

            return generateEBML(EBML);
        }

        function getClusterData(clusterTimecode, clusterCounter, clusterFrames) {
            return [{
                'data': clusterTimecode,
                'id': 0xe7 // Timecode
            }].concat(clusterFrames.map(function(webp) {
                var block = makeSimpleBlock({
                    discardable: 0,
                    frame: webp.data.slice(4),
                    invisible: 0,
                    keyframe: 1,
                    lacing: 0,
                    trackNum: 1,
                    timecode: Math.round(clusterCounter)
                });
                clusterCounter += webp.duration;
                return {
                    data: block,
                    id: 0xa3
                };
            }));
        }

        // sums the lengths of all the frames and gets the duration

        function checkFrames(frames) {
            if (!frames[0]) {
                postMessage({
                    error: 'Something went wrong. Maybe WebP format is not supported in the current browser.'
                });
                return;
            }

            var width = frames[0].width,
                height = frames[0].height,
                duration = frames[0].duration;

            for (var i = 1; i < frames.length; i++) {
                duration += frames[i].duration;
            }
            return {
                duration: duration,
                width: width,
                height: height
            };
        }

        function numToBuffer(num) {
            var parts = [];
            while (num > 0) {
                parts.push(num & 0xff);
                num = num >> 8;
            }
            return new Uint8Array(parts.reverse());
        }

        function strToBuffer(str) {
            return new Uint8Array(str.split('').map(function(e) {
                return e.charCodeAt(0);
            }));
        }

        function bitsToBuffer(bits) {
            var data = [];
            var pad = (bits.length % 8) ? (new Array(1 + 8 - (bits.length % 8))).join('0') : '';
            bits = pad + bits;
            for (var i = 0; i < bits.length; i += 8) {
                data.push(parseInt(bits.substr(i, 8), 2));
            }
            return new Uint8Array(data);
        }

        function generateEBML(json) {
            var ebml = [];
            for (var i = 0; i < json.length; i++) {
                var data = json[i].data;

                if (typeof data === 'object') {
                    data = generateEBML(data);
                }

                if (typeof data === 'number') {
                    data = bitsToBuffer(data.toString(2));
                }

                if (typeof data === 'string') {
                    data = strToBuffer(data);
                }

                var len = data.size || data.byteLength || data.length;
                var zeroes = Math.ceil(Math.ceil(Math.log(len) / Math.log(2)) / 8);
                var sizeToString = len.toString(2);
                var padded = (new Array((zeroes * 7 + 7 + 1) - sizeToString.length)).join('0') + sizeToString;
                var size = (new Array(zeroes)).join('0') + '1' + padded;

                ebml.push(numToBuffer(json[i].id));
                ebml.push(bitsToBuffer(size));
                ebml.push(data);
            }

            return new Blob(ebml, {
                type: 'video/webm'
            });
        }

        function toBinStrOld(bits) {
            var data = '';
            var pad = (bits.length % 8) ? (new Array(1 + 8 - (bits.length % 8))).join('0') : '';
            bits = pad + bits;
            for (var i = 0; i < bits.length; i += 8) {
                data += String.fromCharCode(parseInt(bits.substr(i, 8), 2));
            }
            return data;
        }

        function makeSimpleBlock(data) {
            var flags = 0;

            if (data.keyframe) {
                flags |= 128;
            }

            if (data.invisible) {
                flags |= 8;
            }

            if (data.lacing) {
                flags |= (data.lacing << 1);
            }

            if (data.discardable) {
                flags |= 1;
            }

            if (data.trackNum > 127) {
                throw 'TrackNumber > 127 not supported';
            }

            var out = [data.trackNum | 0x80, data.timecode >> 8, data.timecode & 0xff, flags].map(function(e) {
                return String.fromCharCode(e);
            }).join('') + data.frame;

            return out;
        }

        function parseWebP(riff) {
            var VP8 = riff.RIFF[0].WEBP[0];

            var frameStart = VP8.indexOf('\x9d\x01\x2a'); // A VP8 keyframe starts with the 0x9d012a header
            for (var i = 0, c = []; i < 4; i++) {
                c[i] = VP8.charCodeAt(frameStart + 3 + i);
            }

            var width, height, tmp;

            //the code below is literally copied verbatim from the bitstream spec
            tmp = (c[1] << 8) | c[0];
            width = tmp & 0x3FFF;
            tmp = (c[3] << 8) | c[2];
            height = tmp & 0x3FFF;
            return {
                width: width,
                height: height,
                data: VP8,
                riff: riff
            };
        }

        function getStrLength(string, offset) {
            return parseInt(string.substr(offset + 4, 4).split('').map(function(i) {
                var unpadded = i.charCodeAt(0).toString(2);
                return (new Array(8 - unpadded.length + 1)).join('0') + unpadded;
            }).join(''), 2);
        }

        function parseRIFF(string) {
            var offset = 0;
            var chunks = {};

            while (offset < string.length) {
                var id = string.substr(offset, 4);
                var len = getStrLength(string, offset);
                var data = string.substr(offset + 4 + 4, len);
                offset += 4 + 4 + len;
                chunks[id] = chunks[id] || [];

                if (id === 'RIFF' || id === 'LIST') {
                    chunks[id].push(parseRIFF(data));
                } else {
                    chunks[id].push(data);
                }
            }
            return chunks;
        }

        function doubleToString(num) {
            return [].slice.call(
                new Uint8Array((new Float64Array([num])).buffer), 0).map(function(e) {
                return String.fromCharCode(e);
            }).reverse().join('');
        }

        var webm = new ArrayToWebM(frames.map(function(frame) {
            var webp = parseWebP(parseRIFF(atob(frame.image.slice(23))));
            webp.duration = frame.duration;
            return webp;
        }));

        postMessage(webm);
    }

    /**
     * Encodes frames in WebM container. It uses WebWorkinvoke to invoke 'ArrayToWebM' method.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof Whammy
     * @example
     * recorder = new Whammy().Video(0.8, 100);
     * recorder.compile(function(blob) {
     *    // blob.size - blob.type
     * });
     */
    WhammyVideo.prototype.compile = function(callback) {
        var webWorker = processInWebWorker(whammyInWebWorker);

        webWorker.onmessage = function(event) {
            if (event.data.error) {
                console.error(event.data.error);
                return;
            }
            callback(event.data);
        };

        webWorker.postMessage(this.frames);
    };

    return {
        /**
         * A more abstract-ish API.
         * @method
         * @memberof Whammy
         * @example
         * recorder = new Whammy().Video(0.8, 100);
         * @param {?number} speed - 0.8
         * @param {?number} quality - 100
         */
        Video: WhammyVideo
    };
})();

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.Whammy = Whammy;
}

// ______________ (indexed-db)
// DiskStorage.js

/**
 * DiskStorage is a standalone object used by {@link RecordRTC} to store recorded blobs in IndexedDB storage.
 * @summary Writing blobs into IndexedDB.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @example
 * DiskStorage.Store({
 *     audioBlob: yourAudioBlob,
 *     videoBlob: yourVideoBlob,
 *     gifBlob  : yourGifBlob
 * });
 * DiskStorage.Fetch(function(dataURL, type) {
 *     if(type === 'audioBlob') { }
 *     if(type === 'videoBlob') { }
 *     if(type === 'gifBlob')   { }
 * });
 * // DiskStorage.dataStoreName = 'recordRTC';
 * // DiskStorage.onError = function(error) { };
 * @property {function} init - This method must be called once to initialize IndexedDB ObjectStore. Though, it is auto-used internally.
 * @property {function} Fetch - This method fetches stored blobs from IndexedDB.
 * @property {function} Store - This method stores blobs in IndexedDB.
 * @property {function} onError - This function is invoked for any known/unknown error.
 * @property {string} dataStoreName - Name of the ObjectStore created in IndexedDB storage.
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 */


var DiskStorage = {
    /**
     * This method must be called once to initialize IndexedDB ObjectStore. Though, it is auto-used internally.
     * @method
     * @memberof DiskStorage
     * @internal
     * @example
     * DiskStorage.init();
     */
    init: function() {
        var self = this;

        if (typeof indexedDB === 'undefined' || typeof indexedDB.open === 'undefined') {
            console.error('IndexedDB API are not available in this browser.');
            return;
        }

        var dbVersion = 1;
        var dbName = this.dbName || location.href.replace(/\/|:|#|%|\.|\[|\]/g, ''),
            db;
        var request = indexedDB.open(dbName, dbVersion);

        function createObjectStore(dataBase) {
            dataBase.createObjectStore(self.dataStoreName);
        }

        function putInDB() {
            var transaction = db.transaction([self.dataStoreName], 'readwrite');

            if (self.videoBlob) {
                transaction.objectStore(self.dataStoreName).put(self.videoBlob, 'videoBlob');
            }

            if (self.gifBlob) {
                transaction.objectStore(self.dataStoreName).put(self.gifBlob, 'gifBlob');
            }

            if (self.audioBlob) {
                transaction.objectStore(self.dataStoreName).put(self.audioBlob, 'audioBlob');
            }

            function getFromStore(portionName) {
                transaction.objectStore(self.dataStoreName).get(portionName).onsuccess = function(event) {
                    if (self.callback) {
                        self.callback(event.target.result, portionName);
                    }
                };
            }

            getFromStore('audioBlob');
            getFromStore('videoBlob');
            getFromStore('gifBlob');
        }

        request.onerror = self.onError;

        request.onsuccess = function() {
            db = request.result;
            db.onerror = self.onError;

            if (db.setVersion) {
                if (db.version !== dbVersion) {
                    var setVersion = db.setVersion(dbVersion);
                    setVersion.onsuccess = function() {
                        createObjectStore(db);
                        putInDB();
                    };
                } else {
                    putInDB();
                }
            } else {
                putInDB();
            }
        };
        request.onupgradeneeded = function(event) {
            createObjectStore(event.target.result);
        };
    },
    /**
     * This method fetches stored blobs from IndexedDB.
     * @method
     * @memberof DiskStorage
     * @internal
     * @example
     * DiskStorage.Fetch(function(dataURL, type) {
     *     if(type === 'audioBlob') { }
     *     if(type === 'videoBlob') { }
     *     if(type === 'gifBlob')   { }
     * });
     */
    Fetch: function(callback) {
        this.callback = callback;
        this.init();

        return this;
    },
    /**
     * This method stores blobs in IndexedDB.
     * @method
     * @memberof DiskStorage
     * @internal
     * @example
     * DiskStorage.Store({
     *     audioBlob: yourAudioBlob,
     *     videoBlob: yourVideoBlob,
     *     gifBlob  : yourGifBlob
     * });
     */
    Store: function(config) {
        this.audioBlob = config.audioBlob;
        this.videoBlob = config.videoBlob;
        this.gifBlob = config.gifBlob;

        this.init();

        return this;
    },
    /**
     * This function is invoked for any known/unknown error.
     * @method
     * @memberof DiskStorage
     * @internal
     * @example
     * DiskStorage.onError = function(error){
     *     alerot( JSON.stringify(error) );
     * };
     */
    onError: function(error) {
        console.error(JSON.stringify(error, null, '\t'));
    },

    /**
     * @property {string} dataStoreName - Name of the ObjectStore created in IndexedDB storage.
     * @memberof DiskStorage
     * @internal
     * @example
     * DiskStorage.dataStoreName = 'recordRTC';
     */
    dataStoreName: 'recordRTC',
    dbName: null
};

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.DiskStorage = DiskStorage;
}

// ______________
// GifRecorder.js

/**
 * GifRecorder is standalone calss used by {@link RecordRTC} to record video or canvas into animated gif.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef GifRecorder
 * @class
 * @example
 * var recorder = new GifRecorder(mediaStream || canvas || context, { onGifPreview: function, onGifRecordingStarted: function, width: 1280, height: 720, frameRate: 200, quality: 10 });
 * recorder.record();
 * recorder.stop(function(blob) {
 *     img.src = URL.createObjectURL(blob);
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object or HTMLCanvasElement or CanvasRenderingContext2D.
 * @param {object} config - {disableLogs:true, initCallback: function, width: 320, height: 240, frameRate: 200, quality: 10}
 */

function GifRecorder(mediaStream, config) {
    if (typeof GIFEncoder === 'undefined') {
        var script = document.createElement('script');
        script.src = 'https://www.webrtc-experiment.com/gif-recorder.js';
        (document.body || document.documentElement).appendChild(script);
    }

    config = config || {};

    var isHTMLObject = mediaStream instanceof CanvasRenderingContext2D || mediaStream instanceof HTMLCanvasElement;

    /**
     * This method records MediaStream.
     * @method
     * @memberof GifRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        if (typeof GIFEncoder === 'undefined') {
            setTimeout(self.record, 1000);
            return;
        }

        if (!isLoadedMetaData) {
            setTimeout(self.record, 1000);
            return;
        }

        if (!isHTMLObject) {
            if (!config.width) {
                config.width = video.offsetWidth || 320;
            }

            if (!config.height) {
                config.height = video.offsetHeight || 240;
            }

            if (!config.video) {
                config.video = {
                    width: config.width,
                    height: config.height
                };
            }

            if (!config.canvas) {
                config.canvas = {
                    width: config.width,
                    height: config.height
                };
            }

            canvas.width = config.canvas.width || 320;
            canvas.height = config.canvas.height || 240;

            video.width = config.video.width || 320;
            video.height = config.video.height || 240;
        }

        // external library to record as GIF images
        gifEncoder = new GIFEncoder();

        // void setRepeat(int iter) 
        // Sets the number of times the set of GIF frames should be played. 
        // Default is 1; 0 means play indefinitely.
        gifEncoder.setRepeat(0);

        // void setFrameRate(Number fps) 
        // Sets frame rate in frames per second. 
        // Equivalent to setDelay(1000/fps).
        // Using "setDelay" instead of "setFrameRate"
        gifEncoder.setDelay(config.frameRate || 200);

        // void setQuality(int quality) 
        // Sets quality of color quantization (conversion of images to the 
        // maximum 256 colors allowed by the GIF specification). 
        // Lower values (minimum = 1) produce better colors, 
        // but slow processing significantly. 10 is the default, 
        // and produces good color mapping at reasonable speeds. 
        // Values greater than 20 do not yield significant improvements in speed.
        gifEncoder.setQuality(config.quality || 10);

        // Boolean start() 
        // This writes the GIF Header and returns false if it fails.
        gifEncoder.start();

        if (typeof config.onGifRecordingStarted === 'function') {
            config.onGifRecordingStarted();
        }

        startTime = Date.now();

        function drawVideoFrame(time) {
            if (self.clearedRecordedData === true) {
                return;
            }

            if (isPausedRecording) {
                return setTimeout(function() {
                    drawVideoFrame(time);
                }, 100);
            }

            lastAnimationFrame = requestAnimationFrame(drawVideoFrame);

            if (typeof lastFrameTime === undefined) {
                lastFrameTime = time;
            }

            // ~10 fps
            if (time - lastFrameTime < 90) {
                return;
            }

            if (!isHTMLObject && video.paused) {
                // via: https://github.com/muaz-khan/WebRTC-Experiment/pull/316
                // Tweak for Android Chrome
                video.play();
            }

            if (!isHTMLObject) {
                context.drawImage(video, 0, 0, canvas.width, canvas.height);
            }

            if (config.onGifPreview) {
                config.onGifPreview(canvas.toDataURL('image/png'));
            }

            gifEncoder.addFrame(context);
            lastFrameTime = time;
        }

        lastAnimationFrame = requestAnimationFrame(drawVideoFrame);

        if (config.initCallback) {
            config.initCallback();
        }
    };

    /**
     * This method stops recording MediaStream.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof GifRecorder
     * @example
     * recorder.stop(function(blob) {
     *     img.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        callback = callback || function() {};

        if (lastAnimationFrame) {
            cancelAnimationFrame(lastAnimationFrame);
        }

        endTime = Date.now();

        /**
         * @property {Blob} blob - The recorded blob object.
         * @memberof GifRecorder
         * @example
         * recorder.stop(function(){
         *     var blob = recorder.blob;
         * });
         */
        this.blob = new Blob([new Uint8Array(gifEncoder.stream().bin)], {
            type: 'image/gif'
        });

        callback(this.blob);

        // bug: find a way to clear old recorded blobs
        gifEncoder.stream().bin = [];
    };

    var isPausedRecording = false;

    /**
     * This method pauses the recording process.
     * @method
     * @memberof GifRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        isPausedRecording = true;
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof GifRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        isPausedRecording = false;
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof GifRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        self.clearedRecordedData = true;
        clearRecordedDataCB();
    };

    function clearRecordedDataCB() {
        if (gifEncoder) {
            gifEncoder.stream().bin = [];
        }
    }

    // for debugging
    this.name = 'GifRecorder';
    this.toString = function() {
        return this.name;
    };

    var canvas = document.createElement('canvas');
    var context = canvas.getContext('2d');

    if (isHTMLObject) {
        if (mediaStream instanceof CanvasRenderingContext2D) {
            context = mediaStream;
            canvas = context.canvas;
        } else if (mediaStream instanceof HTMLCanvasElement) {
            context = mediaStream.getContext('2d');
            canvas = mediaStream;
        }
    }

    var isLoadedMetaData = true;

    if (!isHTMLObject) {
        var video = document.createElement('video');
        video.muted = true;
        video.autoplay = true;
        video.playsInline = true;

        isLoadedMetaData = false;
        video.onloadedmetadata = function() {
            isLoadedMetaData = true;
        };

        setSrcObject(mediaStream, video);

        video.play();
    }

    var lastAnimationFrame = null;
    var startTime, endTime, lastFrameTime;

    var gifEncoder;

    var self = this;
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.GifRecorder = GifRecorder;
}

// Last time updated: 2019-06-21 4:09:42 AM UTC

// ________________________
// MultiStreamsMixer v1.2.2

// Open-Sourced: https://github.com/muaz-khan/MultiStreamsMixer

// --------------------------------------------------
// Muaz Khan     - www.MuazKhan.com
// MIT License   - www.WebRTC-Experiment.com/licence
// --------------------------------------------------

function MultiStreamsMixer(arrayOfMediaStreams, elementClass) {

    var browserFakeUserAgent = 'Fake/5.0 (FakeOS) AppleWebKit/123 (KHTML, like Gecko) Fake/12.3.4567.89 Fake/123.45';

    (function(that) {
        if (typeof RecordRTC !== 'undefined') {
            return;
        }

        if (!that) {
            return;
        }

        if (typeof window !== 'undefined') {
            return;
        }

        if (typeof global === 'undefined') {
            return;
        }

        global.navigator = {
            userAgent: browserFakeUserAgent,
            getUserMedia: function() {}
        };

        if (!global.console) {
            global.console = {};
        }

        if (typeof global.console.log === 'undefined' || typeof global.console.error === 'undefined') {
            global.console.error = global.console.log = global.console.log || function() {
                console.log(arguments);
            };
        }

        if (typeof document === 'undefined') {
            /*global document:true */
            that.document = {
                documentElement: {
                    appendChild: function() {
                        return '';
                    }
                }
            };

            document.createElement = document.captureStream = document.mozCaptureStream = function() {
                var obj = {
                    getContext: function() {
                        return obj;
                    },
                    play: function() {},
                    pause: function() {},
                    drawImage: function() {},
                    toDataURL: function() {
                        return '';
                    },
                    style: {}
                };
                return obj;
            };

            that.HTMLVideoElement = function() {};
        }

        if (typeof location === 'undefined') {
            /*global location:true */
            that.location = {
                protocol: 'file:',
                href: '',
                hash: ''
            };
        }

        if (typeof screen === 'undefined') {
            /*global screen:true */
            that.screen = {
                width: 0,
                height: 0
            };
        }

        if (typeof URL === 'undefined') {
            /*global screen:true */
            that.URL = {
                createObjectURL: function() {
                    return '';
                },
                revokeObjectURL: function() {
                    return '';
                }
            };
        }

        /*global window:true */
        that.window = global;
    })(typeof global !== 'undefined' ? global : null);

    // requires: chrome://flags/#enable-experimental-web-platform-features

    elementClass = elementClass || 'multi-streams-mixer';

    var videos = [];
    var isStopDrawingFrames = false;

    var canvas = document.createElement('canvas');
    var context = canvas.getContext('2d');
    canvas.style.opacity = 0;
    canvas.style.position = 'absolute';
    canvas.style.zIndex = -1;
    canvas.style.top = '-1000em';
    canvas.style.left = '-1000em';
    canvas.className = elementClass;
    (document.body || document.documentElement).appendChild(canvas);

    this.disableLogs = false;
    this.frameInterval = 10;

    this.width = 360;
    this.height = 240;

    // use gain node to prevent echo
    this.useGainNode = true;

    var self = this;

    // _____________________________
    // Cross-Browser-Declarations.js

    // WebAudio API representer
    var AudioContext = window.AudioContext;

    if (typeof AudioContext === 'undefined') {
        if (typeof webkitAudioContext !== 'undefined') {
            /*global AudioContext:true */
            AudioContext = webkitAudioContext;
        }

        if (typeof mozAudioContext !== 'undefined') {
            /*global AudioContext:true */
            AudioContext = mozAudioContext;
        }
    }

    /*jshint -W079 */
    var URL = window.URL;

    if (typeof URL === 'undefined' && typeof webkitURL !== 'undefined') {
        /*global URL:true */
        URL = webkitURL;
    }

    if (typeof navigator !== 'undefined' && typeof navigator.getUserMedia === 'undefined') { // maybe window.navigator?
        if (typeof navigator.webkitGetUserMedia !== 'undefined') {
            navigator.getUserMedia = navigator.webkitGetUserMedia;
        }

        if (typeof navigator.mozGetUserMedia !== 'undefined') {
            navigator.getUserMedia = navigator.mozGetUserMedia;
        }
    }

    var MediaStream = window.MediaStream;

    if (typeof MediaStream === 'undefined' && typeof webkitMediaStream !== 'undefined') {
        MediaStream = webkitMediaStream;
    }

    /*global MediaStream:true */
    if (typeof MediaStream !== 'undefined') {
        // override "stop" method for all browsers
        if (typeof MediaStream.prototype.stop === 'undefined') {
            MediaStream.prototype.stop = function() {
                this.getTracks().forEach(function(track) {
                    track.stop();
                });
            };
        }
    }

    var Storage = {};

    if (typeof AudioContext !== 'undefined') {
        Storage.AudioContext = AudioContext;
    } else if (typeof webkitAudioContext !== 'undefined') {
        Storage.AudioContext = webkitAudioContext;
    }

    function setSrcObject(stream, element) {
        if ('srcObject' in element) {
            element.srcObject = stream;
        } else if ('mozSrcObject' in element) {
            element.mozSrcObject = stream;
        } else {
            element.srcObject = stream;
        }
    }

    this.startDrawingFrames = function() {
        drawVideosToCanvas();
    };

    function drawVideosToCanvas() {
        if (isStopDrawingFrames) {
            return;
        }

        var videosLength = videos.length;

        var fullcanvas = false;
        var remaining = [];
        videos.forEach(function(video) {
            if (!video.stream) {
                video.stream = {};
            }

            if (video.stream.fullcanvas) {
                fullcanvas = video;
            } else {
                // todo: video.stream.active or video.stream.live to fix blank frames issues?
                remaining.push(video);
            }
        });

        if (fullcanvas) {
            canvas.width = fullcanvas.stream.width;
            canvas.height = fullcanvas.stream.height;
        } else if (remaining.length) {
            canvas.width = videosLength > 1 ? remaining[0].width * 2 : remaining[0].width;

            var height = 1;
            if (videosLength === 3 || videosLength === 4) {
                height = 2;
            }
            if (videosLength === 5 || videosLength === 6) {
                height = 3;
            }
            if (videosLength === 7 || videosLength === 8) {
                height = 4;
            }
            if (videosLength === 9 || videosLength === 10) {
                height = 5;
            }
            canvas.height = remaining[0].height * height;
        } else {
            canvas.width = self.width || 360;
            canvas.height = self.height || 240;
        }

        if (fullcanvas && fullcanvas instanceof HTMLVideoElement) {
            drawImage(fullcanvas);
        }

        remaining.forEach(function(video, idx) {
            drawImage(video, idx);
        });

        setTimeout(drawVideosToCanvas, self.frameInterval);
    }

    function drawImage(video, idx) {
        if (isStopDrawingFrames) {
            return;
        }

        var x = 0;
        var y = 0;
        var width = video.width;
        var height = video.height;

        if (idx === 1) {
            x = video.width;
        }

        if (idx === 2) {
            y = video.height;
        }

        if (idx === 3) {
            x = video.width;
            y = video.height;
        }

        if (idx === 4) {
            y = video.height * 2;
        }

        if (idx === 5) {
            x = video.width;
            y = video.height * 2;
        }

        if (idx === 6) {
            y = video.height * 3;
        }

        if (idx === 7) {
            x = video.width;
            y = video.height * 3;
        }

        if (typeof video.stream.left !== 'undefined') {
            x = video.stream.left;
        }

        if (typeof video.stream.top !== 'undefined') {
            y = video.stream.top;
        }

        if (typeof video.stream.width !== 'undefined') {
            width = video.stream.width;
        }

        if (typeof video.stream.height !== 'undefined') {
            height = video.stream.height;
        }

        context.drawImage(video, x, y, width, height);

        if (typeof video.stream.onRender === 'function') {
            video.stream.onRender(context, x, y, width, height, idx);
        }
    }

    function getMixedStream() {
        isStopDrawingFrames = false;
        var mixedVideoStream = getMixedVideoStream();

        var mixedAudioStream = getMixedAudioStream();
        if (mixedAudioStream) {
            mixedAudioStream.getTracks().filter(function(t) {
                return t.kind === 'audio';
            }).forEach(function(track) {
                mixedVideoStream.addTrack(track);
            });
        }

        var fullcanvas;
        arrayOfMediaStreams.forEach(function(stream) {
            if (stream.fullcanvas) {
                fullcanvas = true;
            }
        });

        // mixedVideoStream.prototype.appendStreams = appendStreams;
        // mixedVideoStream.prototype.resetVideoStreams = resetVideoStreams;
        // mixedVideoStream.prototype.clearRecordedData = clearRecordedData;

        return mixedVideoStream;
    }

    function getMixedVideoStream() {
        resetVideoStreams();

        var capturedStream;

        if ('captureStream' in canvas) {
            capturedStream = canvas.captureStream();
        } else if ('mozCaptureStream' in canvas) {
            capturedStream = canvas.mozCaptureStream();
        } else if (!self.disableLogs) {
            console.error('Upgrade to latest Chrome or otherwise enable this flag: chrome://flags/#enable-experimental-web-platform-features');
        }

        var videoStream = new MediaStream();

        capturedStream.getTracks().filter(function(t) {
            return t.kind === 'video';
        }).forEach(function(track) {
            videoStream.addTrack(track);
        });

        canvas.stream = videoStream;

        return videoStream;
    }

    function getMixedAudioStream() {
        // via: @pehrsons
        if (!Storage.AudioContextConstructor) {
            Storage.AudioContextConstructor = new Storage.AudioContext();
        }

        self.audioContext = Storage.AudioContextConstructor;

        self.audioSources = [];

        if (self.useGainNode === true) {
            self.gainNode = self.audioContext.createGain();
            self.gainNode.connect(self.audioContext.destination);
            self.gainNode.gain.value = 0; // don't hear self
        }

        var audioTracksLength = 0;
        arrayOfMediaStreams.forEach(function(stream) {
            if (!stream.getTracks().filter(function(t) {
                    return t.kind === 'audio';
                }).length) {
                return;
            }

            audioTracksLength++;

            var audioSource = self.audioContext.createMediaStreamSource(stream);

            if (self.useGainNode === true) {
                audioSource.connect(self.gainNode);
            }

            self.audioSources.push(audioSource);
        });

        if (!audioTracksLength) {
            // because "self.audioContext" is not initialized
            // that's why we've to ignore rest of the code
            return;
        }

        self.audioDestination = self.audioContext.createMediaStreamDestination();
        self.audioSources.forEach(function(audioSource) {
            audioSource.connect(self.audioDestination);
        });
        return self.audioDestination.stream;
    }

    function getVideo(stream) {
        var video = document.createElement('video');

        setSrcObject(stream, video);

        video.className = elementClass;

        video.muted = true;
        video.volume = 0;

        video.width = stream.width || self.width || 360;
        video.height = stream.height || self.height || 240;

        video.play();

        return video;
    }

    this.appendStreams = function(streams) {
        if (!streams) {
            throw 'First parameter is required.';
        }

        if (!(streams instanceof Array)) {
            streams = [streams];
        }

        streams.forEach(function(stream) {
            var newStream = new MediaStream();

            if (stream.getTracks().filter(function(t) {
                    return t.kind === 'video';
                }).length) {
                var video = getVideo(stream);
                video.stream = stream;
                videos.push(video);

                newStream.addTrack(stream.getTracks().filter(function(t) {
                    return t.kind === 'video';
                })[0]);
            }

            if (stream.getTracks().filter(function(t) {
                    return t.kind === 'audio';
                }).length) {
                var audioSource = self.audioContext.createMediaStreamSource(stream);
                self.audioDestination = self.audioContext.createMediaStreamDestination();
                audioSource.connect(self.audioDestination);

                newStream.addTrack(self.audioDestination.stream.getTracks().filter(function(t) {
                    return t.kind === 'audio';
                })[0]);
            }

            arrayOfMediaStreams.push(newStream);
        });
    };

    this.releaseStreams = function() {
        videos = [];
        isStopDrawingFrames = true;

        if (self.gainNode) {
            self.gainNode.disconnect();
            self.gainNode = null;
        }

        if (self.audioSources.length) {
            self.audioSources.forEach(function(source) {
                source.disconnect();
            });
            self.audioSources = [];
        }

        if (self.audioDestination) {
            self.audioDestination.disconnect();
            self.audioDestination = null;
        }

        if (self.audioContext) {
            self.audioContext.close();
        }

        self.audioContext = null;

        context.clearRect(0, 0, canvas.width, canvas.height);

        if (canvas.stream) {
            canvas.stream.stop();
            canvas.stream = null;
        }
    };

    this.resetVideoStreams = function(streams) {
        if (streams && !(streams instanceof Array)) {
            streams = [streams];
        }

        resetVideoStreams(streams);
    };

    function resetVideoStreams(streams) {
        videos = [];
        streams = streams || arrayOfMediaStreams;

        // via: @adrian-ber
        streams.forEach(function(stream) {
            if (!stream.getTracks().filter(function(t) {
                    return t.kind === 'video';
                }).length) {
                return;
            }

            var video = getVideo(stream);
            video.stream = stream;
            videos.push(video);
        });
    }

    // for debugging
    this.name = 'MultiStreamsMixer';
    this.toString = function() {
        return this.name;
    };

    this.getMixedStream = getMixedStream;

}

if (typeof RecordRTC === 'undefined') {
    if (true /* && !!module.exports*/ ) {
        module.exports = MultiStreamsMixer;
    }

    if (true) {
        !(__WEBPACK_AMD_DEFINE_ARRAY__ = [], __WEBPACK_AMD_DEFINE_RESULT__ = (function() {
            return MultiStreamsMixer;
        }).apply(exports, __WEBPACK_AMD_DEFINE_ARRAY__),
				__WEBPACK_AMD_DEFINE_RESULT__ !== undefined && (module.exports = __WEBPACK_AMD_DEFINE_RESULT__));
    }
}

// ______________________
// MultiStreamRecorder.js

/*
 * Video conference recording, using captureStream API along with WebAudio and Canvas2D API.
 */

/**
 * MultiStreamRecorder can record multiple videos in single container.
 * @summary Multi-videos recorder.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef MultiStreamRecorder
 * @class
 * @example
 * var options = {
 *     mimeType: 'video/webm'
 * }
 * var recorder = new MultiStreamRecorder(ArrayOfMediaStreams, options);
 * recorder.record();
 * recorder.stop(function(blob) {
 *     video.src = URL.createObjectURL(blob);
 *
 *     // or
 *     var blob = recorder.blob;
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStreams} mediaStreams - Array of MediaStreams.
 * @param {object} config - {disableLogs:true, frameInterval: 1, mimeType: "video/webm"}
 */

function MultiStreamRecorder(arrayOfMediaStreams, options) {
    arrayOfMediaStreams = arrayOfMediaStreams || [];
    var self = this;

    var mixer;
    var mediaRecorder;

    options = options || {
        elementClass: 'multi-streams-mixer',
        mimeType: 'video/webm',
        video: {
            width: 360,
            height: 240
        }
    };

    if (!options.frameInterval) {
        options.frameInterval = 10;
    }

    if (!options.video) {
        options.video = {};
    }

    if (!options.video.width) {
        options.video.width = 360;
    }

    if (!options.video.height) {
        options.video.height = 240;
    }

    /**
     * This method records all MediaStreams.
     * @method
     * @memberof MultiStreamRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        // github/muaz-khan/MultiStreamsMixer
        mixer = new MultiStreamsMixer(arrayOfMediaStreams, options.elementClass || 'multi-streams-mixer');

        if (getAllVideoTracks().length) {
            mixer.frameInterval = options.frameInterval || 10;
            mixer.width = options.video.width || 360;
            mixer.height = options.video.height || 240;
            mixer.startDrawingFrames();
        }

        if (options.previewStream && typeof options.previewStream === 'function') {
            options.previewStream(mixer.getMixedStream());
        }

        // record using MediaRecorder API
        mediaRecorder = new MediaStreamRecorder(mixer.getMixedStream(), options);
        mediaRecorder.record();
    };

    function getAllVideoTracks() {
        var tracks = [];
        arrayOfMediaStreams.forEach(function(stream) {
            getTracks(stream, 'video').forEach(function(track) {
                tracks.push(track);
            });
        });
        return tracks;
    }

    /**
     * This method stops recording MediaStream.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof MultiStreamRecorder
     * @example
     * recorder.stop(function(blob) {
     *     video.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        if (!mediaRecorder) {
            return;
        }

        mediaRecorder.stop(function(blob) {
            self.blob = blob;

            callback(blob);

            self.clearRecordedData();
        });
    };

    /**
     * This method pauses the recording process.
     * @method
     * @memberof MultiStreamRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        if (mediaRecorder) {
            mediaRecorder.pause();
        }
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof MultiStreamRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        if (mediaRecorder) {
            mediaRecorder.resume();
        }
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof MultiStreamRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        if (mediaRecorder) {
            mediaRecorder.clearRecordedData();
            mediaRecorder = null;
        }

        if (mixer) {
            mixer.releaseStreams();
            mixer = null;
        }
    };

    /**
     * Add extra media-streams to existing recordings.
     * @method
     * @memberof MultiStreamRecorder
     * @param {MediaStreams} mediaStreams - Array of MediaStreams
     * @example
     * recorder.addStreams([newAudioStream, newVideoStream]);
     */
    this.addStreams = function(streams) {
        if (!streams) {
            throw 'First parameter is required.';
        }

        if (!(streams instanceof Array)) {
            streams = [streams];
        }

        arrayOfMediaStreams.concat(streams);

        if (!mediaRecorder || !mixer) {
            return;
        }

        mixer.appendStreams(streams);

        if (options.previewStream && typeof options.previewStream === 'function') {
            options.previewStream(mixer.getMixedStream());
        }
    };

    /**
     * Reset videos during live recording. Replace old videos e.g. replace cameras with full-screen.
     * @method
     * @memberof MultiStreamRecorder
     * @param {MediaStreams} mediaStreams - Array of MediaStreams
     * @example
     * recorder.resetVideoStreams([newVideo1, newVideo2]);
     */
    this.resetVideoStreams = function(streams) {
        if (!mixer) {
            return;
        }

        if (streams && !(streams instanceof Array)) {
            streams = [streams];
        }

        mixer.resetVideoStreams(streams);
    };

    /**
     * Returns MultiStreamsMixer
     * @method
     * @memberof MultiStreamRecorder
     * @example
     * let mixer = recorder.getMixer();
     * mixer.appendStreams([newStream]);
     */
    this.getMixer = function() {
        return mixer;
    };

    // for debugging
    this.name = 'MultiStreamRecorder';
    this.toString = function() {
        return this.name;
    };
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.MultiStreamRecorder = MultiStreamRecorder;
}

// _____________________
// RecordRTC.promises.js

/**
 * RecordRTCPromisesHandler adds promises support in {@link RecordRTC}. Try a {@link https://github.com/muaz-khan/RecordRTC/blob/master/simple-demos/RecordRTCPromisesHandler.html|demo here}
 * @summary Promises for {@link RecordRTC}
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef RecordRTCPromisesHandler
 * @class
 * @example
 * var recorder = new RecordRTCPromisesHandler(mediaStream, options);
 * recorder.startRecording()
 *         .then(successCB)
 *         .catch(errorCB);
 * // Note: You can access all RecordRTC API using "recorder.recordRTC" e.g. 
 * recorder.recordRTC.onStateChanged = function(state) {};
 * recorder.recordRTC.setRecordingDuration(5000);
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - Single media-stream object, array of media-streams, html-canvas-element, etc.
 * @param {object} config - {type:"video", recorderType: MediaStreamRecorder, disableLogs: true, numberOfAudioChannels: 1, bufferSize: 0, sampleRate: 0, video: HTMLVideoElement, etc.}
 * @throws Will throw an error if "new" keyword is not used to initiate "RecordRTCPromisesHandler". Also throws error if first argument "MediaStream" is missing.
 * @requires {@link RecordRTC}
 */

function RecordRTCPromisesHandler(mediaStream, options) {
    if (!this) {
        throw 'Use "new RecordRTCPromisesHandler()"';
    }

    if (typeof mediaStream === 'undefined') {
        throw 'First argument "MediaStream" is required.';
    }

    var self = this;

    /**
     * @property {Blob} blob - Access/reach the native {@link RecordRTC} object.
     * @memberof RecordRTCPromisesHandler
     * @example
     * let internal = recorder.recordRTC.getInternalRecorder();
     * alert(internal instanceof MediaStreamRecorder);
     * recorder.recordRTC.onStateChanged = function(state) {};
     */
    self.recordRTC = new RecordRTC(mediaStream, options);

    /**
     * This method records MediaStream.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.startRecording()
     *         .then(successCB)
     *         .catch(errorCB);
     */
    this.startRecording = function() {
        return new Promise(function(resolve, reject) {
            try {
                self.recordRTC.startRecording();
                resolve();
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method stops the recording.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.stopRecording().then(function() {
     *     var blob = recorder.getBlob();
     * }).catch(errorCB);
     */
    this.stopRecording = function() {
        return new Promise(function(resolve, reject) {
            try {
                self.recordRTC.stopRecording(function(url) {
                    self.blob = self.recordRTC.getBlob();

                    if (!self.blob || !self.blob.size) {
                        reject('Empty blob.', self.blob);
                        return;
                    }

                    resolve(url);
                });
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method pauses the recording. You can resume recording using "resumeRecording" method.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.pauseRecording()
     *         .then(successCB)
     *         .catch(errorCB);
     */
    this.pauseRecording = function() {
        return new Promise(function(resolve, reject) {
            try {
                self.recordRTC.pauseRecording();
                resolve();
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method resumes the recording.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.resumeRecording()
     *         .then(successCB)
     *         .catch(errorCB);
     */
    this.resumeRecording = function() {
        return new Promise(function(resolve, reject) {
            try {
                self.recordRTC.resumeRecording();
                resolve();
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method returns data-url for the recorded blob.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.stopRecording().then(function() {
     *     recorder.getDataURL().then(function(dataURL) {
     *         window.open(dataURL);
     *     }).catch(errorCB);;
     * }).catch(errorCB);
     */
    this.getDataURL = function(callback) {
        return new Promise(function(resolve, reject) {
            try {
                self.recordRTC.getDataURL(function(dataURL) {
                    resolve(dataURL);
                });
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method returns the recorded blob.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.stopRecording().then(function() {
     *     recorder.getBlob().then(function(blob) {})
     * }).catch(errorCB);
     */
    this.getBlob = function() {
        return new Promise(function(resolve, reject) {
            try {
                resolve(self.recordRTC.getBlob());
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method returns the internal recording object.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * let internalRecorder = await recorder.getInternalRecorder();
     * if(internalRecorder instanceof MultiStreamRecorder) {
     *     internalRecorder.addStreams([newAudioStream]);
     *     internalRecorder.resetVideoStreams([screenStream]);
     * }
     * @returns {Object} 
     */
    this.getInternalRecorder = function() {
        return new Promise(function(resolve, reject) {
            try {
                resolve(self.recordRTC.getInternalRecorder());
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * This method resets the recorder. So that you can reuse single recorder instance many times.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * await recorder.reset();
     * recorder.startRecording(); // record again
     */
    this.reset = function() {
        return new Promise(function(resolve, reject) {
            try {
                resolve(self.recordRTC.reset());
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * Destroy RecordRTC instance. Clear all recorders and objects.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * recorder.destroy().then(successCB).catch(errorCB);
     */
    this.destroy = function() {
        return new Promise(function(resolve, reject) {
            try {
                resolve(self.recordRTC.destroy());
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * Get recorder's readonly state.
     * @method
     * @memberof RecordRTCPromisesHandler
     * @example
     * let state = await recorder.getState();
     * // or
     * recorder.getState().then(state => { console.log(state); })
     * @returns {String} Returns recording state.
     */
    this.getState = function() {
        return new Promise(function(resolve, reject) {
            try {
                resolve(self.recordRTC.getState());
            } catch (e) {
                reject(e);
            }
        });
    };

    /**
     * @property {Blob} blob - Recorded data as "Blob" object.
     * @memberof RecordRTCPromisesHandler
     * @example
     * await recorder.stopRecording();
     * let blob = recorder.getBlob(); // or "recorder.recordRTC.blob"
     * invokeSaveAsDialog(blob);
     */
    this.blob = null;

    /**
     * RecordRTC version number
     * @property {String} version - Release version number.
     * @memberof RecordRTCPromisesHandler
     * @static
     * @readonly
     * @example
     * alert(recorder.version);
     */
    this.version = '5.5.9';
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.RecordRTCPromisesHandler = RecordRTCPromisesHandler;
}

// ______________________
// WebAssemblyRecorder.js

/**
 * WebAssemblyRecorder lets you create webm videos in JavaScript via WebAssembly. The library consumes raw RGBA32 buffers (4 bytes per pixel) and turns them into a webm video with the given framerate and quality. This makes it compatible out-of-the-box with ImageData from a CANVAS. With realtime mode you can also use webm-wasm for streaming webm videos.
 * @summary Video recording feature in Chrome, Firefox and maybe Edge.
 * @license {@link https://github.com/muaz-khan/RecordRTC/blob/master/LICENSE|MIT}
 * @author {@link https://MuazKhan.com|Muaz Khan}
 * @typedef WebAssemblyRecorder
 * @class
 * @example
 * var recorder = new WebAssemblyRecorder(mediaStream);
 * recorder.record();
 * recorder.stop(function(blob) {
 *     video.src = URL.createObjectURL(blob);
 * });
 * @see {@link https://github.com/muaz-khan/RecordRTC|RecordRTC Source Code}
 * @param {MediaStream} mediaStream - MediaStream object fetched using getUserMedia API or generated using captureStreamUntilEnded or WebAudio API.
 * @param {object} config - {webAssemblyPath:'webm-wasm.wasm',workerPath: 'webm-worker.js', frameRate: 30, width: 1920, height: 1080, bitrate: 1024}
 */
function WebAssemblyRecorder(stream, config) {
    // based on: github.com/GoogleChromeLabs/webm-wasm

    if (typeof ReadableStream === 'undefined' || typeof WritableStream === 'undefined') {
        // because it fixes readable/writable streams issues
        console.error('Following polyfill is strongly recommended: https://unpkg.com/@mattiasbuelens/web-streams-polyfill/dist/polyfill.min.js');
    }

    config = config || {};

    config.width = config.width || 640;
    config.height = config.height || 480;
    config.frameRate = config.frameRate || 30;
    config.bitrate = config.bitrate || 1200;

    function createBufferURL(buffer, type) {
        return URL.createObjectURL(new Blob([buffer], {
            type: type || ''
        }));
    }

    function cameraStream() {
        return new ReadableStream({
            start: function(controller) {
                var cvs = document.createElement('canvas');
                var video = document.createElement('video');
                video.srcObject = stream;
                video.onplaying = function() {
                    cvs.width = config.width;
                    cvs.height = config.height;
                    var ctx = cvs.getContext('2d');
                    var frameTimeout = 1000 / config.frameRate;
                    setTimeout(function f() {
                        ctx.drawImage(video, 0, 0);
                        controller.enqueue(
                            ctx.getImageData(0, 0, config.width, config.height)
                        );
                        setTimeout(f, frameTimeout);
                    }, frameTimeout);
                };
                video.play();
            }
        });
    }

    var worker;

    function startRecording(stream, buffer) {
        if (!config.workerPath && !buffer) {
            // is it safe to use @latest ?
            fetch(
                'https://unpkg.com/webm-wasm@latest/dist/webm-worker.js'
            ).then(function(r) {
                r.arrayBuffer().then(function(buffer) {
                    startRecording(stream, buffer);
                });
            });
            return;
        }

        if (!config.workerPath && buffer instanceof ArrayBuffer) {
            var blob = new Blob([buffer], {
                type: 'text/javascript'
            });
            config.workerPath = URL.createObjectURL(blob);
        }

        if (!config.workerPath) {
            console.error('workerPath parameter is missing.');
        }

        worker = new Worker(config.workerPath);

        worker.postMessage(config.webAssemblyPath || 'https://unpkg.com/webm-wasm@latest/dist/webm-wasm.wasm');
        worker.addEventListener('message', function(event) {
            if (event.data === 'READY') {
                worker.postMessage({
                    width: config.width,
                    height: config.height,
                    bitrate: config.bitrate || 1200,
                    timebaseDen: config.frameRate || 30,
                    realtime: true
                });

                cameraStream().pipeTo(new WritableStream({
                    write: function(image) {
                        if (!worker) {
                            return;
                        }

                        worker.postMessage(image.data.buffer, [image.data.buffer]);
                    }
                }));
            } else if (!!event.data) {
                if (!isPaused) {
                    arrayOfBuffers.push(event.data);
                }
            }
        });
    }

    /**
     * This method records video.
     * @method
     * @memberof WebAssemblyRecorder
     * @example
     * recorder.record();
     */
    this.record = function() {
        arrayOfBuffers = [];
        isPaused = false;
        this.blob = null;
        startRecording(stream);

        if (typeof config.initCallback === 'function') {
            config.initCallback();
        }
    };

    var isPaused;

    /**
     * This method pauses the recording process.
     * @method
     * @memberof WebAssemblyRecorder
     * @example
     * recorder.pause();
     */
    this.pause = function() {
        isPaused = true;
    };

    /**
     * This method resumes the recording process.
     * @method
     * @memberof WebAssemblyRecorder
     * @example
     * recorder.resume();
     */
    this.resume = function() {
        isPaused = false;
    };

    function terminate() {
        if (!worker) {
            return;
        }

        worker.postMessage(null);
        worker.terminate();
        worker = null;
    }

    var arrayOfBuffers = [];

    /**
     * This method stops recording video.
     * @param {function} callback - Callback function, that is used to pass recorded blob back to the callee.
     * @method
     * @memberof WebAssemblyRecorder
     * @example
     * recorder.stop(function(blob) {
     *     video.src = URL.createObjectURL(blob);
     * });
     */
    this.stop = function(callback) {
        terminate();

        this.blob = new Blob(arrayOfBuffers, {
            type: 'video/webm'
        });

        callback(this.blob);
    };

    // for debugging
    this.name = 'WebAssemblyRecorder';
    this.toString = function() {
        return this.name;
    };

    /**
     * This method resets currently recorded data.
     * @method
     * @memberof WebAssemblyRecorder
     * @example
     * recorder.clearRecordedData();
     */
    this.clearRecordedData = function() {
        arrayOfBuffers = [];
        isPaused = false;
        this.blob = null;

        // todo: if recording-ON then STOP it first
    };

    /**
     * @property {Blob} blob - The recorded blob object.
     * @memberof WebAssemblyRecorder
     * @example
     * recorder.stop(function(){
     *     var blob = recorder.blob;
     * });
     */
    this.blob = null;
}

if (typeof RecordRTC !== 'undefined') {
    RecordRTC.WebAssemblyRecorder = WebAssemblyRecorder;
}

/* WEBPACK VAR INJECTION */}.call(exports, __webpack_require__(18), __webpack_require__(19)))

/***/ }),

/***/ 18:
/***/ (function(module, exports) {

var g;

// This works in non-strict mode
g = (function() {
	return this;
})();

try {
	// This works if eval is allowed (see CSP)
	g = g || Function("return this")() || (1,eval)("this");
} catch(e) {
	// This works if the window reference is available
	if(typeof window === "object")
		g = window;
}

// g can still be undefined, but nothing to do about it...
// We return undefined, instead of nothing here, so it's
// easier to handle this case. if(!global) { ...}

module.exports = g;


/***/ }),

/***/ 19:
/***/ (function(module, exports) {

// shim for using process in browser
var process = module.exports = {};

// cached from whatever global is present so that test runners that stub it
// don't break things.  But we need to wrap it in a try catch in case it is
// wrapped in strict mode code which doesn't define any globals.  It's inside a
// function because try/catches deoptimize in certain engines.

var cachedSetTimeout;
var cachedClearTimeout;

function defaultSetTimout() {
    throw new Error('setTimeout has not been defined');
}
function defaultClearTimeout () {
    throw new Error('clearTimeout has not been defined');
}
(function () {
    try {
        if (typeof setTimeout === 'function') {
            cachedSetTimeout = setTimeout;
        } else {
            cachedSetTimeout = defaultSetTimout;
        }
    } catch (e) {
        cachedSetTimeout = defaultSetTimout;
    }
    try {
        if (typeof clearTimeout === 'function') {
            cachedClearTimeout = clearTimeout;
        } else {
            cachedClearTimeout = defaultClearTimeout;
        }
    } catch (e) {
        cachedClearTimeout = defaultClearTimeout;
    }
} ())
function runTimeout(fun) {
    if (cachedSetTimeout === setTimeout) {
        //normal enviroments in sane situations
        return setTimeout(fun, 0);
    }
    // if setTimeout wasn't available but was latter defined
    if ((cachedSetTimeout === defaultSetTimout || !cachedSetTimeout) && setTimeout) {
        cachedSetTimeout = setTimeout;
        return setTimeout(fun, 0);
    }
    try {
        // when when somebody has screwed with setTimeout but no I.E. maddness
        return cachedSetTimeout(fun, 0);
    } catch(e){
        try {
            // When we are in I.E. but the script has been evaled so I.E. doesn't trust the global object when called normally
            return cachedSetTimeout.call(null, fun, 0);
        } catch(e){
            // same as above but when it's a version of I.E. that must have the global object for 'this', hopfully our context correct otherwise it will throw a global error
            return cachedSetTimeout.call(this, fun, 0);
        }
    }


}
function runClearTimeout(marker) {
    if (cachedClearTimeout === clearTimeout) {
        //normal enviroments in sane situations
        return clearTimeout(marker);
    }
    // if clearTimeout wasn't available but was latter defined
    if ((cachedClearTimeout === defaultClearTimeout || !cachedClearTimeout) && clearTimeout) {
        cachedClearTimeout = clearTimeout;
        return clearTimeout(marker);
    }
    try {
        // when when somebody has screwed with setTimeout but no I.E. maddness
        return cachedClearTimeout(marker);
    } catch (e){
        try {
            // When we are in I.E. but the script has been evaled so I.E. doesn't  trust the global object when called normally
            return cachedClearTimeout.call(null, marker);
        } catch (e){
            // same as above but when it's a version of I.E. that must have the global object for 'this', hopfully our context correct otherwise it will throw a global error.
            // Some versions of I.E. have different rules for clearTimeout vs setTimeout
            return cachedClearTimeout.call(this, marker);
        }
    }



}
var queue = [];
var draining = false;
var currentQueue;
var queueIndex = -1;

function cleanUpNextTick() {
    if (!draining || !currentQueue) {
        return;
    }
    draining = false;
    if (currentQueue.length) {
        queue = currentQueue.concat(queue);
    } else {
        queueIndex = -1;
    }
    if (queue.length) {
        drainQueue();
    }
}

function drainQueue() {
    if (draining) {
        return;
    }
    var timeout = runTimeout(cleanUpNextTick);
    draining = true;

    var len = queue.length;
    while(len) {
        currentQueue = queue;
        queue = [];
        while (++queueIndex < len) {
            if (currentQueue) {
                currentQueue[queueIndex].run();
            }
        }
        queueIndex = -1;
        len = queue.length;
    }
    currentQueue = null;
    draining = false;
    runClearTimeout(timeout);
}

process.nextTick = function (fun) {
    var args = new Array(arguments.length - 1);
    if (arguments.length > 1) {
        for (var i = 1; i < arguments.length; i++) {
            args[i - 1] = arguments[i];
        }
    }
    queue.push(new Item(fun, args));
    if (queue.length === 1 && !draining) {
        runTimeout(drainQueue);
    }
};

// v8 likes predictible objects
function Item(fun, array) {
    this.fun = fun;
    this.array = array;
}
Item.prototype.run = function () {
    this.fun.apply(null, this.array);
};
process.title = 'browser';
process.browser = true;
process.env = {};
process.argv = [];
process.version = ''; // empty string to avoid regexp issues
process.versions = {};

function noop() {}

process.on = noop;
process.addListener = noop;
process.once = noop;
process.off = noop;
process.removeListener = noop;
process.removeAllListeners = noop;
process.emit = noop;
process.prependListener = noop;
process.prependOnceListener = noop;

process.listeners = function (name) { return [] }

process.binding = function (name) {
    throw new Error('process.binding is not supported');
};

process.cwd = function () { return '/' };
process.chdir = function (dir) {
    throw new Error('process.chdir is not supported');
};
process.umask = function() { return 0; };


/***/ })

/******/ });
});
//# sourceMappingURL=data:application/json;charset=utf-8;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIndlYnBhY2s6Ly8vd2VicGFjay91bml2ZXJzYWxNb2R1bGVEZWZpbml0aW9uIiwid2VicGFjazovLy93ZWJwYWNrL2Jvb3RzdHJhcCBmOWIyNGFiYzlmODA1NTBmYmQzZiIsIndlYnBhY2s6Ly8vLi9zcmMvbWljcm9waG9uZS5qcyIsIndlYnBhY2s6Ly8vLi9ub2RlX21vZHVsZXMvcmVjb3JkcnRjL1JlY29yZFJUQy5qcyIsIndlYnBhY2s6Ly8vKHdlYnBhY2spL2J1aWxkaW4vZ2xvYmFsLmpzIiwid2VicGFjazovLy8uL25vZGVfbW9kdWxlcy9wcm9jZXNzL2Jyb3dzZXIuanMiXSwibmFtZXMiOltdLCJtYXBwaW5ncyI6IkFBQUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsQ0FBQztBQUNELE87UUNWQTtRQUNBOztRQUVBO1FBQ0E7O1FBRUE7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7O1FBRUE7UUFDQTs7UUFFQTtRQUNBOztRQUVBO1FBQ0E7UUFDQTs7O1FBR0E7UUFDQTs7UUFFQTtRQUNBOztRQUVBO1FBQ0E7UUFDQTtRQUNBO1FBQ0E7UUFDQTtRQUNBO1FBQ0EsS0FBSztRQUNMO1FBQ0E7O1FBRUE7UUFDQTtRQUNBO1FBQ0EsMkJBQTJCLDBCQUEwQixFQUFFO1FBQ3ZELGlDQUFpQyxlQUFlO1FBQ2hEO1FBQ0E7UUFDQTs7UUFFQTtRQUNBLHNEQUFzRCwrREFBK0Q7O1FBRXJIO1FBQ0E7O1FBRUE7UUFDQTs7Ozs7Ozs7O0FDN0RBO0FBQUE7QUFBQTtBQUFrQzs7QUFFbEM7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0Esb0JBQW9CLGlEQUFTO0FBQzdCLEtBQUs7QUFDTDtBQUNBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7QUFDQSx3RUFBd0U7QUFDeEUsYUFBYSw4REFBOEQ7QUFDM0UsYUFBYTtBQUNiLDJCQUEyQjtBQUMzQix5QkFBeUI7QUFDekIsbUJBQW1CO0FBQ25CLGdE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Q0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVMsbUNBQW1DO0FBQzVDO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsK0JBQStCLGtCQUFrQjtBQUNqRDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBLCtCQUErQixTQUFTO0FBQ3hDO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTs7QUFFQTs7QUFFQSxtQ0FBbUMsZ0JBQWdCO0FBQ25EO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0EsK0JBQStCLFNBQVM7QUFDeEM7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTs7QUFFQTtBQUNBOztBQUVBOztBQUVBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQSwyQkFBMkIsU0FBUztBQUNwQztBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQSxpQkFBaUI7QUFDakI7O0FBRUE7QUFDQTtBQUNBO0FBQ0EsYUFBYTtBQUNiOztBQUVBO0FBQ0E7QUFDQTtBQUNBLGFBQWE7QUFDYjtBQUNBOzs7QUFHQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLGNBQWMsa0NBQWtDLGtDQUFrQztBQUNsRjtBQUNBO0FBQ0EsU0FBUzs7QUFFVDtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0EsZUFBZSxTQUFTO0FBQ3hCO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxRQUFRO0FBQ1I7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBLDBCQUEwQixLQUFLO0FBQy9CO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsZ0JBQWdCO0FBQ2hCO0FBQ0E7QUFDQTtBQUNBLGFBQWE7O0FBRWI7QUFDQSwwQkFBMEIsWUFBWTtBQUN0QztBQUNBO0FBQ0E7QUFDQTtBQUNBLGdCQUFnQjtBQUNoQjtBQUNBOztBQUVBO0FBQ0EsMEJBQTBCLFNBQVM7QUFDbkM7QUFDQTtBQUNBO0FBQ0E7QUFDQSxnQkFBZ0I7QUFDaEI7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTs7QUFFQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0Esa0JBQWtCLE9BQU87QUFDekI7QUFDQTtBQUNBO0FBQ0E7QUFDQSxRQUFRO0FBQ1I7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQSxLQUFLO0FBQ0w7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0EsNENBQTRDO0FBQzVDOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0Esa0JBQWtCLE9BQU87QUFDekI7QUFDQTtBQUNBO0FBQ0E7QUFDQSxRQUFRO0FBQ1I7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLG9CQUFvQixTQUFTO0FBQzdCO0FBQ0E7QUFDQSx1REFB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O0FBQ0EsYUFBYTtBQUNiOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsaUJBQWlCO0FBQ2pCO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBLDJCQUEyQixtQkFBbUI7QUFDOUM7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBLGFBQWE7QUFDYjs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLDJCQUEyQixpQkFBaUI7QUFDNUM7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLDJCQUEyQixpQkFBaUI7QUFDNUM7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0EsYUFBYTtBQUNiOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsMkJBQTJCLGlCQUFpQjtBQUM1QztBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLGFBQWE7O0FBRWI7QUFDQTs7QUFFQTtBQUNBOztBQUVBLHlEQUF5RDtBQUN6RCxtQ0FBbUMsT0FBTztBQUMxQztBQUNBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLGFBQWE7QUFDYjs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxpQkFBaUI7QUFDakI7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLGFBQWE7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QUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0EsaUJBQWlCO0FBQ2pCOztBQUVBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxlQUFlLFNBQVM7QUFDeEI7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFFBQVE7QUFDUjtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBOztBQUVBO0FBQ0Esc0JBQXNCLEtBQUs7QUFDM0I7QUFDQTtBQUNBO0FBQ0E7QUFDQSxZQUFZO0FBQ1o7QUFDQTtBQUNBO0FBQ0EsU0FBUzs7QUFFVDs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBO0FBQ0E7QUFDQTs7QUFFQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTs7QUFFQTtBQUNBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLHFCQUFxQjtBQUNyQix1Q0FBdUM7QUFDdkMsd0NBQXdDO0FBQ3hDLDRDQUE0QztBQUM1QztBQUNBO0FBQ0EscUJBQXFCO0FBQ3JCO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsaUJBQWlCO0FBQ2pCO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLEtBQUs7O0FBRUw7O0FBRUE7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBLDRGQUE0RjtBQUM1RjtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsaUJBQWlCO0FBQ2pCO0FBQ0E7QUFDQTs7QUFFQTs7QUFFQTtBQUNBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0EsU0FBUztBQUNUO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxhQUFhO0FBQ2I7QUFDQTtBQUNBO0FBQ0EsU0FBUzs7QUFFVDtBQUNBO0FBQ0E7QUFDQSxTQUFTO0FBQ1Q7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxTQUFTOztBQUVUO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQSxhQUFhO0FBQ2I7QUFDQSxhQUFhO0FBQ2I7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7O0FBRVQ7QUFDQTtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTs7QUFFQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0EsU0FBUztBQUNUO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBLFNBQVM7O0FBRVQ7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBLHlDQUF5QztBQUN6Qzs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLGlCQUFpQjtBQUNqQjtBQUNBOztBQUVBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBLFNBQVM7O0FBRVQ7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQSxTQUFTO0FBQ1Q7QUFDQTs7QUFFQTtBQUNBOztBQUVBOztBQUVBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxpQkFBaUI7QUFDakI7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQSxpQkFBaUI7QUFDakI7O0FBRUE7QUFDQTtBQUNBLGlCQUFpQjtBQUNqQjtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBLGlCQUFpQjtBQUNqQjs7QUFFQTtBQUNBLFNBQVM7QUFDVDs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0EsYUFBYTtBQUNiO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBOztBQUVBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQSxpQkFBaUI7QUFDakI7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQSxTQUFTO0FBQ1Q7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTs7QUFFQTs7QUFFQTtBQUNBLFFBQVEsSUFBNkI7QUFDckM7QUFDQTs7QUFFQSxRQUFRLElBQTBDO0FBQ2xELFFBQVEsaUNBQTRCLEVBQUUsbUNBQUU7QUFDeEM7QUFDQSxTQUFTO0FBQUEsb0dBQUM7QUFDVjtBQUNBOztBQUVBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBLGFBQWE7QUFDYixZQUFZO0FB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QTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7O0FBRUE7QUFDQTtBQUNBLGVBQWUsU0FBUztBQUN4QjtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsUUFBUTtBQUNSO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0EsU0FBUzs7QUFFVDtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0Esa0JBQWtCLEtBQUs7QUFDdkI7QUFDQTtBQUNBO0FBQ0E7QUFDQSxRQUFRO0FBQ1I7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTs7Ozs7Ozs7O0FDbGhNQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQSxDQUFDOztBQUVEO0FBQ0E7QUFDQTtBQUNBLENBQUM7QUFDRDtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0EsNENBQTRDOztBQUU1Qzs7Ozs7Ozs7QUNwQkE7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7QUFDQSxDQUFDO0FBQ0Q7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxLQUFLO0FBQ0w7QUFDQTtBQUNBO0FBQ0EsU0FBUztBQUNUO0FBQ0E7QUFDQTtBQUNBOzs7QUFHQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0EsS0FBSztBQUNMO0FBQ0E7QUFDQTtBQUNBLFNBQVM7QUFDVDtBQUNBO0FBQ0E7QUFDQTtBQUNBOzs7O0FBSUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBLEtBQUs7QUFDTDtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBO0FBQ0E7QUFDQTtBQUNBLHVCQUF1QixzQkFBc0I7QUFDN0M7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTs7QUFFQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQSxxQkFBcUI7QUFDckI7O0FBRUE7O0FBRUE7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBO0FBQ0E7QUFDQTtBQUNBOztBQUVBLHFDQUFxQzs7QUFFckM7QUFDQTtBQUNBOztBQUVBLDJCQUEyQjtBQUMzQjtBQUNBO0FBQ0E7QUFDQSw0QkFBNEIsVUFBVSIsImZpbGUiOiJ3aWRnZXRzL21pY3JvcGhvbmUuanMiLCJzb3VyY2VzQ29udGVudCI6WyIoZnVuY3Rpb24gd2VicGFja1VuaXZlcnNhbE1vZHVsZURlZmluaXRpb24ocm9vdCwgZmFjdG9yeSkge1xuXHRpZih0eXBlb2YgZXhwb3J0cyA9PT0gJ29iamVjdCcgJiYgdHlwZW9mIG1vZHVsZSA9PT0gJ29iamVjdCcpXG5cdFx0bW9kdWxlLmV4cG9ydHMgPSBmYWN0b3J5KCk7XG5cdGVsc2UgaWYodHlwZW9mIGRlZmluZSA9PT0gJ2Z1bmN0aW9uJyAmJiBkZWZpbmUuYW1kKVxuXHRcdGRlZmluZShcIndpZGdldHMvbWljcm9waG9uZVwiLCBbXSwgZmFjdG9yeSk7XG5cdGVsc2UgaWYodHlwZW9mIGV4cG9ydHMgPT09ICdvYmplY3QnKVxuXHRcdGV4cG9ydHNbXCJ3aWRnZXRzL21pY3JvcGhvbmVcIl0gPSBmYWN0b3J5KCk7XG5cdGVsc2Vcblx0XHRyb290W1wid2lkZ2V0cy9taWNyb3Bob25lXCJdID0gZmFjdG9yeSgpO1xufSkodHlwZW9mIHNlbGYgIT09ICd1bmRlZmluZWQnID8gc2VsZiA6IHRoaXMsIGZ1bmN0aW9uKCkge1xucmV0dXJuIFxuXG5cbi8vIFdFQlBBQ0sgRk9PVEVSIC8vXG4vLyB3ZWJwYWNrL3VuaXZlcnNhbE1vZHVsZURlZmluaXRpb24iLCIgXHQvLyBUaGUgbW9kdWxlIGNhY2hlXG4gXHR2YXIgaW5zdGFsbGVkTW9kdWxlcyA9IHt9O1xuXG4gXHQvLyBUaGUgcmVxdWlyZSBmdW5jdGlvblxuIFx0ZnVuY3Rpb24gX193ZWJwYWNrX3JlcXVpcmVfXyhtb2R1bGVJZCkge1xuXG4gXHRcdC8vIENoZWNrIGlmIG1vZHVsZSBpcyBpbiBjYWNoZVxuIFx0XHRpZihpbnN0YWxsZWRNb2R1bGVzW21vZHVsZUlkXSkge1xuIFx0XHRcdHJldHVybiBpbnN0YWxsZWRNb2R1bGVzW21vZHVsZUlkXS5leHBvcnRzO1xuIFx0XHR9XG4gXHRcdC8vIENyZWF0ZSBhIG5ldyBtb2R1bGUgKGFuZCBwdXQgaXQgaW50byB0aGUgY2FjaGUpXG4gXHRcdHZhciBtb2R1bGUgPSBpbnN0YWxsZWRNb2R1bGVzW21vZHVsZUlkXSA9IHtcbiBcdFx0XHRpOiBtb2R1bGVJZCxcbiBcdFx0XHRsOiBmYWxzZSxcbiBcdFx0XHRleHBvcnRzOiB7fVxuIFx0XHR9O1xuXG4gXHRcdC8vIEV4ZWN1dGUgdGhlIG1vZHVsZSBmdW5jdGlvblxuIFx0XHRtb2R1bGVzW21vZHVsZUlkXS5jYWxsKG1vZHVsZS5leHBvcnRzLCBtb2R1bGUsIG1vZHVsZS5leHBvcnRzLCBfX3dlYnBhY2tfcmVxdWlyZV9fKTtcblxuIFx0XHQvLyBGbGFnIHRoZSBtb2R1bGUgYXMgbG9hZGVkXG4gXHRcdG1vZHVsZS5sID0gdHJ1ZTtcblxuIFx0XHQvLyBSZXR1cm4gdGhlIGV4cG9ydHMgb2YgdGhlIG1vZHVsZVxuIFx0XHRyZXR1cm4gbW9kdWxlLmV4cG9ydHM7XG4gXHR9XG5cblxuIFx0Ly8gZXhwb3NlIHRoZSBtb2R1bGVzIG9iamVjdCAoX193ZWJwYWNrX21vZHVsZXNfXylcbiBcdF9fd2VicGFja19yZXF1aXJlX18ubSA9IG1vZHVsZXM7XG5cbiBcdC8vIGV4cG9zZSB0aGUgbW9kdWxlIGNhY2hlXG4gXHRfX3dlYnBhY2tfcmVxdWlyZV9fLmMgPSBpbnN0YWxsZWRNb2R1bGVzO1xuXG4gXHQvLyBkZWZpbmUgZ2V0dGVyIGZ1bmN0aW9uIGZvciBoYXJtb255IGV4cG9ydHNcbiBcdF9fd2VicGFja19yZXF1aXJlX18uZCA9IGZ1bmN0aW9uKGV4cG9ydHMsIG5hbWUsIGdldHRlcikge1xuIFx0XHRpZighX193ZWJwYWNrX3JlcXVpcmVfXy5vKGV4cG9ydHMsIG5hbWUpKSB7XG4gXHRcdFx0T2JqZWN0LmRlZmluZVByb3BlcnR5KGV4cG9ydHMsIG5hbWUsIHtcbiBcdFx0XHRcdGNvbmZpZ3VyYWJsZTogZmFsc2UsXG4gXHRcdFx0XHRlbnVtZXJhYmxlOiB0cnVlLFxuIFx0XHRcdFx0Z2V0OiBnZXR0ZXJcbiBcdFx0XHR9KTtcbiBcdFx0fVxuIFx0fTtcblxuIFx0Ly8gZ2V0RGVmYXVsdEV4cG9ydCBmdW5jdGlvbiBmb3IgY29tcGF0aWJpbGl0eSB3aXRoIG5vbi1oYXJtb255IG1vZHVsZXNcbiBcdF9fd2VicGFja19yZXF1aXJlX18ubiA9IGZ1bmN0aW9uKG1vZHVsZSkge1xuIFx0XHR2YXIgZ2V0dGVyID0gbW9kdWxlICYmIG1vZHVsZS5fX2VzTW9kdWxlID9cbiBcdFx0XHRmdW5jdGlvbiBnZXREZWZhdWx0KCkgeyByZXR1cm4gbW9kdWxlWydkZWZhdWx0J107IH0gOlxuIFx0XHRcdGZ1bmN0aW9uIGdldE1vZHVsZUV4cG9ydHMoKSB7IHJldHVybiBtb2R1bGU7IH07XG4gXHRcdF9fd2VicGFja19yZXF1aXJlX18uZChnZXR0ZXIsICdhJywgZ2V0dGVyKTtcbiBcdFx0cmV0dXJuIGdldHRlcjtcbiBcdH07XG5cbiBcdC8vIE9iamVjdC5wcm90b3R5cGUuaGFzT3duUHJvcGVydHkuY2FsbFxuIFx0X193ZWJwYWNrX3JlcXVpcmVfXy5vID0gZnVuY3Rpb24ob2JqZWN0LCBwcm9wZXJ0eSkgeyByZXR1cm4gT2JqZWN0LnByb3RvdHlwZS5oYXNPd25Qcm9wZXJ0eS5jYWxsKG9iamVjdCwgcHJvcGVydHkpOyB9O1xuXG4gXHQvLyBfX3dlYnBhY2tfcHVibGljX3BhdGhfX1xuIFx0X193ZWJwYWNrX3JlcXVpcmVfXy5wID0gXCJcIjtcblxuIFx0Ly8gTG9hZCBlbnRyeSBtb2R1bGUgYW5kIHJldHVybiBleHBvcnRzXG4gXHRyZXR1cm4gX193ZWJwYWNrX3JlcXVpcmVfXyhfX3dlYnBhY2tfcmVxdWlyZV9fLnMgPSAxNik7XG5cblxuXG4vLyBXRUJQQUNLIEZPT1RFUiAvL1xuLy8gd2VicGFjay9ib290c3RyYXAgZjliMjRhYmM5ZjgwNTUwZmJkM2YiLCJpbXBvcnQgUmVjb3JkUlRDIGZyb20gXCJyZWNvcmRydGNcIjtcblxuZnVuY3Rpb24gaW5pdChTdXJ2ZXkpIHtcbiAgdmFyIHdpZGdldCA9IHtcbiAgICBuYW1lOiBcIm1pY3JvcGhvbmVcIixcbiAgICB0aXRsZTogXCJNaWNyb3Bob25lXCIsXG4gICAgaWNvbk5hbWU6IFwiaWNvbi1taWNyb3Bob25lXCIsXG4gICAgd2lkZ2V0SXNMb2FkZWQ6IGZ1bmN0aW9uKCkge1xuICAgICAgcmV0dXJuIHR5cGVvZiBSZWNvcmRSVEMgIT0gXCJ1bmRlZmluZWRcIjtcbiAgICB9LFxuICAgIGlzRml0OiBmdW5jdGlvbihxdWVzdGlvbikge1xuICAgICAgcmV0dXJuIHF1ZXN0aW9uLmdldFR5cGUoKSA9PT0gXCJtaWNyb3Bob25lXCI7XG4gICAgfSxcbiAgICBodG1sVGVtcGxhdGU6XG4gICAgICBcIjxkaXYgc3R5bGU9J2hlaWdodDogMzlweCc+XCIgK1xuICAgICAgXCI8YnV0dG9uIHR5cGU9J2J1dHRvbicgIHRpdGxlPSdSZWNvcmQnIHN0eWxlPSd2ZXJ0aWNhbC1hbGlnbjogdG9wOyBtYXJnaW4tdG9wOiAzcHgnID48aSBjbGFzcz0nZmEgZmEtbWljcm9waG9uZScgYXJpYS1oaWRkZW49J3RydWUnPjwvaT48L2J1dHRvbj5cIiArXG4gICAgICBcIiZuYnNwOzxidXR0b24gdHlwZT0nYnV0dG9uJyB0aXRsZT0nU2F2ZScgc3R5bGU9J3ZlcnRpY2FsLWFsaWduOiB0b3A7IG1hcmdpbi10b3A6IDNweCc+PGkgY2xhc3M9J2ZhIGZhLWNsb3VkJyBhcmlhLWhpZGRlbj0ndHJ1ZScgPjwvaT48L2J1dHRvbj5cIiArXG4gICAgICBcIiZuYnNwOzxhdWRpbyBzdHlsZT0nXCIgK1xuICAgICAgXCJ2ZXJ0aWNhbC1hbGlnbjogdG9wO1wiICtcbiAgICAgIFwibWFyZ2luLWxlZnQ6IDEwcHg7XCIgK1xuICAgICAgXCJoZWlnaHQ6MzVweDtcIiArXG4gICAgICBcIi1tb3otYm94LXNoYWRvdzogMnB4IDJweCA0cHggMHB4ICMwMDY3NzM7XCIgK1xuICAgICAgXCItd2Via2l0LWJveC1zaGFkb3c6ICAycHggMnB4IDRweCAwcHggIzAwNjc3MztcIiArXG4gICAgICBcImJveC1zaGFkb3c6IDJweCAycHggNHB4IDBweCAjMDA2NzczO1wiICtcbiAgICAgIFwiJyBcIiArXG4gICAgICBcImNvbnRyb2xzPSd0cnVlJyA+XCIgK1xuICAgICAgXCI8L2F1ZGlvPlwiICtcbiAgICAgIFwiPC9kaXY+XCIsXG4gICAgYWN0aXZhdGVkQnlDaGFuZ2VkOiBmdW5jdGlvbihhY3RpdmF0ZWRCeSkge1xuICAgICAgU3VydmV5Lkpzb25PYmplY3QubWV0YURhdGEuYWRkQ2xhc3MoXCJtaWNyb3Bob25lXCIsIFtdLCBudWxsLCBcImVtcHR5XCIpO1xuICAgIH0sXG5cbiAgICBhZnRlclJlbmRlcjogZnVuY3Rpb24ocXVlc3Rpb24sIGVsKSB7XG4gICAgICB2YXIgcm9vdFdpZGdldCA9IHRoaXM7XG4gICAgICB2YXIgYnV0dG9uU3RhcnRFbCA9IGVsLmdldEVsZW1lbnRzQnlUYWdOYW1lKFwiYnV0dG9uXCIpWzBdO1xuICAgICAgdmFyIGJ1dHRvblN0b3BFbCA9IGVsLmdldEVsZW1lbnRzQnlUYWdOYW1lKFwiYnV0dG9uXCIpWzFdO1xuICAgICAgdmFyIGF1ZGlvRWwgPSBlbC5nZXRFbGVtZW50c0J5VGFnTmFtZShcImF1ZGlvXCIpWzBdO1xuXG4gICAgICAvLy8vLy8vLy8vICBSZWNvcmRSVEMgbG9naWNcblxuICAgICAgdmFyIHN1Y2Nlc3NDYWxsYmFjayA9IGZ1bmN0aW9uKHN0cmVhbSkge1xuICAgICAgICB2YXIgb3B0aW9ucyA9IHtcbiAgICAgICAgICB0eXBlOiBcImF1ZGlvXCIsXG4gICAgICAgICAgbWltZVR5cGU6IFwiYXVkaW8vd2VibVwiLFxuICAgICAgICAgIGF1ZGlvQml0c1BlclNlY29uZDogNDQxMDAsXG4gICAgICAgICAgc2FtcGxlUmF0ZTogNDQxMDAsXG4gICAgICAgICAgYnVmZmVyU2l6ZTogMTYzODQsXG4gICAgICAgICAgbnVtYmVyT2ZBdWRpb0NoYW5uZWxzOiAxXG4gICAgICAgIH07XG4gICAgICAgIGNvbnNvbGUubG9nKFwic3VjY2Vzc0NhbGxiYWNrXCIpO1xuICAgICAgICBxdWVzdGlvbi5zdXJ2ZXkubXlzdHJlYW0gPSBzdHJlYW07XG4gICAgICAgIHF1ZXN0aW9uLnN1cnZleS5yZWNvcmRSVEMgPSBSZWNvcmRSVEMoXG4gICAgICAgICAgcXVlc3Rpb24uc3VydmV5Lm15c3RyZWFtLFxuICAgICAgICAgIG9wdGlvbnNcbiAgICAgICAgKTtcbiAgICAgICAgaWYgKHR5cGVvZiBxdWVzdGlvbi5zdXJ2ZXkucmVjb3JkUlRDICE9IFwidW5kZWZpbmVkXCIpIHtcbiAgICAgICAgICBjb25zb2xlLmxvZyhcInN0YXJ0UmVjb3JkaW5nXCIpO1xuICAgICAgICAgIHF1ZXN0aW9uLnN1cnZleS5yZWNvcmRSVEMuc3RhcnRSZWNvcmRpbmcoKTtcbiAgICAgICAgfVxuICAgICAgfTtcblxuICAgICAgdmFyIGVycm9yQ2FsbGJhY2sgPSBmdW5jdGlvbigpIHtcbiAgICAgICAgYWxlcnQoXCJObyBtaWNyb3Bob25lXCIpO1xuICAgICAgICBxdWVzdGlvbi5zdXJ2ZXkucmVjb3JkUlRDID0gdW5kZWZpbmVkO1xuICAgICAgICBxdWVzdGlvbi5zdXJ2ZXkubXlzdHJlYW0gPSB1bmRlZmluZWQ7XG4gICAgICB9O1xuXG4gICAgICB2YXIgcHJvY2Vzc0F1ZGlvID0gZnVuY3Rpb24oYXVkaW9WaWRlb1dlYk1VUkwpIHtcbiAgICAgICAgY29uc29sZS5sb2coXCJwcm9jZXNzQXVkaW9cIik7XG4gICAgICAgIHZhciByZWNvcmRlZEJsb2IgPSBxdWVzdGlvbi5zdXJ2ZXkucmVjb3JkUlRDLmdldEJsb2IoKTtcblxuICAgICAgICB2YXIgZmlsZVJlYWRlciA9IG5ldyBGaWxlUmVhZGVyKCk7XG4gICAgICAgIGZpbGVSZWFkZXIub25sb2FkID0gZnVuY3Rpb24oZXZlbnQpIHtcbiAgICAgICAgICB2YXIgZGF0YVVyaSA9IGV2ZW50LnRhcmdldC5yZXN1bHQ7XG4gICAgICAgICAgY29uc29sZS5sb2coXCJkYXRhVXJpOiBcIiArIGRhdGFVcmkpO1xuICAgICAgICAgIHF1ZXN0aW9uLnZhbHVlID0gZGF0YVVyaTtcbiAgICAgICAgICBhdWRpb0VsLnNyYyA9IGRhdGFVcmk7XG5cbiAgICAgICAgICBjb25zb2xlLmxvZyhcImNsZWFuaW5nXCIpO1xuICAgICAgICAgIHF1ZXN0aW9uLnN1cnZleS5yZWNvcmRSVEMgPSB1bmRlZmluZWQ7XG4gICAgICAgICAgcXVlc3Rpb24uc3VydmV5Lm15c3RyZWFtID0gdW5kZWZpbmVkO1xuICAgICAgICB9O1xuICAgICAgICBmaWxlUmVhZGVyLnJlYWRBc0RhdGFVUkwocmVjb3JkZWRCbG9iKTtcbiAgICAgIH07XG5cbiAgICAgIHZhciBzdGFydFJlY29yZGluZyA9IGZ1bmN0aW9uKCkge1xuICAgICAgICAvLyBlcmFzZSBwcmV2aW91cyBkYXRhXG4gICAgICAgIHF1ZXN0aW9uLnZhbHVlID0gdW5kZWZpbmVkO1xuXG4gICAgICAgIC8vIGlmIHJlY29yZGVyIG9wZW4gb24gYW5vdGhlciBxdWVzdGlvblx0LSB0cnkgdG8gc3RvcCByZWNvcmRpbmdcbiAgICAgICAgaWYgKHR5cGVvZiBxdWVzdGlvbi5zdXJ2ZXkucmVjb3JkUlRDICE9IFwidW5kZWZpbmVkXCIpIHtcbiAgICAgICAgICBxdWVzdGlvbi5zdXJ2ZXkucmVjb3JkUlRDLnN0b3BSZWNvcmRpbmcoZG9Ob3RoaW5nSGFuZGxlcik7XG4gICAgICAgICAgaWYgKHR5cGVvZiBxdWVzdGlvbi5zdXJ2ZXkubXlzdHJlYW0gIT0gXCJ1bmRlZmluZWRcIikge1xuICAgICAgICAgICAgcXVlc3Rpb24uc3VydmV5Lm15c3RyZWFtLmdldEF1ZGlvVHJhY2tzKCkuZm9yRWFjaChmdW5jdGlvbih0cmFjaykge1xuICAgICAgICAgICAgICB0cmFjay5zdG9wKCk7XG4gICAgICAgICAgICB9KTtcbiAgICAgICAgICB9XG4gICAgICAgIH1cblxuICAgICAgICB2YXIgbWVkaWFDb25zdHJhaW50cyA9IHtcbiAgICAgICAgICB2aWRlbzogZmFsc2UsXG4gICAgICAgICAgYXVkaW86IHRydWVcbiAgICAgICAgfTtcblxuICAgICAgICBuYXZpZ2F0b3IubWVkaWFEZXZpY2VzXG4gICAgICAgICAgLmdldFVzZXJNZWRpYShtZWRpYUNvbnN0cmFpbnRzKVxuICAgICAgICAgIC50aGVuKHN1Y2Nlc3NDYWxsYmFjay5iaW5kKHRoaXMpLCBlcnJvckNhbGxiYWNrLmJpbmQodGhpcykpO1xuICAgICAgfTtcblxuICAgICAgdmFyIHN0b3BSZWNvcmRpbmcgPSBmdW5jdGlvbigpIHtcbiAgICAgICAgY29uc29sZS5sb2coXCJzdG9wUmVjb3JkaW5nXCIpO1xuICAgICAgICBpZiAodHlwZW9mIHF1ZXN0aW9uLnN1cnZleS5yZWNvcmRSVEMgIT0gXCJ1bmRlZmluZWRcIikge1xuICAgICAgICAgIHF1ZXN0aW9uLnN1cnZleS5yZWNvcmRSVEMuc3RvcFJlY29yZGluZyhwcm9jZXNzQXVkaW8uYmluZCh0aGlzKSk7XG4gICAgICAgICAgaWYgKHR5cGVvZiBxdWVzdGlvbi5zdXJ2ZXkubXlzdHJlYW0gIT0gXCJ1bmRlZmluZWRcIikge1xuICAgICAgICAgICAgcXVlc3Rpb24uc3VydmV5Lm15c3RyZWFtLmdldEF1ZGlvVHJhY2tzKCkuZm9yRWFjaChmdW5jdGlvbih0cmFjaykge1xuICAgICAgICAgICAgICB0cmFjay5zdG9wKCk7XG4gICAgICAgICAgICB9KTtcbiAgICAgICAgICB9XG4gICAgICAgIH1cbiAgICAgIH07XG5cbiAgICAgIC8vLy8vLy8vLy8vLy8vICBlbmQgUlRDIGxvZ2ljIC8vLy8vLy8vLy8vLy8vLy8vL1xuXG4gICAgICBpZiAoIXF1ZXN0aW9uLmlzUmVhZE9ubHkpIHtcbiAgICAgICAgYnV0dG9uU3RhcnRFbC5vbmNsaWNrID0gc3RhcnRSZWNvcmRpbmc7XG4gICAgICB9IGVsc2Uge1xuICAgICAgICBidXR0b25TdGFydEVsLnBhcmVudE5vZGUucmVtb3ZlQ2hpbGQoYnV0dG9uU3RhcnRFbCk7XG4gICAgICB9XG5cbiAgICAgIGlmICghcXVlc3Rpb24uaXNSZWFkT25seSkge1xuICAgICAgICBidXR0b25TdG9wRWwub25jbGljayA9IHN0b3BSZWNvcmRpbmc7XG4gICAgICB9IGVsc2Uge1xuICAgICAgICBidXR0b25TdG9wRWwucGFyZW50Tm9kZS5yZW1vdmVDaGlsZChidXR0b25TdG9wRWwpO1xuICAgICAgfVxuXG4gICAgICBhdWRpb0VsLnNyYyA9IHF1ZXN0aW9uLnZhbHVlO1xuXG4gICAgICB2YXIgdXBkYXRlVmFsdWVIYW5kbGVyID0gZnVuY3Rpb24oKSB7fTtcblxuICAgICAgdmFyIGRvTm90aGluZ0hhbmRsZXIgPSBmdW5jdGlvbigpIHt9O1xuXG4gICAgICBxdWVzdGlvbi52YWx1ZUNoYW5nZWRDYWxsYmFjayA9IHVwZGF0ZVZhbHVlSGFuZGxlcjtcbiAgICAgIHVwZGF0ZVZhbHVlSGFuZGxlcigpO1xuICAgIH0sXG4gICAgd2lsbFVubW91bnQ6IGZ1bmN0aW9uKHF1ZXN0aW9uLCBlbCkge1xuICAgICAgY29uc29sZS5sb2coXCJ1bm1vdW50IG1pY3JvcGhvbmUgbm8gcmVjb3JkIFwiKTtcbiAgICAgIGlmICh0eXBlb2YgcXVlc3Rpb24uc3VydmV5LnJlY29yZFJUQyAhPSBcInVuZGVmaW5lZFwiKSB7XG4gICAgICAgIHF1ZXN0aW9uLnN1cnZleS5yZWNvcmRSVEMuc3RvcFJlY29yZGluZyhkb05vdGhpbmdIYW5kbGVyKTtcbiAgICAgICAgaWYgKHR5cGVvZiBxdWVzdGlvbi5zdXJ2ZXkubXlzdHJlYW0gIT0gXCJ1bmRlZmluZWRcIikge1xuICAgICAgICAgIHF1ZXN0aW9uLnN1cnZleS5teXN0cmVhbS5nZXRBdWRpb1RyYWNrcygpLmZvckVhY2goZnVuY3Rpb24odHJhY2spIHtcbiAgICAgICAgICAgIHRyYWNrLnN0b3AoKTtcbiAgICAgICAgICB9KTtcbiAgICAgICAgfVxuICAgICAgICBxdWVzdGlvbi52YWx1ZSA9IHVuZGVmaW5lZDtcbiAgICAgICAgcXVlc3Rpb24uc3VydmV5LnJlY29yZFJUQyA9IHVuZGVmaW5lZDtcbiAgICAgICAgcXVlc3Rpb24uc3VydmV5Lm15c3RyZWFtID0gdW5kZWZpbmVkO1xuICAgICAgfVxuICAgIH1cbiAgfTtcblxuICBTdXJ2ZXkuQ3VzdG9tV2lkZ2V0Q29sbGVjdGlvbi5JbnN0YW5jZS5hZGRDdXN0b21XaWRnZXQod2lkZ2V0LCBcImN1c3RvbXR5cGVcIik7XG59XG5cbmlmICh0eXBlb2YgU3VydmV5ICE9PSBcInVuZGVmaW5lZFwiKSB7XG4gIGluaXQoU3VydmV5KTtcbn1cblxuZXhwb3J0IGRlZmF1bHQgaW5pdDtcblxuXG5cbi8vLy8vLy8vLy8vLy8vLy8vL1xuLy8gV0VCUEFDSyBGT09URVJcbi8vIC4vc3JjL21pY3JvcGhvbmUuanNcbi8vIG1vZHVsZSBpZCA9IDE2XG4vLyBtb2R1bGUgY2h1bmtzID0gMCAxIiwiJ3VzZSBzdHJpY3QnO1xyXG5cclxuLy8gTGFzdCB0aW1lIHVwZGF0ZWQ6IDIwMjAtMDItMjYgMToxMTo0NyBQTSBVVENcclxuXHJcbi8vIF9fX19fX19fX19fX19fX19cclxuLy8gUmVjb3JkUlRDIHY1LjUuOVxyXG5cclxuLy8gT3Blbi1Tb3VyY2VkOiBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQ1xyXG5cclxuLy8gLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS1cclxuLy8gTXVheiBLaGFuICAgICAtIHd3dy5NdWF6S2hhbi5jb21cclxuLy8gTUlUIExpY2Vuc2UgICAtIHd3dy5XZWJSVEMtRXhwZXJpbWVudC5jb20vbGljZW5jZVxyXG4vLyAtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLVxyXG5cclxuLy8gX19fX19fX19fX19fXHJcbi8vIFJlY29yZFJUQy5qc1xyXG5cclxuLyoqXHJcbiAqIHtAbGluayBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQ3xSZWNvcmRSVEN9IGlzIGEgV2ViUlRDIEphdmFTY3JpcHQgbGlicmFyeSBmb3IgYXVkaW8vdmlkZW8gYXMgd2VsbCBhcyBzY3JlZW4gYWN0aXZpdHkgcmVjb3JkaW5nLiBJdCBzdXBwb3J0cyBDaHJvbWUsIEZpcmVmb3gsIE9wZXJhLCBBbmRyb2lkLCBhbmQgTWljcm9zb2Z0IEVkZ2UuIFBsYXRmb3JtczogTGludXgsIE1hYyBhbmQgV2luZG93cy4gXHJcbiAqIEBzdW1tYXJ5IFJlY29yZCBhdWRpbywgdmlkZW8gb3Igc2NyZWVuIGluc2lkZSB0aGUgYnJvd3Nlci5cclxuICogQGxpY2Vuc2Uge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDL2Jsb2IvbWFzdGVyL0xJQ0VOU0V8TUlUfVxyXG4gKiBAYXV0aG9yIHtAbGluayBodHRwczovL011YXpLaGFuLmNvbXxNdWF6IEtoYW59XHJcbiAqIEB0eXBlZGVmIFJlY29yZFJUQ1xyXG4gKiBAY2xhc3NcclxuICogQGV4YW1wbGVcclxuICogdmFyIHJlY29yZGVyID0gUmVjb3JkUlRDKG1lZGlhU3RyZWFtIG9yIFthcnJheU9mTWVkaWFTdHJlYW1dLCB7XHJcbiAqICAgICB0eXBlOiAndmlkZW8nLCAvLyBhdWRpbyBvciB2aWRlbyBvciBnaWYgb3IgY2FudmFzXHJcbiAqICAgICByZWNvcmRlclR5cGU6IE1lZGlhU3RyZWFtUmVjb3JkZXIgfHwgQ2FudmFzUmVjb3JkZXIgfHwgU3RlcmVvQXVkaW9SZWNvcmRlciB8fCBFdGNcclxuICogfSk7XHJcbiAqIHJlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKCk7XHJcbiAqIEBzZWUgRm9yIGZ1cnRoZXIgaW5mb3JtYXRpb246XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICogQHBhcmFtIHtNZWRpYVN0cmVhbX0gbWVkaWFTdHJlYW0gLSBTaW5nbGUgbWVkaWEtc3RyZWFtIG9iamVjdCwgYXJyYXkgb2YgbWVkaWEtc3RyZWFtcywgaHRtbC1jYW52YXMtZWxlbWVudCwgZXRjLlxyXG4gKiBAcGFyYW0ge29iamVjdH0gY29uZmlnIC0ge3R5cGU6XCJ2aWRlb1wiLCByZWNvcmRlclR5cGU6IE1lZGlhU3RyZWFtUmVjb3JkZXIsIGRpc2FibGVMb2dzOiB0cnVlLCBudW1iZXJPZkF1ZGlvQ2hhbm5lbHM6IDEsIGJ1ZmZlclNpemU6IDAsIHNhbXBsZVJhdGU6IDAsIGRlc2lyZWRTYW1wUmF0ZTogMTYwMDAsIHZpZGVvOiBIVE1MVmlkZW9FbGVtZW50LCBldGMufVxyXG4gKi9cclxuXHJcbmZ1bmN0aW9uIFJlY29yZFJUQyhtZWRpYVN0cmVhbSwgY29uZmlnKSB7XHJcbiAgICBpZiAoIW1lZGlhU3RyZWFtKSB7XHJcbiAgICAgICAgdGhyb3cgJ0ZpcnN0IHBhcmFtZXRlciBpcyByZXF1aXJlZC4nO1xyXG4gICAgfVxyXG5cclxuICAgIGNvbmZpZyA9IGNvbmZpZyB8fCB7XHJcbiAgICAgICAgdHlwZTogJ3ZpZGVvJ1xyXG4gICAgfTtcclxuXHJcbiAgICBjb25maWcgPSBuZXcgUmVjb3JkUlRDQ29uZmlndXJhdGlvbihtZWRpYVN0cmVhbSwgY29uZmlnKTtcclxuXHJcbiAgICAvLyBhIHJlZmVyZW5jZSB0byB1c2VyJ3MgcmVjb3JkUlRDIG9iamVjdFxyXG4gICAgdmFyIHNlbGYgPSB0aGlzO1xyXG5cclxuICAgIGZ1bmN0aW9uIHN0YXJ0UmVjb3JkaW5nKGNvbmZpZzIpIHtcclxuICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZygnUmVjb3JkUlRDIHZlcnNpb246ICcsIHNlbGYudmVyc2lvbik7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoISFjb25maWcyKSB7XHJcbiAgICAgICAgICAgIC8vIGFsbG93IHVzZXJzIHRvIHNldCBvcHRpb25zIHVzaW5nIHN0YXJ0UmVjb3JkaW5nIG1ldGhvZFxyXG4gICAgICAgICAgICAvLyBjb25maWcyIGlzIHNpbWlsYXIgdG8gbWFpbiBcImNvbmZpZ1wiIG9iamVjdCAoc2Vjb25kIHBhcmFtZXRlciBvdmVyIFJlY29yZFJUQyBjb25zdHJ1Y3RvcilcclxuICAgICAgICAgICAgY29uZmlnID0gbmV3IFJlY29yZFJUQ0NvbmZpZ3VyYXRpb24obWVkaWFTdHJlYW0sIGNvbmZpZzIpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgY29uc29sZS5sb2coJ3N0YXJ0ZWQgcmVjb3JkaW5nICcgKyBjb25maWcudHlwZSArICcgc3RyZWFtLicpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKG1lZGlhUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgbWVkaWFSZWNvcmRlci5jbGVhclJlY29yZGVkRGF0YSgpO1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnJlY29yZCgpO1xyXG5cclxuICAgICAgICAgICAgc2V0U3RhdGUoJ3JlY29yZGluZycpO1xyXG5cclxuICAgICAgICAgICAgaWYgKHNlbGYucmVjb3JkaW5nRHVyYXRpb24pIHtcclxuICAgICAgICAgICAgICAgIGhhbmRsZVJlY29yZGluZ0R1cmF0aW9uKCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIHNlbGY7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpbml0UmVjb3JkZXIoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIGlmIChzZWxmLnJlY29yZGluZ0R1cmF0aW9uKSB7XHJcbiAgICAgICAgICAgICAgICBoYW5kbGVSZWNvcmRpbmdEdXJhdGlvbigpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIHJldHVybiBzZWxmO1xyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIGluaXRSZWNvcmRlcihpbml0Q2FsbGJhY2spIHtcclxuICAgICAgICBpZiAoaW5pdENhbGxiYWNrKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy5pbml0Q2FsbGJhY2sgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIGluaXRDYWxsYmFjaygpO1xyXG4gICAgICAgICAgICAgICAgaW5pdENhbGxiYWNrID0gY29uZmlnLmluaXRDYWxsYmFjayA9IG51bGw7IC8vIHJlY29yZGVyLmluaXRSZWNvcmRlciBzaG91bGQgYmUgY2FsbC1iYWNrZWQgb25jZS5cclxuICAgICAgICAgICAgfTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHZhciBSZWNvcmRlciA9IG5ldyBHZXRSZWNvcmRlclR5cGUobWVkaWFTdHJlYW0sIGNvbmZpZyk7XHJcblxyXG4gICAgICAgIG1lZGlhUmVjb3JkZXIgPSBuZXcgUmVjb3JkZXIobWVkaWFTdHJlYW0sIGNvbmZpZyk7XHJcbiAgICAgICAgbWVkaWFSZWNvcmRlci5yZWNvcmQoKTtcclxuXHJcbiAgICAgICAgc2V0U3RhdGUoJ3JlY29yZGluZycpO1xyXG5cclxuICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZygnSW5pdGlhbGl6ZWQgcmVjb3JkZXJUeXBlOicsIG1lZGlhUmVjb3JkZXIuY29uc3RydWN0b3IubmFtZSwgJ2ZvciBvdXRwdXQtdHlwZTonLCBjb25maWcudHlwZSk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIHN0b3BSZWNvcmRpbmcoY2FsbGJhY2spIHtcclxuICAgICAgICBjYWxsYmFjayA9IGNhbGxiYWNrIHx8IGZ1bmN0aW9uKCkge307XHJcblxyXG4gICAgICAgIGlmICghbWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICB3YXJuaW5nTG9nKCk7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChzZWxmLnN0YXRlID09PSAncGF1c2VkJykge1xyXG4gICAgICAgICAgICBzZWxmLnJlc3VtZVJlY29yZGluZygpO1xyXG5cclxuICAgICAgICAgICAgc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIHN0b3BSZWNvcmRpbmcoY2FsbGJhY2spO1xyXG4gICAgICAgICAgICB9LCAxKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHNlbGYuc3RhdGUgIT09ICdyZWNvcmRpbmcnICYmICFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgY29uc29sZS53YXJuKCdSZWNvcmRpbmcgc3RhdGUgc2hvdWxkIGJlOiBcInJlY29yZGluZ1wiLCBob3dldmVyIGN1cnJlbnQgc3RhdGUgaXM6ICcsIHNlbGYuc3RhdGUpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgY29uc29sZS5sb2coJ1N0b3BwZWQgcmVjb3JkaW5nICcgKyBjb25maWcudHlwZSArICcgc3RyZWFtLicpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKGNvbmZpZy50eXBlICE9PSAnZ2lmJykge1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnN0b3AoX2NhbGxiYWNrKTtcclxuICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnN0b3AoKTtcclxuICAgICAgICAgICAgX2NhbGxiYWNrKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBzZXRTdGF0ZSgnc3RvcHBlZCcpO1xyXG5cclxuICAgICAgICBmdW5jdGlvbiBfY2FsbGJhY2soX19ibG9iKSB7XHJcbiAgICAgICAgICAgIGlmICghbWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICAgICAgaWYgKHR5cGVvZiBjYWxsYmFjay5jYWxsID09PSAnZnVuY3Rpb24nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2suY2FsbChzZWxmLCAnJyk7XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrKCcnKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgT2JqZWN0LmtleXMobWVkaWFSZWNvcmRlcikuZm9yRWFjaChmdW5jdGlvbihrZXkpIHtcclxuICAgICAgICAgICAgICAgIGlmICh0eXBlb2YgbWVkaWFSZWNvcmRlcltrZXldID09PSAnZnVuY3Rpb24nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIHNlbGZba2V5XSA9IG1lZGlhUmVjb3JkZXJba2V5XTtcclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICB2YXIgYmxvYiA9IG1lZGlhUmVjb3JkZXIuYmxvYjtcclxuXHJcbiAgICAgICAgICAgIGlmICghYmxvYikge1xyXG4gICAgICAgICAgICAgICAgaWYgKF9fYmxvYikge1xyXG4gICAgICAgICAgICAgICAgICAgIG1lZGlhUmVjb3JkZXIuYmxvYiA9IGJsb2IgPSBfX2Jsb2I7XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIHRocm93ICdSZWNvcmRpbmcgZmFpbGVkLic7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChibG9iICYmICFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGJsb2IudHlwZSwgJy0+JywgYnl0ZXNUb1NpemUoYmxvYi5zaXplKSk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChjYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgdmFyIHVybDtcclxuXHJcbiAgICAgICAgICAgICAgICB0cnkge1xyXG4gICAgICAgICAgICAgICAgICAgIHVybCA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAgICAgICAgICAgICAgICB9IGNhdGNoIChlKSB7fVxyXG5cclxuICAgICAgICAgICAgICAgIGlmICh0eXBlb2YgY2FsbGJhY2suY2FsbCA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrLmNhbGwoc2VsZiwgdXJsKTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2sodXJsKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKCFjb25maWcuYXV0b1dyaXRlVG9EaXNrKSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGdldERhdGFVUkwoZnVuY3Rpb24oZGF0YVVSTCkge1xyXG4gICAgICAgICAgICAgICAgdmFyIHBhcmFtZXRlciA9IHt9O1xyXG4gICAgICAgICAgICAgICAgcGFyYW1ldGVyW2NvbmZpZy50eXBlICsgJ0Jsb2InXSA9IGRhdGFVUkw7XHJcbiAgICAgICAgICAgICAgICBEaXNrU3RvcmFnZS5TdG9yZShwYXJhbWV0ZXIpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gcGF1c2VSZWNvcmRpbmcoKSB7XHJcbiAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHdhcm5pbmdMb2coKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHNlbGYuc3RhdGUgIT09ICdyZWNvcmRpbmcnKSB7XHJcbiAgICAgICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zb2xlLndhcm4oJ1VuYWJsZSB0byBwYXVzZSB0aGUgcmVjb3JkaW5nLiBSZWNvcmRpbmcgc3RhdGU6ICcsIHNlbGYuc3RhdGUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHNldFN0YXRlKCdwYXVzZWQnKTtcclxuXHJcbiAgICAgICAgbWVkaWFSZWNvcmRlci5wYXVzZSgpO1xyXG5cclxuICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZygnUGF1c2VkIHJlY29yZGluZy4nKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gcmVzdW1lUmVjb3JkaW5nKCkge1xyXG4gICAgICAgIGlmICghbWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICB3YXJuaW5nTG9nKCk7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChzZWxmLnN0YXRlICE9PSAncGF1c2VkJykge1xyXG4gICAgICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICAgICAgY29uc29sZS53YXJuKCdVbmFibGUgdG8gcmVzdW1lIHRoZSByZWNvcmRpbmcuIFJlY29yZGluZyBzdGF0ZTogJywgc2VsZi5zdGF0ZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc2V0U3RhdGUoJ3JlY29yZGluZycpO1xyXG5cclxuICAgICAgICAvLyBub3QgYWxsIGxpYnMgaGF2ZSB0aGlzIG1ldGhvZCB5ZXRcclxuICAgICAgICBtZWRpYVJlY29yZGVyLnJlc3VtZSgpO1xyXG5cclxuICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZygnUmVzdW1lZCByZWNvcmRpbmcuJyk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIHJlYWRGaWxlKF9ibG9iKSB7XHJcbiAgICAgICAgcG9zdE1lc3NhZ2UobmV3IEZpbGVSZWFkZXJTeW5jKCkucmVhZEFzRGF0YVVSTChfYmxvYikpO1xyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIGdldERhdGFVUkwoY2FsbGJhY2ssIF9tZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgaWYgKCFjYWxsYmFjaykge1xyXG4gICAgICAgICAgICB0aHJvdyAnUGFzcyBhIGNhbGxiYWNrIGZ1bmN0aW9uIG92ZXIgZ2V0RGF0YVVSTC4nO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgdmFyIGJsb2IgPSBfbWVkaWFSZWNvcmRlciA/IF9tZWRpYVJlY29yZGVyLmJsb2IgOiAobWVkaWFSZWNvcmRlciB8fCB7fSkuYmxvYjtcclxuXHJcbiAgICAgICAgaWYgKCFibG9iKSB7XHJcbiAgICAgICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zb2xlLndhcm4oJ0Jsb2IgZW5jb2RlciBkaWQgbm90IGZpbmlzaCBpdHMgam9iIHlldC4nKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIGdldERhdGFVUkwoY2FsbGJhY2ssIF9tZWRpYVJlY29yZGVyKTtcclxuICAgICAgICAgICAgfSwgMTAwMCk7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgV29ya2VyICE9PSAndW5kZWZpbmVkJyAmJiAhbmF2aWdhdG9yLm1vekdldFVzZXJNZWRpYSkge1xyXG4gICAgICAgICAgICB2YXIgd2ViV29ya2VyID0gcHJvY2Vzc0luV2ViV29ya2VyKHJlYWRGaWxlKTtcclxuXHJcbiAgICAgICAgICAgIHdlYldvcmtlci5vbm1lc3NhZ2UgPSBmdW5jdGlvbihldmVudCkge1xyXG4gICAgICAgICAgICAgICAgY2FsbGJhY2soZXZlbnQuZGF0YSk7XHJcbiAgICAgICAgICAgIH07XHJcblxyXG4gICAgICAgICAgICB3ZWJXb3JrZXIucG9zdE1lc3NhZ2UoYmxvYik7XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgdmFyIHJlYWRlciA9IG5ldyBGaWxlUmVhZGVyKCk7XHJcbiAgICAgICAgICAgIHJlYWRlci5yZWFkQXNEYXRhVVJMKGJsb2IpO1xyXG4gICAgICAgICAgICByZWFkZXIub25sb2FkID0gZnVuY3Rpb24oZXZlbnQpIHtcclxuICAgICAgICAgICAgICAgIGNhbGxiYWNrKGV2ZW50LnRhcmdldC5yZXN1bHQpO1xyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gcHJvY2Vzc0luV2ViV29ya2VyKF9mdW5jdGlvbikge1xyXG4gICAgICAgICAgICB0cnkge1xyXG4gICAgICAgICAgICAgICAgdmFyIGJsb2IgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKG5ldyBCbG9iKFtfZnVuY3Rpb24udG9TdHJpbmcoKSxcclxuICAgICAgICAgICAgICAgICAgICAndGhpcy5vbm1lc3NhZ2UgPSAgZnVuY3Rpb24gKGVlZSkgeycgKyBfZnVuY3Rpb24ubmFtZSArICcoZWVlLmRhdGEpO30nXHJcbiAgICAgICAgICAgICAgICBdLCB7XHJcbiAgICAgICAgICAgICAgICAgICAgdHlwZTogJ2FwcGxpY2F0aW9uL2phdmFzY3JpcHQnXHJcbiAgICAgICAgICAgICAgICB9KSk7XHJcblxyXG4gICAgICAgICAgICAgICAgdmFyIHdvcmtlciA9IG5ldyBXb3JrZXIoYmxvYik7XHJcbiAgICAgICAgICAgICAgICBVUkwucmV2b2tlT2JqZWN0VVJMKGJsb2IpO1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHdvcmtlcjtcclxuICAgICAgICAgICAgfSBjYXRjaCAoZSkge31cclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gaGFuZGxlUmVjb3JkaW5nRHVyYXRpb24oY291bnRlcikge1xyXG4gICAgICAgIGNvdW50ZXIgPSBjb3VudGVyIHx8IDA7XHJcblxyXG4gICAgICAgIGlmIChzZWxmLnN0YXRlID09PSAncGF1c2VkJykge1xyXG4gICAgICAgICAgICBzZXRUaW1lb3V0KGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICAgICAgaGFuZGxlUmVjb3JkaW5nRHVyYXRpb24oY291bnRlcik7XHJcbiAgICAgICAgICAgIH0sIDEwMDApO1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoc2VsZi5zdGF0ZSA9PT0gJ3N0b3BwZWQnKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChjb3VudGVyID49IHNlbGYucmVjb3JkaW5nRHVyYXRpb24pIHtcclxuICAgICAgICAgICAgc3RvcFJlY29yZGluZyhzZWxmLm9uUmVjb3JkaW5nU3RvcHBlZCk7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGNvdW50ZXIgKz0gMTAwMDsgLy8gMS1zZWNvbmRcclxuXHJcbiAgICAgICAgc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgaGFuZGxlUmVjb3JkaW5nRHVyYXRpb24oY291bnRlcik7XHJcbiAgICAgICAgfSwgMTAwMCk7XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gc2V0U3RhdGUoc3RhdGUpIHtcclxuICAgICAgICBpZiAoIXNlbGYpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc2VsZi5zdGF0ZSA9IHN0YXRlO1xyXG5cclxuICAgICAgICBpZiAodHlwZW9mIHNlbGYub25TdGF0ZUNoYW5nZWQuY2FsbCA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICBzZWxmLm9uU3RhdGVDaGFuZ2VkLmNhbGwoc2VsZiwgc3RhdGUpO1xyXG4gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIHNlbGYub25TdGF0ZUNoYW5nZWQoc3RhdGUpO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICB2YXIgV0FSTklORyA9ICdJdCBzZWVtcyB0aGF0IHJlY29yZGVyIGlzIGRlc3Ryb3llZCBvciBcInN0YXJ0UmVjb3JkaW5nXCIgaXMgbm90IGludm9rZWQgZm9yICcgKyBjb25maWcudHlwZSArICcgcmVjb3JkZXIuJztcclxuXHJcbiAgICBmdW5jdGlvbiB3YXJuaW5nTG9nKCkge1xyXG4gICAgICAgIGlmIChjb25maWcuZGlzYWJsZUxvZ3MgPT09IHRydWUpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgY29uc29sZS53YXJuKFdBUk5JTkcpO1xyXG4gICAgfVxyXG5cclxuICAgIHZhciBtZWRpYVJlY29yZGVyO1xyXG5cclxuICAgIHZhciByZXR1cm5PYmplY3QgPSB7XHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogVGhpcyBtZXRob2Qgc3RhcnRzIHRoZSByZWNvcmRpbmcuXHJcbiAgICAgICAgICogQG1ldGhvZFxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAaW5zdGFuY2VcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHZhciByZWNvcmRlciA9IFJlY29yZFJUQyhtZWRpYVN0cmVhbSwge1xyXG4gICAgICAgICAqICAgICB0eXBlOiAndmlkZW8nXHJcbiAgICAgICAgICogfSk7XHJcbiAgICAgICAgICogcmVjb3JkZXIuc3RhcnRSZWNvcmRpbmcoKTtcclxuICAgICAgICAgKi9cclxuICAgICAgICBzdGFydFJlY29yZGluZzogc3RhcnRSZWNvcmRpbmcsXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIFRoaXMgbWV0aG9kIHN0b3BzIHRoZSByZWNvcmRpbmcuIEl0IGlzIHN0cm9uZ2x5IHJlY29tbWVuZGVkIHRvIGdldCBcImJsb2JcIiBvciBcIlVSSVwiIGluc2lkZSB0aGUgY2FsbGJhY2sgdG8gbWFrZSBzdXJlIGFsbCByZWNvcmRlcnMgZmluaXNoZWQgdGhlaXIgam9iLlxyXG4gICAgICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQ2FsbGJhY2sgdG8gZ2V0IHRoZSByZWNvcmRlZCBibG9iLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5zdG9wUmVjb3JkaW5nKGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAqICAgICAvLyB1c2UgZWl0aGVyIFwidGhpc1wiIG9yIFwicmVjb3JkZXJcIiBvYmplY3Q7IGJvdGggYXJlIGlkZW50aWNhbFxyXG4gICAgICAgICAqICAgICB2aWRlby5zcmMgPSB0aGlzLnRvVVJMKCk7XHJcbiAgICAgICAgICogICAgIHZhciBibG9iID0gdGhpcy5nZXRCbG9iKCk7XHJcbiAgICAgICAgICogfSk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgc3RvcFJlY29yZGluZzogc3RvcFJlY29yZGluZyxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogVGhpcyBtZXRob2QgcGF1c2VzIHRoZSByZWNvcmRpbmcuIFlvdSBjYW4gcmVzdW1lIHJlY29yZGluZyB1c2luZyBcInJlc3VtZVJlY29yZGluZ1wiIG1ldGhvZC5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEB0b2RvIEZpcmVmb3ggaXMgdW5hYmxlIHRvIHBhdXNlIHRoZSByZWNvcmRpbmcuIEZpeCBpdC5cclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyLnBhdXNlUmVjb3JkaW5nKCk7ICAvLyBwYXVzZSB0aGUgcmVjb3JkaW5nXHJcbiAgICAgICAgICogcmVjb3JkZXIucmVzdW1lUmVjb3JkaW5nKCk7IC8vIHJlc3VtZSBhZ2FpblxyXG4gICAgICAgICAqL1xyXG4gICAgICAgIHBhdXNlUmVjb3JkaW5nOiBwYXVzZVJlY29yZGluZyxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogVGhpcyBtZXRob2QgcmVzdW1lcyB0aGUgcmVjb3JkaW5nLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5wYXVzZVJlY29yZGluZygpOyAgLy8gZmlyc3Qgb2YgYWxsLCBwYXVzZSB0aGUgcmVjb3JkaW5nXHJcbiAgICAgICAgICogcmVjb3JkZXIucmVzdW1lUmVjb3JkaW5nKCk7IC8vIG5vdyByZXN1bWUgaXRcclxuICAgICAgICAgKi9cclxuICAgICAgICByZXN1bWVSZWNvcmRpbmc6IHJlc3VtZVJlY29yZGluZyxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogVGhpcyBtZXRob2QgaW5pdGlhbGl6ZXMgdGhlIHJlY29yZGluZy5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEB0b2RvIFRoaXMgbWV0aG9kIHNob3VsZCBiZSBkZXByZWNhdGVkLlxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogcmVjb3JkZXIuaW5pdFJlY29yZGVyKCk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgaW5pdFJlY29yZGVyOiBpbml0UmVjb3JkZXIsXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIEFzayBSZWNvcmRSVEMgdG8gYXV0by1zdG9wIHRoZSByZWNvcmRpbmcgYWZ0ZXIgNSBtaW51dGVzLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiB2YXIgZml2ZU1pbnV0ZXMgPSA1ICogMTAwMCAqIDYwO1xyXG4gICAgICAgICAqIHJlY29yZGVyLnNldFJlY29yZGluZ0R1cmF0aW9uKGZpdmVNaW51dGVzLCBmdW5jdGlvbigpIHtcclxuICAgICAgICAgKiAgICB2YXIgYmxvYiA9IHRoaXMuZ2V0QmxvYigpO1xyXG4gICAgICAgICAqICAgIHZpZGVvLnNyYyA9IHRoaXMudG9VUkwoKTtcclxuICAgICAgICAgKiB9KTtcclxuICAgICAgICAgKiBcclxuICAgICAgICAgKiAvLyBvciBvdGhlcndpc2VcclxuICAgICAgICAgKiByZWNvcmRlci5zZXRSZWNvcmRpbmdEdXJhdGlvbihmaXZlTWludXRlcykub25SZWNvcmRpbmdTdG9wcGVkKGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAqICAgIHZhciBibG9iID0gdGhpcy5nZXRCbG9iKCk7XHJcbiAgICAgICAgICogICAgdmlkZW8uc3JjID0gdGhpcy50b1VSTCgpO1xyXG4gICAgICAgICAqIH0pO1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIHNldFJlY29yZGluZ0R1cmF0aW9uOiBmdW5jdGlvbihyZWNvcmRpbmdEdXJhdGlvbiwgY2FsbGJhY2spIHtcclxuICAgICAgICAgICAgaWYgKHR5cGVvZiByZWNvcmRpbmdEdXJhdGlvbiA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAgICAgICAgIHRocm93ICdyZWNvcmRpbmdEdXJhdGlvbiBpcyByZXF1aXJlZC4nO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAodHlwZW9mIHJlY29yZGluZ0R1cmF0aW9uICE9PSAnbnVtYmVyJykge1xyXG4gICAgICAgICAgICAgICAgdGhyb3cgJ3JlY29yZGluZ0R1cmF0aW9uIG11c3QgYmUgYSBudW1iZXIuJztcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgc2VsZi5yZWNvcmRpbmdEdXJhdGlvbiA9IHJlY29yZGluZ0R1cmF0aW9uO1xyXG4gICAgICAgICAgICBzZWxmLm9uUmVjb3JkaW5nU3RvcHBlZCA9IGNhbGxiYWNrIHx8IGZ1bmN0aW9uKCkge307XHJcblxyXG4gICAgICAgICAgICByZXR1cm4ge1xyXG4gICAgICAgICAgICAgICAgb25SZWNvcmRpbmdTdG9wcGVkOiBmdW5jdGlvbihjYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgICAgIHNlbGYub25SZWNvcmRpbmdTdG9wcGVkID0gY2FsbGJhY2s7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgfSxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogVGhpcyBtZXRob2QgY2FuIGJlIHVzZWQgdG8gY2xlYXIvcmVzZXQgYWxsIHRoZSByZWNvcmRlZCBkYXRhLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQHRvZG8gRmlndXJlIG91dCB0aGUgZGlmZmVyZW5jZSBiZXR3ZWVuIFwicmVzZXRcIiBhbmQgXCJjbGVhclJlY29yZGVkRGF0YVwiIG1ldGhvZHMuXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5jbGVhclJlY29yZGVkRGF0YSgpO1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIGNsZWFyUmVjb3JkZWREYXRhOiBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgICAgICB3YXJuaW5nTG9nKCk7XHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIG1lZGlhUmVjb3JkZXIuY2xlYXJSZWNvcmRlZERhdGEoKTtcclxuXHJcbiAgICAgICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zb2xlLmxvZygnQ2xlYXJlZCBvbGQgcmVjb3JkZWQgZGF0YS4nKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0sXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIEdldCB0aGUgcmVjb3JkZWQgYmxvYi4gVXNlIHRoaXMgbWV0aG9kIGluc2lkZSB0aGUgXCJzdG9wUmVjb3JkaW5nXCIgY2FsbGJhY2suXHJcbiAgICAgICAgICogQG1ldGhvZFxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAaW5zdGFuY2VcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICogICAgIHZhciBibG9iID0gdGhpcy5nZXRCbG9iKCk7XHJcbiAgICAgICAgICpcclxuICAgICAgICAgKiAgICAgdmFyIGZpbGUgPSBuZXcgRmlsZShbYmxvYl0sICdmaWxlbmFtZS53ZWJtJywge1xyXG4gICAgICAgICAqICAgICAgICAgdHlwZTogJ3ZpZGVvL3dlYm0nXHJcbiAgICAgICAgICogICAgIH0pO1xyXG4gICAgICAgICAqXHJcbiAgICAgICAgICogICAgIHZhciBmb3JtRGF0YSA9IG5ldyBGb3JtRGF0YSgpO1xyXG4gICAgICAgICAqICAgICBmb3JtRGF0YS5hcHBlbmQoJ2ZpbGUnLCBmaWxlKTsgLy8gdXBsb2FkIFwiRmlsZVwiIG9iamVjdCByYXRoZXIgdGhhbiBhIFwiQmxvYlwiXHJcbiAgICAgICAgICogICAgIHVwbG9hZFRvU2VydmVyKGZvcm1EYXRhKTtcclxuICAgICAgICAgKiB9KTtcclxuICAgICAgICAgKiBAcmV0dXJucyB7QmxvYn0gUmV0dXJucyByZWNvcmRlZCBkYXRhIGFzIFwiQmxvYlwiIG9iamVjdC5cclxuICAgICAgICAgKi9cclxuICAgICAgICBnZXRCbG9iOiBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgICAgICB3YXJuaW5nTG9nKCk7XHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIHJldHVybiBtZWRpYVJlY29yZGVyLmJsb2I7XHJcbiAgICAgICAgfSxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogR2V0IGRhdGEtVVJJIGluc3RlYWQgb2YgQmxvYi5cclxuICAgICAgICAgKiBAcGFyYW0ge2Z1bmN0aW9ufSBjYWxsYmFjayAtIENhbGxiYWNrIHRvIGdldCB0aGUgRGF0YS1VUkkuXHJcbiAgICAgICAgICogQG1ldGhvZFxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAaW5zdGFuY2VcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICogICAgIHJlY29yZGVyLmdldERhdGFVUkwoZnVuY3Rpb24oZGF0YVVSSSkge1xyXG4gICAgICAgICAqICAgICAgICAgdmlkZW8uc3JjID0gZGF0YVVSSTtcclxuICAgICAgICAgKiAgICAgfSk7XHJcbiAgICAgICAgICogfSk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgZ2V0RGF0YVVSTDogZ2V0RGF0YVVSTCxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogR2V0IHZpcnR1YWwvdGVtcG9yYXJ5IFVSTC4gVXNhZ2Ugb2YgdGhpcyBVUkwgaXMgbGltaXRlZCB0byBjdXJyZW50IHRhYi5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogcmVjb3JkZXIuc3RvcFJlY29yZGluZyhmdW5jdGlvbigpIHtcclxuICAgICAgICAgKiAgICAgdmlkZW8uc3JjID0gdGhpcy50b1VSTCgpO1xyXG4gICAgICAgICAqIH0pO1xyXG4gICAgICAgICAqIEByZXR1cm5zIHtTdHJpbmd9IFJldHVybnMgYSB2aXJ0dWFsL3RlbXBvcmFyeSBVUkwgZm9yIHRoZSByZWNvcmRlZCBcIkJsb2JcIi5cclxuICAgICAgICAgKi9cclxuICAgICAgICB0b1VSTDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIGlmICghbWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICAgICAgd2FybmluZ0xvZygpO1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICByZXR1cm4gVVJMLmNyZWF0ZU9iamVjdFVSTChtZWRpYVJlY29yZGVyLmJsb2IpO1xyXG4gICAgICAgIH0sXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIEdldCBpbnRlcm5hbCByZWNvcmRpbmcgb2JqZWN0IChpLmUuIGludGVybmFsIG1vZHVsZSkgZS5nLiBNdXRsaVN0cmVhbVJlY29yZGVyLCBNZWRpYVN0cmVhbVJlY29yZGVyLCBTdGVyZW9BdWRpb1JlY29yZGVyIG9yIFdoYW1teVJlY29yZGVyIGV0Yy5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogdmFyIGludGVybmFsUmVjb3JkZXIgPSByZWNvcmRlci5nZXRJbnRlcm5hbFJlY29yZGVyKCk7XHJcbiAgICAgICAgICogaWYoaW50ZXJuYWxSZWNvcmRlciBpbnN0YW5jZW9mIE11bHRpU3RyZWFtUmVjb3JkZXIpIHtcclxuICAgICAgICAgKiAgICAgaW50ZXJuYWxSZWNvcmRlci5hZGRTdHJlYW1zKFtuZXdBdWRpb1N0cmVhbV0pO1xyXG4gICAgICAgICAqICAgICBpbnRlcm5hbFJlY29yZGVyLnJlc2V0VmlkZW9TdHJlYW1zKFtzY3JlZW5TdHJlYW1dKTtcclxuICAgICAgICAgKiB9XHJcbiAgICAgICAgICogQHJldHVybnMge09iamVjdH0gUmV0dXJucyBpbnRlcm5hbCByZWNvcmRpbmcgb2JqZWN0LlxyXG4gICAgICAgICAqL1xyXG4gICAgICAgIGdldEludGVybmFsUmVjb3JkZXI6IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICByZXR1cm4gbWVkaWFSZWNvcmRlcjtcclxuICAgICAgICB9LFxyXG5cclxuICAgICAgICAvKipcclxuICAgICAgICAgKiBJbnZva2Ugc2F2ZS1hcyBkaWFsb2cgdG8gc2F2ZSB0aGUgcmVjb3JkZWQgYmxvYiBpbnRvIHlvdXIgZGlzay5cclxuICAgICAgICAgKiBAcGFyYW0ge3N0cmluZ30gZmlsZU5hbWUgLSBTZXQgeW91ciBvd24gZmlsZSBuYW1lLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5zdG9wUmVjb3JkaW5nKGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAqICAgICB0aGlzLnNhdmUoJ2ZpbGUtbmFtZScpO1xyXG4gICAgICAgICAqXHJcbiAgICAgICAgICogICAgIC8vIG9yIG1hbnVhbGx5OlxyXG4gICAgICAgICAqICAgICBpbnZva2VTYXZlQXNEaWFsb2codGhpcy5nZXRCbG9iKCksICdmaWxlbmFtZS53ZWJtJyk7XHJcbiAgICAgICAgICogfSk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgc2F2ZTogZnVuY3Rpb24oZmlsZU5hbWUpIHtcclxuICAgICAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgICAgICB3YXJuaW5nTG9nKCk7XHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGludm9rZVNhdmVBc0RpYWxvZyhtZWRpYVJlY29yZGVyLmJsb2IsIGZpbGVOYW1lKTtcclxuICAgICAgICB9LFxyXG5cclxuICAgICAgICAvKipcclxuICAgICAgICAgKiBUaGlzIG1ldGhvZCBnZXRzIGEgYmxvYiBmcm9tIGluZGV4ZWQtREIgc3RvcmFnZS5cclxuICAgICAgICAgKiBAcGFyYW0ge2Z1bmN0aW9ufSBjYWxsYmFjayAtIENhbGxiYWNrIHRvIGdldCB0aGUgcmVjb3JkZWQgYmxvYi5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogcmVjb3JkZXIuZ2V0RnJvbURpc2soZnVuY3Rpb24oZGF0YVVSTCkge1xyXG4gICAgICAgICAqICAgICB2aWRlby5zcmMgPSBkYXRhVVJMO1xyXG4gICAgICAgICAqIH0pO1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIGdldEZyb21EaXNrOiBmdW5jdGlvbihjYWxsYmFjaykge1xyXG4gICAgICAgICAgICBpZiAoIW1lZGlhUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgICAgIHdhcm5pbmdMb2coKTtcclxuICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgUmVjb3JkUlRDLmdldEZyb21EaXNrKGNvbmZpZy50eXBlLCBjYWxsYmFjayk7XHJcbiAgICAgICAgfSxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogVGhpcyBtZXRob2QgYXBwZW5kcyBhbiBhcnJheSBvZiB3ZWJwIGltYWdlcyB0byB0aGUgcmVjb3JkZWQgdmlkZW8tYmxvYi4gSXQgdGFrZXMgYW4gXCJhcnJheVwiIG9iamVjdC5cclxuICAgICAgICAgKiBAdHlwZSB7QXJyYXkuPEFycmF5Pn1cclxuICAgICAgICAgKiBAcGFyYW0ge0FycmF5fSBhcnJheU9mV2ViUEltYWdlcyAtIEFycmF5IG9mIHdlYnAgaW1hZ2VzLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQHRvZG8gVGhpcyBtZXRob2Qgc2hvdWxkIGJlIGRlcHJlY2F0ZWQuXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiB2YXIgYXJyYXlPZldlYlBJbWFnZXMgPSBbXTtcclxuICAgICAgICAgKiBhcnJheU9mV2ViUEltYWdlcy5wdXNoKHtcclxuICAgICAgICAgKiAgICAgZHVyYXRpb246IGluZGV4LFxyXG4gICAgICAgICAqICAgICBpbWFnZTogJ2RhdGE6aW1hZ2Uvd2VicDtiYXNlNjQsLi4uJ1xyXG4gICAgICAgICAqIH0pO1xyXG4gICAgICAgICAqIHJlY29yZGVyLnNldEFkdmVydGlzZW1lbnRBcnJheShhcnJheU9mV2ViUEltYWdlcyk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgc2V0QWR2ZXJ0aXNlbWVudEFycmF5OiBmdW5jdGlvbihhcnJheU9mV2ViUEltYWdlcykge1xyXG4gICAgICAgICAgICBjb25maWcuYWR2ZXJ0aXNlbWVudCA9IFtdO1xyXG5cclxuICAgICAgICAgICAgdmFyIGxlbmd0aCA9IGFycmF5T2ZXZWJQSW1hZ2VzLmxlbmd0aDtcclxuICAgICAgICAgICAgZm9yICh2YXIgaSA9IDA7IGkgPCBsZW5ndGg7IGkrKykge1xyXG4gICAgICAgICAgICAgICAgY29uZmlnLmFkdmVydGlzZW1lbnQucHVzaCh7XHJcbiAgICAgICAgICAgICAgICAgICAgZHVyYXRpb246IGksXHJcbiAgICAgICAgICAgICAgICAgICAgaW1hZ2U6IGFycmF5T2ZXZWJQSW1hZ2VzW2ldXHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0sXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIEl0IGlzIGVxdWl2YWxlbnQgdG8gPGNvZGUgY2xhc3M9XCJzdHJcIj5cInJlY29yZGVyLmdldEJsb2IoKVwiPC9jb2RlPiBtZXRob2QuIFVzYWdlIG9mIFwiZ2V0QmxvYlwiIGlzIHJlY29tbWVuZGVkLCB0aG91Z2guXHJcbiAgICAgICAgICogQHByb3BlcnR5IHtCbG9ifSBibG9iIC0gUmVjb3JkZWQgQmxvYiBjYW4gYmUgYWNjZXNzZWQgdXNpbmcgdGhpcyBwcm9wZXJ0eS5cclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGluc3RhbmNlXHJcbiAgICAgICAgICogQHJlYWRvbmx5XHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5zdG9wUmVjb3JkaW5nKGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAqICAgICB2YXIgYmxvYiA9IHRoaXMuYmxvYjtcclxuICAgICAgICAgKlxyXG4gICAgICAgICAqICAgICAvLyBiZWxvdyBvbmUgaXMgcmVjb21tZW5kZWRcclxuICAgICAgICAgKiAgICAgdmFyIGJsb2IgPSB0aGlzLmdldEJsb2IoKTtcclxuICAgICAgICAgKiB9KTtcclxuICAgICAgICAgKi9cclxuICAgICAgICBibG9iOiBudWxsLFxyXG5cclxuICAgICAgICAvKipcclxuICAgICAgICAgKiBUaGlzIHdvcmtzIG9ubHkgd2l0aCB7cmVjb3JkZXJUeXBlOlN0ZXJlb0F1ZGlvUmVjb3JkZXJ9LiBVc2UgdGhpcyBwcm9wZXJ0eSBvbiBcInN0b3BSZWNvcmRpbmdcIiB0byB2ZXJpZnkgdGhlIGVuY29kZXIncyBzYW1wbGUtcmF0ZXMuXHJcbiAgICAgICAgICogQHByb3BlcnR5IHtudW1iZXJ9IGJ1ZmZlclNpemUgLSBCdWZmZXItc2l6ZSB1c2VkIHRvIGVuY29kZSB0aGUgV0FWIGNvbnRhaW5lclxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAaW5zdGFuY2VcclxuICAgICAgICAgKiBAcmVhZG9ubHlcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICogICAgIGFsZXJ0KCdSZWNvcmRlciB1c2VkIHRoaXMgYnVmZmVyLXNpemU6ICcgKyB0aGlzLmJ1ZmZlclNpemUpO1xyXG4gICAgICAgICAqIH0pO1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIGJ1ZmZlclNpemU6IDAsXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIFRoaXMgd29ya3Mgb25seSB3aXRoIHtyZWNvcmRlclR5cGU6U3RlcmVvQXVkaW9SZWNvcmRlcn0uIFVzZSB0aGlzIHByb3BlcnR5IG9uIFwic3RvcFJlY29yZGluZ1wiIHRvIHZlcmlmeSB0aGUgZW5jb2RlcidzIHNhbXBsZS1yYXRlcy5cclxuICAgICAgICAgKiBAcHJvcGVydHkge251bWJlcn0gc2FtcGxlUmF0ZSAtIFNhbXBsZS1yYXRlcyB1c2VkIHRvIGVuY29kZSB0aGUgV0FWIGNvbnRhaW5lclxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAaW5zdGFuY2VcclxuICAgICAgICAgKiBAcmVhZG9ubHlcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICogICAgIGFsZXJ0KCdSZWNvcmRlciB1c2VkIHRoZXNlIHNhbXBsZS1yYXRlczogJyArIHRoaXMuc2FtcGxlUmF0ZSk7XHJcbiAgICAgICAgICogfSk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgc2FtcGxlUmF0ZTogMCxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICoge3JlY29yZGVyVHlwZTpTdGVyZW9BdWRpb1JlY29yZGVyfSByZXR1cm5zIEFycmF5QnVmZmVyIG9iamVjdC5cclxuICAgICAgICAgKiBAcHJvcGVydHkge0FycmF5QnVmZmVyfSBidWZmZXIgLSBBdWRpbyBBcnJheUJ1ZmZlciwgc3VwcG9ydGVkIG9ubHkgaW4gQ2hyb21lLlxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAaW5zdGFuY2VcclxuICAgICAgICAgKiBAcmVhZG9ubHlcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICogICAgIHZhciBhcnJheUJ1ZmZlciA9IHRoaXMuYnVmZmVyO1xyXG4gICAgICAgICAqICAgICBhbGVydChhcnJheUJ1ZmZlci5ieXRlTGVuZ3RoKTtcclxuICAgICAgICAgKiB9KTtcclxuICAgICAgICAgKi9cclxuICAgICAgICBidWZmZXI6IG51bGwsXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIFRoaXMgbWV0aG9kIHJlc2V0cyB0aGUgcmVjb3JkZXIuIFNvIHRoYXQgeW91IGNhbiByZXVzZSBzaW5nbGUgcmVjb3JkZXIgaW5zdGFuY2UgbWFueSB0aW1lcy5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogcmVjb3JkZXIucmVzZXQoKTtcclxuICAgICAgICAgKiByZWNvcmRlci5zdGFydFJlY29yZGluZygpO1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIHJlc2V0OiBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgaWYgKHNlbGYuc3RhdGUgPT09ICdyZWNvcmRpbmcnICYmICFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUud2FybignU3RvcCBhbiBhY3RpdmUgcmVjb3JkZXIuJyk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChtZWRpYVJlY29yZGVyICYmIHR5cGVvZiBtZWRpYVJlY29yZGVyLmNsZWFyUmVjb3JkZWREYXRhID09PSAnZnVuY3Rpb24nKSB7XHJcbiAgICAgICAgICAgICAgICBtZWRpYVJlY29yZGVyLmNsZWFyUmVjb3JkZWREYXRhKCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgbWVkaWFSZWNvcmRlciA9IG51bGw7XHJcbiAgICAgICAgICAgIHNldFN0YXRlKCdpbmFjdGl2ZScpO1xyXG4gICAgICAgICAgICBzZWxmLmJsb2IgPSBudWxsO1xyXG4gICAgICAgIH0sXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIFRoaXMgbWV0aG9kIGlzIGNhbGxlZCB3aGVuZXZlciByZWNvcmRlcidzIHN0YXRlIGNoYW5nZXMuIFVzZSB0aGlzIGFzIGFuIFwiZXZlbnRcIi5cclxuICAgICAgICAgKiBAcHJvcGVydHkge1N0cmluZ30gc3RhdGUgLSBBIHJlY29yZGVyJ3Mgc3RhdGUgY2FuIGJlOiByZWNvcmRpbmcsIHBhdXNlZCwgc3RvcHBlZCBvciBpbmFjdGl2ZS5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBpbnN0YW5jZVxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogcmVjb3JkZXIub25TdGF0ZUNoYW5nZWQgPSBmdW5jdGlvbihzdGF0ZSkge1xyXG4gICAgICAgICAqICAgICBjb25zb2xlLmxvZygnUmVjb3JkZXIgc3RhdGU6ICcsIHN0YXRlKTtcclxuICAgICAgICAgKiB9O1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIG9uU3RhdGVDaGFuZ2VkOiBmdW5jdGlvbihzdGF0ZSkge1xyXG4gICAgICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICAgICAgY29uc29sZS5sb2coJ1JlY29yZGVyIHN0YXRlIGNoYW5nZWQ6Jywgc3RhdGUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogQSByZWNvcmRlciBjYW4gaGF2ZSBpbmFjdGl2ZSwgcmVjb3JkaW5nLCBwYXVzZWQgb3Igc3RvcHBlZCBzdGF0ZXMuXHJcbiAgICAgICAgICogQHByb3BlcnR5IHtTdHJpbmd9IHN0YXRlIC0gQSByZWNvcmRlcidzIHN0YXRlIGNhbiBiZTogcmVjb3JkaW5nLCBwYXVzZWQsIHN0b3BwZWQgb3IgaW5hY3RpdmUuXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBzdGF0aWNcclxuICAgICAgICAgKiBAcmVhZG9ubHlcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIC8vIHRoaXMgbG9vcGVyIGZ1bmN0aW9uIHdpbGwga2VlcCB5b3UgdXBkYXRlZCBhYm91dCB0aGUgcmVjb3JkZXIncyBzdGF0ZXMuXHJcbiAgICAgICAgICogKGZ1bmN0aW9uIGxvb3BlcigpIHtcclxuICAgICAgICAgKiAgICAgZG9jdW1lbnQucXVlcnlTZWxlY3RvcignaDEnKS5pbm5lckhUTUwgPSAnUmVjb3JkZXJcXCdzIHN0YXRlIGlzOiAnICsgcmVjb3JkZXIuc3RhdGU7XHJcbiAgICAgICAgICogICAgIGlmKHJlY29yZGVyLnN0YXRlID09PSAnc3RvcHBlZCcpIHJldHVybjsgLy8gaWdub3JlK3N0b3BcclxuICAgICAgICAgKiAgICAgc2V0VGltZW91dChsb29wZXIsIDEwMDApOyAvLyB1cGRhdGUgYWZ0ZXIgZXZlcnkgMy1zZWNvbmRzXHJcbiAgICAgICAgICogfSkoKTtcclxuICAgICAgICAgKiByZWNvcmRlci5zdGFydFJlY29yZGluZygpO1xyXG4gICAgICAgICAqL1xyXG4gICAgICAgIHN0YXRlOiAnaW5hY3RpdmUnLFxyXG5cclxuICAgICAgICAvKipcclxuICAgICAgICAgKiBHZXQgcmVjb3JkZXIncyByZWFkb25seSBzdGF0ZS5cclxuICAgICAgICAgKiBAbWV0aG9kXHJcbiAgICAgICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1xyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogdmFyIHN0YXRlID0gcmVjb3JkZXIuZ2V0U3RhdGUoKTtcclxuICAgICAgICAgKiBAcmV0dXJucyB7U3RyaW5nfSBSZXR1cm5zIHJlY29yZGluZyBzdGF0ZS5cclxuICAgICAgICAgKi9cclxuICAgICAgICBnZXRTdGF0ZTogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIHJldHVybiBzZWxmLnN0YXRlO1xyXG4gICAgICAgIH0sXHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIERlc3Ryb3kgUmVjb3JkUlRDIGluc3RhbmNlLiBDbGVhciBhbGwgcmVjb3JkZXJzIGFuZCBvYmplY3RzLlxyXG4gICAgICAgICAqIEBtZXRob2RcclxuICAgICAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5kZXN0cm95KCk7XHJcbiAgICAgICAgICovXHJcbiAgICAgICAgZGVzdHJveTogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIHZhciBkaXNhYmxlTG9nc0NhY2hlID0gY29uZmlnLmRpc2FibGVMb2dzO1xyXG5cclxuICAgICAgICAgICAgY29uZmlnID0ge1xyXG4gICAgICAgICAgICAgICAgZGlzYWJsZUxvZ3M6IHRydWVcclxuICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgc2VsZi5yZXNldCgpO1xyXG4gICAgICAgICAgICBzZXRTdGF0ZSgnZGVzdHJveWVkJyk7XHJcbiAgICAgICAgICAgIHJldHVybk9iamVjdCA9IHNlbGYgPSBudWxsO1xyXG5cclxuICAgICAgICAgICAgaWYgKFN0b3JhZ2UuQXVkaW9Db250ZXh0Q29uc3RydWN0b3IpIHtcclxuICAgICAgICAgICAgICAgIFN0b3JhZ2UuQXVkaW9Db250ZXh0Q29uc3RydWN0b3IuY2xvc2UoKTtcclxuICAgICAgICAgICAgICAgIFN0b3JhZ2UuQXVkaW9Db250ZXh0Q29uc3RydWN0b3IgPSBudWxsO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBjb25maWcuZGlzYWJsZUxvZ3MgPSBkaXNhYmxlTG9nc0NhY2hlO1xyXG5cclxuICAgICAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKCdSZWNvcmRSVEMgaXMgZGVzdHJveWVkLicpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSxcclxuXHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogUmVjb3JkUlRDIHZlcnNpb24gbnVtYmVyXHJcbiAgICAgICAgICogQHByb3BlcnR5IHtTdHJpbmd9IHZlcnNpb24gLSBSZWxlYXNlIHZlcnNpb24gbnVtYmVyLlxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENcclxuICAgICAgICAgKiBAc3RhdGljXHJcbiAgICAgICAgICogQHJlYWRvbmx5XHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiBhbGVydChyZWNvcmRlci52ZXJzaW9uKTtcclxuICAgICAgICAgKi9cclxuICAgICAgICB2ZXJzaW9uOiAnNS41LjknXHJcbiAgICB9O1xyXG5cclxuICAgIGlmICghdGhpcykge1xyXG4gICAgICAgIHNlbGYgPSByZXR1cm5PYmplY3Q7XHJcbiAgICAgICAgcmV0dXJuIHJldHVybk9iamVjdDtcclxuICAgIH1cclxuXHJcbiAgICAvLyBpZiBzb21lb25lIHdhbnRzIHRvIHVzZSBSZWNvcmRSVEMgd2l0aCB0aGUgXCJuZXdcIiBrZXl3b3JkLlxyXG4gICAgZm9yICh2YXIgcHJvcCBpbiByZXR1cm5PYmplY3QpIHtcclxuICAgICAgICB0aGlzW3Byb3BdID0gcmV0dXJuT2JqZWN0W3Byb3BdO1xyXG4gICAgfVxyXG5cclxuICAgIHNlbGYgPSB0aGlzO1xyXG5cclxuICAgIHJldHVybiByZXR1cm5PYmplY3Q7XHJcbn1cclxuXHJcblJlY29yZFJUQy52ZXJzaW9uID0gJzUuNS45JztcclxuXHJcbmlmICh0eXBlb2YgbW9kdWxlICE9PSAndW5kZWZpbmVkJyAvKiAmJiAhIW1vZHVsZS5leHBvcnRzKi8gKSB7XHJcbiAgICBtb2R1bGUuZXhwb3J0cyA9IFJlY29yZFJUQztcclxufVxyXG5cclxuaWYgKHR5cGVvZiBkZWZpbmUgPT09ICdmdW5jdGlvbicgJiYgZGVmaW5lLmFtZCkge1xyXG4gICAgZGVmaW5lKCdSZWNvcmRSVEMnLCBbXSwgZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIFJlY29yZFJUQztcclxuICAgIH0pO1xyXG59XG5cclxuUmVjb3JkUlRDLmdldEZyb21EaXNrID0gZnVuY3Rpb24odHlwZSwgY2FsbGJhY2spIHtcclxuICAgIGlmICghY2FsbGJhY2spIHtcclxuICAgICAgICB0aHJvdyAnY2FsbGJhY2sgaXMgbWFuZGF0b3J5Lic7XHJcbiAgICB9XHJcblxyXG4gICAgY29uc29sZS5sb2coJ0dldHRpbmcgcmVjb3JkZWQgJyArICh0eXBlID09PSAnYWxsJyA/ICdibG9icycgOiB0eXBlICsgJyBibG9iICcpICsgJyBmcm9tIGRpc2shJyk7XHJcbiAgICBEaXNrU3RvcmFnZS5GZXRjaChmdW5jdGlvbihkYXRhVVJMLCBfdHlwZSkge1xyXG4gICAgICAgIGlmICh0eXBlICE9PSAnYWxsJyAmJiBfdHlwZSA9PT0gdHlwZSArICdCbG9iJyAmJiBjYWxsYmFjaykge1xyXG4gICAgICAgICAgICBjYWxsYmFjayhkYXRhVVJMKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0eXBlID09PSAnYWxsJyAmJiBjYWxsYmFjaykge1xyXG4gICAgICAgICAgICBjYWxsYmFjayhkYXRhVVJMLCBfdHlwZS5yZXBsYWNlKCdCbG9iJywgJycpKTtcclxuICAgICAgICB9XHJcbiAgICB9KTtcclxufTtcclxuXHJcbi8qKlxyXG4gKiBUaGlzIG1ldGhvZCBjYW4gYmUgdXNlZCB0byBzdG9yZSByZWNvcmRlZCBibG9icyBpbnRvIEluZGV4ZWREQiBzdG9yYWdlLlxyXG4gKiBAcGFyYW0ge29iamVjdH0gb3B0aW9ucyAtIHthdWRpbzogQmxvYiwgdmlkZW86IEJsb2IsIGdpZjogQmxvYn1cclxuICogQG1ldGhvZFxyXG4gKiBAbWVtYmVyb2YgUmVjb3JkUlRDXHJcbiAqIEBleGFtcGxlXHJcbiAqIFJlY29yZFJUQy53cml0ZVRvRGlzayh7XHJcbiAqICAgICBhdWRpbzogYXVkaW9CbG9iLFxyXG4gKiAgICAgdmlkZW86IHZpZGVvQmxvYixcclxuICogICAgIGdpZiAgOiBnaWZCbG9iXHJcbiAqIH0pO1xyXG4gKi9cclxuUmVjb3JkUlRDLndyaXRlVG9EaXNrID0gZnVuY3Rpb24ob3B0aW9ucykge1xyXG4gICAgY29uc29sZS5sb2coJ1dyaXRpbmcgcmVjb3JkZWQgYmxvYihzKSB0byBkaXNrIScpO1xyXG4gICAgb3B0aW9ucyA9IG9wdGlvbnMgfHwge307XHJcbiAgICBpZiAob3B0aW9ucy5hdWRpbyAmJiBvcHRpb25zLnZpZGVvICYmIG9wdGlvbnMuZ2lmKSB7XHJcbiAgICAgICAgb3B0aW9ucy5hdWRpby5nZXREYXRhVVJMKGZ1bmN0aW9uKGF1ZGlvRGF0YVVSTCkge1xyXG4gICAgICAgICAgICBvcHRpb25zLnZpZGVvLmdldERhdGFVUkwoZnVuY3Rpb24odmlkZW9EYXRhVVJMKSB7XHJcbiAgICAgICAgICAgICAgICBvcHRpb25zLmdpZi5nZXREYXRhVVJMKGZ1bmN0aW9uKGdpZkRhdGFVUkwpIHtcclxuICAgICAgICAgICAgICAgICAgICBEaXNrU3RvcmFnZS5TdG9yZSh7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGF1ZGlvQmxvYjogYXVkaW9EYXRhVVJMLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICB2aWRlb0Jsb2I6IHZpZGVvRGF0YVVSTCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgZ2lmQmxvYjogZ2lmRGF0YVVSTFxyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH0pO1xyXG4gICAgfSBlbHNlIGlmIChvcHRpb25zLmF1ZGlvICYmIG9wdGlvbnMudmlkZW8pIHtcclxuICAgICAgICBvcHRpb25zLmF1ZGlvLmdldERhdGFVUkwoZnVuY3Rpb24oYXVkaW9EYXRhVVJMKSB7XHJcbiAgICAgICAgICAgIG9wdGlvbnMudmlkZW8uZ2V0RGF0YVVSTChmdW5jdGlvbih2aWRlb0RhdGFVUkwpIHtcclxuICAgICAgICAgICAgICAgIERpc2tTdG9yYWdlLlN0b3JlKHtcclxuICAgICAgICAgICAgICAgICAgICBhdWRpb0Jsb2I6IGF1ZGlvRGF0YVVSTCxcclxuICAgICAgICAgICAgICAgICAgICB2aWRlb0Jsb2I6IHZpZGVvRGF0YVVSTFxyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH0pO1xyXG4gICAgfSBlbHNlIGlmIChvcHRpb25zLmF1ZGlvICYmIG9wdGlvbnMuZ2lmKSB7XHJcbiAgICAgICAgb3B0aW9ucy5hdWRpby5nZXREYXRhVVJMKGZ1bmN0aW9uKGF1ZGlvRGF0YVVSTCkge1xyXG4gICAgICAgICAgICBvcHRpb25zLmdpZi5nZXREYXRhVVJMKGZ1bmN0aW9uKGdpZkRhdGFVUkwpIHtcclxuICAgICAgICAgICAgICAgIERpc2tTdG9yYWdlLlN0b3JlKHtcclxuICAgICAgICAgICAgICAgICAgICBhdWRpb0Jsb2I6IGF1ZGlvRGF0YVVSTCxcclxuICAgICAgICAgICAgICAgICAgICBnaWZCbG9iOiBnaWZEYXRhVVJMXHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfSk7XHJcbiAgICB9IGVsc2UgaWYgKG9wdGlvbnMudmlkZW8gJiYgb3B0aW9ucy5naWYpIHtcclxuICAgICAgICBvcHRpb25zLnZpZGVvLmdldERhdGFVUkwoZnVuY3Rpb24odmlkZW9EYXRhVVJMKSB7XHJcbiAgICAgICAgICAgIG9wdGlvbnMuZ2lmLmdldERhdGFVUkwoZnVuY3Rpb24oZ2lmRGF0YVVSTCkge1xyXG4gICAgICAgICAgICAgICAgRGlza1N0b3JhZ2UuU3RvcmUoe1xyXG4gICAgICAgICAgICAgICAgICAgIHZpZGVvQmxvYjogdmlkZW9EYXRhVVJMLFxyXG4gICAgICAgICAgICAgICAgICAgIGdpZkJsb2I6IGdpZkRhdGFVUkxcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9KTtcclxuICAgIH0gZWxzZSBpZiAob3B0aW9ucy5hdWRpbykge1xyXG4gICAgICAgIG9wdGlvbnMuYXVkaW8uZ2V0RGF0YVVSTChmdW5jdGlvbihhdWRpb0RhdGFVUkwpIHtcclxuICAgICAgICAgICAgRGlza1N0b3JhZ2UuU3RvcmUoe1xyXG4gICAgICAgICAgICAgICAgYXVkaW9CbG9iOiBhdWRpb0RhdGFVUkxcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfSk7XHJcbiAgICB9IGVsc2UgaWYgKG9wdGlvbnMudmlkZW8pIHtcclxuICAgICAgICBvcHRpb25zLnZpZGVvLmdldERhdGFVUkwoZnVuY3Rpb24odmlkZW9EYXRhVVJMKSB7XHJcbiAgICAgICAgICAgIERpc2tTdG9yYWdlLlN0b3JlKHtcclxuICAgICAgICAgICAgICAgIHZpZGVvQmxvYjogdmlkZW9EYXRhVVJMXHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH0pO1xyXG4gICAgfSBlbHNlIGlmIChvcHRpb25zLmdpZikge1xyXG4gICAgICAgIG9wdGlvbnMuZ2lmLmdldERhdGFVUkwoZnVuY3Rpb24oZ2lmRGF0YVVSTCkge1xyXG4gICAgICAgICAgICBEaXNrU3RvcmFnZS5TdG9yZSh7XHJcbiAgICAgICAgICAgICAgICBnaWZCbG9iOiBnaWZEYXRhVVJMXHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH0pO1xyXG4gICAgfVxyXG59O1xuXHJcbi8vIF9fX19fX19fX19fX19fX19fX19fX19fX19fXHJcbi8vIFJlY29yZFJUQy1Db25maWd1cmF0aW9uLmpzXHJcblxyXG4vKipcclxuICoge0BsaW5rIFJlY29yZFJUQ0NvbmZpZ3VyYXRpb259IGlzIGFuIGlubmVyL3ByaXZhdGUgaGVscGVyIGZvciB7QGxpbmsgUmVjb3JkUlRDfS5cclxuICogQHN1bW1hcnkgSXQgY29uZmlndXJlcyB0aGUgMm5kIHBhcmFtZXRlciBwYXNzZWQgb3ZlciB7QGxpbmsgUmVjb3JkUlRDfSBhbmQgcmV0dXJucyBhIHZhbGlkIFwiY29uZmlnXCIgb2JqZWN0LlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQHR5cGVkZWYgUmVjb3JkUlRDQ29uZmlndXJhdGlvblxyXG4gKiBAY2xhc3NcclxuICogQGV4YW1wbGVcclxuICogdmFyIG9wdGlvbnMgPSBSZWNvcmRSVENDb25maWd1cmF0aW9uKG1lZGlhU3RyZWFtLCBvcHRpb25zKTtcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKiBAcGFyYW0ge01lZGlhU3RyZWFtfSBtZWRpYVN0cmVhbSAtIE1lZGlhU3RyZWFtIG9iamVjdCBmZXRjaGVkIHVzaW5nIGdldFVzZXJNZWRpYSBBUEkgb3IgZ2VuZXJhdGVkIHVzaW5nIGNhcHR1cmVTdHJlYW1VbnRpbEVuZGVkIG9yIFdlYkF1ZGlvIEFQSS5cclxuICogQHBhcmFtIHtvYmplY3R9IGNvbmZpZyAtIHt0eXBlOlwidmlkZW9cIiwgZGlzYWJsZUxvZ3M6IHRydWUsIG51bWJlck9mQXVkaW9DaGFubmVsczogMSwgYnVmZmVyU2l6ZTogMCwgc2FtcGxlUmF0ZTogMCwgdmlkZW86IEhUTUxWaWRlb0VsZW1lbnQsIGdldE5hdGl2ZUJsb2I6dHJ1ZSwgZXRjLn1cclxuICovXHJcblxyXG5mdW5jdGlvbiBSZWNvcmRSVENDb25maWd1cmF0aW9uKG1lZGlhU3RyZWFtLCBjb25maWcpIHtcclxuICAgIGlmICghY29uZmlnLnJlY29yZGVyVHlwZSAmJiAhY29uZmlnLnR5cGUpIHtcclxuICAgICAgICBpZiAoISFjb25maWcuYXVkaW8gJiYgISFjb25maWcudmlkZW8pIHtcclxuICAgICAgICAgICAgY29uZmlnLnR5cGUgPSAndmlkZW8nO1xyXG4gICAgICAgIH0gZWxzZSBpZiAoISFjb25maWcuYXVkaW8gJiYgIWNvbmZpZy52aWRlbykge1xyXG4gICAgICAgICAgICBjb25maWcudHlwZSA9ICdhdWRpbyc7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGlmIChjb25maWcucmVjb3JkZXJUeXBlICYmICFjb25maWcudHlwZSkge1xyXG4gICAgICAgIGlmIChjb25maWcucmVjb3JkZXJUeXBlID09PSBXaGFtbXlSZWNvcmRlciB8fCBjb25maWcucmVjb3JkZXJUeXBlID09PSBDYW52YXNSZWNvcmRlciB8fCAodHlwZW9mIFdlYkFzc2VtYmx5UmVjb3JkZXIgIT09ICd1bmRlZmluZWQnICYmIGNvbmZpZy5yZWNvcmRlclR5cGUgPT09IFdlYkFzc2VtYmx5UmVjb3JkZXIpKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy50eXBlID0gJ3ZpZGVvJztcclxuICAgICAgICB9IGVsc2UgaWYgKGNvbmZpZy5yZWNvcmRlclR5cGUgPT09IEdpZlJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy50eXBlID0gJ2dpZic7XHJcbiAgICAgICAgfSBlbHNlIGlmIChjb25maWcucmVjb3JkZXJUeXBlID09PSBTdGVyZW9BdWRpb1JlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy50eXBlID0gJ2F1ZGlvJztcclxuICAgICAgICB9IGVsc2UgaWYgKGNvbmZpZy5yZWNvcmRlclR5cGUgPT09IE1lZGlhU3RyZWFtUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgaWYgKGdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ2F1ZGlvJykubGVuZ3RoICYmIGdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ3ZpZGVvJykubGVuZ3RoKSB7XHJcbiAgICAgICAgICAgICAgICBjb25maWcudHlwZSA9ICd2aWRlbyc7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAoIWdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ2F1ZGlvJykubGVuZ3RoICYmIGdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ3ZpZGVvJykubGVuZ3RoKSB7XHJcbiAgICAgICAgICAgICAgICBjb25maWcudHlwZSA9ICd2aWRlbyc7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAoZ2V0VHJhY2tzKG1lZGlhU3RyZWFtLCAnYXVkaW8nKS5sZW5ndGggJiYgIWdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ3ZpZGVvJykubGVuZ3RoKSB7XHJcbiAgICAgICAgICAgICAgICBjb25maWcudHlwZSA9ICdhdWRpbyc7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAvLyBjb25maWcudHlwZSA9ICdVbktub3duJztcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIE1lZGlhU3RyZWFtUmVjb3JkZXIgIT09ICd1bmRlZmluZWQnICYmIHR5cGVvZiBNZWRpYVJlY29yZGVyICE9PSAndW5kZWZpbmVkJyAmJiAncmVxdWVzdERhdGEnIGluIE1lZGlhUmVjb3JkZXIucHJvdG90eXBlKSB7XHJcbiAgICAgICAgaWYgKCFjb25maWcubWltZVR5cGUpIHtcclxuICAgICAgICAgICAgY29uZmlnLm1pbWVUeXBlID0gJ3ZpZGVvL3dlYm0nO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFjb25maWcudHlwZSkge1xyXG4gICAgICAgICAgICBjb25maWcudHlwZSA9IGNvbmZpZy5taW1lVHlwZS5zcGxpdCgnLycpWzBdO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFjb25maWcuYml0c1BlclNlY29uZCkge1xyXG4gICAgICAgICAgICAvLyBjb25maWcuYml0c1BlclNlY29uZCA9IDEyODAwMDtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgLy8gY29uc2lkZXIgZGVmYXVsdCB0eXBlPWF1ZGlvXHJcbiAgICBpZiAoIWNvbmZpZy50eXBlKSB7XHJcbiAgICAgICAgaWYgKGNvbmZpZy5taW1lVHlwZSkge1xyXG4gICAgICAgICAgICBjb25maWcudHlwZSA9IGNvbmZpZy5taW1lVHlwZS5zcGxpdCgnLycpWzBdO1xyXG4gICAgICAgIH1cclxuICAgICAgICBpZiAoIWNvbmZpZy50eXBlKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy50eXBlID0gJ2F1ZGlvJztcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgcmV0dXJuIGNvbmZpZztcclxufVxuXHJcbi8vIF9fX19fX19fX19fX19fX19fX1xyXG4vLyBHZXRSZWNvcmRlclR5cGUuanNcclxuXHJcbi8qKlxyXG4gKiB7QGxpbmsgR2V0UmVjb3JkZXJUeXBlfSBpcyBhbiBpbm5lci9wcml2YXRlIGhlbHBlciBmb3Ige0BsaW5rIFJlY29yZFJUQ30uXHJcbiAqIEBzdW1tYXJ5IEl0IHJldHVybnMgYmVzdCByZWNvcmRlci10eXBlIGF2YWlsYWJsZSBmb3IgeW91ciBicm93c2VyLlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQHR5cGVkZWYgR2V0UmVjb3JkZXJUeXBlXHJcbiAqIEBjbGFzc1xyXG4gKiBAZXhhbXBsZVxyXG4gKiB2YXIgUmVjb3JkZXJUeXBlID0gR2V0UmVjb3JkZXJUeXBlKG9wdGlvbnMpO1xyXG4gKiB2YXIgcmVjb3JkZXIgPSBuZXcgUmVjb3JkZXJUeXBlKG9wdGlvbnMpO1xyXG4gKiBAc2VlIHtAbGluayBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQ3xSZWNvcmRSVEMgU291cmNlIENvZGV9XHJcbiAqIEBwYXJhbSB7TWVkaWFTdHJlYW19IG1lZGlhU3RyZWFtIC0gTWVkaWFTdHJlYW0gb2JqZWN0IGZldGNoZWQgdXNpbmcgZ2V0VXNlck1lZGlhIEFQSSBvciBnZW5lcmF0ZWQgdXNpbmcgY2FwdHVyZVN0cmVhbVVudGlsRW5kZWQgb3IgV2ViQXVkaW8gQVBJLlxyXG4gKiBAcGFyYW0ge29iamVjdH0gY29uZmlnIC0ge3R5cGU6XCJ2aWRlb1wiLCBkaXNhYmxlTG9nczogdHJ1ZSwgbnVtYmVyT2ZBdWRpb0NoYW5uZWxzOiAxLCBidWZmZXJTaXplOiAwLCBzYW1wbGVSYXRlOiAwLCB2aWRlbzogSFRNTFZpZGVvRWxlbWVudCwgZXRjLn1cclxuICovXHJcblxyXG5mdW5jdGlvbiBHZXRSZWNvcmRlclR5cGUobWVkaWFTdHJlYW0sIGNvbmZpZykge1xyXG4gICAgdmFyIHJlY29yZGVyO1xyXG5cclxuICAgIC8vIFN0ZXJlb0F1ZGlvUmVjb3JkZXIgY2FuIHdvcmsgd2l0aCBhbGwgdGhyZWU6IEVkZ2UsIEZpcmVmb3ggYW5kIENocm9tZVxyXG4gICAgLy8gdG9kbzogZGV0ZWN0IGlmIGl0IGlzIEVkZ2UsIHRoZW4gYXV0byB1c2U6IFN0ZXJlb0F1ZGlvUmVjb3JkZXJcclxuICAgIGlmIChpc0Nocm9tZSB8fCBpc0VkZ2UgfHwgaXNPcGVyYSkge1xyXG4gICAgICAgIC8vIE1lZGlhIFN0cmVhbSBSZWNvcmRpbmcgQVBJIGhhcyBub3QgYmVlbiBpbXBsZW1lbnRlZCBpbiBjaHJvbWUgeWV0O1xyXG4gICAgICAgIC8vIFRoYXQncyB3aHkgdXNpbmcgV2ViQXVkaW8gQVBJIHRvIHJlY29yZCBzdGVyZW8gYXVkaW8gaW4gV0FWIGZvcm1hdFxyXG4gICAgICAgIHJlY29yZGVyID0gU3RlcmVvQXVkaW9SZWNvcmRlcjtcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIE1lZGlhUmVjb3JkZXIgIT09ICd1bmRlZmluZWQnICYmICdyZXF1ZXN0RGF0YScgaW4gTWVkaWFSZWNvcmRlci5wcm90b3R5cGUgJiYgIWlzQ2hyb21lKSB7XHJcbiAgICAgICAgcmVjb3JkZXIgPSBNZWRpYVN0cmVhbVJlY29yZGVyO1xyXG4gICAgfVxyXG5cclxuICAgIC8vIHZpZGVvIHJlY29yZGVyIChpbiBXZWJNIGZvcm1hdClcclxuICAgIGlmIChjb25maWcudHlwZSA9PT0gJ3ZpZGVvJyAmJiAoaXNDaHJvbWUgfHwgaXNPcGVyYSkpIHtcclxuICAgICAgICByZWNvcmRlciA9IFdoYW1teVJlY29yZGVyO1xyXG5cclxuICAgICAgICBpZiAodHlwZW9mIFdlYkFzc2VtYmx5UmVjb3JkZXIgIT09ICd1bmRlZmluZWQnICYmIHR5cGVvZiBSZWFkYWJsZVN0cmVhbSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAgICAgcmVjb3JkZXIgPSBXZWJBc3NlbWJseVJlY29yZGVyO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICAvLyB2aWRlbyByZWNvcmRlciAoaW4gR2lmIGZvcm1hdClcclxuICAgIGlmIChjb25maWcudHlwZSA9PT0gJ2dpZicpIHtcclxuICAgICAgICByZWNvcmRlciA9IEdpZlJlY29yZGVyO1xyXG4gICAgfVxyXG5cclxuICAgIC8vIGh0bWwyY2FudmFzIHJlY29yZGluZyFcclxuICAgIGlmIChjb25maWcudHlwZSA9PT0gJ2NhbnZhcycpIHtcclxuICAgICAgICByZWNvcmRlciA9IENhbnZhc1JlY29yZGVyO1xyXG4gICAgfVxyXG5cclxuICAgIGlmIChpc01lZGlhUmVjb3JkZXJDb21wYXRpYmxlKCkgJiYgcmVjb3JkZXIgIT09IENhbnZhc1JlY29yZGVyICYmIHJlY29yZGVyICE9PSBHaWZSZWNvcmRlciAmJiB0eXBlb2YgTWVkaWFSZWNvcmRlciAhPT0gJ3VuZGVmaW5lZCcgJiYgJ3JlcXVlc3REYXRhJyBpbiBNZWRpYVJlY29yZGVyLnByb3RvdHlwZSkge1xyXG4gICAgICAgIGlmIChnZXRUcmFja3MobWVkaWFTdHJlYW0sICd2aWRlbycpLmxlbmd0aCB8fCBnZXRUcmFja3MobWVkaWFTdHJlYW0sICdhdWRpbycpLmxlbmd0aCkge1xyXG4gICAgICAgICAgICAvLyBhdWRpby1vbmx5IHJlY29yZGluZ1xyXG4gICAgICAgICAgICBpZiAoY29uZmlnLnR5cGUgPT09ICdhdWRpbycpIHtcclxuICAgICAgICAgICAgICAgIGlmICh0eXBlb2YgTWVkaWFSZWNvcmRlci5pc1R5cGVTdXBwb3J0ZWQgPT09ICdmdW5jdGlvbicgJiYgTWVkaWFSZWNvcmRlci5pc1R5cGVTdXBwb3J0ZWQoJ2F1ZGlvL3dlYm0nKSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHJlY29yZGVyID0gTWVkaWFTdHJlYW1SZWNvcmRlcjtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIC8vIGVsc2UgcmVjb3JkZXIgPSBTdGVyZW9BdWRpb1JlY29yZGVyO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgLy8gdmlkZW8gb3Igc2NyZWVuIHRyYWNrc1xyXG4gICAgICAgICAgICAgICAgaWYgKHR5cGVvZiBNZWRpYVJlY29yZGVyLmlzVHlwZVN1cHBvcnRlZCA9PT0gJ2Z1bmN0aW9uJyAmJiBNZWRpYVJlY29yZGVyLmlzVHlwZVN1cHBvcnRlZCgndmlkZW8vd2VibScpKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmVjb3JkZXIgPSBNZWRpYVN0cmVhbVJlY29yZGVyO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGlmIChtZWRpYVN0cmVhbSBpbnN0YW5jZW9mIEFycmF5ICYmIG1lZGlhU3RyZWFtLmxlbmd0aCkge1xyXG4gICAgICAgIHJlY29yZGVyID0gTXVsdGlTdHJlYW1SZWNvcmRlcjtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoY29uZmlnLnJlY29yZGVyVHlwZSkge1xyXG4gICAgICAgIHJlY29yZGVyID0gY29uZmlnLnJlY29yZGVyVHlwZTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncyAmJiAhIXJlY29yZGVyICYmICEhcmVjb3JkZXIubmFtZSkge1xyXG4gICAgICAgIGNvbnNvbGUubG9nKCdVc2luZyByZWNvcmRlclR5cGU6JywgcmVjb3JkZXIubmFtZSB8fCByZWNvcmRlci5jb25zdHJ1Y3Rvci5uYW1lKTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoIXJlY29yZGVyICYmIGlzU2FmYXJpKSB7XHJcbiAgICAgICAgcmVjb3JkZXIgPSBNZWRpYVN0cmVhbVJlY29yZGVyO1xyXG4gICAgfVxyXG5cclxuICAgIHJldHVybiByZWNvcmRlcjtcclxufVxuXHJcbi8vIF9fX19fX19fX19fX19cclxuLy8gTVJlY29yZFJUQy5qc1xyXG5cclxuLyoqXHJcbiAqIE1SZWNvcmRSVEMgcnVucyBvbiB0b3Agb2Yge0BsaW5rIFJlY29yZFJUQ30gdG8gYnJpbmcgbXVsdGlwbGUgcmVjb3JkaW5ncyBpbiBhIHNpbmdsZSBwbGFjZSwgYnkgcHJvdmlkaW5nIHNpbXBsZSBBUEkuXHJcbiAqIEBzdW1tYXJ5IE1SZWNvcmRSVEMgc3RhbmRzIGZvciBcIk11bHRpcGxlLVJlY29yZFJUQ1wiLlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQHR5cGVkZWYgTVJlY29yZFJUQ1xyXG4gKiBAY2xhc3NcclxuICogQGV4YW1wbGVcclxuICogdmFyIHJlY29yZGVyID0gbmV3IE1SZWNvcmRSVEMoKTtcclxuICogcmVjb3JkZXIuYWRkU3RyZWFtKE1lZGlhU3RyZWFtKTtcclxuICogcmVjb3JkZXIubWVkaWFUeXBlID0ge1xyXG4gKiAgICAgYXVkaW86IHRydWUsIC8vIG9yIFN0ZXJlb0F1ZGlvUmVjb3JkZXIgb3IgTWVkaWFTdHJlYW1SZWNvcmRlclxyXG4gKiAgICAgdmlkZW86IHRydWUsIC8vIG9yIFdoYW1teVJlY29yZGVyIG9yIE1lZGlhU3RyZWFtUmVjb3JkZXIgb3IgV2ViQXNzZW1ibHlSZWNvcmRlciBvciBDYW52YXNSZWNvcmRlclxyXG4gKiAgICAgZ2lmOiB0cnVlICAgIC8vIG9yIEdpZlJlY29yZGVyXHJcbiAqIH07XHJcbiAqIC8vIG1pbWVUeXBlIGlzIG9wdGlvbmFsIGFuZCBzaG91bGQgYmUgc2V0IG9ubHkgaW4gYWR2YW5jZSBjYXNlcy5cclxuICogcmVjb3JkZXIubWltZVR5cGUgPSB7XHJcbiAqICAgICBhdWRpbzogJ2F1ZGlvL3dhdicsXHJcbiAqICAgICB2aWRlbzogJ3ZpZGVvL3dlYm0nLFxyXG4gKiAgICAgZ2lmOiAgICdpbWFnZS9naWYnXHJcbiAqIH07XHJcbiAqIHJlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKCk7XHJcbiAqIEBzZWUgRm9yIGZ1cnRoZXIgaW5mb3JtYXRpb246XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDL3RyZWUvbWFzdGVyL01SZWNvcmRSVEN8TVJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICogQHBhcmFtIHtNZWRpYVN0cmVhbX0gbWVkaWFTdHJlYW0gLSBNZWRpYVN0cmVhbSBvYmplY3QgZmV0Y2hlZCB1c2luZyBnZXRVc2VyTWVkaWEgQVBJIG9yIGdlbmVyYXRlZCB1c2luZyBjYXB0dXJlU3RyZWFtVW50aWxFbmRlZCBvciBXZWJBdWRpbyBBUEkuXHJcbiAqIEByZXF1aXJlcyB7QGxpbmsgUmVjb3JkUlRDfVxyXG4gKi9cclxuXHJcbmZ1bmN0aW9uIE1SZWNvcmRSVEMobWVkaWFTdHJlYW0pIHtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIGF0dGFjaGVzIE1lZGlhU3RyZWFtIG9iamVjdCB0byB7QGxpbmsgTVJlY29yZFJUQ30uXHJcbiAgICAgKiBAcGFyYW0ge01lZGlhU3RyZWFtfSBtZWRpYVN0cmVhbSAtIEEgTWVkaWFTdHJlYW0gb2JqZWN0LCBlaXRoZXIgZmV0Y2hlZCB1c2luZyBnZXRVc2VyTWVkaWEgQVBJLCBvciBnZW5lcmF0ZWQgdXNpbmcgY2FwdHVyZVN0cmVhbVVudGlsRW5kZWQgb3IgV2ViQXVkaW8gQVBJLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1SZWNvcmRSVENcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5hZGRTdHJlYW0oTWVkaWFTdHJlYW0pO1xyXG4gICAgICovXHJcbiAgICB0aGlzLmFkZFN0cmVhbSA9IGZ1bmN0aW9uKF9tZWRpYVN0cmVhbSkge1xyXG4gICAgICAgIGlmIChfbWVkaWFTdHJlYW0pIHtcclxuICAgICAgICAgICAgbWVkaWFTdHJlYW0gPSBfbWVkaWFTdHJlYW07XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgcHJvcGVydHkgY2FuIGJlIHVzZWQgdG8gc2V0IHRoZSByZWNvcmRpbmcgdHlwZSBlLmcuIGF1ZGlvLCBvciB2aWRlbywgb3IgZ2lmLCBvciBjYW52YXMuXHJcbiAgICAgKiBAcHJvcGVydHkge29iamVjdH0gbWVkaWFUeXBlIC0ge2F1ZGlvOiB0cnVlLCB2aWRlbzogdHJ1ZSwgZ2lmOiB0cnVlfVxyXG4gICAgICogQG1lbWJlcm9mIE1SZWNvcmRSVENcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiB2YXIgcmVjb3JkZXIgPSBuZXcgTVJlY29yZFJUQygpO1xyXG4gICAgICogcmVjb3JkZXIubWVkaWFUeXBlID0ge1xyXG4gICAgICogICAgIGF1ZGlvOiB0cnVlLCAvLyBUUlVFIG9yIFN0ZXJlb0F1ZGlvUmVjb3JkZXIgb3IgTWVkaWFTdHJlYW1SZWNvcmRlclxyXG4gICAgICogICAgIHZpZGVvOiB0cnVlLCAvLyBUUlVFIG9yIFdoYW1teVJlY29yZGVyIG9yIE1lZGlhU3RyZWFtUmVjb3JkZXIgb3IgV2ViQXNzZW1ibHlSZWNvcmRlciBvciBDYW52YXNSZWNvcmRlclxyXG4gICAgICogICAgIGdpZiAgOiB0cnVlICAvLyBUUlVFIG9yIEdpZlJlY29yZGVyXHJcbiAgICAgKiB9O1xyXG4gICAgICovXHJcbiAgICB0aGlzLm1lZGlhVHlwZSA9IHtcclxuICAgICAgICBhdWRpbzogdHJ1ZSxcclxuICAgICAgICB2aWRlbzogdHJ1ZVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHN0YXJ0cyByZWNvcmRpbmcuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuc3RhcnRSZWNvcmRpbmcgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICB2YXIgbWVkaWFUeXBlID0gdGhpcy5tZWRpYVR5cGU7XHJcbiAgICAgICAgdmFyIHJlY29yZGVyVHlwZTtcclxuICAgICAgICB2YXIgbWltZVR5cGUgPSB0aGlzLm1pbWVUeXBlIHx8IHtcclxuICAgICAgICAgICAgYXVkaW86IG51bGwsXHJcbiAgICAgICAgICAgIHZpZGVvOiBudWxsLFxyXG4gICAgICAgICAgICBnaWY6IG51bGxcclxuICAgICAgICB9O1xyXG5cclxuICAgICAgICBpZiAodHlwZW9mIG1lZGlhVHlwZS5hdWRpbyAhPT0gJ2Z1bmN0aW9uJyAmJiBpc01lZGlhUmVjb3JkZXJDb21wYXRpYmxlKCkgJiYgIWdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ2F1ZGlvJykubGVuZ3RoKSB7XHJcbiAgICAgICAgICAgIG1lZGlhVHlwZS5hdWRpbyA9IGZhbHNlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiBtZWRpYVR5cGUudmlkZW8gIT09ICdmdW5jdGlvbicgJiYgaXNNZWRpYVJlY29yZGVyQ29tcGF0aWJsZSgpICYmICFnZXRUcmFja3MobWVkaWFTdHJlYW0sICd2aWRlbycpLmxlbmd0aCkge1xyXG4gICAgICAgICAgICBtZWRpYVR5cGUudmlkZW8gPSBmYWxzZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgbWVkaWFUeXBlLmdpZiAhPT0gJ2Z1bmN0aW9uJyAmJiBpc01lZGlhUmVjb3JkZXJDb21wYXRpYmxlKCkgJiYgIWdldFRyYWNrcyhtZWRpYVN0cmVhbSwgJ3ZpZGVvJykubGVuZ3RoKSB7XHJcbiAgICAgICAgICAgIG1lZGlhVHlwZS5naWYgPSBmYWxzZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghbWVkaWFUeXBlLmF1ZGlvICYmICFtZWRpYVR5cGUudmlkZW8gJiYgIW1lZGlhVHlwZS5naWYpIHtcclxuICAgICAgICAgICAgdGhyb3cgJ01lZGlhU3RyZWFtIG11c3QgaGF2ZSBlaXRoZXIgYXVkaW8gb3IgdmlkZW8gdHJhY2tzLic7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoISFtZWRpYVR5cGUuYXVkaW8pIHtcclxuICAgICAgICAgICAgcmVjb3JkZXJUeXBlID0gbnVsbDtcclxuICAgICAgICAgICAgaWYgKHR5cGVvZiBtZWRpYVR5cGUuYXVkaW8gPT09ICdmdW5jdGlvbicpIHtcclxuICAgICAgICAgICAgICAgIHJlY29yZGVyVHlwZSA9IG1lZGlhVHlwZS5hdWRpbztcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgdGhpcy5hdWRpb1JlY29yZGVyID0gbmV3IFJlY29yZFJUQyhtZWRpYVN0cmVhbSwge1xyXG4gICAgICAgICAgICAgICAgdHlwZTogJ2F1ZGlvJyxcclxuICAgICAgICAgICAgICAgIGJ1ZmZlclNpemU6IHRoaXMuYnVmZmVyU2l6ZSxcclxuICAgICAgICAgICAgICAgIHNhbXBsZVJhdGU6IHRoaXMuc2FtcGxlUmF0ZSxcclxuICAgICAgICAgICAgICAgIG51bWJlck9mQXVkaW9DaGFubmVsczogdGhpcy5udW1iZXJPZkF1ZGlvQ2hhbm5lbHMgfHwgMixcclxuICAgICAgICAgICAgICAgIGRpc2FibGVMb2dzOiB0aGlzLmRpc2FibGVMb2dzLFxyXG4gICAgICAgICAgICAgICAgcmVjb3JkZXJUeXBlOiByZWNvcmRlclR5cGUsXHJcbiAgICAgICAgICAgICAgICBtaW1lVHlwZTogbWltZVR5cGUuYXVkaW8sXHJcbiAgICAgICAgICAgICAgICB0aW1lU2xpY2U6IHRoaXMudGltZVNsaWNlLFxyXG4gICAgICAgICAgICAgICAgb25UaW1lU3RhbXA6IHRoaXMub25UaW1lU3RhbXBcclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICBpZiAoIW1lZGlhVHlwZS52aWRlbykge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5hdWRpb1JlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghIW1lZGlhVHlwZS52aWRlbykge1xyXG4gICAgICAgICAgICByZWNvcmRlclR5cGUgPSBudWxsO1xyXG4gICAgICAgICAgICBpZiAodHlwZW9mIG1lZGlhVHlwZS52aWRlbyA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICAgICAgcmVjb3JkZXJUeXBlID0gbWVkaWFUeXBlLnZpZGVvO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICB2YXIgbmV3U3RyZWFtID0gbWVkaWFTdHJlYW07XHJcblxyXG4gICAgICAgICAgICBpZiAoaXNNZWRpYVJlY29yZGVyQ29tcGF0aWJsZSgpICYmICEhbWVkaWFUeXBlLmF1ZGlvICYmIHR5cGVvZiBtZWRpYVR5cGUuYXVkaW8gPT09ICdmdW5jdGlvbicpIHtcclxuICAgICAgICAgICAgICAgIHZhciB2aWRlb1RyYWNrID0gZ2V0VHJhY2tzKG1lZGlhU3RyZWFtLCAndmlkZW8nKVswXTtcclxuXHJcbiAgICAgICAgICAgICAgICBpZiAoaXNGaXJlZm94KSB7XHJcbiAgICAgICAgICAgICAgICAgICAgbmV3U3RyZWFtID0gbmV3IE1lZGlhU3RyZWFtKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgbmV3U3RyZWFtLmFkZFRyYWNrKHZpZGVvVHJhY2spO1xyXG5cclxuICAgICAgICAgICAgICAgICAgICBpZiAocmVjb3JkZXJUeXBlICYmIHJlY29yZGVyVHlwZSA9PT0gV2hhbW15UmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgLy8gRmlyZWZveCBkb2VzIE5PVCBzdXBwb3J0cyB3ZWJwLWVuY29kaW5nIHlldFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAvLyBCdXQgRmlyZWZveCBkbyBzdXBwb3J0cyBXZWJBc3NlbWJseVJlY29yZGVyXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJlY29yZGVyVHlwZSA9IE1lZGlhU3RyZWFtUmVjb3JkZXI7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICBuZXdTdHJlYW0gPSBuZXcgTWVkaWFTdHJlYW0oKTtcclxuICAgICAgICAgICAgICAgICAgICBuZXdTdHJlYW0uYWRkVHJhY2sodmlkZW9UcmFjayk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIHRoaXMudmlkZW9SZWNvcmRlciA9IG5ldyBSZWNvcmRSVEMobmV3U3RyZWFtLCB7XHJcbiAgICAgICAgICAgICAgICB0eXBlOiAndmlkZW8nLFxyXG4gICAgICAgICAgICAgICAgdmlkZW86IHRoaXMudmlkZW8sXHJcbiAgICAgICAgICAgICAgICBjYW52YXM6IHRoaXMuY2FudmFzLFxyXG4gICAgICAgICAgICAgICAgZnJhbWVJbnRlcnZhbDogdGhpcy5mcmFtZUludGVydmFsIHx8IDEwLFxyXG4gICAgICAgICAgICAgICAgZGlzYWJsZUxvZ3M6IHRoaXMuZGlzYWJsZUxvZ3MsXHJcbiAgICAgICAgICAgICAgICByZWNvcmRlclR5cGU6IHJlY29yZGVyVHlwZSxcclxuICAgICAgICAgICAgICAgIG1pbWVUeXBlOiBtaW1lVHlwZS52aWRlbyxcclxuICAgICAgICAgICAgICAgIHRpbWVTbGljZTogdGhpcy50aW1lU2xpY2UsXHJcbiAgICAgICAgICAgICAgICBvblRpbWVTdGFtcDogdGhpcy5vblRpbWVTdGFtcCxcclxuICAgICAgICAgICAgICAgIHdvcmtlclBhdGg6IHRoaXMud29ya2VyUGF0aCxcclxuICAgICAgICAgICAgICAgIHdlYkFzc2VtYmx5UGF0aDogdGhpcy53ZWJBc3NlbWJseVBhdGgsXHJcbiAgICAgICAgICAgICAgICBmcmFtZVJhdGU6IHRoaXMuZnJhbWVSYXRlLCAvLyB1c2VkIGJ5IFdlYkFzc2VtYmx5UmVjb3JkZXI7IHZhbHVlczogdXN1YWxseSAzMDsgYWNjZXB0cyBhbnkuXHJcbiAgICAgICAgICAgICAgICBiaXRyYXRlOiB0aGlzLmJpdHJhdGUgLy8gdXNlZCBieSBXZWJBc3NlbWJseVJlY29yZGVyOyB2YWx1ZXM6IDAgdG8gMTAwMCtcclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICBpZiAoIW1lZGlhVHlwZS5hdWRpbykge1xyXG4gICAgICAgICAgICAgICAgdGhpcy52aWRlb1JlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghIW1lZGlhVHlwZS5hdWRpbyAmJiAhIW1lZGlhVHlwZS52aWRlbykge1xyXG4gICAgICAgICAgICB2YXIgc2VsZiA9IHRoaXM7XHJcblxyXG4gICAgICAgICAgICB2YXIgaXNTaW5nbGVSZWNvcmRlciA9IGlzTWVkaWFSZWNvcmRlckNvbXBhdGlibGUoKSA9PT0gdHJ1ZTtcclxuXHJcbiAgICAgICAgICAgIGlmIChtZWRpYVR5cGUuYXVkaW8gaW5zdGFuY2VvZiBTdGVyZW9BdWRpb1JlY29yZGVyICYmICEhbWVkaWFUeXBlLnZpZGVvKSB7XHJcbiAgICAgICAgICAgICAgICBpc1NpbmdsZVJlY29yZGVyID0gZmFsc2U7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAobWVkaWFUeXBlLmF1ZGlvICE9PSB0cnVlICYmIG1lZGlhVHlwZS52aWRlbyAhPT0gdHJ1ZSAmJiBtZWRpYVR5cGUuYXVkaW8gIT09IG1lZGlhVHlwZS52aWRlbykge1xyXG4gICAgICAgICAgICAgICAgaXNTaW5nbGVSZWNvcmRlciA9IGZhbHNlO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoaXNTaW5nbGVSZWNvcmRlciA9PT0gdHJ1ZSkge1xyXG4gICAgICAgICAgICAgICAgc2VsZi5hdWRpb1JlY29yZGVyID0gbnVsbDtcclxuICAgICAgICAgICAgICAgIHNlbGYudmlkZW9SZWNvcmRlci5zdGFydFJlY29yZGluZygpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgc2VsZi52aWRlb1JlY29yZGVyLmluaXRSZWNvcmRlcihmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgICAgICBzZWxmLmF1ZGlvUmVjb3JkZXIuaW5pdFJlY29yZGVyKGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAvLyBCb3RoIHJlY29yZGVycyBhcmUgcmVhZHkgdG8gcmVjb3JkIHRoaW5ncyBhY2N1cmF0ZWx5XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHNlbGYudmlkZW9SZWNvcmRlci5zdGFydFJlY29yZGluZygpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBzZWxmLmF1ZGlvUmVjb3JkZXIuc3RhcnRSZWNvcmRpbmcoKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoISFtZWRpYVR5cGUuZ2lmKSB7XHJcbiAgICAgICAgICAgIHJlY29yZGVyVHlwZSA9IG51bGw7XHJcbiAgICAgICAgICAgIGlmICh0eXBlb2YgbWVkaWFUeXBlLmdpZiA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICAgICAgcmVjb3JkZXJUeXBlID0gbWVkaWFUeXBlLmdpZjtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB0aGlzLmdpZlJlY29yZGVyID0gbmV3IFJlY29yZFJUQyhtZWRpYVN0cmVhbSwge1xyXG4gICAgICAgICAgICAgICAgdHlwZTogJ2dpZicsXHJcbiAgICAgICAgICAgICAgICBmcmFtZVJhdGU6IHRoaXMuZnJhbWVSYXRlIHx8IDIwMCxcclxuICAgICAgICAgICAgICAgIHF1YWxpdHk6IHRoaXMucXVhbGl0eSB8fCAxMCxcclxuICAgICAgICAgICAgICAgIGRpc2FibGVMb2dzOiB0aGlzLmRpc2FibGVMb2dzLFxyXG4gICAgICAgICAgICAgICAgcmVjb3JkZXJUeXBlOiByZWNvcmRlclR5cGUsXHJcbiAgICAgICAgICAgICAgICBtaW1lVHlwZTogbWltZVR5cGUuZ2lmXHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB0aGlzLmdpZlJlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHN0b3BzIHJlY29yZGluZy5cclxuICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQ2FsbGJhY2sgZnVuY3Rpb24gaXMgaW52b2tlZCB3aGVuIGFsbCBlbmNvZGVycyBmaW5pc2hlZCB0aGVpciBqb2JzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1SZWNvcmRSVENcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5zdG9wUmVjb3JkaW5nKGZ1bmN0aW9uKHJlY29yZGluZyl7XHJcbiAgICAgKiAgICAgdmFyIGF1ZGlvQmxvYiA9IHJlY29yZGluZy5hdWRpbztcclxuICAgICAqICAgICB2YXIgdmlkZW9CbG9iID0gcmVjb3JkaW5nLnZpZGVvO1xyXG4gICAgICogICAgIHZhciBnaWZCbG9iICAgPSByZWNvcmRpbmcuZ2lmO1xyXG4gICAgICogfSk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuc3RvcFJlY29yZGluZyA9IGZ1bmN0aW9uKGNhbGxiYWNrKSB7XHJcbiAgICAgICAgY2FsbGJhY2sgPSBjYWxsYmFjayB8fCBmdW5jdGlvbigpIHt9O1xyXG5cclxuICAgICAgICBpZiAodGhpcy5hdWRpb1JlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHRoaXMuYXVkaW9SZWNvcmRlci5zdG9wUmVjb3JkaW5nKGZ1bmN0aW9uKGJsb2JVUkwpIHtcclxuICAgICAgICAgICAgICAgIGNhbGxiYWNrKGJsb2JVUkwsICdhdWRpbycpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0aGlzLnZpZGVvUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgdGhpcy52aWRlb1JlY29yZGVyLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24oYmxvYlVSTCkge1xyXG4gICAgICAgICAgICAgICAgY2FsbGJhY2soYmxvYlVSTCwgJ3ZpZGVvJyk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHRoaXMuZ2lmUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgdGhpcy5naWZSZWNvcmRlci5zdG9wUmVjb3JkaW5nKGZ1bmN0aW9uKGJsb2JVUkwpIHtcclxuICAgICAgICAgICAgICAgIGNhbGxiYWNrKGJsb2JVUkwsICdnaWYnKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHBhdXNlcyByZWNvcmRpbmcuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnBhdXNlUmVjb3JkaW5nKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucGF1c2VSZWNvcmRpbmcgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpZiAodGhpcy5hdWRpb1JlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHRoaXMuYXVkaW9SZWNvcmRlci5wYXVzZVJlY29yZGluZygpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHRoaXMudmlkZW9SZWNvcmRlcikge1xyXG4gICAgICAgICAgICB0aGlzLnZpZGVvUmVjb3JkZXIucGF1c2VSZWNvcmRpbmcoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0aGlzLmdpZlJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHRoaXMuZ2lmUmVjb3JkZXIucGF1c2VSZWNvcmRpbmcoKTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVzdW1lcyByZWNvcmRpbmcuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnJlc3VtZVJlY29yZGluZygpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnJlc3VtZVJlY29yZGluZyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlmICh0aGlzLmF1ZGlvUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgdGhpcy5hdWRpb1JlY29yZGVyLnJlc3VtZVJlY29yZGluZygpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHRoaXMudmlkZW9SZWNvcmRlcikge1xyXG4gICAgICAgICAgICB0aGlzLnZpZGVvUmVjb3JkZXIucmVzdW1lUmVjb3JkaW5nKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAodGhpcy5naWZSZWNvcmRlcikge1xyXG4gICAgICAgICAgICB0aGlzLmdpZlJlY29yZGVyLnJlc3VtZVJlY29yZGluZygpO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBjYW4gYmUgdXNlZCB0byBtYW51YWxseSBnZXQgYWxsIHJlY29yZGVkIGJsb2JzLlxyXG4gICAgICogQHBhcmFtIHtmdW5jdGlvbn0gY2FsbGJhY2sgLSBBbGwgcmVjb3JkZWQgYmxvYnMgYXJlIHBhc3NlZCBiYWNrIHRvIHRoZSBcImNhbGxiYWNrXCIgZnVuY3Rpb24uXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLmdldEJsb2IoZnVuY3Rpb24ocmVjb3JkaW5nKXtcclxuICAgICAqICAgICB2YXIgYXVkaW9CbG9iID0gcmVjb3JkaW5nLmF1ZGlvO1xyXG4gICAgICogICAgIHZhciB2aWRlb0Jsb2IgPSByZWNvcmRpbmcudmlkZW87XHJcbiAgICAgKiAgICAgdmFyIGdpZkJsb2IgICA9IHJlY29yZGluZy5naWY7XHJcbiAgICAgKiB9KTtcclxuICAgICAqIC8vIG9yXHJcbiAgICAgKiB2YXIgYXVkaW9CbG9iID0gcmVjb3JkZXIuZ2V0QmxvYigpLmF1ZGlvO1xyXG4gICAgICogdmFyIHZpZGVvQmxvYiA9IHJlY29yZGVyLmdldEJsb2IoKS52aWRlbztcclxuICAgICAqL1xyXG4gICAgdGhpcy5nZXRCbG9iID0gZnVuY3Rpb24oY2FsbGJhY2spIHtcclxuICAgICAgICB2YXIgb3V0cHV0ID0ge307XHJcblxyXG4gICAgICAgIGlmICh0aGlzLmF1ZGlvUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgb3V0cHV0LmF1ZGlvID0gdGhpcy5hdWRpb1JlY29yZGVyLmdldEJsb2IoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0aGlzLnZpZGVvUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgb3V0cHV0LnZpZGVvID0gdGhpcy52aWRlb1JlY29yZGVyLmdldEJsb2IoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0aGlzLmdpZlJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIG91dHB1dC5naWYgPSB0aGlzLmdpZlJlY29yZGVyLmdldEJsb2IoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChjYWxsYmFjaykge1xyXG4gICAgICAgICAgICBjYWxsYmFjayhvdXRwdXQpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgcmV0dXJuIG91dHB1dDtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBEZXN0cm95IGFsbCByZWNvcmRlciBpbnN0YW5jZXMuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLmRlc3Ryb3koKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5kZXN0cm95ID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaWYgKHRoaXMuYXVkaW9SZWNvcmRlcikge1xyXG4gICAgICAgICAgICB0aGlzLmF1ZGlvUmVjb3JkZXIuZGVzdHJveSgpO1xyXG4gICAgICAgICAgICB0aGlzLmF1ZGlvUmVjb3JkZXIgPSBudWxsO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHRoaXMudmlkZW9SZWNvcmRlcikge1xyXG4gICAgICAgICAgICB0aGlzLnZpZGVvUmVjb3JkZXIuZGVzdHJveSgpO1xyXG4gICAgICAgICAgICB0aGlzLnZpZGVvUmVjb3JkZXIgPSBudWxsO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHRoaXMuZ2lmUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgdGhpcy5naWZSZWNvcmRlci5kZXN0cm95KCk7XHJcbiAgICAgICAgICAgIHRoaXMuZ2lmUmVjb3JkZXIgPSBudWxsO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBjYW4gYmUgdXNlZCB0byBtYW51YWxseSBnZXQgYWxsIHJlY29yZGVkIGJsb2JzJyBEYXRhVVJMcy5cclxuICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQWxsIHJlY29yZGVkIGJsb2JzJyBEYXRhVVJMcyBhcmUgcGFzc2VkIGJhY2sgdG8gdGhlIFwiY2FsbGJhY2tcIiBmdW5jdGlvbi5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBNUmVjb3JkUlRDXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuZ2V0RGF0YVVSTChmdW5jdGlvbihyZWNvcmRpbmcpe1xyXG4gICAgICogICAgIHZhciBhdWRpb0RhdGFVUkwgPSByZWNvcmRpbmcuYXVkaW87XHJcbiAgICAgKiAgICAgdmFyIHZpZGVvRGF0YVVSTCA9IHJlY29yZGluZy52aWRlbztcclxuICAgICAqICAgICB2YXIgZ2lmRGF0YVVSTCAgID0gcmVjb3JkaW5nLmdpZjtcclxuICAgICAqIH0pO1xyXG4gICAgICovXHJcbiAgICB0aGlzLmdldERhdGFVUkwgPSBmdW5jdGlvbihjYWxsYmFjaykge1xyXG4gICAgICAgIHRoaXMuZ2V0QmxvYihmdW5jdGlvbihibG9iKSB7XHJcbiAgICAgICAgICAgIGlmIChibG9iLmF1ZGlvICYmIGJsb2IudmlkZW8pIHtcclxuICAgICAgICAgICAgICAgIGdldERhdGFVUkwoYmxvYi5hdWRpbywgZnVuY3Rpb24oX2F1ZGlvRGF0YVVSTCkge1xyXG4gICAgICAgICAgICAgICAgICAgIGdldERhdGFVUkwoYmxvYi52aWRlbywgZnVuY3Rpb24oX3ZpZGVvRGF0YVVSTCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBjYWxsYmFjayh7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBhdWRpbzogX2F1ZGlvRGF0YVVSTCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHZpZGVvOiBfdmlkZW9EYXRhVVJMXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAoYmxvYi5hdWRpbykge1xyXG4gICAgICAgICAgICAgICAgZ2V0RGF0YVVSTChibG9iLmF1ZGlvLCBmdW5jdGlvbihfYXVkaW9EYXRhVVJMKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2soe1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBhdWRpbzogX2F1ZGlvRGF0YVVSTFxyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAoYmxvYi52aWRlbykge1xyXG4gICAgICAgICAgICAgICAgZ2V0RGF0YVVSTChibG9iLnZpZGVvLCBmdW5jdGlvbihfdmlkZW9EYXRhVVJMKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2soe1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB2aWRlbzogX3ZpZGVvRGF0YVVSTFxyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgZnVuY3Rpb24gZ2V0RGF0YVVSTChibG9iLCBjYWxsYmFjazAwKSB7XHJcbiAgICAgICAgICAgIGlmICh0eXBlb2YgV29ya2VyICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICAgICAgdmFyIHdlYldvcmtlciA9IHByb2Nlc3NJbldlYldvcmtlcihmdW5jdGlvbiByZWFkRmlsZShfYmxvYikge1xyXG4gICAgICAgICAgICAgICAgICAgIHBvc3RNZXNzYWdlKG5ldyBGaWxlUmVhZGVyU3luYygpLnJlYWRBc0RhdGFVUkwoX2Jsb2IpKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG5cclxuICAgICAgICAgICAgICAgIHdlYldvcmtlci5vbm1lc3NhZ2UgPSBmdW5jdGlvbihldmVudCkge1xyXG4gICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrMDAoZXZlbnQuZGF0YSk7XHJcbiAgICAgICAgICAgICAgICB9O1xyXG5cclxuICAgICAgICAgICAgICAgIHdlYldvcmtlci5wb3N0TWVzc2FnZShibG9iKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIHZhciByZWFkZXIgPSBuZXcgRmlsZVJlYWRlcigpO1xyXG4gICAgICAgICAgICAgICAgcmVhZGVyLnJlYWRBc0RhdGFVUkwoYmxvYik7XHJcbiAgICAgICAgICAgICAgICByZWFkZXIub25sb2FkID0gZnVuY3Rpb24oZXZlbnQpIHtcclxuICAgICAgICAgICAgICAgICAgICBjYWxsYmFjazAwKGV2ZW50LnRhcmdldC5yZXN1bHQpO1xyXG4gICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gcHJvY2Vzc0luV2ViV29ya2VyKF9mdW5jdGlvbikge1xyXG4gICAgICAgICAgICB2YXIgYmxvYiA9IFVSTC5jcmVhdGVPYmplY3RVUkwobmV3IEJsb2IoW19mdW5jdGlvbi50b1N0cmluZygpLFxyXG4gICAgICAgICAgICAgICAgJ3RoaXMub25tZXNzYWdlID0gIGZ1bmN0aW9uIChlZWUpIHsnICsgX2Z1bmN0aW9uLm5hbWUgKyAnKGVlZS5kYXRhKTt9J1xyXG4gICAgICAgICAgICBdLCB7XHJcbiAgICAgICAgICAgICAgICB0eXBlOiAnYXBwbGljYXRpb24vamF2YXNjcmlwdCdcclxuICAgICAgICAgICAgfSkpO1xyXG5cclxuICAgICAgICAgICAgdmFyIHdvcmtlciA9IG5ldyBXb3JrZXIoYmxvYik7XHJcbiAgICAgICAgICAgIHZhciB1cmw7XHJcbiAgICAgICAgICAgIGlmICh0eXBlb2YgVVJMICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICAgICAgdXJsID0gVVJMO1xyXG4gICAgICAgICAgICB9IGVsc2UgaWYgKHR5cGVvZiB3ZWJraXRVUkwgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgICAgICB1cmwgPSB3ZWJraXRVUkw7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICB0aHJvdyAnTmVpdGhlciBVUkwgbm9yIHdlYmtpdFVSTCBkZXRlY3RlZC4nO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHVybC5yZXZva2VPYmplY3RVUkwoYmxvYik7XHJcbiAgICAgICAgICAgIHJldHVybiB3b3JrZXI7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIGNhbiBiZSB1c2VkIHRvIGFzayB7QGxpbmsgTVJlY29yZFJUQ30gdG8gd3JpdGUgYWxsIHJlY29yZGVkIGJsb2JzIGludG8gSW5kZXhlZERCIHN0b3JhZ2UuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLndyaXRlVG9EaXNrKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMud3JpdGVUb0Rpc2sgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBSZWNvcmRSVEMud3JpdGVUb0Rpc2soe1xyXG4gICAgICAgICAgICBhdWRpbzogdGhpcy5hdWRpb1JlY29yZGVyLFxyXG4gICAgICAgICAgICB2aWRlbzogdGhpcy52aWRlb1JlY29yZGVyLFxyXG4gICAgICAgICAgICBnaWY6IHRoaXMuZ2lmUmVjb3JkZXJcclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBjYW4gYmUgdXNlZCB0byBpbnZva2UgYSBzYXZlLWFzIGRpYWxvZyBmb3IgYWxsIHJlY29yZGVkIGJsb2JzLlxyXG4gICAgICogQHBhcmFtIHtvYmplY3R9IGFyZ3MgLSB7YXVkaW86ICdhdWRpby1uYW1lJywgdmlkZW86ICd2aWRlby1uYW1lJywgZ2lmOiAnZ2lmLW5hbWUnfVxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1SZWNvcmRSVENcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5zYXZlKHtcclxuICAgICAqICAgICBhdWRpbzogJ2F1ZGlvLWZpbGUtbmFtZScsXHJcbiAgICAgKiAgICAgdmlkZW86ICd2aWRlby1maWxlLW5hbWUnLFxyXG4gICAgICogICAgIGdpZiAgOiAnZ2lmLWZpbGUtbmFtZSdcclxuICAgICAqIH0pO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnNhdmUgPSBmdW5jdGlvbihhcmdzKSB7XHJcbiAgICAgICAgYXJncyA9IGFyZ3MgfHwge1xyXG4gICAgICAgICAgICBhdWRpbzogdHJ1ZSxcclxuICAgICAgICAgICAgdmlkZW86IHRydWUsXHJcbiAgICAgICAgICAgIGdpZjogdHJ1ZVxyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIGlmICghIWFyZ3MuYXVkaW8gJiYgdGhpcy5hdWRpb1JlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHRoaXMuYXVkaW9SZWNvcmRlci5zYXZlKHR5cGVvZiBhcmdzLmF1ZGlvID09PSAnc3RyaW5nJyA/IGFyZ3MuYXVkaW8gOiAnJyk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoISFhcmdzLnZpZGVvICYmIHRoaXMudmlkZW9SZWNvcmRlcikge1xyXG4gICAgICAgICAgICB0aGlzLnZpZGVvUmVjb3JkZXIuc2F2ZSh0eXBlb2YgYXJncy52aWRlbyA9PT0gJ3N0cmluZycgPyBhcmdzLnZpZGVvIDogJycpO1xyXG4gICAgICAgIH1cclxuICAgICAgICBpZiAoISFhcmdzLmdpZiAmJiB0aGlzLmdpZlJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHRoaXMuZ2lmUmVjb3JkZXIuc2F2ZSh0eXBlb2YgYXJncy5naWYgPT09ICdzdHJpbmcnID8gYXJncy5naWYgOiAnJyk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxufVxyXG5cclxuLyoqXHJcbiAqIFRoaXMgbWV0aG9kIGNhbiBiZSB1c2VkIHRvIGdldCBhbGwgcmVjb3JkZWQgYmxvYnMgZnJvbSBJbmRleGVkREIgc3RvcmFnZS5cclxuICogQHBhcmFtIHtzdHJpbmd9IHR5cGUgLSAnYWxsJyBvciAnYXVkaW8nIG9yICd2aWRlbycgb3IgJ2dpZidcclxuICogQHBhcmFtIHtmdW5jdGlvbn0gY2FsbGJhY2sgLSBDYWxsYmFjayBmdW5jdGlvbiB0byBnZXQgYWxsIHN0b3JlZCBibG9icy5cclxuICogQG1ldGhvZFxyXG4gKiBAbWVtYmVyb2YgTVJlY29yZFJUQ1xyXG4gKiBAZXhhbXBsZVxyXG4gKiBNUmVjb3JkUlRDLmdldEZyb21EaXNrKCdhbGwnLCBmdW5jdGlvbihkYXRhVVJMLCB0eXBlKXtcclxuICogICAgIGlmKHR5cGUgPT09ICdhdWRpbycpIHsgfVxyXG4gKiAgICAgaWYodHlwZSA9PT0gJ3ZpZGVvJykgeyB9XHJcbiAqICAgICBpZih0eXBlID09PSAnZ2lmJykgICB7IH1cclxuICogfSk7XHJcbiAqL1xyXG5NUmVjb3JkUlRDLmdldEZyb21EaXNrID0gUmVjb3JkUlRDLmdldEZyb21EaXNrO1xyXG5cclxuLyoqXHJcbiAqIFRoaXMgbWV0aG9kIGNhbiBiZSB1c2VkIHRvIHN0b3JlIHJlY29yZGVkIGJsb2JzIGludG8gSW5kZXhlZERCIHN0b3JhZ2UuXHJcbiAqIEBwYXJhbSB7b2JqZWN0fSBvcHRpb25zIC0ge2F1ZGlvOiBCbG9iLCB2aWRlbzogQmxvYiwgZ2lmOiBCbG9ifVxyXG4gKiBAbWV0aG9kXHJcbiAqIEBtZW1iZXJvZiBNUmVjb3JkUlRDXHJcbiAqIEBleGFtcGxlXHJcbiAqIE1SZWNvcmRSVEMud3JpdGVUb0Rpc2soe1xyXG4gKiAgICAgYXVkaW86IGF1ZGlvQmxvYixcclxuICogICAgIHZpZGVvOiB2aWRlb0Jsb2IsXHJcbiAqICAgICBnaWYgIDogZ2lmQmxvYlxyXG4gKiB9KTtcclxuICovXHJcbk1SZWNvcmRSVEMud3JpdGVUb0Rpc2sgPSBSZWNvcmRSVEMud3JpdGVUb0Rpc2s7XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIFJlY29yZFJUQy5NUmVjb3JkUlRDID0gTVJlY29yZFJUQztcclxufVxuXHJcbnZhciBicm93c2VyRmFrZVVzZXJBZ2VudCA9ICdGYWtlLzUuMCAoRmFrZU9TKSBBcHBsZVdlYktpdC8xMjMgKEtIVE1MLCBsaWtlIEdlY2tvKSBGYWtlLzEyLjMuNDU2Ny44OSBGYWtlLzEyMy40NSc7XHJcblxyXG4oZnVuY3Rpb24odGhhdCkge1xyXG4gICAgaWYgKCF0aGF0KSB7XHJcbiAgICAgICAgcmV0dXJuO1xyXG4gICAgfVxyXG5cclxuICAgIGlmICh0eXBlb2Ygd2luZG93ICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIHJldHVybjtcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIGdsb2JhbCA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICByZXR1cm47XHJcbiAgICB9XHJcblxyXG4gICAgZ2xvYmFsLm5hdmlnYXRvciA9IHtcclxuICAgICAgICB1c2VyQWdlbnQ6IGJyb3dzZXJGYWtlVXNlckFnZW50LFxyXG4gICAgICAgIGdldFVzZXJNZWRpYTogZnVuY3Rpb24oKSB7fVxyXG4gICAgfTtcclxuXHJcbiAgICBpZiAoIWdsb2JhbC5jb25zb2xlKSB7XHJcbiAgICAgICAgZ2xvYmFsLmNvbnNvbGUgPSB7fTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIGdsb2JhbC5jb25zb2xlLmxvZyA9PT0gJ3VuZGVmaW5lZCcgfHwgdHlwZW9mIGdsb2JhbC5jb25zb2xlLmVycm9yID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIGdsb2JhbC5jb25zb2xlLmVycm9yID0gZ2xvYmFsLmNvbnNvbGUubG9nID0gZ2xvYmFsLmNvbnNvbGUubG9nIHx8IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZyhhcmd1bWVudHMpO1xyXG4gICAgICAgIH07XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKHR5cGVvZiBkb2N1bWVudCA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAvKmdsb2JhbCBkb2N1bWVudDp0cnVlICovXHJcbiAgICAgICAgdGhhdC5kb2N1bWVudCA9IHtcclxuICAgICAgICAgICAgZG9jdW1lbnRFbGVtZW50OiB7XHJcbiAgICAgICAgICAgICAgICBhcHBlbmRDaGlsZDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuICcnO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfTtcclxuXHJcbiAgICAgICAgZG9jdW1lbnQuY3JlYXRlRWxlbWVudCA9IGRvY3VtZW50LmNhcHR1cmVTdHJlYW0gPSBkb2N1bWVudC5tb3pDYXB0dXJlU3RyZWFtID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIHZhciBvYmogPSB7XHJcbiAgICAgICAgICAgICAgICBnZXRDb250ZXh0OiBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gb2JqO1xyXG4gICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgIHBsYXk6IGZ1bmN0aW9uKCkge30sXHJcbiAgICAgICAgICAgICAgICBwYXVzZTogZnVuY3Rpb24oKSB7fSxcclxuICAgICAgICAgICAgICAgIGRyYXdJbWFnZTogZnVuY3Rpb24oKSB7fSxcclxuICAgICAgICAgICAgICAgIHRvRGF0YVVSTDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuICcnO1xyXG4gICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgIHN0eWxlOiB7fVxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgICAgICByZXR1cm4gb2JqO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIHRoYXQuSFRNTFZpZGVvRWxlbWVudCA9IGZ1bmN0aW9uKCkge307XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKHR5cGVvZiBsb2NhdGlvbiA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAvKmdsb2JhbCBsb2NhdGlvbjp0cnVlICovXHJcbiAgICAgICAgdGhhdC5sb2NhdGlvbiA9IHtcclxuICAgICAgICAgICAgcHJvdG9jb2w6ICdmaWxlOicsXHJcbiAgICAgICAgICAgIGhyZWY6ICcnLFxyXG4gICAgICAgICAgICBoYXNoOiAnJ1xyXG4gICAgICAgIH07XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKHR5cGVvZiBzY3JlZW4gPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLypnbG9iYWwgc2NyZWVuOnRydWUgKi9cclxuICAgICAgICB0aGF0LnNjcmVlbiA9IHtcclxuICAgICAgICAgICAgd2lkdGg6IDAsXHJcbiAgICAgICAgICAgIGhlaWdodDogMFxyXG4gICAgICAgIH07XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKHR5cGVvZiBVUkwgPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLypnbG9iYWwgc2NyZWVuOnRydWUgKi9cclxuICAgICAgICB0aGF0LlVSTCA9IHtcclxuICAgICAgICAgICAgY3JlYXRlT2JqZWN0VVJMOiBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIHJldHVybiAnJztcclxuICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgcmV2b2tlT2JqZWN0VVJMOiBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIHJldHVybiAnJztcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH07XHJcbiAgICB9XHJcblxyXG4gICAgLypnbG9iYWwgd2luZG93OnRydWUgKi9cclxuICAgIHRoYXQud2luZG93ID0gZ2xvYmFsO1xyXG59KSh0eXBlb2YgZ2xvYmFsICE9PSAndW5kZWZpbmVkJyA/IGdsb2JhbCA6IG51bGwpO1xuXHJcbi8vIF9fX19fX19fX19fX19fX19fX19fX19fX19fX19fXHJcbi8vIENyb3NzLUJyb3dzZXItRGVjbGFyYXRpb25zLmpzXHJcblxyXG4vLyBhbmltYXRpb24tZnJhbWUgdXNlZCBpbiBXZWJNIHJlY29yZGluZ1xyXG5cclxuLypqc2hpbnQgLVcwNzkgKi9cclxudmFyIHJlcXVlc3RBbmltYXRpb25GcmFtZSA9IHdpbmRvdy5yZXF1ZXN0QW5pbWF0aW9uRnJhbWU7XHJcbmlmICh0eXBlb2YgcmVxdWVzdEFuaW1hdGlvbkZyYW1lID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgaWYgKHR5cGVvZiB3ZWJraXRSZXF1ZXN0QW5pbWF0aW9uRnJhbWUgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLypnbG9iYWwgcmVxdWVzdEFuaW1hdGlvbkZyYW1lOnRydWUgKi9cclxuICAgICAgICByZXF1ZXN0QW5pbWF0aW9uRnJhbWUgPSB3ZWJraXRSZXF1ZXN0QW5pbWF0aW9uRnJhbWU7XHJcbiAgICB9IGVsc2UgaWYgKHR5cGVvZiBtb3pSZXF1ZXN0QW5pbWF0aW9uRnJhbWUgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLypnbG9iYWwgcmVxdWVzdEFuaW1hdGlvbkZyYW1lOnRydWUgKi9cclxuICAgICAgICByZXF1ZXN0QW5pbWF0aW9uRnJhbWUgPSBtb3pSZXF1ZXN0QW5pbWF0aW9uRnJhbWU7XHJcbiAgICB9IGVsc2UgaWYgKHR5cGVvZiBtc1JlcXVlc3RBbmltYXRpb25GcmFtZSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAvKmdsb2JhbCByZXF1ZXN0QW5pbWF0aW9uRnJhbWU6dHJ1ZSAqL1xyXG4gICAgICAgIHJlcXVlc3RBbmltYXRpb25GcmFtZSA9IG1zUmVxdWVzdEFuaW1hdGlvbkZyYW1lO1xyXG4gICAgfSBlbHNlIGlmICh0eXBlb2YgcmVxdWVzdEFuaW1hdGlvbkZyYW1lID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIC8vIHZpYTogaHR0cHM6Ly9naXN0LmdpdGh1Yi5jb20vcGF1bGlyaXNoLzE1Nzk2NzFcclxuICAgICAgICB2YXIgbGFzdFRpbWUgPSAwO1xyXG5cclxuICAgICAgICAvKmdsb2JhbCByZXF1ZXN0QW5pbWF0aW9uRnJhbWU6dHJ1ZSAqL1xyXG4gICAgICAgIHJlcXVlc3RBbmltYXRpb25GcmFtZSA9IGZ1bmN0aW9uKGNhbGxiYWNrLCBlbGVtZW50KSB7XHJcbiAgICAgICAgICAgIHZhciBjdXJyVGltZSA9IG5ldyBEYXRlKCkuZ2V0VGltZSgpO1xyXG4gICAgICAgICAgICB2YXIgdGltZVRvQ2FsbCA9IE1hdGgubWF4KDAsIDE2IC0gKGN1cnJUaW1lIC0gbGFzdFRpbWUpKTtcclxuICAgICAgICAgICAgdmFyIGlkID0gc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIGNhbGxiYWNrKGN1cnJUaW1lICsgdGltZVRvQ2FsbCk7XHJcbiAgICAgICAgICAgIH0sIHRpbWVUb0NhbGwpO1xyXG4gICAgICAgICAgICBsYXN0VGltZSA9IGN1cnJUaW1lICsgdGltZVRvQ2FsbDtcclxuICAgICAgICAgICAgcmV0dXJuIGlkO1xyXG4gICAgICAgIH07XHJcbiAgICB9XHJcbn1cclxuXHJcbi8qanNoaW50IC1XMDc5ICovXHJcbnZhciBjYW5jZWxBbmltYXRpb25GcmFtZSA9IHdpbmRvdy5jYW5jZWxBbmltYXRpb25GcmFtZTtcclxuaWYgKHR5cGVvZiBjYW5jZWxBbmltYXRpb25GcmFtZSA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIGlmICh0eXBlb2Ygd2Via2l0Q2FuY2VsQW5pbWF0aW9uRnJhbWUgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLypnbG9iYWwgY2FuY2VsQW5pbWF0aW9uRnJhbWU6dHJ1ZSAqL1xyXG4gICAgICAgIGNhbmNlbEFuaW1hdGlvbkZyYW1lID0gd2Via2l0Q2FuY2VsQW5pbWF0aW9uRnJhbWU7XHJcbiAgICB9IGVsc2UgaWYgKHR5cGVvZiBtb3pDYW5jZWxBbmltYXRpb25GcmFtZSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAvKmdsb2JhbCBjYW5jZWxBbmltYXRpb25GcmFtZTp0cnVlICovXHJcbiAgICAgICAgY2FuY2VsQW5pbWF0aW9uRnJhbWUgPSBtb3pDYW5jZWxBbmltYXRpb25GcmFtZTtcclxuICAgIH0gZWxzZSBpZiAodHlwZW9mIG1zQ2FuY2VsQW5pbWF0aW9uRnJhbWUgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLypnbG9iYWwgY2FuY2VsQW5pbWF0aW9uRnJhbWU6dHJ1ZSAqL1xyXG4gICAgICAgIGNhbmNlbEFuaW1hdGlvbkZyYW1lID0gbXNDYW5jZWxBbmltYXRpb25GcmFtZTtcclxuICAgIH0gZWxzZSBpZiAodHlwZW9mIGNhbmNlbEFuaW1hdGlvbkZyYW1lID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIC8qZ2xvYmFsIGNhbmNlbEFuaW1hdGlvbkZyYW1lOnRydWUgKi9cclxuICAgICAgICBjYW5jZWxBbmltYXRpb25GcmFtZSA9IGZ1bmN0aW9uKGlkKSB7XHJcbiAgICAgICAgICAgIGNsZWFyVGltZW91dChpZCk7XHJcbiAgICAgICAgfTtcclxuICAgIH1cclxufVxyXG5cclxuLy8gV2ViQXVkaW8gQVBJIHJlcHJlc2VudGVyXHJcbnZhciBBdWRpb0NvbnRleHQgPSB3aW5kb3cuQXVkaW9Db250ZXh0O1xyXG5cclxuaWYgKHR5cGVvZiBBdWRpb0NvbnRleHQgPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICBpZiAodHlwZW9mIHdlYmtpdEF1ZGlvQ29udGV4dCAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAvKmdsb2JhbCBBdWRpb0NvbnRleHQ6dHJ1ZSAqL1xyXG4gICAgICAgIEF1ZGlvQ29udGV4dCA9IHdlYmtpdEF1ZGlvQ29udGV4dDtcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIG1vekF1ZGlvQ29udGV4dCAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAvKmdsb2JhbCBBdWRpb0NvbnRleHQ6dHJ1ZSAqL1xyXG4gICAgICAgIEF1ZGlvQ29udGV4dCA9IG1vekF1ZGlvQ29udGV4dDtcclxuICAgIH1cclxufVxyXG5cclxuLypqc2hpbnQgLVcwNzkgKi9cclxudmFyIFVSTCA9IHdpbmRvdy5VUkw7XHJcblxyXG5pZiAodHlwZW9mIFVSTCA9PT0gJ3VuZGVmaW5lZCcgJiYgdHlwZW9mIHdlYmtpdFVSTCAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIC8qZ2xvYmFsIFVSTDp0cnVlICovXHJcbiAgICBVUkwgPSB3ZWJraXRVUkw7XHJcbn1cclxuXHJcbmlmICh0eXBlb2YgbmF2aWdhdG9yICE9PSAndW5kZWZpbmVkJyAmJiB0eXBlb2YgbmF2aWdhdG9yLmdldFVzZXJNZWRpYSA9PT0gJ3VuZGVmaW5lZCcpIHsgLy8gbWF5YmUgd2luZG93Lm5hdmlnYXRvcj9cclxuICAgIGlmICh0eXBlb2YgbmF2aWdhdG9yLndlYmtpdEdldFVzZXJNZWRpYSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICBuYXZpZ2F0b3IuZ2V0VXNlck1lZGlhID0gbmF2aWdhdG9yLndlYmtpdEdldFVzZXJNZWRpYTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIG5hdmlnYXRvci5tb3pHZXRVc2VyTWVkaWEgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgbmF2aWdhdG9yLmdldFVzZXJNZWRpYSA9IG5hdmlnYXRvci5tb3pHZXRVc2VyTWVkaWE7XHJcbiAgICB9XHJcbn1cclxuXHJcbnZhciBpc0VkZ2UgPSBuYXZpZ2F0b3IudXNlckFnZW50LmluZGV4T2YoJ0VkZ2UnKSAhPT0gLTEgJiYgKCEhbmF2aWdhdG9yLm1zU2F2ZUJsb2IgfHwgISFuYXZpZ2F0b3IubXNTYXZlT3JPcGVuQmxvYik7XHJcbnZhciBpc09wZXJhID0gISF3aW5kb3cub3BlcmEgfHwgbmF2aWdhdG9yLnVzZXJBZ2VudC5pbmRleE9mKCdPUFIvJykgIT09IC0xO1xyXG52YXIgaXNGaXJlZm94ID0gbmF2aWdhdG9yLnVzZXJBZ2VudC50b0xvd2VyQ2FzZSgpLmluZGV4T2YoJ2ZpcmVmb3gnKSA+IC0xICYmICgnbmV0c2NhcGUnIGluIHdpbmRvdykgJiYgLyBydjovLnRlc3QobmF2aWdhdG9yLnVzZXJBZ2VudCk7XHJcbnZhciBpc0Nocm9tZSA9ICghaXNPcGVyYSAmJiAhaXNFZGdlICYmICEhbmF2aWdhdG9yLndlYmtpdEdldFVzZXJNZWRpYSkgfHwgaXNFbGVjdHJvbigpIHx8IG5hdmlnYXRvci51c2VyQWdlbnQudG9Mb3dlckNhc2UoKS5pbmRleE9mKCdjaHJvbWUvJykgIT09IC0xO1xyXG5cclxudmFyIGlzU2FmYXJpID0gL14oKD8hY2hyb21lfGFuZHJvaWQpLikqc2FmYXJpL2kudGVzdChuYXZpZ2F0b3IudXNlckFnZW50KTtcclxuXHJcbmlmIChpc1NhZmFyaSAmJiAhaXNDaHJvbWUgJiYgbmF2aWdhdG9yLnVzZXJBZ2VudC5pbmRleE9mKCdDcmlPUycpICE9PSAtMSkge1xyXG4gICAgaXNTYWZhcmkgPSBmYWxzZTtcclxuICAgIGlzQ2hyb21lID0gdHJ1ZTtcclxufVxyXG5cclxudmFyIE1lZGlhU3RyZWFtID0gd2luZG93Lk1lZGlhU3RyZWFtO1xyXG5cclxuaWYgKHR5cGVvZiBNZWRpYVN0cmVhbSA9PT0gJ3VuZGVmaW5lZCcgJiYgdHlwZW9mIHdlYmtpdE1lZGlhU3RyZWFtICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgTWVkaWFTdHJlYW0gPSB3ZWJraXRNZWRpYVN0cmVhbTtcclxufVxyXG5cclxuLypnbG9iYWwgTWVkaWFTdHJlYW06dHJ1ZSAqL1xyXG5pZiAodHlwZW9mIE1lZGlhU3RyZWFtICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgLy8gb3ZlcnJpZGUgXCJzdG9wXCIgbWV0aG9kIGZvciBhbGwgYnJvd3NlcnNcclxuICAgIGlmICh0eXBlb2YgTWVkaWFTdHJlYW0ucHJvdG90eXBlLnN0b3AgPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgTWVkaWFTdHJlYW0ucHJvdG90eXBlLnN0b3AgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgdGhpcy5nZXRUcmFja3MoKS5mb3JFYWNoKGZ1bmN0aW9uKHRyYWNrKSB7XHJcbiAgICAgICAgICAgICAgICB0cmFjay5zdG9wKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH07XHJcbiAgICB9XHJcbn1cclxuXHJcbi8vIGJlbG93IGZ1bmN0aW9uIHZpYTogaHR0cDovL2dvby5nbC9CM2FlOGNcclxuLyoqXHJcbiAqIFJldHVybiBodW1hbi1yZWFkYWJsZSBmaWxlIHNpemUuXHJcbiAqIEBwYXJhbSB7bnVtYmVyfSBieXRlcyAtIFBhc3MgYnl0ZXMgYW5kIGdldCBmb3JtYXR0ZWQgc3RyaW5nLlxyXG4gKiBAcmV0dXJucyB7c3RyaW5nfSAtIGZvcm1hdHRlZCBzdHJpbmdcclxuICogQGV4YW1wbGVcclxuICogYnl0ZXNUb1NpemUoMTAyNCoxMDI0KjUpID09PSAnNSBHQidcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKi9cclxuZnVuY3Rpb24gYnl0ZXNUb1NpemUoYnl0ZXMpIHtcclxuICAgIHZhciBrID0gMTAwMDtcclxuICAgIHZhciBzaXplcyA9IFsnQnl0ZXMnLCAnS0InLCAnTUInLCAnR0InLCAnVEInXTtcclxuICAgIGlmIChieXRlcyA9PT0gMCkge1xyXG4gICAgICAgIHJldHVybiAnMCBCeXRlcyc7XHJcbiAgICB9XHJcbiAgICB2YXIgaSA9IHBhcnNlSW50KE1hdGguZmxvb3IoTWF0aC5sb2coYnl0ZXMpIC8gTWF0aC5sb2coaykpLCAxMCk7XHJcbiAgICByZXR1cm4gKGJ5dGVzIC8gTWF0aC5wb3coaywgaSkpLnRvUHJlY2lzaW9uKDMpICsgJyAnICsgc2l6ZXNbaV07XHJcbn1cclxuXHJcbi8qKlxyXG4gKiBAcGFyYW0ge0Jsb2J9IGZpbGUgLSBGaWxlIG9yIEJsb2Igb2JqZWN0LiBUaGlzIHBhcmFtZXRlciBpcyByZXF1aXJlZC5cclxuICogQHBhcmFtIHtzdHJpbmd9IGZpbGVOYW1lIC0gT3B0aW9uYWwgZmlsZSBuYW1lIGUuZy4gXCJSZWNvcmRlZC1WaWRlby53ZWJtXCJcclxuICogQGV4YW1wbGVcclxuICogaW52b2tlU2F2ZUFzRGlhbG9nKGJsb2Igb3IgZmlsZSwgW29wdGlvbmFsXSBmaWxlTmFtZSk7XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICovXHJcbmZ1bmN0aW9uIGludm9rZVNhdmVBc0RpYWxvZyhmaWxlLCBmaWxlTmFtZSkge1xyXG4gICAgaWYgKCFmaWxlKSB7XHJcbiAgICAgICAgdGhyb3cgJ0Jsb2Igb2JqZWN0IGlzIHJlcXVpcmVkLic7XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKCFmaWxlLnR5cGUpIHtcclxuICAgICAgICB0cnkge1xyXG4gICAgICAgICAgICBmaWxlLnR5cGUgPSAndmlkZW8vd2VibSc7XHJcbiAgICAgICAgfSBjYXRjaCAoZSkge31cclxuICAgIH1cclxuXHJcbiAgICB2YXIgZmlsZUV4dGVuc2lvbiA9IChmaWxlLnR5cGUgfHwgJ3ZpZGVvL3dlYm0nKS5zcGxpdCgnLycpWzFdO1xyXG5cclxuICAgIGlmIChmaWxlTmFtZSAmJiBmaWxlTmFtZS5pbmRleE9mKCcuJykgIT09IC0xKSB7XHJcbiAgICAgICAgdmFyIHNwbGl0dGVkID0gZmlsZU5hbWUuc3BsaXQoJy4nKTtcclxuICAgICAgICBmaWxlTmFtZSA9IHNwbGl0dGVkWzBdO1xyXG4gICAgICAgIGZpbGVFeHRlbnNpb24gPSBzcGxpdHRlZFsxXTtcclxuICAgIH1cclxuXHJcbiAgICB2YXIgZmlsZUZ1bGxOYW1lID0gKGZpbGVOYW1lIHx8IChNYXRoLnJvdW5kKE1hdGgucmFuZG9tKCkgKiA5OTk5OTk5OTk5KSArIDg4ODg4ODg4OCkpICsgJy4nICsgZmlsZUV4dGVuc2lvbjtcclxuXHJcbiAgICBpZiAodHlwZW9mIG5hdmlnYXRvci5tc1NhdmVPck9wZW5CbG9iICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIHJldHVybiBuYXZpZ2F0b3IubXNTYXZlT3JPcGVuQmxvYihmaWxlLCBmaWxlRnVsbE5hbWUpO1xyXG4gICAgfSBlbHNlIGlmICh0eXBlb2YgbmF2aWdhdG9yLm1zU2F2ZUJsb2IgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgcmV0dXJuIG5hdmlnYXRvci5tc1NhdmVCbG9iKGZpbGUsIGZpbGVGdWxsTmFtZSk7XHJcbiAgICB9XHJcblxyXG4gICAgdmFyIGh5cGVybGluayA9IGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoJ2EnKTtcclxuICAgIGh5cGVybGluay5ocmVmID0gVVJMLmNyZWF0ZU9iamVjdFVSTChmaWxlKTtcclxuICAgIGh5cGVybGluay5kb3dubG9hZCA9IGZpbGVGdWxsTmFtZTtcclxuXHJcbiAgICBoeXBlcmxpbmsuc3R5bGUgPSAnZGlzcGxheTpub25lO29wYWNpdHk6MDtjb2xvcjp0cmFuc3BhcmVudDsnO1xyXG4gICAgKGRvY3VtZW50LmJvZHkgfHwgZG9jdW1lbnQuZG9jdW1lbnRFbGVtZW50KS5hcHBlbmRDaGlsZChoeXBlcmxpbmspO1xyXG5cclxuICAgIGlmICh0eXBlb2YgaHlwZXJsaW5rLmNsaWNrID09PSAnZnVuY3Rpb24nKSB7XHJcbiAgICAgICAgaHlwZXJsaW5rLmNsaWNrKCk7XHJcbiAgICB9IGVsc2Uge1xyXG4gICAgICAgIGh5cGVybGluay50YXJnZXQgPSAnX2JsYW5rJztcclxuICAgICAgICBoeXBlcmxpbmsuZGlzcGF0Y2hFdmVudChuZXcgTW91c2VFdmVudCgnY2xpY2snLCB7XHJcbiAgICAgICAgICAgIHZpZXc6IHdpbmRvdyxcclxuICAgICAgICAgICAgYnViYmxlczogdHJ1ZSxcclxuICAgICAgICAgICAgY2FuY2VsYWJsZTogdHJ1ZVxyXG4gICAgICAgIH0pKTtcclxuICAgIH1cclxuXHJcbiAgICBVUkwucmV2b2tlT2JqZWN0VVJMKGh5cGVybGluay5ocmVmKTtcclxufVxyXG5cclxuLyoqXHJcbiAqIGZyb206IGh0dHBzOi8vZ2l0aHViLmNvbS9jaGV0b24vaXMtZWxlY3Ryb24vYmxvYi9tYXN0ZXIvaW5kZXguanNcclxuICoqL1xyXG5mdW5jdGlvbiBpc0VsZWN0cm9uKCkge1xyXG4gICAgLy8gUmVuZGVyZXIgcHJvY2Vzc1xyXG4gICAgaWYgKHR5cGVvZiB3aW5kb3cgIT09ICd1bmRlZmluZWQnICYmIHR5cGVvZiB3aW5kb3cucHJvY2VzcyA9PT0gJ29iamVjdCcgJiYgd2luZG93LnByb2Nlc3MudHlwZSA9PT0gJ3JlbmRlcmVyJykge1xyXG4gICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgfVxyXG5cclxuICAgIC8vIE1haW4gcHJvY2Vzc1xyXG4gICAgaWYgKHR5cGVvZiBwcm9jZXNzICE9PSAndW5kZWZpbmVkJyAmJiB0eXBlb2YgcHJvY2Vzcy52ZXJzaW9ucyA9PT0gJ29iamVjdCcgJiYgISFwcm9jZXNzLnZlcnNpb25zLmVsZWN0cm9uKSB7XHJcbiAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICB9XHJcblxyXG4gICAgLy8gRGV0ZWN0IHRoZSB1c2VyIGFnZW50IHdoZW4gdGhlIGBub2RlSW50ZWdyYXRpb25gIG9wdGlvbiBpcyBzZXQgdG8gdHJ1ZVxyXG4gICAgaWYgKHR5cGVvZiBuYXZpZ2F0b3IgPT09ICdvYmplY3QnICYmIHR5cGVvZiBuYXZpZ2F0b3IudXNlckFnZW50ID09PSAnc3RyaW5nJyAmJiBuYXZpZ2F0b3IudXNlckFnZW50LmluZGV4T2YoJ0VsZWN0cm9uJykgPj0gMCkge1xyXG4gICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgfVxyXG5cclxuICAgIHJldHVybiBmYWxzZTtcclxufVxyXG5cclxuZnVuY3Rpb24gZ2V0VHJhY2tzKHN0cmVhbSwga2luZCkge1xyXG4gICAgaWYgKCFzdHJlYW0gfHwgIXN0cmVhbS5nZXRUcmFja3MpIHtcclxuICAgICAgICByZXR1cm4gW107XHJcbiAgICB9XHJcblxyXG4gICAgcmV0dXJuIHN0cmVhbS5nZXRUcmFja3MoKS5maWx0ZXIoZnVuY3Rpb24odCkge1xyXG4gICAgICAgIHJldHVybiB0LmtpbmQgPT09IChraW5kIHx8ICdhdWRpbycpO1xyXG4gICAgfSk7XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHNldFNyY09iamVjdChzdHJlYW0sIGVsZW1lbnQpIHtcclxuICAgIGlmICgnc3JjT2JqZWN0JyBpbiBlbGVtZW50KSB7XHJcbiAgICAgICAgZWxlbWVudC5zcmNPYmplY3QgPSBzdHJlYW07XHJcbiAgICB9IGVsc2UgaWYgKCdtb3pTcmNPYmplY3QnIGluIGVsZW1lbnQpIHtcclxuICAgICAgICBlbGVtZW50Lm1velNyY09iamVjdCA9IHN0cmVhbTtcclxuICAgIH0gZWxzZSB7XHJcbiAgICAgICAgZWxlbWVudC5zcmNPYmplY3QgPSBzdHJlYW07XHJcbiAgICB9XHJcbn1cclxuXHJcbi8qKlxyXG4gKiBAcGFyYW0ge0Jsb2J9IGZpbGUgLSBGaWxlIG9yIEJsb2Igb2JqZWN0LlxyXG4gKiBAcGFyYW0ge2Z1bmN0aW9ufSBjYWxsYmFjayAtIENhbGxiYWNrIGZ1bmN0aW9uLlxyXG4gKiBAZXhhbXBsZVxyXG4gKiBnZXRTZWVrYWJsZUJsb2IoYmxvYiBvciBmaWxlLCBjYWxsYmFjayk7XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICovXHJcbmZ1bmN0aW9uIGdldFNlZWthYmxlQmxvYihpbnB1dEJsb2IsIGNhbGxiYWNrKSB7XHJcbiAgICAvLyBFQk1MLmpzIGNvcHlyaWdodHMgZ29lcyB0bzogaHR0cHM6Ly9naXRodWIuY29tL2xlZ29raWNoaS90cy1lYm1sXHJcbiAgICBpZiAodHlwZW9mIEVCTUwgPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgdGhyb3cgbmV3IEVycm9yKCdQbGVhc2UgbGluazogaHR0cHM6Ly93d3cud2VicnRjLWV4cGVyaW1lbnQuY29tL0VCTUwuanMnKTtcclxuICAgIH1cclxuXHJcbiAgICB2YXIgcmVhZGVyID0gbmV3IEVCTUwuUmVhZGVyKCk7XHJcbiAgICB2YXIgZGVjb2RlciA9IG5ldyBFQk1MLkRlY29kZXIoKTtcclxuICAgIHZhciB0b29scyA9IEVCTUwudG9vbHM7XHJcblxyXG4gICAgdmFyIGZpbGVSZWFkZXIgPSBuZXcgRmlsZVJlYWRlcigpO1xyXG4gICAgZmlsZVJlYWRlci5vbmxvYWQgPSBmdW5jdGlvbihlKSB7XHJcbiAgICAgICAgdmFyIGVibWxFbG1zID0gZGVjb2Rlci5kZWNvZGUodGhpcy5yZXN1bHQpO1xyXG4gICAgICAgIGVibWxFbG1zLmZvckVhY2goZnVuY3Rpb24oZWxlbWVudCkge1xyXG4gICAgICAgICAgICByZWFkZXIucmVhZChlbGVtZW50KTtcclxuICAgICAgICB9KTtcclxuICAgICAgICByZWFkZXIuc3RvcCgpO1xyXG4gICAgICAgIHZhciByZWZpbmVkTWV0YWRhdGFCdWYgPSB0b29scy5tYWtlTWV0YWRhdGFTZWVrYWJsZShyZWFkZXIubWV0YWRhdGFzLCByZWFkZXIuZHVyYXRpb24sIHJlYWRlci5jdWVzKTtcclxuICAgICAgICB2YXIgYm9keSA9IHRoaXMucmVzdWx0LnNsaWNlKHJlYWRlci5tZXRhZGF0YVNpemUpO1xyXG4gICAgICAgIHZhciBuZXdCbG9iID0gbmV3IEJsb2IoW3JlZmluZWRNZXRhZGF0YUJ1ZiwgYm9keV0sIHtcclxuICAgICAgICAgICAgdHlwZTogJ3ZpZGVvL3dlYm0nXHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIGNhbGxiYWNrKG5ld0Jsb2IpO1xyXG4gICAgfTtcclxuICAgIGZpbGVSZWFkZXIucmVhZEFzQXJyYXlCdWZmZXIoaW5wdXRCbG9iKTtcclxufVxyXG5cclxuaWYgKHR5cGVvZiBSZWNvcmRSVEMgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICBSZWNvcmRSVEMuaW52b2tlU2F2ZUFzRGlhbG9nID0gaW52b2tlU2F2ZUFzRGlhbG9nO1xyXG4gICAgUmVjb3JkUlRDLmdldFRyYWNrcyA9IGdldFRyYWNrcztcclxuICAgIFJlY29yZFJUQy5nZXRTZWVrYWJsZUJsb2IgPSBnZXRTZWVrYWJsZUJsb2I7XHJcbiAgICBSZWNvcmRSVEMuYnl0ZXNUb1NpemUgPSBieXRlc1RvU2l6ZTtcclxuICAgIFJlY29yZFJUQy5pc0VsZWN0cm9uID0gaXNFbGVjdHJvbjtcclxufVxuXHJcbi8vIF9fX19fX19fX18gKHVzZWQgdG8gaGFuZGxlIHN0dWZmIGxpa2UgaHR0cDovL2dvby5nbC94bUU1ZWcpIGlzc3VlICMxMjlcclxuLy8gU3RvcmFnZS5qc1xyXG5cclxuLyoqXHJcbiAqIFN0b3JhZ2UgaXMgYSBzdGFuZGFsb25lIG9iamVjdCB1c2VkIGJ5IHtAbGluayBSZWNvcmRSVEN9IHRvIHN0b3JlIHJldXNhYmxlIG9iamVjdHMgZS5nLiBcIm5ldyBBdWRpb0NvbnRleHRcIi5cclxuICogQGxpY2Vuc2Uge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDL2Jsb2IvbWFzdGVyL0xJQ0VOU0V8TUlUfVxyXG4gKiBAYXV0aG9yIHtAbGluayBodHRwczovL011YXpLaGFuLmNvbXxNdWF6IEtoYW59XHJcbiAqIEBleGFtcGxlXHJcbiAqIFN0b3JhZ2UuQXVkaW9Db250ZXh0ID09PSB3ZWJraXRBdWRpb0NvbnRleHRcclxuICogQHByb3BlcnR5IHt3ZWJraXRBdWRpb0NvbnRleHR9IEF1ZGlvQ29udGV4dCAtIEtlZXBzIGEgcmVmZXJlbmNlIHRvIEF1ZGlvQ29udGV4dCBvYmplY3QuXHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICovXHJcblxyXG52YXIgU3RvcmFnZSA9IHt9O1xyXG5cclxuaWYgKHR5cGVvZiBBdWRpb0NvbnRleHQgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICBTdG9yYWdlLkF1ZGlvQ29udGV4dCA9IEF1ZGlvQ29udGV4dDtcclxufSBlbHNlIGlmICh0eXBlb2Ygd2Via2l0QXVkaW9Db250ZXh0ICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgU3RvcmFnZS5BdWRpb0NvbnRleHQgPSB3ZWJraXRBdWRpb0NvbnRleHQ7XHJcbn1cclxuXHJcbmlmICh0eXBlb2YgUmVjb3JkUlRDICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgUmVjb3JkUlRDLlN0b3JhZ2UgPSBTdG9yYWdlO1xyXG59XG5cclxuZnVuY3Rpb24gaXNNZWRpYVJlY29yZGVyQ29tcGF0aWJsZSgpIHtcclxuICAgIGlmIChpc0ZpcmVmb3ggfHwgaXNTYWZhcmkgfHwgaXNFZGdlKSB7XHJcbiAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICB9XHJcblxyXG4gICAgdmFyIG5WZXIgPSBuYXZpZ2F0b3IuYXBwVmVyc2lvbjtcclxuICAgIHZhciBuQWd0ID0gbmF2aWdhdG9yLnVzZXJBZ2VudDtcclxuICAgIHZhciBmdWxsVmVyc2lvbiA9ICcnICsgcGFyc2VGbG9hdChuYXZpZ2F0b3IuYXBwVmVyc2lvbik7XHJcbiAgICB2YXIgbWFqb3JWZXJzaW9uID0gcGFyc2VJbnQobmF2aWdhdG9yLmFwcFZlcnNpb24sIDEwKTtcclxuICAgIHZhciBuYW1lT2Zmc2V0LCB2ZXJPZmZzZXQsIGl4O1xyXG5cclxuICAgIGlmIChpc0Nocm9tZSB8fCBpc09wZXJhKSB7XHJcbiAgICAgICAgdmVyT2Zmc2V0ID0gbkFndC5pbmRleE9mKCdDaHJvbWUnKTtcclxuICAgICAgICBmdWxsVmVyc2lvbiA9IG5BZ3Quc3Vic3RyaW5nKHZlck9mZnNldCArIDcpO1xyXG4gICAgfVxyXG5cclxuICAgIC8vIHRyaW0gdGhlIGZ1bGxWZXJzaW9uIHN0cmluZyBhdCBzZW1pY29sb24vc3BhY2UgaWYgcHJlc2VudFxyXG4gICAgaWYgKChpeCA9IGZ1bGxWZXJzaW9uLmluZGV4T2YoJzsnKSkgIT09IC0xKSB7XHJcbiAgICAgICAgZnVsbFZlcnNpb24gPSBmdWxsVmVyc2lvbi5zdWJzdHJpbmcoMCwgaXgpO1xyXG4gICAgfVxyXG5cclxuICAgIGlmICgoaXggPSBmdWxsVmVyc2lvbi5pbmRleE9mKCcgJykpICE9PSAtMSkge1xyXG4gICAgICAgIGZ1bGxWZXJzaW9uID0gZnVsbFZlcnNpb24uc3Vic3RyaW5nKDAsIGl4KTtcclxuICAgIH1cclxuXHJcbiAgICBtYWpvclZlcnNpb24gPSBwYXJzZUludCgnJyArIGZ1bGxWZXJzaW9uLCAxMCk7XHJcblxyXG4gICAgaWYgKGlzTmFOKG1ham9yVmVyc2lvbikpIHtcclxuICAgICAgICBmdWxsVmVyc2lvbiA9ICcnICsgcGFyc2VGbG9hdChuYXZpZ2F0b3IuYXBwVmVyc2lvbik7XHJcbiAgICAgICAgbWFqb3JWZXJzaW9uID0gcGFyc2VJbnQobmF2aWdhdG9yLmFwcFZlcnNpb24sIDEwKTtcclxuICAgIH1cclxuXHJcbiAgICByZXR1cm4gbWFqb3JWZXJzaW9uID49IDQ5O1xyXG59XG5cclxuLy8gX19fX19fX19fX19fX19fX19fX19fX1xyXG4vLyBNZWRpYVN0cmVhbVJlY29yZGVyLmpzXHJcblxyXG4vKipcclxuICogTWVkaWFTdHJlYW1SZWNvcmRlciBpcyBhbiBhYnN0cmFjdGlvbiBsYXllciBmb3Ige0BsaW5rIGh0dHBzOi8vdzNjLmdpdGh1Yi5pby9tZWRpYWNhcHR1cmUtcmVjb3JkL01lZGlhUmVjb3JkZXIuaHRtbHxNZWRpYVJlY29yZGVyIEFQSX0uIEl0IGlzIHVzZWQgYnkge0BsaW5rIFJlY29yZFJUQ30gdG8gcmVjb3JkIE1lZGlhU3RyZWFtKHMpIGluIGJvdGggQ2hyb21lIGFuZCBGaXJlZm94LlxyXG4gKiBAc3VtbWFyeSBSdW5zIHRvcCBvdmVyIHtAbGluayBodHRwczovL3czYy5naXRodWIuaW8vbWVkaWFjYXB0dXJlLXJlY29yZC9NZWRpYVJlY29yZGVyLmh0bWx8TWVkaWFSZWNvcmRlciBBUEl9LlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW58TXVheiBLaGFufVxyXG4gKiBAdHlwZWRlZiBNZWRpYVN0cmVhbVJlY29yZGVyXHJcbiAqIEBjbGFzc1xyXG4gKiBAZXhhbXBsZVxyXG4gKiB2YXIgY29uZmlnID0ge1xyXG4gKiAgICAgbWltZVR5cGU6ICd2aWRlby93ZWJtJywgLy8gdnA4LCB2cDksIGgyNjQsIG1rdiwgb3B1cy92b3JiaXNcclxuICogICAgIGF1ZGlvQml0c1BlclNlY29uZCA6IDI1NiAqIDggKiAxMDI0LFxyXG4gKiAgICAgdmlkZW9CaXRzUGVyU2Vjb25kIDogMjU2ICogOCAqIDEwMjQsXHJcbiAqICAgICBiaXRzUGVyU2Vjb25kOiAyNTYgKiA4ICogMTAyNCwgIC8vIGlmIHRoaXMgaXMgcHJvdmlkZWQsIHNraXAgYWJvdmUgdHdvXHJcbiAqICAgICBjaGVja0ZvckluYWN0aXZlVHJhY2tzOiB0cnVlLFxyXG4gKiAgICAgdGltZVNsaWNlOiAxMDAwLCAvLyBjb25jYXRlbmF0ZSBpbnRlcnZhbHMgYmFzZWQgYmxvYnNcclxuICogICAgIG9uZGF0YWF2YWlsYWJsZTogZnVuY3Rpb24oKSB7fSAvLyBnZXQgaW50ZXJ2YWxzIGJhc2VkIGJsb2JzXHJcbiAqIH1cclxuICogdmFyIHJlY29yZGVyID0gbmV3IE1lZGlhU3RyZWFtUmVjb3JkZXIobWVkaWFTdHJlYW0sIGNvbmZpZyk7XHJcbiAqIHJlY29yZGVyLnJlY29yZCgpO1xyXG4gKiByZWNvcmRlci5zdG9wKGZ1bmN0aW9uKGJsb2IpIHtcclxuICogICAgIHZpZGVvLnNyYyA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAqXHJcbiAqICAgICAvLyBvclxyXG4gKiAgICAgdmFyIGJsb2IgPSByZWNvcmRlci5ibG9iO1xyXG4gKiB9KTtcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKiBAcGFyYW0ge01lZGlhU3RyZWFtfSBtZWRpYVN0cmVhbSAtIE1lZGlhU3RyZWFtIG9iamVjdCBmZXRjaGVkIHVzaW5nIGdldFVzZXJNZWRpYSBBUEkgb3IgZ2VuZXJhdGVkIHVzaW5nIGNhcHR1cmVTdHJlYW1VbnRpbEVuZGVkIG9yIFdlYkF1ZGlvIEFQSS5cclxuICogQHBhcmFtIHtvYmplY3R9IGNvbmZpZyAtIHtkaXNhYmxlTG9nczp0cnVlLCBpbml0Q2FsbGJhY2s6IGZ1bmN0aW9uLCBtaW1lVHlwZTogXCJ2aWRlby93ZWJtXCIsIHRpbWVTbGljZTogMTAwMH1cclxuICogQHRocm93cyBXaWxsIHRocm93IGFuIGVycm9yIGlmIGZpcnN0IGFyZ3VtZW50IFwiTWVkaWFTdHJlYW1cIiBpcyBtaXNzaW5nLiBBbHNvIHRocm93cyBlcnJvciBpZiBcIk1lZGlhUmVjb3JkZXIgQVBJXCIgYXJlIG5vdCBzdXBwb3J0ZWQgYnkgdGhlIGJyb3dzZXIuXHJcbiAqL1xyXG5cclxuZnVuY3Rpb24gTWVkaWFTdHJlYW1SZWNvcmRlcihtZWRpYVN0cmVhbSwgY29uZmlnKSB7XHJcbiAgICB2YXIgc2VsZiA9IHRoaXM7XHJcblxyXG4gICAgaWYgKHR5cGVvZiBtZWRpYVN0cmVhbSA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICB0aHJvdyAnRmlyc3QgYXJndW1lbnQgXCJNZWRpYVN0cmVhbVwiIGlzIHJlcXVpcmVkLic7XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKHR5cGVvZiBNZWRpYVJlY29yZGVyID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIHRocm93ICdZb3VyIGJyb3dzZXIgZG9lcyBub3Qgc3VwcG9ydCB0aGUgTWVkaWEgUmVjb3JkZXIgQVBJLiBQbGVhc2UgdHJ5IG90aGVyIG1vZHVsZXMgZS5nLiBXaGFtbXlSZWNvcmRlciBvciBTdGVyZW9BdWRpb1JlY29yZGVyLic7XHJcbiAgICB9XHJcblxyXG4gICAgY29uZmlnID0gY29uZmlnIHx8IHtcclxuICAgICAgICAvLyBiaXRzUGVyU2Vjb25kOiAyNTYgKiA4ICogMTAyNCxcclxuICAgICAgICBtaW1lVHlwZTogJ3ZpZGVvL3dlYm0nXHJcbiAgICB9O1xyXG5cclxuICAgIGlmIChjb25maWcudHlwZSA9PT0gJ2F1ZGlvJykge1xyXG4gICAgICAgIGlmIChnZXRUcmFja3MobWVkaWFTdHJlYW0sICd2aWRlbycpLmxlbmd0aCAmJiBnZXRUcmFja3MobWVkaWFTdHJlYW0sICdhdWRpbycpLmxlbmd0aCkge1xyXG4gICAgICAgICAgICB2YXIgc3RyZWFtO1xyXG4gICAgICAgICAgICBpZiAoISFuYXZpZ2F0b3IubW96R2V0VXNlck1lZGlhKSB7XHJcbiAgICAgICAgICAgICAgICBzdHJlYW0gPSBuZXcgTWVkaWFTdHJlYW0oKTtcclxuICAgICAgICAgICAgICAgIHN0cmVhbS5hZGRUcmFjayhnZXRUcmFja3MobWVkaWFTdHJlYW0sICdhdWRpbycpWzBdKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIC8vIHdlYmtpdE1lZGlhU3RyZWFtXHJcbiAgICAgICAgICAgICAgICBzdHJlYW0gPSBuZXcgTWVkaWFTdHJlYW0oZ2V0VHJhY2tzKG1lZGlhU3RyZWFtLCAnYXVkaW8nKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgbWVkaWFTdHJlYW0gPSBzdHJlYW07XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoIWNvbmZpZy5taW1lVHlwZSB8fCBjb25maWcubWltZVR5cGUudG9TdHJpbmcoKS50b0xvd2VyQ2FzZSgpLmluZGV4T2YoJ2F1ZGlvJykgPT09IC0xKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy5taW1lVHlwZSA9IGlzQ2hyb21lID8gJ2F1ZGlvL3dlYm0nIDogJ2F1ZGlvL29nZyc7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoY29uZmlnLm1pbWVUeXBlICYmIGNvbmZpZy5taW1lVHlwZS50b1N0cmluZygpLnRvTG93ZXJDYXNlKCkgIT09ICdhdWRpby9vZ2cnICYmICEhbmF2aWdhdG9yLm1vekdldFVzZXJNZWRpYSkge1xyXG4gICAgICAgICAgICAvLyBmb3JjaW5nIGJldHRlciBjb2RlY3Mgb24gRmlyZWZveCAodmlhICMxNjYpXHJcbiAgICAgICAgICAgIGNvbmZpZy5taW1lVHlwZSA9ICdhdWRpby9vZ2cnO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICB2YXIgYXJyYXlPZkJsb2JzID0gW107XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXR1cm5zIGFycmF5IG9mIGJsb2JzLiBVc2Ugb25seSB3aXRoIFwidGltZVNsaWNlXCIuIEl0cyB1c2VmdWwgdG8gcHJldmlldyByZWNvcmRpbmcgYW55dGltZSwgd2l0aG91dCB1c2luZyB0aGUgXCJzdG9wXCIgbWV0aG9kLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1lZGlhU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiB2YXIgYXJyYXlPZkJsb2JzID0gcmVjb3JkZXIuZ2V0QXJyYXlPZkJsb2JzKCk7XHJcbiAgICAgKiBAcmV0dXJucyB7QXJyYXl9IFJldHVybnMgYXJyYXkgb2YgcmVjb3JkZWQgYmxvYnMuXHJcbiAgICAgKi9cclxuICAgIHRoaXMuZ2V0QXJyYXlPZkJsb2JzID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIGFycmF5T2ZCbG9icztcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZWNvcmRzIE1lZGlhU3RyZWFtLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1lZGlhU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5yZWNvcmQoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5yZWNvcmQgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAvLyBzZXQgZGVmYXVsdHNcclxuICAgICAgICBzZWxmLmJsb2IgPSBudWxsO1xyXG4gICAgICAgIHNlbGYuY2xlYXJSZWNvcmRlZERhdGEoKTtcclxuICAgICAgICBzZWxmLnRpbWVzdGFtcHMgPSBbXTtcclxuICAgICAgICBhbGxTdGF0ZXMgPSBbXTtcclxuICAgICAgICBhcnJheU9mQmxvYnMgPSBbXTtcclxuXHJcbiAgICAgICAgdmFyIHJlY29yZGVySGludHMgPSBjb25maWc7XHJcblxyXG4gICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCdQYXNzaW5nIGZvbGxvd2luZyBjb25maWcgb3ZlciBNZWRpYVJlY29yZGVyIEFQSS4nLCByZWNvcmRlckhpbnRzKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIC8vIG1hbmRhdG9yeSB0byBtYWtlIHN1cmUgRmlyZWZveCBkb2Vzbid0IGZhaWxzIHRvIHJlY29yZCBzdHJlYW1zIDMtNCB0aW1lcyB3aXRob3V0IHJlbG9hZGluZyB0aGUgcGFnZS5cclxuICAgICAgICAgICAgbWVkaWFSZWNvcmRlciA9IG51bGw7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoaXNDaHJvbWUgJiYgIWlzTWVkaWFSZWNvcmRlckNvbXBhdGlibGUoKSkge1xyXG4gICAgICAgICAgICAvLyB0byBzdXBwb3J0IHZpZGVvLW9ubHkgcmVjb3JkaW5nIG9uIHN0YWJsZVxyXG4gICAgICAgICAgICByZWNvcmRlckhpbnRzID0gJ3ZpZGVvL3ZwOCc7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAodHlwZW9mIE1lZGlhUmVjb3JkZXIuaXNUeXBlU3VwcG9ydGVkID09PSAnZnVuY3Rpb24nICYmIHJlY29yZGVySGludHMubWltZVR5cGUpIHtcclxuICAgICAgICAgICAgaWYgKCFNZWRpYVJlY29yZGVyLmlzVHlwZVN1cHBvcnRlZChyZWNvcmRlckhpbnRzLm1pbWVUeXBlKSkge1xyXG4gICAgICAgICAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zb2xlLndhcm4oJ01lZGlhUmVjb3JkZXIgQVBJIHNlZW1zIHVuYWJsZSB0byByZWNvcmQgbWltZVR5cGU6JywgcmVjb3JkZXJIaW50cy5taW1lVHlwZSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgcmVjb3JkZXJIaW50cy5taW1lVHlwZSA9IGNvbmZpZy50eXBlID09PSAnYXVkaW8nID8gJ2F1ZGlvL3dlYm0nIDogJ3ZpZGVvL3dlYm0nO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICAvLyB1c2luZyBNZWRpYVJlY29yZGVyIEFQSSBoZXJlXHJcbiAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgbWVkaWFSZWNvcmRlciA9IG5ldyBNZWRpYVJlY29yZGVyKG1lZGlhU3RyZWFtLCByZWNvcmRlckhpbnRzKTtcclxuXHJcbiAgICAgICAgICAgIC8vIHJlc2V0XHJcbiAgICAgICAgICAgIGNvbmZpZy5taW1lVHlwZSA9IHJlY29yZGVySGludHMubWltZVR5cGU7XHJcbiAgICAgICAgfSBjYXRjaCAoZSkge1xyXG4gICAgICAgICAgICAvLyBjaHJvbWUtYmFzZWQgZmFsbGJhY2tcclxuICAgICAgICAgICAgbWVkaWFSZWNvcmRlciA9IG5ldyBNZWRpYVJlY29yZGVyKG1lZGlhU3RyZWFtKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIC8vIG9sZCBoYWNrP1xyXG4gICAgICAgIGlmIChyZWNvcmRlckhpbnRzLm1pbWVUeXBlICYmICFNZWRpYVJlY29yZGVyLmlzVHlwZVN1cHBvcnRlZCAmJiAnY2FuUmVjb3JkTWltZVR5cGUnIGluIG1lZGlhUmVjb3JkZXIgJiYgbWVkaWFSZWNvcmRlci5jYW5SZWNvcmRNaW1lVHlwZShyZWNvcmRlckhpbnRzLm1pbWVUeXBlKSA9PT0gZmFsc2UpIHtcclxuICAgICAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUud2FybignTWVkaWFSZWNvcmRlciBBUEkgc2VlbXMgdW5hYmxlIHRvIHJlY29yZCBtaW1lVHlwZTonLCByZWNvcmRlckhpbnRzLm1pbWVUeXBlKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgLy8gRGlzcGF0Y2hpbmcgT25EYXRhQXZhaWxhYmxlIEhhbmRsZXJcclxuICAgICAgICBtZWRpYVJlY29yZGVyLm9uZGF0YWF2YWlsYWJsZSA9IGZ1bmN0aW9uKGUpIHtcclxuICAgICAgICAgICAgaWYgKGUuZGF0YSkge1xyXG4gICAgICAgICAgICAgICAgYWxsU3RhdGVzLnB1c2goJ29uZGF0YWF2YWlsYWJsZTogJyArIGJ5dGVzVG9TaXplKGUuZGF0YS5zaXplKSk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmICh0eXBlb2YgY29uZmlnLnRpbWVTbGljZSA9PT0gJ251bWJlcicpIHtcclxuICAgICAgICAgICAgICAgIGlmIChlLmRhdGEgJiYgZS5kYXRhLnNpemUgJiYgZS5kYXRhLnNpemUgPiAxMDApIHtcclxuICAgICAgICAgICAgICAgICAgICBhcnJheU9mQmxvYnMucHVzaChlLmRhdGEpO1xyXG4gICAgICAgICAgICAgICAgICAgIHVwZGF0ZVRpbWVTdGFtcCgpO1xyXG5cclxuICAgICAgICAgICAgICAgICAgICBpZiAodHlwZW9mIGNvbmZpZy5vbmRhdGFhdmFpbGFibGUgPT09ICdmdW5jdGlvbicpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgLy8gaW50ZXJ2YWxzIGJhc2VkIGJsb2JzXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHZhciBibG9iID0gY29uZmlnLmdldE5hdGl2ZUJsb2IgPyBlLmRhdGEgOiBuZXcgQmxvYihbZS5kYXRhXSwge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdHlwZTogZ2V0TWltZVR5cGUocmVjb3JkZXJIaW50cylcclxuICAgICAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGNvbmZpZy5vbmRhdGFhdmFpbGFibGUoYmxvYik7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoIWUuZGF0YSB8fCAhZS5kYXRhLnNpemUgfHwgZS5kYXRhLnNpemUgPCAxMDAgfHwgc2VsZi5ibG9iKSB7XHJcbiAgICAgICAgICAgICAgICAvLyBtYWtlIHN1cmUgdGhhdCBzdG9wUmVjb3JkaW5nIGFsd2F5cyBnZXR0aW5nIGZpcmVkXHJcbiAgICAgICAgICAgICAgICAvLyBldmVuIGlmIHRoZXJlIGlzIGludmFsaWQgZGF0YVxyXG4gICAgICAgICAgICAgICAgaWYgKHNlbGYucmVjb3JkaW5nQ2FsbGJhY2spIHtcclxuICAgICAgICAgICAgICAgICAgICBzZWxmLnJlY29yZGluZ0NhbGxiYWNrKG5ldyBCbG9iKFtdLCB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHR5cGU6IGdldE1pbWVUeXBlKHJlY29yZGVySGludHMpXHJcbiAgICAgICAgICAgICAgICAgICAgfSkpO1xyXG4gICAgICAgICAgICAgICAgICAgIHNlbGYucmVjb3JkaW5nQ2FsbGJhY2sgPSBudWxsO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBzZWxmLmJsb2IgPSBjb25maWcuZ2V0TmF0aXZlQmxvYiA/IGUuZGF0YSA6IG5ldyBCbG9iKFtlLmRhdGFdLCB7XHJcbiAgICAgICAgICAgICAgICB0eXBlOiBnZXRNaW1lVHlwZShyZWNvcmRlckhpbnRzKVxyXG4gICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgICAgIGlmIChzZWxmLnJlY29yZGluZ0NhbGxiYWNrKSB7XHJcbiAgICAgICAgICAgICAgICBzZWxmLnJlY29yZGluZ0NhbGxiYWNrKHNlbGYuYmxvYik7XHJcbiAgICAgICAgICAgICAgICBzZWxmLnJlY29yZGluZ0NhbGxiYWNrID0gbnVsbDtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIG1lZGlhUmVjb3JkZXIub25zdGFydCA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICBhbGxTdGF0ZXMucHVzaCgnc3RhcnRlZCcpO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIG1lZGlhUmVjb3JkZXIub25wYXVzZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICBhbGxTdGF0ZXMucHVzaCgncGF1c2VkJyk7XHJcbiAgICAgICAgfTtcclxuXHJcbiAgICAgICAgbWVkaWFSZWNvcmRlci5vbnJlc3VtZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICBhbGxTdGF0ZXMucHVzaCgncmVzdW1lZCcpO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIG1lZGlhUmVjb3JkZXIub25zdG9wID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIGFsbFN0YXRlcy5wdXNoKCdzdG9wcGVkJyk7XHJcbiAgICAgICAgfTtcclxuXHJcbiAgICAgICAgbWVkaWFSZWNvcmRlci5vbmVycm9yID0gZnVuY3Rpb24oZXJyb3IpIHtcclxuICAgICAgICAgICAgaWYgKCFlcnJvcikge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoIWVycm9yLm5hbWUpIHtcclxuICAgICAgICAgICAgICAgIGVycm9yLm5hbWUgPSAnVW5rbm93bkVycm9yJztcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgYWxsU3RhdGVzLnB1c2goJ2Vycm9yOiAnICsgZXJyb3IpO1xyXG5cclxuICAgICAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgIC8vIHZpYTogaHR0cHM6Ly93M2MuZ2l0aHViLmlvL21lZGlhY2FwdHVyZS1yZWNvcmQvTWVkaWFSZWNvcmRlci5odG1sI2V4Y2VwdGlvbi1zdW1tYXJ5XHJcbiAgICAgICAgICAgICAgICBpZiAoZXJyb3IubmFtZS50b1N0cmluZygpLnRvTG93ZXJDYXNlKCkuaW5kZXhPZignaW52YWxpZHN0YXRlJykgIT09IC0xKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc29sZS5lcnJvcignVGhlIE1lZGlhUmVjb3JkZXIgaXMgbm90IGluIGEgc3RhdGUgaW4gd2hpY2ggdGhlIHByb3Bvc2VkIG9wZXJhdGlvbiBpcyBhbGxvd2VkIHRvIGJlIGV4ZWN1dGVkLicsIGVycm9yKTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSBpZiAoZXJyb3IubmFtZS50b1N0cmluZygpLnRvTG93ZXJDYXNlKCkuaW5kZXhPZignbm90c3VwcG9ydGVkJykgIT09IC0xKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc29sZS5lcnJvcignTUlNRSB0eXBlICgnLCByZWNvcmRlckhpbnRzLm1pbWVUeXBlLCAnKSBpcyBub3Qgc3VwcG9ydGVkLicsIGVycm9yKTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSBpZiAoZXJyb3IubmFtZS50b1N0cmluZygpLnRvTG93ZXJDYXNlKCkuaW5kZXhPZignc2VjdXJpdHknKSAhPT0gLTEpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zb2xlLmVycm9yKCdNZWRpYVJlY29yZGVyIHNlY3VyaXR5IGVycm9yJywgZXJyb3IpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIC8vIG9sZGVyIGNvZGUgYmVsb3dcclxuICAgICAgICAgICAgICAgIGVsc2UgaWYgKGVycm9yLm5hbWUgPT09ICdPdXRPZk1lbW9yeScpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zb2xlLmVycm9yKCdUaGUgVUEgaGFzIGV4aGF1c2VkIHRoZSBhdmFpbGFibGUgbWVtb3J5LiBVc2VyIGFnZW50cyBTSE9VTEQgcHJvdmlkZSBhcyBtdWNoIGFkZGl0aW9uYWwgaW5mb3JtYXRpb24gYXMgcG9zc2libGUgaW4gdGhlIG1lc3NhZ2UgYXR0cmlidXRlLicsIGVycm9yKTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSBpZiAoZXJyb3IubmFtZSA9PT0gJ0lsbGVnYWxTdHJlYW1Nb2RpZmljYXRpb24nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc29sZS5lcnJvcignQSBtb2RpZmljYXRpb24gdG8gdGhlIHN0cmVhbSBoYXMgb2NjdXJyZWQgdGhhdCBtYWtlcyBpdCBpbXBvc3NpYmxlIHRvIGNvbnRpbnVlIHJlY29yZGluZy4gQW4gZXhhbXBsZSB3b3VsZCBiZSB0aGUgYWRkaXRpb24gb2YgYSBUcmFjayB3aGlsZSByZWNvcmRpbmcgaXMgb2NjdXJyaW5nLiBVc2VyIGFnZW50cyBTSE9VTEQgcHJvdmlkZSBhcyBtdWNoIGFkZGl0aW9uYWwgaW5mb3JtYXRpb24gYXMgcG9zc2libGUgaW4gdGhlIG1lc3NhZ2UgYXR0cmlidXRlLicsIGVycm9yKTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSBpZiAoZXJyb3IubmFtZSA9PT0gJ090aGVyUmVjb3JkaW5nRXJyb3InKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc29sZS5lcnJvcignVXNlZCBmb3IgYW4gZmF0YWwgZXJyb3Igb3RoZXIgdGhhbiB0aG9zZSBsaXN0ZWQgYWJvdmUuIFVzZXIgYWdlbnRzIFNIT1VMRCBwcm92aWRlIGFzIG11Y2ggYWRkaXRpb25hbCBpbmZvcm1hdGlvbiBhcyBwb3NzaWJsZSBpbiB0aGUgbWVzc2FnZSBhdHRyaWJ1dGUuJywgZXJyb3IpO1xyXG4gICAgICAgICAgICAgICAgfSBlbHNlIGlmIChlcnJvci5uYW1lID09PSAnR2VuZXJpY0Vycm9yJykge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnNvbGUuZXJyb3IoJ1RoZSBVQSBjYW5ub3QgcHJvdmlkZSB0aGUgY29kZWMgb3IgcmVjb3JkaW5nIG9wdGlvbiB0aGF0IGhhcyBiZWVuIHJlcXVlc3RlZC4nLCBlcnJvcik7XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnNvbGUuZXJyb3IoJ01lZGlhUmVjb3JkZXIgRXJyb3InLCBlcnJvcik7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIChmdW5jdGlvbihsb29wZXIpIHtcclxuICAgICAgICAgICAgICAgIGlmICghc2VsZi5tYW51YWxseVN0b3BwZWQgJiYgbWVkaWFSZWNvcmRlciAmJiBtZWRpYVJlY29yZGVyLnN0YXRlID09PSAnaW5hY3RpdmUnKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZGVsZXRlIGNvbmZpZy50aW1lc2xpY2U7XHJcblxyXG4gICAgICAgICAgICAgICAgICAgIC8vIDEwIG1pbnV0ZXMsIGVub3VnaD9cclxuICAgICAgICAgICAgICAgICAgICBtZWRpYVJlY29yZGVyLnN0YXJ0KDEwICogNjAgKiAxMDAwKTtcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgc2V0VGltZW91dChsb29wZXIsIDEwMDApO1xyXG4gICAgICAgICAgICB9KSgpO1xyXG5cclxuICAgICAgICAgICAgaWYgKG1lZGlhUmVjb3JkZXIuc3RhdGUgIT09ICdpbmFjdGl2ZScgJiYgbWVkaWFSZWNvcmRlci5zdGF0ZSAhPT0gJ3N0b3BwZWQnKSB7XHJcbiAgICAgICAgICAgICAgICBtZWRpYVJlY29yZGVyLnN0b3AoKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgY29uZmlnLnRpbWVTbGljZSA9PT0gJ251bWJlcicpIHtcclxuICAgICAgICAgICAgdXBkYXRlVGltZVN0YW1wKCk7XHJcbiAgICAgICAgICAgIG1lZGlhUmVjb3JkZXIuc3RhcnQoY29uZmlnLnRpbWVTbGljZSk7XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgLy8gZGVmYXVsdCBpcyA2MCBtaW51dGVzOyBlbm91Z2g/XHJcbiAgICAgICAgICAgIC8vIHVzZSBjb25maWcgPT4ge3RpbWVTbGljZTogMTAwMH0gb3RoZXJ3aXNlXHJcblxyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnN0YXJ0KDMuNmUrNik7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoY29uZmlnLmluaXRDYWxsYmFjaykge1xyXG4gICAgICAgICAgICBjb25maWcuaW5pdENhbGxiYWNrKCk7IC8vIG9sZCBjb2RlXHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIEBwcm9wZXJ0eSB7QXJyYXl9IHRpbWVzdGFtcHMgLSBBcnJheSBvZiB0aW1lIHN0YW1wc1xyXG4gICAgICogQG1lbWJlcm9mIE1lZGlhU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiBjb25zb2xlLmxvZyhyZWNvcmRlci50aW1lc3RhbXBzKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy50aW1lc3RhbXBzID0gW107XHJcblxyXG4gICAgZnVuY3Rpb24gdXBkYXRlVGltZVN0YW1wKCkge1xyXG4gICAgICAgIHNlbGYudGltZXN0YW1wcy5wdXNoKG5ldyBEYXRlKCkuZ2V0VGltZSgpKTtcclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiBjb25maWcub25UaW1lU3RhbXAgPT09ICdmdW5jdGlvbicpIHtcclxuICAgICAgICAgICAgY29uZmlnLm9uVGltZVN0YW1wKHNlbGYudGltZXN0YW1wc1tzZWxmLnRpbWVzdGFtcHMubGVuZ3RoIC0gMV0sIHNlbGYudGltZXN0YW1wcyk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIGdldE1pbWVUeXBlKHNlY29uZE9iamVjdCkge1xyXG4gICAgICAgIGlmIChtZWRpYVJlY29yZGVyICYmIG1lZGlhUmVjb3JkZXIubWltZVR5cGUpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1lZGlhUmVjb3JkZXIubWltZVR5cGU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICByZXR1cm4gc2Vjb25kT2JqZWN0Lm1pbWVUeXBlIHx8ICd2aWRlby93ZWJtJztcclxuICAgIH1cclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHN0b3BzIHJlY29yZGluZyBNZWRpYVN0cmVhbS5cclxuICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQ2FsbGJhY2sgZnVuY3Rpb24sIHRoYXQgaXMgdXNlZCB0byBwYXNzIHJlY29yZGVkIGJsb2IgYmFjayB0byB0aGUgY2FsbGVlLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1lZGlhU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5zdG9wKGZ1bmN0aW9uKGJsb2IpIHtcclxuICAgICAqICAgICB2aWRlby5zcmMgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKGJsb2IpO1xyXG4gICAgICogfSk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuc3RvcCA9IGZ1bmN0aW9uKGNhbGxiYWNrKSB7XHJcbiAgICAgICAgY2FsbGJhY2sgPSBjYWxsYmFjayB8fCBmdW5jdGlvbigpIHt9O1xyXG5cclxuICAgICAgICBzZWxmLm1hbnVhbGx5U3RvcHBlZCA9IHRydWU7IC8vIHVzZWQgaW5zaWRlIHRoZSBtZWRpYVJlY29yZGVyLm9uZXJyb3JcclxuXHJcbiAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHRoaXMucmVjb3JkaW5nQ2FsbGJhY2sgPSBjYWxsYmFjaztcclxuXHJcbiAgICAgICAgaWYgKG1lZGlhUmVjb3JkZXIuc3RhdGUgPT09ICdyZWNvcmRpbmcnKSB7XHJcbiAgICAgICAgICAgIG1lZGlhUmVjb3JkZXIuc3RvcCgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiBjb25maWcudGltZVNsaWNlID09PSAnbnVtYmVyJykge1xyXG4gICAgICAgICAgICBzZXRUaW1lb3V0KGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICAgICAgc2VsZi5ibG9iID0gbmV3IEJsb2IoYXJyYXlPZkJsb2JzLCB7XHJcbiAgICAgICAgICAgICAgICAgICAgdHlwZTogZ2V0TWltZVR5cGUoY29uZmlnKVxyXG4gICAgICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICAgICAgc2VsZi5yZWNvcmRpbmdDYWxsYmFjayhzZWxmLmJsb2IpO1xyXG4gICAgICAgICAgICB9LCAxMDApO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBwYXVzZXMgdGhlIHJlY29yZGluZyBwcm9jZXNzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1lZGlhU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5wYXVzZSgpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnBhdXNlID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChtZWRpYVJlY29yZGVyLnN0YXRlID09PSAncmVjb3JkaW5nJykge1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnBhdXNlKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJlc3VtZXMgdGhlIHJlY29yZGluZyBwcm9jZXNzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE1lZGlhU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5yZXN1bWUoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5yZXN1bWUgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpZiAoIW1lZGlhUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKG1lZGlhUmVjb3JkZXIuc3RhdGUgPT09ICdwYXVzZWQnKSB7XHJcbiAgICAgICAgICAgIG1lZGlhUmVjb3JkZXIucmVzdW1lKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJlc2V0cyBjdXJyZW50bHkgcmVjb3JkZWQgZGF0YS5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBNZWRpYVN0cmVhbVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuY2xlYXJSZWNvcmRlZERhdGEoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5jbGVhclJlY29yZGVkRGF0YSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlmIChtZWRpYVJlY29yZGVyICYmIG1lZGlhUmVjb3JkZXIuc3RhdGUgPT09ICdyZWNvcmRpbmcnKSB7XHJcbiAgICAgICAgICAgIHNlbGYuc3RvcChjbGVhclJlY29yZGVkRGF0YUNCKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGNsZWFyUmVjb3JkZWREYXRhQ0IoKTtcclxuICAgIH07XHJcblxyXG4gICAgZnVuY3Rpb24gY2xlYXJSZWNvcmRlZERhdGFDQigpIHtcclxuICAgICAgICBhcnJheU9mQmxvYnMgPSBbXTtcclxuICAgICAgICBtZWRpYVJlY29yZGVyID0gbnVsbDtcclxuICAgICAgICBzZWxmLnRpbWVzdGFtcHMgPSBbXTtcclxuICAgIH1cclxuXHJcbiAgICAvLyBSZWZlcmVuY2UgdG8gXCJNZWRpYVJlY29yZGVyXCIgb2JqZWN0XHJcbiAgICB2YXIgbWVkaWFSZWNvcmRlcjtcclxuXHJcbiAgICAvKipcclxuICAgICAqIEFjY2VzcyB0byBuYXRpdmUgTWVkaWFSZWNvcmRlciBBUElcclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBNZWRpYVN0cmVhbVJlY29yZGVyXHJcbiAgICAgKiBAaW5zdGFuY2VcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiB2YXIgaW50ZXJuYWwgPSByZWNvcmRlci5nZXRJbnRlcm5hbFJlY29yZGVyKCk7XHJcbiAgICAgKiBpbnRlcm5hbC5vbmRhdGFhdmFpbGFibGUgPSBmdW5jdGlvbigpIHt9OyAvLyBvdmVycmlkZVxyXG4gICAgICogaW50ZXJuYWwuc3RyZWFtLCBpbnRlcm5hbC5vbnBhdXNlLCBpbnRlcm5hbC5vbnN0b3AsIGV0Yy5cclxuICAgICAqIEByZXR1cm5zIHtPYmplY3R9IFJldHVybnMgaW50ZXJuYWwgcmVjb3JkaW5nIG9iamVjdC5cclxuICAgICAqL1xyXG4gICAgdGhpcy5nZXRJbnRlcm5hbFJlY29yZGVyID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIG1lZGlhUmVjb3JkZXI7XHJcbiAgICB9O1xyXG5cclxuICAgIGZ1bmN0aW9uIGlzTWVkaWFTdHJlYW1BY3RpdmUoKSB7XHJcbiAgICAgICAgaWYgKCdhY3RpdmUnIGluIG1lZGlhU3RyZWFtKSB7XHJcbiAgICAgICAgICAgIGlmICghbWVkaWFTdHJlYW0uYWN0aXZlKSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9IGVsc2UgaWYgKCdlbmRlZCcgaW4gbWVkaWFTdHJlYW0pIHsgLy8gb2xkIGhhY2tcclxuICAgICAgICAgICAgaWYgKG1lZGlhU3RyZWFtLmVuZGVkKSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICB9XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBAcHJvcGVydHkge0Jsb2J9IGJsb2IgLSBSZWNvcmRlZCBkYXRhIGFzIFwiQmxvYlwiIG9iamVjdC5cclxuICAgICAqIEBtZW1iZXJvZiBNZWRpYVN0cmVhbVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbigpIHtcclxuICAgICAqICAgICB2YXIgYmxvYiA9IHJlY29yZGVyLmJsb2I7XHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5ibG9iID0gbnVsbDtcclxuXHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBHZXQgTWVkaWFSZWNvcmRlciByZWFkb25seSBzdGF0ZS5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBNZWRpYVN0cmVhbVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogdmFyIHN0YXRlID0gcmVjb3JkZXIuZ2V0U3RhdGUoKTtcclxuICAgICAqIEByZXR1cm5zIHtTdHJpbmd9IFJldHVybnMgcmVjb3JkaW5nIHN0YXRlLlxyXG4gICAgICovXHJcbiAgICB0aGlzLmdldFN0YXRlID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaWYgKCFtZWRpYVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIHJldHVybiAnaW5hY3RpdmUnO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgcmV0dXJuIG1lZGlhUmVjb3JkZXIuc3RhdGUgfHwgJ2luYWN0aXZlJztcclxuICAgIH07XHJcblxyXG4gICAgLy8gbGlzdCBvZiBhbGwgcmVjb3JkaW5nIHN0YXRlc1xyXG4gICAgdmFyIGFsbFN0YXRlcyA9IFtdO1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogR2V0IE1lZGlhUmVjb3JkZXIgYWxsIHJlY29yZGluZyBzdGF0ZXMuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTWVkaWFTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHZhciBzdGF0ZSA9IHJlY29yZGVyLmdldEFsbFN0YXRlcygpO1xyXG4gICAgICogQHJldHVybnMge0FycmF5fSBSZXR1cm5zIGFsbCByZWNvcmRpbmcgc3RhdGVzXHJcbiAgICAgKi9cclxuICAgIHRoaXMuZ2V0QWxsU3RhdGVzID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIGFsbFN0YXRlcztcclxuICAgIH07XHJcblxyXG4gICAgLy8gaWYgYW55IFRyYWNrIHdpdGhpbiB0aGUgTWVkaWFTdHJlYW0gaXMgbXV0ZWQgb3Igbm90IGVuYWJsZWQgYXQgYW55IHRpbWUsIFxyXG4gICAgLy8gdGhlIGJyb3dzZXIgd2lsbCBvbmx5IHJlY29yZCBibGFjayBmcmFtZXMgXHJcbiAgICAvLyBvciBzaWxlbmNlIHNpbmNlIHRoYXQgaXMgdGhlIGNvbnRlbnQgcHJvZHVjZWQgYnkgdGhlIFRyYWNrXHJcbiAgICAvLyBzbyB3ZSBuZWVkIHRvIHN0b3BSZWNvcmRpbmcgYXMgc29vbiBhcyBhbnkgc2luZ2xlIHRyYWNrIGVuZHMuXHJcbiAgICBpZiAodHlwZW9mIGNvbmZpZy5jaGVja0ZvckluYWN0aXZlVHJhY2tzID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIGNvbmZpZy5jaGVja0ZvckluYWN0aXZlVHJhY2tzID0gZmFsc2U7IC8vIGRpc2FibGUgdG8gbWluaW1pemUgQ1BVIHVzYWdlXHJcbiAgICB9XHJcblxyXG4gICAgdmFyIHNlbGYgPSB0aGlzO1xyXG5cclxuICAgIC8vIHRoaXMgbWV0aG9kIGNoZWNrcyBpZiBtZWRpYSBzdHJlYW0gaXMgc3RvcHBlZFxyXG4gICAgLy8gb3IgaWYgYW55IHRyYWNrIGlzIGVuZGVkLlxyXG4gICAgKGZ1bmN0aW9uIGxvb3BlcigpIHtcclxuICAgICAgICBpZiAoIW1lZGlhUmVjb3JkZXIgfHwgY29uZmlnLmNoZWNrRm9ySW5hY3RpdmVUcmFja3MgPT09IGZhbHNlKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpc01lZGlhU3RyZWFtQWN0aXZlKCkgPT09IGZhbHNlKSB7XHJcbiAgICAgICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zb2xlLmxvZygnTWVkaWFTdHJlYW0gc2VlbXMgc3RvcHBlZC4nKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBzZWxmLnN0b3AoKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc2V0VGltZW91dChsb29wZXIsIDEwMDApOyAvLyBjaGVjayBldmVyeSBzZWNvbmRcclxuICAgIH0pKCk7XHJcblxyXG4gICAgLy8gZm9yIGRlYnVnZ2luZ1xyXG4gICAgdGhpcy5uYW1lID0gJ01lZGlhU3RyZWFtUmVjb3JkZXInO1xyXG4gICAgdGhpcy50b1N0cmluZyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiB0aGlzLm5hbWU7XHJcbiAgICB9O1xyXG59XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIFJlY29yZFJUQy5NZWRpYVN0cmVhbVJlY29yZGVyID0gTWVkaWFTdHJlYW1SZWNvcmRlcjtcclxufVxuXHJcbi8vIHNvdXJjZSBjb2RlIGZyb206IGh0dHA6Ly90eXBlZGFycmF5Lm9yZy93cC1jb250ZW50L3Byb2plY3RzL1dlYkF1ZGlvUmVjb3JkZXIvc2NyaXB0LmpzXHJcbi8vIGh0dHBzOi8vZ2l0aHViLmNvbS9tYXR0ZGlhbW9uZC9SZWNvcmRlcmpzI2xpY2Vuc2UtbWl0XHJcbi8vIF9fX19fX19fX19fX19fX19fX19fX19cclxuLy8gU3RlcmVvQXVkaW9SZWNvcmRlci5qc1xyXG5cclxuLyoqXHJcbiAqIFN0ZXJlb0F1ZGlvUmVjb3JkZXIgaXMgYSBzdGFuZGFsb25lIGNsYXNzIHVzZWQgYnkge0BsaW5rIFJlY29yZFJUQ30gdG8gYnJpbmcgXCJzdGVyZW9cIiBhdWRpby1yZWNvcmRpbmcgaW4gY2hyb21lLlxyXG4gKiBAc3VtbWFyeSBKYXZhU2NyaXB0IHN0YW5kYWxvbmUgb2JqZWN0IGZvciBzdGVyZW8gYXVkaW8gcmVjb3JkaW5nLlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQHR5cGVkZWYgU3RlcmVvQXVkaW9SZWNvcmRlclxyXG4gKiBAY2xhc3NcclxuICogQGV4YW1wbGVcclxuICogdmFyIHJlY29yZGVyID0gbmV3IFN0ZXJlb0F1ZGlvUmVjb3JkZXIoTWVkaWFTdHJlYW0sIHtcclxuICogICAgIHNhbXBsZVJhdGU6IDQ0MTAwLFxyXG4gKiAgICAgYnVmZmVyU2l6ZTogNDA5NlxyXG4gKiB9KTtcclxuICogcmVjb3JkZXIucmVjb3JkKCk7XHJcbiAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oYmxvYikge1xyXG4gKiAgICAgdmlkZW8uc3JjID0gVVJMLmNyZWF0ZU9iamVjdFVSTChibG9iKTtcclxuICogfSk7XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICogQHBhcmFtIHtNZWRpYVN0cmVhbX0gbWVkaWFTdHJlYW0gLSBNZWRpYVN0cmVhbSBvYmplY3QgZmV0Y2hlZCB1c2luZyBnZXRVc2VyTWVkaWEgQVBJIG9yIGdlbmVyYXRlZCB1c2luZyBjYXB0dXJlU3RyZWFtVW50aWxFbmRlZCBvciBXZWJBdWRpbyBBUEkuXHJcbiAqIEBwYXJhbSB7b2JqZWN0fSBjb25maWcgLSB7c2FtcGxlUmF0ZTogNDQxMDAsIGJ1ZmZlclNpemU6IDQwOTYsIG51bWJlck9mQXVkaW9DaGFubmVsczogMSwgZXRjLn1cclxuICovXHJcblxyXG5mdW5jdGlvbiBTdGVyZW9BdWRpb1JlY29yZGVyKG1lZGlhU3RyZWFtLCBjb25maWcpIHtcclxuICAgIGlmICghZ2V0VHJhY2tzKG1lZGlhU3RyZWFtLCAnYXVkaW8nKS5sZW5ndGgpIHtcclxuICAgICAgICB0aHJvdyAnWW91ciBzdHJlYW0gaGFzIG5vIGF1ZGlvIHRyYWNrcy4nO1xyXG4gICAgfVxyXG5cclxuICAgIGNvbmZpZyA9IGNvbmZpZyB8fCB7fTtcclxuXHJcbiAgICB2YXIgc2VsZiA9IHRoaXM7XHJcblxyXG4gICAgLy8gdmFyaWFibGVzXHJcbiAgICB2YXIgbGVmdGNoYW5uZWwgPSBbXTtcclxuICAgIHZhciByaWdodGNoYW5uZWwgPSBbXTtcclxuICAgIHZhciByZWNvcmRpbmcgPSBmYWxzZTtcclxuICAgIHZhciByZWNvcmRpbmdMZW5ndGggPSAwO1xyXG4gICAgdmFyIGpzQXVkaW9Ob2RlO1xyXG5cclxuICAgIHZhciBudW1iZXJPZkF1ZGlvQ2hhbm5lbHMgPSAyO1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogU2V0IHNhbXBsZSByYXRlcyBzdWNoIGFzIDhLIG9yIDE2Sy4gUmVmZXJlbmNlOiBodHRwOi8vc3RhY2tvdmVyZmxvdy5jb20vYS8yODk3NzEzNi81NTIxODJcclxuICAgICAqIEBwcm9wZXJ0eSB7bnVtYmVyfSBkZXNpcmVkU2FtcFJhdGUgLSBEZXNpcmVkIEJpdHMgcGVyIHNhbXBsZSAqIDEwMDBcclxuICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgKiBAaW5zdGFuY2VcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiB2YXIgcmVjb3JkZXIgPSBTdGVyZW9BdWRpb1JlY29yZGVyKG1lZGlhU3RyZWFtLCB7XHJcbiAgICAgKiAgIGRlc2lyZWRTYW1wUmF0ZTogMTYgKiAxMDAwIC8vIGJpdHMtcGVyLXNhbXBsZSAqIDEwMDBcclxuICAgICAqIH0pO1xyXG4gICAgICovXHJcbiAgICB2YXIgZGVzaXJlZFNhbXBSYXRlID0gY29uZmlnLmRlc2lyZWRTYW1wUmF0ZTtcclxuXHJcbiAgICAvLyBiYWNrd2FyZCBjb21wYXRpYmlsaXR5XHJcbiAgICBpZiAoY29uZmlnLmxlZnRDaGFubmVsID09PSB0cnVlKSB7XHJcbiAgICAgICAgbnVtYmVyT2ZBdWRpb0NoYW5uZWxzID0gMTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoY29uZmlnLm51bWJlck9mQXVkaW9DaGFubmVscyA9PT0gMSkge1xyXG4gICAgICAgIG51bWJlck9mQXVkaW9DaGFubmVscyA9IDE7XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKCFudW1iZXJPZkF1ZGlvQ2hhbm5lbHMgfHwgbnVtYmVyT2ZBdWRpb0NoYW5uZWxzIDwgMSkge1xyXG4gICAgICAgIG51bWJlck9mQXVkaW9DaGFubmVscyA9IDI7XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICBjb25zb2xlLmxvZygnU3RlcmVvQXVkaW9SZWNvcmRlciBpcyBzZXQgdG8gcmVjb3JkIG51bWJlciBvZiBjaGFubmVsczogJyArIG51bWJlck9mQXVkaW9DaGFubmVscyk7XHJcbiAgICB9XHJcblxyXG4gICAgLy8gaWYgYW55IFRyYWNrIHdpdGhpbiB0aGUgTWVkaWFTdHJlYW0gaXMgbXV0ZWQgb3Igbm90IGVuYWJsZWQgYXQgYW55IHRpbWUsIFxyXG4gICAgLy8gdGhlIGJyb3dzZXIgd2lsbCBvbmx5IHJlY29yZCBibGFjayBmcmFtZXMgXHJcbiAgICAvLyBvciBzaWxlbmNlIHNpbmNlIHRoYXQgaXMgdGhlIGNvbnRlbnQgcHJvZHVjZWQgYnkgdGhlIFRyYWNrXHJcbiAgICAvLyBzbyB3ZSBuZWVkIHRvIHN0b3BSZWNvcmRpbmcgYXMgc29vbiBhcyBhbnkgc2luZ2xlIHRyYWNrIGVuZHMuXHJcbiAgICBpZiAodHlwZW9mIGNvbmZpZy5jaGVja0ZvckluYWN0aXZlVHJhY2tzID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIGNvbmZpZy5jaGVja0ZvckluYWN0aXZlVHJhY2tzID0gdHJ1ZTtcclxuICAgIH1cclxuXHJcbiAgICBmdW5jdGlvbiBpc01lZGlhU3RyZWFtQWN0aXZlKCkge1xyXG4gICAgICAgIGlmIChjb25maWcuY2hlY2tGb3JJbmFjdGl2ZVRyYWNrcyA9PT0gZmFsc2UpIHtcclxuICAgICAgICAgICAgLy8gYWx3YXlzIHJldHVybiBcInRydWVcIlxyXG4gICAgICAgICAgICByZXR1cm4gdHJ1ZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICgnYWN0aXZlJyBpbiBtZWRpYVN0cmVhbSkge1xyXG4gICAgICAgICAgICBpZiAoIW1lZGlhU3RyZWFtLmFjdGl2ZSkge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSBlbHNlIGlmICgnZW5kZWQnIGluIG1lZGlhU3RyZWFtKSB7IC8vIG9sZCBoYWNrXHJcbiAgICAgICAgICAgIGlmIChtZWRpYVN0cmVhbS5lbmRlZCkge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgfVxyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVjb3JkcyBNZWRpYVN0cmVhbS5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucmVjb3JkKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucmVjb3JkID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaWYgKGlzTWVkaWFTdHJlYW1BY3RpdmUoKSA9PT0gZmFsc2UpIHtcclxuICAgICAgICAgICAgdGhyb3cgJ1BsZWFzZSBtYWtlIHN1cmUgTWVkaWFTdHJlYW0gaXMgYWN0aXZlLic7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICByZXNldFZhcmlhYmxlcygpO1xyXG5cclxuICAgICAgICBpc0F1ZGlvUHJvY2Vzc1N0YXJ0ZWQgPSBpc1BhdXNlZCA9IGZhbHNlO1xyXG4gICAgICAgIHJlY29yZGluZyA9IHRydWU7XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgY29uZmlnLnRpbWVTbGljZSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAgICAgbG9vcGVyKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICBmdW5jdGlvbiBtZXJnZUxlZnRSaWdodEJ1ZmZlcnMoY29uZmlnLCBjYWxsYmFjaykge1xyXG4gICAgICAgIGZ1bmN0aW9uIG1lcmdlQXVkaW9CdWZmZXJzKGNvbmZpZywgY2IpIHtcclxuICAgICAgICAgICAgdmFyIG51bWJlck9mQXVkaW9DaGFubmVscyA9IGNvbmZpZy5udW1iZXJPZkF1ZGlvQ2hhbm5lbHM7XHJcblxyXG4gICAgICAgICAgICAvLyB0b2RvOiBcInNsaWNlKDApXCIgLS0tIGlzIGl0IGNhdXNlcyBsb29wPyBTaG91bGQgYmUgcmVtb3ZlZD9cclxuICAgICAgICAgICAgdmFyIGxlZnRCdWZmZXJzID0gY29uZmlnLmxlZnRCdWZmZXJzLnNsaWNlKDApO1xyXG4gICAgICAgICAgICB2YXIgcmlnaHRCdWZmZXJzID0gY29uZmlnLnJpZ2h0QnVmZmVycy5zbGljZSgwKTtcclxuICAgICAgICAgICAgdmFyIHNhbXBsZVJhdGUgPSBjb25maWcuc2FtcGxlUmF0ZTtcclxuICAgICAgICAgICAgdmFyIGludGVybmFsSW50ZXJsZWF2ZWRMZW5ndGggPSBjb25maWcuaW50ZXJuYWxJbnRlcmxlYXZlZExlbmd0aDtcclxuICAgICAgICAgICAgdmFyIGRlc2lyZWRTYW1wUmF0ZSA9IGNvbmZpZy5kZXNpcmVkU2FtcFJhdGU7XHJcblxyXG4gICAgICAgICAgICBpZiAobnVtYmVyT2ZBdWRpb0NoYW5uZWxzID09PSAyKSB7XHJcbiAgICAgICAgICAgICAgICBsZWZ0QnVmZmVycyA9IG1lcmdlQnVmZmVycyhsZWZ0QnVmZmVycywgaW50ZXJuYWxJbnRlcmxlYXZlZExlbmd0aCk7XHJcbiAgICAgICAgICAgICAgICByaWdodEJ1ZmZlcnMgPSBtZXJnZUJ1ZmZlcnMocmlnaHRCdWZmZXJzLCBpbnRlcm5hbEludGVybGVhdmVkTGVuZ3RoKTtcclxuXHJcbiAgICAgICAgICAgICAgICBpZiAoZGVzaXJlZFNhbXBSYXRlKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgbGVmdEJ1ZmZlcnMgPSBpbnRlcnBvbGF0ZUFycmF5KGxlZnRCdWZmZXJzLCBkZXNpcmVkU2FtcFJhdGUsIHNhbXBsZVJhdGUpO1xyXG4gICAgICAgICAgICAgICAgICAgIHJpZ2h0QnVmZmVycyA9IGludGVycG9sYXRlQXJyYXkocmlnaHRCdWZmZXJzLCBkZXNpcmVkU2FtcFJhdGUsIHNhbXBsZVJhdGUpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAobnVtYmVyT2ZBdWRpb0NoYW5uZWxzID09PSAxKSB7XHJcbiAgICAgICAgICAgICAgICBsZWZ0QnVmZmVycyA9IG1lcmdlQnVmZmVycyhsZWZ0QnVmZmVycywgaW50ZXJuYWxJbnRlcmxlYXZlZExlbmd0aCk7XHJcblxyXG4gICAgICAgICAgICAgICAgaWYgKGRlc2lyZWRTYW1wUmF0ZSkge1xyXG4gICAgICAgICAgICAgICAgICAgIGxlZnRCdWZmZXJzID0gaW50ZXJwb2xhdGVBcnJheShsZWZ0QnVmZmVycywgZGVzaXJlZFNhbXBSYXRlLCBzYW1wbGVSYXRlKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgLy8gc2V0IHNhbXBsZSByYXRlIGFzIGRlc2lyZWQgc2FtcGxlIHJhdGVcclxuICAgICAgICAgICAgaWYgKGRlc2lyZWRTYW1wUmF0ZSkge1xyXG4gICAgICAgICAgICAgICAgc2FtcGxlUmF0ZSA9IGRlc2lyZWRTYW1wUmF0ZTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgLy8gZm9yIGNoYW5naW5nIHRoZSBzYW1wbGluZyByYXRlLCByZWZlcmVuY2U6XHJcbiAgICAgICAgICAgIC8vIGh0dHA6Ly9zdGFja292ZXJmbG93LmNvbS9hLzI4OTc3MTM2LzU1MjE4MlxyXG4gICAgICAgICAgICBmdW5jdGlvbiBpbnRlcnBvbGF0ZUFycmF5KGRhdGEsIG5ld1NhbXBsZVJhdGUsIG9sZFNhbXBsZVJhdGUpIHtcclxuICAgICAgICAgICAgICAgIHZhciBmaXRDb3VudCA9IE1hdGgucm91bmQoZGF0YS5sZW5ndGggKiAobmV3U2FtcGxlUmF0ZSAvIG9sZFNhbXBsZVJhdGUpKTtcclxuICAgICAgICAgICAgICAgIHZhciBuZXdEYXRhID0gW107XHJcbiAgICAgICAgICAgICAgICB2YXIgc3ByaW5nRmFjdG9yID0gTnVtYmVyKChkYXRhLmxlbmd0aCAtIDEpIC8gKGZpdENvdW50IC0gMSkpO1xyXG4gICAgICAgICAgICAgICAgbmV3RGF0YVswXSA9IGRhdGFbMF07XHJcbiAgICAgICAgICAgICAgICBmb3IgKHZhciBpID0gMTsgaSA8IGZpdENvdW50IC0gMTsgaSsrKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdmFyIHRtcCA9IGkgKiBzcHJpbmdGYWN0b3I7XHJcbiAgICAgICAgICAgICAgICAgICAgdmFyIGJlZm9yZSA9IE51bWJlcihNYXRoLmZsb29yKHRtcCkpLnRvRml4ZWQoKTtcclxuICAgICAgICAgICAgICAgICAgICB2YXIgYWZ0ZXIgPSBOdW1iZXIoTWF0aC5jZWlsKHRtcCkpLnRvRml4ZWQoKTtcclxuICAgICAgICAgICAgICAgICAgICB2YXIgYXRQb2ludCA9IHRtcCAtIGJlZm9yZTtcclxuICAgICAgICAgICAgICAgICAgICBuZXdEYXRhW2ldID0gbGluZWFySW50ZXJwb2xhdGUoZGF0YVtiZWZvcmVdLCBkYXRhW2FmdGVyXSwgYXRQb2ludCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICBuZXdEYXRhW2ZpdENvdW50IC0gMV0gPSBkYXRhW2RhdGEubGVuZ3RoIC0gMV07XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gbmV3RGF0YTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgZnVuY3Rpb24gbGluZWFySW50ZXJwb2xhdGUoYmVmb3JlLCBhZnRlciwgYXRQb2ludCkge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIGJlZm9yZSArIChhZnRlciAtIGJlZm9yZSkgKiBhdFBvaW50O1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBmdW5jdGlvbiBtZXJnZUJ1ZmZlcnMoY2hhbm5lbEJ1ZmZlciwgckxlbmd0aCkge1xyXG4gICAgICAgICAgICAgICAgdmFyIHJlc3VsdCA9IG5ldyBGbG9hdDY0QXJyYXkockxlbmd0aCk7XHJcbiAgICAgICAgICAgICAgICB2YXIgb2Zmc2V0ID0gMDtcclxuICAgICAgICAgICAgICAgIHZhciBsbmcgPSBjaGFubmVsQnVmZmVyLmxlbmd0aDtcclxuXHJcbiAgICAgICAgICAgICAgICBmb3IgKHZhciBpID0gMDsgaSA8IGxuZzsgaSsrKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdmFyIGJ1ZmZlciA9IGNoYW5uZWxCdWZmZXJbaV07XHJcbiAgICAgICAgICAgICAgICAgICAgcmVzdWx0LnNldChidWZmZXIsIG9mZnNldCk7XHJcbiAgICAgICAgICAgICAgICAgICAgb2Zmc2V0ICs9IGJ1ZmZlci5sZW5ndGg7XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHJlc3VsdDtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgZnVuY3Rpb24gaW50ZXJsZWF2ZShsZWZ0Q2hhbm5lbCwgcmlnaHRDaGFubmVsKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgbGVuZ3RoID0gbGVmdENoYW5uZWwubGVuZ3RoICsgcmlnaHRDaGFubmVsLmxlbmd0aDtcclxuXHJcbiAgICAgICAgICAgICAgICB2YXIgcmVzdWx0ID0gbmV3IEZsb2F0NjRBcnJheShsZW5ndGgpO1xyXG5cclxuICAgICAgICAgICAgICAgIHZhciBpbnB1dEluZGV4ID0gMDtcclxuXHJcbiAgICAgICAgICAgICAgICBmb3IgKHZhciBpbmRleCA9IDA7IGluZGV4IDwgbGVuZ3RoOykge1xyXG4gICAgICAgICAgICAgICAgICAgIHJlc3VsdFtpbmRleCsrXSA9IGxlZnRDaGFubmVsW2lucHV0SW5kZXhdO1xyXG4gICAgICAgICAgICAgICAgICAgIHJlc3VsdFtpbmRleCsrXSA9IHJpZ2h0Q2hhbm5lbFtpbnB1dEluZGV4XTtcclxuICAgICAgICAgICAgICAgICAgICBpbnB1dEluZGV4Kys7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gcmVzdWx0O1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBmdW5jdGlvbiB3cml0ZVVURkJ5dGVzKHZpZXcsIG9mZnNldCwgc3RyaW5nKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgbG5nID0gc3RyaW5nLmxlbmd0aDtcclxuICAgICAgICAgICAgICAgIGZvciAodmFyIGkgPSAwOyBpIDwgbG5nOyBpKyspIHtcclxuICAgICAgICAgICAgICAgICAgICB2aWV3LnNldFVpbnQ4KG9mZnNldCArIGksIHN0cmluZy5jaGFyQ29kZUF0KGkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgLy8gaW50ZXJsZWF2ZSBib3RoIGNoYW5uZWxzIHRvZ2V0aGVyXHJcbiAgICAgICAgICAgIHZhciBpbnRlcmxlYXZlZDtcclxuXHJcbiAgICAgICAgICAgIGlmIChudW1iZXJPZkF1ZGlvQ2hhbm5lbHMgPT09IDIpIHtcclxuICAgICAgICAgICAgICAgIGludGVybGVhdmVkID0gaW50ZXJsZWF2ZShsZWZ0QnVmZmVycywgcmlnaHRCdWZmZXJzKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKG51bWJlck9mQXVkaW9DaGFubmVscyA9PT0gMSkge1xyXG4gICAgICAgICAgICAgICAgaW50ZXJsZWF2ZWQgPSBsZWZ0QnVmZmVycztcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgdmFyIGludGVybGVhdmVkTGVuZ3RoID0gaW50ZXJsZWF2ZWQubGVuZ3RoO1xyXG5cclxuICAgICAgICAgICAgLy8gY3JlYXRlIHdhdiBmaWxlXHJcbiAgICAgICAgICAgIHZhciByZXN1bHRpbmdCdWZmZXJMZW5ndGggPSA0NCArIGludGVybGVhdmVkTGVuZ3RoICogMjtcclxuXHJcbiAgICAgICAgICAgIHZhciBidWZmZXIgPSBuZXcgQXJyYXlCdWZmZXIocmVzdWx0aW5nQnVmZmVyTGVuZ3RoKTtcclxuXHJcbiAgICAgICAgICAgIHZhciB2aWV3ID0gbmV3IERhdGFWaWV3KGJ1ZmZlcik7XHJcblxyXG4gICAgICAgICAgICAvLyBSSUZGIGNodW5rIGRlc2NyaXB0b3IvaWRlbnRpZmllciBcclxuICAgICAgICAgICAgd3JpdGVVVEZCeXRlcyh2aWV3LCAwLCAnUklGRicpO1xyXG5cclxuICAgICAgICAgICAgLy8gUklGRiBjaHVuayBsZW5ndGhcclxuICAgICAgICAgICAgLy8gY2hhbmdlZCBcIjQ0XCIgdG8gXCIzNlwiIHZpYSAjNDAxXHJcbiAgICAgICAgICAgIHZpZXcuc2V0VWludDMyKDQsIDM2ICsgaW50ZXJsZWF2ZWRMZW5ndGggKiAyLCB0cnVlKTtcclxuXHJcbiAgICAgICAgICAgIC8vIFJJRkYgdHlwZSBcclxuICAgICAgICAgICAgd3JpdGVVVEZCeXRlcyh2aWV3LCA4LCAnV0FWRScpO1xyXG5cclxuICAgICAgICAgICAgLy8gZm9ybWF0IGNodW5rIGlkZW50aWZpZXIgXHJcbiAgICAgICAgICAgIC8vIEZNVCBzdWItY2h1bmtcclxuICAgICAgICAgICAgd3JpdGVVVEZCeXRlcyh2aWV3LCAxMiwgJ2ZtdCAnKTtcclxuXHJcbiAgICAgICAgICAgIC8vIGZvcm1hdCBjaHVuayBsZW5ndGggXHJcbiAgICAgICAgICAgIHZpZXcuc2V0VWludDMyKDE2LCAxNiwgdHJ1ZSk7XHJcblxyXG4gICAgICAgICAgICAvLyBzYW1wbGUgZm9ybWF0IChyYXcpXHJcbiAgICAgICAgICAgIHZpZXcuc2V0VWludDE2KDIwLCAxLCB0cnVlKTtcclxuXHJcbiAgICAgICAgICAgIC8vIHN0ZXJlbyAoMiBjaGFubmVscylcclxuICAgICAgICAgICAgdmlldy5zZXRVaW50MTYoMjIsIG51bWJlck9mQXVkaW9DaGFubmVscywgdHJ1ZSk7XHJcblxyXG4gICAgICAgICAgICAvLyBzYW1wbGUgcmF0ZSBcclxuICAgICAgICAgICAgdmlldy5zZXRVaW50MzIoMjQsIHNhbXBsZVJhdGUsIHRydWUpO1xyXG5cclxuICAgICAgICAgICAgLy8gYnl0ZSByYXRlIChzYW1wbGUgcmF0ZSAqIGJsb2NrIGFsaWduKVxyXG4gICAgICAgICAgICB2aWV3LnNldFVpbnQzMigyOCwgc2FtcGxlUmF0ZSAqIDIsIHRydWUpO1xyXG5cclxuICAgICAgICAgICAgLy8gYmxvY2sgYWxpZ24gKGNoYW5uZWwgY291bnQgKiBieXRlcyBwZXIgc2FtcGxlKSBcclxuICAgICAgICAgICAgdmlldy5zZXRVaW50MTYoMzIsIG51bWJlck9mQXVkaW9DaGFubmVscyAqIDIsIHRydWUpO1xyXG5cclxuICAgICAgICAgICAgLy8gYml0cyBwZXIgc2FtcGxlIFxyXG4gICAgICAgICAgICB2aWV3LnNldFVpbnQxNigzNCwgMTYsIHRydWUpO1xyXG5cclxuICAgICAgICAgICAgLy8gZGF0YSBzdWItY2h1bmtcclxuICAgICAgICAgICAgLy8gZGF0YSBjaHVuayBpZGVudGlmaWVyIFxyXG4gICAgICAgICAgICB3cml0ZVVURkJ5dGVzKHZpZXcsIDM2LCAnZGF0YScpO1xyXG5cclxuICAgICAgICAgICAgLy8gZGF0YSBjaHVuayBsZW5ndGggXHJcbiAgICAgICAgICAgIHZpZXcuc2V0VWludDMyKDQwLCBpbnRlcmxlYXZlZExlbmd0aCAqIDIsIHRydWUpO1xyXG5cclxuICAgICAgICAgICAgLy8gd3JpdGUgdGhlIFBDTSBzYW1wbGVzXHJcbiAgICAgICAgICAgIHZhciBsbmcgPSBpbnRlcmxlYXZlZExlbmd0aDtcclxuICAgICAgICAgICAgdmFyIGluZGV4ID0gNDQ7XHJcbiAgICAgICAgICAgIHZhciB2b2x1bWUgPSAxO1xyXG4gICAgICAgICAgICBmb3IgKHZhciBpID0gMDsgaSA8IGxuZzsgaSsrKSB7XHJcbiAgICAgICAgICAgICAgICB2aWV3LnNldEludDE2KGluZGV4LCBpbnRlcmxlYXZlZFtpXSAqICgweDdGRkYgKiB2b2x1bWUpLCB0cnVlKTtcclxuICAgICAgICAgICAgICAgIGluZGV4ICs9IDI7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChjYikge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIGNiKHtcclxuICAgICAgICAgICAgICAgICAgICBidWZmZXI6IGJ1ZmZlcixcclxuICAgICAgICAgICAgICAgICAgICB2aWV3OiB2aWV3XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgcG9zdE1lc3NhZ2Uoe1xyXG4gICAgICAgICAgICAgICAgYnVmZmVyOiBidWZmZXIsXHJcbiAgICAgICAgICAgICAgICB2aWV3OiB2aWV3XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKGNvbmZpZy5ub1dvcmtlcikge1xyXG4gICAgICAgICAgICBtZXJnZUF1ZGlvQnVmZmVycyhjb25maWcsIGZ1bmN0aW9uKGRhdGEpIHtcclxuICAgICAgICAgICAgICAgIGNhbGxiYWNrKGRhdGEuYnVmZmVyLCBkYXRhLnZpZXcpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcblxyXG4gICAgICAgIHZhciB3ZWJXb3JrZXIgPSBwcm9jZXNzSW5XZWJXb3JrZXIobWVyZ2VBdWRpb0J1ZmZlcnMpO1xyXG5cclxuICAgICAgICB3ZWJXb3JrZXIub25tZXNzYWdlID0gZnVuY3Rpb24oZXZlbnQpIHtcclxuICAgICAgICAgICAgY2FsbGJhY2soZXZlbnQuZGF0YS5idWZmZXIsIGV2ZW50LmRhdGEudmlldyk7XHJcblxyXG4gICAgICAgICAgICAvLyByZWxlYXNlIG1lbW9yeVxyXG4gICAgICAgICAgICBVUkwucmV2b2tlT2JqZWN0VVJMKHdlYldvcmtlci53b3JrZXJVUkwpO1xyXG5cclxuICAgICAgICAgICAgLy8ga2lsbCB3ZWJ3b3JrZXIgKG9yIENocm9tZSB3aWxsIGtpbGwgeW91ciBwYWdlIGFmdGVyIH4yNSBjYWxscylcclxuICAgICAgICAgICAgd2ViV29ya2VyLnRlcm1pbmF0ZSgpO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIHdlYldvcmtlci5wb3N0TWVzc2FnZShjb25maWcpO1xyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIHByb2Nlc3NJbldlYldvcmtlcihfZnVuY3Rpb24pIHtcclxuICAgICAgICB2YXIgd29ya2VyVVJMID0gVVJMLmNyZWF0ZU9iamVjdFVSTChuZXcgQmxvYihbX2Z1bmN0aW9uLnRvU3RyaW5nKCksXHJcbiAgICAgICAgICAgICc7dGhpcy5vbm1lc3NhZ2UgPSAgZnVuY3Rpb24gKGVlZSkgeycgKyBfZnVuY3Rpb24ubmFtZSArICcoZWVlLmRhdGEpO30nXHJcbiAgICAgICAgXSwge1xyXG4gICAgICAgICAgICB0eXBlOiAnYXBwbGljYXRpb24vamF2YXNjcmlwdCdcclxuICAgICAgICB9KSk7XHJcblxyXG4gICAgICAgIHZhciB3b3JrZXIgPSBuZXcgV29ya2VyKHdvcmtlclVSTCk7XHJcbiAgICAgICAgd29ya2VyLndvcmtlclVSTCA9IHdvcmtlclVSTDtcclxuICAgICAgICByZXR1cm4gd29ya2VyO1xyXG4gICAgfVxyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2Qgc3RvcHMgcmVjb3JkaW5nIE1lZGlhU3RyZWFtLlxyXG4gICAgICogQHBhcmFtIHtmdW5jdGlvbn0gY2FsbGJhY2sgLSBDYWxsYmFjayBmdW5jdGlvbiwgdGhhdCBpcyB1c2VkIHRvIHBhc3MgcmVjb3JkZWQgYmxvYiBiYWNrIHRvIHRoZSBjYWxsZWUuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgU3RlcmVvQXVkaW9SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oYmxvYikge1xyXG4gICAgICogICAgIHZpZGVvLnNyYyA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5zdG9wID0gZnVuY3Rpb24oY2FsbGJhY2spIHtcclxuICAgICAgICBjYWxsYmFjayA9IGNhbGxiYWNrIHx8IGZ1bmN0aW9uKCkge307XHJcblxyXG4gICAgICAgIC8vIHN0b3AgcmVjb3JkaW5nXHJcbiAgICAgICAgcmVjb3JkaW5nID0gZmFsc2U7XHJcblxyXG4gICAgICAgIG1lcmdlTGVmdFJpZ2h0QnVmZmVycyh7XHJcbiAgICAgICAgICAgIGRlc2lyZWRTYW1wUmF0ZTogZGVzaXJlZFNhbXBSYXRlLFxyXG4gICAgICAgICAgICBzYW1wbGVSYXRlOiBzYW1wbGVSYXRlLFxyXG4gICAgICAgICAgICBudW1iZXJPZkF1ZGlvQ2hhbm5lbHM6IG51bWJlck9mQXVkaW9DaGFubmVscyxcclxuICAgICAgICAgICAgaW50ZXJuYWxJbnRlcmxlYXZlZExlbmd0aDogcmVjb3JkaW5nTGVuZ3RoLFxyXG4gICAgICAgICAgICBsZWZ0QnVmZmVyczogbGVmdGNoYW5uZWwsXHJcbiAgICAgICAgICAgIHJpZ2h0QnVmZmVyczogbnVtYmVyT2ZBdWRpb0NoYW5uZWxzID09PSAxID8gW10gOiByaWdodGNoYW5uZWwsXHJcbiAgICAgICAgICAgIG5vV29ya2VyOiBjb25maWcubm9Xb3JrZXJcclxuICAgICAgICB9LCBmdW5jdGlvbihidWZmZXIsIHZpZXcpIHtcclxuICAgICAgICAgICAgLyoqXHJcbiAgICAgICAgICAgICAqIEBwcm9wZXJ0eSB7QmxvYn0gYmxvYiAtIFRoZSByZWNvcmRlZCBibG9iIG9iamVjdC5cclxuICAgICAgICAgICAgICogQG1lbWJlcm9mIFN0ZXJlb0F1ZGlvUmVjb3JkZXJcclxuICAgICAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgICAgICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbigpe1xyXG4gICAgICAgICAgICAgKiAgICAgdmFyIGJsb2IgPSByZWNvcmRlci5ibG9iO1xyXG4gICAgICAgICAgICAgKiB9KTtcclxuICAgICAgICAgICAgICovXHJcbiAgICAgICAgICAgIHNlbGYuYmxvYiA9IG5ldyBCbG9iKFt2aWV3XSwge1xyXG4gICAgICAgICAgICAgICAgdHlwZTogJ2F1ZGlvL3dhdidcclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICAvKipcclxuICAgICAgICAgICAgICogQHByb3BlcnR5IHtBcnJheUJ1ZmZlcn0gYnVmZmVyIC0gVGhlIHJlY29yZGVkIGJ1ZmZlciBvYmplY3QuXHJcbiAgICAgICAgICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICAgICAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oKXtcclxuICAgICAgICAgICAgICogICAgIHZhciBidWZmZXIgPSByZWNvcmRlci5idWZmZXI7XHJcbiAgICAgICAgICAgICAqIH0pO1xyXG4gICAgICAgICAgICAgKi9cclxuICAgICAgICAgICAgc2VsZi5idWZmZXIgPSBuZXcgQXJyYXlCdWZmZXIodmlldy5idWZmZXIuYnl0ZUxlbmd0aCk7XHJcblxyXG4gICAgICAgICAgICAvKipcclxuICAgICAgICAgICAgICogQHByb3BlcnR5IHtEYXRhVmlld30gdmlldyAtIFRoZSByZWNvcmRlZCBkYXRhLXZpZXcgb2JqZWN0LlxyXG4gICAgICAgICAgICAgKiBAbWVtYmVyb2YgU3RlcmVvQXVkaW9SZWNvcmRlclxyXG4gICAgICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAgICAgKiByZWNvcmRlci5zdG9wKGZ1bmN0aW9uKCl7XHJcbiAgICAgICAgICAgICAqICAgICB2YXIgdmlldyA9IHJlY29yZGVyLnZpZXc7XHJcbiAgICAgICAgICAgICAqIH0pO1xyXG4gICAgICAgICAgICAgKi9cclxuICAgICAgICAgICAgc2VsZi52aWV3ID0gdmlldztcclxuXHJcbiAgICAgICAgICAgIHNlbGYuc2FtcGxlUmF0ZSA9IGRlc2lyZWRTYW1wUmF0ZSB8fCBzYW1wbGVSYXRlO1xyXG4gICAgICAgICAgICBzZWxmLmJ1ZmZlclNpemUgPSBidWZmZXJTaXplO1xyXG5cclxuICAgICAgICAgICAgLy8gcmVjb3JkZWQgYXVkaW8gbGVuZ3RoXHJcbiAgICAgICAgICAgIHNlbGYubGVuZ3RoID0gcmVjb3JkaW5nTGVuZ3RoO1xyXG5cclxuICAgICAgICAgICAgaXNBdWRpb1Byb2Nlc3NTdGFydGVkID0gZmFsc2U7XHJcblxyXG4gICAgICAgICAgICBpZiAoY2FsbGJhY2spIHtcclxuICAgICAgICAgICAgICAgIGNhbGxiYWNrKHNlbGYuYmxvYik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgaWYgKHR5cGVvZiBSZWNvcmRSVEMuU3RvcmFnZSA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICBSZWNvcmRSVEMuU3RvcmFnZSA9IHtcclxuICAgICAgICAgICAgQXVkaW9Db250ZXh0Q29uc3RydWN0b3I6IG51bGwsXHJcbiAgICAgICAgICAgIEF1ZGlvQ29udGV4dDogd2luZG93LkF1ZGlvQ29udGV4dCB8fCB3aW5kb3cud2Via2l0QXVkaW9Db250ZXh0XHJcbiAgICAgICAgfTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoIVJlY29yZFJUQy5TdG9yYWdlLkF1ZGlvQ29udGV4dENvbnN0cnVjdG9yIHx8IFJlY29yZFJUQy5TdG9yYWdlLkF1ZGlvQ29udGV4dENvbnN0cnVjdG9yLnN0YXRlID09PSAnY2xvc2VkJykge1xyXG4gICAgICAgIFJlY29yZFJUQy5TdG9yYWdlLkF1ZGlvQ29udGV4dENvbnN0cnVjdG9yID0gbmV3IFJlY29yZFJUQy5TdG9yYWdlLkF1ZGlvQ29udGV4dCgpO1xyXG4gICAgfVxyXG5cclxuICAgIHZhciBjb250ZXh0ID0gUmVjb3JkUlRDLlN0b3JhZ2UuQXVkaW9Db250ZXh0Q29uc3RydWN0b3I7XHJcblxyXG4gICAgLy8gY3JlYXRlcyBhbiBhdWRpbyBub2RlIGZyb20gdGhlIG1pY3JvcGhvbmUgaW5jb21pbmcgc3RyZWFtXHJcbiAgICB2YXIgYXVkaW9JbnB1dCA9IGNvbnRleHQuY3JlYXRlTWVkaWFTdHJlYW1Tb3VyY2UobWVkaWFTdHJlYW0pO1xyXG5cclxuICAgIHZhciBsZWdhbEJ1ZmZlclZhbHVlcyA9IFswLCAyNTYsIDUxMiwgMTAyNCwgMjA0OCwgNDA5NiwgODE5MiwgMTYzODRdO1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogRnJvbSB0aGUgc3BlYzogVGhpcyB2YWx1ZSBjb250cm9scyBob3cgZnJlcXVlbnRseSB0aGUgYXVkaW9wcm9jZXNzIGV2ZW50IGlzXHJcbiAgICAgKiBkaXNwYXRjaGVkIGFuZCBob3cgbWFueSBzYW1wbGUtZnJhbWVzIG5lZWQgdG8gYmUgcHJvY2Vzc2VkIGVhY2ggY2FsbC5cclxuICAgICAqIExvd2VyIHZhbHVlcyBmb3IgYnVmZmVyIHNpemUgd2lsbCByZXN1bHQgaW4gYSBsb3dlciAoYmV0dGVyKSBsYXRlbmN5LlxyXG4gICAgICogSGlnaGVyIHZhbHVlcyB3aWxsIGJlIG5lY2Vzc2FyeSB0byBhdm9pZCBhdWRpbyBicmVha3VwIGFuZCBnbGl0Y2hlc1xyXG4gICAgICogVGhlIHNpemUgb2YgdGhlIGJ1ZmZlciAoaW4gc2FtcGxlLWZyYW1lcykgd2hpY2ggbmVlZHMgdG9cclxuICAgICAqIGJlIHByb2Nlc3NlZCBlYWNoIHRpbWUgb25wcm9jZXNzYXVkaW8gaXMgY2FsbGVkLlxyXG4gICAgICogTGVnYWwgdmFsdWVzIGFyZSAoMjU2LCA1MTIsIDEwMjQsIDIwNDgsIDQwOTYsIDgxOTIsIDE2Mzg0KS5cclxuICAgICAqIEBwcm9wZXJ0eSB7bnVtYmVyfSBidWZmZXJTaXplIC0gQnVmZmVyLXNpemUgZm9yIGhvdyBmcmVxdWVudGx5IHRoZSBhdWRpb3Byb2Nlc3MgZXZlbnQgaXMgZGlzcGF0Y2hlZC5cclxuICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIgPSBuZXcgU3RlcmVvQXVkaW9SZWNvcmRlcihtZWRpYVN0cmVhbSwge1xyXG4gICAgICogICAgIGJ1ZmZlclNpemU6IDQwOTZcclxuICAgICAqIH0pO1xyXG4gICAgICovXHJcblxyXG4gICAgLy8gXCIwXCIgbWVhbnMsIGxldCBjaHJvbWUgZGVjaWRlIHRoZSBtb3N0IGFjY3VyYXRlIGJ1ZmZlci1zaXplIGZvciBjdXJyZW50IHBsYXRmb3JtLlxyXG4gICAgdmFyIGJ1ZmZlclNpemUgPSB0eXBlb2YgY29uZmlnLmJ1ZmZlclNpemUgPT09ICd1bmRlZmluZWQnID8gNDA5NiA6IGNvbmZpZy5idWZmZXJTaXplO1xyXG5cclxuICAgIGlmIChsZWdhbEJ1ZmZlclZhbHVlcy5pbmRleE9mKGJ1ZmZlclNpemUpID09PSAtMSkge1xyXG4gICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCdMZWdhbCB2YWx1ZXMgZm9yIGJ1ZmZlci1zaXplIGFyZSAnICsgSlNPTi5zdHJpbmdpZnkobGVnYWxCdWZmZXJWYWx1ZXMsIG51bGwsICdcXHQnKSk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGlmIChjb250ZXh0LmNyZWF0ZUphdmFTY3JpcHROb2RlKSB7XHJcbiAgICAgICAganNBdWRpb05vZGUgPSBjb250ZXh0LmNyZWF0ZUphdmFTY3JpcHROb2RlKGJ1ZmZlclNpemUsIG51bWJlck9mQXVkaW9DaGFubmVscywgbnVtYmVyT2ZBdWRpb0NoYW5uZWxzKTtcclxuICAgIH0gZWxzZSBpZiAoY29udGV4dC5jcmVhdGVTY3JpcHRQcm9jZXNzb3IpIHtcclxuICAgICAgICBqc0F1ZGlvTm9kZSA9IGNvbnRleHQuY3JlYXRlU2NyaXB0UHJvY2Vzc29yKGJ1ZmZlclNpemUsIG51bWJlck9mQXVkaW9DaGFubmVscywgbnVtYmVyT2ZBdWRpb0NoYW5uZWxzKTtcclxuICAgIH0gZWxzZSB7XHJcbiAgICAgICAgdGhyb3cgJ1dlYkF1ZGlvIEFQSSBoYXMgbm8gc3VwcG9ydCBvbiB0aGlzIGJyb3dzZXIuJztcclxuICAgIH1cclxuXHJcbiAgICAvLyBjb25uZWN0IHRoZSBzdHJlYW0gdG8gdGhlIHNjcmlwdCBwcm9jZXNzb3JcclxuICAgIGF1ZGlvSW5wdXQuY29ubmVjdChqc0F1ZGlvTm9kZSk7XHJcblxyXG4gICAgaWYgKCFjb25maWcuYnVmZmVyU2l6ZSkge1xyXG4gICAgICAgIGJ1ZmZlclNpemUgPSBqc0F1ZGlvTm9kZS5idWZmZXJTaXplOyAvLyBkZXZpY2UgYnVmZmVyLXNpemVcclxuICAgIH1cclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoZSBzYW1wbGUgcmF0ZSAoaW4gc2FtcGxlLWZyYW1lcyBwZXIgc2Vjb25kKSBhdCB3aGljaCB0aGVcclxuICAgICAqIEF1ZGlvQ29udGV4dCBoYW5kbGVzIGF1ZGlvLiBJdCBpcyBhc3N1bWVkIHRoYXQgYWxsIEF1ZGlvTm9kZXNcclxuICAgICAqIGluIHRoZSBjb250ZXh0IHJ1biBhdCB0aGlzIHJhdGUuIEluIG1ha2luZyB0aGlzIGFzc3VtcHRpb24sXHJcbiAgICAgKiBzYW1wbGUtcmF0ZSBjb252ZXJ0ZXJzIG9yIFwidmFyaXNwZWVkXCIgcHJvY2Vzc29ycyBhcmUgbm90IHN1cHBvcnRlZFxyXG4gICAgICogaW4gcmVhbC10aW1lIHByb2Nlc3NpbmcuXHJcbiAgICAgKiBUaGUgc2FtcGxlUmF0ZSBwYXJhbWV0ZXIgZGVzY3JpYmVzIHRoZSBzYW1wbGUtcmF0ZSBvZiB0aGVcclxuICAgICAqIGxpbmVhciBQQ00gYXVkaW8gZGF0YSBpbiB0aGUgYnVmZmVyIGluIHNhbXBsZS1mcmFtZXMgcGVyIHNlY29uZC5cclxuICAgICAqIEFuIGltcGxlbWVudGF0aW9uIG11c3Qgc3VwcG9ydCBzYW1wbGUtcmF0ZXMgaW4gYXQgbGVhc3RcclxuICAgICAqIHRoZSByYW5nZSAyMjA1MCB0byA5NjAwMC5cclxuICAgICAqIEBwcm9wZXJ0eSB7bnVtYmVyfSBzYW1wbGVSYXRlIC0gQnVmZmVyLXNpemUgZm9yIGhvdyBmcmVxdWVudGx5IHRoZSBhdWRpb3Byb2Nlc3MgZXZlbnQgaXMgZGlzcGF0Y2hlZC5cclxuICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIgPSBuZXcgU3RlcmVvQXVkaW9SZWNvcmRlcihtZWRpYVN0cmVhbSwge1xyXG4gICAgICogICAgIHNhbXBsZVJhdGU6IDQ0MTAwXHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgdmFyIHNhbXBsZVJhdGUgPSB0eXBlb2YgY29uZmlnLnNhbXBsZVJhdGUgIT09ICd1bmRlZmluZWQnID8gY29uZmlnLnNhbXBsZVJhdGUgOiBjb250ZXh0LnNhbXBsZVJhdGUgfHwgNDQxMDA7XHJcblxyXG4gICAgaWYgKHNhbXBsZVJhdGUgPCAyMjA1MCB8fCBzYW1wbGVSYXRlID4gOTYwMDApIHtcclxuICAgICAgICAvLyBSZWY6IGh0dHA6Ly9zdGFja292ZXJmbG93LmNvbS9hLzI2MzAzOTE4LzU1MjE4MlxyXG4gICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCdzYW1wbGUtcmF0ZSBtdXN0IGJlIHVuZGVyIHJhbmdlIDIyMDUwIGFuZCA5NjAwMC4nKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICBpZiAoY29uZmlnLmRlc2lyZWRTYW1wUmF0ZSkge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZygnRGVzaXJlZCBzYW1wbGUtcmF0ZTogJyArIGNvbmZpZy5kZXNpcmVkU2FtcFJhdGUpO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICB2YXIgaXNQYXVzZWQgPSBmYWxzZTtcclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcGF1c2VzIHRoZSByZWNvcmRpbmcgcHJvY2Vzcy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucGF1c2UoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5wYXVzZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlzUGF1c2VkID0gdHJ1ZTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXN1bWVzIHRoZSByZWNvcmRpbmcgcHJvY2Vzcy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBTdGVyZW9BdWRpb1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucmVzdW1lKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucmVzdW1lID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaWYgKGlzTWVkaWFTdHJlYW1BY3RpdmUoKSA9PT0gZmFsc2UpIHtcclxuICAgICAgICAgICAgdGhyb3cgJ1BsZWFzZSBtYWtlIHN1cmUgTWVkaWFTdHJlYW0gaXMgYWN0aXZlLic7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoIXJlY29yZGluZykge1xyXG4gICAgICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICAgICAgY29uc29sZS5sb2coJ1NlZW1zIHJlY29yZGluZyBoYXMgYmVlbiByZXN0YXJ0ZWQuJyk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgdGhpcy5yZWNvcmQoKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaXNQYXVzZWQgPSBmYWxzZTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXNldHMgY3VycmVudGx5IHJlY29yZGVkIGRhdGEuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgU3RlcmVvQXVkaW9SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLmNsZWFyUmVjb3JkZWREYXRhKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuY2xlYXJSZWNvcmRlZERhdGEgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBjb25maWcuY2hlY2tGb3JJbmFjdGl2ZVRyYWNrcyA9IGZhbHNlO1xyXG5cclxuICAgICAgICBpZiAocmVjb3JkaW5nKSB7XHJcbiAgICAgICAgICAgIHRoaXMuc3RvcChjbGVhclJlY29yZGVkRGF0YUNCKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGNsZWFyUmVjb3JkZWREYXRhQ0IoKTtcclxuICAgIH07XHJcblxyXG4gICAgZnVuY3Rpb24gcmVzZXRWYXJpYWJsZXMoKSB7XHJcbiAgICAgICAgbGVmdGNoYW5uZWwgPSBbXTtcclxuICAgICAgICByaWdodGNoYW5uZWwgPSBbXTtcclxuICAgICAgICByZWNvcmRpbmdMZW5ndGggPSAwO1xyXG4gICAgICAgIGlzQXVkaW9Qcm9jZXNzU3RhcnRlZCA9IGZhbHNlO1xyXG4gICAgICAgIHJlY29yZGluZyA9IGZhbHNlO1xyXG4gICAgICAgIGlzUGF1c2VkID0gZmFsc2U7XHJcbiAgICAgICAgY29udGV4dCA9IG51bGw7XHJcblxyXG4gICAgICAgIHNlbGYubGVmdGNoYW5uZWwgPSBsZWZ0Y2hhbm5lbDtcclxuICAgICAgICBzZWxmLnJpZ2h0Y2hhbm5lbCA9IHJpZ2h0Y2hhbm5lbDtcclxuICAgICAgICBzZWxmLm51bWJlck9mQXVkaW9DaGFubmVscyA9IG51bWJlck9mQXVkaW9DaGFubmVscztcclxuICAgICAgICBzZWxmLmRlc2lyZWRTYW1wUmF0ZSA9IGRlc2lyZWRTYW1wUmF0ZTtcclxuICAgICAgICBzZWxmLnNhbXBsZVJhdGUgPSBzYW1wbGVSYXRlO1xyXG4gICAgICAgIHNlbGYucmVjb3JkaW5nTGVuZ3RoID0gcmVjb3JkaW5nTGVuZ3RoO1xyXG5cclxuICAgICAgICBpbnRlcnZhbHNCYXNlZEJ1ZmZlcnMgPSB7XHJcbiAgICAgICAgICAgIGxlZnQ6IFtdLFxyXG4gICAgICAgICAgICByaWdodDogW10sXHJcbiAgICAgICAgICAgIHJlY29yZGluZ0xlbmd0aDogMFxyXG4gICAgICAgIH07XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gY2xlYXJSZWNvcmRlZERhdGFDQigpIHtcclxuICAgICAgICBpZiAoanNBdWRpb05vZGUpIHtcclxuICAgICAgICAgICAganNBdWRpb05vZGUub25hdWRpb3Byb2Nlc3MgPSBudWxsO1xyXG4gICAgICAgICAgICBqc0F1ZGlvTm9kZS5kaXNjb25uZWN0KCk7XHJcbiAgICAgICAgICAgIGpzQXVkaW9Ob2RlID0gbnVsbDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChhdWRpb0lucHV0KSB7XHJcbiAgICAgICAgICAgIGF1ZGlvSW5wdXQuZGlzY29ubmVjdCgpO1xyXG4gICAgICAgICAgICBhdWRpb0lucHV0ID0gbnVsbDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHJlc2V0VmFyaWFibGVzKCk7XHJcbiAgICB9XHJcblxyXG4gICAgLy8gZm9yIGRlYnVnZ2luZ1xyXG4gICAgdGhpcy5uYW1lID0gJ1N0ZXJlb0F1ZGlvUmVjb3JkZXInO1xyXG4gICAgdGhpcy50b1N0cmluZyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiB0aGlzLm5hbWU7XHJcbiAgICB9O1xyXG5cclxuICAgIHZhciBpc0F1ZGlvUHJvY2Vzc1N0YXJ0ZWQgPSBmYWxzZTtcclxuXHJcbiAgICBmdW5jdGlvbiBvbkF1ZGlvUHJvY2Vzc0RhdGFBdmFpbGFibGUoZSkge1xyXG4gICAgICAgIGlmIChpc1BhdXNlZCkge1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoaXNNZWRpYVN0cmVhbUFjdGl2ZSgpID09PSBmYWxzZSkge1xyXG4gICAgICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICAgICAgY29uc29sZS5sb2coJ01lZGlhU3RyZWFtIHNlZW1zIHN0b3BwZWQuJyk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAganNBdWRpb05vZGUuZGlzY29ubmVjdCgpO1xyXG4gICAgICAgICAgICByZWNvcmRpbmcgPSBmYWxzZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghcmVjb3JkaW5nKSB7XHJcbiAgICAgICAgICAgIGlmIChhdWRpb0lucHV0KSB7XHJcbiAgICAgICAgICAgICAgICBhdWRpb0lucHV0LmRpc2Nvbm5lY3QoKTtcclxuICAgICAgICAgICAgICAgIGF1ZGlvSW5wdXQgPSBudWxsO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIC8qKlxyXG4gICAgICAgICAqIFRoaXMgbWV0aG9kIGlzIGNhbGxlZCBvbiBcIm9uYXVkaW9wcm9jZXNzXCIgZXZlbnQncyBmaXJzdCBpbnZvY2F0aW9uLlxyXG4gICAgICAgICAqIEBtZXRob2Qge2Z1bmN0aW9ufSBvbkF1ZGlvUHJvY2Vzc1N0YXJ0ZWRcclxuICAgICAgICAgKiBAbWVtYmVyb2YgU3RlcmVvQXVkaW9SZWNvcmRlclxyXG4gICAgICAgICAqIEBleGFtcGxlXHJcbiAgICAgICAgICogcmVjb3JkZXIub25BdWRpb1Byb2Nlc3NTdGFydGVkOiBmdW5jdGlvbigpIHsgfTtcclxuICAgICAgICAgKi9cclxuICAgICAgICBpZiAoIWlzQXVkaW9Qcm9jZXNzU3RhcnRlZCkge1xyXG4gICAgICAgICAgICBpc0F1ZGlvUHJvY2Vzc1N0YXJ0ZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICBpZiAoY29uZmlnLm9uQXVkaW9Qcm9jZXNzU3RhcnRlZCkge1xyXG4gICAgICAgICAgICAgICAgY29uZmlnLm9uQXVkaW9Qcm9jZXNzU3RhcnRlZCgpO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoY29uZmlnLmluaXRDYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgY29uZmlnLmluaXRDYWxsYmFjaygpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICB2YXIgbGVmdCA9IGUuaW5wdXRCdWZmZXIuZ2V0Q2hhbm5lbERhdGEoMCk7XHJcblxyXG4gICAgICAgIC8vIHdlIGNsb25lIHRoZSBzYW1wbGVzXHJcbiAgICAgICAgdmFyIGNoTGVmdCA9IG5ldyBGbG9hdDMyQXJyYXkobGVmdCk7XHJcbiAgICAgICAgbGVmdGNoYW5uZWwucHVzaChjaExlZnQpO1xyXG5cclxuICAgICAgICBpZiAobnVtYmVyT2ZBdWRpb0NoYW5uZWxzID09PSAyKSB7XHJcbiAgICAgICAgICAgIHZhciByaWdodCA9IGUuaW5wdXRCdWZmZXIuZ2V0Q2hhbm5lbERhdGEoMSk7XHJcbiAgICAgICAgICAgIHZhciBjaFJpZ2h0ID0gbmV3IEZsb2F0MzJBcnJheShyaWdodCk7XHJcbiAgICAgICAgICAgIHJpZ2h0Y2hhbm5lbC5wdXNoKGNoUmlnaHQpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgcmVjb3JkaW5nTGVuZ3RoICs9IGJ1ZmZlclNpemU7XHJcblxyXG4gICAgICAgIC8vIGV4cG9ydCByYXcgUENNXHJcbiAgICAgICAgc2VsZi5yZWNvcmRpbmdMZW5ndGggPSByZWNvcmRpbmdMZW5ndGg7XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgY29uZmlnLnRpbWVTbGljZSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAgICAgaW50ZXJ2YWxzQmFzZWRCdWZmZXJzLnJlY29yZGluZ0xlbmd0aCArPSBidWZmZXJTaXplO1xyXG4gICAgICAgICAgICBpbnRlcnZhbHNCYXNlZEJ1ZmZlcnMubGVmdC5wdXNoKGNoTGVmdCk7XHJcblxyXG4gICAgICAgICAgICBpZiAobnVtYmVyT2ZBdWRpb0NoYW5uZWxzID09PSAyKSB7XHJcbiAgICAgICAgICAgICAgICBpbnRlcnZhbHNCYXNlZEJ1ZmZlcnMucmlnaHQucHVzaChjaFJpZ2h0KTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBqc0F1ZGlvTm9kZS5vbmF1ZGlvcHJvY2VzcyA9IG9uQXVkaW9Qcm9jZXNzRGF0YUF2YWlsYWJsZTtcclxuXHJcbiAgICAvLyB0byBwcmV2ZW50IHNlbGYgYXVkaW8gdG8gYmUgY29ubmVjdGVkIHdpdGggc3BlYWtlcnNcclxuICAgIGlmIChjb250ZXh0LmNyZWF0ZU1lZGlhU3RyZWFtRGVzdGluYXRpb24pIHtcclxuICAgICAgICBqc0F1ZGlvTm9kZS5jb25uZWN0KGNvbnRleHQuY3JlYXRlTWVkaWFTdHJlYW1EZXN0aW5hdGlvbigpKTtcclxuICAgIH0gZWxzZSB7XHJcbiAgICAgICAganNBdWRpb05vZGUuY29ubmVjdChjb250ZXh0LmRlc3RpbmF0aW9uKTtcclxuICAgIH1cclxuXHJcbiAgICAvLyBleHBvcnQgcmF3IFBDTVxyXG4gICAgdGhpcy5sZWZ0Y2hhbm5lbCA9IGxlZnRjaGFubmVsO1xyXG4gICAgdGhpcy5yaWdodGNoYW5uZWwgPSByaWdodGNoYW5uZWw7XHJcbiAgICB0aGlzLm51bWJlck9mQXVkaW9DaGFubmVscyA9IG51bWJlck9mQXVkaW9DaGFubmVscztcclxuICAgIHRoaXMuZGVzaXJlZFNhbXBSYXRlID0gZGVzaXJlZFNhbXBSYXRlO1xyXG4gICAgdGhpcy5zYW1wbGVSYXRlID0gc2FtcGxlUmF0ZTtcclxuICAgIHNlbGYucmVjb3JkaW5nTGVuZ3RoID0gcmVjb3JkaW5nTGVuZ3RoO1xyXG5cclxuICAgIC8vIGhlbHBlciBmb3IgaW50ZXJ2YWxzIGJhc2VkIGJsb2JzXHJcbiAgICB2YXIgaW50ZXJ2YWxzQmFzZWRCdWZmZXJzID0ge1xyXG4gICAgICAgIGxlZnQ6IFtdLFxyXG4gICAgICAgIHJpZ2h0OiBbXSxcclxuICAgICAgICByZWNvcmRpbmdMZW5ndGg6IDBcclxuICAgIH07XHJcblxyXG4gICAgLy8gdGhpcyBsb29wZXIgaXMgdXNlZCB0byBzdXBwb3J0IGludGVydmFscyBiYXNlZCBibG9icyAodmlhIHRpbWVTbGljZStvbmRhdGFhdmFpbGFibGUpXHJcbiAgICBmdW5jdGlvbiBsb29wZXIoKSB7XHJcbiAgICAgICAgaWYgKCFyZWNvcmRpbmcgfHwgdHlwZW9mIGNvbmZpZy5vbmRhdGFhdmFpbGFibGUgIT09ICdmdW5jdGlvbicgfHwgdHlwZW9mIGNvbmZpZy50aW1lU2xpY2UgPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpbnRlcnZhbHNCYXNlZEJ1ZmZlcnMubGVmdC5sZW5ndGgpIHtcclxuICAgICAgICAgICAgbWVyZ2VMZWZ0UmlnaHRCdWZmZXJzKHtcclxuICAgICAgICAgICAgICAgIGRlc2lyZWRTYW1wUmF0ZTogZGVzaXJlZFNhbXBSYXRlLFxyXG4gICAgICAgICAgICAgICAgc2FtcGxlUmF0ZTogc2FtcGxlUmF0ZSxcclxuICAgICAgICAgICAgICAgIG51bWJlck9mQXVkaW9DaGFubmVsczogbnVtYmVyT2ZBdWRpb0NoYW5uZWxzLFxyXG4gICAgICAgICAgICAgICAgaW50ZXJuYWxJbnRlcmxlYXZlZExlbmd0aDogaW50ZXJ2YWxzQmFzZWRCdWZmZXJzLnJlY29yZGluZ0xlbmd0aCxcclxuICAgICAgICAgICAgICAgIGxlZnRCdWZmZXJzOiBpbnRlcnZhbHNCYXNlZEJ1ZmZlcnMubGVmdCxcclxuICAgICAgICAgICAgICAgIHJpZ2h0QnVmZmVyczogbnVtYmVyT2ZBdWRpb0NoYW5uZWxzID09PSAxID8gW10gOiBpbnRlcnZhbHNCYXNlZEJ1ZmZlcnMucmlnaHRcclxuICAgICAgICAgICAgfSwgZnVuY3Rpb24oYnVmZmVyLCB2aWV3KSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgYmxvYiA9IG5ldyBCbG9iKFt2aWV3XSwge1xyXG4gICAgICAgICAgICAgICAgICAgIHR5cGU6ICdhdWRpby93YXYnXHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIGNvbmZpZy5vbmRhdGFhdmFpbGFibGUoYmxvYik7XHJcblxyXG4gICAgICAgICAgICAgICAgc2V0VGltZW91dChsb29wZXIsIGNvbmZpZy50aW1lU2xpY2UpO1xyXG4gICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgICAgIGludGVydmFsc0Jhc2VkQnVmZmVycyA9IHtcclxuICAgICAgICAgICAgICAgIGxlZnQ6IFtdLFxyXG4gICAgICAgICAgICAgICAgcmlnaHQ6IFtdLFxyXG4gICAgICAgICAgICAgICAgcmVjb3JkaW5nTGVuZ3RoOiAwXHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgc2V0VGltZW91dChsb29wZXIsIGNvbmZpZy50aW1lU2xpY2UpO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxufVxyXG5cclxuaWYgKHR5cGVvZiBSZWNvcmRSVEMgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICBSZWNvcmRSVEMuU3RlcmVvQXVkaW9SZWNvcmRlciA9IFN0ZXJlb0F1ZGlvUmVjb3JkZXI7XHJcbn1cclxuXHJcbi8vIF9fX19fX19fX19fX19fX19fXHJcbi8vIENhbnZhc1JlY29yZGVyLmpzXHJcblxyXG4vKipcclxuICogQ2FudmFzUmVjb3JkZXIgaXMgYSBzdGFuZGFsb25lIGNsYXNzIHVzZWQgYnkge0BsaW5rIFJlY29yZFJUQ30gdG8gYnJpbmcgSFRNTDUtQ2FudmFzIHJlY29yZGluZyBpbnRvIHZpZGVvIFdlYk0uIEl0IHVzZXMgSFRNTDJDYW52YXMgbGlicmFyeSBhbmQgcnVucyB0b3Agb3ZlciB7QGxpbmsgV2hhbW15fS5cclxuICogQHN1bW1hcnkgSFRNTDJDYW52YXMgcmVjb3JkaW5nIGludG8gdmlkZW8gV2ViTS5cclxuICogQGxpY2Vuc2Uge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDL2Jsb2IvbWFzdGVyL0xJQ0VOU0V8TUlUfVxyXG4gKiBAYXV0aG9yIHtAbGluayBodHRwczovL011YXpLaGFuLmNvbXxNdWF6IEtoYW59XHJcbiAqIEB0eXBlZGVmIENhbnZhc1JlY29yZGVyXHJcbiAqIEBjbGFzc1xyXG4gKiBAZXhhbXBsZVxyXG4gKiB2YXIgcmVjb3JkZXIgPSBuZXcgQ2FudmFzUmVjb3JkZXIoaHRtbEVsZW1lbnQsIHsgZGlzYWJsZUxvZ3M6IHRydWUsIHVzZVdoYW1teVJlY29yZGVyOiB0cnVlIH0pO1xyXG4gKiByZWNvcmRlci5yZWNvcmQoKTtcclxuICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbihibG9iKSB7XHJcbiAqICAgICB2aWRlby5zcmMgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKGJsb2IpO1xyXG4gKiB9KTtcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKiBAcGFyYW0ge0hUTUxFbGVtZW50fSBodG1sRWxlbWVudCAtIHF1ZXJ5U2VsZWN0b3IvZ2V0RWxlbWVudEJ5SWQvZ2V0RWxlbWVudHNCeVRhZ05hbWVbMF0vZXRjLlxyXG4gKiBAcGFyYW0ge29iamVjdH0gY29uZmlnIC0ge2Rpc2FibGVMb2dzOnRydWUsIGluaXRDYWxsYmFjazogZnVuY3Rpb259XHJcbiAqL1xyXG5cclxuZnVuY3Rpb24gQ2FudmFzUmVjb3JkZXIoaHRtbEVsZW1lbnQsIGNvbmZpZykge1xyXG4gICAgaWYgKHR5cGVvZiBodG1sMmNhbnZhcyA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICB0aHJvdyAnUGxlYXNlIGxpbms6IGh0dHBzOi8vd3d3LndlYnJ0Yy1leHBlcmltZW50LmNvbS9zY3JlZW5zaG90LmpzJztcclxuICAgIH1cclxuXHJcbiAgICBjb25maWcgPSBjb25maWcgfHwge307XHJcbiAgICBpZiAoIWNvbmZpZy5mcmFtZUludGVydmFsKSB7XHJcbiAgICAgICAgY29uZmlnLmZyYW1lSW50ZXJ2YWwgPSAxMDtcclxuICAgIH1cclxuXHJcbiAgICAvLyB2aWEgRGV0ZWN0UlRDLmpzXHJcbiAgICB2YXIgaXNDYW52YXNTdXBwb3J0c1N0cmVhbUNhcHR1cmluZyA9IGZhbHNlO1xyXG4gICAgWydjYXB0dXJlU3RyZWFtJywgJ21vekNhcHR1cmVTdHJlYW0nLCAnd2Via2l0Q2FwdHVyZVN0cmVhbSddLmZvckVhY2goZnVuY3Rpb24oaXRlbSkge1xyXG4gICAgICAgIGlmIChpdGVtIGluIGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoJ2NhbnZhcycpKSB7XHJcbiAgICAgICAgICAgIGlzQ2FudmFzU3VwcG9ydHNTdHJlYW1DYXB0dXJpbmcgPSB0cnVlO1xyXG4gICAgICAgIH1cclxuICAgIH0pO1xyXG5cclxuICAgIHZhciBfaXNDaHJvbWUgPSAoISF3aW5kb3cud2Via2l0UlRDUGVlckNvbm5lY3Rpb24gfHwgISF3aW5kb3cud2Via2l0R2V0VXNlck1lZGlhKSAmJiAhIXdpbmRvdy5jaHJvbWU7XHJcblxyXG4gICAgdmFyIGNocm9tZVZlcnNpb24gPSA1MDtcclxuICAgIHZhciBtYXRjaEFycmF5ID0gbmF2aWdhdG9yLnVzZXJBZ2VudC5tYXRjaCgvQ2hyb20oZXxpdW0pXFwvKFswLTldKylcXC4vKTtcclxuICAgIGlmIChfaXNDaHJvbWUgJiYgbWF0Y2hBcnJheSAmJiBtYXRjaEFycmF5WzJdKSB7XHJcbiAgICAgICAgY2hyb21lVmVyc2lvbiA9IHBhcnNlSW50KG1hdGNoQXJyYXlbMl0sIDEwKTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoX2lzQ2hyb21lICYmIGNocm9tZVZlcnNpb24gPCA1Mikge1xyXG4gICAgICAgIGlzQ2FudmFzU3VwcG9ydHNTdHJlYW1DYXB0dXJpbmcgPSBmYWxzZTtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoY29uZmlnLnVzZVdoYW1teVJlY29yZGVyKSB7XHJcbiAgICAgICAgaXNDYW52YXNTdXBwb3J0c1N0cmVhbUNhcHR1cmluZyA9IGZhbHNlO1xyXG4gICAgfVxyXG5cclxuICAgIHZhciBnbG9iYWxDYW52YXMsIG1lZGlhU3RyZWFtUmVjb3JkZXI7XHJcblxyXG4gICAgaWYgKGlzQ2FudmFzU3VwcG9ydHNTdHJlYW1DYXB0dXJpbmcpIHtcclxuICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmxvZygnWW91ciBicm93c2VyIHN1cHBvcnRzIGJvdGggTWVkaVJlY29yZGVyIEFQSSBhbmQgY2FudmFzLmNhcHR1cmVTdHJlYW0hJyk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoaHRtbEVsZW1lbnQgaW5zdGFuY2VvZiBIVE1MQ2FudmFzRWxlbWVudCkge1xyXG4gICAgICAgICAgICBnbG9iYWxDYW52YXMgPSBodG1sRWxlbWVudDtcclxuICAgICAgICB9IGVsc2UgaWYgKGh0bWxFbGVtZW50IGluc3RhbmNlb2YgQ2FudmFzUmVuZGVyaW5nQ29udGV4dDJEKSB7XHJcbiAgICAgICAgICAgIGdsb2JhbENhbnZhcyA9IGh0bWxFbGVtZW50LmNhbnZhcztcclxuICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICB0aHJvdyAnUGxlYXNlIHBhc3MgZWl0aGVyIEhUTUxDYW52YXNFbGVtZW50IG9yIENhbnZhc1JlbmRlcmluZ0NvbnRleHQyRC4nO1xyXG4gICAgICAgIH1cclxuICAgIH0gZWxzZSBpZiAoISFuYXZpZ2F0b3IubW96R2V0VXNlck1lZGlhKSB7XHJcbiAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgY29uc29sZS5lcnJvcignQ2FudmFzIHJlY29yZGluZyBpcyBOT1Qgc3VwcG9ydGVkIGluIEZpcmVmb3guJyk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIHZhciBpc1JlY29yZGluZztcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJlY29yZHMgQ2FudmFzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIENhbnZhc1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucmVjb3JkKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucmVjb3JkID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaXNSZWNvcmRpbmcgPSB0cnVlO1xyXG5cclxuICAgICAgICBpZiAoaXNDYW52YXNTdXBwb3J0c1N0cmVhbUNhcHR1cmluZyAmJiAhY29uZmlnLnVzZVdoYW1teVJlY29yZGVyKSB7XHJcbiAgICAgICAgICAgIC8vIENhbnZhc0NhcHR1cmVNZWRpYVN0cmVhbVxyXG4gICAgICAgICAgICB2YXIgY2FudmFzTWVkaWFTdHJlYW07XHJcbiAgICAgICAgICAgIGlmICgnY2FwdHVyZVN0cmVhbScgaW4gZ2xvYmFsQ2FudmFzKSB7XHJcbiAgICAgICAgICAgICAgICBjYW52YXNNZWRpYVN0cmVhbSA9IGdsb2JhbENhbnZhcy5jYXB0dXJlU3RyZWFtKDI1KTsgLy8gMjUgRlBTXHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAoJ21vekNhcHR1cmVTdHJlYW0nIGluIGdsb2JhbENhbnZhcykge1xyXG4gICAgICAgICAgICAgICAgY2FudmFzTWVkaWFTdHJlYW0gPSBnbG9iYWxDYW52YXMubW96Q2FwdHVyZVN0cmVhbSgyNSk7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAoJ3dlYmtpdENhcHR1cmVTdHJlYW0nIGluIGdsb2JhbENhbnZhcykge1xyXG4gICAgICAgICAgICAgICAgY2FudmFzTWVkaWFTdHJlYW0gPSBnbG9iYWxDYW52YXMud2Via2l0Q2FwdHVyZVN0cmVhbSgyNSk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIHRyeSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgbWRTdHJlYW0gPSBuZXcgTWVkaWFTdHJlYW0oKTtcclxuICAgICAgICAgICAgICAgIG1kU3RyZWFtLmFkZFRyYWNrKGdldFRyYWNrcyhjYW52YXNNZWRpYVN0cmVhbSwgJ3ZpZGVvJylbMF0pO1xyXG4gICAgICAgICAgICAgICAgY2FudmFzTWVkaWFTdHJlYW0gPSBtZFN0cmVhbTtcclxuICAgICAgICAgICAgfSBjYXRjaCAoZSkge31cclxuXHJcbiAgICAgICAgICAgIGlmICghY2FudmFzTWVkaWFTdHJlYW0pIHtcclxuICAgICAgICAgICAgICAgIHRocm93ICdjYXB0dXJlU3RyZWFtIEFQSSBhcmUgTk9UIGF2YWlsYWJsZS4nO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAvLyBOb3RlOiBKYW4gMTgsIDIwMTYgc3RhdHVzIGlzIHRoYXQsIFxyXG4gICAgICAgICAgICAvLyBGaXJlZm94IE1lZGlhUmVjb3JkZXIgQVBJIGNhbid0IHJlY29yZCBDYW52YXNDYXB0dXJlTWVkaWFTdHJlYW0gb2JqZWN0LlxyXG4gICAgICAgICAgICBtZWRpYVN0cmVhbVJlY29yZGVyID0gbmV3IE1lZGlhU3RyZWFtUmVjb3JkZXIoY2FudmFzTWVkaWFTdHJlYW0sIHtcclxuICAgICAgICAgICAgICAgIG1pbWVUeXBlOiBjb25maWcubWltZVR5cGUgfHwgJ3ZpZGVvL3dlYm0nXHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICBtZWRpYVN0cmVhbVJlY29yZGVyLnJlY29yZCgpO1xyXG4gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIHdoYW1teS5mcmFtZXMgPSBbXTtcclxuICAgICAgICAgICAgbGFzdFRpbWUgPSBuZXcgRGF0ZSgpLmdldFRpbWUoKTtcclxuICAgICAgICAgICAgZHJhd0NhbnZhc0ZyYW1lKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoY29uZmlnLmluaXRDYWxsYmFjaykge1xyXG4gICAgICAgICAgICBjb25maWcuaW5pdENhbGxiYWNrKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICB0aGlzLmdldFdlYlBJbWFnZXMgPSBmdW5jdGlvbihjYWxsYmFjaykge1xyXG4gICAgICAgIGlmIChodG1sRWxlbWVudC5ub2RlTmFtZS50b0xvd2VyQ2FzZSgpICE9PSAnY2FudmFzJykge1xyXG4gICAgICAgICAgICBjYWxsYmFjaygpO1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICB2YXIgZnJhbWVzTGVuZ3RoID0gd2hhbW15LmZyYW1lcy5sZW5ndGg7XHJcbiAgICAgICAgd2hhbW15LmZyYW1lcy5mb3JFYWNoKGZ1bmN0aW9uKGZyYW1lLCBpZHgpIHtcclxuICAgICAgICAgICAgdmFyIGZyYW1lc1JlbWFpbmluZyA9IGZyYW1lc0xlbmd0aCAtIGlkeDtcclxuICAgICAgICAgICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGZyYW1lc1JlbWFpbmluZyArICcvJyArIGZyYW1lc0xlbmd0aCArICcgZnJhbWVzIHJlbWFpbmluZycpO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoY29uZmlnLm9uRW5jb2RpbmdDYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgY29uZmlnLm9uRW5jb2RpbmdDYWxsYmFjayhmcmFtZXNSZW1haW5pbmcsIGZyYW1lc0xlbmd0aCk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIHZhciB3ZWJwID0gZnJhbWUuaW1hZ2UudG9EYXRhVVJMKCdpbWFnZS93ZWJwJywgMSk7XHJcbiAgICAgICAgICAgIHdoYW1teS5mcmFtZXNbaWR4XS5pbWFnZSA9IHdlYnA7XHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCdHZW5lcmF0aW5nIFdlYk0nKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGNhbGxiYWNrKCk7XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2Qgc3RvcHMgcmVjb3JkaW5nIENhbnZhcy5cclxuICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQ2FsbGJhY2sgZnVuY3Rpb24sIHRoYXQgaXMgdXNlZCB0byBwYXNzIHJlY29yZGVkIGJsb2IgYmFjayB0byB0aGUgY2FsbGVlLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIENhbnZhc1JlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbihibG9iKSB7XHJcbiAgICAgKiAgICAgdmlkZW8uc3JjID0gVVJMLmNyZWF0ZU9iamVjdFVSTChibG9iKTtcclxuICAgICAqIH0pO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnN0b3AgPSBmdW5jdGlvbihjYWxsYmFjaykge1xyXG4gICAgICAgIGlzUmVjb3JkaW5nID0gZmFsc2U7XHJcblxyXG4gICAgICAgIHZhciB0aGF0ID0gdGhpcztcclxuXHJcbiAgICAgICAgaWYgKGlzQ2FudmFzU3VwcG9ydHNTdHJlYW1DYXB0dXJpbmcgJiYgbWVkaWFTdHJlYW1SZWNvcmRlcikge1xyXG4gICAgICAgICAgICBtZWRpYVN0cmVhbVJlY29yZGVyLnN0b3AoY2FsbGJhY2spO1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICB0aGlzLmdldFdlYlBJbWFnZXMoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgIC8qKlxyXG4gICAgICAgICAgICAgKiBAcHJvcGVydHkge0Jsb2J9IGJsb2IgLSBSZWNvcmRlZCBmcmFtZXMgaW4gdmlkZW8vd2VibSBibG9iLlxyXG4gICAgICAgICAgICAgKiBAbWVtYmVyb2YgQ2FudmFzUmVjb3JkZXJcclxuICAgICAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgICAgICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICogICAgIHZhciBibG9iID0gcmVjb3JkZXIuYmxvYjtcclxuICAgICAgICAgICAgICogfSk7XHJcbiAgICAgICAgICAgICAqL1xyXG4gICAgICAgICAgICB3aGFtbXkuY29tcGlsZShmdW5jdGlvbihibG9iKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAoIWNvbmZpZy5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKCdSZWNvcmRpbmcgZmluaXNoZWQhJyk7XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgdGhhdC5ibG9iID0gYmxvYjtcclxuXHJcbiAgICAgICAgICAgICAgICBpZiAodGhhdC5ibG9iLmZvckVhY2gpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGF0LmJsb2IgPSBuZXcgQmxvYihbXSwge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0eXBlOiAndmlkZW8vd2VibSdcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgICAgICBpZiAoY2FsbGJhY2spIHtcclxuICAgICAgICAgICAgICAgICAgICBjYWxsYmFjayh0aGF0LmJsb2IpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIHdoYW1teS5mcmFtZXMgPSBbXTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfSk7XHJcbiAgICB9O1xyXG5cclxuICAgIHZhciBpc1BhdXNlZFJlY29yZGluZyA9IGZhbHNlO1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcGF1c2VzIHRoZSByZWNvcmRpbmcgcHJvY2Vzcy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBDYW52YXNSZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnBhdXNlKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucGF1c2UgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpc1BhdXNlZFJlY29yZGluZyA9IHRydWU7XHJcblxyXG4gICAgICAgIGlmIChtZWRpYVN0cmVhbVJlY29yZGVyIGluc3RhbmNlb2YgTWVkaWFTdHJlYW1SZWNvcmRlcikge1xyXG4gICAgICAgICAgICBtZWRpYVN0cmVhbVJlY29yZGVyLnBhdXNlKCk7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVzdW1lcyB0aGUgcmVjb3JkaW5nIHByb2Nlc3MuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgQ2FudmFzUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5yZXN1bWUoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5yZXN1bWUgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpc1BhdXNlZFJlY29yZGluZyA9IGZhbHNlO1xyXG5cclxuICAgICAgICBpZiAobWVkaWFTdHJlYW1SZWNvcmRlciBpbnN0YW5jZW9mIE1lZGlhU3RyZWFtUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgbWVkaWFTdHJlYW1SZWNvcmRlci5yZXN1bWUoKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFpc1JlY29yZGluZykge1xyXG4gICAgICAgICAgICB0aGlzLnJlY29yZCgpO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXNldHMgY3VycmVudGx5IHJlY29yZGVkIGRhdGEuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgQ2FudmFzUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5jbGVhclJlY29yZGVkRGF0YSgpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLmNsZWFyUmVjb3JkZWREYXRhID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaWYgKGlzUmVjb3JkaW5nKSB7XHJcbiAgICAgICAgICAgIHRoaXMuc3RvcChjbGVhclJlY29yZGVkRGF0YUNCKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgY2xlYXJSZWNvcmRlZERhdGFDQigpO1xyXG4gICAgfTtcclxuXHJcbiAgICBmdW5jdGlvbiBjbGVhclJlY29yZGVkRGF0YUNCKCkge1xyXG4gICAgICAgIHdoYW1teS5mcmFtZXMgPSBbXTtcclxuICAgICAgICBpc1JlY29yZGluZyA9IGZhbHNlO1xyXG4gICAgICAgIGlzUGF1c2VkUmVjb3JkaW5nID0gZmFsc2U7XHJcbiAgICB9XHJcblxyXG4gICAgLy8gZm9yIGRlYnVnZ2luZ1xyXG4gICAgdGhpcy5uYW1lID0gJ0NhbnZhc1JlY29yZGVyJztcclxuICAgIHRoaXMudG9TdHJpbmcgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICByZXR1cm4gdGhpcy5uYW1lO1xyXG4gICAgfTtcclxuXHJcbiAgICBmdW5jdGlvbiBjbG9uZUNhbnZhcygpIHtcclxuICAgICAgICAvL2NyZWF0ZSBhIG5ldyBjYW52YXNcclxuICAgICAgICB2YXIgbmV3Q2FudmFzID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnY2FudmFzJyk7XHJcbiAgICAgICAgdmFyIGNvbnRleHQgPSBuZXdDYW52YXMuZ2V0Q29udGV4dCgnMmQnKTtcclxuXHJcbiAgICAgICAgLy9zZXQgZGltZW5zaW9uc1xyXG4gICAgICAgIG5ld0NhbnZhcy53aWR0aCA9IGh0bWxFbGVtZW50LndpZHRoO1xyXG4gICAgICAgIG5ld0NhbnZhcy5oZWlnaHQgPSBodG1sRWxlbWVudC5oZWlnaHQ7XHJcblxyXG4gICAgICAgIC8vYXBwbHkgdGhlIG9sZCBjYW52YXMgdG8gdGhlIG5ldyBvbmVcclxuICAgICAgICBjb250ZXh0LmRyYXdJbWFnZShodG1sRWxlbWVudCwgMCwgMCk7XHJcblxyXG4gICAgICAgIC8vcmV0dXJuIHRoZSBuZXcgY2FudmFzXHJcbiAgICAgICAgcmV0dXJuIG5ld0NhbnZhcztcclxuICAgIH1cclxuXHJcbiAgICBmdW5jdGlvbiBkcmF3Q2FudmFzRnJhbWUoKSB7XHJcbiAgICAgICAgaWYgKGlzUGF1c2VkUmVjb3JkaW5nKSB7XHJcbiAgICAgICAgICAgIGxhc3RUaW1lID0gbmV3IERhdGUoKS5nZXRUaW1lKCk7XHJcbiAgICAgICAgICAgIHJldHVybiBzZXRUaW1lb3V0KGRyYXdDYW52YXNGcmFtZSwgNTAwKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChodG1sRWxlbWVudC5ub2RlTmFtZS50b0xvd2VyQ2FzZSgpID09PSAnY2FudmFzJykge1xyXG4gICAgICAgICAgICB2YXIgZHVyYXRpb24gPSBuZXcgRGF0ZSgpLmdldFRpbWUoKSAtIGxhc3RUaW1lO1xyXG4gICAgICAgICAgICAvLyB2aWEgIzIwNiwgYnkgSmFjayBpLmUuIEBTZXltb3VyclxyXG4gICAgICAgICAgICBsYXN0VGltZSA9IG5ldyBEYXRlKCkuZ2V0VGltZSgpO1xyXG5cclxuICAgICAgICAgICAgd2hhbW15LmZyYW1lcy5wdXNoKHtcclxuICAgICAgICAgICAgICAgIGltYWdlOiBjbG9uZUNhbnZhcygpLFxyXG4gICAgICAgICAgICAgICAgZHVyYXRpb246IGR1cmF0aW9uXHJcbiAgICAgICAgICAgIH0pO1xyXG5cclxuICAgICAgICAgICAgaWYgKGlzUmVjb3JkaW5nKSB7XHJcbiAgICAgICAgICAgICAgICBzZXRUaW1lb3V0KGRyYXdDYW52YXNGcmFtZSwgY29uZmlnLmZyYW1lSW50ZXJ2YWwpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGh0bWwyY2FudmFzKGh0bWxFbGVtZW50LCB7XHJcbiAgICAgICAgICAgIGdyYWJNb3VzZTogdHlwZW9mIGNvbmZpZy5zaG93TW91c2VQb2ludGVyID09PSAndW5kZWZpbmVkJyB8fCBjb25maWcuc2hvd01vdXNlUG9pbnRlcixcclxuICAgICAgICAgICAgb25yZW5kZXJlZDogZnVuY3Rpb24oY2FudmFzKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgZHVyYXRpb24gPSBuZXcgRGF0ZSgpLmdldFRpbWUoKSAtIGxhc3RUaW1lO1xyXG4gICAgICAgICAgICAgICAgaWYgKCFkdXJhdGlvbikge1xyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiBzZXRUaW1lb3V0KGRyYXdDYW52YXNGcmFtZSwgY29uZmlnLmZyYW1lSW50ZXJ2YWwpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIC8vIHZpYSAjMjA2LCBieSBKYWNrIGkuZS4gQFNleW1vdXJyXHJcbiAgICAgICAgICAgICAgICBsYXN0VGltZSA9IG5ldyBEYXRlKCkuZ2V0VGltZSgpO1xyXG5cclxuICAgICAgICAgICAgICAgIHdoYW1teS5mcmFtZXMucHVzaCh7XHJcbiAgICAgICAgICAgICAgICAgICAgaW1hZ2U6IGNhbnZhcy50b0RhdGFVUkwoJ2ltYWdlL3dlYnAnLCAxKSxcclxuICAgICAgICAgICAgICAgICAgICBkdXJhdGlvbjogZHVyYXRpb25cclxuICAgICAgICAgICAgICAgIH0pO1xyXG5cclxuICAgICAgICAgICAgICAgIGlmIChpc1JlY29yZGluZykge1xyXG4gICAgICAgICAgICAgICAgICAgIHNldFRpbWVvdXQoZHJhd0NhbnZhc0ZyYW1lLCBjb25maWcuZnJhbWVJbnRlcnZhbCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgIH1cclxuXHJcbiAgICB2YXIgbGFzdFRpbWUgPSBuZXcgRGF0ZSgpLmdldFRpbWUoKTtcclxuXHJcbiAgICB2YXIgd2hhbW15ID0gbmV3IFdoYW1teS5WaWRlbygxMDApO1xyXG59XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIFJlY29yZFJUQy5DYW52YXNSZWNvcmRlciA9IENhbnZhc1JlY29yZGVyO1xyXG59XG5cclxuLy8gX19fX19fX19fX19fX19fX19cclxuLy8gV2hhbW15UmVjb3JkZXIuanNcclxuXHJcbi8qKlxyXG4gKiBXaGFtbXlSZWNvcmRlciBpcyBhIHN0YW5kYWxvbmUgY2xhc3MgdXNlZCBieSB7QGxpbmsgUmVjb3JkUlRDfSB0byBicmluZyB2aWRlbyByZWNvcmRpbmcgaW4gQ2hyb21lLiBJdCBydW5zIHRvcCBvdmVyIHtAbGluayBXaGFtbXl9LlxyXG4gKiBAc3VtbWFyeSBWaWRlbyByZWNvcmRpbmcgZmVhdHVyZSBpbiBDaHJvbWUuXHJcbiAqIEBsaWNlbnNlIHtAbGluayBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQy9ibG9iL21hc3Rlci9MSUNFTlNFfE1JVH1cclxuICogQGF1dGhvciB7QGxpbmsgaHR0cHM6Ly9NdWF6S2hhbi5jb218TXVheiBLaGFufVxyXG4gKiBAdHlwZWRlZiBXaGFtbXlSZWNvcmRlclxyXG4gKiBAY2xhc3NcclxuICogQGV4YW1wbGVcclxuICogdmFyIHJlY29yZGVyID0gbmV3IFdoYW1teVJlY29yZGVyKG1lZGlhU3RyZWFtKTtcclxuICogcmVjb3JkZXIucmVjb3JkKCk7XHJcbiAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oYmxvYikge1xyXG4gKiAgICAgdmlkZW8uc3JjID0gVVJMLmNyZWF0ZU9iamVjdFVSTChibG9iKTtcclxuICogfSk7XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICogQHBhcmFtIHtNZWRpYVN0cmVhbX0gbWVkaWFTdHJlYW0gLSBNZWRpYVN0cmVhbSBvYmplY3QgZmV0Y2hlZCB1c2luZyBnZXRVc2VyTWVkaWEgQVBJIG9yIGdlbmVyYXRlZCB1c2luZyBjYXB0dXJlU3RyZWFtVW50aWxFbmRlZCBvciBXZWJBdWRpbyBBUEkuXHJcbiAqIEBwYXJhbSB7b2JqZWN0fSBjb25maWcgLSB7ZGlzYWJsZUxvZ3M6IHRydWUsIGluaXRDYWxsYmFjazogZnVuY3Rpb24sIHZpZGVvOiBIVE1MVmlkZW9FbGVtZW50LCBldGMufVxyXG4gKi9cclxuXHJcbmZ1bmN0aW9uIFdoYW1teVJlY29yZGVyKG1lZGlhU3RyZWFtLCBjb25maWcpIHtcclxuXHJcbiAgICBjb25maWcgPSBjb25maWcgfHwge307XHJcblxyXG4gICAgaWYgKCFjb25maWcuZnJhbWVJbnRlcnZhbCkge1xyXG4gICAgICAgIGNvbmZpZy5mcmFtZUludGVydmFsID0gMTA7XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKCFjb25maWcuZGlzYWJsZUxvZ3MpIHtcclxuICAgICAgICBjb25zb2xlLmxvZygnVXNpbmcgZnJhbWVzLWludGVydmFsOicsIGNvbmZpZy5mcmFtZUludGVydmFsKTtcclxuICAgIH1cclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJlY29yZHMgdmlkZW8uXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgV2hhbW15UmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5yZWNvcmQoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5yZWNvcmQgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpZiAoIWNvbmZpZy53aWR0aCkge1xyXG4gICAgICAgICAgICBjb25maWcud2lkdGggPSAzMjA7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoIWNvbmZpZy5oZWlnaHQpIHtcclxuICAgICAgICAgICAgY29uZmlnLmhlaWdodCA9IDI0MDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghY29uZmlnLnZpZGVvKSB7XHJcbiAgICAgICAgICAgIGNvbmZpZy52aWRlbyA9IHtcclxuICAgICAgICAgICAgICAgIHdpZHRoOiBjb25maWcud2lkdGgsXHJcbiAgICAgICAgICAgICAgICBoZWlnaHQ6IGNvbmZpZy5oZWlnaHRcclxuICAgICAgICAgICAgfTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghY29uZmlnLmNhbnZhcykge1xyXG4gICAgICAgICAgICBjb25maWcuY2FudmFzID0ge1xyXG4gICAgICAgICAgICAgICAgd2lkdGg6IGNvbmZpZy53aWR0aCxcclxuICAgICAgICAgICAgICAgIGhlaWdodDogY29uZmlnLmhlaWdodFxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgY2FudmFzLndpZHRoID0gY29uZmlnLmNhbnZhcy53aWR0aCB8fCAzMjA7XHJcbiAgICAgICAgY2FudmFzLmhlaWdodCA9IGNvbmZpZy5jYW52YXMuaGVpZ2h0IHx8IDI0MDtcclxuXHJcbiAgICAgICAgY29udGV4dCA9IGNhbnZhcy5nZXRDb250ZXh0KCcyZCcpO1xyXG5cclxuICAgICAgICAvLyBzZXR0aW5nIGRlZmF1bHRzXHJcbiAgICAgICAgaWYgKGNvbmZpZy52aWRlbyAmJiBjb25maWcudmlkZW8gaW5zdGFuY2VvZiBIVE1MVmlkZW9FbGVtZW50KSB7XHJcbiAgICAgICAgICAgIHZpZGVvID0gY29uZmlnLnZpZGVvLmNsb25lTm9kZSgpO1xyXG5cclxuICAgICAgICAgICAgaWYgKGNvbmZpZy5pbml0Q2FsbGJhY2spIHtcclxuICAgICAgICAgICAgICAgIGNvbmZpZy5pbml0Q2FsbGJhY2soKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIHZpZGVvID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgndmlkZW8nKTtcclxuXHJcbiAgICAgICAgICAgIHNldFNyY09iamVjdChtZWRpYVN0cmVhbSwgdmlkZW8pO1xyXG5cclxuICAgICAgICAgICAgdmlkZW8ub25sb2FkZWRtZXRhZGF0YSA9IGZ1bmN0aW9uKCkgeyAvLyBcIm9ubG9hZGVkbWV0YWRhdGFcIiBtYXkgTk9UIHdvcmsgaW4gRkY/XHJcbiAgICAgICAgICAgICAgICBpZiAoY29uZmlnLmluaXRDYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbmZpZy5pbml0Q2FsbGJhY2soKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfTtcclxuXHJcbiAgICAgICAgICAgIHZpZGVvLndpZHRoID0gY29uZmlnLnZpZGVvLndpZHRoO1xyXG4gICAgICAgICAgICB2aWRlby5oZWlnaHQgPSBjb25maWcudmlkZW8uaGVpZ2h0O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgdmlkZW8ubXV0ZWQgPSB0cnVlO1xyXG4gICAgICAgIHZpZGVvLnBsYXkoKTtcclxuXHJcbiAgICAgICAgbGFzdFRpbWUgPSBuZXcgRGF0ZSgpLmdldFRpbWUoKTtcclxuICAgICAgICB3aGFtbXkgPSBuZXcgV2hhbW15LlZpZGVvKCk7XHJcblxyXG4gICAgICAgIGlmICghY29uZmlnLmRpc2FibGVMb2dzKSB7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCdjYW52YXMgcmVzb2x1dGlvbnMnLCBjYW52YXMud2lkdGgsICcqJywgY2FudmFzLmhlaWdodCk7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCd2aWRlbyB3aWR0aC9oZWlnaHQnLCB2aWRlby53aWR0aCB8fCBjYW52YXMud2lkdGgsICcqJywgdmlkZW8uaGVpZ2h0IHx8IGNhbnZhcy5oZWlnaHQpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZHJhd0ZyYW1lcyhjb25maWcuZnJhbWVJbnRlcnZhbCk7XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogRHJhdyBhbmQgcHVzaCBmcmFtZXMgdG8gV2hhbW15XHJcbiAgICAgKiBAcGFyYW0ge2ludGVnZXJ9IGZyYW1lSW50ZXJ2YWwgLSBzZXQgbWluaW11bSBpbnRlcnZhbCAoaW4gbWlsbGlzZWNvbmRzKSBiZXR3ZWVuIGVhY2ggdGltZSB3ZSBwdXNoIGEgZnJhbWUgdG8gV2hhbW15XHJcbiAgICAgKi9cclxuICAgIGZ1bmN0aW9uIGRyYXdGcmFtZXMoZnJhbWVJbnRlcnZhbCkge1xyXG4gICAgICAgIGZyYW1lSW50ZXJ2YWwgPSB0eXBlb2YgZnJhbWVJbnRlcnZhbCAhPT0gJ3VuZGVmaW5lZCcgPyBmcmFtZUludGVydmFsIDogMTA7XHJcblxyXG4gICAgICAgIHZhciBkdXJhdGlvbiA9IG5ldyBEYXRlKCkuZ2V0VGltZSgpIC0gbGFzdFRpbWU7XHJcbiAgICAgICAgaWYgKCFkdXJhdGlvbikge1xyXG4gICAgICAgICAgICByZXR1cm4gc2V0VGltZW91dChkcmF3RnJhbWVzLCBmcmFtZUludGVydmFsLCBmcmFtZUludGVydmFsKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpc1BhdXNlZFJlY29yZGluZykge1xyXG4gICAgICAgICAgICBsYXN0VGltZSA9IG5ldyBEYXRlKCkuZ2V0VGltZSgpO1xyXG4gICAgICAgICAgICByZXR1cm4gc2V0VGltZW91dChkcmF3RnJhbWVzLCAxMDApO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgLy8gdmlhICMyMDYsIGJ5IEphY2sgaS5lLiBAU2V5bW91cnJcclxuICAgICAgICBsYXN0VGltZSA9IG5ldyBEYXRlKCkuZ2V0VGltZSgpO1xyXG5cclxuICAgICAgICBpZiAodmlkZW8ucGF1c2VkKSB7XHJcbiAgICAgICAgICAgIC8vIHZpYTogaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9XZWJSVEMtRXhwZXJpbWVudC9wdWxsLzMxNlxyXG4gICAgICAgICAgICAvLyBUd2VhayBmb3IgQW5kcm9pZCBDaHJvbWVcclxuICAgICAgICAgICAgdmlkZW8ucGxheSgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgY29udGV4dC5kcmF3SW1hZ2UodmlkZW8sIDAsIDAsIGNhbnZhcy53aWR0aCwgY2FudmFzLmhlaWdodCk7XHJcbiAgICAgICAgd2hhbW15LmZyYW1lcy5wdXNoKHtcclxuICAgICAgICAgICAgZHVyYXRpb246IGR1cmF0aW9uLFxyXG4gICAgICAgICAgICBpbWFnZTogY2FudmFzLnRvRGF0YVVSTCgnaW1hZ2Uvd2VicCcpXHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIGlmICghaXNTdG9wRHJhd2luZykge1xyXG4gICAgICAgICAgICBzZXRUaW1lb3V0KGRyYXdGcmFtZXMsIGZyYW1lSW50ZXJ2YWwsIGZyYW1lSW50ZXJ2YWwpO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBmdW5jdGlvbiBhc3luY0xvb3Aobykge1xyXG4gICAgICAgIHZhciBpID0gLTEsXHJcbiAgICAgICAgICAgIGxlbmd0aCA9IG8ubGVuZ3RoO1xyXG5cclxuICAgICAgICAoZnVuY3Rpb24gbG9vcCgpIHtcclxuICAgICAgICAgICAgaSsrO1xyXG4gICAgICAgICAgICBpZiAoaSA9PT0gbGVuZ3RoKSB7XHJcbiAgICAgICAgICAgICAgICBvLmNhbGxiYWNrKCk7XHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIC8vIFwic2V0VGltZW91dFwiIGFkZGVkIGJ5IEppbSBNY0xlb2RcclxuICAgICAgICAgICAgc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIG8uZnVuY3Rpb25Ub0xvb3AobG9vcCwgaSk7XHJcbiAgICAgICAgICAgIH0sIDEpO1xyXG4gICAgICAgIH0pKCk7XHJcbiAgICB9XHJcblxyXG5cclxuICAgIC8qKlxyXG4gICAgICogcmVtb3ZlIGJsYWNrIGZyYW1lcyBmcm9tIHRoZSBiZWdpbm5pbmcgdG8gdGhlIHNwZWNpZmllZCBmcmFtZVxyXG4gICAgICogQHBhcmFtIHtBcnJheX0gX2ZyYW1lcyAtIGFycmF5IG9mIGZyYW1lcyB0byBiZSBjaGVja2VkXHJcbiAgICAgKiBAcGFyYW0ge251bWJlcn0gX2ZyYW1lc1RvQ2hlY2sgLSBudW1iZXIgb2YgZnJhbWUgdW50aWwgY2hlY2sgd2lsbCBiZSBleGVjdXRlZCAoLTEgLSB3aWxsIGRyb3AgYWxsIGZyYW1lcyB1bnRpbCBmcmFtZSBub3QgbWF0Y2hlZCB3aWxsIGJlIGZvdW5kKVxyXG4gICAgICogQHBhcmFtIHtudW1iZXJ9IF9waXhUb2xlcmFuY2UgLSAwIC0gdmVyeSBzdHJpY3QgKG9ubHkgYmxhY2sgcGl4ZWwgY29sb3IpIDsgMSAtIGFsbFxyXG4gICAgICogQHBhcmFtIHtudW1iZXJ9IF9mcmFtZVRvbGVyYW5jZSAtIDAgLSB2ZXJ5IHN0cmljdCAob25seSBibGFjayBmcmFtZSBjb2xvcikgOyAxIC0gYWxsXHJcbiAgICAgKiBAcmV0dXJucyB7QXJyYXl9IC0gYXJyYXkgb2YgZnJhbWVzXHJcbiAgICAgKi9cclxuICAgIC8vIHB1bGwjMjkzIGJ5IEB2b2xvZGFsZXhleVxyXG4gICAgZnVuY3Rpb24gZHJvcEJsYWNrRnJhbWVzKF9mcmFtZXMsIF9mcmFtZXNUb0NoZWNrLCBfcGl4VG9sZXJhbmNlLCBfZnJhbWVUb2xlcmFuY2UsIGNhbGxiYWNrKSB7XHJcbiAgICAgICAgdmFyIGxvY2FsQ2FudmFzID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnY2FudmFzJyk7XHJcbiAgICAgICAgbG9jYWxDYW52YXMud2lkdGggPSBjYW52YXMud2lkdGg7XHJcbiAgICAgICAgbG9jYWxDYW52YXMuaGVpZ2h0ID0gY2FudmFzLmhlaWdodDtcclxuICAgICAgICB2YXIgY29udGV4dDJkID0gbG9jYWxDYW52YXMuZ2V0Q29udGV4dCgnMmQnKTtcclxuICAgICAgICB2YXIgcmVzdWx0RnJhbWVzID0gW107XHJcblxyXG4gICAgICAgIHZhciBjaGVja1VudGlsTm90QmxhY2sgPSBfZnJhbWVzVG9DaGVjayA9PT0gLTE7XHJcbiAgICAgICAgdmFyIGVuZENoZWNrRnJhbWUgPSAoX2ZyYW1lc1RvQ2hlY2sgJiYgX2ZyYW1lc1RvQ2hlY2sgPiAwICYmIF9mcmFtZXNUb0NoZWNrIDw9IF9mcmFtZXMubGVuZ3RoKSA/XHJcbiAgICAgICAgICAgIF9mcmFtZXNUb0NoZWNrIDogX2ZyYW1lcy5sZW5ndGg7XHJcbiAgICAgICAgdmFyIHNhbXBsZUNvbG9yID0ge1xyXG4gICAgICAgICAgICByOiAwLFxyXG4gICAgICAgICAgICBnOiAwLFxyXG4gICAgICAgICAgICBiOiAwXHJcbiAgICAgICAgfTtcclxuICAgICAgICB2YXIgbWF4Q29sb3JEaWZmZXJlbmNlID0gTWF0aC5zcXJ0KFxyXG4gICAgICAgICAgICBNYXRoLnBvdygyNTUsIDIpICtcclxuICAgICAgICAgICAgTWF0aC5wb3coMjU1LCAyKSArXHJcbiAgICAgICAgICAgIE1hdGgucG93KDI1NSwgMilcclxuICAgICAgICApO1xyXG4gICAgICAgIHZhciBwaXhUb2xlcmFuY2UgPSBfcGl4VG9sZXJhbmNlICYmIF9waXhUb2xlcmFuY2UgPj0gMCAmJiBfcGl4VG9sZXJhbmNlIDw9IDEgPyBfcGl4VG9sZXJhbmNlIDogMDtcclxuICAgICAgICB2YXIgZnJhbWVUb2xlcmFuY2UgPSBfZnJhbWVUb2xlcmFuY2UgJiYgX2ZyYW1lVG9sZXJhbmNlID49IDAgJiYgX2ZyYW1lVG9sZXJhbmNlIDw9IDEgPyBfZnJhbWVUb2xlcmFuY2UgOiAwO1xyXG4gICAgICAgIHZhciBkb05vdENoZWNrTmV4dCA9IGZhbHNlO1xyXG5cclxuICAgICAgICBhc3luY0xvb3Aoe1xyXG4gICAgICAgICAgICBsZW5ndGg6IGVuZENoZWNrRnJhbWUsXHJcbiAgICAgICAgICAgIGZ1bmN0aW9uVG9Mb29wOiBmdW5jdGlvbihsb29wLCBmKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgbWF0Y2hQaXhDb3VudCwgZW5kUGl4Q2hlY2ssIG1heFBpeENvdW50O1xyXG5cclxuICAgICAgICAgICAgICAgIHZhciBmaW5pc2hJbWFnZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmICghZG9Ob3RDaGVja05leHQgJiYgbWF4UGl4Q291bnQgLSBtYXRjaFBpeENvdW50IDw9IG1heFBpeENvdW50ICogZnJhbWVUb2xlcmFuY2UpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgLy8gY29uc29sZS5sb2coJ3JlbW92ZWQgYmxhY2sgZnJhbWUgOiAnICsgZiArICcgOyBmcmFtZSBkdXJhdGlvbiAnICsgX2ZyYW1lc1tmXS5kdXJhdGlvbik7XHJcbiAgICAgICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgLy8gY29uc29sZS5sb2coJ2ZyYW1lIGlzIHBhc3NlZCA6ICcgKyBmKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKGNoZWNrVW50aWxOb3RCbGFjaykge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgZG9Ob3RDaGVja05leHQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJlc3VsdEZyYW1lcy5wdXNoKF9mcmFtZXNbZl0pO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICBsb29wKCk7XHJcbiAgICAgICAgICAgICAgICB9O1xyXG5cclxuICAgICAgICAgICAgICAgIGlmICghZG9Ob3RDaGVja05leHQpIHtcclxuICAgICAgICAgICAgICAgICAgICB2YXIgaW1hZ2UgPSBuZXcgSW1hZ2UoKTtcclxuICAgICAgICAgICAgICAgICAgICBpbWFnZS5vbmxvYWQgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29udGV4dDJkLmRyYXdJbWFnZShpbWFnZSwgMCwgMCwgY2FudmFzLndpZHRoLCBjYW52YXMuaGVpZ2h0KTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgdmFyIGltYWdlRGF0YSA9IGNvbnRleHQyZC5nZXRJbWFnZURhdGEoMCwgMCwgY2FudmFzLndpZHRoLCBjYW52YXMuaGVpZ2h0KTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWF0Y2hQaXhDb3VudCA9IDA7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGVuZFBpeENoZWNrID0gaW1hZ2VEYXRhLmRhdGEubGVuZ3RoO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXhQaXhDb3VudCA9IGltYWdlRGF0YS5kYXRhLmxlbmd0aCAvIDQ7XHJcblxyXG4gICAgICAgICAgICAgICAgICAgICAgICBmb3IgKHZhciBwaXggPSAwOyBwaXggPCBlbmRQaXhDaGVjazsgcGl4ICs9IDQpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHZhciBjdXJyZW50Q29sb3IgPSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgcjogaW1hZ2VEYXRhLmRhdGFbcGl4XSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBnOiBpbWFnZURhdGEuZGF0YVtwaXggKyAxXSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBiOiBpbWFnZURhdGEuZGF0YVtwaXggKyAyXVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHZhciBjb2xvckRpZmZlcmVuY2UgPSBNYXRoLnNxcnQoXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgTWF0aC5wb3coY3VycmVudENvbG9yLnIgLSBzYW1wbGVDb2xvci5yLCAyKSArXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgTWF0aC5wb3coY3VycmVudENvbG9yLmcgLSBzYW1wbGVDb2xvci5nLCAyKSArXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgTWF0aC5wb3coY3VycmVudENvbG9yLmIgLSBzYW1wbGVDb2xvci5iLCAyKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIC8vIGRpZmZlcmVuY2UgaW4gY29sb3IgaXQgaXMgZGlmZmVyZW5jZSBpbiBjb2xvciB2ZWN0b3JzIChyMSxnMSxiMSkgPD0+IChyMixnMixiMilcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGlmIChjb2xvckRpZmZlcmVuY2UgPD0gbWF4Q29sb3JEaWZmZXJlbmNlICogcGl4VG9sZXJhbmNlKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgbWF0Y2hQaXhDb3VudCsrO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGZpbmlzaEltYWdlKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgICAgICAgICBpbWFnZS5zcmMgPSBfZnJhbWVzW2ZdLmltYWdlO1xyXG4gICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICBmaW5pc2hJbWFnZSgpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9LFxyXG4gICAgICAgICAgICBjYWxsYmFjazogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICByZXN1bHRGcmFtZXMgPSByZXN1bHRGcmFtZXMuY29uY2F0KF9mcmFtZXMuc2xpY2UoZW5kQ2hlY2tGcmFtZSkpO1xyXG5cclxuICAgICAgICAgICAgICAgIGlmIChyZXN1bHRGcmFtZXMubGVuZ3RoIDw9IDApIHtcclxuICAgICAgICAgICAgICAgICAgICAvLyBhdCBsZWFzdCBvbmUgbGFzdCBmcmFtZSBzaG91bGQgYmUgYXZhaWxhYmxlIGZvciBuZXh0IG1hbmlwdWxhdGlvblxyXG4gICAgICAgICAgICAgICAgICAgIC8vIGlmIHRvdGFsIGR1cmF0aW9uIG9mIGFsbCBmcmFtZXMgd2lsbCBiZSA8IDEwMDAgdGhhbiBmZm1wZWcgZG9lc24ndCB3b3JrIHdlbGwuLi5cclxuICAgICAgICAgICAgICAgICAgICByZXN1bHRGcmFtZXMucHVzaChfZnJhbWVzW19mcmFtZXMubGVuZ3RoIC0gMV0pO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgY2FsbGJhY2socmVzdWx0RnJhbWVzKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIHZhciBpc1N0b3BEcmF3aW5nID0gZmFsc2U7XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBzdG9wcyByZWNvcmRpbmcgdmlkZW8uXHJcbiAgICAgKiBAcGFyYW0ge2Z1bmN0aW9ufSBjYWxsYmFjayAtIENhbGxiYWNrIGZ1bmN0aW9uLCB0aGF0IGlzIHVzZWQgdG8gcGFzcyByZWNvcmRlZCBibG9iIGJhY2sgdG8gdGhlIGNhbGxlZS5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBXaGFtbXlSZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oYmxvYikge1xyXG4gICAgICogICAgIHZpZGVvLnNyYyA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5zdG9wID0gZnVuY3Rpb24oY2FsbGJhY2spIHtcclxuICAgICAgICBjYWxsYmFjayA9IGNhbGxiYWNrIHx8IGZ1bmN0aW9uKCkge307XHJcblxyXG4gICAgICAgIGlzU3RvcERyYXdpbmcgPSB0cnVlO1xyXG5cclxuICAgICAgICB2YXIgX3RoaXMgPSB0aGlzO1xyXG4gICAgICAgIC8vIGFuYWx5c2Ugb2YgYWxsIGZyYW1lcyB0YWtlcyBzb21lIHRpbWUhXHJcbiAgICAgICAgc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgLy8gZS5nLiBkcm9wQmxhY2tGcmFtZXMoZnJhbWVzLCAxMCwgMSwgMSkgLSB3aWxsIGN1dCBhbGwgMTAgZnJhbWVzXHJcbiAgICAgICAgICAgIC8vIGUuZy4gZHJvcEJsYWNrRnJhbWVzKGZyYW1lcywgMTAsIDAuNSwgMC41KSAtIHdpbGwgYW5hbHlzZSAxMCBmcmFtZXNcclxuICAgICAgICAgICAgLy8gZS5nLiBkcm9wQmxhY2tGcmFtZXMoZnJhbWVzLCAxMCkgPT09IGRyb3BCbGFja0ZyYW1lcyhmcmFtZXMsIDEwLCAwLCAwKSAtIHdpbGwgYW5hbHlzZSAxMCBmcmFtZXMgd2l0aCBzdHJpY3QgYmxhY2sgY29sb3JcclxuICAgICAgICAgICAgZHJvcEJsYWNrRnJhbWVzKHdoYW1teS5mcmFtZXMsIC0xLCBudWxsLCBudWxsLCBmdW5jdGlvbihmcmFtZXMpIHtcclxuICAgICAgICAgICAgICAgIHdoYW1teS5mcmFtZXMgPSBmcmFtZXM7XHJcblxyXG4gICAgICAgICAgICAgICAgLy8gdG8gZGlzcGxheSBhZHZlcnRpc2VtZW50IGltYWdlcyFcclxuICAgICAgICAgICAgICAgIGlmIChjb25maWcuYWR2ZXJ0aXNlbWVudCAmJiBjb25maWcuYWR2ZXJ0aXNlbWVudC5sZW5ndGgpIHtcclxuICAgICAgICAgICAgICAgICAgICB3aGFtbXkuZnJhbWVzID0gY29uZmlnLmFkdmVydGlzZW1lbnQuY29uY2F0KHdoYW1teS5mcmFtZXMpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIC8qKlxyXG4gICAgICAgICAgICAgICAgICogQHByb3BlcnR5IHtCbG9ifSBibG9iIC0gUmVjb3JkZWQgZnJhbWVzIGluIHZpZGVvL3dlYm0gYmxvYi5cclxuICAgICAgICAgICAgICAgICAqIEBtZW1iZXJvZiBXaGFtbXlSZWNvcmRlclxyXG4gICAgICAgICAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgICAgICAgICAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgKiAgICAgdmFyIGJsb2IgPSByZWNvcmRlci5ibG9iO1xyXG4gICAgICAgICAgICAgICAgICogfSk7XHJcbiAgICAgICAgICAgICAgICAgKi9cclxuICAgICAgICAgICAgICAgIHdoYW1teS5jb21waWxlKGZ1bmN0aW9uKGJsb2IpIHtcclxuICAgICAgICAgICAgICAgICAgICBfdGhpcy5ibG9iID0gYmxvYjtcclxuXHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKF90aGlzLmJsb2IuZm9yRWFjaCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBfdGhpcy5ibG9iID0gbmV3IEJsb2IoW10sIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHR5cGU6ICd2aWRlby93ZWJtJ1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgICAgIGlmIChjYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBjYWxsYmFjayhfdGhpcy5ibG9iKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfSwgMTApO1xyXG4gICAgfTtcclxuXHJcbiAgICB2YXIgaXNQYXVzZWRSZWNvcmRpbmcgPSBmYWxzZTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHBhdXNlcyB0aGUgcmVjb3JkaW5nIHByb2Nlc3MuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgV2hhbW15UmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5wYXVzZSgpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnBhdXNlID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaXNQYXVzZWRSZWNvcmRpbmcgPSB0cnVlO1xyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJlc3VtZXMgdGhlIHJlY29yZGluZyBwcm9jZXNzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFdoYW1teVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucmVzdW1lKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucmVzdW1lID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaXNQYXVzZWRSZWNvcmRpbmcgPSBmYWxzZTtcclxuXHJcbiAgICAgICAgaWYgKGlzU3RvcERyYXdpbmcpIHtcclxuICAgICAgICAgICAgdGhpcy5yZWNvcmQoKTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVzZXRzIGN1cnJlbnRseSByZWNvcmRlZCBkYXRhLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFdoYW1teVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuY2xlYXJSZWNvcmRlZERhdGEoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5jbGVhclJlY29yZGVkRGF0YSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlmICghaXNTdG9wRHJhd2luZykge1xyXG4gICAgICAgICAgICB0aGlzLnN0b3AoY2xlYXJSZWNvcmRlZERhdGFDQik7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGNsZWFyUmVjb3JkZWREYXRhQ0IoKTtcclxuICAgIH07XHJcblxyXG4gICAgZnVuY3Rpb24gY2xlYXJSZWNvcmRlZERhdGFDQigpIHtcclxuICAgICAgICB3aGFtbXkuZnJhbWVzID0gW107XHJcbiAgICAgICAgaXNTdG9wRHJhd2luZyA9IHRydWU7XHJcbiAgICAgICAgaXNQYXVzZWRSZWNvcmRpbmcgPSBmYWxzZTtcclxuICAgIH1cclxuXHJcbiAgICAvLyBmb3IgZGVidWdnaW5nXHJcbiAgICB0aGlzLm5hbWUgPSAnV2hhbW15UmVjb3JkZXInO1xyXG4gICAgdGhpcy50b1N0cmluZyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiB0aGlzLm5hbWU7XHJcbiAgICB9O1xyXG5cclxuICAgIHZhciBjYW52YXMgPSBkb2N1bWVudC5jcmVhdGVFbGVtZW50KCdjYW52YXMnKTtcclxuICAgIHZhciBjb250ZXh0ID0gY2FudmFzLmdldENvbnRleHQoJzJkJyk7XHJcblxyXG4gICAgdmFyIHZpZGVvO1xyXG4gICAgdmFyIGxhc3RUaW1lO1xyXG4gICAgdmFyIHdoYW1teTtcclxufVxyXG5cclxuaWYgKHR5cGVvZiBSZWNvcmRSVEMgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICBSZWNvcmRSVEMuV2hhbW15UmVjb3JkZXIgPSBXaGFtbXlSZWNvcmRlcjtcclxufVxuXHJcbi8vIGh0dHBzOi8vZ2l0aHViLmNvbS9hbnRpbWF0dGVyMTUvd2hhbW15L2Jsb2IvbWFzdGVyL0xJQ0VOU0VcclxuLy8gX19fX19fX19fXHJcbi8vIFdoYW1teS5qc1xyXG5cclxuLy8gdG9kbzogRmlyZWZveCBub3cgc3VwcG9ydHMgd2VicCBmb3Igd2VibSBjb250YWluZXJzIVxyXG4vLyB0aGVpciBNZWRpYVJlY29yZGVyIGltcGxlbWVudGF0aW9uIHdvcmtzIHdlbGwhXHJcbi8vIHNob3VsZCB3ZSBwcm92aWRlIGFuIG9wdGlvbiB0byByZWNvcmQgdmlhIFdoYW1teS5qcyBvciBNZWRpYVJlY29yZGVyIEFQSSBpcyBhIGJldHRlciBzb2x1dGlvbj9cclxuXHJcbi8qKlxyXG4gKiBXaGFtbXkgaXMgYSBzdGFuZGFsb25lIGNsYXNzIHVzZWQgYnkge0BsaW5rIFJlY29yZFJUQ30gdG8gYnJpbmcgdmlkZW8gcmVjb3JkaW5nIGluIENocm9tZS4gSXQgaXMgd3JpdHRlbiBieSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL2FudGltYXR0ZXIxNXxhbnRpbWF0dGVyMTV9XHJcbiAqIEBzdW1tYXJ5IEEgcmVhbCB0aW1lIGphdmFzY3JpcHQgd2VibSBlbmNvZGVyIGJhc2VkIG9uIGEgY2FudmFzIGhhY2suXHJcbiAqIEBsaWNlbnNlIHtAbGluayBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQy9ibG9iL21hc3Rlci9MSUNFTlNFfE1JVH1cclxuICogQGF1dGhvciB7QGxpbmsgaHR0cHM6Ly9NdWF6S2hhbi5jb218TXVheiBLaGFufVxyXG4gKiBAdHlwZWRlZiBXaGFtbXlcclxuICogQGNsYXNzXHJcbiAqIEBleGFtcGxlXHJcbiAqIHZhciByZWNvcmRlciA9IG5ldyBXaGFtbXkoKS5WaWRlbygxNSk7XHJcbiAqIHJlY29yZGVyLmFkZChjb250ZXh0IHx8IGNhbnZhcyB8fCBkYXRhVVJMKTtcclxuICogdmFyIG91dHB1dCA9IHJlY29yZGVyLmNvbXBpbGUoKTtcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKi9cclxuXHJcbnZhciBXaGFtbXkgPSAoZnVuY3Rpb24oKSB7XHJcbiAgICAvLyBhIG1vcmUgYWJzdHJhY3QtaXNoIEFQSVxyXG5cclxuICAgIGZ1bmN0aW9uIFdoYW1teVZpZGVvKGR1cmF0aW9uKSB7XHJcbiAgICAgICAgdGhpcy5mcmFtZXMgPSBbXTtcclxuICAgICAgICB0aGlzLmR1cmF0aW9uID0gZHVyYXRpb24gfHwgMTtcclxuICAgICAgICB0aGlzLnF1YWxpdHkgPSAwLjg7XHJcbiAgICB9XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBQYXNzIENhbnZhcyBvciBDb250ZXh0IG9yIGltYWdlL3dlYnAoc3RyaW5nKSB0byB7QGxpbmsgV2hhbW15fSBlbmNvZGVyLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFdoYW1teVxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyID0gbmV3IFdoYW1teSgpLlZpZGVvKDAuOCwgMTAwKTtcclxuICAgICAqIHJlY29yZGVyLmFkZChjYW52YXMgfHwgY29udGV4dCB8fCAnaW1hZ2Uvd2VicCcpO1xyXG4gICAgICogQHBhcmFtIHtzdHJpbmd9IGZyYW1lIC0gQ2FudmFzIHx8IENvbnRleHQgfHwgaW1hZ2Uvd2VicFxyXG4gICAgICogQHBhcmFtIHtudW1iZXJ9IGR1cmF0aW9uIC0gU3RpY2sgYSBkdXJhdGlvbiAoaW4gbWlsbGlzZWNvbmRzKVxyXG4gICAgICovXHJcbiAgICBXaGFtbXlWaWRlby5wcm90b3R5cGUuYWRkID0gZnVuY3Rpb24oZnJhbWUsIGR1cmF0aW9uKSB7XHJcbiAgICAgICAgaWYgKCdjYW52YXMnIGluIGZyYW1lKSB7IC8vQ2FudmFzUmVuZGVyaW5nQ29udGV4dDJEXHJcbiAgICAgICAgICAgIGZyYW1lID0gZnJhbWUuY2FudmFzO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCd0b0RhdGFVUkwnIGluIGZyYW1lKSB7XHJcbiAgICAgICAgICAgIGZyYW1lID0gZnJhbWUudG9EYXRhVVJMKCdpbWFnZS93ZWJwJywgdGhpcy5xdWFsaXR5KTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghKC9eZGF0YTppbWFnZVxcL3dlYnA7YmFzZTY0LC9pZykudGVzdChmcmFtZSkpIHtcclxuICAgICAgICAgICAgdGhyb3cgJ0lucHV0IG11c3QgYmUgZm9ybWF0dGVkIHByb3Blcmx5IGFzIGEgYmFzZTY0IGVuY29kZWQgRGF0YVVSSSBvZiB0eXBlIGltYWdlL3dlYnAnO1xyXG4gICAgICAgIH1cclxuICAgICAgICB0aGlzLmZyYW1lcy5wdXNoKHtcclxuICAgICAgICAgICAgaW1hZ2U6IGZyYW1lLFxyXG4gICAgICAgICAgICBkdXJhdGlvbjogZHVyYXRpb24gfHwgdGhpcy5kdXJhdGlvblxyXG4gICAgICAgIH0pO1xyXG4gICAgfTtcclxuXHJcbiAgICBmdW5jdGlvbiBwcm9jZXNzSW5XZWJXb3JrZXIoX2Z1bmN0aW9uKSB7XHJcbiAgICAgICAgdmFyIGJsb2IgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKG5ldyBCbG9iKFtfZnVuY3Rpb24udG9TdHJpbmcoKSxcclxuICAgICAgICAgICAgJ3RoaXMub25tZXNzYWdlID0gIGZ1bmN0aW9uIChlZWUpIHsnICsgX2Z1bmN0aW9uLm5hbWUgKyAnKGVlZS5kYXRhKTt9J1xyXG4gICAgICAgIF0sIHtcclxuICAgICAgICAgICAgdHlwZTogJ2FwcGxpY2F0aW9uL2phdmFzY3JpcHQnXHJcbiAgICAgICAgfSkpO1xyXG5cclxuICAgICAgICB2YXIgd29ya2VyID0gbmV3IFdvcmtlcihibG9iKTtcclxuICAgICAgICBVUkwucmV2b2tlT2JqZWN0VVJMKGJsb2IpO1xyXG4gICAgICAgIHJldHVybiB3b3JrZXI7XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gd2hhbW15SW5XZWJXb3JrZXIoZnJhbWVzKSB7XHJcbiAgICAgICAgZnVuY3Rpb24gQXJyYXlUb1dlYk0oZnJhbWVzKSB7XHJcbiAgICAgICAgICAgIHZhciBpbmZvID0gY2hlY2tGcmFtZXMoZnJhbWVzKTtcclxuICAgICAgICAgICAgaWYgKCFpbmZvKSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gW107XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIHZhciBjbHVzdGVyTWF4RHVyYXRpb24gPSAzMDAwMDtcclxuXHJcbiAgICAgICAgICAgIHZhciBFQk1MID0gW3tcclxuICAgICAgICAgICAgICAgICdpZCc6IDB4MWE0NWRmYTMsIC8vIEVCTUxcclxuICAgICAgICAgICAgICAgICdkYXRhJzogW3tcclxuICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IDEsXHJcbiAgICAgICAgICAgICAgICAgICAgJ2lkJzogMHg0Mjg2IC8vIEVCTUxWZXJzaW9uXHJcbiAgICAgICAgICAgICAgICB9LCB7XHJcbiAgICAgICAgICAgICAgICAgICAgJ2RhdGEnOiAxLFxyXG4gICAgICAgICAgICAgICAgICAgICdpZCc6IDB4NDJmNyAvLyBFQk1MUmVhZFZlcnNpb25cclxuICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IDQsXHJcbiAgICAgICAgICAgICAgICAgICAgJ2lkJzogMHg0MmYyIC8vIEVCTUxNYXhJRExlbmd0aFxyXG4gICAgICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgICAgICdkYXRhJzogOCxcclxuICAgICAgICAgICAgICAgICAgICAnaWQnOiAweDQyZjMgLy8gRUJNTE1heFNpemVMZW5ndGhcclxuICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAnZGF0YSc6ICd3ZWJtJyxcclxuICAgICAgICAgICAgICAgICAgICAnaWQnOiAweDQyODIgLy8gRG9jVHlwZVxyXG4gICAgICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgICAgICdkYXRhJzogMixcclxuICAgICAgICAgICAgICAgICAgICAnaWQnOiAweDQyODcgLy8gRG9jVHlwZVZlcnNpb25cclxuICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IDIsXHJcbiAgICAgICAgICAgICAgICAgICAgJ2lkJzogMHg0Mjg1IC8vIERvY1R5cGVSZWFkVmVyc2lvblxyXG4gICAgICAgICAgICAgICAgfV1cclxuICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgJ2lkJzogMHgxODUzODA2NywgLy8gU2VnbWVudFxyXG4gICAgICAgICAgICAgICAgJ2RhdGEnOiBbe1xyXG4gICAgICAgICAgICAgICAgICAgICdpZCc6IDB4MTU0OWE5NjYsIC8vIEluZm9cclxuICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IFt7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogMWU2LCAvL2RvIHRoaW5ncyBpbiBtaWxsaXNlY3MgKG51bSBvZiBuYW5vc2VjcyBmb3IgZHVyYXRpb24gc2NhbGUpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4MmFkN2IxIC8vIFRpbWVjb2RlU2NhbGVcclxuICAgICAgICAgICAgICAgICAgICB9LCB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogJ3doYW1teScsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4NGQ4MCAvLyBNdXhpbmdBcHBcclxuICAgICAgICAgICAgICAgICAgICB9LCB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogJ3doYW1teScsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4NTc0MSAvLyBXcml0aW5nQXBwXHJcbiAgICAgICAgICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IGRvdWJsZVRvU3RyaW5nKGluZm8uZHVyYXRpb24pLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAnaWQnOiAweDQ0ODkgLy8gRHVyYXRpb25cclxuICAgICAgICAgICAgICAgICAgICB9XVxyXG4gICAgICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgICAgICdpZCc6IDB4MTY1NGFlNmIsIC8vIFRyYWNrc1xyXG4gICAgICAgICAgICAgICAgICAgICdkYXRhJzogW3tcclxuICAgICAgICAgICAgICAgICAgICAgICAgJ2lkJzogMHhhZSwgLy8gVHJhY2tFbnRyeVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IFt7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IDEsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAnaWQnOiAweGQ3IC8vIFRyYWNrTnVtYmVyXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogMSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4NzNjNSAvLyBUcmFja1VJRFxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9LCB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IDAsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAnaWQnOiAweDljIC8vIEZsYWdMYWNpbmdcclxuICAgICAgICAgICAgICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgJ2RhdGEnOiAndW5kJyxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4MjJiNTljIC8vIExhbmd1YWdlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogJ1ZfVlA4JyxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4ODYgLy8gQ29kZWNJRFxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9LCB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAnZGF0YSc6ICdWUDgnLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgJ2lkJzogMHgyNTg2ODggLy8gQ29kZWNOYW1lXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogMSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4ODMgLy8gVHJhY2tUeXBlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0sIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4ZTAsIC8vIFZpZGVvXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IFt7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgJ2RhdGEnOiBpbmZvLndpZHRoLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICdpZCc6IDB4YjAgLy8gUGl4ZWxXaWR0aFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfSwge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICdkYXRhJzogaW5mby5oZWlnaHQsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgJ2lkJzogMHhiYSAvLyBQaXhlbEhlaWdodFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfV1cclxuICAgICAgICAgICAgICAgICAgICAgICAgfV1cclxuICAgICAgICAgICAgICAgICAgICB9XVxyXG4gICAgICAgICAgICAgICAgfV1cclxuICAgICAgICAgICAgfV07XHJcblxyXG4gICAgICAgICAgICAvL0dlbmVyYXRlIGNsdXN0ZXJzIChtYXggZHVyYXRpb24pXHJcbiAgICAgICAgICAgIHZhciBmcmFtZU51bWJlciA9IDA7XHJcbiAgICAgICAgICAgIHZhciBjbHVzdGVyVGltZWNvZGUgPSAwO1xyXG4gICAgICAgICAgICB3aGlsZSAoZnJhbWVOdW1iZXIgPCBmcmFtZXMubGVuZ3RoKSB7XHJcblxyXG4gICAgICAgICAgICAgICAgdmFyIGNsdXN0ZXJGcmFtZXMgPSBbXTtcclxuICAgICAgICAgICAgICAgIHZhciBjbHVzdGVyRHVyYXRpb24gPSAwO1xyXG4gICAgICAgICAgICAgICAgZG8ge1xyXG4gICAgICAgICAgICAgICAgICAgIGNsdXN0ZXJGcmFtZXMucHVzaChmcmFtZXNbZnJhbWVOdW1iZXJdKTtcclxuICAgICAgICAgICAgICAgICAgICBjbHVzdGVyRHVyYXRpb24gKz0gZnJhbWVzW2ZyYW1lTnVtYmVyXS5kdXJhdGlvbjtcclxuICAgICAgICAgICAgICAgICAgICBmcmFtZU51bWJlcisrO1xyXG4gICAgICAgICAgICAgICAgfSB3aGlsZSAoZnJhbWVOdW1iZXIgPCBmcmFtZXMubGVuZ3RoICYmIGNsdXN0ZXJEdXJhdGlvbiA8IGNsdXN0ZXJNYXhEdXJhdGlvbik7XHJcblxyXG4gICAgICAgICAgICAgICAgdmFyIGNsdXN0ZXJDb3VudGVyID0gMDtcclxuICAgICAgICAgICAgICAgIHZhciBjbHVzdGVyID0ge1xyXG4gICAgICAgICAgICAgICAgICAgICdpZCc6IDB4MWY0M2I2NzUsIC8vIENsdXN0ZXJcclxuICAgICAgICAgICAgICAgICAgICAnZGF0YSc6IGdldENsdXN0ZXJEYXRhKGNsdXN0ZXJUaW1lY29kZSwgY2x1c3RlckNvdW50ZXIsIGNsdXN0ZXJGcmFtZXMpXHJcbiAgICAgICAgICAgICAgICB9OyAvL0FkZCBjbHVzdGVyIHRvIHNlZ21lbnRcclxuICAgICAgICAgICAgICAgIEVCTUxbMV0uZGF0YS5wdXNoKGNsdXN0ZXIpO1xyXG4gICAgICAgICAgICAgICAgY2x1c3RlclRpbWVjb2RlICs9IGNsdXN0ZXJEdXJhdGlvbjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgcmV0dXJuIGdlbmVyYXRlRUJNTChFQk1MKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGZ1bmN0aW9uIGdldENsdXN0ZXJEYXRhKGNsdXN0ZXJUaW1lY29kZSwgY2x1c3RlckNvdW50ZXIsIGNsdXN0ZXJGcmFtZXMpIHtcclxuICAgICAgICAgICAgcmV0dXJuIFt7XHJcbiAgICAgICAgICAgICAgICAnZGF0YSc6IGNsdXN0ZXJUaW1lY29kZSxcclxuICAgICAgICAgICAgICAgICdpZCc6IDB4ZTcgLy8gVGltZWNvZGVcclxuICAgICAgICAgICAgfV0uY29uY2F0KGNsdXN0ZXJGcmFtZXMubWFwKGZ1bmN0aW9uKHdlYnApIHtcclxuICAgICAgICAgICAgICAgIHZhciBibG9jayA9IG1ha2VTaW1wbGVCbG9jayh7XHJcbiAgICAgICAgICAgICAgICAgICAgZGlzY2FyZGFibGU6IDAsXHJcbiAgICAgICAgICAgICAgICAgICAgZnJhbWU6IHdlYnAuZGF0YS5zbGljZSg0KSxcclxuICAgICAgICAgICAgICAgICAgICBpbnZpc2libGU6IDAsXHJcbiAgICAgICAgICAgICAgICAgICAga2V5ZnJhbWU6IDEsXHJcbiAgICAgICAgICAgICAgICAgICAgbGFjaW5nOiAwLFxyXG4gICAgICAgICAgICAgICAgICAgIHRyYWNrTnVtOiAxLFxyXG4gICAgICAgICAgICAgICAgICAgIHRpbWVjb2RlOiBNYXRoLnJvdW5kKGNsdXN0ZXJDb3VudGVyKVxyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICBjbHVzdGVyQ291bnRlciArPSB3ZWJwLmR1cmF0aW9uO1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHtcclxuICAgICAgICAgICAgICAgICAgICBkYXRhOiBibG9jayxcclxuICAgICAgICAgICAgICAgICAgICBpZDogMHhhM1xyXG4gICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgfSkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgLy8gc3VtcyB0aGUgbGVuZ3RocyBvZiBhbGwgdGhlIGZyYW1lcyBhbmQgZ2V0cyB0aGUgZHVyYXRpb25cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gY2hlY2tGcmFtZXMoZnJhbWVzKSB7XHJcbiAgICAgICAgICAgIGlmICghZnJhbWVzWzBdKSB7XHJcbiAgICAgICAgICAgICAgICBwb3N0TWVzc2FnZSh7XHJcbiAgICAgICAgICAgICAgICAgICAgZXJyb3I6ICdTb21ldGhpbmcgd2VudCB3cm9uZy4gTWF5YmUgV2ViUCBmb3JtYXQgaXMgbm90IHN1cHBvcnRlZCBpbiB0aGUgY3VycmVudCBicm93c2VyLidcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICB2YXIgd2lkdGggPSBmcmFtZXNbMF0ud2lkdGgsXHJcbiAgICAgICAgICAgICAgICBoZWlnaHQgPSBmcmFtZXNbMF0uaGVpZ2h0LFxyXG4gICAgICAgICAgICAgICAgZHVyYXRpb24gPSBmcmFtZXNbMF0uZHVyYXRpb247XHJcblxyXG4gICAgICAgICAgICBmb3IgKHZhciBpID0gMTsgaSA8IGZyYW1lcy5sZW5ndGg7IGkrKykge1xyXG4gICAgICAgICAgICAgICAgZHVyYXRpb24gKz0gZnJhbWVzW2ldLmR1cmF0aW9uO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiB7XHJcbiAgICAgICAgICAgICAgICBkdXJhdGlvbjogZHVyYXRpb24sXHJcbiAgICAgICAgICAgICAgICB3aWR0aDogd2lkdGgsXHJcbiAgICAgICAgICAgICAgICBoZWlnaHQ6IGhlaWdodFxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gbnVtVG9CdWZmZXIobnVtKSB7XHJcbiAgICAgICAgICAgIHZhciBwYXJ0cyA9IFtdO1xyXG4gICAgICAgICAgICB3aGlsZSAobnVtID4gMCkge1xyXG4gICAgICAgICAgICAgICAgcGFydHMucHVzaChudW0gJiAweGZmKTtcclxuICAgICAgICAgICAgICAgIG51bSA9IG51bSA+PiA4O1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgVWludDhBcnJheShwYXJ0cy5yZXZlcnNlKCkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gc3RyVG9CdWZmZXIoc3RyKSB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgVWludDhBcnJheShzdHIuc3BsaXQoJycpLm1hcChmdW5jdGlvbihlKSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gZS5jaGFyQ29kZUF0KDApO1xyXG4gICAgICAgICAgICB9KSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBmdW5jdGlvbiBiaXRzVG9CdWZmZXIoYml0cykge1xyXG4gICAgICAgICAgICB2YXIgZGF0YSA9IFtdO1xyXG4gICAgICAgICAgICB2YXIgcGFkID0gKGJpdHMubGVuZ3RoICUgOCkgPyAobmV3IEFycmF5KDEgKyA4IC0gKGJpdHMubGVuZ3RoICUgOCkpKS5qb2luKCcwJykgOiAnJztcclxuICAgICAgICAgICAgYml0cyA9IHBhZCArIGJpdHM7XHJcbiAgICAgICAgICAgIGZvciAodmFyIGkgPSAwOyBpIDwgYml0cy5sZW5ndGg7IGkgKz0gOCkge1xyXG4gICAgICAgICAgICAgICAgZGF0YS5wdXNoKHBhcnNlSW50KGJpdHMuc3Vic3RyKGksIDgpLCAyKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBVaW50OEFycmF5KGRhdGEpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gZ2VuZXJhdGVFQk1MKGpzb24pIHtcclxuICAgICAgICAgICAgdmFyIGVibWwgPSBbXTtcclxuICAgICAgICAgICAgZm9yICh2YXIgaSA9IDA7IGkgPCBqc29uLmxlbmd0aDsgaSsrKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgZGF0YSA9IGpzb25baV0uZGF0YTtcclxuXHJcbiAgICAgICAgICAgICAgICBpZiAodHlwZW9mIGRhdGEgPT09ICdvYmplY3QnKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZGF0YSA9IGdlbmVyYXRlRUJNTChkYXRhKTtcclxuICAgICAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgICAgICBpZiAodHlwZW9mIGRhdGEgPT09ICdudW1iZXInKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZGF0YSA9IGJpdHNUb0J1ZmZlcihkYXRhLnRvU3RyaW5nKDIpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgICAgICBpZiAodHlwZW9mIGRhdGEgPT09ICdzdHJpbmcnKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZGF0YSA9IHN0clRvQnVmZmVyKGRhdGEpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIHZhciBsZW4gPSBkYXRhLnNpemUgfHwgZGF0YS5ieXRlTGVuZ3RoIHx8IGRhdGEubGVuZ3RoO1xyXG4gICAgICAgICAgICAgICAgdmFyIHplcm9lcyA9IE1hdGguY2VpbChNYXRoLmNlaWwoTWF0aC5sb2cobGVuKSAvIE1hdGgubG9nKDIpKSAvIDgpO1xyXG4gICAgICAgICAgICAgICAgdmFyIHNpemVUb1N0cmluZyA9IGxlbi50b1N0cmluZygyKTtcclxuICAgICAgICAgICAgICAgIHZhciBwYWRkZWQgPSAobmV3IEFycmF5KCh6ZXJvZXMgKiA3ICsgNyArIDEpIC0gc2l6ZVRvU3RyaW5nLmxlbmd0aCkpLmpvaW4oJzAnKSArIHNpemVUb1N0cmluZztcclxuICAgICAgICAgICAgICAgIHZhciBzaXplID0gKG5ldyBBcnJheSh6ZXJvZXMpKS5qb2luKCcwJykgKyAnMScgKyBwYWRkZWQ7XHJcblxyXG4gICAgICAgICAgICAgICAgZWJtbC5wdXNoKG51bVRvQnVmZmVyKGpzb25baV0uaWQpKTtcclxuICAgICAgICAgICAgICAgIGVibWwucHVzaChiaXRzVG9CdWZmZXIoc2l6ZSkpO1xyXG4gICAgICAgICAgICAgICAgZWJtbC5wdXNoKGRhdGEpO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICByZXR1cm4gbmV3IEJsb2IoZWJtbCwge1xyXG4gICAgICAgICAgICAgICAgdHlwZTogJ3ZpZGVvL3dlYm0nXHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gdG9CaW5TdHJPbGQoYml0cykge1xyXG4gICAgICAgICAgICB2YXIgZGF0YSA9ICcnO1xyXG4gICAgICAgICAgICB2YXIgcGFkID0gKGJpdHMubGVuZ3RoICUgOCkgPyAobmV3IEFycmF5KDEgKyA4IC0gKGJpdHMubGVuZ3RoICUgOCkpKS5qb2luKCcwJykgOiAnJztcclxuICAgICAgICAgICAgYml0cyA9IHBhZCArIGJpdHM7XHJcbiAgICAgICAgICAgIGZvciAodmFyIGkgPSAwOyBpIDwgYml0cy5sZW5ndGg7IGkgKz0gOCkge1xyXG4gICAgICAgICAgICAgICAgZGF0YSArPSBTdHJpbmcuZnJvbUNoYXJDb2RlKHBhcnNlSW50KGJpdHMuc3Vic3RyKGksIDgpLCAyKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIGRhdGE7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBmdW5jdGlvbiBtYWtlU2ltcGxlQmxvY2soZGF0YSkge1xyXG4gICAgICAgICAgICB2YXIgZmxhZ3MgPSAwO1xyXG5cclxuICAgICAgICAgICAgaWYgKGRhdGEua2V5ZnJhbWUpIHtcclxuICAgICAgICAgICAgICAgIGZsYWdzIHw9IDEyODtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKGRhdGEuaW52aXNpYmxlKSB7XHJcbiAgICAgICAgICAgICAgICBmbGFncyB8PSA4O1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoZGF0YS5sYWNpbmcpIHtcclxuICAgICAgICAgICAgICAgIGZsYWdzIHw9IChkYXRhLmxhY2luZyA8PCAxKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKGRhdGEuZGlzY2FyZGFibGUpIHtcclxuICAgICAgICAgICAgICAgIGZsYWdzIHw9IDE7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChkYXRhLnRyYWNrTnVtID4gMTI3KSB7XHJcbiAgICAgICAgICAgICAgICB0aHJvdyAnVHJhY2tOdW1iZXIgPiAxMjcgbm90IHN1cHBvcnRlZCc7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIHZhciBvdXQgPSBbZGF0YS50cmFja051bSB8IDB4ODAsIGRhdGEudGltZWNvZGUgPj4gOCwgZGF0YS50aW1lY29kZSAmIDB4ZmYsIGZsYWdzXS5tYXAoZnVuY3Rpb24oZSkge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIFN0cmluZy5mcm9tQ2hhckNvZGUoZSk7XHJcbiAgICAgICAgICAgIH0pLmpvaW4oJycpICsgZGF0YS5mcmFtZTtcclxuXHJcbiAgICAgICAgICAgIHJldHVybiBvdXQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBmdW5jdGlvbiBwYXJzZVdlYlAocmlmZikge1xyXG4gICAgICAgICAgICB2YXIgVlA4ID0gcmlmZi5SSUZGWzBdLldFQlBbMF07XHJcblxyXG4gICAgICAgICAgICB2YXIgZnJhbWVTdGFydCA9IFZQOC5pbmRleE9mKCdcXHg5ZFxceDAxXFx4MmEnKTsgLy8gQSBWUDgga2V5ZnJhbWUgc3RhcnRzIHdpdGggdGhlIDB4OWQwMTJhIGhlYWRlclxyXG4gICAgICAgICAgICBmb3IgKHZhciBpID0gMCwgYyA9IFtdOyBpIDwgNDsgaSsrKSB7XHJcbiAgICAgICAgICAgICAgICBjW2ldID0gVlA4LmNoYXJDb2RlQXQoZnJhbWVTdGFydCArIDMgKyBpKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgdmFyIHdpZHRoLCBoZWlnaHQsIHRtcDtcclxuXHJcbiAgICAgICAgICAgIC8vdGhlIGNvZGUgYmVsb3cgaXMgbGl0ZXJhbGx5IGNvcGllZCB2ZXJiYXRpbSBmcm9tIHRoZSBiaXRzdHJlYW0gc3BlY1xyXG4gICAgICAgICAgICB0bXAgPSAoY1sxXSA8PCA4KSB8IGNbMF07XHJcbiAgICAgICAgICAgIHdpZHRoID0gdG1wICYgMHgzRkZGO1xyXG4gICAgICAgICAgICB0bXAgPSAoY1szXSA8PCA4KSB8IGNbMl07XHJcbiAgICAgICAgICAgIGhlaWdodCA9IHRtcCAmIDB4M0ZGRjtcclxuICAgICAgICAgICAgcmV0dXJuIHtcclxuICAgICAgICAgICAgICAgIHdpZHRoOiB3aWR0aCxcclxuICAgICAgICAgICAgICAgIGhlaWdodDogaGVpZ2h0LFxyXG4gICAgICAgICAgICAgICAgZGF0YTogVlA4LFxyXG4gICAgICAgICAgICAgICAgcmlmZjogcmlmZlxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gZ2V0U3RyTGVuZ3RoKHN0cmluZywgb2Zmc2V0KSB7XHJcbiAgICAgICAgICAgIHJldHVybiBwYXJzZUludChzdHJpbmcuc3Vic3RyKG9mZnNldCArIDQsIDQpLnNwbGl0KCcnKS5tYXAoZnVuY3Rpb24oaSkge1xyXG4gICAgICAgICAgICAgICAgdmFyIHVucGFkZGVkID0gaS5jaGFyQ29kZUF0KDApLnRvU3RyaW5nKDIpO1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIChuZXcgQXJyYXkoOCAtIHVucGFkZGVkLmxlbmd0aCArIDEpKS5qb2luKCcwJykgKyB1bnBhZGRlZDtcclxuICAgICAgICAgICAgfSkuam9pbignJyksIDIpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZnVuY3Rpb24gcGFyc2VSSUZGKHN0cmluZykge1xyXG4gICAgICAgICAgICB2YXIgb2Zmc2V0ID0gMDtcclxuICAgICAgICAgICAgdmFyIGNodW5rcyA9IHt9O1xyXG5cclxuICAgICAgICAgICAgd2hpbGUgKG9mZnNldCA8IHN0cmluZy5sZW5ndGgpIHtcclxuICAgICAgICAgICAgICAgIHZhciBpZCA9IHN0cmluZy5zdWJzdHIob2Zmc2V0LCA0KTtcclxuICAgICAgICAgICAgICAgIHZhciBsZW4gPSBnZXRTdHJMZW5ndGgoc3RyaW5nLCBvZmZzZXQpO1xyXG4gICAgICAgICAgICAgICAgdmFyIGRhdGEgPSBzdHJpbmcuc3Vic3RyKG9mZnNldCArIDQgKyA0LCBsZW4pO1xyXG4gICAgICAgICAgICAgICAgb2Zmc2V0ICs9IDQgKyA0ICsgbGVuO1xyXG4gICAgICAgICAgICAgICAgY2h1bmtzW2lkXSA9IGNodW5rc1tpZF0gfHwgW107XHJcblxyXG4gICAgICAgICAgICAgICAgaWYgKGlkID09PSAnUklGRicgfHwgaWQgPT09ICdMSVNUJykge1xyXG4gICAgICAgICAgICAgICAgICAgIGNodW5rc1tpZF0ucHVzaChwYXJzZVJJRkYoZGF0YSkpO1xyXG4gICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICBjaHVua3NbaWRdLnB1c2goZGF0YSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIGNodW5rcztcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGZ1bmN0aW9uIGRvdWJsZVRvU3RyaW5nKG51bSkge1xyXG4gICAgICAgICAgICByZXR1cm4gW10uc2xpY2UuY2FsbChcclxuICAgICAgICAgICAgICAgIG5ldyBVaW50OEFycmF5KChuZXcgRmxvYXQ2NEFycmF5KFtudW1dKSkuYnVmZmVyKSwgMCkubWFwKGZ1bmN0aW9uKGUpIHtcclxuICAgICAgICAgICAgICAgIHJldHVybiBTdHJpbmcuZnJvbUNoYXJDb2RlKGUpO1xyXG4gICAgICAgICAgICB9KS5yZXZlcnNlKCkuam9pbignJyk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICB2YXIgd2VibSA9IG5ldyBBcnJheVRvV2ViTShmcmFtZXMubWFwKGZ1bmN0aW9uKGZyYW1lKSB7XHJcbiAgICAgICAgICAgIHZhciB3ZWJwID0gcGFyc2VXZWJQKHBhcnNlUklGRihhdG9iKGZyYW1lLmltYWdlLnNsaWNlKDIzKSkpKTtcclxuICAgICAgICAgICAgd2VicC5kdXJhdGlvbiA9IGZyYW1lLmR1cmF0aW9uO1xyXG4gICAgICAgICAgICByZXR1cm4gd2VicDtcclxuICAgICAgICB9KSk7XHJcblxyXG4gICAgICAgIHBvc3RNZXNzYWdlKHdlYm0pO1xyXG4gICAgfVxyXG5cclxuICAgIC8qKlxyXG4gICAgICogRW5jb2RlcyBmcmFtZXMgaW4gV2ViTSBjb250YWluZXIuIEl0IHVzZXMgV2ViV29ya2ludm9rZSB0byBpbnZva2UgJ0FycmF5VG9XZWJNJyBtZXRob2QuXHJcbiAgICAgKiBAcGFyYW0ge2Z1bmN0aW9ufSBjYWxsYmFjayAtIENhbGxiYWNrIGZ1bmN0aW9uLCB0aGF0IGlzIHVzZWQgdG8gcGFzcyByZWNvcmRlZCBibG9iIGJhY2sgdG8gdGhlIGNhbGxlZS5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBXaGFtbXlcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlciA9IG5ldyBXaGFtbXkoKS5WaWRlbygwLjgsIDEwMCk7XHJcbiAgICAgKiByZWNvcmRlci5jb21waWxlKGZ1bmN0aW9uKGJsb2IpIHtcclxuICAgICAqICAgIC8vIGJsb2Iuc2l6ZSAtIGJsb2IudHlwZVxyXG4gICAgICogfSk7XHJcbiAgICAgKi9cclxuICAgIFdoYW1teVZpZGVvLnByb3RvdHlwZS5jb21waWxlID0gZnVuY3Rpb24oY2FsbGJhY2spIHtcclxuICAgICAgICB2YXIgd2ViV29ya2VyID0gcHJvY2Vzc0luV2ViV29ya2VyKHdoYW1teUluV2ViV29ya2VyKTtcclxuXHJcbiAgICAgICAgd2ViV29ya2VyLm9ubWVzc2FnZSA9IGZ1bmN0aW9uKGV2ZW50KSB7XHJcbiAgICAgICAgICAgIGlmIChldmVudC5kYXRhLmVycm9yKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zb2xlLmVycm9yKGV2ZW50LmRhdGEuZXJyb3IpO1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGNhbGxiYWNrKGV2ZW50LmRhdGEpO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIHdlYldvcmtlci5wb3N0TWVzc2FnZSh0aGlzLmZyYW1lcyk7XHJcbiAgICB9O1xyXG5cclxuICAgIHJldHVybiB7XHJcbiAgICAgICAgLyoqXHJcbiAgICAgICAgICogQSBtb3JlIGFic3RyYWN0LWlzaCBBUEkuXHJcbiAgICAgICAgICogQG1ldGhvZFxyXG4gICAgICAgICAqIEBtZW1iZXJvZiBXaGFtbXlcclxuICAgICAgICAgKiBAZXhhbXBsZVxyXG4gICAgICAgICAqIHJlY29yZGVyID0gbmV3IFdoYW1teSgpLlZpZGVvKDAuOCwgMTAwKTtcclxuICAgICAgICAgKiBAcGFyYW0gez9udW1iZXJ9IHNwZWVkIC0gMC44XHJcbiAgICAgICAgICogQHBhcmFtIHs/bnVtYmVyfSBxdWFsaXR5IC0gMTAwXHJcbiAgICAgICAgICovXHJcbiAgICAgICAgVmlkZW86IFdoYW1teVZpZGVvXHJcbiAgICB9O1xyXG59KSgpO1xyXG5cclxuaWYgKHR5cGVvZiBSZWNvcmRSVEMgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICBSZWNvcmRSVEMuV2hhbW15ID0gV2hhbW15O1xyXG59XG5cclxuLy8gX19fX19fX19fX19fX18gKGluZGV4ZWQtZGIpXHJcbi8vIERpc2tTdG9yYWdlLmpzXHJcblxyXG4vKipcclxuICogRGlza1N0b3JhZ2UgaXMgYSBzdGFuZGFsb25lIG9iamVjdCB1c2VkIGJ5IHtAbGluayBSZWNvcmRSVEN9IHRvIHN0b3JlIHJlY29yZGVkIGJsb2JzIGluIEluZGV4ZWREQiBzdG9yYWdlLlxyXG4gKiBAc3VtbWFyeSBXcml0aW5nIGJsb2JzIGludG8gSW5kZXhlZERCLlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQGV4YW1wbGVcclxuICogRGlza1N0b3JhZ2UuU3RvcmUoe1xyXG4gKiAgICAgYXVkaW9CbG9iOiB5b3VyQXVkaW9CbG9iLFxyXG4gKiAgICAgdmlkZW9CbG9iOiB5b3VyVmlkZW9CbG9iLFxyXG4gKiAgICAgZ2lmQmxvYiAgOiB5b3VyR2lmQmxvYlxyXG4gKiB9KTtcclxuICogRGlza1N0b3JhZ2UuRmV0Y2goZnVuY3Rpb24oZGF0YVVSTCwgdHlwZSkge1xyXG4gKiAgICAgaWYodHlwZSA9PT0gJ2F1ZGlvQmxvYicpIHsgfVxyXG4gKiAgICAgaWYodHlwZSA9PT0gJ3ZpZGVvQmxvYicpIHsgfVxyXG4gKiAgICAgaWYodHlwZSA9PT0gJ2dpZkJsb2InKSAgIHsgfVxyXG4gKiB9KTtcclxuICogLy8gRGlza1N0b3JhZ2UuZGF0YVN0b3JlTmFtZSA9ICdyZWNvcmRSVEMnO1xyXG4gKiAvLyBEaXNrU3RvcmFnZS5vbkVycm9yID0gZnVuY3Rpb24oZXJyb3IpIHsgfTtcclxuICogQHByb3BlcnR5IHtmdW5jdGlvbn0gaW5pdCAtIFRoaXMgbWV0aG9kIG11c3QgYmUgY2FsbGVkIG9uY2UgdG8gaW5pdGlhbGl6ZSBJbmRleGVkREIgT2JqZWN0U3RvcmUuIFRob3VnaCwgaXQgaXMgYXV0by11c2VkIGludGVybmFsbHkuXHJcbiAqIEBwcm9wZXJ0eSB7ZnVuY3Rpb259IEZldGNoIC0gVGhpcyBtZXRob2QgZmV0Y2hlcyBzdG9yZWQgYmxvYnMgZnJvbSBJbmRleGVkREIuXHJcbiAqIEBwcm9wZXJ0eSB7ZnVuY3Rpb259IFN0b3JlIC0gVGhpcyBtZXRob2Qgc3RvcmVzIGJsb2JzIGluIEluZGV4ZWREQi5cclxuICogQHByb3BlcnR5IHtmdW5jdGlvbn0gb25FcnJvciAtIFRoaXMgZnVuY3Rpb24gaXMgaW52b2tlZCBmb3IgYW55IGtub3duL3Vua25vd24gZXJyb3IuXHJcbiAqIEBwcm9wZXJ0eSB7c3RyaW5nfSBkYXRhU3RvcmVOYW1lIC0gTmFtZSBvZiB0aGUgT2JqZWN0U3RvcmUgY3JlYXRlZCBpbiBJbmRleGVkREIgc3RvcmFnZS5cclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKi9cclxuXHJcblxyXG52YXIgRGlza1N0b3JhZ2UgPSB7XHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIG11c3QgYmUgY2FsbGVkIG9uY2UgdG8gaW5pdGlhbGl6ZSBJbmRleGVkREIgT2JqZWN0U3RvcmUuIFRob3VnaCwgaXQgaXMgYXV0by11c2VkIGludGVybmFsbHkuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgRGlza1N0b3JhZ2VcclxuICAgICAqIEBpbnRlcm5hbFxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIERpc2tTdG9yYWdlLmluaXQoKTtcclxuICAgICAqL1xyXG4gICAgaW5pdDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgdmFyIHNlbGYgPSB0aGlzO1xyXG5cclxuICAgICAgICBpZiAodHlwZW9mIGluZGV4ZWREQiA9PT0gJ3VuZGVmaW5lZCcgfHwgdHlwZW9mIGluZGV4ZWREQi5vcGVuID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmVycm9yKCdJbmRleGVkREIgQVBJIGFyZSBub3QgYXZhaWxhYmxlIGluIHRoaXMgYnJvd3Nlci4nKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgdmFyIGRiVmVyc2lvbiA9IDE7XHJcbiAgICAgICAgdmFyIGRiTmFtZSA9IHRoaXMuZGJOYW1lIHx8IGxvY2F0aW9uLmhyZWYucmVwbGFjZSgvXFwvfDp8I3wlfFxcLnxcXFt8XFxdL2csICcnKSxcclxuICAgICAgICAgICAgZGI7XHJcbiAgICAgICAgdmFyIHJlcXVlc3QgPSBpbmRleGVkREIub3BlbihkYk5hbWUsIGRiVmVyc2lvbik7XHJcblxyXG4gICAgICAgIGZ1bmN0aW9uIGNyZWF0ZU9iamVjdFN0b3JlKGRhdGFCYXNlKSB7XHJcbiAgICAgICAgICAgIGRhdGFCYXNlLmNyZWF0ZU9iamVjdFN0b3JlKHNlbGYuZGF0YVN0b3JlTmFtZSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBmdW5jdGlvbiBwdXRJbkRCKCkge1xyXG4gICAgICAgICAgICB2YXIgdHJhbnNhY3Rpb24gPSBkYi50cmFuc2FjdGlvbihbc2VsZi5kYXRhU3RvcmVOYW1lXSwgJ3JlYWR3cml0ZScpO1xyXG5cclxuICAgICAgICAgICAgaWYgKHNlbGYudmlkZW9CbG9iKSB7XHJcbiAgICAgICAgICAgICAgICB0cmFuc2FjdGlvbi5vYmplY3RTdG9yZShzZWxmLmRhdGFTdG9yZU5hbWUpLnB1dChzZWxmLnZpZGVvQmxvYiwgJ3ZpZGVvQmxvYicpO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoc2VsZi5naWZCbG9iKSB7XHJcbiAgICAgICAgICAgICAgICB0cmFuc2FjdGlvbi5vYmplY3RTdG9yZShzZWxmLmRhdGFTdG9yZU5hbWUpLnB1dChzZWxmLmdpZkJsb2IsICdnaWZCbG9iJyk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChzZWxmLmF1ZGlvQmxvYikge1xyXG4gICAgICAgICAgICAgICAgdHJhbnNhY3Rpb24ub2JqZWN0U3RvcmUoc2VsZi5kYXRhU3RvcmVOYW1lKS5wdXQoc2VsZi5hdWRpb0Jsb2IsICdhdWRpb0Jsb2InKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgZnVuY3Rpb24gZ2V0RnJvbVN0b3JlKHBvcnRpb25OYW1lKSB7XHJcbiAgICAgICAgICAgICAgICB0cmFuc2FjdGlvbi5vYmplY3RTdG9yZShzZWxmLmRhdGFTdG9yZU5hbWUpLmdldChwb3J0aW9uTmFtZSkub25zdWNjZXNzID0gZnVuY3Rpb24oZXZlbnQpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoc2VsZi5jYWxsYmFjaykge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBzZWxmLmNhbGxiYWNrKGV2ZW50LnRhcmdldC5yZXN1bHQsIHBvcnRpb25OYW1lKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9O1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBnZXRGcm9tU3RvcmUoJ2F1ZGlvQmxvYicpO1xyXG4gICAgICAgICAgICBnZXRGcm9tU3RvcmUoJ3ZpZGVvQmxvYicpO1xyXG4gICAgICAgICAgICBnZXRGcm9tU3RvcmUoJ2dpZkJsb2InKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHJlcXVlc3Qub25lcnJvciA9IHNlbGYub25FcnJvcjtcclxuXHJcbiAgICAgICAgcmVxdWVzdC5vbnN1Y2Nlc3MgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgZGIgPSByZXF1ZXN0LnJlc3VsdDtcclxuICAgICAgICAgICAgZGIub25lcnJvciA9IHNlbGYub25FcnJvcjtcclxuXHJcbiAgICAgICAgICAgIGlmIChkYi5zZXRWZXJzaW9uKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAoZGIudmVyc2lvbiAhPT0gZGJWZXJzaW9uKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdmFyIHNldFZlcnNpb24gPSBkYi5zZXRWZXJzaW9uKGRiVmVyc2lvbik7XHJcbiAgICAgICAgICAgICAgICAgICAgc2V0VmVyc2lvbi5vbnN1Y2Nlc3MgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY3JlYXRlT2JqZWN0U3RvcmUoZGIpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBwdXRJbkRCKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcHV0SW5EQigpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgcHV0SW5EQigpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfTtcclxuICAgICAgICByZXF1ZXN0Lm9udXBncmFkZW5lZWRlZCA9IGZ1bmN0aW9uKGV2ZW50KSB7XHJcbiAgICAgICAgICAgIGNyZWF0ZU9iamVjdFN0b3JlKGV2ZW50LnRhcmdldC5yZXN1bHQpO1xyXG4gICAgICAgIH07XHJcbiAgICB9LFxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBmZXRjaGVzIHN0b3JlZCBibG9icyBmcm9tIEluZGV4ZWREQi5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBEaXNrU3RvcmFnZVxyXG4gICAgICogQGludGVybmFsXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogRGlza1N0b3JhZ2UuRmV0Y2goZnVuY3Rpb24oZGF0YVVSTCwgdHlwZSkge1xyXG4gICAgICogICAgIGlmKHR5cGUgPT09ICdhdWRpb0Jsb2InKSB7IH1cclxuICAgICAqICAgICBpZih0eXBlID09PSAndmlkZW9CbG9iJykgeyB9XHJcbiAgICAgKiAgICAgaWYodHlwZSA9PT0gJ2dpZkJsb2InKSAgIHsgfVxyXG4gICAgICogfSk7XHJcbiAgICAgKi9cclxuICAgIEZldGNoOiBmdW5jdGlvbihjYWxsYmFjaykge1xyXG4gICAgICAgIHRoaXMuY2FsbGJhY2sgPSBjYWxsYmFjaztcclxuICAgICAgICB0aGlzLmluaXQoKTtcclxuXHJcbiAgICAgICAgcmV0dXJuIHRoaXM7XHJcbiAgICB9LFxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBzdG9yZXMgYmxvYnMgaW4gSW5kZXhlZERCLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIERpc2tTdG9yYWdlXHJcbiAgICAgKiBAaW50ZXJuYWxcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiBEaXNrU3RvcmFnZS5TdG9yZSh7XHJcbiAgICAgKiAgICAgYXVkaW9CbG9iOiB5b3VyQXVkaW9CbG9iLFxyXG4gICAgICogICAgIHZpZGVvQmxvYjogeW91clZpZGVvQmxvYixcclxuICAgICAqICAgICBnaWZCbG9iICA6IHlvdXJHaWZCbG9iXHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgU3RvcmU6IGZ1bmN0aW9uKGNvbmZpZykge1xyXG4gICAgICAgIHRoaXMuYXVkaW9CbG9iID0gY29uZmlnLmF1ZGlvQmxvYjtcclxuICAgICAgICB0aGlzLnZpZGVvQmxvYiA9IGNvbmZpZy52aWRlb0Jsb2I7XHJcbiAgICAgICAgdGhpcy5naWZCbG9iID0gY29uZmlnLmdpZkJsb2I7XHJcblxyXG4gICAgICAgIHRoaXMuaW5pdCgpO1xyXG5cclxuICAgICAgICByZXR1cm4gdGhpcztcclxuICAgIH0sXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgZnVuY3Rpb24gaXMgaW52b2tlZCBmb3IgYW55IGtub3duL3Vua25vd24gZXJyb3IuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgRGlza1N0b3JhZ2VcclxuICAgICAqIEBpbnRlcm5hbFxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIERpc2tTdG9yYWdlLm9uRXJyb3IgPSBmdW5jdGlvbihlcnJvcil7XHJcbiAgICAgKiAgICAgYWxlcm90KCBKU09OLnN0cmluZ2lmeShlcnJvcikgKTtcclxuICAgICAqIH07XHJcbiAgICAgKi9cclxuICAgIG9uRXJyb3I6IGZ1bmN0aW9uKGVycm9yKSB7XHJcbiAgICAgICAgY29uc29sZS5lcnJvcihKU09OLnN0cmluZ2lmeShlcnJvciwgbnVsbCwgJ1xcdCcpKTtcclxuICAgIH0sXHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBAcHJvcGVydHkge3N0cmluZ30gZGF0YVN0b3JlTmFtZSAtIE5hbWUgb2YgdGhlIE9iamVjdFN0b3JlIGNyZWF0ZWQgaW4gSW5kZXhlZERCIHN0b3JhZ2UuXHJcbiAgICAgKiBAbWVtYmVyb2YgRGlza1N0b3JhZ2VcclxuICAgICAqIEBpbnRlcm5hbFxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIERpc2tTdG9yYWdlLmRhdGFTdG9yZU5hbWUgPSAncmVjb3JkUlRDJztcclxuICAgICAqL1xyXG4gICAgZGF0YVN0b3JlTmFtZTogJ3JlY29yZFJUQycsXHJcbiAgICBkYk5hbWU6IG51bGxcclxufTtcclxuXHJcbmlmICh0eXBlb2YgUmVjb3JkUlRDICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgUmVjb3JkUlRDLkRpc2tTdG9yYWdlID0gRGlza1N0b3JhZ2U7XHJcbn1cblxyXG4vLyBfX19fX19fX19fX19fX1xyXG4vLyBHaWZSZWNvcmRlci5qc1xyXG5cclxuLyoqXHJcbiAqIEdpZlJlY29yZGVyIGlzIHN0YW5kYWxvbmUgY2Fsc3MgdXNlZCBieSB7QGxpbmsgUmVjb3JkUlRDfSB0byByZWNvcmQgdmlkZW8gb3IgY2FudmFzIGludG8gYW5pbWF0ZWQgZ2lmLlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQHR5cGVkZWYgR2lmUmVjb3JkZXJcclxuICogQGNsYXNzXHJcbiAqIEBleGFtcGxlXHJcbiAqIHZhciByZWNvcmRlciA9IG5ldyBHaWZSZWNvcmRlcihtZWRpYVN0cmVhbSB8fCBjYW52YXMgfHwgY29udGV4dCwgeyBvbkdpZlByZXZpZXc6IGZ1bmN0aW9uLCBvbkdpZlJlY29yZGluZ1N0YXJ0ZWQ6IGZ1bmN0aW9uLCB3aWR0aDogMTI4MCwgaGVpZ2h0OiA3MjAsIGZyYW1lUmF0ZTogMjAwLCBxdWFsaXR5OiAxMCB9KTtcclxuICogcmVjb3JkZXIucmVjb3JkKCk7XHJcbiAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oYmxvYikge1xyXG4gKiAgICAgaW1nLnNyYyA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAqIH0pO1xyXG4gKiBAc2VlIHtAbGluayBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQ3xSZWNvcmRSVEMgU291cmNlIENvZGV9XHJcbiAqIEBwYXJhbSB7TWVkaWFTdHJlYW19IG1lZGlhU3RyZWFtIC0gTWVkaWFTdHJlYW0gb2JqZWN0IG9yIEhUTUxDYW52YXNFbGVtZW50IG9yIENhbnZhc1JlbmRlcmluZ0NvbnRleHQyRC5cclxuICogQHBhcmFtIHtvYmplY3R9IGNvbmZpZyAtIHtkaXNhYmxlTG9nczp0cnVlLCBpbml0Q2FsbGJhY2s6IGZ1bmN0aW9uLCB3aWR0aDogMzIwLCBoZWlnaHQ6IDI0MCwgZnJhbWVSYXRlOiAyMDAsIHF1YWxpdHk6IDEwfVxyXG4gKi9cclxuXHJcbmZ1bmN0aW9uIEdpZlJlY29yZGVyKG1lZGlhU3RyZWFtLCBjb25maWcpIHtcclxuICAgIGlmICh0eXBlb2YgR0lGRW5jb2RlciA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICB2YXIgc2NyaXB0ID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnc2NyaXB0Jyk7XHJcbiAgICAgICAgc2NyaXB0LnNyYyA9ICdodHRwczovL3d3dy53ZWJydGMtZXhwZXJpbWVudC5jb20vZ2lmLXJlY29yZGVyLmpzJztcclxuICAgICAgICAoZG9jdW1lbnQuYm9keSB8fCBkb2N1bWVudC5kb2N1bWVudEVsZW1lbnQpLmFwcGVuZENoaWxkKHNjcmlwdCk7XHJcbiAgICB9XHJcblxyXG4gICAgY29uZmlnID0gY29uZmlnIHx8IHt9O1xyXG5cclxuICAgIHZhciBpc0hUTUxPYmplY3QgPSBtZWRpYVN0cmVhbSBpbnN0YW5jZW9mIENhbnZhc1JlbmRlcmluZ0NvbnRleHQyRCB8fCBtZWRpYVN0cmVhbSBpbnN0YW5jZW9mIEhUTUxDYW52YXNFbGVtZW50O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVjb3JkcyBNZWRpYVN0cmVhbS5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBHaWZSZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnJlY29yZCgpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnJlY29yZCA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlmICh0eXBlb2YgR0lGRW5jb2RlciA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAgICAgc2V0VGltZW91dChzZWxmLnJlY29yZCwgMTAwMCk7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICghaXNMb2FkZWRNZXRhRGF0YSkge1xyXG4gICAgICAgICAgICBzZXRUaW1lb3V0KHNlbGYucmVjb3JkLCAxMDAwKTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFpc0hUTUxPYmplY3QpIHtcclxuICAgICAgICAgICAgaWYgKCFjb25maWcud2lkdGgpIHtcclxuICAgICAgICAgICAgICAgIGNvbmZpZy53aWR0aCA9IHZpZGVvLm9mZnNldFdpZHRoIHx8IDMyMDtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKCFjb25maWcuaGVpZ2h0KSB7XHJcbiAgICAgICAgICAgICAgICBjb25maWcuaGVpZ2h0ID0gdmlkZW8ub2Zmc2V0SGVpZ2h0IHx8IDI0MDtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKCFjb25maWcudmlkZW8pIHtcclxuICAgICAgICAgICAgICAgIGNvbmZpZy52aWRlbyA9IHtcclxuICAgICAgICAgICAgICAgICAgICB3aWR0aDogY29uZmlnLndpZHRoLFxyXG4gICAgICAgICAgICAgICAgICAgIGhlaWdodDogY29uZmlnLmhlaWdodFxyXG4gICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKCFjb25maWcuY2FudmFzKSB7XHJcbiAgICAgICAgICAgICAgICBjb25maWcuY2FudmFzID0ge1xyXG4gICAgICAgICAgICAgICAgICAgIHdpZHRoOiBjb25maWcud2lkdGgsXHJcbiAgICAgICAgICAgICAgICAgICAgaGVpZ2h0OiBjb25maWcuaGVpZ2h0XHJcbiAgICAgICAgICAgICAgICB9O1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBjYW52YXMud2lkdGggPSBjb25maWcuY2FudmFzLndpZHRoIHx8IDMyMDtcclxuICAgICAgICAgICAgY2FudmFzLmhlaWdodCA9IGNvbmZpZy5jYW52YXMuaGVpZ2h0IHx8IDI0MDtcclxuXHJcbiAgICAgICAgICAgIHZpZGVvLndpZHRoID0gY29uZmlnLnZpZGVvLndpZHRoIHx8IDMyMDtcclxuICAgICAgICAgICAgdmlkZW8uaGVpZ2h0ID0gY29uZmlnLnZpZGVvLmhlaWdodCB8fCAyNDA7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICAvLyBleHRlcm5hbCBsaWJyYXJ5IHRvIHJlY29yZCBhcyBHSUYgaW1hZ2VzXHJcbiAgICAgICAgZ2lmRW5jb2RlciA9IG5ldyBHSUZFbmNvZGVyKCk7XHJcblxyXG4gICAgICAgIC8vIHZvaWQgc2V0UmVwZWF0KGludCBpdGVyKSBcclxuICAgICAgICAvLyBTZXRzIHRoZSBudW1iZXIgb2YgdGltZXMgdGhlIHNldCBvZiBHSUYgZnJhbWVzIHNob3VsZCBiZSBwbGF5ZWQuIFxyXG4gICAgICAgIC8vIERlZmF1bHQgaXMgMTsgMCBtZWFucyBwbGF5IGluZGVmaW5pdGVseS5cclxuICAgICAgICBnaWZFbmNvZGVyLnNldFJlcGVhdCgwKTtcclxuXHJcbiAgICAgICAgLy8gdm9pZCBzZXRGcmFtZVJhdGUoTnVtYmVyIGZwcykgXHJcbiAgICAgICAgLy8gU2V0cyBmcmFtZSByYXRlIGluIGZyYW1lcyBwZXIgc2Vjb25kLiBcclxuICAgICAgICAvLyBFcXVpdmFsZW50IHRvIHNldERlbGF5KDEwMDAvZnBzKS5cclxuICAgICAgICAvLyBVc2luZyBcInNldERlbGF5XCIgaW5zdGVhZCBvZiBcInNldEZyYW1lUmF0ZVwiXHJcbiAgICAgICAgZ2lmRW5jb2Rlci5zZXREZWxheShjb25maWcuZnJhbWVSYXRlIHx8IDIwMCk7XHJcblxyXG4gICAgICAgIC8vIHZvaWQgc2V0UXVhbGl0eShpbnQgcXVhbGl0eSkgXHJcbiAgICAgICAgLy8gU2V0cyBxdWFsaXR5IG9mIGNvbG9yIHF1YW50aXphdGlvbiAoY29udmVyc2lvbiBvZiBpbWFnZXMgdG8gdGhlIFxyXG4gICAgICAgIC8vIG1heGltdW0gMjU2IGNvbG9ycyBhbGxvd2VkIGJ5IHRoZSBHSUYgc3BlY2lmaWNhdGlvbikuIFxyXG4gICAgICAgIC8vIExvd2VyIHZhbHVlcyAobWluaW11bSA9IDEpIHByb2R1Y2UgYmV0dGVyIGNvbG9ycywgXHJcbiAgICAgICAgLy8gYnV0IHNsb3cgcHJvY2Vzc2luZyBzaWduaWZpY2FudGx5LiAxMCBpcyB0aGUgZGVmYXVsdCwgXHJcbiAgICAgICAgLy8gYW5kIHByb2R1Y2VzIGdvb2QgY29sb3IgbWFwcGluZyBhdCByZWFzb25hYmxlIHNwZWVkcy4gXHJcbiAgICAgICAgLy8gVmFsdWVzIGdyZWF0ZXIgdGhhbiAyMCBkbyBub3QgeWllbGQgc2lnbmlmaWNhbnQgaW1wcm92ZW1lbnRzIGluIHNwZWVkLlxyXG4gICAgICAgIGdpZkVuY29kZXIuc2V0UXVhbGl0eShjb25maWcucXVhbGl0eSB8fCAxMCk7XHJcblxyXG4gICAgICAgIC8vIEJvb2xlYW4gc3RhcnQoKSBcclxuICAgICAgICAvLyBUaGlzIHdyaXRlcyB0aGUgR0lGIEhlYWRlciBhbmQgcmV0dXJucyBmYWxzZSBpZiBpdCBmYWlscy5cclxuICAgICAgICBnaWZFbmNvZGVyLnN0YXJ0KCk7XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgY29uZmlnLm9uR2lmUmVjb3JkaW5nU3RhcnRlZCA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICBjb25maWcub25HaWZSZWNvcmRpbmdTdGFydGVkKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBzdGFydFRpbWUgPSBEYXRlLm5vdygpO1xyXG5cclxuICAgICAgICBmdW5jdGlvbiBkcmF3VmlkZW9GcmFtZSh0aW1lKSB7XHJcbiAgICAgICAgICAgIGlmIChzZWxmLmNsZWFyZWRSZWNvcmRlZERhdGEgPT09IHRydWUpIHtcclxuICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKGlzUGF1c2VkUmVjb3JkaW5nKSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gc2V0VGltZW91dChmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgICAgICBkcmF3VmlkZW9GcmFtZSh0aW1lKTtcclxuICAgICAgICAgICAgICAgIH0sIDEwMCk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGxhc3RBbmltYXRpb25GcmFtZSA9IHJlcXVlc3RBbmltYXRpb25GcmFtZShkcmF3VmlkZW9GcmFtZSk7XHJcblxyXG4gICAgICAgICAgICBpZiAodHlwZW9mIGxhc3RGcmFtZVRpbWUgPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgbGFzdEZyYW1lVGltZSA9IHRpbWU7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIC8vIH4xMCBmcHNcclxuICAgICAgICAgICAgaWYgKHRpbWUgLSBsYXN0RnJhbWVUaW1lIDwgOTApIHtcclxuICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKCFpc0hUTUxPYmplY3QgJiYgdmlkZW8ucGF1c2VkKSB7XHJcbiAgICAgICAgICAgICAgICAvLyB2aWE6IGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vV2ViUlRDLUV4cGVyaW1lbnQvcHVsbC8zMTZcclxuICAgICAgICAgICAgICAgIC8vIFR3ZWFrIGZvciBBbmRyb2lkIENocm9tZVxyXG4gICAgICAgICAgICAgICAgdmlkZW8ucGxheSgpO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBpZiAoIWlzSFRNTE9iamVjdCkge1xyXG4gICAgICAgICAgICAgICAgY29udGV4dC5kcmF3SW1hZ2UodmlkZW8sIDAsIDAsIGNhbnZhcy53aWR0aCwgY2FudmFzLmhlaWdodCk7XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmIChjb25maWcub25HaWZQcmV2aWV3KSB7XHJcbiAgICAgICAgICAgICAgICBjb25maWcub25HaWZQcmV2aWV3KGNhbnZhcy50b0RhdGFVUkwoJ2ltYWdlL3BuZycpKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgZ2lmRW5jb2Rlci5hZGRGcmFtZShjb250ZXh0KTtcclxuICAgICAgICAgICAgbGFzdEZyYW1lVGltZSA9IHRpbWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBsYXN0QW5pbWF0aW9uRnJhbWUgPSByZXF1ZXN0QW5pbWF0aW9uRnJhbWUoZHJhd1ZpZGVvRnJhbWUpO1xyXG5cclxuICAgICAgICBpZiAoY29uZmlnLmluaXRDYWxsYmFjaykge1xyXG4gICAgICAgICAgICBjb25maWcuaW5pdENhbGxiYWNrKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHN0b3BzIHJlY29yZGluZyBNZWRpYVN0cmVhbS5cclxuICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQ2FsbGJhY2sgZnVuY3Rpb24sIHRoYXQgaXMgdXNlZCB0byBwYXNzIHJlY29yZGVkIGJsb2IgYmFjayB0byB0aGUgY2FsbGVlLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIEdpZlJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbihibG9iKSB7XHJcbiAgICAgKiAgICAgaW1nLnNyYyA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5zdG9wID0gZnVuY3Rpb24oY2FsbGJhY2spIHtcclxuICAgICAgICBjYWxsYmFjayA9IGNhbGxiYWNrIHx8IGZ1bmN0aW9uKCkge307XHJcblxyXG4gICAgICAgIGlmIChsYXN0QW5pbWF0aW9uRnJhbWUpIHtcclxuICAgICAgICAgICAgY2FuY2VsQW5pbWF0aW9uRnJhbWUobGFzdEFuaW1hdGlvbkZyYW1lKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGVuZFRpbWUgPSBEYXRlLm5vdygpO1xyXG5cclxuICAgICAgICAvKipcclxuICAgICAgICAgKiBAcHJvcGVydHkge0Jsb2J9IGJsb2IgLSBUaGUgcmVjb3JkZWQgYmxvYiBvYmplY3QuXHJcbiAgICAgICAgICogQG1lbWJlcm9mIEdpZlJlY29yZGVyXHJcbiAgICAgICAgICogQGV4YW1wbGVcclxuICAgICAgICAgKiByZWNvcmRlci5zdG9wKGZ1bmN0aW9uKCl7XHJcbiAgICAgICAgICogICAgIHZhciBibG9iID0gcmVjb3JkZXIuYmxvYjtcclxuICAgICAgICAgKiB9KTtcclxuICAgICAgICAgKi9cclxuICAgICAgICB0aGlzLmJsb2IgPSBuZXcgQmxvYihbbmV3IFVpbnQ4QXJyYXkoZ2lmRW5jb2Rlci5zdHJlYW0oKS5iaW4pXSwge1xyXG4gICAgICAgICAgICB0eXBlOiAnaW1hZ2UvZ2lmJ1xyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBjYWxsYmFjayh0aGlzLmJsb2IpO1xyXG5cclxuICAgICAgICAvLyBidWc6IGZpbmQgYSB3YXkgdG8gY2xlYXIgb2xkIHJlY29yZGVkIGJsb2JzXHJcbiAgICAgICAgZ2lmRW5jb2Rlci5zdHJlYW0oKS5iaW4gPSBbXTtcclxuICAgIH07XHJcblxyXG4gICAgdmFyIGlzUGF1c2VkUmVjb3JkaW5nID0gZmFsc2U7XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBwYXVzZXMgdGhlIHJlY29yZGluZyBwcm9jZXNzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIEdpZlJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucGF1c2UoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5wYXVzZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlzUGF1c2VkUmVjb3JkaW5nID0gdHJ1ZTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXN1bWVzIHRoZSByZWNvcmRpbmcgcHJvY2Vzcy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBHaWZSZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnJlc3VtZSgpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnJlc3VtZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlzUGF1c2VkUmVjb3JkaW5nID0gZmFsc2U7XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVzZXRzIGN1cnJlbnRseSByZWNvcmRlZCBkYXRhLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIEdpZlJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuY2xlYXJSZWNvcmRlZERhdGEoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5jbGVhclJlY29yZGVkRGF0YSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHNlbGYuY2xlYXJlZFJlY29yZGVkRGF0YSA9IHRydWU7XHJcbiAgICAgICAgY2xlYXJSZWNvcmRlZERhdGFDQigpO1xyXG4gICAgfTtcclxuXHJcbiAgICBmdW5jdGlvbiBjbGVhclJlY29yZGVkRGF0YUNCKCkge1xyXG4gICAgICAgIGlmIChnaWZFbmNvZGVyKSB7XHJcbiAgICAgICAgICAgIGdpZkVuY29kZXIuc3RyZWFtKCkuYmluID0gW107XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIC8vIGZvciBkZWJ1Z2dpbmdcclxuICAgIHRoaXMubmFtZSA9ICdHaWZSZWNvcmRlcic7XHJcbiAgICB0aGlzLnRvU3RyaW5nID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIHRoaXMubmFtZTtcclxuICAgIH07XHJcblxyXG4gICAgdmFyIGNhbnZhcyA9IGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoJ2NhbnZhcycpO1xyXG4gICAgdmFyIGNvbnRleHQgPSBjYW52YXMuZ2V0Q29udGV4dCgnMmQnKTtcclxuXHJcbiAgICBpZiAoaXNIVE1MT2JqZWN0KSB7XHJcbiAgICAgICAgaWYgKG1lZGlhU3RyZWFtIGluc3RhbmNlb2YgQ2FudmFzUmVuZGVyaW5nQ29udGV4dDJEKSB7XHJcbiAgICAgICAgICAgIGNvbnRleHQgPSBtZWRpYVN0cmVhbTtcclxuICAgICAgICAgICAgY2FudmFzID0gY29udGV4dC5jYW52YXM7XHJcbiAgICAgICAgfSBlbHNlIGlmIChtZWRpYVN0cmVhbSBpbnN0YW5jZW9mIEhUTUxDYW52YXNFbGVtZW50KSB7XHJcbiAgICAgICAgICAgIGNvbnRleHQgPSBtZWRpYVN0cmVhbS5nZXRDb250ZXh0KCcyZCcpO1xyXG4gICAgICAgICAgICBjYW52YXMgPSBtZWRpYVN0cmVhbTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgdmFyIGlzTG9hZGVkTWV0YURhdGEgPSB0cnVlO1xyXG5cclxuICAgIGlmICghaXNIVE1MT2JqZWN0KSB7XHJcbiAgICAgICAgdmFyIHZpZGVvID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgndmlkZW8nKTtcclxuICAgICAgICB2aWRlby5tdXRlZCA9IHRydWU7XHJcbiAgICAgICAgdmlkZW8uYXV0b3BsYXkgPSB0cnVlO1xyXG4gICAgICAgIHZpZGVvLnBsYXlzSW5saW5lID0gdHJ1ZTtcclxuXHJcbiAgICAgICAgaXNMb2FkZWRNZXRhRGF0YSA9IGZhbHNlO1xyXG4gICAgICAgIHZpZGVvLm9ubG9hZGVkbWV0YWRhdGEgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgaXNMb2FkZWRNZXRhRGF0YSA9IHRydWU7XHJcbiAgICAgICAgfTtcclxuXHJcbiAgICAgICAgc2V0U3JjT2JqZWN0KG1lZGlhU3RyZWFtLCB2aWRlbyk7XHJcblxyXG4gICAgICAgIHZpZGVvLnBsYXkoKTtcclxuICAgIH1cclxuXHJcbiAgICB2YXIgbGFzdEFuaW1hdGlvbkZyYW1lID0gbnVsbDtcclxuICAgIHZhciBzdGFydFRpbWUsIGVuZFRpbWUsIGxhc3RGcmFtZVRpbWU7XHJcblxyXG4gICAgdmFyIGdpZkVuY29kZXI7XHJcblxyXG4gICAgdmFyIHNlbGYgPSB0aGlzO1xyXG59XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIFJlY29yZFJUQy5HaWZSZWNvcmRlciA9IEdpZlJlY29yZGVyO1xyXG59XG5cclxuLy8gTGFzdCB0aW1lIHVwZGF0ZWQ6IDIwMTktMDYtMjEgNDowOTo0MiBBTSBVVENcclxuXHJcbi8vIF9fX19fX19fX19fX19fX19fX19fX19fX1xyXG4vLyBNdWx0aVN0cmVhbXNNaXhlciB2MS4yLjJcclxuXHJcbi8vIE9wZW4tU291cmNlZDogaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9NdWx0aVN0cmVhbXNNaXhlclxyXG5cclxuLy8gLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS1cclxuLy8gTXVheiBLaGFuICAgICAtIHd3dy5NdWF6S2hhbi5jb21cclxuLy8gTUlUIExpY2Vuc2UgICAtIHd3dy5XZWJSVEMtRXhwZXJpbWVudC5jb20vbGljZW5jZVxyXG4vLyAtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLVxyXG5cclxuZnVuY3Rpb24gTXVsdGlTdHJlYW1zTWl4ZXIoYXJyYXlPZk1lZGlhU3RyZWFtcywgZWxlbWVudENsYXNzKSB7XHJcblxyXG4gICAgdmFyIGJyb3dzZXJGYWtlVXNlckFnZW50ID0gJ0Zha2UvNS4wIChGYWtlT1MpIEFwcGxlV2ViS2l0LzEyMyAoS0hUTUwsIGxpa2UgR2Vja28pIEZha2UvMTIuMy40NTY3Ljg5IEZha2UvMTIzLjQ1JztcclxuXHJcbiAgICAoZnVuY3Rpb24odGhhdCkge1xyXG4gICAgICAgIGlmICh0eXBlb2YgUmVjb3JkUlRDICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoIXRoYXQpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiB3aW5kb3cgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgZ2xvYmFsID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBnbG9iYWwubmF2aWdhdG9yID0ge1xyXG4gICAgICAgICAgICB1c2VyQWdlbnQ6IGJyb3dzZXJGYWtlVXNlckFnZW50LFxyXG4gICAgICAgICAgICBnZXRVc2VyTWVkaWE6IGZ1bmN0aW9uKCkge31cclxuICAgICAgICB9O1xyXG5cclxuICAgICAgICBpZiAoIWdsb2JhbC5jb25zb2xlKSB7XHJcbiAgICAgICAgICAgIGdsb2JhbC5jb25zb2xlID0ge307XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAodHlwZW9mIGdsb2JhbC5jb25zb2xlLmxvZyA9PT0gJ3VuZGVmaW5lZCcgfHwgdHlwZW9mIGdsb2JhbC5jb25zb2xlLmVycm9yID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICBnbG9iYWwuY29uc29sZS5lcnJvciA9IGdsb2JhbC5jb25zb2xlLmxvZyA9IGdsb2JhbC5jb25zb2xlLmxvZyB8fCBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGFyZ3VtZW50cyk7XHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAodHlwZW9mIGRvY3VtZW50ID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICAvKmdsb2JhbCBkb2N1bWVudDp0cnVlICovXHJcbiAgICAgICAgICAgIHRoYXQuZG9jdW1lbnQgPSB7XHJcbiAgICAgICAgICAgICAgICBkb2N1bWVudEVsZW1lbnQ6IHtcclxuICAgICAgICAgICAgICAgICAgICBhcHBlbmRDaGlsZDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJldHVybiAnJztcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH07XHJcblxyXG4gICAgICAgICAgICBkb2N1bWVudC5jcmVhdGVFbGVtZW50ID0gZG9jdW1lbnQuY2FwdHVyZVN0cmVhbSA9IGRvY3VtZW50Lm1vekNhcHR1cmVTdHJlYW0gPSBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgICAgIHZhciBvYmogPSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZ2V0Q29udGV4dDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJldHVybiBvYmo7XHJcbiAgICAgICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgICAgICBwbGF5OiBmdW5jdGlvbigpIHt9LFxyXG4gICAgICAgICAgICAgICAgICAgIHBhdXNlOiBmdW5jdGlvbigpIHt9LFxyXG4gICAgICAgICAgICAgICAgICAgIGRyYXdJbWFnZTogZnVuY3Rpb24oKSB7fSxcclxuICAgICAgICAgICAgICAgICAgICB0b0RhdGFVUkw6IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICByZXR1cm4gJyc7XHJcbiAgICAgICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgICAgICBzdHlsZToge31cclxuICAgICAgICAgICAgICAgIH07XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gb2JqO1xyXG4gICAgICAgICAgICB9O1xyXG5cclxuICAgICAgICAgICAgdGhhdC5IVE1MVmlkZW9FbGVtZW50ID0gZnVuY3Rpb24oKSB7fTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgbG9jYXRpb24gPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIC8qZ2xvYmFsIGxvY2F0aW9uOnRydWUgKi9cclxuICAgICAgICAgICAgdGhhdC5sb2NhdGlvbiA9IHtcclxuICAgICAgICAgICAgICAgIHByb3RvY29sOiAnZmlsZTonLFxyXG4gICAgICAgICAgICAgICAgaHJlZjogJycsXHJcbiAgICAgICAgICAgICAgICBoYXNoOiAnJ1xyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiBzY3JlZW4gPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIC8qZ2xvYmFsIHNjcmVlbjp0cnVlICovXHJcbiAgICAgICAgICAgIHRoYXQuc2NyZWVuID0ge1xyXG4gICAgICAgICAgICAgICAgd2lkdGg6IDAsXHJcbiAgICAgICAgICAgICAgICBoZWlnaHQ6IDBcclxuICAgICAgICAgICAgfTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgVVJMID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICAvKmdsb2JhbCBzY3JlZW46dHJ1ZSAqL1xyXG4gICAgICAgICAgICB0aGF0LlVSTCA9IHtcclxuICAgICAgICAgICAgICAgIGNyZWF0ZU9iamVjdFVSTDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuICcnO1xyXG4gICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgIHJldm9rZU9iamVjdFVSTDogZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuICcnO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgLypnbG9iYWwgd2luZG93OnRydWUgKi9cclxuICAgICAgICB0aGF0LndpbmRvdyA9IGdsb2JhbDtcclxuICAgIH0pKHR5cGVvZiBnbG9iYWwgIT09ICd1bmRlZmluZWQnID8gZ2xvYmFsIDogbnVsbCk7XHJcblxyXG4gICAgLy8gcmVxdWlyZXM6IGNocm9tZTovL2ZsYWdzLyNlbmFibGUtZXhwZXJpbWVudGFsLXdlYi1wbGF0Zm9ybS1mZWF0dXJlc1xyXG5cclxuICAgIGVsZW1lbnRDbGFzcyA9IGVsZW1lbnRDbGFzcyB8fCAnbXVsdGktc3RyZWFtcy1taXhlcic7XHJcblxyXG4gICAgdmFyIHZpZGVvcyA9IFtdO1xyXG4gICAgdmFyIGlzU3RvcERyYXdpbmdGcmFtZXMgPSBmYWxzZTtcclxuXHJcbiAgICB2YXIgY2FudmFzID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnY2FudmFzJyk7XHJcbiAgICB2YXIgY29udGV4dCA9IGNhbnZhcy5nZXRDb250ZXh0KCcyZCcpO1xyXG4gICAgY2FudmFzLnN0eWxlLm9wYWNpdHkgPSAwO1xyXG4gICAgY2FudmFzLnN0eWxlLnBvc2l0aW9uID0gJ2Fic29sdXRlJztcclxuICAgIGNhbnZhcy5zdHlsZS56SW5kZXggPSAtMTtcclxuICAgIGNhbnZhcy5zdHlsZS50b3AgPSAnLTEwMDBlbSc7XHJcbiAgICBjYW52YXMuc3R5bGUubGVmdCA9ICctMTAwMGVtJztcclxuICAgIGNhbnZhcy5jbGFzc05hbWUgPSBlbGVtZW50Q2xhc3M7XHJcbiAgICAoZG9jdW1lbnQuYm9keSB8fCBkb2N1bWVudC5kb2N1bWVudEVsZW1lbnQpLmFwcGVuZENoaWxkKGNhbnZhcyk7XHJcblxyXG4gICAgdGhpcy5kaXNhYmxlTG9ncyA9IGZhbHNlO1xyXG4gICAgdGhpcy5mcmFtZUludGVydmFsID0gMTA7XHJcblxyXG4gICAgdGhpcy53aWR0aCA9IDM2MDtcclxuICAgIHRoaXMuaGVpZ2h0ID0gMjQwO1xyXG5cclxuICAgIC8vIHVzZSBnYWluIG5vZGUgdG8gcHJldmVudCBlY2hvXHJcbiAgICB0aGlzLnVzZUdhaW5Ob2RlID0gdHJ1ZTtcclxuXHJcbiAgICB2YXIgc2VsZiA9IHRoaXM7XHJcblxyXG4gICAgLy8gX19fX19fX19fX19fX19fX19fX19fX19fX19fX19cclxuICAgIC8vIENyb3NzLUJyb3dzZXItRGVjbGFyYXRpb25zLmpzXHJcblxyXG4gICAgLy8gV2ViQXVkaW8gQVBJIHJlcHJlc2VudGVyXHJcbiAgICB2YXIgQXVkaW9Db250ZXh0ID0gd2luZG93LkF1ZGlvQ29udGV4dDtcclxuXHJcbiAgICBpZiAodHlwZW9mIEF1ZGlvQ29udGV4dCA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICBpZiAodHlwZW9mIHdlYmtpdEF1ZGlvQ29udGV4dCAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICAgICAgLypnbG9iYWwgQXVkaW9Db250ZXh0OnRydWUgKi9cclxuICAgICAgICAgICAgQXVkaW9Db250ZXh0ID0gd2Via2l0QXVkaW9Db250ZXh0O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiBtb3pBdWRpb0NvbnRleHQgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIC8qZ2xvYmFsIEF1ZGlvQ29udGV4dDp0cnVlICovXHJcbiAgICAgICAgICAgIEF1ZGlvQ29udGV4dCA9IG1vekF1ZGlvQ29udGV4dDtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgLypqc2hpbnQgLVcwNzkgKi9cclxuICAgIHZhciBVUkwgPSB3aW5kb3cuVVJMO1xyXG5cclxuICAgIGlmICh0eXBlb2YgVVJMID09PSAndW5kZWZpbmVkJyAmJiB0eXBlb2Ygd2Via2l0VVJMICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIC8qZ2xvYmFsIFVSTDp0cnVlICovXHJcbiAgICAgICAgVVJMID0gd2Via2l0VVJMO1xyXG4gICAgfVxyXG5cclxuICAgIGlmICh0eXBlb2YgbmF2aWdhdG9yICE9PSAndW5kZWZpbmVkJyAmJiB0eXBlb2YgbmF2aWdhdG9yLmdldFVzZXJNZWRpYSA9PT0gJ3VuZGVmaW5lZCcpIHsgLy8gbWF5YmUgd2luZG93Lm5hdmlnYXRvcj9cclxuICAgICAgICBpZiAodHlwZW9mIG5hdmlnYXRvci53ZWJraXRHZXRVc2VyTWVkaWEgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIG5hdmlnYXRvci5nZXRVc2VyTWVkaWEgPSBuYXZpZ2F0b3Iud2Via2l0R2V0VXNlck1lZGlhO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiBuYXZpZ2F0b3IubW96R2V0VXNlck1lZGlhICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICBuYXZpZ2F0b3IuZ2V0VXNlck1lZGlhID0gbmF2aWdhdG9yLm1vekdldFVzZXJNZWRpYTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgdmFyIE1lZGlhU3RyZWFtID0gd2luZG93Lk1lZGlhU3RyZWFtO1xyXG5cclxuICAgIGlmICh0eXBlb2YgTWVkaWFTdHJlYW0gPT09ICd1bmRlZmluZWQnICYmIHR5cGVvZiB3ZWJraXRNZWRpYVN0cmVhbSAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgICAgICBNZWRpYVN0cmVhbSA9IHdlYmtpdE1lZGlhU3RyZWFtO1xyXG4gICAgfVxyXG5cclxuICAgIC8qZ2xvYmFsIE1lZGlhU3RyZWFtOnRydWUgKi9cclxuICAgIGlmICh0eXBlb2YgTWVkaWFTdHJlYW0gIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLy8gb3ZlcnJpZGUgXCJzdG9wXCIgbWV0aG9kIGZvciBhbGwgYnJvd3NlcnNcclxuICAgICAgICBpZiAodHlwZW9mIE1lZGlhU3RyZWFtLnByb3RvdHlwZS5zdG9wID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICBNZWRpYVN0cmVhbS5wcm90b3R5cGUuc3RvcCA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5nZXRUcmFja3MoKS5mb3JFYWNoKGZ1bmN0aW9uKHRyYWNrKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdHJhY2suc3RvcCgpO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIHZhciBTdG9yYWdlID0ge307XHJcblxyXG4gICAgaWYgKHR5cGVvZiBBdWRpb0NvbnRleHQgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgU3RvcmFnZS5BdWRpb0NvbnRleHQgPSBBdWRpb0NvbnRleHQ7XHJcbiAgICB9IGVsc2UgaWYgKHR5cGVvZiB3ZWJraXRBdWRpb0NvbnRleHQgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgU3RvcmFnZS5BdWRpb0NvbnRleHQgPSB3ZWJraXRBdWRpb0NvbnRleHQ7XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gc2V0U3JjT2JqZWN0KHN0cmVhbSwgZWxlbWVudCkge1xyXG4gICAgICAgIGlmICgnc3JjT2JqZWN0JyBpbiBlbGVtZW50KSB7XHJcbiAgICAgICAgICAgIGVsZW1lbnQuc3JjT2JqZWN0ID0gc3RyZWFtO1xyXG4gICAgICAgIH0gZWxzZSBpZiAoJ21velNyY09iamVjdCcgaW4gZWxlbWVudCkge1xyXG4gICAgICAgICAgICBlbGVtZW50Lm1velNyY09iamVjdCA9IHN0cmVhbTtcclxuICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICBlbGVtZW50LnNyY09iamVjdCA9IHN0cmVhbTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgdGhpcy5zdGFydERyYXdpbmdGcmFtZXMgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBkcmF3VmlkZW9zVG9DYW52YXMoKTtcclxuICAgIH07XHJcblxyXG4gICAgZnVuY3Rpb24gZHJhd1ZpZGVvc1RvQ2FudmFzKCkge1xyXG4gICAgICAgIGlmIChpc1N0b3BEcmF3aW5nRnJhbWVzKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHZhciB2aWRlb3NMZW5ndGggPSB2aWRlb3MubGVuZ3RoO1xyXG5cclxuICAgICAgICB2YXIgZnVsbGNhbnZhcyA9IGZhbHNlO1xyXG4gICAgICAgIHZhciByZW1haW5pbmcgPSBbXTtcclxuICAgICAgICB2aWRlb3MuZm9yRWFjaChmdW5jdGlvbih2aWRlbykge1xyXG4gICAgICAgICAgICBpZiAoIXZpZGVvLnN0cmVhbSkge1xyXG4gICAgICAgICAgICAgICAgdmlkZW8uc3RyZWFtID0ge307XHJcbiAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIGlmICh2aWRlby5zdHJlYW0uZnVsbGNhbnZhcykge1xyXG4gICAgICAgICAgICAgICAgZnVsbGNhbnZhcyA9IHZpZGVvO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgLy8gdG9kbzogdmlkZW8uc3RyZWFtLmFjdGl2ZSBvciB2aWRlby5zdHJlYW0ubGl2ZSB0byBmaXggYmxhbmsgZnJhbWVzIGlzc3Vlcz9cclxuICAgICAgICAgICAgICAgIHJlbWFpbmluZy5wdXNoKHZpZGVvKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBpZiAoZnVsbGNhbnZhcykge1xyXG4gICAgICAgICAgICBjYW52YXMud2lkdGggPSBmdWxsY2FudmFzLnN0cmVhbS53aWR0aDtcclxuICAgICAgICAgICAgY2FudmFzLmhlaWdodCA9IGZ1bGxjYW52YXMuc3RyZWFtLmhlaWdodDtcclxuICAgICAgICB9IGVsc2UgaWYgKHJlbWFpbmluZy5sZW5ndGgpIHtcclxuICAgICAgICAgICAgY2FudmFzLndpZHRoID0gdmlkZW9zTGVuZ3RoID4gMSA/IHJlbWFpbmluZ1swXS53aWR0aCAqIDIgOiByZW1haW5pbmdbMF0ud2lkdGg7XHJcblxyXG4gICAgICAgICAgICB2YXIgaGVpZ2h0ID0gMTtcclxuICAgICAgICAgICAgaWYgKHZpZGVvc0xlbmd0aCA9PT0gMyB8fCB2aWRlb3NMZW5ndGggPT09IDQpIHtcclxuICAgICAgICAgICAgICAgIGhlaWdodCA9IDI7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKHZpZGVvc0xlbmd0aCA9PT0gNSB8fCB2aWRlb3NMZW5ndGggPT09IDYpIHtcclxuICAgICAgICAgICAgICAgIGhlaWdodCA9IDM7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKHZpZGVvc0xlbmd0aCA9PT0gNyB8fCB2aWRlb3NMZW5ndGggPT09IDgpIHtcclxuICAgICAgICAgICAgICAgIGhlaWdodCA9IDQ7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKHZpZGVvc0xlbmd0aCA9PT0gOSB8fCB2aWRlb3NMZW5ndGggPT09IDEwKSB7XHJcbiAgICAgICAgICAgICAgICBoZWlnaHQgPSA1O1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGNhbnZhcy5oZWlnaHQgPSByZW1haW5pbmdbMF0uaGVpZ2h0ICogaGVpZ2h0O1xyXG4gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIGNhbnZhcy53aWR0aCA9IHNlbGYud2lkdGggfHwgMzYwO1xyXG4gICAgICAgICAgICBjYW52YXMuaGVpZ2h0ID0gc2VsZi5oZWlnaHQgfHwgMjQwO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKGZ1bGxjYW52YXMgJiYgZnVsbGNhbnZhcyBpbnN0YW5jZW9mIEhUTUxWaWRlb0VsZW1lbnQpIHtcclxuICAgICAgICAgICAgZHJhd0ltYWdlKGZ1bGxjYW52YXMpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgcmVtYWluaW5nLmZvckVhY2goZnVuY3Rpb24odmlkZW8sIGlkeCkge1xyXG4gICAgICAgICAgICBkcmF3SW1hZ2UodmlkZW8sIGlkeCk7XHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIHNldFRpbWVvdXQoZHJhd1ZpZGVvc1RvQ2FudmFzLCBzZWxmLmZyYW1lSW50ZXJ2YWwpO1xyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIGRyYXdJbWFnZSh2aWRlbywgaWR4KSB7XHJcbiAgICAgICAgaWYgKGlzU3RvcERyYXdpbmdGcmFtZXMpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgdmFyIHggPSAwO1xyXG4gICAgICAgIHZhciB5ID0gMDtcclxuICAgICAgICB2YXIgd2lkdGggPSB2aWRlby53aWR0aDtcclxuICAgICAgICB2YXIgaGVpZ2h0ID0gdmlkZW8uaGVpZ2h0O1xyXG5cclxuICAgICAgICBpZiAoaWR4ID09PSAxKSB7XHJcbiAgICAgICAgICAgIHggPSB2aWRlby53aWR0aDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpZHggPT09IDIpIHtcclxuICAgICAgICAgICAgeSA9IHZpZGVvLmhlaWdodDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpZHggPT09IDMpIHtcclxuICAgICAgICAgICAgeCA9IHZpZGVvLndpZHRoO1xyXG4gICAgICAgICAgICB5ID0gdmlkZW8uaGVpZ2h0O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKGlkeCA9PT0gNCkge1xyXG4gICAgICAgICAgICB5ID0gdmlkZW8uaGVpZ2h0ICogMjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpZHggPT09IDUpIHtcclxuICAgICAgICAgICAgeCA9IHZpZGVvLndpZHRoO1xyXG4gICAgICAgICAgICB5ID0gdmlkZW8uaGVpZ2h0ICogMjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChpZHggPT09IDYpIHtcclxuICAgICAgICAgICAgeSA9IHZpZGVvLmhlaWdodCAqIDM7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoaWR4ID09PSA3KSB7XHJcbiAgICAgICAgICAgIHggPSB2aWRlby53aWR0aDtcclxuICAgICAgICAgICAgeSA9IHZpZGVvLmhlaWdodCAqIDM7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAodHlwZW9mIHZpZGVvLnN0cmVhbS5sZWZ0ICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICB4ID0gdmlkZW8uc3RyZWFtLmxlZnQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAodHlwZW9mIHZpZGVvLnN0cmVhbS50b3AgIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIHkgPSB2aWRlby5zdHJlYW0udG9wO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiB2aWRlby5zdHJlYW0ud2lkdGggIT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgICAgIHdpZHRoID0gdmlkZW8uc3RyZWFtLndpZHRoO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHR5cGVvZiB2aWRlby5zdHJlYW0uaGVpZ2h0ICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgICAgICBoZWlnaHQgPSB2aWRlby5zdHJlYW0uaGVpZ2h0O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgY29udGV4dC5kcmF3SW1hZ2UodmlkZW8sIHgsIHksIHdpZHRoLCBoZWlnaHQpO1xyXG5cclxuICAgICAgICBpZiAodHlwZW9mIHZpZGVvLnN0cmVhbS5vblJlbmRlciA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICB2aWRlby5zdHJlYW0ub25SZW5kZXIoY29udGV4dCwgeCwgeSwgd2lkdGgsIGhlaWdodCwgaWR4KTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZnVuY3Rpb24gZ2V0TWl4ZWRTdHJlYW0oKSB7XHJcbiAgICAgICAgaXNTdG9wRHJhd2luZ0ZyYW1lcyA9IGZhbHNlO1xyXG4gICAgICAgIHZhciBtaXhlZFZpZGVvU3RyZWFtID0gZ2V0TWl4ZWRWaWRlb1N0cmVhbSgpO1xyXG5cclxuICAgICAgICB2YXIgbWl4ZWRBdWRpb1N0cmVhbSA9IGdldE1peGVkQXVkaW9TdHJlYW0oKTtcclxuICAgICAgICBpZiAobWl4ZWRBdWRpb1N0cmVhbSkge1xyXG4gICAgICAgICAgICBtaXhlZEF1ZGlvU3RyZWFtLmdldFRyYWNrcygpLmZpbHRlcihmdW5jdGlvbih0KSB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gdC5raW5kID09PSAnYXVkaW8nO1xyXG4gICAgICAgICAgICB9KS5mb3JFYWNoKGZ1bmN0aW9uKHRyYWNrKSB7XHJcbiAgICAgICAgICAgICAgICBtaXhlZFZpZGVvU3RyZWFtLmFkZFRyYWNrKHRyYWNrKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICB2YXIgZnVsbGNhbnZhcztcclxuICAgICAgICBhcnJheU9mTWVkaWFTdHJlYW1zLmZvckVhY2goZnVuY3Rpb24oc3RyZWFtKSB7XHJcbiAgICAgICAgICAgIGlmIChzdHJlYW0uZnVsbGNhbnZhcykge1xyXG4gICAgICAgICAgICAgICAgZnVsbGNhbnZhcyA9IHRydWU7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgLy8gbWl4ZWRWaWRlb1N0cmVhbS5wcm90b3R5cGUuYXBwZW5kU3RyZWFtcyA9IGFwcGVuZFN0cmVhbXM7XHJcbiAgICAgICAgLy8gbWl4ZWRWaWRlb1N0cmVhbS5wcm90b3R5cGUucmVzZXRWaWRlb1N0cmVhbXMgPSByZXNldFZpZGVvU3RyZWFtcztcclxuICAgICAgICAvLyBtaXhlZFZpZGVvU3RyZWFtLnByb3RvdHlwZS5jbGVhclJlY29yZGVkRGF0YSA9IGNsZWFyUmVjb3JkZWREYXRhO1xyXG5cclxuICAgICAgICByZXR1cm4gbWl4ZWRWaWRlb1N0cmVhbTtcclxuICAgIH1cclxuXHJcbiAgICBmdW5jdGlvbiBnZXRNaXhlZFZpZGVvU3RyZWFtKCkge1xyXG4gICAgICAgIHJlc2V0VmlkZW9TdHJlYW1zKCk7XHJcblxyXG4gICAgICAgIHZhciBjYXB0dXJlZFN0cmVhbTtcclxuXHJcbiAgICAgICAgaWYgKCdjYXB0dXJlU3RyZWFtJyBpbiBjYW52YXMpIHtcclxuICAgICAgICAgICAgY2FwdHVyZWRTdHJlYW0gPSBjYW52YXMuY2FwdHVyZVN0cmVhbSgpO1xyXG4gICAgICAgIH0gZWxzZSBpZiAoJ21vekNhcHR1cmVTdHJlYW0nIGluIGNhbnZhcykge1xyXG4gICAgICAgICAgICBjYXB0dXJlZFN0cmVhbSA9IGNhbnZhcy5tb3pDYXB0dXJlU3RyZWFtKCk7XHJcbiAgICAgICAgfSBlbHNlIGlmICghc2VsZi5kaXNhYmxlTG9ncykge1xyXG4gICAgICAgICAgICBjb25zb2xlLmVycm9yKCdVcGdyYWRlIHRvIGxhdGVzdCBDaHJvbWUgb3Igb3RoZXJ3aXNlIGVuYWJsZSB0aGlzIGZsYWc6IGNocm9tZTovL2ZsYWdzLyNlbmFibGUtZXhwZXJpbWVudGFsLXdlYi1wbGF0Zm9ybS1mZWF0dXJlcycpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgdmFyIHZpZGVvU3RyZWFtID0gbmV3IE1lZGlhU3RyZWFtKCk7XHJcblxyXG4gICAgICAgIGNhcHR1cmVkU3RyZWFtLmdldFRyYWNrcygpLmZpbHRlcihmdW5jdGlvbih0KSB7XHJcbiAgICAgICAgICAgIHJldHVybiB0LmtpbmQgPT09ICd2aWRlbyc7XHJcbiAgICAgICAgfSkuZm9yRWFjaChmdW5jdGlvbih0cmFjaykge1xyXG4gICAgICAgICAgICB2aWRlb1N0cmVhbS5hZGRUcmFjayh0cmFjayk7XHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIGNhbnZhcy5zdHJlYW0gPSB2aWRlb1N0cmVhbTtcclxuXHJcbiAgICAgICAgcmV0dXJuIHZpZGVvU3RyZWFtO1xyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIGdldE1peGVkQXVkaW9TdHJlYW0oKSB7XHJcbiAgICAgICAgLy8gdmlhOiBAcGVocnNvbnNcclxuICAgICAgICBpZiAoIVN0b3JhZ2UuQXVkaW9Db250ZXh0Q29uc3RydWN0b3IpIHtcclxuICAgICAgICAgICAgU3RvcmFnZS5BdWRpb0NvbnRleHRDb25zdHJ1Y3RvciA9IG5ldyBTdG9yYWdlLkF1ZGlvQ29udGV4dCgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc2VsZi5hdWRpb0NvbnRleHQgPSBTdG9yYWdlLkF1ZGlvQ29udGV4dENvbnN0cnVjdG9yO1xyXG5cclxuICAgICAgICBzZWxmLmF1ZGlvU291cmNlcyA9IFtdO1xyXG5cclxuICAgICAgICBpZiAoc2VsZi51c2VHYWluTm9kZSA9PT0gdHJ1ZSkge1xyXG4gICAgICAgICAgICBzZWxmLmdhaW5Ob2RlID0gc2VsZi5hdWRpb0NvbnRleHQuY3JlYXRlR2FpbigpO1xyXG4gICAgICAgICAgICBzZWxmLmdhaW5Ob2RlLmNvbm5lY3Qoc2VsZi5hdWRpb0NvbnRleHQuZGVzdGluYXRpb24pO1xyXG4gICAgICAgICAgICBzZWxmLmdhaW5Ob2RlLmdhaW4udmFsdWUgPSAwOyAvLyBkb24ndCBoZWFyIHNlbGZcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHZhciBhdWRpb1RyYWNrc0xlbmd0aCA9IDA7XHJcbiAgICAgICAgYXJyYXlPZk1lZGlhU3RyZWFtcy5mb3JFYWNoKGZ1bmN0aW9uKHN0cmVhbSkge1xyXG4gICAgICAgICAgICBpZiAoIXN0cmVhbS5nZXRUcmFja3MoKS5maWx0ZXIoZnVuY3Rpb24odCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiB0LmtpbmQgPT09ICdhdWRpbyc7XHJcbiAgICAgICAgICAgICAgICB9KS5sZW5ndGgpIHtcclxuICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgYXVkaW9UcmFja3NMZW5ndGgrKztcclxuXHJcbiAgICAgICAgICAgIHZhciBhdWRpb1NvdXJjZSA9IHNlbGYuYXVkaW9Db250ZXh0LmNyZWF0ZU1lZGlhU3RyZWFtU291cmNlKHN0cmVhbSk7XHJcblxyXG4gICAgICAgICAgICBpZiAoc2VsZi51c2VHYWluTm9kZSA9PT0gdHJ1ZSkge1xyXG4gICAgICAgICAgICAgICAgYXVkaW9Tb3VyY2UuY29ubmVjdChzZWxmLmdhaW5Ob2RlKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgc2VsZi5hdWRpb1NvdXJjZXMucHVzaChhdWRpb1NvdXJjZSk7XHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIGlmICghYXVkaW9UcmFja3NMZW5ndGgpIHtcclxuICAgICAgICAgICAgLy8gYmVjYXVzZSBcInNlbGYuYXVkaW9Db250ZXh0XCIgaXMgbm90IGluaXRpYWxpemVkXHJcbiAgICAgICAgICAgIC8vIHRoYXQncyB3aHkgd2UndmUgdG8gaWdub3JlIHJlc3Qgb2YgdGhlIGNvZGVcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc2VsZi5hdWRpb0Rlc3RpbmF0aW9uID0gc2VsZi5hdWRpb0NvbnRleHQuY3JlYXRlTWVkaWFTdHJlYW1EZXN0aW5hdGlvbigpO1xyXG4gICAgICAgIHNlbGYuYXVkaW9Tb3VyY2VzLmZvckVhY2goZnVuY3Rpb24oYXVkaW9Tb3VyY2UpIHtcclxuICAgICAgICAgICAgYXVkaW9Tb3VyY2UuY29ubmVjdChzZWxmLmF1ZGlvRGVzdGluYXRpb24pO1xyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHJldHVybiBzZWxmLmF1ZGlvRGVzdGluYXRpb24uc3RyZWFtO1xyXG4gICAgfVxyXG5cclxuICAgIGZ1bmN0aW9uIGdldFZpZGVvKHN0cmVhbSkge1xyXG4gICAgICAgIHZhciB2aWRlbyA9IGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoJ3ZpZGVvJyk7XHJcblxyXG4gICAgICAgIHNldFNyY09iamVjdChzdHJlYW0sIHZpZGVvKTtcclxuXHJcbiAgICAgICAgdmlkZW8uY2xhc3NOYW1lID0gZWxlbWVudENsYXNzO1xyXG5cclxuICAgICAgICB2aWRlby5tdXRlZCA9IHRydWU7XHJcbiAgICAgICAgdmlkZW8udm9sdW1lID0gMDtcclxuXHJcbiAgICAgICAgdmlkZW8ud2lkdGggPSBzdHJlYW0ud2lkdGggfHwgc2VsZi53aWR0aCB8fCAzNjA7XHJcbiAgICAgICAgdmlkZW8uaGVpZ2h0ID0gc3RyZWFtLmhlaWdodCB8fCBzZWxmLmhlaWdodCB8fCAyNDA7XHJcblxyXG4gICAgICAgIHZpZGVvLnBsYXkoKTtcclxuXHJcbiAgICAgICAgcmV0dXJuIHZpZGVvO1xyXG4gICAgfVxyXG5cclxuICAgIHRoaXMuYXBwZW5kU3RyZWFtcyA9IGZ1bmN0aW9uKHN0cmVhbXMpIHtcclxuICAgICAgICBpZiAoIXN0cmVhbXMpIHtcclxuICAgICAgICAgICAgdGhyb3cgJ0ZpcnN0IHBhcmFtZXRlciBpcyByZXF1aXJlZC4nO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCEoc3RyZWFtcyBpbnN0YW5jZW9mIEFycmF5KSkge1xyXG4gICAgICAgICAgICBzdHJlYW1zID0gW3N0cmVhbXNdO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc3RyZWFtcy5mb3JFYWNoKGZ1bmN0aW9uKHN0cmVhbSkge1xyXG4gICAgICAgICAgICB2YXIgbmV3U3RyZWFtID0gbmV3IE1lZGlhU3RyZWFtKCk7XHJcblxyXG4gICAgICAgICAgICBpZiAoc3RyZWFtLmdldFRyYWNrcygpLmZpbHRlcihmdW5jdGlvbih0KSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIHQua2luZCA9PT0gJ3ZpZGVvJztcclxuICAgICAgICAgICAgICAgIH0pLmxlbmd0aCkge1xyXG4gICAgICAgICAgICAgICAgdmFyIHZpZGVvID0gZ2V0VmlkZW8oc3RyZWFtKTtcclxuICAgICAgICAgICAgICAgIHZpZGVvLnN0cmVhbSA9IHN0cmVhbTtcclxuICAgICAgICAgICAgICAgIHZpZGVvcy5wdXNoKHZpZGVvKTtcclxuXHJcbiAgICAgICAgICAgICAgICBuZXdTdHJlYW0uYWRkVHJhY2soc3RyZWFtLmdldFRyYWNrcygpLmZpbHRlcihmdW5jdGlvbih0KSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIHQua2luZCA9PT0gJ3ZpZGVvJztcclxuICAgICAgICAgICAgICAgIH0pWzBdKTtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgaWYgKHN0cmVhbS5nZXRUcmFja3MoKS5maWx0ZXIoZnVuY3Rpb24odCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiB0LmtpbmQgPT09ICdhdWRpbyc7XHJcbiAgICAgICAgICAgICAgICB9KS5sZW5ndGgpIHtcclxuICAgICAgICAgICAgICAgIHZhciBhdWRpb1NvdXJjZSA9IHNlbGYuYXVkaW9Db250ZXh0LmNyZWF0ZU1lZGlhU3RyZWFtU291cmNlKHN0cmVhbSk7XHJcbiAgICAgICAgICAgICAgICBzZWxmLmF1ZGlvRGVzdGluYXRpb24gPSBzZWxmLmF1ZGlvQ29udGV4dC5jcmVhdGVNZWRpYVN0cmVhbURlc3RpbmF0aW9uKCk7XHJcbiAgICAgICAgICAgICAgICBhdWRpb1NvdXJjZS5jb25uZWN0KHNlbGYuYXVkaW9EZXN0aW5hdGlvbik7XHJcblxyXG4gICAgICAgICAgICAgICAgbmV3U3RyZWFtLmFkZFRyYWNrKHNlbGYuYXVkaW9EZXN0aW5hdGlvbi5zdHJlYW0uZ2V0VHJhY2tzKCkuZmlsdGVyKGZ1bmN0aW9uKHQpIHtcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gdC5raW5kID09PSAnYXVkaW8nO1xyXG4gICAgICAgICAgICAgICAgfSlbMF0pO1xyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBhcnJheU9mTWVkaWFTdHJlYW1zLnB1c2gobmV3U3RyZWFtKTtcclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgdGhpcy5yZWxlYXNlU3RyZWFtcyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHZpZGVvcyA9IFtdO1xyXG4gICAgICAgIGlzU3RvcERyYXdpbmdGcmFtZXMgPSB0cnVlO1xyXG5cclxuICAgICAgICBpZiAoc2VsZi5nYWluTm9kZSkge1xyXG4gICAgICAgICAgICBzZWxmLmdhaW5Ob2RlLmRpc2Nvbm5lY3QoKTtcclxuICAgICAgICAgICAgc2VsZi5nYWluTm9kZSA9IG51bGw7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBpZiAoc2VsZi5hdWRpb1NvdXJjZXMubGVuZ3RoKSB7XHJcbiAgICAgICAgICAgIHNlbGYuYXVkaW9Tb3VyY2VzLmZvckVhY2goZnVuY3Rpb24oc291cmNlKSB7XHJcbiAgICAgICAgICAgICAgICBzb3VyY2UuZGlzY29ubmVjdCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgc2VsZi5hdWRpb1NvdXJjZXMgPSBbXTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChzZWxmLmF1ZGlvRGVzdGluYXRpb24pIHtcclxuICAgICAgICAgICAgc2VsZi5hdWRpb0Rlc3RpbmF0aW9uLmRpc2Nvbm5lY3QoKTtcclxuICAgICAgICAgICAgc2VsZi5hdWRpb0Rlc3RpbmF0aW9uID0gbnVsbDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChzZWxmLmF1ZGlvQ29udGV4dCkge1xyXG4gICAgICAgICAgICBzZWxmLmF1ZGlvQ29udGV4dC5jbG9zZSgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgc2VsZi5hdWRpb0NvbnRleHQgPSBudWxsO1xyXG5cclxuICAgICAgICBjb250ZXh0LmNsZWFyUmVjdCgwLCAwLCBjYW52YXMud2lkdGgsIGNhbnZhcy5oZWlnaHQpO1xyXG5cclxuICAgICAgICBpZiAoY2FudmFzLnN0cmVhbSkge1xyXG4gICAgICAgICAgICBjYW52YXMuc3RyZWFtLnN0b3AoKTtcclxuICAgICAgICAgICAgY2FudmFzLnN0cmVhbSA9IG51bGw7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICB0aGlzLnJlc2V0VmlkZW9TdHJlYW1zID0gZnVuY3Rpb24oc3RyZWFtcykge1xyXG4gICAgICAgIGlmIChzdHJlYW1zICYmICEoc3RyZWFtcyBpbnN0YW5jZW9mIEFycmF5KSkge1xyXG4gICAgICAgICAgICBzdHJlYW1zID0gW3N0cmVhbXNdO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgcmVzZXRWaWRlb1N0cmVhbXMoc3RyZWFtcyk7XHJcbiAgICB9O1xyXG5cclxuICAgIGZ1bmN0aW9uIHJlc2V0VmlkZW9TdHJlYW1zKHN0cmVhbXMpIHtcclxuICAgICAgICB2aWRlb3MgPSBbXTtcclxuICAgICAgICBzdHJlYW1zID0gc3RyZWFtcyB8fCBhcnJheU9mTWVkaWFTdHJlYW1zO1xyXG5cclxuICAgICAgICAvLyB2aWE6IEBhZHJpYW4tYmVyXHJcbiAgICAgICAgc3RyZWFtcy5mb3JFYWNoKGZ1bmN0aW9uKHN0cmVhbSkge1xyXG4gICAgICAgICAgICBpZiAoIXN0cmVhbS5nZXRUcmFja3MoKS5maWx0ZXIoZnVuY3Rpb24odCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiB0LmtpbmQgPT09ICd2aWRlbyc7XHJcbiAgICAgICAgICAgICAgICB9KS5sZW5ndGgpIHtcclxuICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgdmFyIHZpZGVvID0gZ2V0VmlkZW8oc3RyZWFtKTtcclxuICAgICAgICAgICAgdmlkZW8uc3RyZWFtID0gc3RyZWFtO1xyXG4gICAgICAgICAgICB2aWRlb3MucHVzaCh2aWRlbyk7XHJcbiAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgLy8gZm9yIGRlYnVnZ2luZ1xyXG4gICAgdGhpcy5uYW1lID0gJ011bHRpU3RyZWFtc01peGVyJztcclxuICAgIHRoaXMudG9TdHJpbmcgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICByZXR1cm4gdGhpcy5uYW1lO1xyXG4gICAgfTtcclxuXHJcbiAgICB0aGlzLmdldE1peGVkU3RyZWFtID0gZ2V0TWl4ZWRTdHJlYW07XHJcblxyXG59XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyA9PT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIGlmICh0eXBlb2YgbW9kdWxlICE9PSAndW5kZWZpbmVkJyAvKiAmJiAhIW1vZHVsZS5leHBvcnRzKi8gKSB7XHJcbiAgICAgICAgbW9kdWxlLmV4cG9ydHMgPSBNdWx0aVN0cmVhbXNNaXhlcjtcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIGRlZmluZSA9PT0gJ2Z1bmN0aW9uJyAmJiBkZWZpbmUuYW1kKSB7XHJcbiAgICAgICAgZGVmaW5lKCdNdWx0aVN0cmVhbXNNaXhlcicsIFtdLCBmdW5jdGlvbigpIHtcclxuICAgICAgICAgICAgcmV0dXJuIE11bHRpU3RyZWFtc01peGVyO1xyXG4gICAgICAgIH0pO1xyXG4gICAgfVxyXG59XG5cclxuLy8gX19fX19fX19fX19fX19fX19fX19fX1xyXG4vLyBNdWx0aVN0cmVhbVJlY29yZGVyLmpzXHJcblxyXG4vKlxyXG4gKiBWaWRlbyBjb25mZXJlbmNlIHJlY29yZGluZywgdXNpbmcgY2FwdHVyZVN0cmVhbSBBUEkgYWxvbmcgd2l0aCBXZWJBdWRpbyBhbmQgQ2FudmFzMkQgQVBJLlxyXG4gKi9cclxuXHJcbi8qKlxyXG4gKiBNdWx0aVN0cmVhbVJlY29yZGVyIGNhbiByZWNvcmQgbXVsdGlwbGUgdmlkZW9zIGluIHNpbmdsZSBjb250YWluZXIuXHJcbiAqIEBzdW1tYXJ5IE11bHRpLXZpZGVvcyByZWNvcmRlci5cclxuICogQGxpY2Vuc2Uge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDL2Jsb2IvbWFzdGVyL0xJQ0VOU0V8TUlUfVxyXG4gKiBAYXV0aG9yIHtAbGluayBodHRwczovL011YXpLaGFuLmNvbXxNdWF6IEtoYW59XHJcbiAqIEB0eXBlZGVmIE11bHRpU3RyZWFtUmVjb3JkZXJcclxuICogQGNsYXNzXHJcbiAqIEBleGFtcGxlXHJcbiAqIHZhciBvcHRpb25zID0ge1xyXG4gKiAgICAgbWltZVR5cGU6ICd2aWRlby93ZWJtJ1xyXG4gKiB9XHJcbiAqIHZhciByZWNvcmRlciA9IG5ldyBNdWx0aVN0cmVhbVJlY29yZGVyKEFycmF5T2ZNZWRpYVN0cmVhbXMsIG9wdGlvbnMpO1xyXG4gKiByZWNvcmRlci5yZWNvcmQoKTtcclxuICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbihibG9iKSB7XHJcbiAqICAgICB2aWRlby5zcmMgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKGJsb2IpO1xyXG4gKlxyXG4gKiAgICAgLy8gb3JcclxuICogICAgIHZhciBibG9iID0gcmVjb3JkZXIuYmxvYjtcclxuICogfSk7XHJcbiAqIEBzZWUge0BsaW5rIGh0dHBzOi8vZ2l0aHViLmNvbS9tdWF6LWtoYW4vUmVjb3JkUlRDfFJlY29yZFJUQyBTb3VyY2UgQ29kZX1cclxuICogQHBhcmFtIHtNZWRpYVN0cmVhbXN9IG1lZGlhU3RyZWFtcyAtIEFycmF5IG9mIE1lZGlhU3RyZWFtcy5cclxuICogQHBhcmFtIHtvYmplY3R9IGNvbmZpZyAtIHtkaXNhYmxlTG9nczp0cnVlLCBmcmFtZUludGVydmFsOiAxLCBtaW1lVHlwZTogXCJ2aWRlby93ZWJtXCJ9XHJcbiAqL1xyXG5cclxuZnVuY3Rpb24gTXVsdGlTdHJlYW1SZWNvcmRlcihhcnJheU9mTWVkaWFTdHJlYW1zLCBvcHRpb25zKSB7XHJcbiAgICBhcnJheU9mTWVkaWFTdHJlYW1zID0gYXJyYXlPZk1lZGlhU3RyZWFtcyB8fCBbXTtcclxuICAgIHZhciBzZWxmID0gdGhpcztcclxuXHJcbiAgICB2YXIgbWl4ZXI7XHJcbiAgICB2YXIgbWVkaWFSZWNvcmRlcjtcclxuXHJcbiAgICBvcHRpb25zID0gb3B0aW9ucyB8fCB7XHJcbiAgICAgICAgZWxlbWVudENsYXNzOiAnbXVsdGktc3RyZWFtcy1taXhlcicsXHJcbiAgICAgICAgbWltZVR5cGU6ICd2aWRlby93ZWJtJyxcclxuICAgICAgICB2aWRlbzoge1xyXG4gICAgICAgICAgICB3aWR0aDogMzYwLFxyXG4gICAgICAgICAgICBoZWlnaHQ6IDI0MFxyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgaWYgKCFvcHRpb25zLmZyYW1lSW50ZXJ2YWwpIHtcclxuICAgICAgICBvcHRpb25zLmZyYW1lSW50ZXJ2YWwgPSAxMDtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoIW9wdGlvbnMudmlkZW8pIHtcclxuICAgICAgICBvcHRpb25zLnZpZGVvID0ge307XHJcbiAgICB9XHJcblxyXG4gICAgaWYgKCFvcHRpb25zLnZpZGVvLndpZHRoKSB7XHJcbiAgICAgICAgb3B0aW9ucy52aWRlby53aWR0aCA9IDM2MDtcclxuICAgIH1cclxuXHJcbiAgICBpZiAoIW9wdGlvbnMudmlkZW8uaGVpZ2h0KSB7XHJcbiAgICAgICAgb3B0aW9ucy52aWRlby5oZWlnaHQgPSAyNDA7XHJcbiAgICB9XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZWNvcmRzIGFsbCBNZWRpYVN0cmVhbXMuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTXVsdGlTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnJlY29yZCgpO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnJlY29yZCA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIC8vIGdpdGh1Yi9tdWF6LWtoYW4vTXVsdGlTdHJlYW1zTWl4ZXJcclxuICAgICAgICBtaXhlciA9IG5ldyBNdWx0aVN0cmVhbXNNaXhlcihhcnJheU9mTWVkaWFTdHJlYW1zLCBvcHRpb25zLmVsZW1lbnRDbGFzcyB8fCAnbXVsdGktc3RyZWFtcy1taXhlcicpO1xyXG5cclxuICAgICAgICBpZiAoZ2V0QWxsVmlkZW9UcmFja3MoKS5sZW5ndGgpIHtcclxuICAgICAgICAgICAgbWl4ZXIuZnJhbWVJbnRlcnZhbCA9IG9wdGlvbnMuZnJhbWVJbnRlcnZhbCB8fCAxMDtcclxuICAgICAgICAgICAgbWl4ZXIud2lkdGggPSBvcHRpb25zLnZpZGVvLndpZHRoIHx8IDM2MDtcclxuICAgICAgICAgICAgbWl4ZXIuaGVpZ2h0ID0gb3B0aW9ucy52aWRlby5oZWlnaHQgfHwgMjQwO1xyXG4gICAgICAgICAgICBtaXhlci5zdGFydERyYXdpbmdGcmFtZXMoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGlmIChvcHRpb25zLnByZXZpZXdTdHJlYW0gJiYgdHlwZW9mIG9wdGlvbnMucHJldmlld1N0cmVhbSA9PT0gJ2Z1bmN0aW9uJykge1xyXG4gICAgICAgICAgICBvcHRpb25zLnByZXZpZXdTdHJlYW0obWl4ZXIuZ2V0TWl4ZWRTdHJlYW0oKSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICAvLyByZWNvcmQgdXNpbmcgTWVkaWFSZWNvcmRlciBBUElcclxuICAgICAgICBtZWRpYVJlY29yZGVyID0gbmV3IE1lZGlhU3RyZWFtUmVjb3JkZXIobWl4ZXIuZ2V0TWl4ZWRTdHJlYW0oKSwgb3B0aW9ucyk7XHJcbiAgICAgICAgbWVkaWFSZWNvcmRlci5yZWNvcmQoKTtcclxuICAgIH07XHJcblxyXG4gICAgZnVuY3Rpb24gZ2V0QWxsVmlkZW9UcmFja3MoKSB7XHJcbiAgICAgICAgdmFyIHRyYWNrcyA9IFtdO1xyXG4gICAgICAgIGFycmF5T2ZNZWRpYVN0cmVhbXMuZm9yRWFjaChmdW5jdGlvbihzdHJlYW0pIHtcclxuICAgICAgICAgICAgZ2V0VHJhY2tzKHN0cmVhbSwgJ3ZpZGVvJykuZm9yRWFjaChmdW5jdGlvbih0cmFjaykge1xyXG4gICAgICAgICAgICAgICAgdHJhY2tzLnB1c2godHJhY2spO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9KTtcclxuICAgICAgICByZXR1cm4gdHJhY2tzO1xyXG4gICAgfVxyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2Qgc3RvcHMgcmVjb3JkaW5nIE1lZGlhU3RyZWFtLlxyXG4gICAgICogQHBhcmFtIHtmdW5jdGlvbn0gY2FsbGJhY2sgLSBDYWxsYmFjayBmdW5jdGlvbiwgdGhhdCBpcyB1c2VkIHRvIHBhc3MgcmVjb3JkZWQgYmxvYiBiYWNrIHRvIHRoZSBjYWxsZWUuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTXVsdGlTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0b3AoZnVuY3Rpb24oYmxvYikge1xyXG4gICAgICogICAgIHZpZGVvLnNyYyA9IFVSTC5jcmVhdGVPYmplY3RVUkwoYmxvYik7XHJcbiAgICAgKiB9KTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5zdG9wID0gZnVuY3Rpb24oY2FsbGJhY2spIHtcclxuICAgICAgICBpZiAoIW1lZGlhUmVjb3JkZXIpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgbWVkaWFSZWNvcmRlci5zdG9wKGZ1bmN0aW9uKGJsb2IpIHtcclxuICAgICAgICAgICAgc2VsZi5ibG9iID0gYmxvYjtcclxuXHJcbiAgICAgICAgICAgIGNhbGxiYWNrKGJsb2IpO1xyXG5cclxuICAgICAgICAgICAgc2VsZi5jbGVhclJlY29yZGVkRGF0YSgpO1xyXG4gICAgICAgIH0pO1xyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHBhdXNlcyB0aGUgcmVjb3JkaW5nIHByb2Nlc3MuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTXVsdGlTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnBhdXNlKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucGF1c2UgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpZiAobWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnBhdXNlKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJlc3VtZXMgdGhlIHJlY29yZGluZyBwcm9jZXNzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIE11bHRpU3RyZWFtUmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5yZXN1bWUoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5yZXN1bWUgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpZiAobWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLnJlc3VtZSgpO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXNldHMgY3VycmVudGx5IHJlY29yZGVkIGRhdGEuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTXVsdGlTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLmNsZWFyUmVjb3JkZWREYXRhKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuY2xlYXJSZWNvcmRlZERhdGEgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBpZiAobWVkaWFSZWNvcmRlcikge1xyXG4gICAgICAgICAgICBtZWRpYVJlY29yZGVyLmNsZWFyUmVjb3JkZWREYXRhKCk7XHJcbiAgICAgICAgICAgIG1lZGlhUmVjb3JkZXIgPSBudWxsO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKG1peGVyKSB7XHJcbiAgICAgICAgICAgIG1peGVyLnJlbGVhc2VTdHJlYW1zKCk7XHJcbiAgICAgICAgICAgIG1peGVyID0gbnVsbDtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogQWRkIGV4dHJhIG1lZGlhLXN0cmVhbXMgdG8gZXhpc3RpbmcgcmVjb3JkaW5ncy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBNdWx0aVN0cmVhbVJlY29yZGVyXHJcbiAgICAgKiBAcGFyYW0ge01lZGlhU3RyZWFtc30gbWVkaWFTdHJlYW1zIC0gQXJyYXkgb2YgTWVkaWFTdHJlYW1zXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuYWRkU3RyZWFtcyhbbmV3QXVkaW9TdHJlYW0sIG5ld1ZpZGVvU3RyZWFtXSk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuYWRkU3RyZWFtcyA9IGZ1bmN0aW9uKHN0cmVhbXMpIHtcclxuICAgICAgICBpZiAoIXN0cmVhbXMpIHtcclxuICAgICAgICAgICAgdGhyb3cgJ0ZpcnN0IHBhcmFtZXRlciBpcyByZXF1aXJlZC4nO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCEoc3RyZWFtcyBpbnN0YW5jZW9mIEFycmF5KSkge1xyXG4gICAgICAgICAgICBzdHJlYW1zID0gW3N0cmVhbXNdO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgYXJyYXlPZk1lZGlhU3RyZWFtcy5jb25jYXQoc3RyZWFtcyk7XHJcblxyXG4gICAgICAgIGlmICghbWVkaWFSZWNvcmRlciB8fCAhbWl4ZXIpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgbWl4ZXIuYXBwZW5kU3RyZWFtcyhzdHJlYW1zKTtcclxuXHJcbiAgICAgICAgaWYgKG9wdGlvbnMucHJldmlld1N0cmVhbSAmJiB0eXBlb2Ygb3B0aW9ucy5wcmV2aWV3U3RyZWFtID09PSAnZnVuY3Rpb24nKSB7XHJcbiAgICAgICAgICAgIG9wdGlvbnMucHJldmlld1N0cmVhbShtaXhlci5nZXRNaXhlZFN0cmVhbSgpKTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogUmVzZXQgdmlkZW9zIGR1cmluZyBsaXZlIHJlY29yZGluZy4gUmVwbGFjZSBvbGQgdmlkZW9zIGUuZy4gcmVwbGFjZSBjYW1lcmFzIHdpdGggZnVsbC1zY3JlZW4uXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTXVsdGlTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQHBhcmFtIHtNZWRpYVN0cmVhbXN9IG1lZGlhU3RyZWFtcyAtIEFycmF5IG9mIE1lZGlhU3RyZWFtc1xyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnJlc2V0VmlkZW9TdHJlYW1zKFtuZXdWaWRlbzEsIG5ld1ZpZGVvMl0pO1xyXG4gICAgICovXHJcbiAgICB0aGlzLnJlc2V0VmlkZW9TdHJlYW1zID0gZnVuY3Rpb24oc3RyZWFtcykge1xyXG4gICAgICAgIGlmICghbWl4ZXIpIHtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKHN0cmVhbXMgJiYgIShzdHJlYW1zIGluc3RhbmNlb2YgQXJyYXkpKSB7XHJcbiAgICAgICAgICAgIHN0cmVhbXMgPSBbc3RyZWFtc107XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBtaXhlci5yZXNldFZpZGVvU3RyZWFtcyhzdHJlYW1zKTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBSZXR1cm5zIE11bHRpU3RyZWFtc01peGVyXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgTXVsdGlTdHJlYW1SZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIGxldCBtaXhlciA9IHJlY29yZGVyLmdldE1peGVyKCk7XHJcbiAgICAgKiBtaXhlci5hcHBlbmRTdHJlYW1zKFtuZXdTdHJlYW1dKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5nZXRNaXhlciA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBtaXhlcjtcclxuICAgIH07XHJcblxyXG4gICAgLy8gZm9yIGRlYnVnZ2luZ1xyXG4gICAgdGhpcy5uYW1lID0gJ011bHRpU3RyZWFtUmVjb3JkZXInO1xyXG4gICAgdGhpcy50b1N0cmluZyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiB0aGlzLm5hbWU7XHJcbiAgICB9O1xyXG59XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIFJlY29yZFJUQy5NdWx0aVN0cmVhbVJlY29yZGVyID0gTXVsdGlTdHJlYW1SZWNvcmRlcjtcclxufVxuXHJcbi8vIF9fX19fX19fX19fX19fX19fX19fX1xyXG4vLyBSZWNvcmRSVEMucHJvbWlzZXMuanNcclxuXHJcbi8qKlxyXG4gKiBSZWNvcmRSVENQcm9taXNlc0hhbmRsZXIgYWRkcyBwcm9taXNlcyBzdXBwb3J0IGluIHtAbGluayBSZWNvcmRSVEN9LiBUcnkgYSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvc2ltcGxlLWRlbW9zL1JlY29yZFJUQ1Byb21pc2VzSGFuZGxlci5odG1sfGRlbW8gaGVyZX1cclxuICogQHN1bW1hcnkgUHJvbWlzZXMgZm9yIHtAbGluayBSZWNvcmRSVEN9XHJcbiAqIEBsaWNlbnNlIHtAbGluayBodHRwczovL2dpdGh1Yi5jb20vbXVhei1raGFuL1JlY29yZFJUQy9ibG9iL21hc3Rlci9MSUNFTlNFfE1JVH1cclxuICogQGF1dGhvciB7QGxpbmsgaHR0cHM6Ly9NdWF6S2hhbi5jb218TXVheiBLaGFufVxyXG4gKiBAdHlwZWRlZiBSZWNvcmRSVENQcm9taXNlc0hhbmRsZXJcclxuICogQGNsYXNzXHJcbiAqIEBleGFtcGxlXHJcbiAqIHZhciByZWNvcmRlciA9IG5ldyBSZWNvcmRSVENQcm9taXNlc0hhbmRsZXIobWVkaWFTdHJlYW0sIG9wdGlvbnMpO1xyXG4gKiByZWNvcmRlci5zdGFydFJlY29yZGluZygpXHJcbiAqICAgICAgICAgLnRoZW4oc3VjY2Vzc0NCKVxyXG4gKiAgICAgICAgIC5jYXRjaChlcnJvckNCKTtcclxuICogLy8gTm90ZTogWW91IGNhbiBhY2Nlc3MgYWxsIFJlY29yZFJUQyBBUEkgdXNpbmcgXCJyZWNvcmRlci5yZWNvcmRSVENcIiBlLmcuIFxyXG4gKiByZWNvcmRlci5yZWNvcmRSVEMub25TdGF0ZUNoYW5nZWQgPSBmdW5jdGlvbihzdGF0ZSkge307XHJcbiAqIHJlY29yZGVyLnJlY29yZFJUQy5zZXRSZWNvcmRpbmdEdXJhdGlvbig1MDAwKTtcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKiBAcGFyYW0ge01lZGlhU3RyZWFtfSBtZWRpYVN0cmVhbSAtIFNpbmdsZSBtZWRpYS1zdHJlYW0gb2JqZWN0LCBhcnJheSBvZiBtZWRpYS1zdHJlYW1zLCBodG1sLWNhbnZhcy1lbGVtZW50LCBldGMuXHJcbiAqIEBwYXJhbSB7b2JqZWN0fSBjb25maWcgLSB7dHlwZTpcInZpZGVvXCIsIHJlY29yZGVyVHlwZTogTWVkaWFTdHJlYW1SZWNvcmRlciwgZGlzYWJsZUxvZ3M6IHRydWUsIG51bWJlck9mQXVkaW9DaGFubmVsczogMSwgYnVmZmVyU2l6ZTogMCwgc2FtcGxlUmF0ZTogMCwgdmlkZW86IEhUTUxWaWRlb0VsZW1lbnQsIGV0Yy59XHJcbiAqIEB0aHJvd3MgV2lsbCB0aHJvdyBhbiBlcnJvciBpZiBcIm5ld1wiIGtleXdvcmQgaXMgbm90IHVzZWQgdG8gaW5pdGlhdGUgXCJSZWNvcmRSVENQcm9taXNlc0hhbmRsZXJcIi4gQWxzbyB0aHJvd3MgZXJyb3IgaWYgZmlyc3QgYXJndW1lbnQgXCJNZWRpYVN0cmVhbVwiIGlzIG1pc3NpbmcuXHJcbiAqIEByZXF1aXJlcyB7QGxpbmsgUmVjb3JkUlRDfVxyXG4gKi9cclxuXHJcbmZ1bmN0aW9uIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlcihtZWRpYVN0cmVhbSwgb3B0aW9ucykge1xyXG4gICAgaWYgKCF0aGlzKSB7XHJcbiAgICAgICAgdGhyb3cgJ1VzZSBcIm5ldyBSZWNvcmRSVENQcm9taXNlc0hhbmRsZXIoKVwiJztcclxuICAgIH1cclxuXHJcbiAgICBpZiAodHlwZW9mIG1lZGlhU3RyZWFtID09PSAndW5kZWZpbmVkJykge1xyXG4gICAgICAgIHRocm93ICdGaXJzdCBhcmd1bWVudCBcIk1lZGlhU3RyZWFtXCIgaXMgcmVxdWlyZWQuJztcclxuICAgIH1cclxuXHJcbiAgICB2YXIgc2VsZiA9IHRoaXM7XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBAcHJvcGVydHkge0Jsb2J9IGJsb2IgLSBBY2Nlc3MvcmVhY2ggdGhlIG5hdGl2ZSB7QGxpbmsgUmVjb3JkUlRDfSBvYmplY3QuXHJcbiAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDUHJvbWlzZXNIYW5kbGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogbGV0IGludGVybmFsID0gcmVjb3JkZXIucmVjb3JkUlRDLmdldEludGVybmFsUmVjb3JkZXIoKTtcclxuICAgICAqIGFsZXJ0KGludGVybmFsIGluc3RhbmNlb2YgTWVkaWFTdHJlYW1SZWNvcmRlcik7XHJcbiAgICAgKiByZWNvcmRlci5yZWNvcmRSVEMub25TdGF0ZUNoYW5nZWQgPSBmdW5jdGlvbihzdGF0ZSkge307XHJcbiAgICAgKi9cclxuICAgIHNlbGYucmVjb3JkUlRDID0gbmV3IFJlY29yZFJUQyhtZWRpYVN0cmVhbSwgb3B0aW9ucyk7XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZWNvcmRzIE1lZGlhU3RyZWFtLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0YXJ0UmVjb3JkaW5nKClcclxuICAgICAqICAgICAgICAgLnRoZW4oc3VjY2Vzc0NCKVxyXG4gICAgICogICAgICAgICAuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuc3RhcnRSZWNvcmRpbmcgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICByZXR1cm4gbmV3IFByb21pc2UoZnVuY3Rpb24ocmVzb2x2ZSwgcmVqZWN0KSB7XHJcbiAgICAgICAgICAgIHRyeSB7XHJcbiAgICAgICAgICAgICAgICBzZWxmLnJlY29yZFJUQy5zdGFydFJlY29yZGluZygpO1xyXG4gICAgICAgICAgICAgICAgcmVzb2x2ZSgpO1xyXG4gICAgICAgICAgICB9IGNhdGNoIChlKSB7XHJcbiAgICAgICAgICAgICAgICByZWplY3QoZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBzdG9wcyB0aGUgcmVjb3JkaW5nLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoKS50aGVuKGZ1bmN0aW9uKCkge1xyXG4gICAgICogICAgIHZhciBibG9iID0gcmVjb3JkZXIuZ2V0QmxvYigpO1xyXG4gICAgICogfSkuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuc3RvcFJlY29yZGluZyA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBuZXcgUHJvbWlzZShmdW5jdGlvbihyZXNvbHZlLCByZWplY3QpIHtcclxuICAgICAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgICAgIHNlbGYucmVjb3JkUlRDLnN0b3BSZWNvcmRpbmcoZnVuY3Rpb24odXJsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgc2VsZi5ibG9iID0gc2VsZi5yZWNvcmRSVEMuZ2V0QmxvYigpO1xyXG5cclxuICAgICAgICAgICAgICAgICAgICBpZiAoIXNlbGYuYmxvYiB8fCAhc2VsZi5ibG9iLnNpemUpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmVqZWN0KCdFbXB0eSBibG9iLicsIHNlbGYuYmxvYik7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgICAgIHJlc29sdmUodXJsKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB9IGNhdGNoIChlKSB7XHJcbiAgICAgICAgICAgICAgICByZWplY3QoZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCBwYXVzZXMgdGhlIHJlY29yZGluZy4gWW91IGNhbiByZXN1bWUgcmVjb3JkaW5nIHVzaW5nIFwicmVzdW1lUmVjb3JkaW5nXCIgbWV0aG9kLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnBhdXNlUmVjb3JkaW5nKClcclxuICAgICAqICAgICAgICAgLnRoZW4oc3VjY2Vzc0NCKVxyXG4gICAgICogICAgICAgICAuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucGF1c2VSZWNvcmRpbmcgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICByZXR1cm4gbmV3IFByb21pc2UoZnVuY3Rpb24ocmVzb2x2ZSwgcmVqZWN0KSB7XHJcbiAgICAgICAgICAgIHRyeSB7XHJcbiAgICAgICAgICAgICAgICBzZWxmLnJlY29yZFJUQy5wYXVzZVJlY29yZGluZygpO1xyXG4gICAgICAgICAgICAgICAgcmVzb2x2ZSgpO1xyXG4gICAgICAgICAgICB9IGNhdGNoIChlKSB7XHJcbiAgICAgICAgICAgICAgICByZWplY3QoZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXN1bWVzIHRoZSByZWNvcmRpbmcuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDUHJvbWlzZXNIYW5kbGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucmVzdW1lUmVjb3JkaW5nKClcclxuICAgICAqICAgICAgICAgLnRoZW4oc3VjY2Vzc0NCKVxyXG4gICAgICogICAgICAgICAuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucmVzdW1lUmVjb3JkaW5nID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIG5ldyBQcm9taXNlKGZ1bmN0aW9uKHJlc29sdmUsIHJlamVjdCkge1xyXG4gICAgICAgICAgICB0cnkge1xyXG4gICAgICAgICAgICAgICAgc2VsZi5yZWNvcmRSVEMucmVzdW1lUmVjb3JkaW5nKCk7XHJcbiAgICAgICAgICAgICAgICByZXNvbHZlKCk7XHJcbiAgICAgICAgICAgIH0gY2F0Y2ggKGUpIHtcclxuICAgICAgICAgICAgICAgIHJlamVjdChlKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHJldHVybnMgZGF0YS11cmwgZm9yIHRoZSByZWNvcmRlZCBibG9iLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoKS50aGVuKGZ1bmN0aW9uKCkge1xyXG4gICAgICogICAgIHJlY29yZGVyLmdldERhdGFVUkwoKS50aGVuKGZ1bmN0aW9uKGRhdGFVUkwpIHtcclxuICAgICAqICAgICAgICAgd2luZG93Lm9wZW4oZGF0YVVSTCk7XHJcbiAgICAgKiAgICAgfSkuY2F0Y2goZXJyb3JDQik7O1xyXG4gICAgICogfSkuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuZ2V0RGF0YVVSTCA9IGZ1bmN0aW9uKGNhbGxiYWNrKSB7XHJcbiAgICAgICAgcmV0dXJuIG5ldyBQcm9taXNlKGZ1bmN0aW9uKHJlc29sdmUsIHJlamVjdCkge1xyXG4gICAgICAgICAgICB0cnkge1xyXG4gICAgICAgICAgICAgICAgc2VsZi5yZWNvcmRSVEMuZ2V0RGF0YVVSTChmdW5jdGlvbihkYXRhVVJMKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmVzb2x2ZShkYXRhVVJMKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB9IGNhdGNoIChlKSB7XHJcbiAgICAgICAgICAgICAgICByZWplY3QoZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXR1cm5zIHRoZSByZWNvcmRlZCBibG9iLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoKS50aGVuKGZ1bmN0aW9uKCkge1xyXG4gICAgICogICAgIHJlY29yZGVyLmdldEJsb2IoKS50aGVuKGZ1bmN0aW9uKGJsb2IpIHt9KVxyXG4gICAgICogfSkuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuZ2V0QmxvYiA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBuZXcgUHJvbWlzZShmdW5jdGlvbihyZXNvbHZlLCByZWplY3QpIHtcclxuICAgICAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgICAgIHJlc29sdmUoc2VsZi5yZWNvcmRSVEMuZ2V0QmxvYigpKTtcclxuICAgICAgICAgICAgfSBjYXRjaCAoZSkge1xyXG4gICAgICAgICAgICAgICAgcmVqZWN0KGUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmV0dXJucyB0aGUgaW50ZXJuYWwgcmVjb3JkaW5nIG9iamVjdC5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBSZWNvcmRSVENQcm9taXNlc0hhbmRsZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiBsZXQgaW50ZXJuYWxSZWNvcmRlciA9IGF3YWl0IHJlY29yZGVyLmdldEludGVybmFsUmVjb3JkZXIoKTtcclxuICAgICAqIGlmKGludGVybmFsUmVjb3JkZXIgaW5zdGFuY2VvZiBNdWx0aVN0cmVhbVJlY29yZGVyKSB7XHJcbiAgICAgKiAgICAgaW50ZXJuYWxSZWNvcmRlci5hZGRTdHJlYW1zKFtuZXdBdWRpb1N0cmVhbV0pO1xyXG4gICAgICogICAgIGludGVybmFsUmVjb3JkZXIucmVzZXRWaWRlb1N0cmVhbXMoW3NjcmVlblN0cmVhbV0pO1xyXG4gICAgICogfVxyXG4gICAgICogQHJldHVybnMge09iamVjdH0gXHJcbiAgICAgKi9cclxuICAgIHRoaXMuZ2V0SW50ZXJuYWxSZWNvcmRlciA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBuZXcgUHJvbWlzZShmdW5jdGlvbihyZXNvbHZlLCByZWplY3QpIHtcclxuICAgICAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgICAgIHJlc29sdmUoc2VsZi5yZWNvcmRSVEMuZ2V0SW50ZXJuYWxSZWNvcmRlcigpKTtcclxuICAgICAgICAgICAgfSBjYXRjaCAoZSkge1xyXG4gICAgICAgICAgICAgICAgcmVqZWN0KGUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcmVzZXRzIHRoZSByZWNvcmRlci4gU28gdGhhdCB5b3UgY2FuIHJldXNlIHNpbmdsZSByZWNvcmRlciBpbnN0YW5jZSBtYW55IHRpbWVzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIGF3YWl0IHJlY29yZGVyLnJlc2V0KCk7XHJcbiAgICAgKiByZWNvcmRlci5zdGFydFJlY29yZGluZygpOyAvLyByZWNvcmQgYWdhaW5cclxuICAgICAqL1xyXG4gICAgdGhpcy5yZXNldCA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBuZXcgUHJvbWlzZShmdW5jdGlvbihyZXNvbHZlLCByZWplY3QpIHtcclxuICAgICAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgICAgIHJlc29sdmUoc2VsZi5yZWNvcmRSVEMucmVzZXQoKSk7XHJcbiAgICAgICAgICAgIH0gY2F0Y2ggKGUpIHtcclxuICAgICAgICAgICAgICAgIHJlamVjdChlKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIERlc3Ryb3kgUmVjb3JkUlRDIGluc3RhbmNlLiBDbGVhciBhbGwgcmVjb3JkZXJzIGFuZCBvYmplY3RzLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLmRlc3Ryb3koKS50aGVuKHN1Y2Nlc3NDQikuY2F0Y2goZXJyb3JDQik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuZGVzdHJveSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBuZXcgUHJvbWlzZShmdW5jdGlvbihyZXNvbHZlLCByZWplY3QpIHtcclxuICAgICAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgICAgIHJlc29sdmUoc2VsZi5yZWNvcmRSVEMuZGVzdHJveSgpKTtcclxuICAgICAgICAgICAgfSBjYXRjaCAoZSkge1xyXG4gICAgICAgICAgICAgICAgcmVqZWN0KGUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICB9O1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogR2V0IHJlY29yZGVyJ3MgcmVhZG9ubHkgc3RhdGUuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgUmVjb3JkUlRDUHJvbWlzZXNIYW5kbGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogbGV0IHN0YXRlID0gYXdhaXQgcmVjb3JkZXIuZ2V0U3RhdGUoKTtcclxuICAgICAqIC8vIG9yXHJcbiAgICAgKiByZWNvcmRlci5nZXRTdGF0ZSgpLnRoZW4oc3RhdGUgPT4geyBjb25zb2xlLmxvZyhzdGF0ZSk7IH0pXHJcbiAgICAgKiBAcmV0dXJucyB7U3RyaW5nfSBSZXR1cm5zIHJlY29yZGluZyBzdGF0ZS5cclxuICAgICAqL1xyXG4gICAgdGhpcy5nZXRTdGF0ZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIHJldHVybiBuZXcgUHJvbWlzZShmdW5jdGlvbihyZXNvbHZlLCByZWplY3QpIHtcclxuICAgICAgICAgICAgdHJ5IHtcclxuICAgICAgICAgICAgICAgIHJlc29sdmUoc2VsZi5yZWNvcmRSVEMuZ2V0U3RhdGUoKSk7XHJcbiAgICAgICAgICAgIH0gY2F0Y2ggKGUpIHtcclxuICAgICAgICAgICAgICAgIHJlamVjdChlKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIEBwcm9wZXJ0eSB7QmxvYn0gYmxvYiAtIFJlY29yZGVkIGRhdGEgYXMgXCJCbG9iXCIgb2JqZWN0LlxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIGF3YWl0IHJlY29yZGVyLnN0b3BSZWNvcmRpbmcoKTtcclxuICAgICAqIGxldCBibG9iID0gcmVjb3JkZXIuZ2V0QmxvYigpOyAvLyBvciBcInJlY29yZGVyLnJlY29yZFJUQy5ibG9iXCJcclxuICAgICAqIGludm9rZVNhdmVBc0RpYWxvZyhibG9iKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5ibG9iID0gbnVsbDtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFJlY29yZFJUQyB2ZXJzaW9uIG51bWJlclxyXG4gICAgICogQHByb3BlcnR5IHtTdHJpbmd9IHZlcnNpb24gLSBSZWxlYXNlIHZlcnNpb24gbnVtYmVyLlxyXG4gICAgICogQG1lbWJlcm9mIFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlclxyXG4gICAgICogQHN0YXRpY1xyXG4gICAgICogQHJlYWRvbmx5XHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogYWxlcnQocmVjb3JkZXIudmVyc2lvbik7XHJcbiAgICAgKi9cclxuICAgIHRoaXMudmVyc2lvbiA9ICc1LjUuOSc7XHJcbn1cclxuXHJcbmlmICh0eXBlb2YgUmVjb3JkUlRDICE9PSAndW5kZWZpbmVkJykge1xyXG4gICAgUmVjb3JkUlRDLlJlY29yZFJUQ1Byb21pc2VzSGFuZGxlciA9IFJlY29yZFJUQ1Byb21pc2VzSGFuZGxlcjtcclxufVxuXHJcbi8vIF9fX19fX19fX19fX19fX19fX19fX19cclxuLy8gV2ViQXNzZW1ibHlSZWNvcmRlci5qc1xyXG5cclxuLyoqXHJcbiAqIFdlYkFzc2VtYmx5UmVjb3JkZXIgbGV0cyB5b3UgY3JlYXRlIHdlYm0gdmlkZW9zIGluIEphdmFTY3JpcHQgdmlhIFdlYkFzc2VtYmx5LiBUaGUgbGlicmFyeSBjb25zdW1lcyByYXcgUkdCQTMyIGJ1ZmZlcnMgKDQgYnl0ZXMgcGVyIHBpeGVsKSBhbmQgdHVybnMgdGhlbSBpbnRvIGEgd2VibSB2aWRlbyB3aXRoIHRoZSBnaXZlbiBmcmFtZXJhdGUgYW5kIHF1YWxpdHkuIFRoaXMgbWFrZXMgaXQgY29tcGF0aWJsZSBvdXQtb2YtdGhlLWJveCB3aXRoIEltYWdlRGF0YSBmcm9tIGEgQ0FOVkFTLiBXaXRoIHJlYWx0aW1lIG1vZGUgeW91IGNhbiBhbHNvIHVzZSB3ZWJtLXdhc20gZm9yIHN0cmVhbWluZyB3ZWJtIHZpZGVvcy5cclxuICogQHN1bW1hcnkgVmlkZW8gcmVjb3JkaW5nIGZlYXR1cmUgaW4gQ2hyb21lLCBGaXJlZm94IGFuZCBtYXliZSBFZGdlLlxyXG4gKiBAbGljZW5zZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEMvYmxvYi9tYXN0ZXIvTElDRU5TRXxNSVR9XHJcbiAqIEBhdXRob3Ige0BsaW5rIGh0dHBzOi8vTXVhektoYW4uY29tfE11YXogS2hhbn1cclxuICogQHR5cGVkZWYgV2ViQXNzZW1ibHlSZWNvcmRlclxyXG4gKiBAY2xhc3NcclxuICogQGV4YW1wbGVcclxuICogdmFyIHJlY29yZGVyID0gbmV3IFdlYkFzc2VtYmx5UmVjb3JkZXIobWVkaWFTdHJlYW0pO1xyXG4gKiByZWNvcmRlci5yZWNvcmQoKTtcclxuICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbihibG9iKSB7XHJcbiAqICAgICB2aWRlby5zcmMgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKGJsb2IpO1xyXG4gKiB9KTtcclxuICogQHNlZSB7QGxpbmsgaHR0cHM6Ly9naXRodWIuY29tL211YXota2hhbi9SZWNvcmRSVEN8UmVjb3JkUlRDIFNvdXJjZSBDb2RlfVxyXG4gKiBAcGFyYW0ge01lZGlhU3RyZWFtfSBtZWRpYVN0cmVhbSAtIE1lZGlhU3RyZWFtIG9iamVjdCBmZXRjaGVkIHVzaW5nIGdldFVzZXJNZWRpYSBBUEkgb3IgZ2VuZXJhdGVkIHVzaW5nIGNhcHR1cmVTdHJlYW1VbnRpbEVuZGVkIG9yIFdlYkF1ZGlvIEFQSS5cclxuICogQHBhcmFtIHtvYmplY3R9IGNvbmZpZyAtIHt3ZWJBc3NlbWJseVBhdGg6J3dlYm0td2FzbS53YXNtJyx3b3JrZXJQYXRoOiAnd2VibS13b3JrZXIuanMnLCBmcmFtZVJhdGU6IDMwLCB3aWR0aDogMTkyMCwgaGVpZ2h0OiAxMDgwLCBiaXRyYXRlOiAxMDI0fVxyXG4gKi9cclxuZnVuY3Rpb24gV2ViQXNzZW1ibHlSZWNvcmRlcihzdHJlYW0sIGNvbmZpZykge1xyXG4gICAgLy8gYmFzZWQgb246IGdpdGh1Yi5jb20vR29vZ2xlQ2hyb21lTGFicy93ZWJtLXdhc21cclxuXHJcbiAgICBpZiAodHlwZW9mIFJlYWRhYmxlU3RyZWFtID09PSAndW5kZWZpbmVkJyB8fCB0eXBlb2YgV3JpdGFibGVTdHJlYW0gPT09ICd1bmRlZmluZWQnKSB7XHJcbiAgICAgICAgLy8gYmVjYXVzZSBpdCBmaXhlcyByZWFkYWJsZS93cml0YWJsZSBzdHJlYW1zIGlzc3Vlc1xyXG4gICAgICAgIGNvbnNvbGUuZXJyb3IoJ0ZvbGxvd2luZyBwb2x5ZmlsbCBpcyBzdHJvbmdseSByZWNvbW1lbmRlZDogaHR0cHM6Ly91bnBrZy5jb20vQG1hdHRpYXNidWVsZW5zL3dlYi1zdHJlYW1zLXBvbHlmaWxsL2Rpc3QvcG9seWZpbGwubWluLmpzJyk7XHJcbiAgICB9XHJcblxyXG4gICAgY29uZmlnID0gY29uZmlnIHx8IHt9O1xyXG5cclxuICAgIGNvbmZpZy53aWR0aCA9IGNvbmZpZy53aWR0aCB8fCA2NDA7XHJcbiAgICBjb25maWcuaGVpZ2h0ID0gY29uZmlnLmhlaWdodCB8fCA0ODA7XHJcbiAgICBjb25maWcuZnJhbWVSYXRlID0gY29uZmlnLmZyYW1lUmF0ZSB8fCAzMDtcclxuICAgIGNvbmZpZy5iaXRyYXRlID0gY29uZmlnLmJpdHJhdGUgfHwgMTIwMDtcclxuXHJcbiAgICBmdW5jdGlvbiBjcmVhdGVCdWZmZXJVUkwoYnVmZmVyLCB0eXBlKSB7XHJcbiAgICAgICAgcmV0dXJuIFVSTC5jcmVhdGVPYmplY3RVUkwobmV3IEJsb2IoW2J1ZmZlcl0sIHtcclxuICAgICAgICAgICAgdHlwZTogdHlwZSB8fCAnJ1xyXG4gICAgICAgIH0pKTtcclxuICAgIH1cclxuXHJcbiAgICBmdW5jdGlvbiBjYW1lcmFTdHJlYW0oKSB7XHJcbiAgICAgICAgcmV0dXJuIG5ldyBSZWFkYWJsZVN0cmVhbSh7XHJcbiAgICAgICAgICAgIHN0YXJ0OiBmdW5jdGlvbihjb250cm9sbGVyKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgY3ZzID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnY2FudmFzJyk7XHJcbiAgICAgICAgICAgICAgICB2YXIgdmlkZW8gPSBkb2N1bWVudC5jcmVhdGVFbGVtZW50KCd2aWRlbycpO1xyXG4gICAgICAgICAgICAgICAgdmlkZW8uc3JjT2JqZWN0ID0gc3RyZWFtO1xyXG4gICAgICAgICAgICAgICAgdmlkZW8ub25wbGF5aW5nID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY3ZzLndpZHRoID0gY29uZmlnLndpZHRoO1xyXG4gICAgICAgICAgICAgICAgICAgIGN2cy5oZWlnaHQgPSBjb25maWcuaGVpZ2h0O1xyXG4gICAgICAgICAgICAgICAgICAgIHZhciBjdHggPSBjdnMuZ2V0Q29udGV4dCgnMmQnKTtcclxuICAgICAgICAgICAgICAgICAgICB2YXIgZnJhbWVUaW1lb3V0ID0gMTAwMCAvIGNvbmZpZy5mcmFtZVJhdGU7XHJcbiAgICAgICAgICAgICAgICAgICAgc2V0VGltZW91dChmdW5jdGlvbiBmKCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBjdHguZHJhd0ltYWdlKHZpZGVvLCAwLCAwKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29udHJvbGxlci5lbnF1ZXVlKFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY3R4LmdldEltYWdlRGF0YSgwLCAwLCBjb25maWcud2lkdGgsIGNvbmZpZy5oZWlnaHQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHNldFRpbWVvdXQoZiwgZnJhbWVUaW1lb3V0KTtcclxuICAgICAgICAgICAgICAgICAgICB9LCBmcmFtZVRpbWVvdXQpO1xyXG4gICAgICAgICAgICAgICAgfTtcclxuICAgICAgICAgICAgICAgIHZpZGVvLnBsYXkoKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIHZhciB3b3JrZXI7XHJcblxyXG4gICAgZnVuY3Rpb24gc3RhcnRSZWNvcmRpbmcoc3RyZWFtLCBidWZmZXIpIHtcclxuICAgICAgICBpZiAoIWNvbmZpZy53b3JrZXJQYXRoICYmICFidWZmZXIpIHtcclxuICAgICAgICAgICAgLy8gaXMgaXQgc2FmZSB0byB1c2UgQGxhdGVzdCA/XHJcbiAgICAgICAgICAgIGZldGNoKFxyXG4gICAgICAgICAgICAgICAgJ2h0dHBzOi8vdW5wa2cuY29tL3dlYm0td2FzbUBsYXRlc3QvZGlzdC93ZWJtLXdvcmtlci5qcydcclxuICAgICAgICAgICAgKS50aGVuKGZ1bmN0aW9uKHIpIHtcclxuICAgICAgICAgICAgICAgIHIuYXJyYXlCdWZmZXIoKS50aGVuKGZ1bmN0aW9uKGJ1ZmZlcikge1xyXG4gICAgICAgICAgICAgICAgICAgIHN0YXJ0UmVjb3JkaW5nKHN0cmVhbSwgYnVmZmVyKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFjb25maWcud29ya2VyUGF0aCAmJiBidWZmZXIgaW5zdGFuY2VvZiBBcnJheUJ1ZmZlcikge1xyXG4gICAgICAgICAgICB2YXIgYmxvYiA9IG5ldyBCbG9iKFtidWZmZXJdLCB7XHJcbiAgICAgICAgICAgICAgICB0eXBlOiAndGV4dC9qYXZhc2NyaXB0J1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgY29uZmlnLndvcmtlclBhdGggPSBVUkwuY3JlYXRlT2JqZWN0VVJMKGJsb2IpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgaWYgKCFjb25maWcud29ya2VyUGF0aCkge1xyXG4gICAgICAgICAgICBjb25zb2xlLmVycm9yKCd3b3JrZXJQYXRoIHBhcmFtZXRlciBpcyBtaXNzaW5nLicpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgd29ya2VyID0gbmV3IFdvcmtlcihjb25maWcud29ya2VyUGF0aCk7XHJcblxyXG4gICAgICAgIHdvcmtlci5wb3N0TWVzc2FnZShjb25maWcud2ViQXNzZW1ibHlQYXRoIHx8ICdodHRwczovL3VucGtnLmNvbS93ZWJtLXdhc21AbGF0ZXN0L2Rpc3Qvd2VibS13YXNtLndhc20nKTtcclxuICAgICAgICB3b3JrZXIuYWRkRXZlbnRMaXN0ZW5lcignbWVzc2FnZScsIGZ1bmN0aW9uKGV2ZW50KSB7XHJcbiAgICAgICAgICAgIGlmIChldmVudC5kYXRhID09PSAnUkVBRFknKSB7XHJcbiAgICAgICAgICAgICAgICB3b3JrZXIucG9zdE1lc3NhZ2Uoe1xyXG4gICAgICAgICAgICAgICAgICAgIHdpZHRoOiBjb25maWcud2lkdGgsXHJcbiAgICAgICAgICAgICAgICAgICAgaGVpZ2h0OiBjb25maWcuaGVpZ2h0LFxyXG4gICAgICAgICAgICAgICAgICAgIGJpdHJhdGU6IGNvbmZpZy5iaXRyYXRlIHx8IDEyMDAsXHJcbiAgICAgICAgICAgICAgICAgICAgdGltZWJhc2VEZW46IGNvbmZpZy5mcmFtZVJhdGUgfHwgMzAsXHJcbiAgICAgICAgICAgICAgICAgICAgcmVhbHRpbWU6IHRydWVcclxuICAgICAgICAgICAgICAgIH0pO1xyXG5cclxuICAgICAgICAgICAgICAgIGNhbWVyYVN0cmVhbSgpLnBpcGVUbyhuZXcgV3JpdGFibGVTdHJlYW0oe1xyXG4gICAgICAgICAgICAgICAgICAgIHdyaXRlOiBmdW5jdGlvbihpbWFnZSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAoIXdvcmtlcikge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgICAgICAgICB3b3JrZXIucG9zdE1lc3NhZ2UoaW1hZ2UuZGF0YS5idWZmZXIsIFtpbWFnZS5kYXRhLmJ1ZmZlcl0pO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH0pKTtcclxuICAgICAgICAgICAgfSBlbHNlIGlmICghIWV2ZW50LmRhdGEpIHtcclxuICAgICAgICAgICAgICAgIGlmICghaXNQYXVzZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBhcnJheU9mQnVmZmVycy5wdXNoKGV2ZW50LmRhdGEpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZWNvcmRzIHZpZGVvLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFdlYkFzc2VtYmx5UmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5yZWNvcmQoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5yZWNvcmQgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBhcnJheU9mQnVmZmVycyA9IFtdO1xyXG4gICAgICAgIGlzUGF1c2VkID0gZmFsc2U7XHJcbiAgICAgICAgdGhpcy5ibG9iID0gbnVsbDtcclxuICAgICAgICBzdGFydFJlY29yZGluZyhzdHJlYW0pO1xyXG5cclxuICAgICAgICBpZiAodHlwZW9mIGNvbmZpZy5pbml0Q2FsbGJhY2sgPT09ICdmdW5jdGlvbicpIHtcclxuICAgICAgICAgICAgY29uZmlnLmluaXRDYWxsYmFjaygpO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG4gICAgdmFyIGlzUGF1c2VkO1xyXG5cclxuICAgIC8qKlxyXG4gICAgICogVGhpcyBtZXRob2QgcGF1c2VzIHRoZSByZWNvcmRpbmcgcHJvY2Vzcy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBXZWJBc3NlbWJseVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucGF1c2UoKTtcclxuICAgICAqL1xyXG4gICAgdGhpcy5wYXVzZSA9IGZ1bmN0aW9uKCkge1xyXG4gICAgICAgIGlzUGF1c2VkID0gdHJ1ZTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXN1bWVzIHRoZSByZWNvcmRpbmcgcHJvY2Vzcy5cclxuICAgICAqIEBtZXRob2RcclxuICAgICAqIEBtZW1iZXJvZiBXZWJBc3NlbWJseVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIucmVzdW1lKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMucmVzdW1lID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgaXNQYXVzZWQgPSBmYWxzZTtcclxuICAgIH07XHJcblxyXG4gICAgZnVuY3Rpb24gdGVybWluYXRlKCkge1xyXG4gICAgICAgIGlmICghd29ya2VyKSB7XHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHdvcmtlci5wb3N0TWVzc2FnZShudWxsKTtcclxuICAgICAgICB3b3JrZXIudGVybWluYXRlKCk7XHJcbiAgICAgICAgd29ya2VyID0gbnVsbDtcclxuICAgIH1cclxuXHJcbiAgICB2YXIgYXJyYXlPZkJ1ZmZlcnMgPSBbXTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIFRoaXMgbWV0aG9kIHN0b3BzIHJlY29yZGluZyB2aWRlby5cclxuICAgICAqIEBwYXJhbSB7ZnVuY3Rpb259IGNhbGxiYWNrIC0gQ2FsbGJhY2sgZnVuY3Rpb24sIHRoYXQgaXMgdXNlZCB0byBwYXNzIHJlY29yZGVkIGJsb2IgYmFjayB0byB0aGUgY2FsbGVlLlxyXG4gICAgICogQG1ldGhvZFxyXG4gICAgICogQG1lbWJlcm9mIFdlYkFzc2VtYmx5UmVjb3JkZXJcclxuICAgICAqIEBleGFtcGxlXHJcbiAgICAgKiByZWNvcmRlci5zdG9wKGZ1bmN0aW9uKGJsb2IpIHtcclxuICAgICAqICAgICB2aWRlby5zcmMgPSBVUkwuY3JlYXRlT2JqZWN0VVJMKGJsb2IpO1xyXG4gICAgICogfSk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuc3RvcCA9IGZ1bmN0aW9uKGNhbGxiYWNrKSB7XHJcbiAgICAgICAgdGVybWluYXRlKCk7XHJcblxyXG4gICAgICAgIHRoaXMuYmxvYiA9IG5ldyBCbG9iKGFycmF5T2ZCdWZmZXJzLCB7XHJcbiAgICAgICAgICAgIHR5cGU6ICd2aWRlby93ZWJtJ1xyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBjYWxsYmFjayh0aGlzLmJsb2IpO1xyXG4gICAgfTtcclxuXHJcbiAgICAvLyBmb3IgZGVidWdnaW5nXHJcbiAgICB0aGlzLm5hbWUgPSAnV2ViQXNzZW1ibHlSZWNvcmRlcic7XHJcbiAgICB0aGlzLnRvU3RyaW5nID0gZnVuY3Rpb24oKSB7XHJcbiAgICAgICAgcmV0dXJuIHRoaXMubmFtZTtcclxuICAgIH07XHJcblxyXG4gICAgLyoqXHJcbiAgICAgKiBUaGlzIG1ldGhvZCByZXNldHMgY3VycmVudGx5IHJlY29yZGVkIGRhdGEuXHJcbiAgICAgKiBAbWV0aG9kXHJcbiAgICAgKiBAbWVtYmVyb2YgV2ViQXNzZW1ibHlSZWNvcmRlclxyXG4gICAgICogQGV4YW1wbGVcclxuICAgICAqIHJlY29yZGVyLmNsZWFyUmVjb3JkZWREYXRhKCk7XHJcbiAgICAgKi9cclxuICAgIHRoaXMuY2xlYXJSZWNvcmRlZERhdGEgPSBmdW5jdGlvbigpIHtcclxuICAgICAgICBhcnJheU9mQnVmZmVycyA9IFtdO1xyXG4gICAgICAgIGlzUGF1c2VkID0gZmFsc2U7XHJcbiAgICAgICAgdGhpcy5ibG9iID0gbnVsbDtcclxuXHJcbiAgICAgICAgLy8gdG9kbzogaWYgcmVjb3JkaW5nLU9OIHRoZW4gU1RPUCBpdCBmaXJzdFxyXG4gICAgfTtcclxuXHJcbiAgICAvKipcclxuICAgICAqIEBwcm9wZXJ0eSB7QmxvYn0gYmxvYiAtIFRoZSByZWNvcmRlZCBibG9iIG9iamVjdC5cclxuICAgICAqIEBtZW1iZXJvZiBXZWJBc3NlbWJseVJlY29yZGVyXHJcbiAgICAgKiBAZXhhbXBsZVxyXG4gICAgICogcmVjb3JkZXIuc3RvcChmdW5jdGlvbigpe1xyXG4gICAgICogICAgIHZhciBibG9iID0gcmVjb3JkZXIuYmxvYjtcclxuICAgICAqIH0pO1xyXG4gICAgICovXHJcbiAgICB0aGlzLmJsb2IgPSBudWxsO1xyXG59XHJcblxyXG5pZiAodHlwZW9mIFJlY29yZFJUQyAhPT0gJ3VuZGVmaW5lZCcpIHtcclxuICAgIFJlY29yZFJUQy5XZWJBc3NlbWJseVJlY29yZGVyID0gV2ViQXNzZW1ibHlSZWNvcmRlcjtcclxufVxuXG5cblxuLy8vLy8vLy8vLy8vLy8vLy8vXG4vLyBXRUJQQUNLIEZPT1RFUlxuLy8gLi9ub2RlX21vZHVsZXMvcmVjb3JkcnRjL1JlY29yZFJUQy5qc1xuLy8gbW9kdWxlIGlkID0gMTdcbi8vIG1vZHVsZSBjaHVua3MgPSAwIDEiLCJ2YXIgZztcclxuXHJcbi8vIFRoaXMgd29ya3MgaW4gbm9uLXN0cmljdCBtb2RlXHJcbmcgPSAoZnVuY3Rpb24oKSB7XHJcblx0cmV0dXJuIHRoaXM7XHJcbn0pKCk7XHJcblxyXG50cnkge1xyXG5cdC8vIFRoaXMgd29ya3MgaWYgZXZhbCBpcyBhbGxvd2VkIChzZWUgQ1NQKVxyXG5cdGcgPSBnIHx8IEZ1bmN0aW9uKFwicmV0dXJuIHRoaXNcIikoKSB8fCAoMSxldmFsKShcInRoaXNcIik7XHJcbn0gY2F0Y2goZSkge1xyXG5cdC8vIFRoaXMgd29ya3MgaWYgdGhlIHdpbmRvdyByZWZlcmVuY2UgaXMgYXZhaWxhYmxlXHJcblx0aWYodHlwZW9mIHdpbmRvdyA9PT0gXCJvYmplY3RcIilcclxuXHRcdGcgPSB3aW5kb3c7XHJcbn1cclxuXHJcbi8vIGcgY2FuIHN0aWxsIGJlIHVuZGVmaW5lZCwgYnV0IG5vdGhpbmcgdG8gZG8gYWJvdXQgaXQuLi5cclxuLy8gV2UgcmV0dXJuIHVuZGVmaW5lZCwgaW5zdGVhZCBvZiBub3RoaW5nIGhlcmUsIHNvIGl0J3NcclxuLy8gZWFzaWVyIHRvIGhhbmRsZSB0aGlzIGNhc2UuIGlmKCFnbG9iYWwpIHsgLi4ufVxyXG5cclxubW9kdWxlLmV4cG9ydHMgPSBnO1xyXG5cblxuXG4vLy8vLy8vLy8vLy8vLy8vLy9cbi8vIFdFQlBBQ0sgRk9PVEVSXG4vLyAod2VicGFjaykvYnVpbGRpbi9nbG9iYWwuanNcbi8vIG1vZHVsZSBpZCA9IDE4XG4vLyBtb2R1bGUgY2h1bmtzID0gMCAxIiwiLy8gc2hpbSBmb3IgdXNpbmcgcHJvY2VzcyBpbiBicm93c2VyXG52YXIgcHJvY2VzcyA9IG1vZHVsZS5leHBvcnRzID0ge307XG5cbi8vIGNhY2hlZCBmcm9tIHdoYXRldmVyIGdsb2JhbCBpcyBwcmVzZW50IHNvIHRoYXQgdGVzdCBydW5uZXJzIHRoYXQgc3R1YiBpdFxuLy8gZG9uJ3QgYnJlYWsgdGhpbmdzLiAgQnV0IHdlIG5lZWQgdG8gd3JhcCBpdCBpbiBhIHRyeSBjYXRjaCBpbiBjYXNlIGl0IGlzXG4vLyB3cmFwcGVkIGluIHN0cmljdCBtb2RlIGNvZGUgd2hpY2ggZG9lc24ndCBkZWZpbmUgYW55IGdsb2JhbHMuICBJdCdzIGluc2lkZSBhXG4vLyBmdW5jdGlvbiBiZWNhdXNlIHRyeS9jYXRjaGVzIGRlb3B0aW1pemUgaW4gY2VydGFpbiBlbmdpbmVzLlxuXG52YXIgY2FjaGVkU2V0VGltZW91dDtcbnZhciBjYWNoZWRDbGVhclRpbWVvdXQ7XG5cbmZ1bmN0aW9uIGRlZmF1bHRTZXRUaW1vdXQoKSB7XG4gICAgdGhyb3cgbmV3IEVycm9yKCdzZXRUaW1lb3V0IGhhcyBub3QgYmVlbiBkZWZpbmVkJyk7XG59XG5mdW5jdGlvbiBkZWZhdWx0Q2xlYXJUaW1lb3V0ICgpIHtcbiAgICB0aHJvdyBuZXcgRXJyb3IoJ2NsZWFyVGltZW91dCBoYXMgbm90IGJlZW4gZGVmaW5lZCcpO1xufVxuKGZ1bmN0aW9uICgpIHtcbiAgICB0cnkge1xuICAgICAgICBpZiAodHlwZW9mIHNldFRpbWVvdXQgPT09ICdmdW5jdGlvbicpIHtcbiAgICAgICAgICAgIGNhY2hlZFNldFRpbWVvdXQgPSBzZXRUaW1lb3V0O1xuICAgICAgICB9IGVsc2Uge1xuICAgICAgICAgICAgY2FjaGVkU2V0VGltZW91dCA9IGRlZmF1bHRTZXRUaW1vdXQ7XG4gICAgICAgIH1cbiAgICB9IGNhdGNoIChlKSB7XG4gICAgICAgIGNhY2hlZFNldFRpbWVvdXQgPSBkZWZhdWx0U2V0VGltb3V0O1xuICAgIH1cbiAgICB0cnkge1xuICAgICAgICBpZiAodHlwZW9mIGNsZWFyVGltZW91dCA9PT0gJ2Z1bmN0aW9uJykge1xuICAgICAgICAgICAgY2FjaGVkQ2xlYXJUaW1lb3V0ID0gY2xlYXJUaW1lb3V0O1xuICAgICAgICB9IGVsc2Uge1xuICAgICAgICAgICAgY2FjaGVkQ2xlYXJUaW1lb3V0ID0gZGVmYXVsdENsZWFyVGltZW91dDtcbiAgICAgICAgfVxuICAgIH0gY2F0Y2ggKGUpIHtcbiAgICAgICAgY2FjaGVkQ2xlYXJUaW1lb3V0ID0gZGVmYXVsdENsZWFyVGltZW91dDtcbiAgICB9XG59ICgpKVxuZnVuY3Rpb24gcnVuVGltZW91dChmdW4pIHtcbiAgICBpZiAoY2FjaGVkU2V0VGltZW91dCA9PT0gc2V0VGltZW91dCkge1xuICAgICAgICAvL25vcm1hbCBlbnZpcm9tZW50cyBpbiBzYW5lIHNpdHVhdGlvbnNcbiAgICAgICAgcmV0dXJuIHNldFRpbWVvdXQoZnVuLCAwKTtcbiAgICB9XG4gICAgLy8gaWYgc2V0VGltZW91dCB3YXNuJ3QgYXZhaWxhYmxlIGJ1dCB3YXMgbGF0dGVyIGRlZmluZWRcbiAgICBpZiAoKGNhY2hlZFNldFRpbWVvdXQgPT09IGRlZmF1bHRTZXRUaW1vdXQgfHwgIWNhY2hlZFNldFRpbWVvdXQpICYmIHNldFRpbWVvdXQpIHtcbiAgICAgICAgY2FjaGVkU2V0VGltZW91dCA9IHNldFRpbWVvdXQ7XG4gICAgICAgIHJldHVybiBzZXRUaW1lb3V0KGZ1biwgMCk7XG4gICAgfVxuICAgIHRyeSB7XG4gICAgICAgIC8vIHdoZW4gd2hlbiBzb21lYm9keSBoYXMgc2NyZXdlZCB3aXRoIHNldFRpbWVvdXQgYnV0IG5vIEkuRS4gbWFkZG5lc3NcbiAgICAgICAgcmV0dXJuIGNhY2hlZFNldFRpbWVvdXQoZnVuLCAwKTtcbiAgICB9IGNhdGNoKGUpe1xuICAgICAgICB0cnkge1xuICAgICAgICAgICAgLy8gV2hlbiB3ZSBhcmUgaW4gSS5FLiBidXQgdGhlIHNjcmlwdCBoYXMgYmVlbiBldmFsZWQgc28gSS5FLiBkb2Vzbid0IHRydXN0IHRoZSBnbG9iYWwgb2JqZWN0IHdoZW4gY2FsbGVkIG5vcm1hbGx5XG4gICAgICAgICAgICByZXR1cm4gY2FjaGVkU2V0VGltZW91dC5jYWxsKG51bGwsIGZ1biwgMCk7XG4gICAgICAgIH0gY2F0Y2goZSl7XG4gICAgICAgICAgICAvLyBzYW1lIGFzIGFib3ZlIGJ1dCB3aGVuIGl0J3MgYSB2ZXJzaW9uIG9mIEkuRS4gdGhhdCBtdXN0IGhhdmUgdGhlIGdsb2JhbCBvYmplY3QgZm9yICd0aGlzJywgaG9wZnVsbHkgb3VyIGNvbnRleHQgY29ycmVjdCBvdGhlcndpc2UgaXQgd2lsbCB0aHJvdyBhIGdsb2JhbCBlcnJvclxuICAgICAgICAgICAgcmV0dXJuIGNhY2hlZFNldFRpbWVvdXQuY2FsbCh0aGlzLCBmdW4sIDApO1xuICAgICAgICB9XG4gICAgfVxuXG5cbn1cbmZ1bmN0aW9uIHJ1bkNsZWFyVGltZW91dChtYXJrZXIpIHtcbiAgICBpZiAoY2FjaGVkQ2xlYXJUaW1lb3V0ID09PSBjbGVhclRpbWVvdXQpIHtcbiAgICAgICAgLy9ub3JtYWwgZW52aXJvbWVudHMgaW4gc2FuZSBzaXR1YXRpb25zXG4gICAgICAgIHJldHVybiBjbGVhclRpbWVvdXQobWFya2VyKTtcbiAgICB9XG4gICAgLy8gaWYgY2xlYXJUaW1lb3V0IHdhc24ndCBhdmFpbGFibGUgYnV0IHdhcyBsYXR0ZXIgZGVmaW5lZFxuICAgIGlmICgoY2FjaGVkQ2xlYXJUaW1lb3V0ID09PSBkZWZhdWx0Q2xlYXJUaW1lb3V0IHx8ICFjYWNoZWRDbGVhclRpbWVvdXQpICYmIGNsZWFyVGltZW91dCkge1xuICAgICAgICBjYWNoZWRDbGVhclRpbWVvdXQgPSBjbGVhclRpbWVvdXQ7XG4gICAgICAgIHJldHVybiBjbGVhclRpbWVvdXQobWFya2VyKTtcbiAgICB9XG4gICAgdHJ5IHtcbiAgICAgICAgLy8gd2hlbiB3aGVuIHNvbWVib2R5IGhhcyBzY3Jld2VkIHdpdGggc2V0VGltZW91dCBidXQgbm8gSS5FLiBtYWRkbmVzc1xuICAgICAgICByZXR1cm4gY2FjaGVkQ2xlYXJUaW1lb3V0KG1hcmtlcik7XG4gICAgfSBjYXRjaCAoZSl7XG4gICAgICAgIHRyeSB7XG4gICAgICAgICAgICAvLyBXaGVuIHdlIGFyZSBpbiBJLkUuIGJ1dCB0aGUgc2NyaXB0IGhhcyBiZWVuIGV2YWxlZCBzbyBJLkUuIGRvZXNuJ3QgIHRydXN0IHRoZSBnbG9iYWwgb2JqZWN0IHdoZW4gY2FsbGVkIG5vcm1hbGx5XG4gICAgICAgICAgICByZXR1cm4gY2FjaGVkQ2xlYXJUaW1lb3V0LmNhbGwobnVsbCwgbWFya2VyKTtcbiAgICAgICAgfSBjYXRjaCAoZSl7XG4gICAgICAgICAgICAvLyBzYW1lIGFzIGFib3ZlIGJ1dCB3aGVuIGl0J3MgYSB2ZXJzaW9uIG9mIEkuRS4gdGhhdCBtdXN0IGhhdmUgdGhlIGdsb2JhbCBvYmplY3QgZm9yICd0aGlzJywgaG9wZnVsbHkgb3VyIGNvbnRleHQgY29ycmVjdCBvdGhlcndpc2UgaXQgd2lsbCB0aHJvdyBhIGdsb2JhbCBlcnJvci5cbiAgICAgICAgICAgIC8vIFNvbWUgdmVyc2lvbnMgb2YgSS5FLiBoYXZlIGRpZmZlcmVudCBydWxlcyBmb3IgY2xlYXJUaW1lb3V0IHZzIHNldFRpbWVvdXRcbiAgICAgICAgICAgIHJldHVybiBjYWNoZWRDbGVhclRpbWVvdXQuY2FsbCh0aGlzLCBtYXJrZXIpO1xuICAgICAgICB9XG4gICAgfVxuXG5cblxufVxudmFyIHF1ZXVlID0gW107XG52YXIgZHJhaW5pbmcgPSBmYWxzZTtcbnZhciBjdXJyZW50UXVldWU7XG52YXIgcXVldWVJbmRleCA9IC0xO1xuXG5mdW5jdGlvbiBjbGVhblVwTmV4dFRpY2soKSB7XG4gICAgaWYgKCFkcmFpbmluZyB8fCAhY3VycmVudFF1ZXVlKSB7XG4gICAgICAgIHJldHVybjtcbiAgICB9XG4gICAgZHJhaW5pbmcgPSBmYWxzZTtcbiAgICBpZiAoY3VycmVudFF1ZXVlLmxlbmd0aCkge1xuICAgICAgICBxdWV1ZSA9IGN1cnJlbnRRdWV1ZS5jb25jYXQocXVldWUpO1xuICAgIH0gZWxzZSB7XG4gICAgICAgIHF1ZXVlSW5kZXggPSAtMTtcbiAgICB9XG4gICAgaWYgKHF1ZXVlLmxlbmd0aCkge1xuICAgICAgICBkcmFpblF1ZXVlKCk7XG4gICAgfVxufVxuXG5mdW5jdGlvbiBkcmFpblF1ZXVlKCkge1xuICAgIGlmIChkcmFpbmluZykge1xuICAgICAgICByZXR1cm47XG4gICAgfVxuICAgIHZhciB0aW1lb3V0ID0gcnVuVGltZW91dChjbGVhblVwTmV4dFRpY2spO1xuICAgIGRyYWluaW5nID0gdHJ1ZTtcblxuICAgIHZhciBsZW4gPSBxdWV1ZS5sZW5ndGg7XG4gICAgd2hpbGUobGVuKSB7XG4gICAgICAgIGN1cnJlbnRRdWV1ZSA9IHF1ZXVlO1xuICAgICAgICBxdWV1ZSA9IFtdO1xuICAgICAgICB3aGlsZSAoKytxdWV1ZUluZGV4IDwgbGVuKSB7XG4gICAgICAgICAgICBpZiAoY3VycmVudFF1ZXVlKSB7XG4gICAgICAgICAgICAgICAgY3VycmVudFF1ZXVlW3F1ZXVlSW5kZXhdLnJ1bigpO1xuICAgICAgICAgICAgfVxuICAgICAgICB9XG4gICAgICAgIHF1ZXVlSW5kZXggPSAtMTtcbiAgICAgICAgbGVuID0gcXVldWUubGVuZ3RoO1xuICAgIH1cbiAgICBjdXJyZW50UXVldWUgPSBudWxsO1xuICAgIGRyYWluaW5nID0gZmFsc2U7XG4gICAgcnVuQ2xlYXJUaW1lb3V0KHRpbWVvdXQpO1xufVxuXG5wcm9jZXNzLm5leHRUaWNrID0gZnVuY3Rpb24gKGZ1bikge1xuICAgIHZhciBhcmdzID0gbmV3IEFycmF5KGFyZ3VtZW50cy5sZW5ndGggLSAxKTtcbiAgICBpZiAoYXJndW1lbnRzLmxlbmd0aCA+IDEpIHtcbiAgICAgICAgZm9yICh2YXIgaSA9IDE7IGkgPCBhcmd1bWVudHMubGVuZ3RoOyBpKyspIHtcbiAgICAgICAgICAgIGFyZ3NbaSAtIDFdID0gYXJndW1lbnRzW2ldO1xuICAgICAgICB9XG4gICAgfVxuICAgIHF1ZXVlLnB1c2gobmV3IEl0ZW0oZnVuLCBhcmdzKSk7XG4gICAgaWYgKHF1ZXVlLmxlbmd0aCA9PT0gMSAmJiAhZHJhaW5pbmcpIHtcbiAgICAgICAgcnVuVGltZW91dChkcmFpblF1ZXVlKTtcbiAgICB9XG59O1xuXG4vLyB2OCBsaWtlcyBwcmVkaWN0aWJsZSBvYmplY3RzXG5mdW5jdGlvbiBJdGVtKGZ1biwgYXJyYXkpIHtcbiAgICB0aGlzLmZ1biA9IGZ1bjtcbiAgICB0aGlzLmFycmF5ID0gYXJyYXk7XG59XG5JdGVtLnByb3RvdHlwZS5ydW4gPSBmdW5jdGlvbiAoKSB7XG4gICAgdGhpcy5mdW4uYXBwbHkobnVsbCwgdGhpcy5hcnJheSk7XG59O1xucHJvY2Vzcy50aXRsZSA9ICdicm93c2VyJztcbnByb2Nlc3MuYnJvd3NlciA9IHRydWU7XG5wcm9jZXNzLmVudiA9IHt9O1xucHJvY2Vzcy5hcmd2ID0gW107XG5wcm9jZXNzLnZlcnNpb24gPSAnJzsgLy8gZW1wdHkgc3RyaW5nIHRvIGF2b2lkIHJlZ2V4cCBpc3N1ZXNcbnByb2Nlc3MudmVyc2lvbnMgPSB7fTtcblxuZnVuY3Rpb24gbm9vcCgpIHt9XG5cbnByb2Nlc3Mub24gPSBub29wO1xucHJvY2Vzcy5hZGRMaXN0ZW5lciA9IG5vb3A7XG5wcm9jZXNzLm9uY2UgPSBub29wO1xucHJvY2Vzcy5vZmYgPSBub29wO1xucHJvY2Vzcy5yZW1vdmVMaXN0ZW5lciA9IG5vb3A7XG5wcm9jZXNzLnJlbW92ZUFsbExpc3RlbmVycyA9IG5vb3A7XG5wcm9jZXNzLmVtaXQgPSBub29wO1xucHJvY2Vzcy5wcmVwZW5kTGlzdGVuZXIgPSBub29wO1xucHJvY2Vzcy5wcmVwZW5kT25jZUxpc3RlbmVyID0gbm9vcDtcblxucHJvY2Vzcy5saXN0ZW5lcnMgPSBmdW5jdGlvbiAobmFtZSkgeyByZXR1cm4gW10gfVxuXG5wcm9jZXNzLmJpbmRpbmcgPSBmdW5jdGlvbiAobmFtZSkge1xuICAgIHRocm93IG5ldyBFcnJvcigncHJvY2Vzcy5iaW5kaW5nIGlzIG5vdCBzdXBwb3J0ZWQnKTtcbn07XG5cbnByb2Nlc3MuY3dkID0gZnVuY3Rpb24gKCkgeyByZXR1cm4gJy8nIH07XG5wcm9jZXNzLmNoZGlyID0gZnVuY3Rpb24gKGRpcikge1xuICAgIHRocm93IG5ldyBFcnJvcigncHJvY2Vzcy5jaGRpciBpcyBub3Qgc3VwcG9ydGVkJyk7XG59O1xucHJvY2Vzcy51bWFzayA9IGZ1bmN0aW9uKCkgeyByZXR1cm4gMDsgfTtcblxuXG5cbi8vLy8vLy8vLy8vLy8vLy8vL1xuLy8gV0VCUEFDSyBGT09URVJcbi8vIC4vbm9kZV9tb2R1bGVzL3Byb2Nlc3MvYnJvd3Nlci5qc1xuLy8gbW9kdWxlIGlkID0gMTlcbi8vIG1vZHVsZSBjaHVua3MgPSAwIDEiXSwic291cmNlUm9vdCI6IiJ9