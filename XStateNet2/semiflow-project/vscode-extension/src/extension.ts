import * as vscode from 'vscode';

export function activate(context: vscode.ExtensionContext) {
    console.log('Semi Flow Language extension activated');
    
    // Register validator
    const validator = vscode.languages.registerDocumentFormattingEditProvider(
        'semiflow',
        {
            provideDocumentFormattingEdits(document) {
                // Validation logic here
                return [];
            }
        }
    );
    
    context.subscriptions.push(validator);
}

export function deactivate() {}
