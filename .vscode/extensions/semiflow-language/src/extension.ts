import * as vscode from 'vscode';

export function activate(context: vscode.ExtensionContext) {
    console.log('Semi Flow Language extension is now active!');
    
    const disposable = vscode.commands.registerCommand('semiflow.validate', () => {
        vscode.window.showInformationMessage('Semi Flow Language Active!');
    });
    
    context.subscriptions.push(disposable);
}

export function deactivate() {}
