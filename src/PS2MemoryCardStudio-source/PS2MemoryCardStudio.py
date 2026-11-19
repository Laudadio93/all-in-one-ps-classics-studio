from __future__ import annotations

import os
import ctypes
import gzip
import base64
import math
import shutil
import struct
import sys
import tempfile
import threading
import time
import traceback
import zlib
from pathlib import Path
from tkinter import filedialog, messagebox
import tkinter as tk
# PyInstaller's --windowed bootloader leaves standard streams unset. CustomTkinter
# may report an optional font fallback during import, so provide quiet sinks.
if sys.stderr is None:
    sys.stderr = open(os.devnull, "w")
if sys.stdout is None:
    sys.stdout = open(os.devnull, "w")
import customtkinter as ctk


IS_FROZEN = bool(getattr(sys, "frozen", False))
SOURCE_DIR = Path(__file__).resolve().parent
RESOURCE_DIR = Path(getattr(sys, "_MEIPASS", SOURCE_DIR))
ENGINE_DIR = SOURCE_DIR / "engine"
if not IS_FROZEN:
    sys.path.insert(0, str(ENGINE_DIR))

import ps2mc  # noqa: E402
import ps2save  # noqa: E402
import ps1card


DEFAULT_TEMPLATE_DIR = RESOURCE_DIR / "templates"
DEFAULT_OUTPUT_DIR = Path.home() / "Documents" / "PS2 Memory Card Studio" / "Cards"
APP_DATA_ROOT = Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local")))
SETTINGS_DIR = APP_DATA_ROOT / "PS2 Memory Card Studio"
OUTPUT_DIR_PREFERENCE = SETTINGS_DIR / "output-folder.txt"
LEGACY_OUTPUT_DIR_PREFERENCE = APP_DATA_ROOT / "PS2SaveCardStudio" / "output-folder.txt"
CARD_MIB = (8, 16, 32, 64)
MIB = 1024 * 1024
ABOUT_LICENSE = """This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute this
software, in source code or compiled form, for any purpose and by any means.

The software is provided as-is, without warranty of any kind. For the full
license text, see https://unlicense.org/.
"""
ABOUT_CREDITS = (
    "The PS2 memory-card filesystem and save-format engine is adapted from the "
    "PS2 Memory Card Manager by Ross Ridge and its maintainers. Original PS2 "
    "filesystem work by Ross Ridge is public domain; the manager is released "
    "under the Unlicense.\n\n"
    "PS2 Memory Card Studio bundles that engine into this standalone application."
)


def expected_image_size(size_mib: int, with_ecc: bool) -> int:
    raw_size = size_mib * MIB
    if with_ecc:
        pages = raw_size // ps2mc.PS2MC_STANDARD_PAGE_SIZE
        return pages * (ps2mc.PS2MC_STANDARD_PAGE_SIZE + 16)
    return raw_size


def find_template(folder: Path, size_mib: int, with_ecc: bool) -> Path | None:
    if with_ecc:
        candidates = [
            folder / "with ECC" / f"blank {size_mib}MiB.ps2",
            folder / "with ECC" / f"blank {size_mib}MiB.vm2",
        ]
    else:
        candidates = [
            folder / "without ECC" / f"blank {size_mib}MiB.bin",
            folder / "without ECC" / f"blank {size_mib}MiB (old).bin",
        ]
    expected = expected_image_size(size_mib, with_ecc)
    for candidate in candidates:
        if candidate.is_file() and candidate.stat().st_size == expected:
            return candidate
        compressed = candidate.with_name(candidate.name + ".gz")
        if compressed.is_file() and compressed.stat().st_size >= 18:
            with compressed.open("rb") as source:
                source.seek(-4, os.SEEK_END)
                if struct.unpack("<I", source.read(4))[0] == expected:
                    return compressed
    return None


def preferred_output_dir() -> Path:
    """Load the current or legacy saved folder, falling back to Documents."""
    for preference in (OUTPUT_DIR_PREFERENCE, LEGACY_OUTPUT_DIR_PREFERENCE):
        try:
            saved = Path(preference.read_text(encoding="utf-8").strip()).expanduser()
            if saved.is_dir():
                return saved
        except (OSError, ValueError):
            continue
    return DEFAULT_OUTPUT_DIR


def load_save(path: Path):
    with path.open("rb") as src:
        kind = ps2save.detect_file_type(src)
        src.seek(0)
        save = ps2save.ps2_save_file()
        loaders = {
            "psu": save.load_ems,
            "max": save.load_max_drive,
            "cbs": save.load_codebreaker,
            "sps": save.load_sharkport,
            "psv": save.load_psv,
        }
        if kind not in loaders:
            raise ValueError("The file is not a supported PS2 save file.")
        loaders[kind](src)
    return save, kind


def save_payload_size(save) -> int:
    """Return the combined uncompressed file bytes that will be copied to a card."""
    total = 0
    for index in range(save.get_directory()[2]):
        _entry, data = save.get_file(index)
        total += len(data)
    return total


