#!/bin/bash
# Abre el juego, BORRA LOS DATOS desde el menú (respaldar antes las partidas), empieza NUEVO TURNO y congela el tiempo
# en la llegada en auto del tutorial. Uso: cinematica_inicio.sh [segundos tras cargar el tutorial, por defecto 20]
G="$HOME/.local/share/Steam/steamapps/common/The Mortuary Assistant"; L="$G/BepInEx/LogOutput.log"
ps -eo pid,args | awk '/The Mortuary Assistant.exe *$/ && !/awk|bash/ {print $1}' | xargs -r kill
for i in $(seq 1 25); do ps -eo args | grep -q "[T]he Mortuary Assistant.exe *$" || break; sleep 1; done; sleep 6
cp "$HOME/Proyectos/MortuaryRealista/bin/Release/net6.0/MortuaryRealista.dll" "$G/BepInEx/plugins/MortuaryRealista/"; rm -f "$G/Capturas/comandos.txt"
(steam steam://rungameid/1295920 >/dev/null 2>&1 &); sleep 15; for i in $(seq 1 120); do grep -q "Escena: MainMenu" "$L" 2>/dev/null && break; sleep 1; done
for k in $(seq 1 20); do sleep 5; n0=$(wc -l < "$L"); printf 'botones\n' > "$G/Capturas/comandos.txt"; sleep 2; tail -n +$n0 "$L" | grep -q "NUEVO TURNO" && break; done
n0=$(wc -l < "$L"); printf 'botones\n' > "$G/Capturas/comandos.txt"; sleep 3
tail -n +$n0 "$L" | grep -q "CONTINUAR" && { printf 'boton BORRAR DATOS\nesperar 2\nboton #ok\nesperar 2\n' > "$G/Capturas/comandos.txt"; sleep 7; echo "datos borrados"; }
printf 'boton NUEVO TURNO\n' > "$G/Capturas/comandos.txt"
for i in $(seq 1 240); do grep -q "Escena: Mortuary_Tutorial" "$L" && break; sleep 1; done; sleep "${1:-20}"
printf 'tiempo 0\n' > "$G/Capturas/comandos.txt"; sleep 2; echo "congelado en la cinemática"
