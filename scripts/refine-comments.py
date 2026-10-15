#!/usr/bin/env python3
"""
精细化清理代码注释脚本

功能：逐文件移除设计决策论证、历史背景、架构权衡等内容，仅保留核心技术说明

用法：
    python scripts/refine-comments.py <file1.cs> [file2.cs ...]
"""

import re
import sys
from pathlib import Path

# 需要移除的复杂注释模式
PATTERNS_TO_REMOVE = [
    # 内部追踪号（单独一行或嵌入句中）
    (r'[A-Z]+-\d+[：:\s]', ''),  # F-5：/ MA-1 / MT-08
    
    # 设计动机/背景说明段落
    (r'<b>动机[（(][^）)]+[）)][：:\s].*?(?=<para>|</para>|$)', '', re.DOTALL),
    
    # 最小面原则/防过度设计说明
    (r'<b>最小面原则[）)].*?(?=<para>|</para>|$)', '', re.DOTALL),
    
    # 详细实现契约列表
    (r'<b>实现契约[）)][：:\s].*?<list type="bullet">.*?</list>', '', re.DOTALL),
    
    # 接线方式/集成路径
    (r'接线方式[：:].*?(?=<para>|</remarks>|$)', '', re.DOTALL),
    
    # 评审修订标注
    (r'[（(]?评审修订\s*\d+[）)]?', ''),
    
    # 括号内的追踪号
    (r'[（(][A-Z]+-\d+[）)]', ''),
]

def clean_comment(content: str) -> str:
    """清理单个文件的注释内容"""
    cleaned = content
    
    for pattern, replacement, *flags in PATTERNS_TO_REMOVE:
        flag = flags[0] if flags else 0
        cleaned = re.sub(pattern, replacement, cleaned, flags=flag)
    
    # 清理多余的空行和空格
    cleaned = re.sub(r'\n{3,}', '\n\n', cleaned)
    cleaned = re.sub(r' +$', '', cleaned, flags=re.MULTILINE)
    
    return cleaned

def process_file(filepath: Path) -> bool:
    """处理单个文件，返回是否发生变更"""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            original = f.read()
        
        cleaned = clean_comment(original)
        
        if cleaned != original:
            with open(filepath, 'w', encoding='utf-8', newline='\n') as f:
                f.write(cleaned)
            return True
        return False
    except Exception as e:
        print(f"Error processing {filepath}: {e}")
        return False

def main():
    if len(sys.argv) < 2:
        print("Usage: python scripts/refine-comments.py <file1.cs> [file2.cs ...]")
        sys.exit(1)
    
    files = [Path(arg) for arg in sys.argv[1:]]
    changed_count = 0
    
    for filepath in files:
        if not filepath.exists():
            print(f"File not found: {filepath}")
            continue
        
        if process_file(filepath):
            print(f"✓ Cleaned: {filepath}")
            changed_count += 1
        else:
            print(f"- No changes: {filepath}")
    
    print(f"\nTotal: {changed_count}/{len(files)} files cleaned")

if __name__ == '__main__':
    main()
