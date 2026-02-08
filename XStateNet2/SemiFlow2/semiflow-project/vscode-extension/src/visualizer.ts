import * as vscode from 'vscode';
import * as path from 'path';

let currentPanel: vscode.WebviewPanel | undefined;

export function createVisualizer(
    context: vscode.ExtensionContext,
    xstateJson: string,
    fileName: string,
    sflSource?: string
): void {
    const title = `SFL: ${path.basename(fileName)}`;

    if (currentPanel) {
        currentPanel.reveal(vscode.ViewColumn.Beside);
        currentPanel.title = title;
        updateVisualizerContent(currentPanel, xstateJson, sflSource);
        return;
    }

    currentPanel = vscode.window.createWebviewPanel(
        'sflVisualizer',
        title,
        vscode.ViewColumn.Beside,
        {
            enableScripts: true,
            retainContextWhenHidden: true
        }
    );

    currentPanel.onDidDispose(() => {
        currentPanel = undefined;
    }, null, context.subscriptions);

    updateVisualizerContent(currentPanel, xstateJson, sflSource);
}

// --- Data structures ---

interface MermaidDiagram { name: string; code: string; }

interface LayerTab {
    id: string;           // tab id: "L1_MSC_001"
    label: string;        // tab label: "L1: MSC_001"
    schedulerType: string;
    schedulerName: string;
    layer: string;
    sflBlock: string;     // extracted SFL source for this scheduler
    diagram: MermaidDiagram;
}

interface XStateNode {
    initial?: string;
    type?: string;
    states?: Record<string, XStateNode>;
    on?: Record<string, Array<{ target?: string[] | string }> | string | { target?: string[] | string } | null>;
    after?: Record<string, Array<{ target?: string[] | string }>>;
    meta?: Record<string, unknown>;
}

interface XStateMachine {
    id: string;
    type?: string;
    initial?: string;
    states: Record<string, XStateNode>;
}

// --- Build visualization data ---

function updateVisualizerContent(panel: vscode.WebviewPanel, xstateJson: string, sflSource?: string): void {
    const diagrams = xstateToMermaidDiagrams(xstateJson);
    const layerTabs = buildLayerTabs(diagrams, xstateJson, sflSource);
    const workflowCode = sflSource ? sflToWorkflowDiagram(sflSource) : '';
    panel.webview.html = getWebviewHtml(layerTabs, workflowCode, xstateJson, sflSource);
}

function buildLayerTabs(diagrams: MermaidDiagram[], xstateJson: string, sflSource?: string): LayerTab[] {
    const tabs: LayerTab[] = [];
    const sflBlocks = sflSource ? extractSflBlocks(sflSource) : new Map<string, { type: string; block: string }>();

    let machine: XStateMachine;
    try { machine = JSON.parse(xstateJson); } catch { return tabs; }

    for (const diag of diagrams) {
        const node = machine.states?.[diag.name];
        const meta = node?.meta as Record<string, string> | undefined;
        const schedulerType = meta?.schedulerType || 'Unknown';
        const layer = meta?.layer || 'L4';
        const sflInfo = sflBlocks.get(diag.name);

        tabs.push({
            id: `${layer}_${diag.name}`,
            label: `${layer}: ${diag.name}`,
            schedulerType: sflInfo?.type || schedulerType,
            schedulerName: diag.name,
            layer,
            sflBlock: sflInfo?.block || '',
            diagram: diag
        });
    }

    // Sort by layer
    tabs.sort((a, b) => a.layer.localeCompare(b.layer));
    return tabs;
}

