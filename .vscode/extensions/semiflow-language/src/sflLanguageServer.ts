import {
    createConnection,
    TextDocuments,
    ProposedFeatures,
    InitializeParams,
    InitializeResult,
    TextDocumentSyncKind,
    CompletionItem,
    CompletionItemKind
} from 'vscode-languageserver/node';

import { TextDocument } from 'vscode-languageserver-textdocument';
import { SFLValidator } from './sflValidator';

const connection = createConnection(ProposedFeatures.all);
const documents: TextDocuments<TextDocument> = new TextDocuments(TextDocument);
const validator = new SFLValidator();

connection.onInitialize((params: InitializeParams): InitializeResult => {
    return {
        capabilities: {
            textDocumentSync: TextDocumentSyncKind.Incremental,
            completionProvider: {
                resolveProvider: true,
                triggerCharacters: ['.', ':', '[', '(', '"']
            },
            hoverProvider: true
        }
    };
});

connection.onInitialized(() => {
    connection.console.log('Semi Flow Language Server initialized!');
});

documents.onDidChangeContent(change => {
    validateTextDocument(change.document);
});

async function validateTextDocument(textDocument: TextDocument): Promise<void> {
    const text = textDocument.getText();
    const diagnostics = validator.validateDocument(text);
    connection.sendDiagnostics({ uri: textDocument.uri, diagnostics });
}

connection.onCompletion((): CompletionItem[] => {
    return [
        // Rules
        {
            label: 'PSR_001',
            kind: CompletionItemKind.Constant,
            detail: 'Pipeline Slot Assignment'
        },
        {
            label: 'WAR_001',
            kind: CompletionItemKind.Constant,
            detail: 'Cyclic Zip Distribution'
        },
        {
            label: 'SSR_001',
            kind: CompletionItemKind.Constant,
            detail: 'Three Phase Steady State'
        },
        // Keywords
        {
            label: 'SCHEDULE',
            kind: CompletionItemKind.Keyword,
            insertText: 'SCHEDULE ${1:name} {\n\t$0\n}',
            insertTextFormat: 2
        },
        {
            label: 'APPLY_RULE',
            kind: CompletionItemKind.Function,
            insertText: 'APPLY_RULE("${1|PSR_001,WAR_001,SSR_001|}")',
            insertTextFormat: 2
        },
        {
            label: 'FORMULA',
            kind: CompletionItemKind.Function,
            insertText: 'FORMULA[(i % ${1:3}) == ${2:0}, i IN [${3:1}..${4:25}]]',
            insertTextFormat: 2
        }
    ];
});

documents.listen(connection);
connection.listen();