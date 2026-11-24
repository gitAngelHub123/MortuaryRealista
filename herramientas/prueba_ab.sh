#!/bin/bash
# © Angel — Prueba A/B dentro de la morgue: entra solo con CONTINUAR, enciende luces, lleva al jugador a cada vista
# y saca fotos con/sin. Restaura la configuración y las partidas al terminar.
# Uso: prueba_ab.sh "nombre" "Seccion.Clave=valor" ...
G="$HOME/.local/share/Steam/steamapps/common/The Mortuary Assistant"
C="$G/BepInEx/config/angel.mortuary.terrorcine.cfg"
SAVE="$HOME/.local/share/Steam/steamapps/compatdata/1295920/pfx/drive_c/users/steamuser/AppData/LocalLow/DarkStone Digital/The Mortuary Assistant"
RESP="$HOME/Proyectos/MortuaryRealista/respaldo_partidas/20261123_antes_pruebas"
SAL="$HOME/Proyectos/MortuaryRealista/trabajo/ab"
nombre="$1"; shift
cp "$C" /tmp/claude-cfg-ab.bak 2>/dev/null || true
cp "$C" "$SAL/../cfg_antes_ab.cfg"
python3 -I - "$C" "$@" <<'E'
import sys, re
p = sys.argv[1]; cambios = {}
for a in sys.argv[2:]:
    k, v = a.split("=", 1); sec, clave = k.split("."); cambios[(sec, clave)] = v
base = {("Pruebas","BotonAuto"):"CONTINUAR", ("Pruebas","CapturaAutoSegundos"):"30", ("Pruebas","EscenaCaptura"):"Mortuary",
        ("Pruebas","SecuenciaAB"):"true", ("Pruebas","EncenderLuces"):"true", ("Pruebas","MoverJugador"):"true",
        ("Pruebas","PruebaRendimiento"):"false"}
base.update(cambios)
lineas = open(p).read().split("\n"); sec = None; hechos = set()
for i, l in enumerate(lineas):
    m = re.match(r"^\[(.+)\]$", l)
    if m: sec = m.group(1)
    if " = " in l and not l.startswith("#"):
        k = l.split(" = ")[0]
        if (sec, k) in base: lineas[i] = k + " = " + base[(sec, k)]; hechos.add((sec, k))
for (sec, k), v in base.items():
    if (sec, k) in hechos: continue
    idx = next((i for i, l in enumerate(lineas) if l == "[" + sec + "]"), None)
    if idx is None: lineas += ["", "[" + sec + "]", k + " = " + v]
    else: lineas.insert(idx + 1, k + " = " + v)
open(p, "w").write("\n".join(lineas))
E
ps -eo pid,args | awk '/The Mortuary Assistant.exe *$/ && !/awk|bash/ {print $1}' | xargs -r kill
sleep 6
T=$(date +%FT%T)
setsid steam steam://rungameid/1295920 >/dev/null 2>&1 &
timeout 400 bash -c "until [ \"\$(find '$G/BepInEx/LogOutput.log' -newermt '$T')\" ] && grep -q 'secuencia A/B terminada' '$G/BepInEx/LogOutput.log'; do sleep 4; done"
estado=$?
sleep 25   # FPS con todo puesto
grep -E "FPS: medio" "$G/BepInEx/LogOutput.log" | tail -3
ps -eo pid,args | awk '/The Mortuary Assistant.exe *$/ && !/awk|bash/ {print $1}' | xargs -r kill
sleep 5
# restaurar configuración (solo la sección de pruebas vuelve a su estado) y partidas
cp "$SAL/../cfg_antes_ab.cfg" "$C"
cp -a "$RESP"/. "$SAVE"/
mkdir -p "$SAL/$nombre"
find "$G/Capturas" -newermt "$T" -name "*.png" -exec mv {} "$SAL/$nombre/" \;
cp "$G/BepInEx/LogOutput.log" "$SAL/$nombre/LogOutput.log"
echo "$nombre: estado=$estado capturas=$(ls "$SAL/$nombre"/*.png 2>/dev/null | wc -l)"
