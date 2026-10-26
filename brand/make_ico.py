"""Packs rendered PNGs into a Windows .ico: 32-bit BMP frames up to 128 px, a PNG frame for 256 px.

usage: python make_ico.py <png-dir> <out.ico>
The 16–48 px frames come from the simplified drawing (bobinkumanda-small-N.png), the larger ones from the full mark.
"""
import io
import struct
import sys
from pathlib import Path

from PIL import Image

SMALL = (16, 20, 24, 32, 40, 48)
LARGE = (64, 96, 128, 256)


def bmp_frame(im: Image.Image) -> bytes:
    w, h = im.size
    px = im.convert("RGBA").tobytes("raw", "BGRA")
    rows = [px[y * w * 4:(y + 1) * w * 4] for y in range(h)]
    xor = b"".join(reversed(rows))  # DIB rows run bottom-up
    mask_row = ((w + 31) // 32) * 4
    and_mask = bytes(mask_row * h)  # alpha carries transparency; the mask stays empty
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(xor) + len(and_mask), 0, 0, 0, 0)
    return header + xor + and_mask


def main(src: Path, out: Path) -> None:
    frames = []
    for s in SMALL + LARGE:
        name = f"bobinkumanda-small-{s}.png" if s in SMALL else f"bobinkumanda-{s}.png"
        im = Image.open(src / name).convert("RGBA")
        assert im.size == (s, s), (name, im.size)
        if s == 256:
            buf = io.BytesIO()
            im.save(buf, "png", optimize=True)
            frames.append((s, buf.getvalue()))
        else:
            frames.append((s, bmp_frame(im)))
    offset = 6 + 16 * len(frames)
    head = struct.pack("<HHH", 0, 1, len(frames))
    entries, blobs = b"", b""
    for s, data in frames:
        d = 0 if s == 256 else s
        entries += struct.pack("<BBBBHHII", d, d, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs += data
    out.write_bytes(head + entries + blobs)
    print(out, out.stat().st_size, "bayt,", len(frames), "boyut")


if __name__ == "__main__":
    main(Path(sys.argv[1]), Path(sys.argv[2]))
