# -*- mode: python ; coding: utf-8 -*-
from pathlib import Path
from PyInstaller.utils.hooks import collect_data_files

datas = []
datas += collect_data_files('customtkinter')
datas += [
    ('PS2MemoryCardStudio-source/playstation-logo.png', '.'),
    ('PS2MemoryCardStudio-source/ps2-memory-card.png', '.'),
    ('PS2MemoryCardStudio-source/ps1-memory-card.png', '.'),
    ('PS2MemoryCardStudio-source/app-icon-small.png', '.'),
    ('PS2MemoryCardStudio-source/app-icon-32.png', '.'),
    ('PS2MemoryCardStudio-source/app-icon-48.png', '.'),
    ('PS2MemoryCardStudio-source/app-icon.png', '.'),
    ('PS2MemoryCardStudio-source/app-icon.ico', '.'),
]
template_root = Path('PS2MemoryCardStudio-source/templates-compressed')
datas += [
    (str(card_file), str(Path('templates') / card_file.parent.relative_to(template_root)))
    for card_file in template_root.rglob('*') if card_file.is_file()
]


a = Analysis(
    ['PS2MemoryCardStudio-source/PS2MemoryCardStudio.py'],
    pathex=['PS2MemoryCardStudio-source/engine'],
    binaries=[],
    datas=datas,
    hiddenimports=['darkdetect', 'ps2mc', 'ps2save', 'ps2mc_dir', 'ps2mc_ecc', 'lzari', 'sjistab', 'round'],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='PS2 Memory Card Studio',
    icon='PS2MemoryCardStudio-source/app-icon.ico',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
