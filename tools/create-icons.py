from pathlib import Path
from PIL import Image, ImageDraw

target = Path(__file__).resolve().parents[1] / 'src' / 'ReMinder.App' / 'Assets'
target.mkdir(parents=True, exist_ok=True)
im = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((8, 8, 248, 248), radius=64, fill='#6750A4')
d.ellipse((57, 57, 199, 199), outline='#F3E9FF', width=13)
d.line((128, 91, 128, 130, 157, 148), fill='#F3E9FF', width=13)
d.ellipse((121, 84, 135, 98), fill='#F3E9FF')
d.ellipse((150, 141, 164, 155), fill='#F3E9FF')
im.save(target / 're-minder.png')
im.save(target / 're-minder.ico', sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print(target)
