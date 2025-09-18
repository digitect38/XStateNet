import os
import re

def replace_double_quotes_in_json(content):
    """Replace double quotes with single quotes in JSON strings"""

    # Replace "" patterns with ' in JSON-like content
    # This handles patterns like ""id"", ""initial"", etc.

    # Pattern 1: Replace "" around JSON keys/values
    content = re.sub(r'""([^"]+)""', r"'\1'", content)

    # Pattern 2: Also handle cases where we have ""word"": value
    content = re.sub(r'"([^"]+)":\s*"([^"]+)"', r"'\1': '\2'", content)

    return content

def process_file(filepath):
    """Process a single file"""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        # Check if file contains JSON-like patterns with double quotes
        if '""' in content:
            new_content = replace_double_quotes_in_json(content)

            if new_content != content:
                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(new_content)
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
            print(f"Modified: {os.path.basename(filepath)}")

    print(f"\nModified {modified_count} files")

if __name__ == "__main__":
    main()