# Operator arm skin upgrade: from each arm atlas (glTF baseColor + metallicRoughness) builds an HDRP albedo with natural
# skin tone and mottling, a tangent-space normal map (pores, fine creases, relief from the painted texture) and an
# HDRP mask map (R metallic, G AO, B detail mask, A smoothness) with skin-like smoothness. Only skin pixels change colour.
import sys, numpy as np
from PIL import Image, ImageFilter
rng = np.random.default_rng(1234)
OUT = 'Assets/GameAssets/OperatorSkin'
JOBS = {
 'Soph_Arms':   ('Assets/GameAssets/Soph/Soph_AKMN_Plasma_Textures/27_texture_0.png', 'Assets/GameAssets/Soph/Soph_AKMN_Plasma_Textures/28_texture_0_metallic_roughness.png'),
 'Eli_ArmL':    ('Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore_Textures/05_texture_0.png', 'Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore_Textures/06_texture_0_metallic_roughness.png'),
 'Eli_ArmR':    ('Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore_Textures/07_texture_0.png', 'Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore_Textures/08_texture_0_metallic_roughness.png'),
 'Tactical_A':  ('Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack_Textures/00_texture_0.png', 'Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack_Textures/01_texture_0_metallic_roughness.png'),
 'Tactical_B':  ('Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack_Textures/06_texture_0.png', 'Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack_Textures/07_texture_0_metallic_roughness.png'),
}
def noise(n, cells, octaves=1, persist=.5):
    acc = np.zeros((n, n), np.float32); amp = 1; tot = 0
    for o in range(octaves):
        c = max(2, int(cells * 2 ** o))
        g = Image.fromarray((rng.random((c, c)) * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)
        acc += (np.asarray(g, np.float32) / 255 - .5) * amp; tot += amp; amp *= persist
    return acc / tot * 2   # about -1..1
def blur(a, r): 
    from scipy.ndimage import gaussian_filter; return gaussian_filter(a.astype(np.float32), r)

for name, (base, mr) in JOBS.items():
    rgb = np.asarray(Image.open(base).convert('RGB'), np.float32) / 255; n = rgb.shape[0]
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); sat = (mx - mn) / np.maximum(mx, 1e-4)
    skin = ((R > .38) & (R >= G) & (G >= B * .92) & (R - B > .10) & (sat > .14) & (sat < .62)).astype(np.float32)
    skin = np.clip(blur(skin, 1.5) * 1.4 - .2, 0, 1)
    # Albedo: less salmon, a touch darker, warm/red mottling, faint freckles and pore specks.
    tone = np.array([.92, .97, .93], np.float32) * .93
    mott = noise(n, 24, 4); fine = noise(n, 180, 2); red = noise(n, 10, 3)
    pores = (rng.random((n, n)) > .988).astype(np.float32); pores = blur(pores, .8); pores /= max(pores.max(), 1e-4)
    freck = np.clip((noise(n, 300, 1) - .62) * 4, 0, 1) * np.clip(noise(n, 8, 2) + .3, 0, 1)
    sk = rgb * tone
    sk[..., 0] *= 1 + .07 * red + .025 * mott
    sk[..., 1] *= 1 - .02 * red + .02 * mott
    sk[..., 2] *= 1 - .03 * red + .02 * mott
    lum = 1 + .035 * fine - .10 * pores - .12 * freck
    sk *= lum[..., None]
    alb = rgb * (1 - skin[..., None]) + np.clip(sk, 0, 1) * skin[..., None]
    Image.fromarray((np.clip(alb, 0, 1) * 255 + .5).astype(np.uint8)).save(f'{OUT}/{name}_Albedo.png')
    # Height: relief of the painted texture (high-pass, clamped so island borders do not turn into ridges) everywhere,
    # plus pores, cross-hatched fine creases and soft bumps on the skin.
    l = rgb @ np.array([.3, .59, .11], np.float32)
    hp = np.clip(l - blur(l, 4), -.06, .06)
    edge = blur((mx < .04).astype(np.float32), 3)      # background around the islands
    hp *= np.clip(1 - edge * 3, 0, 1)
    sk_edge = np.abs(skin - blur(skin, 3)); hp *= np.clip(1 - sk_edge * 6, 0, 1)   # no ridge where skin meets cloth
    crease = 1 - np.abs(noise(n, 220, 2)); crease = crease ** 6
    h = hp * 1.2 + skin * (-.10 * pores + .025 * crease + .015 * noise(n, 90, 3) - .25 * freck * 0)
    gx = np.roll(h, -1, 1) - np.roll(h, 1, 1); gy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    strength = 3.0
    nx, ny = -gx * strength, gy * strength           # image rows go down, texture V goes up (OpenGL-style +Y)
    nz = np.ones_like(nx); ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.stack([nx / ln, ny / ln, nz / ln], -1) * .5 + .5
    Image.fromarray((nrm * 255 + .5).astype(np.uint8)).save(f'{OUT}/{name}_Normal.png')
    # Mask map: metallic from glTF B, AO 1 (slightly darker in creases), detail mask 0, smoothness from 1-roughness,
    # skin at about .45 with variation (oilier on top, drier in pores).
    m = np.asarray(Image.open(mr).convert('RGB'), np.float32) / 255
    if m.shape[0] != n: m = np.asarray(Image.open(mr).convert('RGB').resize((n, n)), np.float32) / 255
    smooth = 1 - m[..., 1]
    sks = np.clip(smooth - .07 + .02 * mott - .05 * pores, .2, .9)   # just a little rougher than the original
    A = smooth * (1 - skin) + sks * skin
    ao = np.clip(1 - skin * (.35 * crease + .25 * pores), .6, 1)
    mask = np.stack([m[..., 2] * (1 - skin), ao, np.zeros_like(ao), A], -1)
    Image.fromarray((np.clip(mask, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(f'{OUT}/{name}_Mask.png')
    print(name, 'skin %.1f%%' % (skin.mean() * 100), 'orig skin smooth %.2f' % (smooth[skin > .5].mean() if (skin > .5).any() else -1))
