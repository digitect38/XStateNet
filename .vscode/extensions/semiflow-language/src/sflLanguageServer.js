"use strict";
exports.__esModule = true;
exports.SFLLanguageServer = void 0;
var sflValidator_1 = require("./sflValidator");
var SFLLanguageServer = /** @class */ (function () {
    function SFLLanguageServer() {
        this.validator = new sflValidator_1.SFLValidator();
    }
    SFLLanguageServer.prototype.initialize = function () {
        console.log('Semi Flow Language Server initialized');
    };
    SFLLanguageServer.prototype.validate = function (text) {
        return this.validator.validateDocument(text);
    };
    return SFLLanguageServer;
}());
exports.SFLLanguageServer = SFLLanguageServer;
var server = new SFLLanguageServer();
server.initialize();
