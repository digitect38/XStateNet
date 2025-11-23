import { Diagnostic, DiagnosticSeverity, Range } from 'vscode-languageserver/node';

export class SFLValidator {
    private schedulingRules = new Map([
        ['PSR_001', 'Pipeline Slot Assignment'],
        ['PSR_002', 'Processing Time Pattern'],
        ['PSR_003', 'WTR Assignment Matrix'],
        ['WAR_001', 'Cyclic Zip Distribution'],
        ['WAR_002', 'WSC Pipeline Slot Control'],
        ['SSR_001', 'Three Phase Steady State'],
        ['SSR_002', 'Pipeline State Detection'],
        ['VR_001', 'No Double Booking'],
        ['VR_002', 'WTR Capacity'],
        ['VR_003', 'Sequence Integrity']
    ]);

    public validateDocument(text: string): Diagnostic[] {
        const diagnostics: Diagnostic[] = [];
        const lines = text.split('\n');
        
        this.checkBracketBalance(text, diagnostics);
        this.validateSchedulingRules(lines, diagnostics);
        this.checkPipelineConstraints(lines, diagnostics);
        this.validateWSCAssignments(lines, diagnostics);
        
        return diagnostics;
    }

    private checkBracketBalance(text: string, diagnostics: Diagnostic[]): void {
        const stack: Array<{char: string, pos: number}> = [];
        const pairs: {[key: string]: string} = {'{': '}', '[': ']', '(': ')'};
        
        for (let i = 0; i < text.length; i++) {
            const char = text[i];
            
            if (char in pairs) {
                stack.push({char, pos: i});
            } else if (Object.values(pairs).includes(char)) {
                if (stack.length === 0 || pairs[stack[stack.length - 1].char] !== char) {
                    diagnostics.push(this.createDiagnostic(
                        i, i + 1,
                        `Unmatched closing bracket: ${char}`,
                        DiagnosticSeverity.Error
                    ));
                } else {
                    stack.pop();
                }
            }
        }
        
        for (const item of stack) {
            diagnostics.push(this.createDiagnostic(
                item.pos, item.pos + 1,
                `Unclosed bracket: ${item.char}`,
                DiagnosticSeverity.Error
            ));
        }
    }

    private validateSchedulingRules(lines: string[], diagnostics: Diagnostic[]): void {
        lines.forEach((line, index) => {
            const ruleMatch = line.match(/APPLY_RULE\s*\(\s*"([^"]+)"\s*\)/);
            if (ruleMatch) {
                const ruleName = ruleMatch[1];
                
                if (!ruleName.includes('*') && !this.schedulingRules.has(ruleName)) {
                    diagnostics.push({
                        severity: DiagnosticSeverity.Error,
                        range: {
                            start: { line: index, character: 0 },
                            end: { line: index, character: line.length }
                        },
                        message: `Unknown rule: ${ruleName}. Available: ${Array.from(this.schedulingRules.keys()).join(', ')}`
                    });
                }
            }
        });
    }

    private checkPipelineConstraints(lines: string[], diagnostics: Diagnostic[]): void {
        let pipelineDepth = 0;
        let wscCount = 0;
        let depthLine = -1;
        
        lines.forEach((line, index) => {
            if (line.includes('pipeline_depth:')) {
                depthLine = index;
                const match = line.match(/\d+/);
                if (match) pipelineDepth = parseInt(match[0]);
            }
            if (line.includes('WAFER_SCHEDULER')) {
                wscCount++;
            }
        });
        
        if (pipelineDepth > 0 && wscCount > 0 && pipelineDepth !== wscCount) {
            diagnostics.push({
                severity: DiagnosticSeverity.Error,
                range: {
                    start: { line: Math.max(0, depthLine), character: 0 },
                    end: { line: Math.max(0, depthLine), character: 100 }
                },
                message: `Pipeline depth (${pipelineDepth}) must equal WSC count (${wscCount})`
            });
        }
    }

    private validateWSCAssignments(lines: string[], diagnostics: Diagnostic[]): void {
        lines.forEach((line, index) => {
            // Check for large arrays
            if (line.includes('wafers:') && line.includes('[')) {
                const match = line.match(/\[([^\]]+)\]/);
                if (match) {
                    const elements = match[1].split(',');
                    if (elements.length > 10) {
                        diagnostics.push({
                            severity: DiagnosticSeverity.Warning,
                            range: {
                                start: { line: index, character: 0 },
                                end: { line: index, character: line.length }
                            },
                            message: `Consider using FORMULA for ${elements.length} wafers`,
                            code: 'SFL003'
                        });
                    }
                }
            }
        });
    }

    private createDiagnostic(start: number, end: number, message: string, severity: DiagnosticSeverity): Diagnostic {
        return {
            severity,
            range: {
                start: { line: 0, character: start },
                end: { line: 0, character: end }
            },
            message
        };
    }
}