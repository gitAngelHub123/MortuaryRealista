# © Angel — Escalado IA de texturas con PyTorch + spandrel (por mosaicos, con relleno envolvente)
import math
import numpy as np
import torch
from spandrel import ModelLoader
from safetensors.torch import load_file


class Escalador:
    def __init__(self, ruta_modelo, mosaico=256, solape=16):
        if ruta_modelo.endswith(".safetensors"):
            sd = load_file(ruta_modelo)
        else:
            # Solo pesos: torch no ejecuta código del archivo
            sd = torch.load(ruta_modelo, map_location="cpu", weights_only=True)
        self.desc = ModelLoader().load_from_state_dict(sd)
        self.escala = self.desc.scale
        self.medio = bool(self.desc.supports_half)
        m = self.desc.model.eval().cuda()
        self.modelo = m.half() if self.medio else m
        req = self.desc.size_requirements
        self.multiplo = max(1, getattr(req, "multiple_of", 1) or 1)
        self.minimo = max(1, getattr(req, "minimum", 1) or 1)
        self.mosaico = mosaico
        self.solape = solape

    @torch.inference_mode()
    def _parche(self, t):
        # t: 1x3xhxw float en GPU; rellena hasta el múltiplo que pide el modelo
        _, _, h, w = t.shape
        H = max(self.minimo, int(math.ceil(h / self.multiplo) * self.multiplo))
        W = max(self.minimo, int(math.ceil(w / self.multiplo) * self.multiplo))
        if H != h or W != w:
            t = torch.nn.functional.pad(t, (0, W - w, 0, H - h), mode="replicate")
        r = self.modelo(t.half() if self.medio else t).float()
        return r[:, :, : h * self.escala, : w * self.escala]

    @torch.inference_mode()
    def escalar_rgb(self, rgb, envolvente=True, relleno=16, reducir=1):
        """rgb: HxWx3 uint8 -> (H*e)x(W*e)x3 uint8, con e = escala/reducir.
        reducir=2 promedia 2x2 en la GPU (supermuestreo) para obtener 2x sin gastar RAM."""
        h0, w0 = rgb.shape[:2]
        p = relleno if envolvente else 0
        if p:
            rgb = np.pad(rgb, ((p, p), (p, p), (0, 0)), mode="wrap")
        x = torch.from_numpy(np.ascontiguousarray(rgb)).permute(2, 0, 1).float().div_(255.0).unsqueeze(0)
        _, _, H, W = x.shape
        e, T, S = self.escala // reducir, self.mosaico, self.solape
        salida = torch.empty((3, H * e, W * e), dtype=torch.uint8)
        for y0 in range(0, H, T):
            for x0 in range(0, W, T):
                y1, x1 = min(y0 + T, H), min(x0 + T, W)
                ys, xs = max(y0 - S, 0), max(x0 - S, 0)
                ye, xe = min(y1 + S, H), min(x1 + S, W)
                r = self._parche(x[:, :, ys:ye, xs:xe].cuda())
                if reducir > 1:
                    r = torch.nn.functional.avg_pool2d(r, reducir)
                oy, ox = (y0 - ys) * e, (x0 - xs) * e
                bloque = r[0, :, oy:oy + (y1 - y0) * e, ox:ox + (x1 - x0) * e]
                salida[:, y0 * e:y1 * e, x0 * e:x1 * e] = bloque.clamp_(0, 1).mul_(255).round_().byte().cpu()
        out = salida.permute(1, 2, 0).numpy()
        if p:
            out = out[p * e: p * e + h0 * e, p * e: p * e + w0 * e]
        return np.ascontiguousarray(out)
