#!/usr/bin/env python3
"""Apply this repository's educational files to a locally obtained Unity template."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import shutil
import sys

ROOT = Path(__file__).resolve().parent.parent
EXPECTED_EDITOR = '6000.3.27f2'
VERSIONS = {
    'com.unity.animation.rigging': '1.4.1',
    'com.unity.ide.rider': '3.1.0',
    'com.unity.inputsystem': '1.20.1',
    'com.unity.multiplayer.center': '1.0.1',
    'com.unity.multiplayer.playmode': '2.0.2',
    'com.unity.multiplayer.tools': '2.2.12',
    'com.unity.netcode.gameobjects': '2.14.0',
    'com.unity.postprocessing': '3.5.4',
    'com.unity.services.multiplayer': '2.4.0',
    'com.unity.timeline': '1.8.13',
    'com.unity.transport': '2.7.4',
}

def patch_method(text: str, name: str, base_first: bool) -> str:
    old = f'private void {name}()'
    new = f'protected override void {name}()'
    if text.count(old) != 1 or new in text:
        raise ValueError(f'GameNetworkManager.{name} is not the expected template version')
    text = text.replace(old, new, 1)
    start = text.index(new)
    opening = text.index('{', start)
    depth = 0
    for i in range(opening, len(text)):
        depth += (text[i] == '{') - (text[i] == '}')
        if depth == 0:
            closing = i
            break
    else:
        raise ValueError(f'Could not parse {name}')
    body = text[opening + 1:closing]
    if f'base.{name}()' in body:
        raise ValueError(f'Unexpected existing base call in {name}')
    if base_first:
        body = '\n            base.' + name + '();' + body
    else:
        if not body.endswith('        '):
            raise ValueError(f'Unexpected formatting in {name}')
        body = body[:-8] + '            base.' + name + '();\n        '
    return text[:opening + 1] + body + text[closing:]

def install(project: Path) -> None:
    project = project.resolve(strict=True)
    manifest = project/'Packages/manifest.json'
    manager = project/'Assets/Core/Scripts/Runtime/Framework/Networking/GameNetworkManager.cs'
    scene = project/'Assets/Shooter/TestScenes/[BB] Shooter.unity'
    version = project/'ProjectSettings/ProjectVersion.txt'
    for f in (manifest, manager, scene, version):
        if not f.is_file(): raise ValueError(f'Not the expected Unity template; missing: {f}')
    if not re.search(r'^m_EditorVersion:\s*'+re.escape(EXPECTED_EDITOR)+r'\s*$', version.read_text(encoding='utf-8-sig'), re.MULTILINE):
        raise ValueError(f'Expected Unity Editor {EXPECTED_EDITOR}; check ProjectVersion.txt')
    destination = project/'Assets/CheatEducation'
    backup = project/'.cheat-education-backup'
    if destination.exists() or backup.exists():
        raise ValueError('Assets/CheatEducation or .cheat-education-backup already exists; no files were changed')
    if (project/'Temp/UnityLockfile').exists():
        raise ValueError('Unity Editor appears open; close it before installing (Temp/UnityLockfile exists)')
    raw_manager = manager.read_text(encoding='utf-8-sig')
    manager_text = patch_method(patch_method(raw_manager, 'Awake', True), 'OnDestroy', False)
    raw_manifest = manifest.read_text(encoding='utf-8-sig')
    data = json.loads(raw_manifest)
    if not isinstance(data.get('dependencies'), dict): raise ValueError('Invalid package manifest')
    data['dependencies'].update(VERSIONS)
    manifest_text = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
    # Back up only the two files modified in the user's project. The package lock is updated by Unity.
    backup.mkdir()
    shutil.copy2(manager, backup/'GameNetworkManager.cs')
    shutil.copy2(manifest, backup/'manifest.json')
    try:
        shutil.copytree(ROOT/'Assets/CheatEducation', destination)
        manager.write_text(manager_text, encoding='utf-8', newline='\n')
        manifest.write_text(manifest_text, encoding='utf-8', newline='\n')
    except Exception:
        shutil.copy2(backup/'GameNetworkManager.cs', manager)
        shutil.copy2(backup/'manifest.json', manifest)
        if destination.exists(): shutil.rmtree(destination)
        raise
    print('教材を追加しました。Unity Hub でプロジェクトを開き、README のシーン設定へ進んでください。')
    print(f'変更前の2ファイル: {backup}')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Unity 公式テンプレートに教材を導入します')
    parser.add_argument('project', type=Path, help='Unity Hub で作成した公式テンプレートのプロジェクト')
    args = parser.parse_args()
    try: install(args.project)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f'導入できませんでした: {exc}', file=sys.stderr)
        sys.exit(1)
