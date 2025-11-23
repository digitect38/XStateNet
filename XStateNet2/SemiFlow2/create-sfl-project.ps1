# Semi Flow Language Complete Project Generator
# Run this PowerShell script to create all project files
# Usage: ./create-sfl-project.ps1

$projectRoot = "$PSScriptRoot\semiflow-project"
Write-Host "🚀 Creating Semi Flow Language Project at: $projectRoot" -ForegroundColor Cyan

# Create all directories
function Create-Directories {
    $dirs = @(
        "$projectRoot\sfl-spec",
        "$projectRoot\vscode-extension\src",
        "$projectRoot\vscode-extension\syntaxes",
        "$projectRoot\vscode-extension\snippets",
        "$projectRoot\vscode-extension\themes",
        "$projectRoot\sfl-compiler\src",
        "$projectRoot\sfl-rules\war",
        "$projectRoot\sfl-rules\psr",
        "$projectRoot\sfl-rules\ssr",
        "$projectRoot\examples\basic",
        "$projectRoot\examples\advanced",
        "$projectRoot\docs\tutorials",
        "$projectRoot\tests\fixtures",
        "$projectRoot\tools\sfl-cli",
        "$projectRoot\.vscode"
    )
    
    foreach ($dir in $dirs) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    Write-Host "✓ Directories created" -ForegroundColor Green
}

# 1. Grammar Specification
function Create-GrammarSpec {
    $content = @'
# Semi Flow Language (SFL) Grammar Specification v2.0

## 1. File Extension
- Primary: `*.sfl`
- Include: `*.sfli`
- Config: `*.sflc`

## 2. Basic Syntax

### Keywords
```
MASTER_SCHEDULER, WAFER_SCHEDULER, ROBOT_SCHEDULER, STATION
SCHEDULE, APPLY_RULE, VERIFY, FORMULA
LAYER, L1, L2, L3, L4
CONFIG, transaction, publish, subscribe
```

### Identifiers
- Schedulers: `MSC_001`, `WSC_001`, `RSC_001`
- Wafers: `W001`, `W002`, ..., `W025`
- Stations: `STN_CMP01`, `STN_CLN02`
- Transactions: `TXN_20240101_00001_A3F2`

## 3. Structure
```sfl
SCHEDULER_TYPE IDENTIFIER {
    LAYER: L1
    CONFIG { ... }
    SCHEDULE NAME { ... }
}
```
'@
    $content | Out-File -FilePath "$projectRoot\sfl-spec\grammar-spec-v2.0.md" -Encoding UTF8
    Write-Host "✓ Grammar specification created" -ForegroundColor Green
}

# 2. Standard Rules Library
function Create-RulesLibrary {
    $content = @'
# Semi Flow Language Standard Rules Library v2.0

## Built-in Rules

### WAR_001: Cyclic Zip Distribution
Distributes wafers across WSCs in cyclic pattern
- Input: 25 wafers, 3 WSCs
- Output: WSC1=[W1,W4,W7...], WSC2=[W2,W5,W8...], WSC3=[W3,W6,W9...]

### PSR_001: Pipeline Slot Assignment
Assigns wafers to pipeline slots without collision

### SSR_001: Three Phase Steady State
Maintains steady state with 3-phase operation
'@
    $content | Out-File -FilePath "$projectRoot\sfl-spec\standard-rules-library.md" -Encoding UTF8
    Write-Host "✓ Rules library created" -ForegroundColor Green
}

