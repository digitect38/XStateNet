import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
} from 'vscode-languageclient/node';
import { createVisualizer } from './visualizer';

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;

export async function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('SemiFlow');
    outputChannel.appendLine('SemiFlow Language extension activating...');

    // Start language server
    await startLanguageServer(context);

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('semiflow.compile', () => compileSfl()),
        vscode.commands.registerCommand('semiflow.visualize', () => visualizeSfl(context)),
        vscode.commands.registerCommand('semiflow.visualizeJson', () => visualizeSfl(context)),
        vscode.commands.registerCommand('semiflow.restartServer', () => restartServer(context)),
        outputChannel
    );

    outputChannel.appendLine('SemiFlow Language extension activated');
}

export async function deactivate(): Promise<void> {
    if (client) {
        await client.stop();
    }
}

async function startLanguageServer(context: vscode.ExtensionContext): Promise<void> {
    const serverDll = findLanguageServerDll(context);
    if (!serverDll) {
        outputChannel.appendLine('WARNING: SemiFlow Language Server DLL not found.');
        outputChannel.appendLine('Set "semiflow.languageServerPath" in settings or build the SemiFlow2.LanguageServer project.');
        return;
    }

    outputChannel.appendLine(`Using language server: ${serverDll}`);

    const config = vscode.workspace.getConfiguration('semiflow');
    const dotnetPath = config.get<string>('dotnetPath', 'dotnet');

    const serverOptions: ServerOptions = {
        run: {
            command: dotnetPath,
            args: [serverDll],
            options: { env: { ...process.env } }
        },
        debug: {
            command: dotnetPath,
            args: [serverDll, '--debug'],
            options: { env: { ...process.env } }
        }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'semiflow' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{sfl,sfli,sflc}')
        },
        outputChannel,
        traceOutputChannel: outputChannel,
    };

    client = new LanguageClient(
        'semiflow',
        'SemiFlow Language Server',
        serverOptions,
        clientOptions
    );

    try {
        await client.start();
        outputChannel.appendLine('Language server started successfully');
    } catch (error) {
        outputChannel.appendLine(`Failed to start language server: ${error}`);
        vscode.window.showWarningMessage(
            'SemiFlow Language Server failed to start. Check Output > SemiFlow for details.'
        );
    }
}

function findLanguageServerDll(context: vscode.ExtensionContext): string | undefined {
    const config = vscode.workspace.getConfiguration('semiflow');
    const configuredPath = config.get<string>('languageServerPath', '');

    // 1. User-configured path
    if (configuredPath && fs.existsSync(configuredPath)) {
        return configuredPath;
    }

    // 2. Bundled with extension
    const bundledPath = path.join(context.extensionPath, 'server', 'SemiFlow2.LanguageServer.dll');
    if (fs.existsSync(bundledPath)) {
        return bundledPath;
    }

    // 3. Development paths (relative to extension in the repo)
    const devPaths = [
        path.resolve(context.extensionPath, '..', '..', '..', 'SemiFlow2.LanguageServer', 'bin', 'Debug', 'net8.0', 'SemiFlow2.LanguageServer.dll'),
        path.resolve(context.extensionPath, '..', '..', '..', 'SemiFlow2.LanguageServer', 'bin', 'Release', 'net8.0', 'SemiFlow2.LanguageServer.dll'),
    ];

    if (vscode.workspace.workspaceFolders) {
        for (const folder of vscode.workspace.workspaceFolders) {
            devPaths.push(
                path.join(folder.uri.fsPath, 'SemiFlow2.LanguageServer', 'bin', 'Debug', 'net8.0', 'SemiFlow2.LanguageServer.dll'),
                path.join(folder.uri.fsPath, 'SemiFlow2.LanguageServer', 'bin', 'Release', 'net8.0', 'SemiFlow2.LanguageServer.dll')
            );
        }
    }

    for (const p of devPaths) {
        if (fs.existsSync(p)) {
            return p;
        }
    }

    return undefined;
}

async function compileSfl(): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'semiflow') {
        vscode.window.showWarningMessage('Open an SFL file to compile.');
        return;
    }

    if (!client) {
        vscode.window.showWarningMessage('Language server not running. Cannot compile.');
        return;
    }

    try {
        const result = await client.sendRequest<CompileResult>('semiflow/compile', {
            uri: editor.document.uri.toString(),
            text: editor.document.getText()
        });

        if (result && result.json) {
            const doc = await vscode.workspace.openTextDocument({
                content: result.json,
                language: 'json'
            });
            await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside);

            if (result.success) {
                vscode.window.showInformationMessage('SFL compiled successfully to XState JSON');
            } else {
                vscode.window.showWarningMessage('SFL compiled with errors. Check diagnostics.');
            }
        }
    } catch (error) {
        vscode.window.showErrorMessage(`Compilation failed: ${error}`);
    }
}

async function visualizeSfl(context: vscode.ExtensionContext): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showWarningMessage('Open an SFL or XState JSON file to visualize.');
        return;
    }

    const langId = editor.document.languageId;

    // JSON file — visualize directly
    if (langId === 'json' || langId === 'jsonc') {
        const text = editor.document.getText().trim();
        try {
            const parsed = JSON.parse(text);
            if (!parsed.states) {
                vscode.window.showWarningMessage('Not a valid XState JSON: missing "states" property.');
                return;
            }
        } catch {
            vscode.window.showWarningMessage('Current file is not valid JSON.');
            return;
        }
        createVisualizer(context, text, editor.document.fileName);
        return;
    }

    // SFL file — compile via language server first
    if (langId === 'semiflow') {
        if (!client) {
            vscode.window.showWarningMessage('Language server not running. Cannot visualize.');
            return;
        }
        try {
            const result = await client.sendRequest<CompileResult>('semiflow/compile', {
                uri: editor.document.uri.toString(),
                text: editor.document.getText()
            });
            if (result && result.json) {
                createVisualizer(context, result.json, editor.document.fileName, editor.document.getText());
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Visualization failed: ${error}`);
        }
        return;
    }

    vscode.window.showWarningMessage('Open an SFL or XState JSON file to visualize.');
}

async function restartServer(context: vscode.ExtensionContext): Promise<void> {
    if (client) {
        await client.stop();
        client = undefined;
    }
    await startLanguageServer(context);
    vscode.window.showInformationMessage('SemiFlow Language Server restarted');
}

interface CompileResult {
    success: boolean;
    json: string;
    diagnosticCount: number;
}
