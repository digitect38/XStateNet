import os
import re

test_dir = r"C:\Develop25\XStateNet\Test"

def fix_all_should_patterns(content):
    """Fix all remaining .Should() patterns"""

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

    # someVar.Should().BeGreaterThan(value) -> Assert.True(someVar > value)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeGreaterThan\(([^)]+)\)',
                     r'Assert.True(\1 > \2)', content)

    # someVar.Should().BeGreaterThanOrEqualTo(value) -> Assert.True(someVar >= value)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeGreaterThanOrEqualTo\(([^)]+)\)',
                     r'Assert.True(\1 >= \2)', content)

    # someVar.Should().BeLessThan(value) -> Assert.True(someVar < value)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeLessThan\(([^)]+)\)',
                     r'Assert.True(\1 < \2)', content)

    # someVar.Should().BeLessThanOrEqualTo(value) -> Assert.True(someVar <= value)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeLessThanOrEqualTo\(([^)]+)\)',
                     r'Assert.True(\1 <= \2)', content)

    # someVar.Should().BeTrue() -> Assert.True(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeTrue\([^)]*\)',
                     r'Assert.True(\1)', content)

    # someVar.Should().BeFalse() -> Assert.False(someVar)
    content = re.sub(r'([a-zA-Z_][a-zA-Z0-9_.\[\]()!]*?)\.Should\(\)\.BeFalse\([^)]*\)',
                     r'Assert.False(\1)', content)

    # Fix complex expressions with casts - special handling for JValue
    # (counterVal is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterVal ?? 0)).Should().Be(2)
    # -> Assert.Equal(2, counterVal is Newtonsoft.Json.Linq.JValue jv ? jv.ToObject<int>() : (int)(counterVal ?? 0))
    content = re.sub(r'\(([^)]*is[^)]*\?[^)]*:[^)]*)\)\.Should\(\)\.Be\(([^)]+)\)',
                     r'Assert.Equal(\2, \1)', content)

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
            content = fix_all_should_patterns(content)

            if content != original:
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(content)
                print(f"Fixed: {file_path}")
                fixed_count += 1

print(f"\nTotal files fixed: {fixed_count}")