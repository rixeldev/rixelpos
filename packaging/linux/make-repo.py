#!/usr/bin/env python3
"""Builds a minimal pacman repository from one or more .pkg.tar.zst files.

GitHub Pages friendly: writes rixelpos.db, rixelpos.files and the packages.
No pacman/repo-add dependency is needed; bsdtar is used to read .PKGINFO.
"""
import argparse
import hashlib
import io
import os
import shutil
import subprocess
import tarfile
import time
from pathlib import Path

KEYS = ("pkgname", "pkgbase", "pkgver", "pkgrel", "pkgdesc", "url", "builddate",
        "packager", "size", "arch", "license", "depend", "optdepend", "provides",
        "conflicts", "replaces")


def read_pkginfo(package: Path) -> dict[str, list[str]]:
    for command in (["bsdtar", "-xOf", str(package), ".PKGINFO"],
                    ["tar", "--zstd", "-xOf", str(package), ".PKGINFO"]):
        try:
            output = subprocess.check_output(command, stderr=subprocess.DEVNULL, text=True)
            break
        except (OSError, subprocess.CalledProcessError):
            continue
    else:
        raise RuntimeError(f"Cannot read .PKGINFO from {package}")

    info: dict[str, list[str]] = {}
    for line in output.splitlines():
        if " = " not in line:
            continue
        key, value = line.split(" = ", 1)
        if key in KEYS:
            info.setdefault(key, []).append(value)
    return info


def list_files(package: Path) -> list[str]:
    try:
        output = subprocess.check_output(["bsdtar", "-tf", str(package)], stderr=subprocess.DEVNULL, text=True)
    except (OSError, subprocess.CalledProcessError):
        try:
            output = subprocess.check_output(["tar", "--zstd", "-tf", str(package)], stderr=subprocess.DEVNULL, text=True)
        except (OSError, subprocess.CalledProcessError):
            return []
    return [line for line in output.splitlines() if line and line not in (".PKGINFO", ".INSTALL")]


def desc_text(name: str, version: str, filename: str, size: int, sha: str, info: dict[str, list[str]]) -> str:
    def block(key: str, values: list[str]) -> str:
        if not values:
            return ""
        return f"%{key}%\n" + "\n".join(values) + "\n\n"

    desc = ""
    desc += f"%FILENAME%\n{filename}\n\n"
    desc += f"%NAME%\n{name}\n\n"
    desc += f"%BASE%\n{info.get('pkgbase', [name])[0]}\n\n"
    desc += f"%VERSION%\n{version}\n\n"
    desc += block("DESC", info.get("pkgdesc", []))
    desc += f"%CSIZE%\n{size}\n\n"
    desc += f"%ISIZE%\n{info.get('size', ['0'])[0]}\n\n"
    desc += f"%SHA256SUM%\n{sha}\n\n"
    desc += f"%ARCH%\n{info.get('arch', ['x86_64'])[0]}\n\n"
    desc += f"%BUILDDATE%\n{info.get('builddate', [str(int(time.time()))])[0]}\n\n"
    desc += f"%PACKAGER%\n{info.get('packager', ['RixelDev'])[0]}\n\n"
    desc += block("URL", info.get("url", []))
    desc += block("LICENSE", info.get("license", []))
    desc += block("DEPENDS", info.get("depend", []))
    desc += block("OPTDEPENDS", info.get("optdepend", []))
    desc += block("PROVIDES", info.get("provides", []))
    desc += block("CONFLICTS", info.get("conflicts", []))
    desc += block("REPLACES", info.get("replaces", []))
    return desc


def add_tar_member(archive: tarfile.TarFile, name: str, data: bytes) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = int(time.time())
    info.mode = 0o644
    archive.addfile(info, io.BytesIO(data))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--packages", required=True, help="Directory containing .pkg.tar.zst")
    parser.add_argument("--output", required=True, help="Repository output directory")
    parser.add_argument("--repo", default="rixelpos")
    parser.add_argument("--arch", default="x86_64")
    parser.add_argument("--server", default="https://rixeldev.github.io/rixelpos/pacman/$arch")
    args = parser.parse_args()

    packages_dir = Path(args.packages)
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)

    packages = sorted(packages_dir.glob("*.pkg.tar.zst"))
    if not packages:
        print(f"No packages found in {packages_dir}")
        return 1

    db_entries: list[tuple[str, bytes]] = []
    files_entries: list[tuple[str, bytes]] = []

    for package in packages:
        info = read_pkginfo(package)
        name = info.get("pkgname", [package.name.split("-")[0]])[0]
        version = info.get("pkgver", ["0-1"])[0]
        if "-" not in version:
            version = f"{version}-{info.get('pkgrel', ['1'])[0]}"
        entry_name = f"{name}-{version}"
        sha = hashlib.sha256(package.read_bytes()).hexdigest()
        size = package.stat().st_size
        desc = desc_text(name, version, package.name, size, sha, info).encode()
        db_entries.append((f"{entry_name}/desc", desc))

        files = list_files(package)
        files_text = ("%FILES%\n" + "\n".join(files) + "\n\n") if files else "%FILES%\n\n"
        files_entries.append((f"{entry_name}/files", files_text.encode()))

        target = output / package.name
        if target.resolve() != package.resolve():
            shutil.copy2(package, target)

    for archive_name, entries in ((f"{args.repo}.db.tar.gz", db_entries),
                                  (f"{args.repo}.files.tar.gz", files_entries)):
        archive_path = output / archive_name
        with tarfile.open(archive_path, "w:gz") as archive:
            for name, data in entries:
                directory = name.split("/", 1)[0] + "/"
                if not any(m.name == directory for m in archive.getmembers()):
                    dir_info = tarfile.TarInfo(directory)
                    dir_info.type = tarfile.DIRTYPE
                    dir_info.mode = 0o755
                    dir_info.mtime = int(time.time())
                    archive.addfile(dir_info)
                add_tar_member(archive, name, data)
        # pacman accepts either .db or .db.tar.gz; copy for simple static hosting.
        shutil.copy2(archive_path, output / archive_name.replace(".tar.gz", ""))

    server = args.server.replace("$arch", args.arch)
    (output / "pacman.conf").write_text(
        "[rixelpos]\nSigLevel = Optional TrustAll\nServer = " + server + "\n", encoding="utf-8")
    (output / "README.txt").write_text(
        "RixelPOS pacman repository.\n\n"
        "Add to /etc/pacman.conf:\n\n"
        "[rixelpos]\nSigLevel = Optional TrustAll\nServer = " + server + "\n\n"
        "Then: sudo pacman -Syu rixelpos\n", encoding="utf-8")

    print(f"Repository built in {output}")
    for package in packages:
        print(f"  package: {package.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
