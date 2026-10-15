#!/usr/bin/env python3
"""
Mud.HttpUtils 注释合规性检查脚本

功能：
1. 扫描指定目录下的 .cs 文件
2. 检测违规注释模式（开发代号、评审标注等）
3. 输出违规位置及上下文
4. 返回非零退出码供 CI 使用

用法：
    python scripts/lint-comments.py [目录路径...]
    # 默认扫描当前工作目录

示例：
    python scripts/lint-comments.py Mud.HttpUtils.Client Mud.HttpUtils.Generator
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

# 违规模式定义
FORBIDDEN_PATTERNS = [
    # 开发代号系列
    (r'//\s*[GM]\d+-\w+', '开发代号（G/M 系列）', '应迁移至 ADR 或清理'),
    (r'//\s*P\d+-[A-Z]{2}-\d+', '优先级追踪号（P 系列）', '仅允许进行中的 P0/P1 任务'),
    (r'//\s*(EL|TK|HC)-\d+', '功能线追踪号', '应迁移至 ADR 或清理'),

    # 评审标注
    (r'评审修订\s*\d+', '评审标注', '应迁移至 ADR'),
    (r'\d{4}-\d{2}-\d{2}\s*评审', '评审日期记录', '应迁移至 ADR'),

    # TODO 待删除
    (r'//.*TODO.*删除', 'TODO 待删除标记', '应纳入 Issue 追踪'),
    (r'//.*FIXME.*删除', 'FIXME 待删除标记', '应纳入 Issue 追踪'),

    # 内部黑话
    (r'//.*仲裁', '仲裁记录', '应提炼为技术原因，详情入 ADR'),
    (r'//.*决策过程', '决策过程记录', '应迁移至 ADR'),
]

# 允许的例外模式（进行中的 P0/P1 任务）
ALLOWED_EXCEPTIONS = [
    r'//\s*\[P[01]-.*\]',  # [P0-XXX] 或 [P1-XXX] 格式
]


def is_allowed_exception(line: str) -> bool:
    """检查是否为允许的例外（进行中的高优先级任务）"""
    return any(re.search(pattern, line) for pattern in ALLOWED_EXCEPTIONS)


def check_file(filepath: Path) -> List[Tuple[int, str, str, str]]:
    """
    检查单个文件的注释合规性

    Returns:
        违规列表：[(行号, 内容, 匹配模式, 建议)]
    """
    violations = []

    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            lines = f.readlines()
    except UnicodeDecodeError:
        # 跳过非 UTF-8 文件
        return violations
    except IOError:
        return violations

    for i, line in enumerate(lines, 1):
        # 只检查注释行
        stripped = line.strip()
        if not (stripped.startswith('//') or stripped.startswith('///')):
            continue

        # 检查是否为允许的例外
        if is_allowed_exception(stripped):
            continue

        # 检查违规模式
        for pattern, description, suggestion in FORBIDDEN_PATTERNS:
            if re.search(pattern, stripped):
                violations.append((i, stripped, description, suggestion))
                break  # 每行只报告第一个匹配

    return violations


def scan_directory(directory: Path) -> List[Tuple[Path, int, str, str, str]]:
    """
    递归扫描目录下的所有 .cs 文件

    Returns:
        违规列表：[(文件路径, 行号, 内容, 匹配模式, 建议)]
    """
    all_violations = []

    for root, _, files in os.walk(directory):
        # 跳过生成文件和测试文件（可选）
        if 'obj' in root or 'bin' in root:
            continue

        for filename in files:
            if not filename.endswith('.cs'):
                continue

            filepath = Path(root) / filename
            violations = check_file(filepath)

            for line_no, content, pattern, suggestion in violations:
                all_violations.append((filepath, line_no, content, pattern, suggestion))

    return all_violations


def format_output(violations: List[Tuple[Path, int, str, str, str]]) -> str:
    """格式化输出结果"""
    if not violations:
        return "[OK] No comment violations found"

    lines = []
    lines.append(f"[ERROR] Found {len(violations)} comment violations:\n")

    # 按文件分组
    by_file = {}
    for filepath, line_no, content, pattern, suggestion in violations:
        by_file.setdefault(filepath, []).append((line_no, content, pattern, suggestion))

    for filepath, file_violations in sorted(by_file.items()):
        lines.append(f"\n[FILE] {filepath}:")
        for line_no, content, pattern, suggestion in file_violations:
            # 截断过长的注释
            display_content = content[:80] + ('...' if len(content) > 80 else '')
            lines.append(f"  Line {line_no}: {display_content}")
            lines.append(f"    -> {pattern} | Suggestion: {suggestion}")

    lines.append(f"\nTotal {len(violations)} violations. Please fix and resubmit.")
    return '\n'.join(lines)


def main():
    """主函数"""
    # 确定扫描目录
    if len(sys.argv) > 1:
        directories = [Path(arg) for arg in sys.argv[1:]]
    else:
        # 默认扫描项目根目录下的主要源代码目录
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

    print(f"Scanning directories: {', '.join(str(d) for d in existing_dirs)}")
    print("=" * 60)

    # 执行扫描
    all_violations = []
    for directory in existing_dirs:
        violations = scan_directory(directory)
        all_violations.extend(violations)

    # 输出结果
    output = format_output(all_violations)
    print(output)

    # 返回退出码
    sys.exit(1 if all_violations else 0)


if __name__ == '__main__':
    main()
