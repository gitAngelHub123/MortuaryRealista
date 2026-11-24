#!/bin/bash
# © Angel — Mide FPS del juego con distintas combinaciones de funciones del mod (misma vista de inicio)
# Uso: biseccion.sh "nombre" "Seccion.Clave=valor" ...
G="$HOME/.local/share/Steam/steamapps/common/The Mortuary Assistant"
C="$G/BepInEx/config/angel.mortuary.terrorcine.cfg"
nombre="$1"; shift
# base: todo apagado
python3 -I - "$C" "$@" <<'E'
import sys, re
p = sys.argv[1]; cambios = {}
for a in sys.argv[2:]:
    k, v = a.split("=", 1); sec, clave = k.split("."); cambios[(sec, clave)] = v
base = {("General","Activo"):"false", ("DLSS","Activar"):"false", ("Realismo","Activar"):"false",
        ("TexturasIA","Activar"):"false", ("Calidad","SesgoLOD"):"-1", ("Pruebas","PruebaRendimiento"):"false",
        ("Pruebas","SecuenciaAB"):"false", ("Pruebas","CapturaAutoSegundos"):"30"}
base.update(cambios)
s = open(p).read(); sec = None; out = []
for l in s.split("\n"):
    m = re.match(r"^\[(.+)\]$", l)
    if m: sec = m.group(1)
    if " = " in l:
        k = l.split(" = ")[0]
        if (sec, k) in base: l = k + " = " + base[(sec, k)]
    out.append(l)
open(p, "w").write("\n".join(out))
E
ps -eo pid,args | awk '/The Mortuary Assistant.exe *$/ && !/awk|bash/ {print $1}' | xargs -r kill
sleep 6
setsid steam steam://rungameid/1295920 >/dev/null 2>&1 &
T=$(date +%FT%T)
timeout 280 bash -c "until [ \"\$(find '$G/BepInEx/LogOutput.log' -newermt '$T')\" ] && grep -q 'Captura guardada.*auto_Mortuary' '$G/BepInEx/LogOutput.log'; do sleep 4; done"
# 3 lecturas de FPS después de la captura
timeout 90 bash -c "until [ \$(sed -n '/Captura guardada.*auto_Mortuary/,\$p' '$G/BepInEx/LogOutput.log' | grep -c 'FPS: medio') -ge 7 ]; do sleep 3; done"
fps=$(sed -n '/Captura guardada.*auto_Mortuary/,$p' "$G/BepInEx/LogOutput.log" | grep 'FPS: medio' | head -7 | sed -E 's/.*medio ([0-9.]+).*/\1/' | tr '\n' ' ')
vram=$(nvidia-smi --query-compute-apps=name,used_memory --format=csv,noheader | grep -i mortuary | awk -F', ' '{print $2}')
otros=$(nvidia-smi --query-compute-apps=name,used_memory --format=csv,noheader | grep -ic witch)
echo "$nombre | FPS: $fps | VRAM juego: $vram | instancias Witch Hunt: $otros"
