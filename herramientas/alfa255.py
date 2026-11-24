# © Angel — Arregla el alfa 254 que deja el compresor BC7 (etcpak) en zonas opacas.
# Los materiales Ga_Skin_Body recortan con umbral 1,0: un alfa de 254/255 los "deshace en puntitos".
# En bloques BC7 de modo 6 con ambos extremos de alfa a 127 (7 bits), se ponen a 1 los bits P → alfa 255 exacto.
# Uso: alfa255.py archivo.mtex [...]   (solo .mtex de color, formato 1 = BC7)
import sys, struct, os
import numpy as np

def arreglar_bloques(datos: bytes):
    b = np.frombuffer(datos, dtype="<u8").reshape(-1, 2).copy()
    lo, hi = b[:, 0], b[:, 1]
    modo6 = (lo & np.uint64(0x7F)) == np.uint64(0x40)
    a0 = (lo >> np.uint64(49)) & np.uint64(0x7F)
    a1 = (lo >> np.uint64(56)) & np.uint64(0x7F)
    sel = modo6 & (a0 == 127) & (a1 == 127) & (((lo >> np.uint64(63)) & np.uint64(1) == 0) | (hi & np.uint64(1) == 0))
    lo[sel] |= np.uint64(1) << np.uint64(63)
    hi[sel] |= np.uint64(1)
    b[:, 0], b[:, 1] = lo, hi
    return b.tobytes(), int(sel.sum())

def arreglar_archivo(ruta):
    d = open(ruta, "rb").read()
    if d[:4] != b"MTX1":
        return 0
    _, W, H, fmt, n, L = struct.unpack("<6i", d[4:28])
    if fmt != 1:
        return 0
    nuevo, cambios = arreglar_bloques(d[28:28 + L])
    if cambios:
        tmp = ruta + ".tmp"
        with open(tmp, "wb") as f:
            f.write(d[:28]); f.write(nuevo); f.write(d[28 + L:])
        os.replace(tmp, ruta)
    return cambios

if __name__ == "__main__":
    total = 0
    for r in sys.argv[1:]:
        c = arreglar_archivo(r); total += c
        print(os.path.basename(r), c)
    print("bloques arreglados:", total)