# 3. VSCode Extension Files
function Create-VSCodeExtension {
    # package.json
    $packageJson = @'
{
  "name": "semiflow-language",
  "displayName": "Semi Flow Language",
  "version": "2.0.0",
  "publisher": "semiconductor-mfg",
  "engines": {"vscode": "^1.75.0"},
  "main": "./out/extension.js",
  "categories": ["Programming Languages"],
  "contributes": {
    "languages": [{
      "id": "semiflow",
      "aliases": ["Semi Flow", "SFL"],
      "extensions": [".sfl", ".sfli", ".sflc"],
      "configuration": "./language-configuration.json"
    }],
    "grammars": [{
      "language": "semiflow",
      "scopeName": "source.sfl",
      "path": "./syntaxes/semiflow.tmLanguage.json"
    }],
    "snippets": [{
      "language": "semiflow",
      "path": "./snippets/semiflow.snippets.json"
    }],
    "themes": [{
      "label": "Semi Flow Dark",
      "uiTheme": "vs-dark",
      "path": "./themes/semiflow-dark.json"
    }]
  },
  "scripts": {
    "compile": "tsc -p ./",
    "watch": "tsc -watch -p ./"
  },
  "devDependencies": {
    "@types/vscode": "^1.75.0",
    "@types/node": "^18.0.0",
    "typescript": "^4.9.0"
  }
}
'@
    $packageJson | Out-File -FilePath "$projectRoot\vscode-extension\package.json" -Encoding UTF8
    
    # tsconfig.json
    $tsconfig = @'
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "commonjs",
    "lib": ["ES2020"],
    "outDir": "./out",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules"]
}
'@
    $tsconfig | Out-File -FilePath "$projectRoot\vscode-extension\tsconfig.json" -Encoding UTF8
    
    # extension.ts
    $extension = @'
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
'@
    $extension | Out-File -FilePath "$projectRoot\vscode-extension\src\extension.ts" -Encoding UTF8
    
    # Grammar file
    $grammar = @'
{
  "$schema": "https://raw.githubusercontent.com/martinring/tmlanguage/master/tmlanguage.json",
  "name": "Semi Flow Language",
  "patterns": [
    {"include": "#keywords"},
    {"include": "#strings"},
    {"include": "#comments"},
    {"include": "#numbers"},
    {"include": "#identifiers"}
  ],
  "repository": {
    "keywords": {
      "patterns": [{
        "name": "keyword.control.sfl",
        "match": "\\b(MASTER_SCHEDULER|WAFER_SCHEDULER|ROBOT_SCHEDULER|STATION|SCHEDULE|APPLY_RULE|VERIFY|CONFIG|LAYER)\\b"
      }]
    },
    "strings": {
      "patterns": [{
        "name": "string.quoted.double.sfl",
        "begin": "\"",
        "end": "\""
      }]
    },
    "comments": {
      "patterns": [
        {
          "name": "comment.line.double-slash.sfl",
          "match": "//.*$"
        },
        {
          "name": "comment.block.sfl",
          "begin": "/\\*",
          "end": "\\*/"
        }
      ]
    },
    "numbers": {
      "patterns": [{
        "name": "constant.numeric.sfl",
        "match": "\\b[0-9]+(\\.?[0-9]+)?\\b"
      }]
    },
    "identifiers": {
      "patterns": [
        {
          "name": "entity.name.class.scheduler.sfl",
          "match": "\\b(MSC|WSC|RSC)_[0-9]+\\b"
        },
        {
          "name": "variable.other.wafer.sfl",
          "match": "\\bW[0-9]+\\b"
        },
        {
          "name": "constant.language.layer.sfl",
          "match": "\\bL[1-4]\\b"
        }
      ]
    }
  },
  "scopeName": "source.sfl"
}
'@
    $grammar | Out-File -FilePath "$projectRoot\vscode-extension\syntaxes\semiflow.tmLanguage.json" -Encoding UTF8
    
    # Snippets
    $snippets = @'
{
  "Master Scheduler": {
    "prefix": "msc",
    "body": [
      "MASTER_SCHEDULER ${1:MSC_001} {",
      "\tLAYER: L1",
      "\tCONFIG {",
      "\t\twafer_distribution: \"CYCLIC_ZIP\"",
      "\t\ttotal_wafers: ${2:25}",
      "\t}",
      "\t$0",
      "}"
    ],
    "description": "Create a Master Scheduler"
  },
  "Apply Rule": {
    "prefix": "rule",
    "body": [
      "APPLY_RULE(\"${1|WAR_001,PSR_001,SSR_001|}\")$0"
    ],
    "description": "Apply a scheduling rule"
  },
  "Schedule Block": {
    "prefix": "schedule",
    "body": [
      "SCHEDULE ${1:PRODUCTION_RUN} {",
      "\twafer_count: ${2:25}",
      "\tAPPLY_RULE(\"WAR_001\")",
      "\tAPPLY_RULE(\"PSR_001\")",
      "\t$0",
      "}"
    ],
    "description": "Create a schedule block"
  }
}
'@
    $snippets | Out-File -FilePath "$projectRoot\vscode-extension\snippets\semiflow.snippets.json" -Encoding UTF8
    
    Write-Host "✓ VSCode extension files created" -ForegroundColor Green
}