function extractSflBlocks(sfl: string): Map<string, { type: string; block: string }> {
    const blocks = new Map<string, { type: string; block: string }>();
    const pattern = /\b(MASTER_SCHEDULER|WAFER_SCHEDULER|ROBOT_SCHEDULER|STATION)\s+(\w+)\s*\{/g;
    const matches: { type: string; name: string; start: number }[] = [];

    let m: RegExpExecArray | null;
    while ((m = pattern.exec(sfl)) !== null) {
        matches.push({ type: m[1], name: m[2], start: m.index });
    }

    for (let i = 0; i < matches.length; i++) {
        const start = matches[i].start;
        // Find balanced closing brace
        let depth = 0;
        let end = sfl.indexOf('{', start);
        for (let j = end; j < sfl.length; j++) {
            if (sfl[j] === '{') { depth++; }
            if (sfl[j] === '}') { depth--; }
            if (depth === 0) { end = j + 1; break; }
        }
        blocks.set(matches[i].name, {
            type: matches[i].type,
            block: sfl.substring(start, end)
        });
    }

    return blocks;
}

// --- Mermaid generators ---

function prefixed(name: string, prefix: string | null): string {
    return prefix ? `${prefix}_${name}` : name;
}

function xstateToMermaidDiagrams(jsonStr: string): MermaidDiagram[] {
    try {
        const machine: XStateMachine = JSON.parse(jsonStr);
        if (machine.type === 'parallel' && machine.states) {
            return Object.entries(machine.states).map(([name, node]) => {
                const lines: string[] = ['stateDiagram-v2', '    direction LR'];
                renderStateContent(node, lines, '    ', null);
                return { name, code: lines.join('\n') };
            });
        } else if (machine.states) {
            const lines: string[] = ['stateDiagram-v2'];
            if (machine.initial) { lines.push(`    [*] --> ${machine.initial}`); }
            for (const [name, node] of Object.entries(machine.states)) {
                renderStateTransitions(name, node, lines, '    ', null);
            }
            return [{ name: machine.id || 'machine', code: lines.join('\n') }];
        }
        return [{ name: 'empty', code: 'stateDiagram-v2\n    note "No states found"' }];
    } catch {
        return [{ name: 'error', code: 'stateDiagram-v2\n    note "Failed to parse"' }];
    }
}

function renderStateContent(node: XStateNode, lines: string[], indent: string, prefix: string | null): void {
    if (!node.states) { return; }
    if (prefix) {
        for (const name of Object.keys(node.states)) {
            lines.push(`${indent}state "${name}" as ${prefix}_${name}`);
        }
    }
    if (node.initial) { lines.push(`${indent}[*] --> ${prefixed(node.initial, prefix)}`); }
    for (const [name, child] of Object.entries(node.states)) {
        const sid = prefixed(name, prefix);
        if (child.states) {
            lines.push(`${indent}state ${sid} {`);
            renderStateContent(child, lines, indent + '    ', prefix ? `${prefix}_${name}` : name);
            lines.push(`${indent}}`);
        }
        renderStateTransitions(sid, child, lines, indent, prefix);
    }
}

function renderStateTransitions(name: string, node: XStateNode, lines: string[], indent: string, prefix: string | null): void {
    if (node.type === 'final') { lines.push(`${indent}${name} --> [*]`); }
    if (node.on) {
        for (const [event, transitions] of Object.entries(node.on)) {
            if (Array.isArray(transitions)) {
                for (const t of transitions) {
                    const target = getTarget(t);
                    if (target && prefixed(target, prefix) !== name) {
                        lines.push(`${indent}${name} --> ${prefixed(target, prefix)} : ${event.replace(/[/+#]/g, '_')}`);
                    }
                }
            } else if (typeof transitions === 'string') {
                lines.push(`${indent}${name} --> ${prefixed(transitions, prefix)} : ${event.replace(/[/+#]/g, '_')}`);
            }
        }
    }
    if (node.after) {
        for (const [delay, transitions] of Object.entries(node.after)) {
            if (Array.isArray(transitions)) {
                for (const t of transitions) {
                    const target = getTarget(t);
                    if (target) { lines.push(`${indent}${name} --> ${prefixed(target, prefix)} : after ${delay}ms`); }
                }
            }
        }
    }
}

function getTarget(transition: { target?: string[] | string } | string): string | undefined {
    if (typeof transition === 'string') return transition;
    if (!transition.target) return undefined;
    if (Array.isArray(transition.target)) return transition.target[0];
    return transition.target;
}

// --- Workflow diagram ---

interface SflScheduler {
    type: string; name: string; layer: string;
    publishes: { msgType: string; topic: string }[];
    subscribes: { topic: string; alias: string }[];
    transactions: { name: string; refs: string[] }[];
}

function parseSflForWorkflow(sfl: string): SflScheduler[] {
    const schedulers: SflScheduler[] = [];
    const blocks = extractSflBlocks(sfl);

    for (const [name, info] of blocks) {
        const body = info.block;
        const layerMatch = body.match(/LAYER:\s*(L\d)/);
        const layer = layerMatch ? layerMatch[1] : 'L4';

        const publishes: { msgType: string; topic: string }[] = [];
        let pm: RegExpExecArray | null;
        const pubRe = /publish\s+(\w+)\s+to\s+"([^"]+)"/g;
        while ((pm = pubRe.exec(body)) !== null) { publishes.push({ msgType: pm[1], topic: pm[2] }); }

        const subscribes: { topic: string; alias: string }[] = [];
        const subRe = /subscribe\s+to\s+"([^"]+)"\s+as\s+(\w+)/g;
        let sm: RegExpExecArray | null;
        while ((sm = subRe.exec(body)) !== null) { subscribes.push({ topic: sm[1], alias: sm[2] }); }

        const transactions: { name: string; refs: string[] }[] = [];
        const txnRe = /transaction\s+(\w+)\s*\{([^}]*)\}/gs;
        let tm: RegExpExecArray | null;
        while ((tm = txnRe.exec(body)) !== null) {
            const refs: string[] = [];
            const parentMatch = tm[2].match(/parent:\s*(\w+)/);
            if (parentMatch) { refs.push(parentMatch[1]); }
            const cmdMatch = tm[2].match(/command:\s*\w+\(([^)]+)\)/);
            if (cmdMatch) {
                for (const arg of cmdMatch[1].split(',').map(s => s.trim())) {
                    if (arg.match(/^STN_/)) { refs.push(arg); }
                }
            }
            transactions.push({ name: tm[1], refs });
        }

        schedulers.push({ type: info.type, name, layer, publishes, subscribes, transactions });
    }
    return schedulers;
}

