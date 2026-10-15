#!/usr/bin/env python3
"""为 Packages/ 下缺少 .meta 的文件和文件夹生成 Unity .meta（新 GUID）。

git 安装的 UPM 包是只读的，Unity 不会为它生成 .meta，缺失的资源会被忽略，所以 .meta 必须提交。
以 ~ 结尾的文件夹（Documentation~、Samples~）Unity 不导入，跳过。
用法：python3 tools/gen-meta.py
"""
import os
import uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Packages")

TEMPLATES = {
    "folder": "fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".cs": "fileFormatVersion: 2\nguid: {g}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".asmdef": "fileFormatVersion: 2\nguid: {g}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    "text": "fileFormatVersion: 2\nguid: {g}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
}
TEXT_EXT = {".json", ".md", ".txt"}


def template_for(path):
    if os.path.isdir(path):
        return TEMPLATES["folder"]
    ext = os.path.splitext(path)[1]
    if ext in TEMPLATES:
        return TEMPLATES[ext]
    if ext in TEXT_EXT:
        return TEMPLATES["text"]
    return None


def main():
    created = 0
    for package in sorted(os.listdir(ROOT)):
        pkg_dir = os.path.join(ROOT, package)
        if not os.path.isdir(pkg_dir):
            continue
        for dirpath, dirnames, filenames in os.walk(pkg_dir):
            dirnames[:] = [d for d in dirnames if not d.endswith("~") and not d.startswith(".")]
            for name in dirnames + filenames:
                if name.endswith(".meta") or name.startswith("."):
                    continue
                path = os.path.join(dirpath, name)
                meta = path + ".meta"
                if os.path.exists(meta):
                    continue
                tpl = template_for(path)
                if tpl is None:
                    print("跳过（未知类型）:", os.path.relpath(path, ROOT))
                    continue
                with open(meta, "w", newline="\n") as f:
                    f.write(tpl.format(g=uuid.uuid4().hex))
                print("生成:", os.path.relpath(meta, ROOT))
                created += 1
    print(f"共生成 {created} 个 .meta")


if __name__ == "__main__":
    main()