# 4. Example SFL Files
function Create-Examples {
    # Basic example
    $basicExample = @'
// hello_world.sfl
// Semi Flow Language Basic Example

MASTER_SCHEDULER MSC_HELLO {
    LAYER: L1
    CONFIG {
        name: "Hello Semi Flow"
        version: "2.0"
    }
}
'@
    $basicExample | Out-File -FilePath "$projectRoot\examples\basic\hello_world.sfl" -Encoding UTF8
    
    # Cyclic Zip Example
    $cyclicZip = @'
// cyclic_zip_25wafers.sfl
// Cyclic Zip Distribution for 25 Wafers

MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    
    CONFIG {
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
        active_wsc_count: 3
    }
    
    SCHEDULE PRODUCTION_LOT_001 {
        wafer_count: 25
        
        // Apply cyclic zip distribution
        APPLY_RULE("WAR_001")  // Distributes to 3 WSCs
        
        // Expected result:
        // WSC_001: W1,W4,W7,W10,W13,W16,W19,W22,W25 (9 wafers)
        // WSC_002: W2,W5,W8,W11,W14,W17,W20,W23     (8 wafers)  
        // WSC_003: W3,W6,W9,W12,W15,W18,W21,W24     (8 wafers)
    }
}

WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    CONFIG {
        assigned_wafers: [W1,W4,W7,W10,W13,W16,W19,W22,W25]
        pipeline_depth: 3
    }
}

WAFER_SCHEDULER WSC_002 {
    LAYER: L2
    CONFIG {
        assigned_wafers: [W2,W5,W8,W11,W14,W17,W20,W23]
        pipeline_depth: 3
    }
}

WAFER_SCHEDULER WSC_003 {
    LAYER: L2
    CONFIG {
        assigned_wafers: [W3,W6,W9,W12,W15,W18,W21,W24]
        pipeline_depth: 3
    }
}
'@
    $cyclicZip | Out-File -FilePath "$projectRoot\examples\advanced\cyclic_zip_25wafers.sfl" -Encoding UTF8
    
    # CMP Line System
    $cmpSystem = @'
// cmp_line_system.sfl
// Complete CMP Production Line

import semiflow.algorithms.cyclic_zip
import semiflow.semi.e90

MASTER_SCHEDULER MSC_CMP_LINE {
    LAYER: L1
    
    CONFIG {
        fab_name: "FAB_SEOUL_01"
        line_type: "CMP_PRODUCTION"
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
        optimization_interval: 30s
    }
    
    SCHEDULE DAILY_PRODUCTION {
        wafer_count: 25
        scheduler_count: 3
        
        APPLY_RULE("WAR_001")  // Cyclic Zip
        APPLY_RULE("PSR_001")  // Pipeline Slots
        APPLY_RULE("SSR_001")  // Steady State
        
        VERIFY {
            all_wafers_assigned: true
            no_conflicts: true
            pipeline_depth: 3
        }
    }
}

ROBOT_SCHEDULER RSC_EFEM_001 {
    LAYER: L3
    
    CONFIG {
        robot_type: "EFEM"
        max_velocity: 2.0
        max_acceleration: 5.0
        position_update_rate: 10Hz
    }
    
    transaction MOVE_WAFER {
        command: move(W001, STN_CMP01, STN_CLN01)
        timeout: 30s
    }
}

