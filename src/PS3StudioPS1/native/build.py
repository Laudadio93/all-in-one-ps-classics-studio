"""Rebuild the embedded x64 module with MinGW-w64 GCC and CPython headers."""
import os
from pathlib import Path
import shutil
import subprocess
import sysconfig
import tempfile
from zipfile import ZipFile

here = Path(__file__).resolve().parent
resources = here.parent / "Resources"
compiler = os.environ.get("CC", "gcc")
headers = sysconfig.get_path("include")
with tempfile.TemporaryDirectory() as temp:
    build = Path(temp)
    with ZipFile(resources / "pkg-writer.zip") as archive:
        libraries = [archive.extract("_internal/" + name, build)
                     for name in ("python310.dll", "libcrypto-1_1.dll")]
    output = build / "ps3-pkgcrypt.pyd"
    subprocess.run([compiler, "-m64", "-std=c11", "-O3", "-Wall", "-Wextra",
                    "-Wno-missing-field-initializers", "-s", "-shared", "-static-libgcc",
                    "-Wl,--no-insert-timestamp", "-I" + headers, here / "pkgcrypt.c",
                    *libraries, "-o", output.name], cwd=build, check=True)
    shutil.copyfile(output, resources / output.name)