function sflToWorkflowDiagram(sfl: string): string {
    const schedulers = parseSflForWorkflow(sfl);
    if (schedulers.length === 0) { return ''; }

    const lines: string[] = ['flowchart TB'];
    const typeLabels: Record<string, string> = {
        'MASTER_SCHEDULER': 'Master', 'WAFER_SCHEDULER': 'Wafer',
        'ROBOT_SCHEDULER': 'Robot', 'STATION': 'Station'
    };
    const layerLabels: Record<string, string> = {
        'L1': 'L1 — Master Orchestration', 'L2': 'L2 — Wafer Scheduling',
        'L3': 'L3 — Robot Control', 'L4': 'L4 — Station Process'
    };

    const layers = new Map<string, SflScheduler[]>();
    for (const s of schedulers) {
        if (!layers.has(s.layer)) { layers.set(s.layer, []); }
        layers.get(s.layer)!.push(s);
    }

    for (const layer of [...layers.keys()].sort()) {
        lines.push(`    subgraph ${layer}["${layerLabels[layer] || layer}"]`);
        for (const s of layers.get(layer)!) {
            lines.push(`        ${s.name}["<b>${s.name}</b><br/><i>${typeLabels[s.type] || s.type}</i>"]`);
        }
        lines.push('    end');
    }

    const nameSet = new Set(schedulers.map(s => s.name));
    for (const s of schedulers) {
        for (const pub of s.publishes) {
            let matched = false;
            for (const other of schedulers) {
                if (other.name === s.name) { continue; }
                for (const sub of other.subscribes) {
                    if (topicMatches(pub.topic, sub.topic)) {
                        lines.push(`    ${s.name} -->|"${pub.msgType}<br/>${pub.topic}"| ${other.name}`);
                        matched = true;
                    }
                }
            }
            if (!matched) {
                const target = guessTargetFromTopic(s.name, schedulers);
                if (target) { lines.push(`    ${s.name} -.->|"${pub.msgType}<br/>${pub.topic}"| ${target}`); }
            }
        }
        for (const sub of s.subscribes) {
            const hasPublisher = schedulers.some(o => o.name !== s.name && o.publishes.some(p => topicMatches(p.topic, sub.topic)));
            if (!hasPublisher) {
                const source = guessSourceFromTopic(sub.topic, s.name, schedulers);
                if (source) { lines.push(`    ${source} -.->|"subscribe<br/>${sub.topic}"| ${s.name}`); }
            }
        }
        for (const txn of s.transactions) {
            for (const ref of txn.refs) {
                if (nameSet.has(ref)) { lines.push(`    ${s.name} ==>|"txn: ${txn.name}"| ${ref}`); }
            }
        }
    }

    lines.push('');
    lines.push('    style L1 fill:#1a365d,stroke:#2b6cb0,color:#bee3f8');
    lines.push('    style L2 fill:#22543d,stroke:#38a169,color:#c6f6d5');
    lines.push('    style L3 fill:#553c9a,stroke:#805ad5,color:#e9d8fd');
    lines.push('    style L4 fill:#744210,stroke:#d69e2e,color:#fefcbf');
    return lines.join('\n');
}

