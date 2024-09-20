/*
 * ATTENTION: The "eval" devtool has been used (maybe by default in mode: "development").
 * This devtool is neither made for production nor for readable output files.
 * It uses "eval()" calls to create a separate source file in the browser devtools.
 * If you are trying to read the output file, select a different devtool (https://webpack.js.org/configuration/devtool/)
 * or disable the default devtool with "devtool: false".
 * If you are looking for production-ready output files, see mode: "production" (https://webpack.js.org/configuration/mode/).
 */
/******/ (() => { // webpackBootstrap
/******/ 	var __webpack_modules__ = ({

/***/ "./node_modules/bootstrap-cookie-alert/cookiealert.js":
/*!************************************************************!*\
  !*** ./node_modules/bootstrap-cookie-alert/cookiealert.js ***!
  \************************************************************/
/***/ (() => {

eval("/*\r\n * Bootstrap Cookie Alert by Wruczek\r\n * https://github.com/Wruczek/Bootstrap-Cookie-Alert\r\n * Released under MIT license\r\n */\r\n(function () {\r\n    \"use strict\";\r\n\r\n    var cookieAlert = document.querySelector(\".cookiealert\");\r\n    var acceptCookies = document.querySelector(\".acceptcookies\");\r\n\r\n    if (!cookieAlert) {\r\n       return;\r\n    }\r\n\r\n    cookieAlert.offsetHeight; // Force browser to trigger reflow (https://stackoverflow.com/a/39451131)\r\n\r\n    // Show the alert if we cant find the \"acceptCookies\" cookie\r\n    if (!getCookie(\"acceptCookies\")) {\r\n        cookieAlert.classList.add(\"show\");\r\n    }\r\n\r\n    // When clicking on the agree button, create a 1 year\r\n    // cookie to remember user's choice and close the banner\r\n    acceptCookies.addEventListener(\"click\", function () {\r\n        setCookie(\"acceptCookies\", true, 365);\r\n        cookieAlert.classList.remove(\"show\");\r\n\r\n        // dispatch the accept event\r\n        window.dispatchEvent(new Event(\"cookieAlertAccept\"))\r\n    });\r\n\r\n    // Cookie functions from w3schools\r\n    function setCookie(cname, cvalue, exdays) {\r\n        var d = new Date();\r\n        d.setTime(d.getTime() + (exdays * 24 * 60 * 60 * 1000));\r\n        var expires = \"expires=\" + d.toUTCString();\r\n        document.cookie = cname + \"=\" + cvalue + \";\" + expires + \";path=/\";\r\n    }\r\n\r\n    function getCookie(cname) {\r\n        var name = cname + \"=\";\r\n        var decodedCookie = decodeURIComponent(document.cookie);\r\n        var ca = decodedCookie.split(';');\r\n        for (var i = 0; i < ca.length; i++) {\r\n            var c = ca[i];\r\n            while (c.charAt(0) === ' ') {\r\n                c = c.substring(1);\r\n            }\r\n            if (c.indexOf(name) === 0) {\r\n                return c.substring(name.length, c.length);\r\n            }\r\n        }\r\n        return \"\";\r\n    }\r\n})();\r\n\n\n//# sourceURL=webpack://keenthemes/./node_modules/bootstrap-cookie-alert/cookiealert.js?");

/***/ }),

/***/ "./webpack/plugins/custom/cookiealert/cookiealert.scss":
/*!*************************************************************!*\
  !*** ./webpack/plugins/custom/cookiealert/cookiealert.scss ***!
  \*************************************************************/
/***/ ((__unused_webpack_module, __webpack_exports__, __webpack_require__) => {

"use strict";
eval("__webpack_require__.r(__webpack_exports__);\n// extracted by mini-css-extract-plugin\n\n\n//# sourceURL=webpack://keenthemes/./webpack/plugins/custom/cookiealert/cookiealert.scss?");

/***/ }),

/***/ "./webpack/plugins/custom/cookiealert/cookiealert.js":
/*!***********************************************************!*\
  !*** ./webpack/plugins/custom/cookiealert/cookiealert.js ***!
  \***********************************************************/
/***/ ((__unused_webpack_module, __unused_webpack_exports, __webpack_require__) => {

eval("// Cookiealert -  A simple, good looking cookie alert for Bootstrap: https://github.com/Wruczek/Bootstrap-Cookie-Alert\n\n__webpack_require__(/*! bootstrap-cookie-alert/cookiealert.js */ \"./node_modules/bootstrap-cookie-alert/cookiealert.js\");\n\n__webpack_require__(/*! ./cookiealert.scss */ \"./webpack/plugins/custom/cookiealert/cookiealert.scss\");\n\n\n//# sourceURL=webpack://keenthemes/./webpack/plugins/custom/cookiealert/cookiealert.js?");

/***/ })

/******/ 	});
/************************************************************************/
/******/ 	// The module cache
/******/ 	var __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		var cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		__webpack_modules__[moduleId](module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/make namespace object */
/******/ 	(() => {
/******/ 		// define __esModule on exports
/******/ 		__webpack_require__.r = (exports) => {
/******/ 			if(typeof Symbol !== 'undefined' && Symbol.toStringTag) {
/******/ 				Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });
/******/ 			}
/******/ 			Object.defineProperty(exports, '__esModule', { value: true });
/******/ 		};
/******/ 	})();
/******/ 	
/************************************************************************/
/******/ 	
/******/ 	// startup
/******/ 	// Load entry module and return exports
/******/ 	// This entry module can't be inlined because the eval devtool is used.
/******/ 	var __webpack_exports__ = __webpack_require__("./webpack/plugins/custom/cookiealert/cookiealert.js");
/******/ 	
/******/ })()
;