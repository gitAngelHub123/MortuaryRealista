#!/bin/bash
# Cierra el juego, instala el DLL compilado, abre el juego y carga la escena pedida (sin tocar partidas). Uso: lanzar_escena.sh Mortuary_Tutorial [espera]
G="$HOME/.local/share/Steam/steamapps/common/The Mortuary Assistant"; L="$G/BepInEx/LogOutput.log"
ps -eo pid,args | awk '/The Mortuary Assistant.exe *$/ && !/awk|bash/ {print $1}' | xargs -r kill
for i in $(seq 1 25); do ps -eo args | grep -q "[T]he Mortuary Assistant.exe *$" || break; sleep 1; done
sleep 6
cp "$HOME/Proyectos/MortuaryRealista/bin/Release/net6.0/MortuaryRealista.dll" "$G/BepInEx/plugins/MortuaryRealista/"
rm -f "$G/Capturas/comandos.txt"
(steam steam://rungameid/1295920 >/dev/null 2>&1 &)
sleep 15; for i in $(seq 1 120); do grep -q "Escena: MainMenu" "$L" 2>/dev/null && break; sleep 1; done; sleep 8
printf 'escena %s\n' "$1" > "$G/Capturas/comandos.txt"
for i in $(seq 1 120); do grep -q "Escena: $1" "$L" && break; sleep 1; done
sleep "${2:-25}"; echo "en $1"
