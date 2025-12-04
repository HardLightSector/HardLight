import os
import re

def resolve_conflict_markers(file_path):
    """Remove git conflict markers, keeping the incoming changes (after =======)"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Pattern to match git conflict markers with optional file paths
        # Handles both empty and non-empty HEAD sections
        # Updated pattern to handle path suffixes after HEAD and empty sections
        pattern = r'<<<<<<< HEAD(?::.*?)?\n(.*?)\n=======\n(.*?)\n>>>>>>> .*?\n'
        
        # Replace with just the incoming version (group 2)
        # If group 2 is empty, replace with empty string
        def replacer(match):
            incoming = match.group(2)
            if incoming.strip():
                return incoming + '\n'
            else:
                return ''
        
        resolved = re.sub(pattern, replacer, content, flags=re.DOTALL)
        
        # Check if anything changed
        if resolved != content:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(resolved)
            return True
        return False
    except Exception as e:
        print(f"Error processing {file_path}: {e}")
        return False

def find_and_resolve_yaml_conflicts(root_dir):
    """Find all YAML files with conflicts and resolve them"""
    resolved_count = 0
    
    for root, dirs, files in os.walk(root_dir):
        for file in files:
            if file.endswith('.yml'):
                file_path = os.path.join(root, file)
                if resolve_conflict_markers(file_path):
                    resolved_count += 1
                    print(f"Resolved: {file_path}")
    
    print(f"\nTotal files resolved: {resolved_count}")

if __name__ == "__main__":
    resolve_path = r"F:\Floofdev\HardLight\Resources"
    print(f"Resolving conflicts in {resolve_path}...")
    find_and_resolve_yaml_conflicts(resolve_path)
