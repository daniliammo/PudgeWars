"""Bake the engine-start placeholder using Pillow; no Unity/game font assets are loaded at runtime.

Run: python3 Tools/Startup/build_splash.py
Keep its layout aligned with StartupLoadingScreen.cs. The native splash has no measurable progress.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
FONT = ROOT / 'Assets/TextMesh Pro/Fonts/LiberationSans.ttf'
OUTPUT = ROOT / 'Assets/Art/UI/Startup/EngineStartup.png'
SIZE = (1920, 1080)


def main():
    image = Image.new('RGB', SIZE, (6, 8, 10))
    draw = ImageDraw.Draw(image)
    # The Unity logo is drawn by the engine in the empty centre of the image.
    draw.text((384, 734), 'Подготовка игры', font=ImageFont.truetype(str(FONT), 36),
              fill=(230, 219, 184), anchor='lm')
    draw.text((384, 810), 'Запуск движка Unity', font=ImageFont.truetype(str(FONT), 24),
              fill=(179, 186, 199), anchor='lm')
    draw.rectangle((384, 875, 1421, 896), fill=(31, 36, 46))
    # An empty track with no percentage avoids inventing engine progress.
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUTPUT, optimize=True)
    print(f'{OUTPUT.relative_to(ROOT)}: {OUTPUT.stat().st_size} bytes')


if __name__ == '__main__':
    main()