def create_and_import(save_paths: list[Path], card_size: int, with_ecc: bool,
                      template_dir: Path, output_path: Path) -> dict:
    loaded = []
    failures = []
    for save_path in save_paths:
        try:
            save, kind = load_save(save_path)
            loaded.append((save_path, save, kind))
        except Exception as exc:
            failures.append(f"{save_path.name}: {str(exc).strip() or 'Could not read save.'}")
    if not loaded:
        details = "\n".join(failures) or "No valid save files were selected."
        raise ValueError(details)

    payload_size = sum(save_payload_size(save) for _path, save, _kind in loaded)
    capacity_bytes = card_size * MIB
    if payload_size > capacity_bytes:
        raise ValueError(
            f"The selected saves contain {payload_size / MIB:.2f} MiB of files, "
            f"which exceeds the selected {card_size} MiB card capacity.\n\n"
            "Remove some saves or choose a larger card."
        )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    template = find_template(template_dir, card_size, with_ecc)
    if template and template.resolve() == output_path.resolve():
        raise ValueError("Choose a new output file; the blank template must stay unchanged.")

    fd, temp_name = tempfile.mkstemp(prefix=".ps2card-", suffix=".tmp", dir=output_path.parent)
    os.close(fd)
    temp_path = Path(temp_name)
    try:
        if template:
            if template.suffix.lower() == ".gz":
                with gzip.open(template, "rb") as source, temp_path.open("wb") as target:
                    shutil.copyfileobj(source, target, 1024 * 1024)
            else:
                shutil.copyfile(template, temp_path)
        else:
            pages = card_size * MIB // ps2mc.PS2MC_STANDARD_PAGE_SIZE
            params = (
                with_ecc,
                ps2mc.PS2MC_STANDARD_PAGE_SIZE,
                ps2mc.PS2MC_STANDARD_PAGES_PER_ERASE_BLOCK,
                pages,
            )
            with temp_path.open("w+b") as card_file:
                ps2mc.ps2mc(card_file, True, params).close()

        imported_saves = []
        with temp_path.open("r+b") as card_file:
            card = ps2mc.ps2mc(card_file, True)
            try:
                card_ecc = card.spare_size > 0
                if card_ecc != with_ecc:
                    raise ValueError("The selected template's ECC type does not match the dropdown.")
                for save_path, save, kind in loaded:
                    save_dir = save.get_directory()[8]
                    try:
                        if not card.import_save_file(save, False):
                            failures.append(f"{save_path.name}: save folder {save_dir} already exists on this card.")
                            continue
                        imported_saves.append({"file": save_path.name, "directory": save_dir, "format": kind})
                    except Exception as exc:
                        raise RuntimeError(f"Could not import {save_path.name}: {exc}") from exc
                if not imported_saves:
                    details = "\n".join(failures) or "No saves were imported."
                    raise ValueError(details)
                free_bytes = card.get_free_space()
                total_bytes = card.get_allocatable_space()
            finally:
                card.close()

        if output_path.exists():
            stamp = time.strftime("%Y%m%d-%H%M%S")
            backup_path = output_path.with_name(f"{output_path.stem}.{stamp}.bak{output_path.suffix}")
            shutil.copy2(output_path, backup_path)
        else:
            backup_path = None
        os.replace(temp_path, output_path)
        return {
            "imported": imported_saves,
            "failures": failures,
            "template": template,
            "backup": backup_path,
            "free": free_bytes,
            "total": total_bytes,
        }
    finally:
        temp_path.unlink(missing_ok=True)


def _curve(points, steps=10):
    """Sample a cubic Bezier as coordinates for Tk's vector canvas."""
    (x0,y0),(x1,y1),(x2,y2),(x3,y3)=points
    return [((1-t)**3*x0+3*(1-t)**2*t*x1+3*(1-t)*t*t*x2+t**3*x3,
             (1-t)**3*y0+3*(1-t)**2*t*y1+3*(1-t)*t*t*y2+t**3*y3)
            for i in range(steps+1) for t in [i/steps]]


def draw_ps_emblem(canvas, x, y, scale, color="#E5002B", background="#0D1522"):
    """Draw the classic PS monogram and its elliptical base from layered curves."""
    def path(coords, fill):
        pts = [(x+px*scale, y+py*scale) for px,py in coords]
        canvas.create_polygon(*(v for p in pts for v in p), fill=fill, outline="", smooth=True, splinesteps=24)
    # Curved ribbons form the flat, rising oval base of the PlayStation mark.
    upper = _curve([(4,40),(17,35),(25,38),(34,40)]) + _curve([(34,40),(43,39),(51,35),(57,32)])[1:]
    lower = _curve([(57,32),(49,42),(37,49),(24,51)]) + _curve([(24,51),(14,49),(7,46),(4,43)])[1:]
    path(upper + lower, color if color != "#E5002B" else "#F5C400")
    if color == "#E5002B":
        # Color bands are integral to the original logo's sweeping base.
        path([(12,39),(20,38),(31,41),(42,40),(53,35),(57,32),(56,37),(44,44),(31,46),(20,44)], "#00A99D")
        path([(25,47),(36,46),(48,41),(56,36),(58,39),(49,47),(37,51),(25,52)], "#3974C6")
    # The upright P sits in front of the colored elliptical ribbons.
    path([(23,2),(34,5),(43,10),(46,17),(44,23),(39,27),(32,29),(32,52),(23,49)], color)
    path([(31,12),(36,14),(38,18),(36,21),(31,22)], background)


def draw_ps_logo(canvas, x: float, y: float, scale: float, background="#0D1522"):
    """Draw the multicolor PlayStation mark used in the app identity."""
    draw_ps_emblem(canvas, x, y, scale, color="#E5002B", background=background)