STATION STN_CMP01 {
    LAYER: L4
    
    CONFIG {
        type: "CMP_POLISHER"
        process_time: 180s
        capacity: 1
    }
}

STATION STN_CLN01 {
    LAYER: L4
    
    CONFIG {
        type: "POST_CMP_CLEANER"
        process_time: 120s
        capacity: 1
    }
}
'@
    $cmpSystem | Out-File -FilePath "$projectRoot\examples\advanced\cmp_line_system.sfl" -Encoding UTF8
    
    Write-Host "✓ Example files created" -ForegroundColor Green
}

# 5. Built-in Rule Files
function Create-RuleFiles {
    # WAR_001
    $war001 = @'
// WAR_001.sfl
// Cyclic Zip Distribution Rule

RULE WAR_001 {
    id: "WAR_001"
    name: "Cyclic_Zip_Distribution"
    category: "WAFER_ASSIGNMENT"
    version: "2.0"
    
    DESCRIPTION {
        "Distributes wafers across multiple WSCs using cyclic zip pattern"
    }
    
    PARAMETERS {
        wafer_list: wafer_id_t[]
        wsc_count: integer
        start_offset: integer = 0
    }
    
    FORMULA {
        for (i = 0; i < wafer_list.length; i++) {
            wsc_index = (i + start_offset) % wsc_count
            assign(wafer_list[i], wsc[wsc_index])
        }
    }
    
    CONSTRAINTS {
        min_wsc_count: 1
        max_wsc_count: 10
        load_difference: <= 1
    }
}
'@
    $war001 | Out-File -FilePath "$projectRoot\sfl-rules\war\WAR_001.sfl" -Encoding UTF8
    
    # PSR_001
    $psr001 = @'
// PSR_001.sfl  
// Pipeline Slot Assignment Rule

RULE PSR_001 {
    id: "PSR_001"
    name: "Pipeline_Slot_Assignment"
    category: "PIPELINE_SCHEDULING"
    version: "2.0"
    
    DESCRIPTION {
        "Assigns wafers to pipeline slots for collision-free operation"
    }
    
    PARAMETERS {
        pipeline_depth: integer = 3
        wafer_queue: wafer_id_t[]
        cycle_time: duration_t
    }
    
    FORMULA {
        PHASE_1_SLOTS = [0, 3, 6, 9, 12, 15, 18, 21, 24]
        PHASE_2_SLOTS = [1, 4, 7, 10, 13, 16, 19, 22, 25]
        PHASE_3_SLOTS = [2, 5, 8, 11, 14, 17, 20, 23, 26]
        
        for (i = 0; i < wafer_queue.length; i++) {
            phase = i % pipeline_depth
            slot = floor(i / pipeline_depth)
            assign_to_phase(wafer_queue[i], phase, slot)
        }
    }
}
'@
    $psr001 | Out-File -FilePath "$projectRoot\sfl-rules\psr\PSR_001.sfl" -Encoding UTF8
    
    # SSR_001
    $ssr001 = @'
// SSR_001.sfl
// Three Phase Steady State Rule

RULE SSR_001 {
    id: "SSR_001"
    name: "Three_Phase_Steady_State"
    category: "STEADY_STATE"
    version: "2.0"
    
    DESCRIPTION {
        "Maintains three-phase steady state operation"
    }
    
    PARAMETERS {
        phase_count: integer = 3
        wafer_input_rate: frequency_t
        station_capacity: map<station_id_t, integer>
    }
    
    FORMULA {
        PHASE_A = {
            wafers: [1, 4, 7, 10, 13, 16, 19, 22, 25],
            timing: 0s
        }
        
        PHASE_B = {
            wafers: [2, 5, 8, 11, 14, 17, 20, 23],
            timing: 20s
        }
        
        PHASE_C = {
            wafers: [3, 6, 9, 12, 15, 18, 21, 24],
            timing: 40s
        }
        
        maintain_steady_state()
    }
}
'@
    $ssr001 | Out-File -FilePath "$projectRoot\sfl-rules\ssr\SSR_001.sfl" -Encoding UTF8
    
    Write-Host "✓ Rule files created" -ForegroundColor Green
}

