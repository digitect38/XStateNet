"use strict";
exports.__esModule = true;
exports.deactivate = exports.activate = void 0;
var vscode = require("vscode");
function activate(context) {
    console.log('Semi Flow Language extension is now active!');
    var disposable = vscode.commands.registerCommand('semiflow.validate', function () {
        vscode.window.showInformationMessage('Semi Flow Language Active!');
    });
    context.subscriptions.push(disposable);
}
exports.activate = activate;
function deactivate() { }
exports.deactivate = deactivate;
