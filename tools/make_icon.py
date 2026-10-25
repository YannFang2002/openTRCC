"""Generates the OpenTRCC app icon (PNG + multi-size ICO).

Design: a dark rounded panel holding a small screen with a glowing teal
frame-rate trace. Kept simple so it stays readable at 16 px in the tray.
"""
from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
SS = 2  # supersampling factor


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def gradient(size, top, bottom):
    image = Image.new("RGB", (size, size))
    draw = ImageDraw.Draw(image)
    for y in range(size):
        t = y / (size - 1)
        color = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        draw.line([(0, y), (size, y)], fill=color)
    return image


def main():
    size = SIZE * SS
    radius = int(size * 0.22)

    # Panel background.
    panel = gradient(size, (11, 18, 32), (17, 28, 46)).convert("RGBA")
    panel.putalpha(rounded_mask(size, radius))

    # Screen recess.
    margin_x = int(size * 0.16)
    margin_y = int(size * 0.20)
    screen_box = [margin_x, margin_y, size - margin_x, size - margin_y]
    screen_radius = int(size * 0.09)

    screen = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(screen)
    draw.rounded_rectangle(screen_box, radius=screen_radius, fill=(4, 9, 16, 255))
    panel.alpha_composite(screen)

    # Screen bezel with a soft glow.
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    gdraw.rounded_rectangle(
        screen_box, radius=screen_radius, outline=(45, 212, 191, 255), width=int(size * 0.018))
    glow = glow.filter(ImageFilter.GaussianBlur(int(size * 0.012)))
    panel.alpha_composite(glow)

    bezel = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bdraw = ImageDraw.Draw(bezel)
    bdraw.rounded_rectangle(
        screen_box, radius=screen_radius, outline=(148, 245, 226, 255), width=int(size * 0.011))
    panel.alpha_composite(bezel)

    # Frame-time trace inside the screen.
    left = margin_x + int(size * 0.06)
    right = size - margin_x - int(size * 0.06)
    top = margin_y + int(size * 0.055)
    bottom = size - margin_y - int(size * 0.055)

    points = [
        (left, bottom - (bottom - top) * 0.22),
        (left + (right - left) * 0.14, bottom - (bottom - top) * 0.38),
        (left + (right - left) * 0.28, bottom - (bottom - top) * 0.28),
        (left + (right - left) * 0.42, bottom - (bottom - top) * 0.62),
        (left + (right - left) * 0.56, bottom - (bottom - top) * 0.50),
        (left + (right - left) * 0.70, bottom - (bottom - top) * 0.80),
        (right, bottom - (bottom - top) * 0.86),
    ]

    trace = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    tdraw = ImageDraw.Draw(trace)
    width = int(size * 0.020)
    tdraw.line(points, fill=(45, 212, 191, 255), width=width, joint="curve")
    for point in points:
        r = width * 0.9
        tdraw.ellipse([point[0] - r, point[1] - r, point[0] + r, point[1] + r], fill=(45, 212, 191, 255))

    trace_glow = trace.filter(ImageFilter.GaussianBlur(int(size * 0.020)))
    panel.alpha_composite(trace_glow)
    panel.alpha_composite(trace)

    # Endpoint highlight.
    end = points[-1]
    dot = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ddraw = ImageDraw.Draw(dot)
    r = int(size * 0.030)
    ddraw.ellipse([end[0] - r, end[1] - r, end[0] + r, end[1] + r], fill=(190, 255, 245, 255))
    dot_glow = dot.filter(ImageFilter.GaussianBlur(int(size * 0.022)))
    panel.alpha_composite(dot_glow)
    panel.alpha_composite(dot)

    # Downscale to the final icon.
    icon = panel.resize((SIZE, SIZE), Image.LANCZOS)
    icon.save("assets/icon/opentrcc.png")

    sizes = [16, 24, 32, 48, 64, 128, 256]
    icon.save("assets/icon/opentrcc.ico", sizes=[(s, s) for s in sizes])
    print("wrote assets/icon/opentrcc.png and opentrcc.ico")


if __name__ == "__main__":
    main()
