import os
import re

test_dir = r"C:\Develop25\XStateNet\Test"

def fix_all_should_patterns(content):
    """Fix all remaining .Should() patterns"""

    # Fix complex patterns first

    # someVar.Should().NotBeNull() -> Assert.NotNull(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.NotBeNull\(\)',
                     r'Assert.NotNull(\1)', content)

    # someVar.Should().BeNull() -> Assert.Null(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeNull\(\)',
                     r'Assert.Null(\1)', content)

    # someVar.Should().Be(value) -> Assert.Equal(value, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.Be\(([^)]+)\)',
                     r'Assert.Equal(\2, \1)', content)

    # someVar.Should().BeTrue() -> Assert.True(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeTrue\(\)',
                     r'Assert.True(\1)', content)

    # someVar.Should().BeFalse() -> Assert.False(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeFalse\(\)',
                     r'Assert.False(\1)', content)

    # someVar.Should().Contain(item) -> Assert.Contains(item, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.Contain\(([^)]+)\)',
                     r'Assert.Contains(\2, \1)', content)

    # someVar.Should().NotContain(item) -> Assert.DoesNotContain(item, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.NotContain\(([^)]+)\)',
                     r'Assert.DoesNotContain(\2, \1)', content)

    # someVar.Should().HaveCount(n) -> Assert.Equal(n, someVar.Count)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.HaveCount\(([^)]+)\)',
                     r'Assert.Equal(\2, \1.Count)', content)

    # someVar.Should().BeEmpty() -> Assert.Empty(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeEmpty\(\)',
                     r'Assert.Empty(\1)', content)

    # someVar.Should().NotBeEmpty() -> Assert.NotEmpty(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.NotBeEmpty\(\)',
                     r'Assert.NotEmpty(\1)', content)

    # someVar.Should().BeGreaterThan(n) -> Assert.True(someVar > n)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeGreaterThan\(([^)]+)\)',
                     r'Assert.True(\1 > \2)', content)

    # someVar.Should().BeLessThan(n) -> Assert.True(someVar < n)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeLessThan\(([^)]+)\)',
                     r'Assert.True(\1 < \2)', content)

    # someVar.Should().BeGreaterOrEqualTo(n) -> Assert.True(someVar >= n)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeGreaterOrEqualTo\(([^)]+)\)',
                     r'Assert.True(\1 >= \2)', content)

    # someVar.Should().StartWith(str) -> Assert.StartsWith(str, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.StartWith\(([^)]+)\)',
                     r'Assert.StartsWith(\2, \1)', content)

    # someVar.Should().EndWith(str) -> Assert.EndsWith(str, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.EndWith\(([^)]+)\)',
                     r'Assert.EndsWith(\2, \1)', content)

    # someVar.Should().BeEquivalentTo(value) -> Assert.Equal(value, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.BeEquivalentTo\(([^)]+)\)',
                     r'Assert.Equal(\2, \1)', content)

    # someVar.Should().NotBe(value) -> Assert.NotEqual(value, someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()]*?)\.Should\(\)\.NotBe\(([^)]+)\)',
                     r'Assert.NotEqual(\2, \1)', content)

    # Fix incorrect replacements like actor.Assert.Contains -> Assert.Contains
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_]*?)\.Assert\.Contains\(([^,]+), ([a-zA-Z_][a-zA-Z0-9_]*?)\)',
                     r'Assert.Contains(\2, \1.\3)', content)

    # Fix incorrect replacements like actor.Assert.Equal -> Assert.Equal
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_]*?)\.Assert\.Equal\(([^,]+), ([a-zA-Z_][a-zA-Z0-9_]*?)\)',
                     r'Assert.Equal(\2, \1.\3)', content)

    # Fix pingHits.Assert.Contains -> Assert.Contains
    content = re.sub(r'pingHits\.Assert\.Contains\(([^)]+)\)',
                     r'Assert.Contains(\1, pingHits)', content)

    # Fix pongHits.Assert.Contains -> Assert.Contains
    content = re.sub(r'pongHits\.Assert\.Contains\(([^)]+)\)',
                     r'Assert.Contains(\1, pongHits)', content)

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
            if '.Should()' not in content and '.Assert.' not in content:
                continue

            original = content
            content = fix_all_should_patterns(content)

            if content != original:
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(content)
                print(f"Fixed: {file_path}")
                fixed_count += 1

print(f"\nTotal files fixed: {fixed_count}")