class PS2MemoryCardStudio:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("PS2 Memory Card Studio")
        self.app_icon_small = tk.PhotoImage(file=str(RESOURCE_DIR / "app-icon-small.png"))
        self.app_icon_32 = tk.PhotoImage(file=str(RESOURCE_DIR / "app-icon-32.png"))
        self.app_icon_48 = tk.PhotoImage(file=str(RESOURCE_DIR / "app-icon-48.png"))
        self.app_icon_source = tk.PhotoImage(file=str(RESOURCE_DIR / "app-icon.png"))
        self.app_icon_sizes = [
            self.app_icon_small,
            self.app_icon_32,
            self.app_icon_48,
            self.app_icon_source.subsample(4, 4),
            self.app_icon_source.subsample(2, 2),
            self.app_icon_source,
        ]
        if sys.platform == "win32":
            try:
                self.root.iconbitmap(str(RESOURCE_DIR / "app-icon.ico"))
            except tk.TclError:
                # iconphoto below is the fallback for Tk builds that cannot
                # read PNG-backed frames from an ICO file.
                pass
        # Apply the explicit image after iconbitmap so the Tk window and its
        # taskbar button use the bundled artwork rather than the default icon.
        self.root.iconphoto(True, *self.app_icon_sizes)
        screen_width = root.winfo_screenwidth()
        screen_height = root.winfo_screenheight()
        window_width = min(1120, max(820, screen_width - 48))
        window_height = min(830, max(720, screen_height - 72))
        self.root.geometry(f"{window_width}x{window_height}"
                           f"+{max(0, (screen_width - window_width) // 2)}"
                           f"+{max(0, (screen_height - window_height) // 2)}")
        self.root.minsize(min(980, window_width), min(720, window_height))
        self.root.configure(fg_color="#0C1421")
        self.ps1_mode = os.environ.get("CLASSICSSUITE_MEMORY_VIEW") == "ps1"
        self.save_paths: list[Path] = []
        self.template_dir = tk.StringVar(value=str(DEFAULT_TEMPLATE_DIR))
        self.card_type = tk.StringVar(value="MCR" if self.ps1_mode else "ECC")
        self.card_size = tk.StringVar(value="128 KiB" if self.ps1_mode else "8 MiB")
        self.output_dir = tk.StringVar(value=str(preferred_output_dir()))
        self.output_name = tk.StringVar(value="new_save_8MiB_ECC.ps2")
        self.status = tk.StringVar(value="Ready · all processing stays on this computer")
        self.busy = False
        self.close_after_import = False
        self.root.protocol("WM_DELETE_WINDOW", self._request_close)
        try:
            Path(self.output_dir.get()).mkdir(parents=True, exist_ok=True)
        except OSError:
            # The default is convenient, but the app should still open when the
            # OS or a managed environment blocks Documents; users can pick a folder.
            pass
        self._style()
        self._build()

    def _style(self):
        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")
        self.colors = {
            "canvas": "#0C1421", "surface": "#141D2B", "surface_soft": "#192536",
            "ink": "#F4F6FA", "muted": "#A5B1C2", "line": "#2A3A4E",
            "accent": "#16B6E5", "accent_hover": "#42C9EF", "accent_soft": "#20354A",
            "green": "#4AC9A7", "hero": "#141F30", "hero_muted": "#B2BECE",
        }

    def _build(self):
        c = self.colors
        entry = {"height":40, "corner_radius":9, "border_width":1, "fg_color":"#0C1725", "border_color":"#304158", "text_color":"#E7EDF5", "font":("Segoe UI", 9)}
        self.backdrop = tk.Canvas(self.root, bg=c["canvas"], highlightthickness=0, bd=0)
        self.backdrop.place(x=0, y=0, relwidth=1, relheight=1)
        self.backdrop.bind("<Configure>", self._paint_backdrop)
        outer = ctk.CTkFrame(self.root, fg_color="transparent", corner_radius=0)
        outer.pack(fill="both", expand=True, padx=24, pady=(20, 12))

        header = ctk.CTkFrame(outer, fg_color="transparent", corner_radius=0, height=102)
        header.pack(fill="x", pady=(0, 14))
        header.pack_propagate(False)
        self.header_edge = tk.Canvas(header, bg=c["canvas"], highlightthickness=0, bd=0)
        self.header_edge.place(x=0, y=0, relwidth=1, relheight=1)
        self._header_phase = 0.0
        self._header_last_tick = time.monotonic()
        self._header_glow_pixels = []
        self._header_glow_size = (0, 0)
        self._header_glow_photo = None
        self._header_glow_item = None
        self.header_edge.bind("<Configure>", self._paint_header_edge)
        self.ps_logo_source = tk.PhotoImage(file=str(RESOURCE_DIR / "playstation-logo.png"))
        self.ps_logo_image = self.ps_logo_source.subsample(15, 15)
        # Match the header fill explicitly: a transparent CTk frame inherits
        # the darker application canvas, which creates a visible rectangular
        # patch behind the logo and title.
        brand = ctk.CTkFrame(header, fg_color=c["hero"], corner_radius=0)
        brand.pack(side="left", padx=(88, 0))
        title = ctk.CTkFrame(brand, fg_color=c["hero"], corner_radius=0)
        title.pack(side="left", anchor="center")
        ctk.CTkLabel(title, text="PS1 MEMORY CARD STUDIO" if self.ps1_mode else "PS2 MEMORY CARD STUDIO", text_color="#66CDEA", font=("Segoe UI", 10, "bold"), height=16).pack(anchor="w", pady=(0, 4))
        ctk.CTkLabel(title, text="Your PS1 saves, all in one place." if self.ps1_mode else "Your PS2 saves, all in one place.", text_color="#F5F7FA", font=("Segoe UI Semibold", 23), height=30).pack(anchor="w")
        ctk.CTkLabel(title, text="Prepare PS1 saves for Apollo in a few steps." if self.ps1_mode else "Prepare PS2 saves for Apollo in a few steps.", text_color=c["hero_muted"], font=("Segoe UI", 13), height=18).pack(anchor="w", pady=(3, 0))
        header_actions = ctk.CTkFrame(header, fg_color=c["hero"], corner_radius=0)
        header_actions.pack(side="right", padx=22)
        ctk.CTkButton(header_actions, text="About", command=self.show_about, width=84, height=34, corner_radius=8, fg_color="#243247", hover_color="#30435D", text_color="#DCE6F3", font=("Segoe UI", 10)).pack(anchor="e")
        ctk.CTkButton(
            header_actions,
            text="LOCAL & PRIVATE",
            command=self.show_privacy_info,
            width=120,
            height=32,
            corner_radius=8,
            fg_color="#20354A",
            hover_color="#2A4963",
            text_color="#A5DDF0",
            font=("Segoe UI", 8, "bold"),
        ).pack(anchor="e", pady=(8, 0))

        body = ctk.CTkFrame(outer, fg_color="transparent", corner_radius=0)
        body.pack(fill="both", expand=True)
        body.grid_columnconfigure((0, 1), weight=1, uniform="main")
        body.grid_rowconfigure(0, weight=1)
        left_column = ctk.CTkFrame(body, fg_color="transparent")
        left_column.grid(row=0, column=0, sticky="nsew", padx=(0, 8))
        right_column = ctk.CTkFrame(body, fg_color="transparent")
        right_column.grid(row=0, column=1, sticky="nsew", padx=(8, 0))
        left = ctk.CTkFrame(left_column, fg_color=c["surface"], corner_radius=14, border_width=1, border_color=c["line"])
        left.pack(fill="both", expand=True, pady=(0, 14))
        right = ctk.CTkFrame(right_column, fg_color=c["surface"], corner_radius=14, border_width=1, border_color=c["line"])
        right.pack(fill="x", pady=(0, 14))

        def label(parent, text, size=10, color=None, bold=False, **kwargs):
            font = ("Segoe UI Semibold" if bold else "Segoe UI", size)
            return ctk.CTkLabel(parent, text=text, text_color=color or c["ink"], font=font, **kwargs)

        def step(parent, text, color="#65CBEA", pill=None):
            label(parent, text, 10, color, True).pack(anchor="w", padx=20, pady=(16, 5))

        step(left, "01   SOURCE FILES")
        label(left, "Choose your saves", 17, bold=True).pack(anchor="w", padx=22)
        label(left, "Combine MCS saves or raw PS1 cards on one card." if self.ps1_mode else "Combine PSU, MAX, CBS, SPS, XPS, and PSV saves on one card.", 10, c["muted"]).pack(anchor="w", padx=22, pady=(4, 14))
        actions = ctk.CTkFrame(left, fg_color="transparent")
        actions.pack(fill="x", padx=22, pady=(0, 10))
        ctk.CTkButton(actions, text="+  Add save files", command=self.choose_saves, width=150, height=36, corner_radius=12, fg_color=c["accent"], hover_color=c["accent_hover"], text_color="#081824", font=("Segoe UI Semibold", 10)).pack(side="left")
        ctk.CTkButton(actions, text="Clear", command=self.clear_saves, width=72, height=36, corner_radius=12, fg_color="#243247", hover_color="#30435D", text_color="#CAD5E3", font=("Segoe UI", 10)).pack(side="left", padx=(8, 0))
        self.file_count_label = label(actions, "0 FILES", 9, "#91A1B5", True)
        self.file_count_label.pack(side="right")
        list_wrap = ctk.CTkFrame(left, fg_color="#101927", corner_radius=10, border_width=1, border_color="#25364B")
        list_wrap.pack(fill="both", expand=True, padx=22, pady=(0, 16))
        self.save_list = tk.Listbox(list_wrap, height=6, selectmode="extended", exportselection=False, bg="#101927", fg="#E7EDF5", selectbackground="#14617B", selectforeground="#FFFFFF", relief="flat", highlightthickness=0, font=("Segoe UI", 10), activestyle="none", bd=0)
        self.save_list.pack(fill="both", expand=True, padx=10, pady=8)
        self.empty_hint = ctk.CTkLabel(list_wrap, text="No save files selected", text_color="#8FA5BA", font=("Segoe UI", 11))
        self.empty_hint.place(relx=0.5, rely=0.5, anchor="center")

        left = ctk.CTkFrame(left_column, fg_color=c["surface"], corner_radius=14, border_width=1, border_color=c["line"])
        left.pack(fill="x")
        step(left, "02   CARD FORMAT")
        label(left, "Set up the virtual card", 16, bold=True).pack(anchor="w", padx=22)
        label(left, "Choose the PS1 card image format." if self.ps1_mode else "Pick the card encoding and capacity.", 10, c["muted"]).pack(anchor="w", padx=22, pady=(4, 13))
        fields = ctk.CTkFrame(left, fg_color="transparent")
        fields.pack(fill="x", padx=22)
        fields.grid_columnconfigure((0, 1), weight=1, uniform="format")
        label(fields, "CARD FORMAT" if self.ps1_mode else "ECC FORMAT", 8, "#8798AD", True).grid(row=0, column=0, sticky="w", pady=(0, 7))
        label(fields, "CARD CAPACITY", 8, "#8798AD", True).grid(row=0, column=1, sticky="w", padx=(12, 0), pady=(0, 7))
        combo = {"height":40, "corner_radius":9, "border_width":1, "fg_color":"#0C1725", "border_color":"#304158", "button_color":"#2B3B51", "button_hover_color":"#3B506A", "dropdown_fg_color":"#192638", "dropdown_hover_color":"#2A3D56", "dropdown_text_color":"#F2F5F8", "text_color":"#F2F5F8", "font":("Segoe UI", 10)}
        self.type_box = ctk.CTkComboBox(fields, variable=self.card_type, values=("MCR", "VM1", "MCD", "VMC") if self.ps1_mode else ("ECC", "Non-ECC"), state="readonly", command=lambda _v: self._card_options_changed(), **combo)
        self.type_box.grid(row=1, column=0, sticky="ew")
        self.size_box = ctk.CTkComboBox(fields, variable=self.card_size, values=("128 KiB",) if self.ps1_mode else tuple(f"{n} MiB" for n in CARD_MIB), state="readonly", command=lambda _v: self._card_options_changed(), **combo)
        self.size_box.grid(row=1, column=1, sticky="ew", padx=(12, 0))
        for combo_box in (self.type_box, self.size_box):
            self._install_clean_combo_arrow(combo_box)
        label(left, "BLANK CARD TEMPLATES  ·  INCLUDED", 8, "#8798AD", True).pack(anchor="w", padx=22, pady=(18, 7))
        label(left, "A blank PS1 card is included: 15 usable save blocks." if self.ps1_mode else "ECC and Non-ECC blank cards for 8, 16, 32, and 64 MiB are bundled with the app.", 9, "#A5B1C2", wraplength=440, justify="left").pack(anchor="w", padx=22, pady=(0, 18))

        step(right, "03   DESTINATION", "#82BFFF", "#20334A")
        label(right, "Save your new card", 17, bold=True).pack(anchor="w", padx=22)
        label(right, "All selected saves will be combined into this card.", 10, c["muted"]).pack(anchor="w", padx=22, pady=(4, 11))
        label(right, "OUTPUT FOLDER  ·  SAVED AS DEFAULT", 8, "#8798AD", True).pack(anchor="w", padx=22, pady=(0, 7))
        output_row = ctk.CTkFrame(right, fg_color="transparent")
        output_row.pack(fill="x", padx=22)
        ctk.CTkEntry(output_row, textvariable=self.output_dir, **entry).pack(side="left", fill="x", expand=True, padx=(0, 9))
        ctk.CTkButton(output_row, text="Browse", command=self.choose_output_dir, width=76, height=40, corner_radius=11, fg_color="#28384D", hover_color="#354A65", text_color="#E7EDF5", font=("Segoe UI", 9)).pack(side="right")
        label(right, "CARD FILENAME", 8, "#8798AD", True).pack(anchor="w", padx=22, pady=(14, 7))
        ctk.CTkEntry(right, textvariable=self.output_name, **entry).pack(fill="x", padx=22, pady=(0, 18))

        right = ctk.CTkFrame(right_column, fg_color=c["surface"], corner_radius=14, border_width=1, border_color=c["line"])
        right.pack(fill="both", expand=True)
        step(right, "CARD OVERVIEW")
        preview_frame = ctk.CTkFrame(right, fg_color="#192536", corner_radius=18, border_width=1, border_color="#314258", height=212)
        preview_frame.pack(fill="x", padx=22, pady=(12, 0))
        preview_frame.pack_propagate(False)
        self.preview = tk.Canvas(preview_frame, height=204, bg="#192536", highlightthickness=0, bd=0)
        self.preview.pack(fill="both", expand=True, padx=5, pady=5)
        self.preview.bind("<Configure>", lambda _event: self._draw_card_preview())
        details = ctk.CTkFrame(right, fg_color="transparent")
        details.pack(fill="x", padx=22, pady=(8, 0))
        label(details, "PS1 raw cards  ·  .MCR / .VM1 / .MCD / .VMC" if self.ps1_mode else "ECC → .PS2    ·    Non-ECC → .VMC", 9, "#D7E0EB").pack(anchor="w")
        label(details, "Apollo scans memory cards in  /PS1/VMC/" if self.ps1_mode else "Apollo scans memory cards in  /PS2/VMC/", 9, "#8798AD").pack(anchor="w", pady=(4, 0))
        self.import_button = ctk.CTkButton(right, text="Create Virtual Memory Card", command=self.start_import, height=44, corner_radius=10, fg_color=c["accent"], hover_color=c["accent_hover"], text_color="#081824", font=("Segoe UI Semibold", 12))
        self.import_button.pack(fill="x", padx=22, pady=(10, 8))

        # Retain a quiet bottom edge without the redundant status/tagline row.
        ctk.CTkFrame(outer, height=1, fg_color="#29384B", corner_radius=0).pack(
            fill="x", pady=(8, 0))
        self._refresh_save_list()
        if self.ps1_mode:
            self._suggest_filename()
        self._draw_card_preview()
        self.root.after(70, self._animate_header_glow)

    @staticmethod
    def _install_clean_combo_arrow(combo_box):
        """Use a crisp Canvas chevron in place of the tiny custom-font glyph."""
        canvas = combo_box._canvas

        def draw_arrow(x_position, y_position, size):
            x_position, y_position, size = round(x_position), round(y_position), round(size)
            items = canvas.find_withtag("dropdown_arrow")
            requires_recoloring = False
            if not items or canvas.type(items[0]) != "line":
                canvas.delete("dropdown_arrow")
                canvas.create_line(
                    0, 0, 0, 0,
                    tags="dropdown_arrow",
                    width=max(2, round(size / 4)),
                    joinstyle=tk.ROUND,
                    capstyle=tk.ROUND,
                )
                canvas.tag_raise("dropdown_arrow")
                requires_recoloring = True

            canvas.coords(
                "dropdown_arrow",
                x_position - size * 0.45, y_position - size * 0.12,
                x_position, y_position + size * 0.28,
                x_position + size * 0.45, y_position - size * 0.12,
            )
            return requires_recoloring

        # Keep this replacement scoped to the two format selectors, so all
        # other CustomTkinter widgets retain their standard rendering.
        combo_box.draw_engine.draw_dropdown_arrow = draw_arrow
        combo_box._draw()

    def _paint_header_edge(self, _event=None):
        """Paint a complete rounded header capsule with its muted base edge."""
        canvas = self.header_edge
        width, height = canvas.winfo_width(), canvas.winfo_height()
        if width < 4 or height < 4:
            return
        if self._header_glow_item is not None and self._header_glow_size == (width, height):
            return
        left, top, right, bottom = 1.0, 1.0, width-1.0, height-1.0
        radius = min(23.0, (bottom-top)/2, (right-left)/2)
        points = [(left+radius, top), (right-radius, top)]
        corners = ((right-radius, top+radius, -90, 0),
                   (right-radius, bottom-radius, 0, 90),
                   (left+radius, bottom-radius, 90, 180),
                   (left+radius, top+radius, 180, 270))
        for cx, cy, start, end in corners:
            points.extend((cx+radius*math.cos(math.radians(a)),
                           cy+radius*math.sin(math.radians(a)))
                          for a in range(start+10, end+1, 10))
            if end == 0:
                points.append((right, bottom-radius))
            elif end == 90:
                points.append((left+radius, bottom))
            elif end == 180:
                points.append((left, top+radius))
        coords = [v for point in points for v in point]
        canvas.delete("all")
        canvas.create_polygon(*coords, fill=self.colors["hero"], outline="")
        if getattr(self, "ps_logo_image", None) is not None:
            canvas.create_image(92, height / 2, image=self.ps_logo_image,
                                anchor="center", tags="brand_logo")
        closed = coords + coords[:2]
        canvas.create_line(*closed, fill="#29384B", width=1.4, joinstyle=tk.ROUND)
        shadow_coords = []
        for x, y in points:
            shadow_coords.extend((x + 0.5, y + 1.6))
        canvas.create_line(*(shadow_coords + shadow_coords[:2]), fill="#080D18",
                           width=8, joinstyle=tk.ROUND, tags="header_shadow")
        self._build_header_glow_geometry(width, height, radius)
        self._header_glow_item = canvas.create_image(0, 0, anchor="nw", tags="glow")
        self._draw_header_glow()

    def _draw_header_glow(self):
        if not self._header_glow_pixels:
            return
        canvas = self.header_edge
        # Draw one PNG stroke for the whole rounded edge. Tk's canvas has no
        # gradient stroke, and stitching many colored lines leaves visible
        # joins. The transparent PNG is generated with the standard library.
        # Precompute the gradient once per frame rather than recalculating
        # trigonometry, easing and alpha for every border pixel.
        palette = ((230, 0, 45), (255, 203, 5), (0, 169, 157), (22, 139, 250), (230, 0, 45))
        if not hasattr(self, "_glow_color_table"):
            table = []
            for i in range(1025):
                index = min(i // 256, 3)
                amount = (i - index * 256) / 256
                table.append(tuple(round(palette[index][j] + (palette[index+1][j] - palette[index][j]) * amount) for j in range(3)))
            self._glow_color_table = table
        table = self._glow_color_table
        cosine, sine = math.cos(self._header_phase), math.sin(self._header_phase)
        width, height = self._header_glow_size
        row_bytes = width * 4
        scanlines = bytearray((row_bytes + 1) * height)
        for pixel, nx, ny, alpha in self._header_glow_pixels:
            color_index = max(0, min(1024, int((nx * cosine + ny * sine + 0.5) * 1024)))
            r, g, b = table[color_index]
            scanlines[pixel] = r
            scanlines[pixel + 1] = g
            scanlines[pixel + 2] = b
            scanlines[pixel + 3] = alpha

        png_data = self._encode_rgba_png(width, height, scanlines)
        self._header_glow_photo = tk.PhotoImage(data=png_data, format="png",
                                                master=canvas)
        canvas.itemconfigure(self._header_glow_item, image=self._header_glow_photo)

    @staticmethod
    def _encode_rgba_png(width, height, scanlines):
        def chunk(kind, payload):
            checksum = zlib.crc32(kind + payload) & 0xFFFFFFFF
            return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)

        header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
        return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) +
                chunk(b"IDAT", zlib.compress(scanlines, level=1)) + chunk(b"IEND", b""))

    def _build_header_glow_geometry(self, width, height, corner):
        """Build the glow mask from the distance to the rounded rectangle."""
        self._header_glow_size = (width, height)
        reach = 8
        side_width = min(width // 2, math.ceil(1 + corner + reach))
        opacity = [round(255 * max(math.exp(-0.5 * (d / 64 / 1.9) ** 2),
                                   0.58 * math.exp(-0.5 * (d / 64 / 4.7) ** 2)))
                   for d in range(reach * 64 + 1)]
        # qx and qy are signed offsets from the rectangle of corner centers.
        qx = [abs(x + 0.5 - width / 2) - (width / 2 - 1 - corner) for x in range(width)]
        glow_pixels = []
        for y in range(height):
            qy = abs(y + 0.5 - height / 2) - (height / 2 - 1 - corner)
            if min(y, height - 1 - y) > reach + 1 and width > 2 * side_width + 1:
                spans = (range(side_width), range(width - side_width, width))
            else:
                spans = (range(width),)
            ny = y / height - 0.5
            row = y * (width * 4 + 1) + 1
            for span in spans:
                for x in span:
                    dx = qx[x]
                    if dx > 0 and qy > 0:
                        distance = abs(math.hypot(dx, qy) - corner)
                    else:
                        distance = abs(max(dx, qy) - corner)
                    if distance <= reach:
                        glow_pixels.append((row + x * 4, x / width - 0.5, ny,
                                            opacity[round(distance * 64)]))
        self._header_glow_pixels = glow_pixels

    def _animate_header_glow(self):
        if not self.root.winfo_exists():
            return
        now = time.monotonic()
        elapsed = min(now - self._header_last_tick, 0.25)
        self._header_last_tick = now
        visible = self.root.winfo_viewable()
        if visible and sys.platform == "win32":
            # A Tk window can remain mapped when its host tab is hidden.
            visible = bool(ctypes.windll.user32.IsWindowVisible(self.root.winfo_id()))
        if visible and self._header_glow_pixels:
            self._header_phase = (self._header_phase + 2 * math.pi * elapsed / 8.0) % (2 * math.pi)
            self._draw_header_glow()
        self.root.after(40 if visible else 300, self._animate_header_glow)

    def _paint_backdrop(self, _event=None):
        canvas = getattr(self, "backdrop", None)
        if canvas is None:
            return
        canvas.delete("all")
        canvas.configure(bg=self.colors["canvas"])

    def choose_saves(self):
        paths = filedialog.askopenfilenames(
            title="Select PS1 saves or raw memory cards" if self.ps1_mode else "Select one or more PS2 save files",
            filetypes=([("PS1 MCS saves and raw cards", "*.mcs *.mcr *.mcd *.vm1 *.vmc *.bin *.srm *.ps *.mci *.psm *.ddf"), ("All files", "*.*")] if self.ps1_mode else [("PS2 save files", "*.psu *.max *.cbs *.sps *.xps *.psv"), ("All files", "*.*")]),
        )
        if paths:
            existing = {path.resolve() for path in self.save_paths}
            for value in paths:
                path = Path(value)
                if path.resolve() not in existing:
                    self.save_paths.append(path)
                    existing.add(path.resolve())
            self._refresh_save_list()
            self._suggest_filename()

    def show_about(self):
        dialog = ctk.CTkToplevel(self.root)
        dialog.title("About PS1 Memory Card Studio" if self.ps1_mode else "About PS2 Memory Card Studio")
        dialog.transient(self.root)
        dialog.geometry("560x470")
        dialog.minsize(500, 400)
        dialog.configure(fg_color="#0B101B")
        frame = ctk.CTkFrame(dialog, fg_color="#151F2E", corner_radius=20)
        frame.pack(fill="both", expand=True)
        ctk.CTkLabel(frame, text="PS1 Memory Card Studio" if self.ps1_mode else "PS2 Memory Card Studio", text_color="#F4F6FA", font=("Segoe UI Semibold", 22)).pack(anchor="w", padx=24, pady=(22, 0))
        ctk.CTkLabel(frame, text="A local tool for building PS1 virtual memory cards." if self.ps1_mode else "A local tool for building PS2 virtual memory cards.", text_color="#A5B1C2", font=("Segoe UI", 10)).pack(anchor="w", padx=24, pady=(5, 15))
        box = ctk.CTkTextbox(frame, wrap="word", fg_color="#101927", text_color="#DCE5EF", border_color="#2A3A4E", border_width=1, corner_radius=13, font=("Segoe UI", 10))
        box.pack(fill="both", expand=True, padx=24)
        box.insert("1.0", ABOUT_CREDITS + "\n\n" + ABOUT_LICENSE)
        box.configure(state="disabled")
        ctk.CTkButton(frame, text="Close", command=dialog.destroy, height=38, corner_radius=11, fg_color="#28384D", hover_color="#354A65").pack(anchor="e", padx=24, pady=18)
        dialog.grab_set()

    def show_privacy_info(self):
        dialog = ctk.CTkToplevel(self.root)
        dialog.title("Local & Private")
        dialog.transient(self.root)
        dialog.geometry("460x300")
        dialog.resizable(False, False)
        dialog.configure(fg_color="#0B101B")

        frame = ctk.CTkFrame(dialog, fg_color="#151F2E", corner_radius=20,
                             border_width=1, border_color="#2A3A4E")
        frame.pack(fill="both", expand=True, padx=10, pady=10)
        ctk.CTkLabel(
            frame,
            text="Your saves stay on this device",
            text_color="#F4F6FA",
            font=("Segoe UI Semibold", 19),
        ).pack(anchor="w", padx=24, pady=(22, 8))
        ctk.CTkLabel(
            frame,
            text=(
                "PS2 Memory Card Studio reads the save files you select and "
                "creates the virtual card in the output folder you choose. "
                "Save contents are processed on this computer and are not "
                "uploaded to an online service. Your output-folder choice is "
                "remembered locally for next time."
            ),
            text_color="#B7C3D2",
            font=("Segoe UI", 11),
            wraplength=390,
            justify="left",
        ).pack(anchor="w", padx=24, pady=(0, 14))
        ctk.CTkLabel(
            frame,
            text="Selected saves  ·  converted locally  ·  card saved where you choose",
            text_color="#6BD5B5",
            font=("Segoe UI", 9, "bold"),
            wraplength=390,
            justify="left",
        ).pack(anchor="w", padx=24)
        ctk.CTkButton(
            frame,
            text="Got it",
            command=dialog.destroy,
            width=90,
            height=36,
            corner_radius=11,
            fg_color="#28384D",
            hover_color="#354A65",
        ).pack(anchor="e", padx=24, pady=18)
        dialog.grab_set()

    def clear_saves(self):
        self.save_paths.clear()
        self._refresh_save_list()
        self._suggest_filename()

    def _refresh_save_list(self):
        self.save_list.delete(0, tk.END)
        if hasattr(self, "empty_hint"):
            if self.save_paths:
                self.empty_hint.place_forget()
            else:
                self.empty_hint.place(relx=0.5, rely=0.5, anchor="center")
        for path in self.save_paths:
            self.save_list.insert(tk.END, path.name)
        if hasattr(self, "file_count_label"):
            count = len(self.save_paths)
            self.file_count_label.configure(text=f"{count} FILE" if count == 1 else f"{count} FILES")

    def choose_output_dir(self):
        folder = filedialog.askdirectory(
            title="Choose where new PS1 cards will be saved" if self.ps1_mode else "Choose where new PS2 cards will be saved",
            initialdir=self.output_dir.get(),
            mustexist=True,
        )
        if folder:
            self.output_dir.set(folder)
            self._remember_output_dir(Path(folder))

    def _remember_output_dir(self, folder: Path):
        """Save the chosen folder so it remains the default next time."""
        try:
            SETTINGS_DIR.mkdir(parents=True, exist_ok=True)
            temp_path = OUTPUT_DIR_PREFERENCE.with_suffix(".tmp")
            temp_path.write_text(str(folder.resolve()), encoding="utf-8")
            os.replace(temp_path, OUTPUT_DIR_PREFERENCE)
        except OSError:
            # Folder selection and card creation remain usable if preferences
            # cannot be written in a managed or portable environment.
            pass

    def _suggest_filename(self):
        if len(self.save_paths) == 1:
            stem = self.save_paths[0].stem
        elif len(self.save_paths) > 1:
            stem = f"{len(self.save_paths)}_saves"
        else:
            stem = "new_save"
        if self.ps1_mode:
            self.output_name.set(f"{stem}_PS1.{self.card_type.get().lower()}")
            return
        size = int(self.card_size.get().split()[0])
        with_ecc = self.card_type.get() == "ECC"
        extension = ".ps2" if with_ecc else ".vmc"
        self.output_name.set(f"{stem}_{size}MiB_{'ECC' if with_ecc else 'NoECC'}{extension}")

    def _card_options_changed(self):
        self._suggest_filename()
        self._draw_card_preview()

    def _draw_card_preview(self):
        canvas = getattr(self, "preview", None)
        if canvas is None or not canvas.winfo_exists():
            return
        canvas.delete("all")
        if self.ps1_mode:
            if not hasattr(self, "ps1_card_source"):
                self.ps1_card_source = tk.PhotoImage(data=base64.b64encode((RESOURCE_DIR / "ps1-memory-card.png").read_bytes()), format="png")
                self.ps1_card_image = self.ps1_card_source.subsample(4, 4)
            text_x = 326
            # Center the artwork in the space between the left edge and the
            # summary, and vertically between the top and bottom edges.
            canvas.create_image(text_x / 2, canvas.winfo_height() / 2,
                                image=self.ps1_card_image, anchor="center")
            canvas.create_text(text_x,42,text="PS1 MEMORY CARD",anchor="w",fill="#8ABFFF",font=("Segoe UI Semibold",-23))
            canvas.create_text(text_x,87,text="128 KiB",anchor="w",fill="#F2F5F8",font=("Segoe UI Semibold",-46))
            canvas.create_text(text_x,125,text=f"{self.card_type.get()} FORMAT · 15 BLOCKS",anchor="w",fill="#AFBDCD",font=("Segoe UI",-20))
            canvas.create_oval(text_x,166,text_x+11,177,fill="#4AC9A7",outline="")
            canvas.create_text(text_x+19,172,text="Ready for save import",anchor="w",fill="#A8B8C8",font=("Segoe UI",-17))
            return
        if not hasattr(self, "card_art_source"):
            self.card_art_source = tk.PhotoImage(file=str(RESOURCE_DIR / "ps2-memory-card.png"))
            self.card_art_image = self.card_art_source.subsample(4, 4)
        x, y = 28, 12
        # This is the user's transparent card image, including its molded
        # texture and multicolor PS logo. Keep the rendered cutout crisp.
        canvas.create_image(x, y, image=self.card_art_image, anchor="nw")
        capacity = self.card_size.get().split()[0]
        # The source image's original 8 MB ink was cleared. Draw the chosen
        # capacity back into that spot on every format or size change.
        canvas.create_text(x+128, y+109, text=capacity, anchor="center", fill="#C3C6CB", font=("Segoe UI Semibold", -42))
        canvas.create_text(x+166, y+113, text="MB", anchor="center", fill="#AEB4BC", font=("Segoe UI Semibold", -17))

        text_x = x+270+28
        canvas.create_text(text_x, 42, text="VIRTUAL MEMORY CARD", anchor="w", fill="#8ABFFF", font=("Segoe UI Semibold",-23))
        canvas.create_text(text_x, 87, text=f"{capacity} MiB", anchor="w", fill="#F2F5F8", font=("Segoe UI Semibold",-46))
        canvas.create_text(text_x, 125, text=f"{self.card_type.get().upper()} FORMAT", anchor="w", fill="#AFBDCD", font=("Segoe UI",-20))
        canvas.create_oval(text_x,166,text_x+11,177,fill="#4AC9A7",outline="")
        canvas.create_text(text_x+19,172,text="Ready for save import",anchor="w",fill="#A8B8C8",font=("Segoe UI",-17))

    def start_import(self):
        if self.busy:
            return
        if not self.save_paths and not self.ps1_mode:
            messagebox.showerror("Choose saves", "Add one or more .PSU, .MAX, .CBS, .SPS, .XPS, or .PSV save files first.")
            return
        missing = [path.name for path in self.save_paths if not path.is_file()]
        if missing:
            messagebox.showerror("Save file unavailable", "These selected files are no longer available:\n\n" + "\n".join(missing))
            return
        size = 0 if self.ps1_mode else int(self.card_size.get().split()[0])
        with_ecc = self.card_type.get() == "ECC"
        extension = "." + self.card_type.get().lower() if self.ps1_mode else (".ps2" if with_ecc else ".vmc")
        output_folder = Path(self.output_dir.get()).expanduser()
        filename = self.output_name.get().strip()
        if not output_folder.is_dir():
            messagebox.showerror("Choose an output folder", "Select an existing folder where you have permission to save files.")
            return
        self._remember_output_dir(output_folder)
        if not filename or filename in {".", ".."} or Path(filename).name != filename or "/" in filename or "\\" in filename:
            messagebox.showerror("Check the card filename", "Enter a filename without folder paths.")
            return
        output_path = output_folder / filename
        if output_path.suffix.lower() != extension:
            messagebox.showerror(
                "Check the card filename",
                f"This card type should be saved with the {extension} extension.",
            )
            return
        if output_path.exists():
            if not messagebox.askyesno("Replace existing file?", "A file with that name exists. Continue? A backup will be created first."):
                return
        self.busy = True
        self.import_button.configure(state="disabled")
        self.status.set(f"Creating the card and importing {len(self.save_paths)} save(s)…")
        template_dir = Path(self.template_dir.get())
        thread = threading.Thread(
            target=self._worker,
            args=(self.save_paths.copy(), size, with_ecc, template_dir, output_path),
            daemon=True,
        )
        thread.start()

    def _request_close(self):
        if self.busy:
            self.close_after_import = True
            self.status.set("Finishing the memory card before closing…")
        else:
            self.root.destroy()

    def _worker(self, save_paths, size, with_ecc, template_dir, output_path):
        try:
            result = ps1card.create_ps1(save_paths, template_dir / "blank-ps1.mcr", output_path) if self.ps1_mode else create_and_import(save_paths, size, with_ecc, template_dir, output_path)
            self.root.after(0, self._finish_import, self._success, result, output_path, size, with_ecc)
        except Exception as exc:
            detail = str(exc).strip() or traceback.format_exc()
            self.root.after(0, self._finish_import, self._failure, detail)

    def _finish_import(self, report, *args):
        report(*args)
        if self.close_after_import and self.root.winfo_exists():
            self.root.destroy()

    def _success(self, result, output_path, size, with_ecc):
        self.busy = False
        self.import_button.configure(state="normal")
        if self.ps1_mode:
            self.status.set(f"Done · imported {len(result['imported'])} save(s) · {output_path.name}")
            messagebox.showinfo("PS1 card created", f"Imported {len(result['imported'])} save(s).\nFree blocks: {result['free'] // 8192} of 15\n\nSaved to:\n{output_path}\n\nFor Apollo, copy the card to /PS1/VMC/.")
            return
        template_note = f"Template: {result['template'].name.removesuffix('.gz')}" if result["template"] else "A new blank card was formatted."
        backup_note = f"\nBackup: {result['backup']}" if result["backup"] else ""
        imported_count = len(result["imported"])
        failure_count = len(result["failures"])
        self.status.set(f"Done · imported {imported_count} save(s) · {output_path.name}")
        imported_lines = "\n".join(f"• {item['file']} → {item['directory']}" for item in result["imported"])
        failures_text = ""
        if result["failures"]:
            failures_text = "\n\nNot imported:\n" + "\n".join(f"• {item}" for item in result["failures"])
        messagebox.showinfo(
            "Card created" if failure_count == 0 else "Card created with some saves skipped",
            f"Imported {imported_count} of {imported_count + failure_count} selected save(s).\n\n"
            f"Card: {size} MiB · {'ECC' if with_ecc else 'Non-ECC'}\n"
            f"Free space: {result['free'] // 1024:,} KiB\n"
            f"{template_note}\n\n"
            f"Imported saves:\n{imported_lines}{failures_text}\n\n"
            f"Saved to:\n{output_path}{backup_note}\n\n"
            "Copy the card to your USB drive under /PS2/VMC/ for Apollo.",
        )

    def _failure(self, detail):
        self.busy = False
        self.import_button.configure(state="normal")
        self.status.set("Import failed · the output file was not changed")
        if "PermissionError" in detail or "Access is denied" in detail or "Permission denied" in detail:
            detail += (
                "\n\nIf you launched this copy from inside Codex, its workspace sandbox only "
                "allows writes in the workspace. Choose the app's Cards folder, or launch "
                "Start PS2 Memory Card Studio.cmd from File Explorer to save outside Codex. "
                "If you launched it independently and still see this, check that folder's "
                "Windows permissions and Controlled folder access settings."
            )
        messagebox.showerror("Could not create card", detail)


def main():
    log_path = Path(tempfile.gettempdir()) / "PS2MemoryCardStudio-startup.log"

    def log_exception(exc_type, exc_value, exc_traceback):
        with log_path.open("a", encoding="utf-8") as log:
            traceback.print_exception(exc_type, exc_value, exc_traceback, file=log)

    try:
        root = ctk.CTk()
        root.report_callback_exception = log_exception
        PS2MemoryCardStudio(root)
        root.mainloop()
    except Exception as exc:
        log_exception(type(exc), exc, exc.__traceback__)
        try:
            messagebox.showerror(
                "PS2 Memory Card Studio could not start",
                f"The startup error was recorded here:\n{log_path}",
            )
        except Exception:
            pass


if __name__ == "__main__":
    main()
