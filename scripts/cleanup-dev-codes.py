#!/usr/bin/env python3
"""
批量清理代码注释中的开发代号标记

功能：
1. 扫描 .cs 文件中的 G/M/P/EL/TK/HC 系列标记
2. 移除标记前缀，保留核心技术说明
3. 生成清理报告

用法：
    python scripts/cleanup-dev-codes.py --dry-run   # 预览变更
    python scripts/cleanup-dev-codes.py --apply     # 实际执行
"""

import re
import sys
import os
from pathlib import Path
from typing import List, Tuple

# 设置输出编码为 UTF-8
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# 开发代号模式（需要清理的前缀）
DEV_CODE_PATTERNS = [
    # 基础格式：G7-03：/ M6-HC-12：/ HC-04：
    r'G\d+-\d+[：:\s]',      
    r'M\d+-[A-Z]+-\d+[：:/\s\(]',  
    r'(EL|TK|HC)-\d+\s*修复[：:\s]',  
    r'(EL|TK|HC)-\d+[：:\s]',  
    
    # 带括号的复合格式：M6-HC-05（D2-A）/ TK-20（P2.10）
    r'[GM]\d+[-\w]+\s*[（(][^）)]+[）)]\s*[：:\s]',
    
    # 带斜杠的格式：G8-01/
    r'[GM]\d+-\d+[a-z]*/',
    
    # 评审标注：评审修订 5 / （评审修订 7）
    r'[（(]?评审修订\s*\d+[）)]?[：:：\s\)]',
    
    # 括号内的评审标注（句子中间）
    r'（评审修订\s*\d+）',
]

def clean_comment_line(line: str) -> Tuple[str, bool]:
    """
    清理单行注释中的开发代号标记

    Returns:
        (清理后的行, 是否发生变更)
    """
    original = line
    stripped = line.lstrip()
    indent = line[:len(line) - len(stripped)]

    # 只处理注释行
    if not (stripped.startswith('//') or stripped.startswith('///')):
        return line, False

    # 提取注释前缀和内容
    if stripped.startswith('///'):
        comment_prefix = '///'
    else:
        comment_prefix = '//'
    
    content = stripped[len(comment_prefix):]
    # 移除开头的空格，但保留后续内容
    content_stripped = content.lstrip()

    # 检查是否包含开发代号或评审标注
    has_dev_code = any(re.search(p, content_stripped) for p in DEV_CODE_PATTERNS)

    if not has_dev_code:
        return line, False

    # 清理所有匹配的模式
    cleaned_content = content_stripped
    for pattern in DEV_CODE_PATTERNS:
        cleaned_content = re.sub(pattern, '', cleaned_content)

    # 重新组装（保持原有的 /// 或 // 格式）
    cleaned_line = indent + comment_prefix + ' ' + cleaned_content.lstrip()

    return cleaned_line, True


def process_file(filepath: Path, dry_run: bool = True) -> Tuple[int, List[Tuple[int, str, str]]]:
    """
    处理单个文件

    Returns:
        (变更行数, [(行号, 原始内容, 新内容)])
    """
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            lines = f.readlines()
    except (UnicodeDecodeError, IOError):
        return 0, []

    changes = []
    new_lines = []

    for i, line in enumerate(lines):
        cleaned_line, changed = clean_comment_line(line)
        new_lines.append(cleaned_line)
        if changed:
            changes.append((i + 1, line.rstrip(), cleaned_line.rstrip()))

    if not changes:
        return 0, []

    # 如果实际应用变更
    if not dry_run and changes:
        with open(filepath, 'w', encoding='utf-8', newline='\n') as f:
            f.writelines(new_lines)

    return len(changes), changes


def scan_directory(directory: Path, dry_run: bool = True) -> Tuple[int, List[Tuple[Path, int, List[Tuple[int, str, str]]]]]:
    """
    递归扫描目录并处理所有 .cs 文件

    Returns:
        (总变更数, [(文件路径, 变更行数, 变更详情)])
    """
    all_changes = []
    total_changes = 0

    for root, _, files in os.walk(directory):
        # 跳过生成文件和测试文件
        if 'obj' in root or 'bin' in root:
            continue

        for filename in files:
            if not filename.endswith('.cs'):
                continue

            filepath = Path(root) / filename
            change_count, changes = process_file(filepath, dry_run)

            if change_count > 0:
                all_changes.append((filepath, change_count, changes))
                total_changes += change_count

    return total_changes, all_changes


def format_report(total_changes: int, file_changes: List[Tuple[Path, int, List[Tuple[int, str, str]]]]) -> str:
    """格式化清理报告"""
    if total_changes == 0:
        return "[OK] No development code markers found"

    lines = []
    lines.append(f"[SUMMARY] Cleaned {total_changes} comment lines across {len(file_changes)} files\n")

    for filepath, change_count, changes in sorted(file_changes):
        lines.append(f"\n[FILE] {filepath} ({change_count} changes):")
        for line_no, old, new in changes[:5]:  # 只显示前5个变更
            # 截断过长的行
            old_display = old.strip()[:100] + ('...' if len(old.strip()) > 100 else '')
            new_display = new.strip()[:100] + ('...' if len(new.strip()) > 100 else '')
            lines.append(f"  Line {line_no}:")
            lines.append(f"    - {old_display}")
            lines.append(f"    + {new_display}")

        if change_count > 5:
            lines.append(f"    ... and {change_count - 5} more changes")

    lines.append(f"\n[RESULT] Total {total_changes} lines cleaned")
    return '\n'.join(lines)


def main():
    """主函数"""
    dry_run = '--dry-run' in sys.argv

    # 确定扫描目录
    if len(sys.argv) > 1 and sys.argv[1] not in ['--dry-run', '--apply']:
        directories = [Path(arg) for arg in sys.argv[1:] if arg not in ['--dry-run', '--apply']]
    else:
        # 默认扫描主要源代码目录
        project_root = Path.cwd()
        directories = [
            project_root / 'Mud.HttpUtils.Client',
            project_root / 'Mud.HttpUtils.Generator',
            project_root / 'Mud.HttpUtils.Abstractions',
            project_root / 'Mud.HttpUtils.Resilience',
        ]

    # 过滤存在的目录
    existing_dirs = [d for d in directories if d.exists()]
    if not existing_dirs:
        print("Error: No valid source code directories found")
        sys.exit(1)

    mode = "DRY RUN" if dry_run else "APPLY"
    print(f"[MODE] {mode} - Scanning directories: {', '.join(str(d) for d in existing_dirs)}")
    print("=" * 80)

    # 执行清理（处理所有目录）
    total_changes = 0
    file_changes = []
    for directory in existing_dirs:
        changes, fc = scan_directory(directory, dry_run)
        total_changes += changes
        file_changes.extend(fc)

    # 输出报告
    report = format_report(total_changes, file_changes)
    print(report)

    if dry_run:
        print("\n[HINT] Run with --apply to actually apply these changes")
    else:
        print(f"\n[SUCCESS] Applied {total_changes} changes")

    sys.exit(0)


if __name__ == '__main__':
    main()