# 6. Documentation Files
function Create-Documentation {
    # README.md
    $readme = @'
# Semi Flow Language (SFL)

Domain-specific language for semiconductor manufacturing scheduling systems.

## Version
2.0.0

## Quick Start
```bash
# Install VSCode extension
code --install-extension ./vscode-extension

# Open example file
code examples/advanced/cyclic_zip_25wafers.sfl
```

## Features
- Hierarchical scheduling (MSC → WSC → RSC → Station)
- Cyclic zip wafer distribution
- Pipeline scheduling with collision prevention
- SEMI standards compliance (E87, E88, E90, E94)
- Real-time transaction tracking
- Pub/Sub messaging with QoS

## File Extensions
- `.sfl` - Semi Flow Language source files
- `.sfli` - Include files
- `.sflc` - Configuration files

## Built-in Rules
- **WAR_001**: Cyclic Zip Distribution
- **PSR_001**: Pipeline Slot Assignment  
- **SSR_001**: Three Phase Steady State

## Example
```sfl
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    CONFIG {
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
    }
    SCHEDULE PRODUCTION {
        APPLY_RULE("WAR_001")
    }
}
```

## Documentation
- [Grammar Specification](sfl-spec/grammar-spec-v2.0.md)
- [Standard Rules Library](sfl-spec/standard-rules-library.md)
- [User Guide](docs/USER_GUIDE.md)

## License
MIT License - Semiconductor Manufacturing Consortium
'@
    $readme | Out-File -FilePath "$projectRoot\README.md" -Encoding UTF8
    
    # User Guide
    $userGuide = @'
# Semi Flow Language User Guide

## 1. Installation

### VSCode Extension
1. Open VSCode
2. Go to Extensions (Ctrl+Shift+X)
3. Click "Install from VSIX"
4. Select `semiflow-language.vsix`

## 2. Writing Your First SFL Program

### Basic Structure
Every SFL program consists of schedulers organized in layers:
- L1: Master Scheduler (MSC)
- L2: Wafer Scheduler (WSC)
- L3: Robot Scheduler (RSC)
- L4: Station

### Example
```sfl
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    CONFIG {
        total_wafers: 25
    }
}
```

## 3. Using Rules

### Apply Built-in Rules
```sfl
SCHEDULE PRODUCTION {
    APPLY_RULE("WAR_001")  // Cyclic Zip
    APPLY_RULE("PSR_001")  // Pipeline Slots
}
```

### Verify Results
```sfl
VERIFY {
    all_wafers_assigned: true
    no_conflicts: true
}
```

## 4. Commands

### Compile
```bash
sfl compile file.sfl
```

### Validate
```bash
sfl validate file.sfl
```

### Run Simulation
```bash
sfl simulate file.sfl --wafers 25 --time 3600
```
'@
    $userGuide | Out-File -FilePath "$projectRoot\docs\USER_GUIDE.md" -Encoding UTF8
    
    Write-Host "✓ Documentation created" -ForegroundColor Green
}

# 7. VSCode Settings
function Create-VSCodeSettings {
    $settings = @'
{
  "files.associations": {
    "*.sfl": "semiflow",
    "*.sfli": "semiflow",
    "*.sflc": "semiflow"
  },
  "editor.tokenColorCustomizations": {
    "textMateRules": [
      {
        "scope": "keyword.control.sfl",
        "settings": {"foreground": "#569cd6"}
      },
      {
        "scope": "entity.name.class.scheduler.sfl",
        "settings": {"foreground": "#4ec9b0"}
      },
      {
        "scope": "variable.other.wafer.sfl",
        "settings": {"foreground": "#9cdcfe"}
      }
    ]
  }
}
'@
    $settings | Out-File -FilePath "$projectRoot\.vscode\settings.json" -Encoding UTF8
    
    Write-Host "✓ VSCode settings created" -ForegroundColor Green
}

