import os
import re

def replace_double_quotes_comprehensive(content):
    """Replace double quotes with single quotes in JSON strings more comprehensively"""

    lines = content.split('\n')
    modified_lines = []

    for line in lines:
        # Check if this line looks like it contains JSON content
        # Look for patterns that indicate JSON in C# code
        if any(pattern in line for pattern in ['""', '@"', 'json', 'script', 'Script', 'stateMachine']):
            # Replace "" with ' but be careful about C# string escaping

            # First handle escaped quotes in C# verbatim strings
            line = line.replace('\\""', '__TEMP_ESCAPED__')

            # Replace double-double quotes pattern
            line = line.replace('""', "'")

            # Restore escaped quotes
            line = line.replace('__TEMP_ESCAPED__', '\\"')

        modified_lines.append(line)

    return '\n'.join(modified_lines)

def process_file(filepath):
    """Process a single file"""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        # Check if file contains patterns we want to replace
        if '""' in content:
            new_content = replace_double_quotes_comprehensive(content)

            if new_content != content:
                # Create backup
                backup_path = filepath + '.bak'
                with open(backup_path, 'w', encoding='utf-8') as f:
                    f.write(content)

                # Write modified content
                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(new_content)

                print(f"Modified: {os.path.basename(filepath)}")
                return True
        return False
    except Exception as e:
        print(f"Error processing {filepath}: {e}")
        return False

def main():
    test_dir = r"C:\Develop25\XStateNet\test"

    # Get all .cs files
    cs_files = []
    for root, dirs, files in os.walk(test_dir):
        for file in files:
            if file.endswith('.cs'):
                cs_files.append(os.path.join(root, file))

    print(f"Found {len(cs_files)} C# files in test directory")

    modified_count = 0
    for filepath in cs_files:
        if process_file(filepath):
            modified_count += 1

    print(f"\nModified {modified_count} files")

if __name__ == "__main__":
    main()