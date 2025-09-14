import os
import re

test_dir = r"C:\Develop25\XStateNet\Test"

def fix_remaining_patterns(content):
    """Fix all remaining .Should() patterns including complex ones"""

    # Fix simple object.Should().Be(value) patterns
    content = re.sub(r'_contextValues\["counter"\]\.Should\(\)\.Be\((\d+)\)',
                     r'Assert.Equal(\1, _contextValues["counter"])', content)

    content = re.sub(r'_stateMachine\.ContextMap!\["value"\]\.Should\(\)\.Be\("([^"]+)"\)',
                     r'Assert.Equal("\1", _stateMachine.ContextMap!["value"])', content)

    content = re.sub(r'_stateMachine\.ContextMap\["value"\]\.Should\(\)\.Be\("([^"]+)"\)',
                     r'Assert.Equal("\1", _stateMachine.ContextMap["value"])', content)

    # Fix complex JValue conversion patterns
    # (counterVal is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterVal ?? 0)).Should().Be(2)
    content = re.sub(r'\(([a-zA-Z_][a-zA-Z0-9_]*) is Newtonsoft\.Json\.Linq\.JValue ([a-zA-Z_][a-zA-Z0-9_]*) \? \2\.ToObject<int>\(\) : \(int\)\(\1 \?\? 0\)\)\.Should\(\)\.Be\((\d+)\)',
                     r'Assert.Equal(\3, \1 is Newtonsoft.Json.Linq.JValue \2 ? \2.ToObject<int>() : (int)(\1 ?? 0))', content)

    # Fix UnitTest_InvokeServices pattern
    content = re.sub(r'_stateMachine\.ContextMap!\["data"\]\.Should\(\)\.Be\("([^"]+)"\)',
                     r'Assert.Equal("\1", _stateMachine.ContextMap!["data"])', content)

    return content

# Process specific files with errors
files_to_fix = [
    "UnitTest_InternalTransitions.cs",
    "UnitTest_InvokeServices.cs"
]

fixed_count = 0
for file_name in files_to_fix:
    file_path = os.path.join(test_dir, file_name)

    if not os.path.exists(file_path):
        continue

    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()

    original = content
    content = fix_remaining_patterns(content)

    if content != original:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Fixed: {file_path}")
        fixed_count += 1

print(f"\nTotal files fixed: {fixed_count}")