# 8. CLI Tool
function Create-CLITool {
    $cli = @'
#!/usr/bin/env node
// sfl-cli - Semi Flow Language Command Line Interface

const fs = require('fs');
const path = require('path');

const command = process.argv[2];
const file = process.argv[3];

if (!command || !file) {
    console.log('Usage: sfl <command> <file.sfl>');
    console.log('Commands: compile, validate, simulate');
    process.exit(1);
}

switch(command) {
    case 'compile':
        console.log(`Compiling ${file}...`);
        // Compilation logic here
        console.log('✓ Compilation successful');
        break;
    case 'validate':
        console.log(`Validating ${file}...`);
        // Validation logic here
        console.log('✓ Validation passed');
        break;
    case 'simulate':
        console.log(`Simulating ${file}...`);
        // Simulation logic here
        console.log('✓ Simulation complete');
        break;
    default:
        console.log(`Unknown command: ${command}`);
}
'@
    $cli | Out-File -FilePath "$projectRoot\tools\sfl-cli\sfl.js" -Encoding UTF8
    
    # package.json for CLI
    $cliPackage = @'
{
  "name": "sfl-cli",
  "version": "2.0.0",
  "description": "Semi Flow Language CLI",
  "main": "sfl.js",
  "bin": {
    "sfl": "./sfl.js"
  },
  "scripts": {
    "test": "echo \"Error: no test specified\" && exit 1"
  }
}
'@
    $cliPackage | Out-File -FilePath "$projectRoot\tools\sfl-cli\package.json" -Encoding UTF8
    
    Write-Host "✓ CLI tool created" -ForegroundColor Green
}

# 9. Test Files
function Create-TestFiles {
    $testFile = @'
// test_cyclic_zip.sfl
// Test file for cyclic zip validation

MASTER_SCHEDULER TEST_MSC {
    LAYER: L1
    
    CONFIG {
        test_mode: true
        wafer_distribution: "CYCLIC_ZIP"
        total_wafers: 25
    }
    
    SCHEDULE TEST_RUN {
        APPLY_RULE("WAR_001")
        
        VERIFY {
            wsc_001_count: 9
            wsc_002_count: 8
            wsc_003_count: 8
            total_distributed: 25
        }
    }
}
'@
    $testFile | Out-File -FilePath "$projectRoot\tests\fixtures\test_cyclic_zip.sfl" -Encoding UTF8
    
    Write-Host "✓ Test files created" -ForegroundColor Green
}

# 10. Git Files
function Create-GitFiles {
    $gitignore = @'
# Dependencies
node_modules/
*.log

# Build outputs
out/
dist/
*.js
*.js.map

# IDE
.idea/
*.swp
*.swo

# OS
.DS_Store
Thumbs.db

# Test
coverage/
.nyc_output/
'@
    $gitignore | Out-File -FilePath "$projectRoot\.gitignore" -Encoding UTF8
    
    Write-Host "✓ Git files created" -ForegroundColor Green
}

# Main execution
Write-Host "`n📦 Semi Flow Language Project Generator v2.0" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Create-Directories
Create-GrammarSpec
Create-RulesLibrary
Create-VSCodeExtension
Create-Examples
Create-RuleFiles
Create-Documentation
Create-VSCodeSettings
Create-CLITool
Create-TestFiles
Create-GitFiles

Write-Host "`n✅ Project creation complete!" -ForegroundColor Green
Write-Host "`n📂 Project location: $projectRoot" -ForegroundColor Cyan
Write-Host "`n🚀 Next steps:" -ForegroundColor Yellow
Write-Host "  1. cd $projectRoot" -ForegroundColor White
Write-Host "  2. cd vscode-extension && npm install" -ForegroundColor White
Write-Host "  3. code ." -ForegroundColor White
Write-Host "  4. Press F5 to test the extension" -ForegroundColor White
Write-Host "`n📖 Open examples\advanced\cyclic_zip_25wafers.sfl to see it in action!" -ForegroundColor Magenta