function topicMatches(pubTopic: string, subPattern: string): boolean {
    const pattern = subPattern.replace(/\+/g, '[^/]+').replace(/#/g, '.*');
    try { return new RegExp(`^${pattern}$`).test(pubTopic); }
    catch { return pubTopic === subPattern; }
}

function guessTargetFromTopic(sourceName: string, schedulers: SflScheduler[]): string | null {
    const src = schedulers.find(x => x.name === sourceName);
    if (!src) { return null; }
    for (const s of schedulers) {
        if (s.name !== sourceName && s.layer < src.layer) { return s.name; }
    }
    return null;
}

function guessSourceFromTopic(topic: string, targetName: string, schedulers: SflScheduler[]): string | null {
    const targetLayer = schedulers.find(x => x.name === targetName)!.layer;
    if (topic.split('/')[0] === 'msc') {
        return schedulers.find(s => s.type === 'MASTER_SCHEDULER')?.name || null;
    }
    for (const s of schedulers) {
        if (s.name !== targetName && s.layer < targetLayer) { return s.name; }
    }
    return null;
}

// --- HTML generation ---

function getWebviewHtml(layerTabs: LayerTab[], workflowCode: string, xstateJson: string, sflSource?: string): string {
    const tabsJson = JSON.stringify(layerTabs.map(t => ({
        id: t.id, label: t.label, schedulerType: t.schedulerType,
        schedulerName: t.schedulerName, layer: t.layer,
        sflBlock: t.sflBlock, diagramCode: t.diagram.code
    })));
    const escapedJson = xstateJson.replace(/`/g, '\\`').replace(/\$/g, '\\$');
    const hasWorkflow = workflowCode.length > 0;
    const workflowJson = hasWorkflow ? JSON.stringify(workflowCode) : 'null';
    const hasSfl = !!sflSource;

    const nonce = getNonce();
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'nonce-${nonce}' https://cdn.jsdelivr.net; style-src 'unsafe-inline'; img-src data:;">
    <title>SFL Visualizer</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: var(--vscode-font-family);
            background: var(--vscode-editor-background);
            color: var(--vscode-editor-foreground);
        }
        .tabs {
            display: flex;
            gap: 2px;
            border-bottom: 1px solid var(--vscode-panel-border);
            position: sticky;
            top: 0;
            background: var(--vscode-editor-background);
            z-index: 10;
            padding: 4px 8px 0;
            flex-wrap: wrap;
        }
        .tab {
            padding: 8px 14px;
            cursor: pointer;
            border: none;
            background: transparent;
            color: var(--vscode-foreground);
            border-bottom: 2px solid transparent;
            font-size: 12px;
            white-space: nowrap;
        }
        .tab.active { border-bottom-color: var(--vscode-focusBorder); color: var(--vscode-focusBorder); }
        .tab:hover { background: var(--vscode-list-hoverBackground); }
        .tab .layer-badge {
            display: inline-block;
            padding: 1px 5px;
            border-radius: 3px;
            font-size: 10px;
            margin-right: 4px;
            font-weight: 600;
        }
        .badge-L1 { background: #2b6cb0; color: #bee3f8; }
        .badge-L2 { background: #38a169; color: #c6f6d5; }
        .badge-L3 { background: #805ad5; color: #e9d8fd; }
        .badge-L4 { background: #d69e2e; color: #fefcbf; }
        .content { display: none; padding: 0; }
        .content.active { display: block; }

        /* Split layout: SFL text on top, diagram on bottom */
        .layer-view { display: flex; flex-direction: column; height: calc(100vh - 44px); }
        .layer-section { flex: 1; overflow: auto; border-bottom: 1px solid var(--vscode-panel-border); }
        .layer-section:last-child { border-bottom: none; }
        .section-label {
            position: sticky; top: 0; z-index: 5;
            padding: 6px 16px;
            font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;
            background: var(--vscode-sideBarSectionHeader-background, rgba(128,128,128,0.15));
            border-bottom: 1px solid var(--vscode-panel-border);
            color: var(--vscode-sideBarSectionHeader-foreground, var(--vscode-foreground));
        }
        pre.source-block {
            white-space: pre;
            font-family: var(--vscode-editor-fontFamily);
            font-size: var(--vscode-editor-fontSize);
            line-height: 1.5;
            padding: 12px 16px;
            overflow: auto;
        }
        .diagram-area {
            text-align: center;
            padding: 16px;
            overflow: auto;
        }
        .diagram-area svg { max-width: 100%; }

        /* Workflow + JSON tabs */
        .full-view { padding: 16px; height: calc(100vh - 44px); overflow: auto; }
        .full-view svg { max-width: 100%; }
        pre.json-block {
            white-space: pre;
            font-family: var(--vscode-editor-fontFamily);
            font-size: var(--vscode-editor-fontSize);
            line-height: 1.5;
            padding: 16px;
            background: var(--vscode-textCodeBlock-background);
            border-radius: 4px;
            overflow: auto;
        }
        .error { color: var(--vscode-errorForeground); padding: 16px; }
    </style>
</head>
<body>
    <div class="tabs" id="tab-bar"></div>
    <div id="tab-contents"></div>

    <script nonce="${nonce}" src="https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"></script>
    <script nonce="${nonce}">
        const layerTabs = ${tabsJson};
        const xstateJson = \`${escapedJson}\`;
        const workflowCode = ${workflowJson};
        const hasSfl = ${hasSfl};

        const isDark = document.body.getAttribute('data-vscode-theme-kind')?.includes('dark')
            ?? window.matchMedia('(prefers-color-scheme: dark)').matches;

        mermaid.initialize({
            startOnLoad: false,
            theme: isDark ? 'dark' : 'default',
            securityLevel: 'loose',
            stateDiagram: { useMaxWidth: true }
        });

        // Build tabs
        const tabBar = document.getElementById('tab-bar');
        const tabContents = document.getElementById('tab-contents');
        let firstTabId = null;

        // Workflow tab (if SFL)
        if (workflowCode) {
            firstTabId = 'workflow';
            addTab('workflow', '<span class="layer-badge" style="background:#555;color:#eee;">SYS</span>Workflow');
            const div = document.createElement('div');
            div.id = 'workflow-tab';
            div.className = 'content full-view';
            div.innerHTML = '<div id="workflow-diagram" style="text-align:center;"></div>';
            tabContents.appendChild(div);
        }

        // Layer tabs
        for (const lt of layerTabs) {
            if (!firstTabId) firstTabId = lt.id;
            const badge = '<span class="layer-badge badge-' + lt.layer + '">' + lt.layer + '</span>';
            addTab(lt.id, badge + lt.schedulerName);

            const div = document.createElement('div');
            div.id = lt.id + '-tab';
            div.className = 'content';
            div.innerHTML =
                '<div class="layer-view">' +
                (lt.sflBlock ?
                    '<div class="layer-section">' +
                    '  <div class="section-label">SFL Source — ' + lt.schedulerType + ' ' + lt.schedulerName + '</div>' +
                    '  <pre class="source-block">' + escapeHtml(lt.sflBlock) + '</pre>' +
                    '</div>' : '') +
                '<div class="layer-section">' +
                '  <div class="section-label">State Machine Diagram</div>' +
                '  <div class="diagram-area" id="diagram-' + lt.id + '"></div>' +
                '</div>' +
                '</div>';
            tabContents.appendChild(div);
        }

        // XState JSON tab — diagram + JSON text
        addTab('json', 'XState JSON');
        const jsonDiv = document.createElement('div');
        jsonDiv.id = 'json-tab';
        jsonDiv.className = 'content';
        let jsonPre = '';
        try {
            jsonPre = escapeHtml(JSON.stringify(JSON.parse(xstateJson), null, 2));
        } catch {
            jsonPre = escapeHtml(xstateJson);
        }
        jsonDiv.innerHTML =
            '<div class="layer-view">' +
            '  <div class="layer-section">' +
            '    <div class="section-label">State Machine Diagram (All Regions)</div>' +
            '    <div class="diagram-area" id="diagram-json-all"></div>' +
            '  </div>' +
            '  <div class="layer-section">' +
            '    <div class="section-label">XState JSON</div>' +
            '    <pre class="json-block">' + jsonPre + '</pre>' +
            '  </div>' +
            '</div>';
        tabContents.appendChild(jsonDiv);

        // Activate first tab
        if (firstTabId) showTab(firstTabId);

        // Render all diagrams
        async function renderAll() {
            // Workflow
            if (workflowCode) {
                const el = document.getElementById('workflow-diagram');
                try {
                    const { svg } = await mermaid.render('mermaid-wf', workflowCode);
                    el.innerHTML = svg;
                } catch (e) {
                    el.innerHTML = '<div class="error">Workflow render error: ' + e.message + '</div><pre>' + escapeHtml(workflowCode) + '</pre>';
                }
            }
            // Combined diagram for JSON tab (all regions stacked)
            const jsonAllEl = document.getElementById('diagram-json-all');
            if (jsonAllEl) {
                let allHtml = '';
                for (let i = 0; i < layerTabs.length; i++) {
                    const lt = layerTabs[i];
                    try {
                        const { svg } = await mermaid.render('mermaid-all-' + i, lt.diagramCode);
                        allHtml += '<div style="margin-bottom:24px;"><h3 style="text-align:center;margin-bottom:8px;font-size:14px;opacity:0.7;">' + lt.schedulerName + '</h3>' + svg + '</div>';
                    } catch (e) {
                        allHtml += '<div class="error">' + lt.schedulerName + ': ' + e.message + '</div>';
                    }
                }
                jsonAllEl.innerHTML = allHtml;
            }
            // State machine diagrams (per-layer tabs)
            for (let i = 0; i < layerTabs.length; i++) {
                const lt = layerTabs[i];
                const el = document.getElementById('diagram-' + lt.id);
                if (!el) continue;
                try {
                    const { svg } = await mermaid.render('mermaid-sm-' + i, lt.diagramCode);
                    el.innerHTML = svg;
                } catch (e) {
                    el.innerHTML = '<div class="error">Diagram error: ' + e.message + '</div><pre>' + escapeHtml(lt.diagramCode) + '</pre>';
                }
            }
        }
        renderAll();

        function addTab(id, html) {
            const btn = document.createElement('button');
            btn.className = 'tab';
            btn.innerHTML = html;
            btn.onclick = () => showTab(id);
            btn.dataset.tabId = id;
            tabBar.appendChild(btn);
        }

        function showTab(id) {
            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
            document.querySelectorAll('.content').forEach(c => c.classList.remove('active'));
            const btn = document.querySelector('[data-tab-id="' + id + '"]');
            if (btn) btn.classList.add('active');
            const content = document.getElementById(id + '-tab');
            if (content) content.classList.add('active');
        }

        function escapeHtml(s) {
            return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
        }

        window.addEventListener('message', event => {
            if (event.data.type === 'update') location.reload();
        });
    </script>
</body>
</html>`;
}

function getNonce(): string {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}
