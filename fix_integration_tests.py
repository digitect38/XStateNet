import re

# Read the file
with open(r'C:\Develop25\XStateNet\SemiStandard.Tests\StateMachineIntegrationTests.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix FluentAssertions patterns
replacements = [
    (r'carrier\.Should\(\)\.BeTrue\(\)', 'Assert.True(carrier)'),
    (r'(\w+)\.Should\(\)\.BeTrue\(\)', r'Assert.True(\1)'),
    (r'(\w+)\.Should\(\)\.BeFalse\(\)', r'Assert.False(\1)'),
    (r'(\w+)\.Should\(\)\.NotBeNull\(\)', r'Assert.NotNull(\1)'),
    (r'(\w+)\.Should\(\)\.BeNull\(\)', r'Assert.Null(\1)'),
    (r'(\w+)\.Should\(\)\.NotBeEmpty\(\)', r'Assert.NotEmpty(\1)'),
    (r'(\w+)\.GetCurrentState\(\)\.Should\(\)\.Contain\("([^"]+)"\)', r'Assert.Contains("\2", \1.GetCurrentState())'),
    (r'(\w+)\.GetCurrentState\(\)\.Should\(\)\.NotContain\("([^"]+)"\)', r'Assert.DoesNotContain("\2", \1.GetCurrentState())'),
    (r'(\w+)\.Should\(\)\.HaveCount\((\d+)\)', r'Assert.Equal(\2, \1.Count)'),
    (r'(\w+)\.Should\(\)\.Be\((\d+)\)', r'Assert.Equal(\2, \1)'),
    (r'(\w+)\.EventLog\.Should\(\)\.Contain\("([^"]+)"\)', r'Assert.Contains("\2", \1.EventLog)'),
    (r'(\w+)\.GetAllJobs\(\)\.Should\(\)\.HaveCount\((\d+)\)', r'Assert.Equal(\2, \1.GetAllJobs().Count)'),
    (r'(\w+)\.Should\(\)\.Contain\("([^"]+)"\)', r'Assert.Contains("\2", \1)'),
    (r'(\w+)\.HasMapping\("([^"]+)", "([^"]+)"\)\.Should\(\)\.BeTrue\(\)', r'Assert.True(\1.HasMapping("\2", "\3"))'),
]

for pattern, replacement in replacements:
    content = re.sub(pattern, replacement, content)

# Write the file back
with open(r'C:\Develop25\XStateNet\SemiStandard.Tests\StateMachineIntegrationTests.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Fixed all FluentAssertions patterns in StateMachineIntegrationTests.cs")