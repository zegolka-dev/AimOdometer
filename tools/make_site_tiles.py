"""Cuts site/assets/shots/*.webp (demo data, no personal apps) into tiles for the landing page's marquee and cards.

Run after new screenshots: python tools/make_site_tiles.py. Tiles are 840 x 540 (shown at 420 x 270, sharp on 2x
screens); card images are cut to their own shapes.
"""
import os

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "site", "assets")
SHOTS = os.path.join(ROOT, "shots")
TILES = os.path.join(ROOT, "tiles")

# (screenshot, name, x, y, width) in the 1180 x 760 screenshot; height follows the tile shape (420:270).
MARQUEE = [
    ("overview", "overview-today", 270, 130, 600),
    ("games", "games-share", 268, 160, 600),
    ("stats", "stats-days", 280, 290, 860),
    ("achievements", "achievements-grid", 270, 150, 600),
    ("map", "map-route", 276, 330, 650),
    ("overview", "overview-days", 275, 480, 430),
    ("stats", "stats-totals", 280, 150, 560),
    ("games", "games-list", 548, 170, 600),
    ("achievements", "achievements-progress", 270, 300, 520),
    ("map", "map-top", 270, 60, 600),
    ("overview", "overview-totals", 275, 330, 600),
]

# (screenshot, name, x, y, width, height) for the stacking cards.
CARDS = [
    ("overview", "card-overview", 250, 60, 930, 700),
    ("stats", "card-heatmap", 270, 560, 900, 200),
    ("games", "card-games", 548, 160, 610, 260),
    ("achievements", "card-achievements", 250, 60, 930, 700),
    ("map", "card-map", 250, 60, 930, 700),
    ("map", "card-route", 276, 330, 880, 420),
    ("stats", "card-days", 270, 280, 900, 300),
]


def save(image, path, size):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.resize(size, Image.LANCZOS).save(path, "WEBP", quality=82, method=6)


def main():
    for lang in ("en", "ru"):
        shots = {}
        for shot, name, x, y, width in MARQUEE:
            image = shots.setdefault(shot, Image.open(os.path.join(SHOTS, f"{shot}-{lang}.webp")).convert("RGB"))
            height = round(width * 270 / 420)
            x = min(x, image.width - width)
            y = min(y, image.height - height)
            save(image.crop((x, y, x + width, y + height)), os.path.join(TILES, f"{name}-{lang}.webp"), (840, 540))
        for shot, name, x, y, width, height in CARDS:
            image = shots.setdefault(shot, Image.open(os.path.join(SHOTS, f"{shot}-{lang}.webp")).convert("RGB"))
            crop = image.crop((x, y, min(x + width, image.width), min(y + height, image.height)))
            scale = min(1.0, 1200 / crop.width)
            save(crop, os.path.join(TILES, f"{name}-{lang}.webp"), (round(crop.width * scale), round(crop.height * scale)))
    print("tiles written")


if __name__ == "__main__":
    main()
