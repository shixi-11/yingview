"""Render the application's vector-like window mark at Windows icon sizes."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
target = root / "src" / "YingView" / "Assets"
target.mkdir(parents=True, exist_ok=True)
im = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((36, 36, 988, 988), radius=210, fill="#172524")
d.rounded_rectangle((211, 225, 813, 799), radius=70, outline="#A5E3D1", width=42)
d.line((235, 684, 424, 479, 545, 602, 673, 444, 790, 570), fill="#A5E3D1", width=48, joint="curve")
d.ellipse((604, 327, 682, 405), fill="#A5E3D1")
im.save(target / "yingview.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
