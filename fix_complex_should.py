import os
import re

test_dir = r"C:\Develop25\XStateNet\Test"

def fix_complex_should_patterns(content):
    """Fix complex .Should() patterns with Contains and other methods"""

    # someString.Contains(substring).Should().BeTrue() -> Assert.Contains(substring, someString)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Contains\(([^)]+)\)\.Should\(\)\.BeTrue\(\)',
                     r'Assert.Contains(\2, \1)', content)

    # someString.Contains(substring).Should().BeFalse() -> Assert.DoesNotContain(substring, someString)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Contains\(([^)]+)\)\.Should\(\)\.BeFalse\(\)',
                     r'Assert.DoesNotContain(\2, \1)', content)

    # someBool.Should().BeTrue() -> Assert.True(someBool)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()<>=!\s]*?)\.Should\(\)\.BeTrue\(\)',
                     r'Assert.True(\1)', content)

    # someBool.Should().BeFalse() -> Assert.False(someBool)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()<>=!\s]*?)\.Should\(\)\.BeFalse\(\)',
                     r'Assert.False(\1)', content)

    # someVar.Should().NotBeNull() -> Assert.NotNull(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.NotBeNull\(\)',
                     r'Assert.NotNull(\1)', content)

    # someVar.Should().BeNull() -> Assert.Null(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeNull\(\)',
                     r'Assert.Null(\1)', content)

    # someVar.Should().Be(value) -> Assert.Equal(value, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.Be\(([^)]+)\)',
                     r'Assert.Equal(\2, \1)', content)

    # someVar.Should().NotBe(value) -> Assert.NotEqual(value, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.NotBe\(([^)]+)\)',
                     r'Assert.NotEqual(\2, \1)', content)

    # someVar.Should().Contain(item) -> Assert.Contains(item, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.Contain\(([^)]+)\)',
                     r'Assert.Contains(\2, \1)', content)

    # someVar.Should().HaveCount(n) -> Assert.Equal(n, someVar.Count)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.HaveCount\(([^)]+)\)',
                     r'Assert.Equal(\2, \1.Count)', content)

    # someVar.Should().StartWith(str) -> Assert.StartsWith(str, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.StartWith\(([^)]+)\)',
                     r'Assert.StartsWith(\2, \1)', content)

    # someVar.Should().EndWith(str) -> Assert.EndsWith(str, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.EndWith\(([^)]+)\)',
                     r'Assert.EndsWith(\2, \1)', content)

    # Fix specific patterns like: resultingData != null ? resultingData : "".Should().Be
    content = re.sub(r'\(([^)]+)\s*!=\s*null\s*\?\s*([^:]+)\s*:\s*([^)]+)\)\.Should\(\)\.Be\(([^)]+)\)',
                     r'Assert.Equal(\4, \1 != null ? \2 : \3)', content)

    return content

# Process all .cs files
fixed_count = 0
for root, dirs, files in os.walk(test_dir):
    for file in files:
        if file.endswith('.cs'):
            file_path = os.path.join(root, file)

            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()

            # Skip if no .Should() patterns
            if '.Should()' not in content:
                continue

            original = content
            content = fix_complex_should_patterns(content)

            if content != original:
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(content)
                print(f"Fixed: {file_path}")
                fixed_count += 1

print(f"\nTotal files fixed: {fixed_count}")